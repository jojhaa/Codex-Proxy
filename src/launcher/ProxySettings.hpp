#pragma once
#include <string>
#include <stdexcept>
#include <algorithm>
#include <cctype>

inline std::string BuildChromiumProxyUrl(std::string type, std::string host, int port) {
    std::transform(type.begin(), type.end(), type.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    if (type != "socks5" && type != "http" && type != "https") throw std::invalid_argument("代理类型必须为 socks5/http/https");
    if (host.empty() || host.find_first_of(" \t\r\n\"';/\\@") != std::string::npos || port < 1 || port > 65535)
        throw std::invalid_argument("代理主机或端口无效");
    if (host.find(':') != std::string::npos && host.front() != '[') host = "[" + host + "]";
    return type + "://" + host + ":" + std::to_string(port);
}
