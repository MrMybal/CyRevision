#pragma once
#include "core.hpp"
namespace cymcp {
struct ProcessOptions {
    std::filesystem::path executable;
    std::vector<std::string> arguments;
    std::filesystem::path cwd;
    std::map<std::string, std::string> environment;
    // Memory guards only. A full queue applies backpressure to the child; it never fails a turn.
    std::size_t max_line_bytes = 256 * 1024 * 1024, max_queue_bytes = 64 * 1024 * 1024;
};
enum class StdoutFormat { Lines, Binary };
std::vector<std::string> process_error_secrets(const ProcessOptions&);
struct ProcessLine {
    bool stderr_stream = false;
    std::string text;
};
class Process {
    struct Impl;
    std::unique_ptr<Impl> impl_;

  public:
    explicit Process(const ProcessOptions&);
    // Binary mode returns raw stdout chunks from next(), preserving every byte. Stderr
    // remains line-oriented. Both streams use the same bounded, cancellable output queue.
    Process(const ProcessOptions&, StdoutFormat);
    ~Process();
    Process(const Process&) = delete;
    Process& operator=(const Process&) = delete;
    void write(const std::string&, const Cancellation& = {});
    void close_input();
    // Returns false only after both stdout/stderr reach EOF. Cancellation works while silent.
    bool next(ProcessLine&, const Cancellation& = {});
    int wait(const Cancellation& = {});
    bool running();
    void terminate() noexcept;
};
std::filesystem::path find_executable(const std::string& name);
// A file in a fresh owner-only temporary directory, removed with this object. Used to pass
// configuration that may contain credentials (MCP headers, settings) without exposing it in
// the process command line, and to pass texts longer than the OS command-line limit.
class PrivateFile {
    std::filesystem::path directory_, path_;

  public:
    PrivateFile(const std::string& name, const std::string& content);
    ~PrivateFile();
    PrivateFile(const PrivateFile&) = delete;
    PrivateFile& operator=(const PrivateFile&) = delete;
    const std::filesystem::path& path() const noexcept {
        return path_;
    }
};
} // namespace cymcp
