#pragma once
#include <winsock2.h>
#include <ws2tcpip.h>
#include <string>
#include <memory>
#include "../core/Logger.hpp"

namespace Network {
    class SocketWrapper {
        SOCKET m_sock;
    public:
        SocketWrapper(SOCKET sock) : m_sock(sock) {}
        ~SocketWrapper() {
            // Do not close socket here as we don't own it in hooks
        }

        void SetTimeouts(int recv_ms, int send_ms) {
            if (m_sock == INVALID_SOCKET) return;
            setsockopt(m_sock, SOL_SOCKET, SO_RCVTIMEO, (const char*)&recv_ms, sizeof(recv_ms));
            setsockopt(m_sock, SOL_SOCKET, SO_SNDTIMEO, (const char*)&send_ms, sizeof(send_ms));
        }

        static bool RedirectToProxy(sockaddr_in* addr, const std::string& proxyHost, int proxyPort) {
            if (!addr) return false;
            // 优先直接解析 IPv4 字符串，失败时尝试 DNS 解析
            if (inet_pton(AF_INET, proxyHost.c_str(), &addr->sin_addr) != 1) {
                addrinfo hints{};
                hints.ai_family = AF_INET;
                hints.ai_socktype = SOCK_STREAM;
                hints.ai_protocol = IPPROTO_TCP;
                addrinfo* res = nullptr;
                int rc = getaddrinfo(proxyHost.c_str(), nullptr, &hints, &res);
                if (rc != 0 || !res) return false;
                auto* resolved = (sockaddr_in*)res->ai_addr;
                addr->sin_addr = resolved->sin_addr;
                freeaddrinfo(res);
            }
            addr->sin_port = htons(proxyPort);
            return true;
        }
    };
}
