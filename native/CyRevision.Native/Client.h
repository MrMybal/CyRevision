#pragma once
#include <windows.h>
#include <nlohmann/json.hpp>
#include <atomic>
#include <condition_variable>
#include <deque>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace cyrevision {
using Json = nlohmann::json;
std::wstring wide(const std::string& text);
std::string utf8(const std::wstring& text);
std::wstring quote(const std::wstring& argument);

class Handle {
    HANDLE value_{};
public:
    Handle() = default;
    explicit Handle(HANDLE value) : value_(value) {}
    ~Handle() { reset(); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
    operator HANDLE() const { return value_; }
    HANDLE* put() { reset(); return &value_; }
    void reset(HANDLE value = nullptr) { if(value_ && value_ != INVALID_HANDLE_VALUE) CloseHandle(value_); value_=value; }
};

class EngineConnection {
public:
    virtual ~EngineConnection() = default;
    virtual unsigned long long send(Json request) = 0;
    virtual std::vector<Json> take() = 0;
    virtual bool alive() const = 0;
};

class Client : public EngineConnection {
    Handle input_, output_, errors_, process_, job_, wake_;
    std::thread writer_, reader_, errorReader_;
    std::atomic<bool> stopping_{false};
    std::atomic<unsigned long long> nextId_{1};
    std::mutex inputMutex_, outputMutex_, errorMutex_;
    std::condition_variable ready_;
    std::deque<std::string> requests_;
    std::deque<Json> responses_;
    std::string diagnostic_;
    void readLoop();
    void writeLoop();
    void errorLoop();
public:
    Client(const std::wstring& backend, const std::vector<std::wstring>& arguments);
    ~Client();
    Client(const Client&)=delete;
    Client& operator=(const Client&)=delete;
    unsigned long long send(Json request) override;
    std::vector<Json> take() override;
    bool alive() const override;
    std::string diagnostic();
    HANDLE wakeEvent() const { return wake_; }
    void stop();
};
}
