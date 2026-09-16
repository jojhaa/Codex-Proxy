#pragma once
#include <string>
#include <vector>
#include <algorithm>
#include <cctype>

namespace Core {
inline std::vector<std::string> DefaultRemoteDnsDomains() {
    return {"openai.com", "*.openai.com", "chatgpt.com", "*.chatgpt.com", "oaistatic.com", "*.oaistatic.com", "oaiusercontent.com", "*.oaiusercontent.com"};
}
inline bool UsesRemoteDns(std::string host, const std::vector<std::string>& domains) {
    auto normalize = [](std::string s) {
        std::transform(s.begin(), s.end(), s.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        while (!s.empty() && s.back() == '.') s.pop_back();
        return s;
    };
    host = normalize(host);
    for (auto pattern : domains) {
        pattern = normalize(pattern);
        if (pattern.compare(0, 2, "*.") == 0) {
            const auto suffix = pattern.substr(1);
            if (host.size() > suffix.size() && host.compare(host.size()-suffix.size(), suffix.size(), suffix) == 0) return true;
        } else if (!pattern.empty() && host == pattern) return true;
    }
    return false;
}
}
