// Copyright (c) Time Silent. SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

bool verifyOnly = args.Length == 1 && args[0] == "--verify-only";
try {
    if (args.Length != 0 && !verifyOnly) throw new ArgumentException("不支持的启动参数。");
    using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("ProcWeaver.Payload.zip")
        ?? throw new IOException("单文件包缺少组件资源。");
    string digest = Convert.ToHexString(SHA256.HashData(payload));
    payload.Position = 0;
    using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
    string parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProcWeaver", "packages");
    Directory.CreateDirectory(parent);
    string destination = Path.Combine(parent, "1.0.11-" + digest[..16]);
    using var mutex = new Mutex(false, @"Local\ProcWeaver.Package." + digest[..16]);
    bool locked;
    try { locked = mutex.WaitOne(TimeSpan.FromSeconds(60)); }
    catch (AbandonedMutexException) { locked = true; }
    if (!locked) throw new IOException("另一个实例正在准备组件，请稍后重试。");
    try {
        if (!Directory.Exists(destination)) {
            string staging = destination + ".staging-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(staging);
            try {
                foreach (var entry in zip.Entries) {
                    string target = ResolveEntry(staging, entry.FullName);
                    if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var input = entry.Open();
                    using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
                    input.CopyTo(output);
                }
                Directory.Move(staging, destination);
            } finally {
                // staging is an internally generated sibling; never remove existing user data.
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
        }
        foreach (var entry in zip.Entries) {
            if (entry.FullName.EndsWith('/')) continue;
            string target = ResolveEntry(destination, entry.FullName);
            if (!File.Exists(target)) throw new IOException("组件缺失：" + entry.FullName);
            // Runtime configuration belongs to the user and must never be overwritten.
            if (entry.FullName is "config.json" or "process/config.json") continue;
            using var expected = entry.Open();
            using var actual = File.OpenRead(target);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(expected), SHA256.HashData(actual)))
                throw new IOException("组件已修改或损坏：" + entry.FullName + "。请保留配置后重新解包。");
        }
    } finally { mutex.ReleaseMutex(); }
    if (verifyOnly) { Console.WriteLine(destination); return 0; }
    _ = Process.Start(new ProcessStartInfo(Path.Combine(destination, "ProcWeaver.exe")) {
        WorkingDirectory = destination, UseShellExecute = false
    }) ?? throw new IOException("无法启动管理器。");
    return 0;
} catch (Exception error) {
    if (verifyOnly) Console.Error.WriteLine(error.Message);
    else Native.MessageBoxW(0, error.Message, "ProcWeaver 启动失败", 0x10);
    return 1;
}

static string ResolveEntry(string root, string name) {
    string prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
    string target = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
    if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("组件路径越界。");
    return target;
}

static class Native {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int MessageBoxW(nint window, string text, string title, uint type);
}
