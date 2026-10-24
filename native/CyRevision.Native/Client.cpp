#include "Client.h"
#include <filesystem>
#include <stdexcept>

namespace cyrevision {
std::wstring wide(const std::string& text) {
    if(text.empty()) return {};
    int size=MultiByteToWideChar(CP_UTF8,0,text.data(),static_cast<int>(text.size()),nullptr,0);
    std::wstring result(size,L'\0');
    MultiByteToWideChar(CP_UTF8,0,text.data(),static_cast<int>(text.size()),result.data(),size);
    return result;
}
std::string utf8(const std::wstring& text) {
    if(text.empty()) return {};
    int size=WideCharToMultiByte(CP_UTF8,0,text.data(),static_cast<int>(text.size()),nullptr,0,nullptr,nullptr);
    std::string result(size,'\0');
    WideCharToMultiByte(CP_UTF8,0,text.data(),static_cast<int>(text.size()),result.data(),size,nullptr,nullptr);
    return result;
}
std::wstring quote(const std::wstring& argument) {
    std::wstring result=L"\"";
    size_t slashes=0;
    for(wchar_t character:argument) {
        if(character==L'\\') { ++slashes; continue; }
        if(character==L'"') result.append(slashes*2+1,L'\\');
        else result.append(slashes,L'\\');
        slashes=0;
        result+=character;
    }
    result.append(slashes*2,L'\\');
    return result+L'"';
}
Client::Client(const std::wstring& backend,const std::vector<std::wstring>& arguments) {
    SECURITY_ATTRIBUTES attributes{sizeof(attributes),nullptr,TRUE};
    Handle childInput,childOutput,childErrors,thread;
    if(!CreatePipe(childInput.put(),input_.put(),&attributes,0) ||
       !CreatePipe(output_.put(),childOutput.put(),&attributes,0) ||
       !CreatePipe(errors_.put(),childErrors.put(),&attributes,0))
        throw std::runtime_error("Could not create the engine connection.");
    if(!SetHandleInformation(input_,HANDLE_FLAG_INHERIT,0) ||
       !SetHandleInformation(output_,HANDLE_FLAG_INHERIT,0) ||
       !SetHandleInformation(errors_,HANDLE_FLAG_INHERIT,0))
        throw std::runtime_error("Could not configure the engine connection.");
    wake_.reset(CreateEventW(nullptr,FALSE,FALSE,nullptr));
    job_.reset(CreateJobObjectW(nullptr,nullptr));
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
    limits.BasicLimitInformation.LimitFlags=JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
    if(!wake_ || !job_ || !SetInformationJobObject(job_,JobObjectExtendedLimitInformation,&limits,sizeof(limits)))
        throw std::runtime_error("Could not configure engine lifetime.");
    STARTUPINFOW startup{};
    startup.cb=sizeof(startup);
    startup.dwFlags=STARTF_USESTDHANDLES;
    startup.hStdInput=childInput; startup.hStdOutput=childOutput; startup.hStdError=childErrors;
    PROCESS_INFORMATION information{};
    std::wstring command=quote(backend)+L" --native-backend";
    for(const auto& argument:arguments) command+=L" "+quote(argument);
    auto directory=std::filesystem::path(backend).parent_path().wstring();
    if(!CreateProcessW(backend.c_str(),command.data(),nullptr,nullptr,TRUE,CREATE_NO_WINDOW|CREATE_SUSPENDED,
        nullptr,directory.c_str(),&startup,&information))
        throw std::runtime_error("Could not start the CyRevision engine (Windows "+std::to_string(GetLastError())+").");
    process_.reset(information.hProcess); thread.reset(information.hThread);
    if(!AssignProcessToJobObject(job_,process_)) {
        TerminateProcess(process_,1);
        throw std::runtime_error("Could not attach the engine lifetime.");
    }
    if(ResumeThread(thread)==static_cast<DWORD>(-1)) throw std::runtime_error("Could not resume the engine.");
    childInput.reset(); childOutput.reset(); childErrors.reset();
    try {
        writer_=std::thread(&Client::writeLoop,this);
        reader_=std::thread(&Client::readLoop,this);
        errorReader_=std::thread(&Client::errorLoop,this);
    } catch(...) { stop(); throw; }
}
Client::~Client() { stop(); }
unsigned long long Client::send(Json request) {
    if(stopping_) return 0;
    auto id=nextId_++;
    request["id"]=id;
    auto text=request.dump(-1,' ',false,Json::error_handler_t::replace)+"\n";
    {
        std::lock_guard<std::mutex> lock(inputMutex_);
        if(requests_.size()>=128) return 0;
        requests_.push_back(std::move(text));
    }
    ready_.notify_one();
    return id;
}
void Client::writeLoop() {
    while(!stopping_) {
        std::string request;
        {
            std::unique_lock<std::mutex> lock(inputMutex_);
            ready_.wait(lock,[&]{return stopping_ || !requests_.empty();});
            if(stopping_) return;
            request=std::move(requests_.front()); requests_.pop_front();
        }
        size_t offset=0;
        while(offset<request.size() && !stopping_) {
            DWORD written=0;
            if(!WriteFile(input_,request.data()+offset,static_cast<DWORD>(request.size()-offset),&written,nullptr) || written==0) return;
            offset+=written;
        }
    }
}
void Client::readLoop() {
    std::string buffered;
    char chunk[16384];
    DWORD count=0;
    while(!stopping_ && ReadFile(output_,chunk,sizeof(chunk),&count,nullptr) && count) {
        buffered.append(chunk,count);
        if(buffered.size()>32*1024*1024) {
            std::lock_guard<std::mutex> lock(errorMutex_);
            diagnostic_="The engine response exceeded the connection limit.";
            break;
        }
        size_t newline;
        while((newline=buffered.find('\n'))!=std::string::npos) {
            Json message=Json::parse(buffered.begin(),buffered.begin()+static_cast<std::ptrdiff_t>(newline),nullptr,false);
            buffered.erase(0,newline+1);
            if(!message.is_object()) continue;
            {
                std::lock_guard<std::mutex> lock(outputMutex_);
                if(responses_.size()<256) responses_.push_back(std::move(message));
            }
            SetEvent(wake_);
        }
    }
    SetEvent(wake_);
}
void Client::errorLoop() {
    char chunk[4096]; DWORD count=0;
    while(!stopping_ && ReadFile(errors_,chunk,sizeof(chunk),&count,nullptr) && count) {
        std::lock_guard<std::mutex> lock(errorMutex_);
        diagnostic_.append(chunk,count);
        if(diagnostic_.size()>16384) diagnostic_.erase(0,diagnostic_.size()-16384);
    }
}
std::vector<Json> Client::take() {
    std::lock_guard<std::mutex> lock(outputMutex_);
    std::vector<Json> result;
    while(!responses_.empty()) { result.push_back(std::move(responses_.front())); responses_.pop_front(); }
    return result;
}
std::string Client::diagnostic() { std::lock_guard<std::mutex> lock(errorMutex_); return diagnostic_; }
bool Client::alive() const { return process_ && WaitForSingleObject(process_,0)==WAIT_TIMEOUT; }
void Client::stop() {
    if(stopping_) return;
    if(alive()) {
        send({{"command","cancel"}});
        send({{"command","shutdown"}});
        if(WaitForSingleObject(process_,6000)==WAIT_TIMEOUT) TerminateJobObject(job_,1);
    }
    stopping_=true; ready_.notify_all();
    job_.reset();
    for(auto* thread:{&writer_,&reader_,&errorReader_})
        if(thread->joinable()) CancelSynchronousIo(thread->native_handle());
    for(auto* thread:{&writer_,&reader_,&errorReader_})
        if(thread->joinable()) thread->join();
    input_.reset(); output_.reset(); errors_.reset(); process_.reset();
}
}
