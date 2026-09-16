using System.Diagnostics;
using System.Net;

namespace CodexProxyUI;

internal static class LaunchSafety
{
    public static string ProxyUrl(string type, string host, string portText)
    {
        host = host.Trim().Trim('[', ']');
        if (type is not ("http" or "socks5") || Uri.CheckHostName(host) == UriHostNameType.Unknown ||
            !int.TryParse(portText, out int port) || port is < 1 or > 65535)
            throw new ArgumentException("请输入有效代理主机及 1–65535 的端口。");
        if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            host = $"[{host}]";
        // Chromium SOCKS5 始终由代理解析目标 DNS，不接受 socks5h 协议名。
        return $"{type}://{host}:{port}";
    }

    public static bool LoopbackConfirmed(bool completed, int exitCode, string output, string family) =>
        completed && exitCode == 0 && output.Split('\n').Any(line =>
            line.TrimEnd().EndsWith(": " + family, StringComparison.OrdinalIgnoreCase));

    public static Process? TrackNewProcess(int pid, DateTime requestedAt)
    {
        var process = Process.GetProcessById(pid);
        try
        {
            // 保留进程句柄绑定身份，拒绝接管激活接口返回的已有实例。
            _ = process.Handle;
            if (!process.HasExited && process.StartTime.ToUniversalTime() >= requestedAt) return process;
        }
        catch { process.Dispose(); throw; }
        process.Dispose();
        return null;
    }

    public static bool RequestClose(Process? process) =>
        process != null && !process.HasExited && process.CloseMainWindow();
}
