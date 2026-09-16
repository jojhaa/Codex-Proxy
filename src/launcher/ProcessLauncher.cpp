// Project-specific contributions: Copyright (c) Time Silent.
// SPDX-License-Identifier: GPL-3.0-only
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <filesystem>
#include <iostream>
#include <string>
#include <vector>
#include "injection/ProcessInjector.hpp"

// 仅管理本次创建的进程，不附加或终止任何已有进程。
int wmain(int argc, wchar_t** argv) {
    SetConsoleOutputCP(CP_UTF8);
    if (argc < 2) return 2;
    wchar_t own[32768]{};
    if (!GetModuleFileNameW(nullptr, own, 32768)) return 3;
    auto dll = std::filesystem::path(own).parent_path() / L"codex_proxy.dll";
    const auto executable = std::filesystem::absolute(argv[1]);
    if (!std::filesystem::is_regular_file(dll) || !std::filesystem::is_regular_file(executable)) return 4;
    auto quote = [](const std::wstring& value) {
        std::wstring out = L"\""; size_t slashes = 0;
        for (wchar_t c : value) {
            if (c == L'\\') { ++slashes; continue; }
            out.append(slashes * (c == L'\"' ? 2 : 1), L'\\'); slashes = 0;
            if (c == L'\"') out += L'\\'; out += c;
        }
        out.append(slashes * 2, L'\\'); return out + L'\"';
    };
    std::wstring command = quote(executable.wstring());
    for (int i = 2; i < argc; ++i) command += L" " + quote(argv[i]);
    std::vector<wchar_t> mutableCommand(command.begin(), command.end()); mutableCommand.push_back(0);
    STARTUPINFOW startup{}; startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESTDHANDLES; // 不让目标持有加载器的输出管道。
    PROCESS_INFORMATION child{};
    // 子进程不继承 stdout 管道，管理器可以等待此 helper 退出。
    if (!CreateProcessW(executable.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE,
            CREATE_SUSPENDED, nullptr, executable.parent_path().c_str(), &startup, &child)) {
        std::cerr << "CREATE_FAILED " << GetLastError() << '\n'; return 5;
    }
    std::string reason;
    bool injected = Injection::ProcessInjector::InjectDll(child.hProcess, dll.wstring(), &reason);
    if (!injected || ResumeThread(child.hThread) == static_cast<DWORD>(-1)) {
        // 本次尚未运行的自有进程失败即撤销，避免无代理启动。
        TerminateProcess(child.hProcess, 1); WaitForSingleObject(child.hProcess, 5000);
        CloseHandle(child.hThread); CloseHandle(child.hProcess);
        std::cerr << "PROXY_START_FAILED " << reason << '\n'; return 6;
    }
    std::cout << "PROXY_PID=" << child.dwProcessId << '\n';
    CloseHandle(child.hThread); CloseHandle(child.hProcess);
    return 0;
}
