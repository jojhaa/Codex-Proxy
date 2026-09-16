// Copyright (c) Time Silent. SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using System.Text.Json;
using CodexProxyUI;

try {
    if (args.Length < 1 || args[0] is not ("resolve" or "standard" or "process"))
        throw new ArgumentException("无效的客户端操作。");
    string? customPath = args.Length > 1 ? args[1] : null;
    string executable = ClientStart.ResolveExecutable(customPath);
    if (args[0] == "resolve") {
        Console.WriteLine("CODEX_CLIENT_RESULT=" + JsonSerializer.Serialize(new { path = executable, pid = 0 }));
        return 0;
    }
    if (ClientStart.IsRunning(executable))
        throw new IOException("客户端已在运行，请先保存任务并完全退出客户端后再启动。");
    using var bridge = args[0] == "standard"
        ? new DnsBridgeSession(Path.Combine(AppContext.BaseDirectory, "config.json")) : null;
    using var process = args[0] == "process"
        ? ClientStart.StartIntercepted(executable, Path.Combine(AppContext.BaseDirectory, "process", "config.json"))
        : Process.Start(ClientStart.Create(executable, bridge!.ProxyUrl))
            ?? throw new IOException("无法创建客户端进程。");
    bridge?.Follow(process);
    Console.WriteLine("CODEX_CLIENT_RESULT=" + JsonSerializer.Serialize(new { path = executable, pid = process.Id }));
    return 0;
} catch (Exception error) {
    Console.WriteLine("CODEX_CLIENT_RESULT=" + JsonSerializer.Serialize(new { error = error.Message }));
    return 1;
}
