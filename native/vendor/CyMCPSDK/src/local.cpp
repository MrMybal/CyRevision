#include <cstdlib>
#include <cymcp/local.hpp>
#include <fstream>
namespace cymcp {
int available_loopback_port();
struct LocalEngine::Impl {
    LocalEngineOptions options;
    std::unique_ptr<Process> process;
    std::thread drain;
    std::string url, key;
    std::filesystem::path keyfile;
    mutable std::mutex mutex;
    std::mutex cancellation_mutex;
    Cancellation startup_cancel;
    Cancellation output_cancel;
    explicit Impl(LocalEngineOptions opts) : options(std::move(opts)) {}
    void stop() noexcept {
        output_cancel.cancel();
        if (process)
            process->terminate();
        if (drain.joinable())
            drain.join();
        process.reset();
        url.clear();
        if (!keyfile.empty()) {
            std::error_code ec;
            std::filesystem::remove(keyfile, ec);
            keyfile.clear();
        }
    }
    ~Impl() {
        stop();
    }
};
LocalEngine::LocalEngine(LocalEngineOptions options)
    : impl_(std::make_unique<Impl>(std::move(options))) {}
LocalEngine::~LocalEngine() = default;
void LocalEngine::start(const CallContext& context) {
    auto& p = *impl_;
    std::lock_guard lock(p.mutex);
    if (p.process)
        throw Error("InvalidState", "Local engine already running");
    if (!p.options.model.is_absolute() || !std::filesystem::is_regular_file(p.options.model))
        throw Error("ModelMissing", "Configure an existing absolute model path");
    if (p.options.private_work_directory.empty() || p.options.context_tokens < 128 ||
        p.options.context_tokens > 1024 * 1024 || p.options.gpu_layers < 0 ||
        p.options.startup_timeout.count() <= 0)
        throw Error("InvalidOptions", "Invalid local engine options");
    for (const auto& arg : p.options.process.arguments)
        if (arg.rfind("--host", 0) == 0 || arg.rfind("--port", 0) == 0 ||
            arg.rfind("--api-key", 0) == 0 || arg == "-m" || arg == "--model")
            throw Error("InvalidOptions",
                        "Engine connection/model arguments are owned by LocalEngine");
    auto opts = p.options.process;
    if (!p.options.cuda_runtime_directory.empty()) {
        const auto& runtime = p.options.cuda_runtime_directory;
#if defined(_WIN32)
        const std::string key = "PATH";
        constexpr char separator = ';';
        const wchar_t* current = _wgetenv(L"PATH");
        std::string inherited = current ? path_utf8(std::filesystem::path(current)) : "";
        const char* library = "cudart64_12.dll";
#elif defined(__linux__)
        const std::string key = "LD_LIBRARY_PATH";
        constexpr char separator = ':';
        const char* current = std::getenv("LD_LIBRARY_PATH");
        std::string inherited = current ? current : "";
        const char* library = "libcudart.so.12";
#else
        throw Error("UnsupportedPlatform", "CUDA runtime integration supports Windows and Linux");
#endif
#if defined(_WIN32) || defined(__linux__)
        const auto path = path_utf8(runtime);
        if (!runtime.is_absolute() || path.find(separator) != std::string::npos ||
            !std::filesystem::is_regular_file(runtime / library))
            throw Error("RuntimeMissing", "Configure the installed CUDA runtime directory");
        for (auto it = opts.environment.begin(); it != opts.environment.end(); ++it) {
#ifdef _WIN32
            const bool match = _stricmp(it->first.c_str(), key.c_str()) == 0;
#else
            const bool match = it->first == key;
#endif
            if (match) {
                inherited = it->second;
                opts.environment.erase(it);
                break;
            }
        }
        opts.environment[key] = inherited.empty() ? path : path + separator + inherited;
#endif
    }
    private_directory(p.options.private_work_directory);
    p.key = random_token();
    p.keyfile = p.options.private_work_directory / ("engine-" + random_token(16) + ".key");
    {
        std::ofstream key(p.keyfile, std::ios::binary);
        key << p.key;
        if (!key)
            throw Error("FileError", "Cannot create private engine credential");
    }
#ifndef _WIN32
    std::filesystem::permissions(
        p.keyfile, std::filesystem::perms::owner_read | std::filesystem::perms::owner_write,
        std::filesystem::perm_options::replace);
#endif
    int port = available_loopback_port();
    opts.arguments.insert(opts.arguments.end(),
                          {"--model", path_utf8(p.options.model), "--host", "127.0.0.1", "--port",
                           std::to_string(port), "--api-key-file", path_utf8(p.keyfile),
                           "--ctx-size", std::to_string(p.options.context_tokens), "--n-gpu-layers",
                           std::to_string(p.options.gpu_layers), "--parallel", "1"});
    p.url = "http://127.0.0.1:" + std::to_string(port);
    p.output_cancel = Cancellation();
    Cancellation startup = context.cancellation.child();
    startup.deadline = std::min(startup.deadline, Clock::now() + p.options.startup_timeout);
    {
        std::lock_guard cancellation_lock(p.cancellation_mutex);
        p.startup_cancel = startup;
    }
    try {
        context.emit("EngineStarting");
        p.process = std::make_unique<Process>(opts);
        p.process->close_input();
        p.drain = std::thread([&p] {
            try {
                ProcessLine line;
                while (p.process->next(line, p.output_cancel)) {
                }
            } catch (...) {
                p.process->terminate();
            }
        });
        while (true) {
            startup.check();
            if (!p.process->running())
                throw Error("EngineStartFailed", "Local engine exited before readiness");
            try {
                HttpRequest req;
                req.url = p.url + "/v1/models";
                req.headers["Authorization"] = "Bearer " + p.key;
                req.timeout = std::chrono::milliseconds(500);
                auto response = HttpClient().perform(req, startup);
                if (response.status == 200) {
                    auto value = parse_json(response.body);
                    if (value.contains("data") && value["data"].is_array() &&
                        !value["data"].empty())
                        break;
                }
            } catch (const Error& e) {
                if (e.code != "NetworkError" && e.code != "Timeout")
                    throw;
                startup.check();
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(50));
        }
        context.emit("EngineReady", {{"baseUrl", p.url + "/v1"}});
    } catch (...) {
        p.stop();
        throw;
    }
}
void LocalEngine::stop() noexcept {
    {
        std::lock_guard cancellation_lock(impl_->cancellation_mutex);
        impl_->startup_cancel.cancel();
    }
    std::lock_guard lock(impl_->mutex);
    impl_->stop();
}
std::string LocalEngine::base_url() const {
    std::lock_guard lock(impl_->mutex);
    if (!impl_->process)
        throw Error("InvalidState", "Local engine not running");
    return impl_->url + "/v1";
}
std::string LocalEngine::token() const {
    std::lock_guard lock(impl_->mutex);
    if (!impl_->process)
        throw Error("InvalidState", "Local engine not running");
    return impl_->key;
}
} // namespace cymcp
