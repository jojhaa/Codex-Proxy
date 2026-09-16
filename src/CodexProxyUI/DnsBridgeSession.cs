using System.Diagnostics;
using System.IO;

namespace CodexProxyUI;

internal sealed class DnsBridgeSession : IDisposable
{
    private readonly Process process;
    private bool transferred;
    public string ProxyUrl { get; }
    public DnsBridgeSession(string configPath)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "codex_dns_bridge.exe")) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardInput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(Path.GetFullPath(configPath));
        process = Process.Start(start) ?? throw new IOException("无法启动域名 DNS 转发服务。");
        try {
            var output = process.StandardOutput.ReadLineAsync();
            if (!output.Wait(10000) || !int.TryParse(output.Result, out int port) || port is < 1 or > 65535)
                throw new IOException("域名 DNS 转发服务未就绪。");
            ProxyUrl = $"http://127.0.0.1:{port}";
        } catch { process.StandardInput.Close(); process.Dispose(); throw; }
    }
    public void Follow(Process target)
    {
        process.StandardInput.WriteLine($"{target.Id}:{target.StartTime.ToUniversalTime().Ticks}");
        process.StandardInput.Flush();
        process.StandardInput.Close();
        var ready = process.StandardOutput.ReadLineAsync();
        if (!ready.Wait(5000) || ready.Result != "READY") throw new IOException("DNS 转发服务未绑定目标进程。");
        transferred = true;
    }
    public void Dispose()
    {
        if (!transferred) process.StandardInput.Close();
        process.Dispose();
    }
}
