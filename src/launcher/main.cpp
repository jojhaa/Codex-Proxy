#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <filesystem>
#include <iostream>
#include <string>
#include <vector>

// 共用 UI 启动链路，确保命令行入口也经过域名 DNS 策略转发器。
int main() {
    SetConsoleOutputCP(CP_UTF8);
    wchar_t path[32768] = {};
    DWORD length = GetModuleFileNameW(nullptr, path, 32768);
    if (!length || length >= 32768) return 1;
    auto ui = std::filesystem::path(path).parent_path() / L"ProcWeaver.exe";
    if (!std::filesystem::is_regular_file(ui)) {
        ui = std::filesystem::path(path).parent_path() / L"CodexProxyUI.exe";
    }
    if (!std::filesystem::is_regular_file(ui)) {
        std::cerr << "缺少 ProcWeaver.exe，请使用完整验证包。\n"; return 1;
    }
    std::wstring command = L"\"" + ui.wstring() + L"\" --launch";
    std::vector<wchar_t> buffer(command.begin(), command.end()); buffer.push_back(0);
    STARTUPINFOW startup{}; startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(ui.c_str(), buffer.data(), nullptr, nullptr, FALSE, 0, nullptr,
                        ui.parent_path().c_str(), &startup, &process)) {
        std::cerr << "启动管理器失败，错误码=" << GetLastError() << "\n"; return 1;
    }
    CloseHandle(process.hThread); CloseHandle(process.hProcess);
    std::cout << "已打开管理器，代理与 DNS 状态请查看管理器日志。\n";
    return 0;
}
