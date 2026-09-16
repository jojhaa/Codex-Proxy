using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace CodexProxyUI;

internal static class ClientStart
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackagesByPackageFamily(string family, ref uint count, IntPtr names,
        ref uint length, IntPtr buffer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackagePathByFullName(string name, ref uint length, StringBuilder? path);

    public static string FindExecutable(string family)
    {
        var candidates = new List<string>();
        uint count = 0, length = 0;
        int result = GetPackagesByPackageFamily(family, ref count, IntPtr.Zero, ref length, IntPtr.Zero);
        if (result == 122 && count > 0 && count <= 256 && length <= 1048576) {
            IntPtr names = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size));
            IntPtr buffer = Marshal.AllocHGlobal(checked((int)length * 2));
            try {
                if (GetPackagesByPackageFamily(family, ref count, names, ref length, buffer) == 0) {
                    for (int i = 0; i < count; i++) {
                        string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(names, i * IntPtr.Size))!;
                        uint pathLength = 0;
                        if (GetPackagePathByFullName(name, ref pathLength, null) != 122) continue;
                        var path = new StringBuilder(checked((int)pathLength));
                        if (GetPackagePathByFullName(name, ref pathLength, path) != 0) continue;
                        string root = path.ToString();
                        string manifest = Path.Combine(root, "AppxManifest.xml");
                        if (!File.Exists(manifest)) continue;
                        var app = XDocument.Load(manifest).Descendants().SingleOrDefault(x =>
                            x.Name.LocalName == "Application" && (string?)x.Attribute("Id") == "App");
                        string? relative = (string?)app?.Attribute("Executable");
                        if (string.IsNullOrWhiteSpace(relative)) continue;
                        string exe = Path.GetFullPath(Path.Combine(root, relative));
                        if (exe.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase) && File.Exists(exe) &&
                            Path.GetFileName(exe).Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase)) candidates.Add(exe);
                    }
                }
            } finally { Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(names); }
        }

        if (candidates.Count == 0) {
            string winApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            if (Directory.Exists(winApps)) {
                try {
                    foreach (var dir in Directory.GetDirectories(winApps, "OpenAI.Codex_*")) {
                        string exe = Path.Combine(dir, "app", "ChatGPT.exe");
                        if (File.Exists(exe)) candidates.Add(exe);
                    }
                } catch { }
            }
        }

        if (candidates.Count == 0) {
            string localExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ChatGPT", "ChatGPT.exe");
            if (File.Exists(localExe)) candidates.Add(localExe);
        }

        if (candidates.Count == 0)
            throw new IOException("未找到当前用户安装的 ChatGPT 客户端。请确认已在微软应用商店安装 ChatGPT。");

        if (candidates.Count > 1) {
            candidates = candidates.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(c => {
                try {
                    var vi = FileVersionInfo.GetVersionInfo(c);
                    if (vi.FileMajorPart > 0 || vi.FileMinorPart > 0)
                        return new Version(vi.FileMajorPart, vi.FileMinorPart, vi.FileBuildPart, vi.FilePrivatePart);
                } catch { }
                return new Version(0, 0, 0, 0);
            }).ThenByDescending(c => {
                try { return File.GetLastWriteTimeUtc(c); } catch { return DateTime.MinValue; }
            }).ToList();
        }

        return candidates[0];
    }

    public static string ResolveExecutable(string? specifiedPath)
    {
        if (!string.IsNullOrWhiteSpace(specifiedPath) &&
            !specifiedPath.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
            !specifiedPath.Contains("OpenAI.Codex", StringComparison.OrdinalIgnoreCase))
        {
            string expanded = Environment.ExpandEnvironmentVariables(specifiedPath.Trim('\"', ' '));
            if (File.Exists(expanded))
            {
                return Path.GetFullPath(expanded);
            }
            throw new IOException($"指定的客户端路径不存在: {specifiedPath}");
        }
        return FindExecutable("OpenAI.Codex_2p2nqsd0c76g0");
    }

    public static ProcessStartInfo Create(string executable, string proxyUrl)
    {
        var start = new ProcessStartInfo(executable) {
            UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        start.ArgumentList.Add("--proxy-server=" + proxyUrl);
        start.ArgumentList.Add("--proxy-bypass-list=<-loopback>;localhost;127.0.0.1;::1");
        foreach (string name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" })
            start.Environment[name] = proxyUrl;
        foreach (string name in new[] { "NO_PROXY", "no_proxy" }) start.Environment[name] = "localhost,127.0.0.1,::1";
        start.Environment["NODE_USE_ENV_PROXY"] = "1";
        return start;
    }

    public static bool IsRunning(string executable)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable))) {
            using (process) {
                try {
                    if (process.HasExited) continue;
                    if (string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) return true;
                } catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) {
                    throw new IOException("无法核对已有客户端身份，请先保存任务并手动退出客户端。");
                }
            }
        }
        return false;
    }

    public static Process StartIntercepted(string executable, string configPath)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "process");
        if (!string.Equals(Path.GetFullPath(configPath), Path.Combine(directory, "config.json"), StringComparison.OrdinalIgnoreCase))
            throw new IOException("进程代理配置必须位于管理器与 DLL 同一目录。");
        var start = new ProcessStartInfo(Path.Combine(directory, "codex_process_launcher.exe")) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(executable);
        // 拦截原始 socket，不叠加环境代理或 Chromium 代理参数。
        foreach (string key in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "all_proxy", "no_proxy", "NODE_USE_ENV_PROXY" })
            start.Environment.Remove(key);
        using var helper = Process.Start(start) ?? throw new IOException("无法启动进程代理加载器。");
        var output = helper.StandardOutput.ReadToEndAsync();
        var error = helper.StandardError.ReadToEndAsync();
        if (!helper.WaitForExit(20000)) throw new IOException("进程代理加载超时，请查看本地日志。");
        if (!output.Wait(2000) || !error.Wait(2000) || helper.ExitCode != 0)
            throw new IOException("进程代理加载失败，未放行新的客户端。" + (error.IsCompletedSuccessfully ? error.Result : ""));
        string? pidLine = output.Result.Split('\n').LastOrDefault(x => x.StartsWith("PROXY_PID="));
        if (pidLine == null || !int.TryParse(pidLine[10..].Trim(), out int pid)) throw new IOException("加载器未返回目标进程身份。");
        var process = Process.GetProcessById(pid);
        _ = process.Handle;
        return process;
    }
}
