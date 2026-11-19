#pragma once
#include "http.hpp"
#include "process.hpp"
namespace cymcp {
struct LocalEngineOptions {
    ProcessOptions process;
    std::filesystem::path model, private_work_directory;
    int context_tokens = 4096, gpu_layers = 0;
    std::chrono::milliseconds startup_timeout{120000};
    // Optional runtime/ directory returned under the installed CUDA package. Prepended
    // to the library search path of this engine process only.
    std::filesystem::path cuda_runtime_directory;
};
class LocalEngine {
    struct Impl;
    std::unique_ptr<Impl> impl_;

  public:
    explicit LocalEngine(LocalEngineOptions);
    ~LocalEngine();
    void start(const CallContext& = {});
    void stop() noexcept;
    std::string base_url() const;
    std::string token() const;
};
struct ArchiveMember {
    std::string member_path, relative_path, sha256;
    std::uint64_t size = 0;
};
struct Artifact {
    std::string relative_path, url, sha256;
    std::uint64_t size = 0;
    bool executable = false;
    // Explicit regular files to extract after the archive itself passes verification.
    std::vector<ArchiveMember> members;
};
struct Package {
    std::string id, version, platform;
    std::vector<Artifact> artifacts;
    std::vector<std::string> redirect_hosts;
    std::string installer;
    std::vector<std::string> installer_arguments;
    bool requires_elevation = false;
};
// NVIDIA CUDART 12 for Windows/Linux x64. install() returns a private package directory
// containing runtime/ and the vendor license. No elevation or system driver changes.
Package cuda_runtime_package();
struct DependencyOptions {
    std::filesystem::path cache_directory;
    Policy policy;
    bool allow_loopback_http_for_tests = false;
    std::uint64_t max_package_bytes = 16ULL * 1024 * 1024 * 1024;
};
using ElevationHandler = std::function<int(const ProcessOptions&, const CallContext&)>;
class DependencyManager {
    DependencyOptions options_;

  public:
    explicit DependencyManager(DependencyOptions);
    // Manifests must come from the host's trusted catalogue, never from model output.
    std::filesystem::path install(const Package&, const CallContext& = {}) const;
    bool verify(const Package&, const CallContext& = {}) const;
    int run_installer(const Package&, const CallContext& = {}, ElevationHandler = {}) const;
    void uninstall(const Package&, const CallContext& = {}) const;
};
} // namespace cymcp
