#pragma once
#include <cstdint>
#include <string_view>
namespace cyrevision {
inline std::uint64_t uiIdentity(std::string_view key){
    std::uint64_t hash=14695981039346656037ULL;
    for(unsigned char c:key){hash^=c;hash*=1099511628211ULL;}
    return hash?hash:1;
}
}
