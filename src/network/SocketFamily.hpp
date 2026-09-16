#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif

#include <winsock2.h>
#include <ws2tcpip.h>

namespace Network {

enum class DualStackResult {
    NotApplicable,
    AlreadyEnabled,
    Enabled,
    Failed,
};

inline bool IsIpv4MappedAddress(const sockaddr_in6& address) {
    return IN6_IS_ADDR_V4MAPPED(&address.sin6_addr) != 0;
}

// Windows 默认创建 IPv6-only socket。代理模式需要在 bind 前关闭 IPV6_V6ONLY，
// 才能把 IPv4 代理端点表示为 v4-mapped IPv6 地址。
inline DualStackResult EnsureIpv6DualStack(SOCKET socket, int addressFamily, int* errorCode = nullptr) {
    if (errorCode) *errorCode = 0;
    if (socket == INVALID_SOCKET || addressFamily != AF_INET6) {
        return DualStackResult::NotApplicable;
    }

    DWORD v6Only = 1;
    int optionLength = static_cast<int>(sizeof(v6Only));
    if (getsockopt(
            socket,
            IPPROTO_IPV6,
            IPV6_V6ONLY,
            reinterpret_cast<char*>(&v6Only),
            &optionLength) == SOCKET_ERROR) {
        if (errorCode) *errorCode = WSAGetLastError();
        return DualStackResult::Failed;
    }
    if (v6Only == 0) return DualStackResult::AlreadyEnabled;

    const DWORD disabled = 0;
    if (setsockopt(
            socket,
            IPPROTO_IPV6,
            IPV6_V6ONLY,
            reinterpret_cast<const char*>(&disabled),
            static_cast<int>(sizeof(disabled))) == SOCKET_ERROR) {
        if (errorCode) *errorCode = WSAGetLastError();
        return DualStackResult::Failed;
    }
    return DualStackResult::Enabled;
}

}  // namespace Network
