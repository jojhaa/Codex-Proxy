#pragma once

#include <string_view>

namespace Core {

enum class UdpAction {
    Block,
    Direct,
    Proxy,
};

// auto 在 SOCKS5 下优先使用 UDP Associate；其它代理类型按显式 fallback 决策。
// 这样既兼容 QUIC/图片流量，也不会在默认配置下静默直连。
inline UdpAction ResolveUdpAction(
    std::string_view udpMode,
    std::string_view proxyType,
    std::string_view udpFallback) {
    if (udpMode == "direct") return UdpAction::Direct;
    if (udpMode == "proxy") return UdpAction::Proxy;
    if (udpMode == "auto") {
        if (proxyType == "socks5") return UdpAction::Proxy;
        return udpFallback == "direct" ? UdpAction::Direct : UdpAction::Block;
    }
    return UdpAction::Block;
}

inline const char* UdpActionName(UdpAction action) {
    switch (action) {
    case UdpAction::Direct:
        return "direct";
    case UdpAction::Proxy:
        return "proxy";
    case UdpAction::Block:
    default:
        return "block";
    }
}

}  // namespace Core
