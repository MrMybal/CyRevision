#include <algorithm>
#include <array>
#include <cstdlib>
#include <cstring>
#include <cymcp/process.hpp>
#include "error_credentials.hpp"
#include <fstream>
#ifdef _WIN32
#include <windows.h>
#else
#include <fcntl.h>
#include <poll.h>
#include <pthread.h>
#include <signal.h>
#include <sys/wait.h>
#include <unistd.h>
extern char** environ;
#endif
namespace cymcp {
std::vector<std::string> process_error_secrets(const ProcessOptions& options) {
    std::vector<std::string> values;
    for (const auto& [key, value] : options.environment)
        if (!value.empty() && detail::credential_name(key))
            values.push_back(value);
    for (std::size_t i = 0; i < options.arguments.size(); ++i) {
        std::string_view arg = options.arguments[i];
        const auto separator = arg.find('=');
        if (!arg.starts_with('-') || !detail::credential_name(arg.substr(0, separator)))
            continue;
        if (separator != arg.npos && separator + 1 < arg.size())
            values.emplace_back(arg.substr(separator + 1));
        else if (separator == arg.npos && i + 1 < options.arguments.size())
            values.push_back(options.arguments[++i]);
    }
    return values;
}
namespace {
#ifdef _WIN32
std::wstring wide(const std::string& s) {
    if (s.empty())
        return {};
    int n = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, s.data(), static_cast<int>(s.size()),
                                nullptr, 0);
    if (!n)
        throw Error("InvalidEncoding", "Expected UTF-8");
    std::wstring out(n, 0);
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, s.data(), static_cast<int>(s.size()),
                        out.data(), n);
    return out;
}
std::wstring quote(const std::wstring& s) {
    std::wstring out = L"\"";
    std::size_t slashes = 0;
    for (wchar_t c : s) {
        if (c == L'\\') {
            ++slashes;
            continue;
        }
        if (c == L'\"') {
            out.append(slashes * 2 + 1, L'\\');
            out += c;
        } else {
            out.append(slashes, L'\\');
            out += c;
        }
        slashes = 0;
    }
    out.append(slashes * 2, L'\\');
    return out + L'\"';
}
struct Handle {
    HANDLE value = nullptr;
    ~Handle() {
        if (value && value != INVALID_HANDLE_VALUE)
            CloseHandle(value);
    }
    void reset(HANDLE h = nullptr) {
        if (value && value != INVALID_HANDLE_VALUE)
            CloseHandle(value);
        value = h;
    }
    HANDLE release() {
        auto h = value;
        value = nullptr;
        return h;
    }
};
#endif
bool bad_text(const std::string& s) {
    return s.find('\0') != std::string::npos;
}
} // namespace
PrivateFile::PrivateFile(const std::string& name, const std::string& content) {
    if (name.empty() || name.find_first_of("/\\:") != std::string::npos)
        throw Error("InvalidOptions", "Private file name must be a plain file name");
    directory_ = std::filesystem::temp_directory_path() / ("cymcp-" + random_token(16));
    private_directory(directory_); // owner-only ACL / 0700
    path_ = directory_ / std::filesystem::u8path(name);
    std::ofstream output(path_, std::ios::binary | std::ios::trunc);
    output.write(content.data(), static_cast<std::streamsize>(content.size()));
    output.flush();
    if (!output) {
        std::error_code ec;
        std::filesystem::remove_all(directory_, ec);
        throw Error("FileError", "Cannot write private configuration file");
    }
}
PrivateFile::~PrivateFile() {
    std::error_code ec;
    std::filesystem::remove_all(directory_, ec);
}
std::filesystem::path find_executable(const std::string& name) {
    if (name.empty() || bad_text(name))
        throw Error("ExecutableMissing", "Executable not configured");
    auto path = std::filesystem::path(std::u8string(name.begin(), name.end()));
    if (path.is_absolute() || path.has_parent_path()) {
        path = std::filesystem::absolute(path);
        if (!std::filesystem::is_regular_file(path))
            throw Error("ExecutableMissing", "Configured executable does not exist");
        return path;
    }
#ifdef _WIN32
    char* raw = nullptr;
    std::size_t length = 0;
    _dupenv_s(&raw, &length, "PATH");
    std::string paths = raw ? raw : "";
    std::free(raw);
#else
    const char* raw = std::getenv("PATH");
    std::string paths = raw ? raw : "";
#endif
#ifdef _WIN32
    const char separator = ';';
#else
    const char separator = ':';
#endif
    std::size_t at = 0;
    while (at <= paths.size()) {
        auto end = paths.find(separator, at);
        auto dir = paths.substr(at, end - at);
        if (!dir.empty()) {
            auto candidate = std::filesystem::path(std::u8string(dir.begin(), dir.end())) / path;
#ifdef _WIN32
            if (candidate.extension().empty())
                candidate += L".exe";
#endif
            if (std::filesystem::is_regular_file(candidate)) {
#ifndef _WIN32
                if (access(candidate.c_str(), X_OK) != 0) {
                    if (end == std::string::npos)
                        break;
                    at = end + 1;
                    continue;
                }
#endif
                return std::filesystem::absolute(candidate);
            }
        }
        if (end == std::string::npos)
            break;
        at = end + 1;
    }
    throw Error("ExecutableMissing", "Executable not found in PATH; configure its native path");
}
struct Process::Impl {
    ProcessOptions options;
    StdoutFormat stdout_format;
    std::mutex mutex, write_mutex, status_mutex;
    std::condition_variable wake, space;
    std::deque<ProcessLine> lines;
    std::size_t queued = 0;
    int closed_streams = 0;
    std::string fault;
    std::thread output, error;
    std::atomic_bool stopping{false};
    int exit_code = -1;
#ifdef _WIN32
    Handle process, job, input;
#else
    pid_t pid = -1;
    int input = -1;
    bool exited = false;
#endif
    Impl(ProcessOptions opts, StdoutFormat format)
        : options(std::move(opts)), stdout_format(format) {}
#ifndef _WIN32
    int peek_exit(siginfo_t& info) {
        int result;
        do {
            result = waitid(P_PID, static_cast<id_t>(pid), &info, WEXITED | WNOHANG | WNOWAIT);
        } while (result < 0 && errno == EINTR);
        if (result < 0 && errno == ECHILD)
            pid = -1; // A host-provided child reaper may already have released this PID.
        return result;
    }
#endif
    void stop() noexcept {
        stopping.store(true);
        space.notify_all();
#ifdef _WIN32
        if (job.value)
            TerminateJobObject(job.value, 130);
        else if (process.value)
            TerminateProcess(process.value, 130);
#else
        std::lock_guard lock(status_mutex);
        siginfo_t info{};
        if (pid > 0 && peek_exit(info) == 0)
            kill(-pid, SIGKILL);
#endif
        wake.notify_all();
    }
    void push(bool err, std::string line) {
        std::unique_lock lock(mutex);
        if (!fault.empty())
            return;
        const auto cost = line.size() + sizeof(ProcessLine);
        if (line.size() > options.max_line_bytes) {
            fault = "Process line exceeds configured limit";
            wake.notify_all();
            return;
        }
        // Backpressure: wait for the consumer (the pipe then throttles the child) instead of
        // failing a long or verbose agent turn. One oversized line is always admitted alone.
        // stop() sets an atomic without the mutex: poll so its wakeup cannot be missed.
        while (!(stopping.load() || !fault.empty() || queued == 0 ||
                 cost <= options.max_queue_bytes - std::min(queued, options.max_queue_bytes)))
            space.wait_for(lock, std::chrono::milliseconds(50));
        if (stopping.load() || !fault.empty())
            return;
        queued += cost;
        lines.push_back({err, std::move(line)});
        wake.notify_one();
    }
    template <typename F> void read_stream(bool err, F read_chunk) {
        std::array<char, 8192> buffer{};
        const bool binary = !err && stdout_format == StdoutFormat::Binary;
        const auto capacity =
            binary ? std::min(buffer.size(), options.max_line_bytes) : buffer.size();
        std::string partial;
        try {
            while (!stopping.load()) {
                auto n = read_chunk(buffer.data(), capacity);
                if (n <= 0)
                    break;
                if (binary) {
                    push(false, std::string(buffer.data(), static_cast<std::size_t>(n)));
                    continue;
                }
                for (std::size_t i = 0; i < static_cast<std::size_t>(n); ++i) {
                    if (buffer[i] == '\n') {
                        if (!partial.empty() && partial.back() == '\r')
                            partial.pop_back();
                        push(err, std::move(partial));
                        partial.clear();
                    } else
                        partial += buffer[i];
                    if (partial.size() > options.max_line_bytes) {
                        std::lock_guard lock(mutex);
                        fault = "Process line exceeds configured limit";
                        wake.notify_all();
                        stop();
                        break;
                    }
                }
                {
                    std::lock_guard lock(mutex);
                    if (!fault.empty()) {
                        stop();
                        break;
                    }
                }
            }
            if (!partial.empty() && !stopping.load())
                push(err, std::move(partial));
        } catch (...) {
            std::lock_guard lock(mutex);
            fault = "Process output reader failed";
        }
        {
            std::lock_guard lock(mutex);
            ++closed_streams;
        }
        wake.notify_all();
    }
    bool active() {
        std::lock_guard lock(status_mutex);
#ifdef _WIN32
        if (WaitForSingleObject(process.value, 0) == WAIT_TIMEOUT)
            return true;
        DWORD code = 0;
        if (!GetExitCodeProcess(process.value, &code))
            throw Error("ProcessError", "Cannot read process status");
        exit_code = static_cast<int>(code);
        return false;
#else
        if (exited)
            return false;
        // Keep the leader's PID reserved until destruction. Descendants may still own
        // pipes or be running after their parent exits, and the group ID must stay ours.
        siginfo_t info{};
        if (pid <= 0 || peek_exit(info) < 0)
            throw Error("ProcessError", "Cannot wait for process");
        if (info.si_pid == 0)
            return true;
        exited = true;
        exit_code = info.si_code == CLD_EXITED ? info.si_status : 128 + info.si_status;
        return false;
#endif
    }
    ~Impl() {
        stop();
#ifdef _WIN32
        if (process.value)
            WaitForSingleObject(process.value, 5000);
#else
        if (pid > 0) {
            int status = 0;
            while (waitpid(pid, &status, 0) < 0 && errno == EINTR) {
            }
        }
#endif
        if (output.joinable())
            output.join();
        if (error.joinable())
            error.join();
#ifndef _WIN32
        if (input >= 0)
            close(input);
#endif
    }
};
Process::Process(const ProcessOptions& options) : Process(options, StdoutFormat::Lines) {}
Process::Process(const ProcessOptions& options, StdoutFormat format)
    : impl_(std::make_unique<Impl>(options, format)) {
    auto& p = *impl_;
    if (!options.max_line_bytes || !options.max_queue_bytes)
        throw Error("InvalidOptions", "Process limits cannot be zero");
    auto executable = find_executable(path_utf8(options.executable));
    for (const auto& arg : options.arguments)
        if (bad_text(arg))
            throw Error("InvalidOptions", "NUL in process argument");
    for (const auto& [key, value] : options.environment)
        if (key.empty() || key.find('=') != std::string::npos || bad_text(key) || bad_text(value))
            throw Error("InvalidOptions", "Invalid environment entry");
#ifdef _WIN32
    auto ext = executable.extension().wstring();
    std::transform(ext.begin(), ext.end(), ext.begin(), ::towlower);
    if (ext != L".exe" && ext != L".com")
        throw Error("Unsupported", "Use a native executable, or node.exe with a script argument; "
                                   "shell shims are not executed");
    SECURITY_ATTRIBUTES security{sizeof(security), nullptr, TRUE};
    Handle child_in, child_out, child_err, parent_out, parent_err;
    HANDLE tmp = nullptr;
    if (!CreatePipe(&child_in.value, &tmp, &security, 0))
        throw Error("ProcessError", "Cannot create stdin pipe");
    p.input.reset(tmp);
    if (!CreatePipe(&parent_out.value, &child_out.value, &security, 0) ||
        !CreatePipe(&parent_err.value, &child_err.value, &security, 0))
        throw Error("ProcessError", "Cannot create output pipes");
    SetHandleInformation(p.input.value, HANDLE_FLAG_INHERIT, 0);
    SetHandleInformation(parent_out.value, HANDLE_FLAG_INHERIT, 0);
    SetHandleInformation(parent_err.value, HANDLE_FLAG_INHERIT, 0);
    SIZE_T bytes = 0;
    InitializeProcThreadAttributeList(nullptr, 1, 0, &bytes);
    std::vector<unsigned char> storage(bytes);
    auto attrs = reinterpret_cast<PPROC_THREAD_ATTRIBUTE_LIST>(storage.data());
    if (!InitializeProcThreadAttributeList(attrs, 1, 0, &bytes))
        throw Error("ProcessError", "Cannot configure process attributes");
    struct AttrCleanup {
        PPROC_THREAD_ATTRIBUTE_LIST value;
        ~AttrCleanup() {
            DeleteProcThreadAttributeList(value);
        }
    } cleanup{attrs};
    HANDLE inherited[] = {child_in.value, child_out.value, child_err.value};
    if (!UpdateProcThreadAttribute(attrs, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST, inherited,
                                   sizeof(inherited), nullptr, nullptr))
        throw Error("ProcessError", "Cannot restrict inherited handles");
    STARTUPINFOEXW startup{};
    startup.StartupInfo.cb = sizeof(startup);
    startup.lpAttributeList = attrs;
    startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
    startup.StartupInfo.hStdInput = child_in.value;
    startup.StartupInfo.hStdOutput = child_out.value;
    startup.StartupInfo.hStdError = child_err.value;
    std::wstring cmd = quote(executable.wstring());
    for (const auto& arg : options.arguments)
        cmd += L" " + quote(wide(arg));
    // CreateProcessW's hard limit; report it precisely instead of a generic launch failure.
    if (cmd.size() >= 32767)
        throw Error("CommandLineTooLong",
                    "Arguments exceed the Windows 32 767-character command line; pass long "
                    "content through stdin or a file");
    struct Less {
        bool operator()(const std::wstring& a, const std::wstring& b) const {
            return _wcsicmp(a.c_str(), b.c_str()) < 0;
        }
    };
    std::map<std::wstring, std::wstring, Less> env;
    auto native = GetEnvironmentStringsW();
    if (!native)
        throw Error("ProcessError", "Cannot read environment");
    for (auto at = native; *at; at += wcslen(at) + 1) {
        std::wstring entry = at;
        auto eq = entry.find(L'=', 1);
        if (eq != std::wstring::npos)
            env[entry.substr(0, eq)] = entry.substr(eq + 1);
    }
    FreeEnvironmentStringsW(native);
    for (const auto& [k, v] : options.environment)
        env[wide(k)] = wide(v);
    std::wstring envblock;
    for (const auto& [k, v] : env) {
        envblock += k + L"=" + v;
        envblock += L'\0';
    }
    envblock += L'\0';
    p.job.reset(CreateJobObjectW(nullptr, nullptr));
    if (!p.job.value)
        throw Error("ProcessError", "Cannot create process job");
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
    limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
    if (!SetInformationJobObject(p.job.value, JobObjectExtendedLimitInformation, &limits,
                                 sizeof(limits)))
        throw Error("ProcessError", "Cannot configure process job");
    PROCESS_INFORMATION info{};
    std::wstring cwd = options.cwd.wstring();
    if (!CreateProcessW(executable.c_str(), cmd.data(), nullptr, nullptr, TRUE,
                        CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW | CREATE_SUSPENDED |
                            EXTENDED_STARTUPINFO_PRESENT,
                        envblock.data(), cwd.empty() ? nullptr : cwd.c_str(), &startup.StartupInfo,
                        &info))
        throw Error("ProcessError", "Failed to launch executable");
    p.process.reset(info.hProcess);
    Handle initial_thread;
    initial_thread.reset(info.hThread);
    if (!AssignProcessToJobObject(p.job.value, p.process.value)) {
        TerminateProcess(p.process.value, 130);
        throw Error("ProcessError", "Cannot assign owned process job");
    }
    if (ResumeThread(initial_thread.value) == static_cast<DWORD>(-1))
        throw Error("ProcessError", "Cannot resume owned process");
    child_in.reset();
    child_out.reset();
    child_err.reset();
    auto out = parent_out.release();
    auto err = parent_err.release();
    p.output = std::thread([&p, out] {
        Handle h;
        h.reset(out);
        p.read_stream(false, [out](char* b, std::size_t size) {
            DWORD n = 0;
            if (!ReadFile(out, b, static_cast<DWORD>(size), &n, nullptr))
                return DWORD(0);
            return n;
        });
    });
    p.error = std::thread([&p, err] {
        Handle h;
        h.reset(err);
        p.read_stream(true, [err](char* b, std::size_t size) {
            DWORD n = 0;
            if (!ReadFile(err, b, static_cast<DWORD>(size), &n, nullptr))
                return DWORD(0);
            return n;
        });
    });
#else
    int in[2], out[2], err[2], exec_error[2];
    struct Pipes {
        std::vector<int> fds;
        ~Pipes() {
            for (int fd : fds)
                if (fd >= 0)
                    close(fd);
        }
        void release(int fd) {
            for (auto& x : fds)
                if (x == fd)
                    x = -1;
        }
    } pipes;
    auto make_pipe = [&](int* fds) {
        if (pipe(fds) != 0)
            throw Error("ProcessError", "Cannot create pipe");
        for (int index = 0; index < 2; ++index)
            if (fds[index] < 3) {
                auto moved = fcntl(fds[index], F_DUPFD_CLOEXEC, 3);
                if (moved < 0) {
                    close(fds[0]);
                    close(fds[1]);
                    throw Error("ProcessError", "Cannot reserve pipe descriptors");
                }
                close(fds[index]);
                fds[index] = moved;
            }
        pipes.fds.push_back(fds[0]);
        pipes.fds.push_back(fds[1]);
        fcntl(fds[0], F_SETFD, FD_CLOEXEC);
        fcntl(fds[1], F_SETFD, FD_CLOEXEC);
    };
    make_pipe(in);
    make_pipe(out);
    make_pipe(err);
    make_pipe(exec_error);
    std::vector<std::string> arguments = {executable.string()};
    arguments.insert(arguments.end(), options.arguments.begin(), options.arguments.end());
    std::vector<char*> argv;
    for (auto& arg : arguments)
        argv.push_back(arg.data());
    argv.push_back(nullptr);
    std::map<std::string, std::string> env;
    for (auto at = environ; *at; ++at) {
        std::string e = *at;
        auto eq = e.find('=');
        if (eq != std::string::npos)
            env[e.substr(0, eq)] = e.substr(eq + 1);
    }
    for (const auto& [k, v] : options.environment)
        env[k] = v;
    std::vector<std::string> envstrings;
    for (const auto& [k, v] : env)
        envstrings.push_back(k + "=" + v);
    std::vector<char*> envp;
    for (auto& e : envstrings)
        envp.push_back(e.data());
    envp.push_back(nullptr);
    std::string cwd = options.cwd.string();
    pid_t child = fork();
    if (child < 0)
        throw Error("ProcessError", "Cannot fork process");
    if (child == 0) {
        close(exec_error[0]);
        if (setpgid(0, 0) < 0 || dup2(in[0], STDIN_FILENO) < 0 || dup2(out[1], STDOUT_FILENO) < 0 ||
            dup2(err[1], STDERR_FILENO) < 0 || (!cwd.empty() && chdir(cwd.c_str()) != 0)) {
            int e = errno;
            ::write(exec_error[1], &e, sizeof(e));
            _exit(127);
        }
        for (int fd : pipes.fds)
            if (fd != exec_error[1] && fd > 2)
                close(fd);
        execve(executable.c_str(), argv.data(), envp.data());
        int e = errno;
        ::write(exec_error[1], &e, sizeof(e));
        _exit(127);
    }
    p.pid = child;
    setpgid(child, child);
    close(exec_error[1]);
    pipes.release(exec_error[1]);
    int launch_error = 0;
    ssize_t n;
    do {
        n = read(exec_error[0], &launch_error, sizeof(launch_error));
    } while (n < 0 && errno == EINTR);
    if (n != 0)
        throw Error("ProcessError", "Failed to execute configured process");
    p.input = in[1];
    if (fcntl(p.input, F_SETFL, fcntl(p.input, F_GETFL) | O_NONBLOCK) < 0)
        throw Error("ProcessError", "Cannot configure process stdin");
    pipes.release(p.input);
    pipes.release(out[0]);
    pipes.release(err[0]);
    p.output = std::thread([&p, fd = out[0]] {
        p.read_stream(false, [&p, fd](char* b, std::size_t size) {
            pollfd item{fd, POLLIN, 0};
            while (!p.stopping.load()) {
                auto ready = poll(&item, 1, 50);
                if (ready > 0)
                    break;
                if (ready < 0 && errno != EINTR)
                    return ssize_t(-1);
            }
            if (p.stopping.load())
                return ssize_t(0);
            ssize_t n;
            do {
                n = read(fd, b, size);
            } while (n < 0 && errno == EINTR);
            return n;
        });
        close(fd);
    });
    p.error = std::thread([&p, fd = err[0]] {
        p.read_stream(true, [&p, fd](char* b, std::size_t size) {
            pollfd item{fd, POLLIN, 0};
            while (!p.stopping.load()) {
                auto ready = poll(&item, 1, 50);
                if (ready > 0)
                    break;
                if (ready < 0 && errno != EINTR)
                    return ssize_t(-1);
            }
            if (p.stopping.load())
                return ssize_t(0);
            ssize_t n;
            do {
                n = read(fd, b, size);
            } while (n < 0 && errno == EINTR);
            return n;
        });
        close(fd);
    });
#endif
}
Process::~Process() = default;
void Process::terminate() noexcept {
    impl_->stop();
}
bool Process::running() {
    return impl_->active();
}
void Process::write(const std::string& text, const Cancellation& cancel) {
    auto& p = *impl_;
    std::lock_guard lock(p.write_mutex);
    std::size_t at = 0;
    cancel.check();
    std::jthread watcher([&](std::stop_token stop) {
        std::mutex mutex;
        std::condition_variable_any wake;
        std::unique_lock wait(mutex);
        while (!stop.stop_requested()) {
            if (cancel.cancelled() || Clock::now() >= cancel.deadline) {
                p.stop();
                return;
            }
            wake.wait_for(wait, stop, std::chrono::milliseconds(20), [] { return false; });
        }
    });
    while (at < text.size()) {
        cancel.check();
#ifdef _WIN32
        DWORD n = 0;
        if (!p.input.value ||
            !WriteFile(p.input.value, text.data() + at,
                       static_cast<DWORD>(std::min<std::size_t>(text.size() - at, 65536)), &n,
                       nullptr) ||
            !n) {
            cancel.check();
            throw Error("ProcessClosed", "Process stdin closed");
        }
#else
        if (p.input < 0)
            throw Error("ProcessClosed", "Process stdin closed");
        sigset_t set, old, pending;
        sigemptyset(&set);
        sigaddset(&set, SIGPIPE);
        pthread_sigmask(SIG_BLOCK, &set, &old);
        sigpending(&pending);
        bool previous = sigismember(&pending, SIGPIPE) == 1;
        ssize_t n = ::write(p.input, text.data() + at, text.size() - at);
        int saved = errno;
        if (n < 0 && saved == EPIPE && !previous) {
            sigset_t after;
            sigpending(&after);
            if (sigismember(&after, SIGPIPE) == 1) {
                int signal_number = 0;
                sigwait(&set, &signal_number);
            }
        }
        pthread_sigmask(SIG_SETMASK, &old, nullptr);
        if (n < 0 && saved == EINTR)
            continue;
        if (n < 0 && (saved == EAGAIN || saved == EWOULDBLOCK)) {
            pollfd item{p.input, POLLOUT, 0};
            poll(&item, 1, 20);
            continue;
        }
        if (n <= 0)
            throw Error("ProcessClosed", "Process stdin closed");
#endif
        at += static_cast<std::size_t>(n);
    }
    cancel.check();
    watcher.request_stop();
}
void Process::close_input() {
    auto& p = *impl_;
    std::lock_guard lock(p.write_mutex);
#ifdef _WIN32
    p.input.reset();
#else
    if (p.input >= 0) {
        close(p.input);
        p.input = -1;
    }
#endif
}
bool Process::next(ProcessLine& line, const Cancellation& cancel) {
    auto& p = *impl_;
    std::unique_lock lock(p.mutex);
    while (true) {
        cancel.check();
        if (!p.fault.empty())
            throw Error("OutputOverflow", p.fault);
        if (!p.lines.empty()) {
            line = std::move(p.lines.front());
            p.lines.pop_front();
            p.queued -= line.text.size() + sizeof(ProcessLine);
            p.space.notify_all();
            return true;
        }
        if (p.closed_streams == 2)
            return false;
        p.wake.wait_for(lock, std::chrono::milliseconds(20));
    }
}
int Process::wait(const Cancellation& cancel) {
    while (running()) {
        cancel.check();
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
    cancel.check();
    return impl_->exit_code;
}
} // namespace cymcp
