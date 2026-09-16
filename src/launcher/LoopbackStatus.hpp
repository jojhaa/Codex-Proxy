#pragma once
#include <windows.h>
#include <netfw.h>
#include <userenv.h>

// 只读查询；失败与未配置均不报告已确认，不自动变更系统权限。
inline bool IsLoopbackExempt(const wchar_t* familyName, DWORD& error) {
    PSID sid = nullptr;
    const HRESULT hr = DeriveAppContainerSidFromAppContainerName(familyName, &sid);
    if (FAILED(hr)) { error = static_cast<DWORD>(hr); return false; }
    DWORD count = 0;
    PSID_AND_ATTRIBUTES entries = nullptr;
    const HMODULE firewall = LoadLibraryExW(L"FirewallAPI.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!firewall) { error = GetLastError(); FreeSid(sid); return false; }
    using GetConfig = DWORD(WINAPI*)(DWORD*, PSID_AND_ATTRIBUTES*);
    const auto getConfig = reinterpret_cast<GetConfig>(GetProcAddress(firewall, "NetworkIsolationGetAppContainerConfig"));
    if (!getConfig) { error = GetLastError(); FreeLibrary(firewall); FreeSid(sid); return false; }
    error = getConfig(&count, &entries);
    bool found = false;
    if (error == ERROR_SUCCESS) {
        for (DWORD i = 0; i < count; ++i) {
            if (EqualSid(sid, entries[i].Sid)) { found = true; break; }
        }
    }
    if (entries) {
        for (DWORD i = 0; i < count; ++i) HeapFree(GetProcessHeap(), 0, entries[i].Sid);
        HeapFree(GetProcessHeap(), 0, entries);
    }
    FreeSid(sid);
    FreeLibrary(firewall);
    return found;
}
