#pragma once
#include "DapaEngineDraw.h"
#include <bcrypt.h>
#include <vector>

namespace DapaCsxDraw {
// Adapter for the accepted calls in Paintball build C192935E... . No menu
// decisions are replaced. Verify the ENTIRE owner function before installing.
inline constexpr uint32_t ownerRva=0x105bd0;
inline constexpr size_t ownerSize=0x384;
inline constexpr std::array<uint8_t,32> ownerSha256{
    0x2b,0x5b,0xc1,0x45,0xd2,0xa4,0xd7,0xf9,0xc9,0x79,0xdf,0x11,0x7f,0xa7,0x24,0xec,
    0xb4,0x9b,0x14,0xe8,0x15,0x83,0x02,0x87,0xb4,0x39,0x84,0xf2,0xbd,0x92,0xef,0x24};
inline constexpr std::array<DapaEngineDraw::Site,2> sites{{
    {0x105c3c,true,7,0,{0x41,0xff,0x92,0xa0,0,0,0}},
    {0x105f10,true,6,0,{0xff,0x90,0xa0,0,0,0}}
}};

inline bool MatchesOwner(const void* function) {
    std::array<uint8_t,ownerSize> bytes{};
    SIZE_T read=0;
    if(!ReadProcessMemory(GetCurrentProcess(),function,bytes.data(),bytes.size(),&read) || read!=bytes.size())return false;
    std::array<uint8_t,32> hash{};
    BCRYPT_ALG_HANDLE algorithm=nullptr;BCRYPT_HASH_HANDLE handle=nullptr;
    if(BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0)return false;
    DWORD objectSize=0,resultSize=0;
    bool ok=BCryptGetProperty(algorithm,BCRYPT_OBJECT_LENGTH,reinterpret_cast<PUCHAR>(&objectSize),sizeof(objectSize),&resultSize,0)>=0;
    std::vector<uint8_t> object(ok?objectSize:0);
    if(ok)ok=BCryptCreateHash(algorithm,&handle,object.data(),objectSize,nullptr,0,0)>=0;
    if(ok)ok=BCryptHashData(handle,bytes.data(),ULONG(bytes.size()),0)>=0;
    if(ok)ok=BCryptFinishHash(handle,hash.data(),ULONG(hash.size()),0)>=0;
    if(handle)BCryptDestroyHash(handle);
    BCryptCloseAlgorithmProvider(algorithm,0);
    return ok && hash==ownerSha256;
}

// Non-fatal near allocation. The page stays resident for any in-flight callback.
inline uint8_t* AllocateNear(uintptr_t address) {
    SYSTEM_INFO info{};GetSystemInfo(&info);
    const uintptr_t step=info.dwAllocationGranularity;
    const uintptr_t lower=address>0x70000000?address-0x70000000:step;
    const uintptr_t upper=address+0x70000000;
    for(uintptr_t probe=(lower+step-1)&~(step-1);probe<upper;) {
        MEMORY_BASIC_INFORMATION region{};
        if(!VirtualQuery(reinterpret_cast<void*>(probe),&region,sizeof(region)))return nullptr;
        const uintptr_t end=reinterpret_cast<uintptr_t>(region.BaseAddress)+region.RegionSize;
        if(end<=probe)return nullptr;
        if(region.State==MEM_FREE && end-probe>=4096) {
            if(auto* memory=VirtualAlloc(reinterpret_cast<void*>(probe),4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE))
                return static_cast<uint8_t*>(memory);
        }
        probe=(end+step-1)&~(step-1);
    }
    return nullptr;
}

struct Adapter {
    std::array<DapaEngineDraw::Hook,2> hooks{};
    uint8_t* code=nullptr;
    DWORD error=0;
    bool Prepare(uintptr_t module,void* owner,uintptr_t callback) {
        if(reinterpret_cast<uintptr_t>(owner)!=module+ownerRva || !MatchesOwner(owner)) {
            error=ERROR_REVISION_MISMATCH;return false;
        }
        for(size_t i=0;i<hooks.size();++i) {
            if(!hooks[i].Prepare(sites[i],reinterpret_cast<uint8_t*>(module+sites[i].rva)) || hooks[i].chained) {
                error=ERROR_INVALID_DATA;return false;
            }
        }
        code=AllocateNear(module+ownerRva);
        if(!code) {error=ERROR_NOT_ENOUGH_MEMORY;return false;}
        for(size_t i=0;i<hooks.size();++i)if(!hooks[i].Build(code+i*64,callback)) {
            error=hooks[i].error;return false;
        }
        DWORD old=0;
        if(!VirtualProtect(code,4096,PAGE_EXECUTE_READ,&old)) {error=GetLastError();return false;}
        FlushInstructionCache(GetCurrentProcess(),code,128);
        return true;
    }
    bool Install() {
        for(auto& hook:hooks)if(!hook.Install()) {error=hook.error;Remove();return false;}
        return true;
    }
    bool Remove() {
        bool ok=true;
        for(auto& hook:hooks)if(!hook.Remove()) {error=hook.error;ok=false;}
        return ok;
    }
};
}
