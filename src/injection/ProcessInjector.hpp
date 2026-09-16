#pragma once

// 防止 windows.h 自动包含 winsock.h (避免与 winsock2.h 冲突)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif

#include <windows.h>
#include <tlhelp32.h>
#include <string>
#include "../core/Logger.hpp"

namespace Injection {

    // 进程注入器
    // 用于将 DLL 注入到子进程中
    class ProcessInjector {
    public:
        static bool VerifyReady(HANDLE process, const std::wstring& path) {
            HMODULE local = LoadLibraryExW(path.c_str(), nullptr, DONT_RESOLVE_DLL_REFERENCES);
            if (!local) return false;
            auto probe = GetProcAddress(local, "CodexProxyReady");
            SIZE_T offset = probe ? reinterpret_cast<SIZE_T>(probe) - reinterpret_cast<SIZE_T>(local) : 0;
            HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, GetProcessId(process));
            BYTE* remoteBase = nullptr;
            if (snapshot != INVALID_HANDLE_VALUE) {
                MODULEENTRY32W module{}; module.dwSize = sizeof(module);
                if (Module32FirstW(snapshot, &module)) do {
                    if (_wcsicmp(module.szExePath, path.c_str()) == 0) { remoteBase = module.modBaseAddr; break; }
                } while (Module32NextW(snapshot, &module));
                CloseHandle(snapshot);
            }
            bool ready = false;
            if (probe && remoteBase) {
                HANDLE thread = CreateRemoteThread(process, nullptr, 0,
                    reinterpret_cast<LPTHREAD_START_ROUTINE>(remoteBase + offset), nullptr, 0, nullptr);
                if (thread) {
                    DWORD result = 0;
                    ready = WaitForSingleObject(thread, 5000) == WAIT_OBJECT_0 &&
                        GetExitCodeThread(thread, &result) && result == 1;
                    CloseHandle(thread);
                }
            }
            FreeLibrary(local);
            return ready;
        }
        // 注入 DLL 到目标进程
        // hProcess: 目标进程句柄 (需要 PROCESS_ALL_ACCESS 权限)
        // dllPath: 要注入的 DLL 完整路径
        // failureReason: 可选，失败时返回简短诊断信息
        // 返回: 成功返回 true
        static bool InjectDll(HANDLE hProcess, const std::wstring& dllPath, std::string* failureReason = nullptr) {
            const auto setFailureReason = [failureReason](const std::string& reason) {
                if (failureReason) {
                    *failureReason = reason;
                }
            };

            USHORT targetMachine = 0, targetNative = 0, ownMachine = 0, ownNative = 0;
            if (!IsWow64Process2(hProcess, &targetMachine, &targetNative) ||
                !IsWow64Process2(GetCurrentProcess(), &ownMachine, &ownNative) ||
                targetMachine != ownMachine || targetNative != ownNative) {
                setFailureReason("unsupported process architecture"); return false;
            }

            // 步骤 1: 在目标进程中分配内存
            SIZE_T dllPathSize = (dllPath.length() + 1) * sizeof(wchar_t);
            LPVOID remoteDllPath = VirtualAllocEx(
                hProcess,
                NULL,
                dllPathSize,
                MEM_COMMIT | MEM_RESERVE,
                PAGE_READWRITE
            );

            if (!remoteDllPath) {
                const DWORD err = GetLastError();
                Core::Logger::Error("ProcessInjector: 虚拟内存分配失败 (VirtualAllocEx failed), err=" + std::to_string(err));
                setFailureReason("VirtualAllocEx failed, err=" + std::to_string(err));
                return false;
            }

            // 步骤 2: 将 DLL 路径写入目标进程
            if (!WriteProcessMemory(hProcess, remoteDllPath, dllPath.c_str(), dllPathSize, NULL)) {
                const DWORD err = GetLastError();
                Core::Logger::Error("ProcessInjector: 写入进程内存失败 (WriteProcessMemory failed), err=" + std::to_string(err));
                setFailureReason("WriteProcessMemory failed, err=" + std::to_string(err));
                VirtualFreeEx(hProcess, remoteDllPath, 0, MEM_RELEASE);
                return false;
            }

            // 步骤 3: 获取 LoadLibraryW 地址 (Kernel32.dll 在所有进程中地址相同)
            HMODULE hKernel32 = GetModuleHandleW(L"kernel32.dll");
            if (!hKernel32) {
                const DWORD err = GetLastError();
                Core::Logger::Error("ProcessInjector: 获取 Kernel32 句柄失败, err=" + std::to_string(err));
                setFailureReason("GetModuleHandleW(kernel32.dll) failed, err=" + std::to_string(err));
                VirtualFreeEx(hProcess, remoteDllPath, 0, MEM_RELEASE);
                return false;
            }

            LPTHREAD_START_ROUTINE loadLibraryAddr =
                (LPTHREAD_START_ROUTINE)GetProcAddress(hKernel32, "LoadLibraryW");
            if (!loadLibraryAddr) {
                const DWORD err = GetLastError();
                Core::Logger::Error("ProcessInjector: 获取 LoadLibraryW 地址失败, err=" + std::to_string(err));
                setFailureReason("GetProcAddress(LoadLibraryW) failed, err=" + std::to_string(err));
                VirtualFreeEx(hProcess, remoteDllPath, 0, MEM_RELEASE);
                return false;
            }

            // 步骤 4: 创建远程线程调用 LoadLibraryW
            HANDLE hThread = CreateRemoteThread(
                hProcess,
                NULL,
                0,
                loadLibraryAddr,
                remoteDllPath,
                0,
                NULL
            );

            if (!hThread) {
                const DWORD err = GetLastError();
                Core::Logger::Error("ProcessInjector: 创建远程线程失败 (CreateRemoteThread failed), err=" + std::to_string(err));
                setFailureReason("CreateRemoteThread failed, err=" + std::to_string(err));
                VirtualFreeEx(hProcess, remoteDllPath, 0, MEM_RELEASE);
                return false;
            }

            // 步骤 5: 等待注入完成
            DWORD waitRc = WaitForSingleObject(hThread, 5000); // 最多等待 5 秒

            bool ok = false;
            DWORD exitCode = 0;

            if (waitRc == WAIT_OBJECT_0) {
                // 注意：GetExitCodeThread 返回 DWORD；在 64 位进程中我们仅用它判断是否为 0（NULL）
                if (GetExitCodeThread(hThread, &exitCode)) {
                    ok = (exitCode != 0 && exitCode != STILL_ACTIVE);
                    if (!ok) {
                        setFailureReason("LoadLibraryW returned 0");
                    }
                } else {
                    DWORD err = GetLastError();
                    setFailureReason("GetExitCodeThread failed, err=" + std::to_string(err));
                    Core::Logger::Warn("ProcessInjector: 获取远程线程退出码失败 (GetExitCodeThread failed), err=" + std::to_string(err));
                }
            } else if (waitRc == WAIT_TIMEOUT) {
                // 超时意味着远程线程可能仍在读取 remoteDllPath；此时释放远程内存可能导致 UAF
                setFailureReason("WaitForSingleObject timeout(5000ms)");
                Core::Logger::Warn("ProcessInjector: 等待远程线程超时(5000ms)，注入结果未知；为避免 UAF 将不释放远程路径内存");
            } else {
                DWORD err = GetLastError();
                setFailureReason("WaitForSingleObject failed, rc=" + std::to_string(waitRc) + ", err=" + std::to_string(err));
                Core::Logger::Error("ProcessInjector: 等待远程线程失败 (WaitForSingleObject failed), rc=" + std::to_string(waitRc) +
                                    ", err=" + std::to_string(err));
            }

            // 清理：句柄必须关闭；远程路径内存仅在远程线程结束后释放，避免 UAF
            CloseHandle(hThread);
            if (waitRc == WAIT_OBJECT_0) {
                VirtualFreeEx(hProcess, remoteDllPath, 0, MEM_RELEASE);
            }

            if (ok) {
                if (!VerifyReady(hProcess, dllPath)) {
                    setFailureReason("DLL loaded but proxy hooks not ready");
                    return false;
                }
                setFailureReason("");
                Core::Logger::Info("ProcessInjector: 注入成功 (LoadLibraryW 返回非空)");
                return true;
            }
            if (waitRc == WAIT_OBJECT_0) {
                Core::Logger::Error("ProcessInjector: 注入失败 (LoadLibraryW 返回 0)");
            }
            if (failureReason && failureReason->empty()) {
                *failureReason = "unknown injector failure";
            }
            return false;
        }

        // 获取当前 DLL 的完整路径
        static std::wstring GetCurrentDllPath() {
            wchar_t path[MAX_PATH];
            HMODULE hModule = NULL;

            // 获取当前 DLL 的句柄
            GetModuleHandleExW(
                GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                (LPCWSTR)&GetCurrentDllPath,
                &hModule
            );

            if (hModule && GetModuleFileNameW(hModule, path, MAX_PATH)) {
                return std::wstring(path);
            }

            return L"";
        }
    };
}
