using CodexProxy.Dns;
using System.Diagnostics;

try {
    if (args.Length != 1) return 2;
    await using var server = new RelayServer(RelaySettings.Load(args[0]));
    server.Start();
    Console.WriteLine(server.Port);
    // 启动方通过私有标准输入授予目标进程生命周期；EOF/启动失败自动退出。
    var lease = await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60));
    var fields = lease?.Split(':');
    if (fields?.Length != 2 || !int.TryParse(fields[0], out var pid) || !long.TryParse(fields[1], out var ticks)) return 0;
    using var target = Process.GetProcessById(pid);
    _ = target.Handle;
    if (target.StartTime.ToUniversalTime().Ticks != ticks) return 0;
    Console.WriteLine("READY");
    await target.WaitForExitAsync();
    return 0;
} catch { Console.Error.WriteLine("DNS 转发服务启动或运行失败。"); return 1; }
