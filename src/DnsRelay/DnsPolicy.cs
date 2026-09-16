using System.Net;
using System.IO;
using System.Text.Json;

namespace CodexProxy.Dns;

public sealed class DnsPolicy
{
    // 精确根域与子域分别列出，避免 openai.com.evil 或 notopenai.com 误命中。
    public static readonly string[] DefaultDomains = ["openai.com", "*.openai.com", "chatgpt.com", "*.chatgpt.com",
        "oaistatic.com", "*.oaistatic.com", "oaiusercontent.com", "*.oaiusercontent.com"];
    private readonly string[] domains;
    public DnsPolicy(IEnumerable<string> domains)
    {
        this.domains = domains.Select(x => x.Trim().ToLowerInvariant().TrimEnd('.')).Distinct().ToArray();
        if (this.domains.Length > 128 || this.domains.Any(x => {
            var name = x.StartsWith("*.") ? x[2..] : x;
            return name.Length == 0 || name.Contains('*') || !name.Contains('.') ||
                Uri.CheckHostName(name) != UriHostNameType.Dns || name.Any(c => c > 127);
        })) throw new ArgumentException("远端 DNS 名单只允许 ASCII 域名或 *.域名，最多 128 项。");
    }
    public bool UseRemote(string host)
    {
        host = host.ToLowerInvariant().TrimEnd('.').Trim('[', ']');
        if (IPAddress.TryParse(host, out _)) return false;
        return domains.Any(pattern => pattern.StartsWith("*.")
            ? host.Length > pattern.Length - 1 && host.EndsWith(pattern[1..], StringComparison.Ordinal)
            : host == pattern);
    }
    public async Task<string[]> Targets(string host,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve, CancellationToken ct)
    {
        host = host.Trim('[', ']').TrimEnd('.');
        if (IPAddress.TryParse(host, out var ip)) return [ip.ToString()];
        if (UseRemote(host)) return [host];
        var addresses = await resolve(host, ct);
        if (addresses.Length == 0) throw new IOException("本地 DNS 没有返回地址。");
        // 不把本地解析失败升级为远端解析。
        return addresses.Select(x => x.ToString()).Distinct().ToArray();
    }
}

public sealed record RelaySettings(string Host, int Port, string Type, DnsPolicy Policy)
{
    public static RelaySettings Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var proxy = doc.RootElement.GetProperty("proxy");
        var host = proxy.GetProperty("host").GetString() ?? "";
        var port = proxy.GetProperty("port").GetInt32();
        var type = (proxy.GetProperty("type").GetString() ?? "").ToLowerInvariant();
        if (Uri.CheckHostName(host.Trim('[', ']')) == UriHostNameType.Unknown || port is < 1 or > 65535 ||
            type is not ("http" or "socks5")) throw new ArgumentException("上游代理配置无效。");
        var domains = proxy.TryGetProperty("remote_dns_domains", out var list)
            ? list.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : DnsPolicy.DefaultDomains;
        return new(host.Trim('[', ']'), port, type, new DnsPolicy(domains));
    }
}
