#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif

#include "UpdateChecker.hpp"

#include <windows.h>
#include <winhttp.h>

#include <algorithm>
#include <atomic>
#include <cctype>
#include <cstdint>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

#include <nlohmann/json.hpp>

#include "../core/Config.hpp"
#include "../core/Logger.hpp"

namespace {
    constexpr const char* kLatestReleaseApiUrl = "https://api.github.com/repos/yuaotian/antigravity-proxy/releases/latest";
    constexpr const char* kDefaultReleasePage = "https://github.com/yuaotian/antigravity-proxy/releases/latest";
    constexpr DWORD kMaxResponseBytes = 1024 * 1024;

    std::atomic<bool> g_started{false};

    struct LatestReleaseInfo {
        std::string tagName;
        std::string name;
        std::string htmlUrl;
        std::string body;
    };

    static std::wstring Utf8ToWide(const std::string& input) {
        if (input.empty()) return L"";
        int len = MultiByteToWideChar(CP_UTF8, 0, input.c_str(), -1, nullptr, 0);
        if (len <= 0) return L"";
        std::wstring result((size_t)len, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, input.c_str(), -1, &result[0], len);
        if (!result.empty() && result.back() == L'\0') result.pop_back();
        return result;
    }

    static std::string TrimAscii(std::string s) {
        auto isWs = [](unsigned char c) { return std::isspace(c) != 0; };
        size_t begin = 0;
        while (begin < s.size() && isWs((unsigned char)s[begin])) begin++;
        size_t end = s.size();
        while (end > begin && isWs((unsigned char)s[end - 1])) end--;
        return s.substr(begin, end - begin);
    }

    static std::string NormalizeVersionText(std::string version) {
        version = TrimAscii(version);
        if (!version.empty() && (version[0] == 'v' || version[0] == 'V')) {
            version.erase(version.begin());
        }
        return version;
    }

    static std::vector<int> ParseVersionParts(std::string version) {
        version = NormalizeVersionText(version);
        std::vector<int> parts;
        size_t i = 0;
        while (i < version.size()) {
            while (i < version.size() && !std::isdigit((unsigned char)version[i])) i++;
            if (i >= version.size()) break;
            int value = 0;
            bool any = false;
            while (i < version.size() && std::isdigit((unsigned char)version[i])) {
                any = true;
                value = value * 10 + (version[i] - '0');
                i++;
            }
            if (any) parts.push_back(value);
            if (parts.size() >= 4) break;
        }
        return parts;
    }

    static int CompareVersions(const std::string& current, const std::string& latest) {
        const auto a = ParseVersionParts(current);
        const auto b = ParseVersionParts(latest);
        if (a.empty() || b.empty()) return 0;
        const size_t n = (std::max)(a.size(), b.size());
        for (size_t i = 0; i < n; ++i) {
            const int av = i < a.size() ? a[i] : 0;
            const int bv = i < b.size() ? b[i] : 0;
            if (av < bv) return -1;
            if (av > bv) return 1;
        }
        return 0;
    }

    static std::string TruncateForDialog(std::string text, size_t maxLen) {
        text = TrimAscii(text);
        if (text.size() <= maxLen) return text;
        return text.substr(0, maxLen) + "\n...";
    }

    static std::string JoinMirrorUrl(std::string mirror, const std::string& targetUrl) {
        mirror = TrimAscii(mirror);
        if (mirror.empty()) return "";
        if (mirror.back() != '/') mirror.push_back('/');
        return mirror + targetUrl;
    }

    static std::string GetUpdateNotifyMarkerPath() {
        const std::string logDir = Core::Logger::GetLogDirectoryPath();
        if (logDir.empty()) return "";
        return logDir + "\\update-notify.marker";
    }

    static bool HasNotifiedRelease(const std::string& tagName) {
        const auto& config = Core::Config::Instance();
        if (!config.updates.notify_once) return false;
        const std::string markerPath = GetUpdateNotifyMarkerPath();
        if (markerPath.empty()) return false;
        std::ifstream f(markerPath);
        if (!f.is_open()) return false;
        std::string saved;
        std::getline(f, saved);
        return TrimAscii(saved) == TrimAscii(tagName);
    }

    static void MarkReleaseNotified(const std::string& tagName) {
        const auto& config = Core::Config::Instance();
        if (!config.updates.notify_once) return;
        const std::string markerPath = GetUpdateNotifyMarkerPath();
        if (markerPath.empty()) return;
        std::ofstream f(markerPath, std::ios::out | std::ios::trunc);
        if (f.is_open()) {
            f << TrimAscii(tagName) << "\n";
        }
    }

    static bool ReadWinHttpResponse(HINTERNET request, std::string* out) {
        if (!out) return false;
        out->clear();
        for (;;) {
            DWORD available = 0;
            if (!WinHttpQueryDataAvailable(request, &available)) {
                Core::Logger::Warn("[更新] 查询响应数据失败, err=" + std::to_string(GetLastError()));
                return false;
            }
            if (available == 0) break;
            if (out->size() + available > kMaxResponseBytes) {
                Core::Logger::Warn("[更新] GitHub Release 响应过大，已放弃解析");
                return false;
            }
            std::string chunk(available, '\0');
            DWORD read = 0;
            if (!WinHttpReadData(request, &chunk[0], available, &read)) {
                Core::Logger::Warn("[更新] 读取响应数据失败, err=" + std::to_string(GetLastError()));
                return false;
            }
            chunk.resize(read);
            out->append(chunk);
        }
        return true;
    }

    static bool QueryHttpsJson(const std::string& url, bool allowInsecureCertificate, const std::string& sourceName, std::string* responseBody) {
        const auto& config = Core::Config::Instance();
        const std::wstring wideUrl = Utf8ToWide(url);
        if (wideUrl.empty()) return false;

        URL_COMPONENTS parts{};
        parts.dwStructSize = sizeof(parts);
        parts.dwSchemeLength = (DWORD)-1;
        parts.dwHostNameLength = (DWORD)-1;
        parts.dwUrlPathLength = (DWORD)-1;
        parts.dwExtraInfoLength = (DWORD)-1;
        if (!WinHttpCrackUrl(wideUrl.c_str(), 0, 0, &parts) ||
            parts.nScheme != INTERNET_SCHEME_HTTPS ||
            parts.dwHostNameLength == 0) {
            Core::Logger::Warn("[更新] URL 解析失败或非 HTTPS: " + sourceName);
            return false;
        }

        const std::wstring host(parts.lpszHostName, parts.dwHostNameLength);
        std::wstring path(parts.lpszUrlPath ? std::wstring(parts.lpszUrlPath, parts.dwUrlPathLength) : L"/");
        if (parts.lpszExtraInfo && parts.dwExtraInfoLength > 0) {
            path.append(parts.lpszExtraInfo, parts.dwExtraInfoLength);
        }
        if (path.empty()) path = L"/";

        HINTERNET session = WinHttpOpen(
            L"antigravity-proxy-update-check/1.0",
            WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
            WINHTTP_NO_PROXY_NAME,
            WINHTTP_NO_PROXY_BYPASS,
            0
        );
        if (!session) {
            Core::Logger::Warn("[更新] WinHttpOpen 失败, err=" + std::to_string(GetLastError()));
            return false;
        }

        WinHttpSetTimeouts(
            session,
            config.updates.timeout_ms,
            config.updates.timeout_ms,
            config.updates.timeout_ms,
            config.updates.timeout_ms
        );

        HINTERNET connect = WinHttpConnect(session, host.c_str(), parts.nPort, 0);
        if (!connect) {
            Core::Logger::Warn("[更新] WinHttpConnect 失败, source=" + sourceName + ", err=" + std::to_string(GetLastError()));
            WinHttpCloseHandle(session);
            return false;
        }

        HINTERNET request = WinHttpOpenRequest(
            connect,
            L"GET",
            path.c_str(),
            nullptr,
            WINHTTP_NO_REFERER,
            WINHTTP_DEFAULT_ACCEPT_TYPES,
            WINHTTP_FLAG_SECURE
        );
        if (!request) {
            Core::Logger::Warn("[更新] WinHttpOpenRequest 失败, err=" + std::to_string(GetLastError()));
            WinHttpCloseHandle(connect);
            WinHttpCloseHandle(session);
            return false;
        }

        if (allowInsecureCertificate) {
            // 仅用于用户显式配置的 GitHub 代理站，容忍代理站证书链异常；直连 GitHub 不使用该放宽策略。
            DWORD flags =
                SECURITY_FLAG_IGNORE_UNKNOWN_CA |
                SECURITY_FLAG_IGNORE_CERT_CN_INVALID |
                SECURITY_FLAG_IGNORE_CERT_DATE_INVALID |
                SECURITY_FLAG_IGNORE_CERT_WRONG_USAGE;
            WinHttpSetOption(request, WINHTTP_OPTION_SECURITY_FLAGS, &flags, sizeof(flags));
        }

        const wchar_t* headers =
            L"Accept: application/vnd.github+json\r\n"
            L"X-GitHub-Api-Version: 2022-11-28\r\n"
            L"User-Agent: antigravity-proxy-update-check\r\n";

        bool ok = WinHttpSendRequest(
            request,
            headers,
            (DWORD)-1L,
            WINHTTP_NO_REQUEST_DATA,
            0,
            0,
            0
        ) != FALSE;
        if (ok) {
            ok = WinHttpReceiveResponse(request, nullptr) != FALSE;
        }
        if (!ok) {
            Core::Logger::Warn("[更新] 请求 Release API 失败, source=" + sourceName + ", err=" + std::to_string(GetLastError()));
            WinHttpCloseHandle(request);
            WinHttpCloseHandle(connect);
            WinHttpCloseHandle(session);
            return false;
        }

        DWORD status = 0;
        DWORD statusSize = sizeof(status);
        if (!WinHttpQueryHeaders(
                request,
                WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
                WINHTTP_HEADER_NAME_BY_INDEX,
                &status,
                &statusSize,
                WINHTTP_NO_HEADER_INDEX) ||
            status != 200) {
            Core::Logger::Warn("[更新] GitHub Release API 状态码异常: " + std::to_string(status));
            WinHttpCloseHandle(request);
            WinHttpCloseHandle(connect);
            WinHttpCloseHandle(session);
            return false;
        }

        ok = ReadWinHttpResponse(request, responseBody);
        WinHttpCloseHandle(request);
        WinHttpCloseHandle(connect);
        WinHttpCloseHandle(session);
        return ok;
    }

    static bool QueryLatestReleaseJson(std::string* responseBody) {
        if (QueryHttpsJson(kLatestReleaseApiUrl, false, "github", responseBody)) {
            Core::Logger::Info("[更新] 已通过 GitHub 直连获取 Release 信息");
            return true;
        }

        const auto& config = Core::Config::Instance();
        for (const auto& mirror : config.updates.mirrors) {
            const std::string mirrorUrl = JoinMirrorUrl(mirror, kLatestReleaseApiUrl);
            if (mirrorUrl.empty()) continue;
            const std::string sourceName = "mirror=" + mirror;
            if (QueryHttpsJson(mirrorUrl, config.updates.allow_insecure_mirrors, sourceName, responseBody)) {
                Core::Logger::Info("[更新] 已通过 GitHub 代理站获取 Release 信息: " + mirror);
                return true;
            }
        }
        return false;
    }

    static bool ParseLatestRelease(const std::string& body, LatestReleaseInfo* out) {
        if (!out) return false;
        auto json = nlohmann::json::parse(body, nullptr, false);
        if (json.is_discarded() || !json.is_object()) {
            Core::Logger::Warn("[更新] GitHub Release JSON 解析失败");
            return false;
        }
        out->tagName = json.value("tag_name", "");
        out->name = json.value("name", "");
        out->htmlUrl = json.value("html_url", "");
        out->body = json.value("body", "");
        if (out->tagName.empty()) {
            Core::Logger::Warn("[更新] GitHub Release JSON 缺少 tag_name");
            return false;
        }
        if (out->htmlUrl.empty()) {
            out->htmlUrl = kDefaultReleasePage;
        }
        return true;
    }

    static void OpenReleasePage(const std::string& url) {
        const std::wstring wideUrl = Utf8ToWide(url.empty() ? kDefaultReleasePage : url);
        if (wideUrl.empty()) return;
        HMODULE shell32 = LoadLibraryW(L"shell32.dll");
        if (!shell32) return;
        using ShellExecuteWFn = HINSTANCE (WINAPI*)(HWND, LPCWSTR, LPCWSTR, LPCWSTR, LPCWSTR, INT);
        auto shellExecuteW = reinterpret_cast<ShellExecuteWFn>(GetProcAddress(shell32, "ShellExecuteW"));
        if (shellExecuteW) {
            shellExecuteW(nullptr, L"open", wideUrl.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
        }
        FreeLibrary(shell32);
    }

    static void ShowUpdateDialog(const LatestReleaseInfo& release, const std::string& currentVersion) {
        HMODULE user32 = LoadLibraryW(L"user32.dll");
        if (!user32) return;
        using MessageBoxWFn = int (WINAPI*)(HWND, LPCWSTR, LPCWSTR, UINT);
        auto messageBoxW = reinterpret_cast<MessageBoxWFn>(GetProcAddress(user32, "MessageBoxW"));
        if (!messageBoxW) {
            FreeLibrary(user32);
            return;
        }

        std::ostringstream oss;
        oss << "检测到 Antigravity-Proxy 新版本。\n\n"
            << "当前版本: " << currentVersion << "\n"
            << "最新版本: " << release.tagName << "\n";
        if (!release.name.empty() && release.name != release.tagName) {
            oss << "发布名称: " << release.name << "\n";
        }
        if (!release.body.empty()) {
            oss << "\n发布说明:\n" << TruncateForDialog(release.body, 900) << "\n";
        }
        oss << "\n是否打开 GitHub Release 页面下载？";

        const std::wstring message = Utf8ToWide(oss.str());
        const int result = messageBoxW(
            nullptr,
            message.c_str(),
            L"Antigravity-Proxy 更新提示",
            MB_YESNO | MB_ICONINFORMATION | MB_TOPMOST | MB_SETFOREGROUND
        );
        FreeLibrary(user32);

        if (result == IDYES) {
            OpenReleasePage(release.htmlUrl.empty() ? kDefaultReleasePage : release.htmlUrl);
        }
    }

    static DWORD WINAPI UpdateCheckThreadProc(LPVOID) {
        const auto& config = Core::Config::Instance();
        const int delayMs = config.updates.check_delay_ms > 0 ? config.updates.check_delay_ms : 15000;
        Sleep((DWORD)delayMs);

        Core::Logger::Info("[更新] 后台 GitHub Release 检查已启动");
        std::string body;
        if (!QueryLatestReleaseJson(&body)) {
            Core::Logger::Info("[更新] 未能获取 GitHub Release 信息，本次静默跳过");
            return 0;
        }

        LatestReleaseInfo release;
        if (!ParseLatestRelease(body, &release)) {
            Core::Logger::Info("[更新] Release 信息解析失败，本次静默跳过");
            return 0;
        }

        const std::string currentVersion = NormalizeVersionText(config.buildVersion.empty() ? "0" : config.buildVersion);
        const int cmp = CompareVersions(currentVersion, release.tagName);
        Core::Logger::Info("[更新] 当前版本=" + currentVersion + ", 最新版本=" + release.tagName);
        if (cmp >= 0) {
            return 0;
        }
        if (HasNotifiedRelease(release.tagName)) {
            Core::Logger::Info("[更新] 新版本 " + release.tagName + " 已提醒过，本次不重复弹窗");
            return 0;
        }

        ShowUpdateDialog(release, currentVersion);
        MarkReleaseNotified(release.tagName);
        return 0;
    }
}

namespace UpdateChecker {
    void StartAsync() {
        const auto& config = Core::Config::Instance();
        if (!config.updates.enabled) {
            if (Core::Logger::IsEnabled(Core::LogLevel::Debug)) {
                Core::Logger::Debug("[更新] 自动更新检查未启用。如需开启，请设置 updates.enabled=true。");
            }
            return;
        }
        bool expected = false;
        if (!g_started.compare_exchange_strong(expected, true)) {
            return;
        }

        HANDLE hThread = CreateThread(nullptr, 0, UpdateCheckThreadProc, nullptr, 0, nullptr);
        if (hThread) {
            CloseHandle(hThread);
        } else {
            g_started.store(false);
            Core::Logger::Warn("[更新] 启动后台更新检查线程失败, err=" + std::to_string(GetLastError()));
        }
    }
}
