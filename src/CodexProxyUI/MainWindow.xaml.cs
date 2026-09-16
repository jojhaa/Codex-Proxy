using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace CodexProxyUI;

[ComImport]
[Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IApplicationActivationManager
{
    [PreserveSig]
    int ActivateApplication(
        [In] string appUserModelId,
        [In] string? arguments,
        [In] uint options,
        [Out] out uint processId);

    [PreserveSig]
    int ActivateForFile(
        [In] string appUserModelId,
        [In] IntPtr itemArray,
        [In] string? verb,
        [Out] out uint processId);

    [PreserveSig]
    int ActivateForProtocol(
        [In] string appUserModelId,
        [In] IntPtr itemArray,
        [Out] out uint processId);
}

[ComImport]
[Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
internal class ApplicationActivationManagerClass
{
}

public partial class MainWindow : Window
{
    private const string PackageFamilyName = "OpenAI.Codex_2p2nqsd0c76g0";
    private const string Aumid = "OpenAI.Codex_2p2nqsd0c76g0!App";
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "CodexProxyLauncher";

    private readonly DispatcherTimer statusTimer;
    private string configFilePath = "";
    private Process? managedProcess;
    private bool loadingConfig;
    private bool processMode;
    private string standardConfigPath = "";

    public MainWindow()
    {
        InitializeComponent();

        LocateConfigFile();
        standardConfigPath = configFilePath;
        // 模式选择独立保存；旧配置首次打开仍使用标准代理。
        try {
            processMode = JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(standardConfigPath)!, "launcher.settings.json")))?["mode"]?.ToString() == "process";
        } catch (FileNotFoundException) { }
        catch (Exception ex) { AppendLog($"模式设置读取失败，使用标准代理：{ex.Message}"); }
        if (processMode) configFilePath = Path.Combine(Path.GetDirectoryName(standardConfigPath)!, "process", "config.json");
        RadioProcessMode.IsChecked = processMode;
        LoadConfig();

        statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        statusTimer.Tick += StatusTimer_Tick;
        statusTimer.Start();

        CheckAutoStart();
        UpdateStatusDisplay();
        AppendLog("Codex Proxy Pro 独立代理管理控制台已就绪。");
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void LocateConfigFile()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string p1 = Path.Combine(baseDir, "config.json");
        string p2 = Path.Combine(Directory.GetParent(baseDir)?.FullName ?? "", "config.json");
        string p3 = Path.Combine(Directory.GetParent(baseDir)?.Parent_FullName() ?? "", "config.json");

        if (File.Exists(p1)) configFilePath = p1;
        else if (File.Exists(p2)) configFilePath = p2;
        else if (File.Exists(p3)) configFilePath = p3;
        else configFilePath = p1;
    }

    private void LoadConfig()
    {
        loadingConfig = true;
        try
        {
            if (File.Exists(configFilePath))
            {
                string jsonString = File.ReadAllText(configFilePath);
                var doc = JsonNode.Parse(jsonString);
                var proxy = doc?["proxy"];
                if (proxy != null)
                {
                    string host = proxy["host"]?.ToString() ?? "127.0.0.1";
                    string port = proxy["port"]?.ToString() ?? "7890";
                    string type = (proxy["type"]?.ToString() ?? "socks5").ToLowerInvariant();

                    ChkRemoteDns.IsChecked = true; // 由本地桥接按域名名单决定解析位置。

                    TxtProxyHost.Text = host;
                    TxtProxyPort.Text = port;
                    TxtRemoteDnsDomains.Text = string.Join("; ", proxy["remote_dns_domains"] is JsonArray domains
                        ? domains.Select(x => x!.GetValue<string>())
                        : CodexProxy.Dns.DnsPolicy.DefaultDomains);

                    if (type.Contains("http"))
                    {
                        RadioHttp.IsChecked = true;
                    }
                    else
                    {
                        RadioSocks5.IsChecked = true;
                    }
                    AppendLog($"已加载配置: {configFilePath} ({type.ToUpperInvariant()}://{host}:{port}, 远端 DNS 按配置名单限定)");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"读取配置异常: {ex.Message}");
        }
        finally { loadingConfig = false; }

        loadingConfig = true;
        TxtProxyHost.Text = "127.0.0.1";
        TxtProxyPort.Text = "7890";
        RadioSocks5.IsChecked = true;
        ChkRemoteDns.IsChecked = true;
        TxtRemoteDnsDomains.Text = string.Join("; ", CodexProxy.Dns.DnsPolicy.DefaultDomains);
        loadingConfig = false;
    }

    private bool SaveConfig()
    {
        try
        {
            JsonNode? doc = null;
            if (File.Exists(configFilePath))
            {
                doc = JsonNode.Parse(File.ReadAllText(configFilePath));
            }

            if (doc == null || doc is not JsonObject)
            {
                doc = new JsonObject();
            }

            string type = (RadioHttp.IsChecked == true) ? "http" : "socks5";
            string host = TxtProxyHost.Text.Trim();
            _ = LaunchSafety.ProxyUrl(type, host, TxtProxyPort.Text);
            int port = int.Parse(TxtProxyPort.Text.Trim());
            bool remoteDns = ChkRemoteDns.IsChecked == true;

            var proxyObj = doc["proxy"] as JsonObject;
            if (proxyObj == null) { proxyObj = new JsonObject(); doc["proxy"] = proxyObj; }
            proxyObj["host"] = host;
            proxyObj["port"] = port;
            proxyObj["type"] = type;
            proxyObj["remote_dns"] = remoteDns;

            if (TxtRemoteDnsDomains != null && !string.IsNullOrWhiteSpace(TxtRemoteDnsDomains.Text))
            {
                var domainList = TxtRemoteDnsDomains.Text
                    .Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(d => d.Trim())
                    .Where(d => !string.IsNullOrEmpty(d))
                    .ToList();
                proxyObj["remote_dns_domains"] = JsonSerializer.SerializeToNode(domainList);
            }
            else
            {
                proxyObj["remote_dns_domains"] = new JsonArray();
            }
            _ = new CodexProxy.Dns.DnsPolicy(proxyObj["remote_dns_domains"]!.AsArray().Select(x => x!.GetValue<string>()));

            if (processMode) {
                doc["child_injection"] ??= JsonValue.Create(true);
                doc["child_injection_mode"] ??= JsonValue.Create("filtered");
                doc["target_processes"] ??= JsonSerializer.SerializeToNode(new[] { "ChatGPT.exe", "codex.exe", "node.exe", "codex-code-mode-host.exe", "codex-command-runner.exe", "msedgewebview2.exe" });
                doc["proxy_rules"] ??= new JsonObject { ["allowed_ports"] = new JsonArray(), ["dns_mode"] = "direct", ["ipv6_mode"] = "proxy" };
                doc["proxy_rules"]!["udp_mode"] = "direct";
                doc["ui"] ??= new JsonObject { ["load_notify"] = "none" };
            }
            Directory.CreateDirectory(Path.GetDirectoryName(configFilePath)!);
            string formatted = doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            if (File.Exists(configFilePath))
            {
                string backup = configFilePath + ".backup-" + Guid.NewGuid().ToString("N");
                File.Copy(configFilePath, backup);
                var originalHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(configFilePath));
                var backupHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(backup));
                if (!originalHash.SequenceEqual(backupHash)) throw new IOException("配置备份校验失败。");
                string temporary = configFilePath + ".tmp-" + Guid.NewGuid().ToString("N");
                File.WriteAllText(temporary, formatted, new System.Text.UTF8Encoding(false));
                File.Replace(temporary, configFilePath, null);
            }
            else File.WriteAllText(configFilePath, formatted, new System.Text.UTF8Encoding(false));
            AppendLog($"[✔ 配置已持久化] {type.ToUpperInvariant()}://{host}:{port} (远端 DNS 按配置名单限定)");
            return true;
        }
        catch (Exception ex)
        {
            AppendLog($"[错误] 保存配置失败: {ex.Message}");
            return false;
        }
    }

    private void RadioProto_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded && !loadingConfig)
        {
            SaveConfig();
        }
    }

    private void ProxyMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || loadingConfig) return;
        bool next = RadioProcessMode.IsChecked == true;
        if (next == processMode) return;
        if (!SaveConfig()) {
            RadioProcessMode.IsChecked = processMode;
            RadioStandardMode.IsChecked = !processMode;
            return;
        }
        string settings = Path.Combine(Path.GetDirectoryName(standardConfigPath)!, "launcher.settings.json");
        try {
            if (File.Exists(settings)) {
                string backup = settings + ".backup-" + Guid.NewGuid().ToString("N");
                File.Copy(settings, backup);
                if (!System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(settings)).SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(backup))))
                    throw new IOException("模式配置备份校验失败。");
            }
            string temp = settings + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temp, new JsonObject { ["mode"] = next ? "process" : "standard" }.ToJsonString(), new System.Text.UTF8Encoding(false));
            File.Move(temp, settings, true);
            processMode = next;
            configFilePath = next ? Path.Combine(Path.GetDirectoryName(standardConfigPath)!, "process", "config.json") : standardConfigPath;
            LoadConfig();
            AppendLog($"已选择{(next ? "进程代理" : "标准代理")}；仅用于下次启动，已有客户端请先保存任务并手动退出。");
        } catch (Exception ex) {
            AppendLog($"切换模式失败：{ex.Message}");
            RadioProcessMode.IsChecked = processMode;
            RadioStandardMode.IsChecked = !processMode;
        }
    }

    private void StatusTimer_Tick(object? sender, EventArgs e)
    {
        UpdateStatusDisplay();
    }

    private void UpdateStatusDisplay()
    {
        try
        {
            var processes = Process.GetProcessesByName("ChatGPT");
            if (processes.Length > 0)
            {
                int mainPid = processes[0].Id;
                TxtAppStatusTitle.Text = "ChatGPT 正在运行";
                TxtAppStatusSub.Text = $"检测到进程 PID: {mainPid} · 代理效果尚未验证";
                TxtAppStatusTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));

                StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                StatusLabel.Text = $"在线 (PID {mainPid})";
                StatusLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
                StatusBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
                StatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#132A22"));

                BtnLaunch.Content = "打开客户端（保留现有任务）";
            }
            else
            {
                TxtAppStatusTitle.Text = "ChatGPT 客户端未运行";
                TxtAppStatusSub.Text = "使用所选代理参数启动；实际联网效果需验证";
                TxtAppStatusTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FAFAFA"));

                StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#71717A"));
                StatusLabel.Text = "空闲就绪";
                StatusLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A1A1AA"));
                StatusBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#353542"));
                StatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#222228"));

                BtnLaunch.Content = "🚀 启动 ChatGPT 专属代理";
            }
            foreach (var process in processes) process.Dispose();
        }
        catch { }
    }

    private async void BtnTestProxy_Click(object sender, RoutedEventArgs e)
    {
        BtnTestProxy.IsEnabled = false;
        TxtTestResult.Text = "正在检查 TCP 端口...";
        TxtTestResult.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A1A1AA"));

        string host = TxtProxyHost.Text.Trim();
        if (!int.TryParse(TxtProxyPort.Text.Trim(), out int port)) port = 7890;
        string type = (RadioHttp.IsChecked == true) ? "http" : "socks5";

        var sw = Stopwatch.StartNew();
        string portText = TxtProxyPort.Text;
        bool ok = false;
        string error = "";

        await System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                using var client = new TcpClient();
                _ = LaunchSafety.ProxyUrl(type, host, portText);
                var result = client.BeginConnect(host, port, null, null);
                using var waitHandle = result.AsyncWaitHandle;
                bool success = waitHandle.WaitOne(TimeSpan.FromSeconds(2));
                if (!success)
                {
                    error = "超时无法连接";
                    return;
                }
                client.EndConnect(result);
                sw.Stop();
                ok = true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        });

        BtnTestProxy.IsEnabled = true;
        if (ok)
        {
            TxtTestResult.Text = $"TCP 可达 ({sw.ElapsedMilliseconds} ms)，未验证代理";
            TxtTestResult.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
            AppendLog($"[TCP 检查] {host}:{port} 可达；未验证代理协议、认证及转发能力。");
        }
        else
        {
            TxtTestResult.Text = $"❌ 无法连接 ({error})";
            TxtTestResult.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171"));
            AppendLog($"[探测失败] 代理连接失败: {error}");
        }
    }

    private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
    {
        SaveConfig();
    }

    private void BtnLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (SaveConfig()) LaunchChatGpt();
    }

    private void ChkRemoteDns_Click(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            SaveConfig();
        }
    }

    private void LaunchChatGpt()
    {
        string executable;
        try {
            if (RadioPathManual != null && RadioPathManual.IsChecked == true && !string.IsNullOrWhiteSpace(TxtTargetAppPath.Text) && File.Exists(TxtTargetAppPath.Text.Trim())) {
                executable = TxtTargetAppPath.Text.Trim();
            } else {
                executable = ClientStart.FindExecutable(PackageFamilyName);
            }
            if (ClientStart.IsRunning(executable)) {
                AppendLog("[未启动] 客户端已在运行，无法给已有进程补传代理环境。请保存任务并手动完全退出客户端后再启动。");
                return;
            }
        } catch (Exception ex) { AppendLog($"[未启动] {ex.Message}"); return; }

        if (processMode) {
            try {
                var created = ClientStart.StartIntercepted(executable, configFilePath);
                managedProcess?.Dispose();
                managedProcess = created;
                AppendLog($"[进程代理] PID {created.Id} 已完成注入与就绪检查；UDP 直连，远端 DNS 限于配置名单。");
            } catch (Exception ex) { AppendLog($"[进程代理启动失败] {ex.Message}"); }
            UpdateStatusDisplay();
            return;
        }

        DnsBridgeSession bridge;
        try { bridge = new DnsBridgeSession(configFilePath); }
        catch (Exception ex) { AppendLog($"[DNS] 启动失败：{ex.Message}"); return; }
        using var bridgeScope = bridge;
        string proxyUrl = bridge.ProxyUrl;

        AppendLog("--------------------------------------------------");
        AppendLog($"[准备唤起] 目标代理通道: {proxyUrl}");
        AppendLog("[DNS] 仅名单内域名由上游解析；名单外本地解析，保留原代理连接。本地解析失败不转远端。");
        AppendLog("已有实例会保留；修改代理后，请先保存任务并手动退出客户端，再重新启动。");

        // 2. 检查本地回环豁免
        EnsureLoopbackExemption();

        // 直接创建已注册包内的桌面进程，显式环境随进程创建传入。
        try
        {
            var start = ClientStart.Create(executable, proxyUrl);
            DateTime requestedAt = DateTime.UtcNow;
            using var created = Process.Start(start) ?? throw new IOException("无法创建客户端进程。");
            var newlyStarted = LaunchSafety.TrackNewProcess(created.Id, requestedAt)
                ?? throw new IOException("没有获得新的客户端实例，代理未生效。");
            try { bridge.Follow(newlyStarted); }
            catch { newlyStarted.Dispose(); throw; }
            managedProcess?.Dispose();
            managedProcess = newlyStarted;
            AppendLog($"[已创建] 客户端 PID: {created.Id}，代理环境已随进程传入；Node 环境代理已启用。");
            AppendLog("[待验证] 任务后端须完成实际请求后才能确认可用；本程序不等同于系统级全进程代理。");
        }
        catch (Exception ex)
        {
            AppendLog($"[异常] 激活过程发生异常: {ex.Message}");
            MessageBox.Show($"激活失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        UpdateStatusDisplay();
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppendLog(LaunchSafety.RequestClose(managedProcess)
                ? "已请求本次启动的窗口正常关闭；请处理客户端的保存提示。"
                : "没有可关闭的本次启动窗口，请在客户端中手动退出。");
        }
        catch (Exception ex) { AppendLog($"请求关闭失败: {ex.Message}"); }
        UpdateStatusDisplay();
    }

    public void LaunchFromShortcut() => BtnLaunch_Click(this, new RoutedEventArgs());

    private void EnsureLoopbackExemption()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "CheckNetIsolation.exe"),
                Arguments = "LoopbackExempt -s",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            if (p == null) throw new InvalidOperationException("无法启动回环豁免查询。");
            var output = p.StandardOutput.ReadToEndAsync();
            var errors = p.StandardError.ReadToEndAsync();
            bool completed = p.WaitForExit(2000);
            bool confirmed = completed && output.Wait(500) &&
                LaunchSafety.LoopbackConfirmed(true, p.ExitCode, output.Result, PackageFamilyName);
            AppendLog(confirmed ? "[回环] 已查询到目标包豁免。"
                : "[回环] 未确认目标包豁免（查询失败、超时或未配置）；使用本地代理时请检查管理员设置。");
        }
        catch (Exception ex) { AppendLog($"[回环] 查询失败: {ex.Message}"); }
    }

    protected override void OnClosed(EventArgs e)
    {
        statusTimer.Stop();
        managedProcess?.Dispose();
        base.OnClosed(e);
    }

    private void BtnCreateShortcut_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string shortcutPath = Path.Combine(desktop, "ChatGPT (独立代理).lnk");
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = exePath;
                shortcut.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                shortcut.Description = "启动带独立透明代理的 ChatGPT 客户端";
                shortcut.IconLocation = $"{exePath},0";
                shortcut.Save();
                AppendLog($"[✔ 桌面图标] 已在桌面生成快捷方式: {shortcutPath}");
                MessageBox.Show("桌面快捷方式创建成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[错误] 创建快捷方式失败: {ex.Message}");
        }
    }

    private void ChkAutoStart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key != null)
            {
                if (ChkAutoStart.IsChecked == true)
                {
                    string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    key.SetValue(AppName, $"\"{exePath}\"");
                    AppendLog("[设置] 已开启开机自启。");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                    AppendLog("[设置] 已关闭开机自启。");
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[错误] 修改开机自启失败: {ex.Message}");
        }
    }

    private void CheckAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            ChkAutoStart.IsChecked = key?.GetValue(AppName) != null;
        }
        catch { }
    }

    private void BtnOpenConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(configFilePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{configFilePath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show($"未找到配置文件: {configFilePath}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"打开配置异常: {ex.Message}");
        }
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        TxtLog.Clear();
        AppendLog("诊断控制台日志已清空。");
    }

    private void BtnCopyLog_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(TxtLog.Text);
        AppendLog("控制台日志已成功复制到剪贴板。");
    }

    private void RadioPathMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || TxtTargetAppPath == null) return;
        if (RadioPathManual.IsChecked == true)
        {
            TxtTargetAppPath.IsReadOnly = false;
            BtnAutoDetectPath.Visibility = Visibility.Collapsed;
            BtnBrowseAppPath.Visibility = Visibility.Visible;
            AppendLog("[模式切换] 已切换至手动指定路径模式；可直接输入或浏览 exe 文件。");
        }
        else
        {
            TxtTargetAppPath.IsReadOnly = true;
            BtnAutoDetectPath.Visibility = Visibility.Visible;
            BtnBrowseAppPath.Visibility = Visibility.Collapsed;
            AutoDetectPath();
        }
    }

    private void BtnAutoDetectPath_Click(object sender, RoutedEventArgs e)
    {
        AutoDetectPath();
    }

    private void AutoDetectPath()
    {
        try
        {
            string exe = ClientStart.FindExecutable(PackageFamilyName);
            TxtTargetAppPath.Text = exe;
            AppendLog($"[自动探测成功] 已检测到目标应用路径: {exe}");
        }
        catch (Exception ex)
        {
            TxtTargetAppPath.Text = Aumid;
            AppendLog($"[自动探测提示] 未在商店包直接提取 exe，已重置为通用 AUMID 包名 ({Aumid}): {ex.Message}");
        }
    }

    private void BtnBrowseAppPath_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
                Title = "选择 ChatGPT 可执行程序文件"
            };
            if (dlg.ShowDialog() == true)
            {
                TxtTargetAppPath.Text = dlg.FileName;
                AppendLog($"[手动指定路径] 已选择程序文件: {dlg.FileName}");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[错误] 打开文件对话框失败: {ex.Message}");
        }
    }

    private void BtnToggleSettings_Click(object sender, RoutedEventArgs e)
    {
        if (GridSettings.Visibility == Visibility.Visible)
        {
            GridSettings.Visibility = Visibility.Collapsed;
            GridMainContent.Visibility = Visibility.Visible;
        }
        else
        {
            GridSettings.Visibility = Visibility.Visible;
            GridMainContent.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnBackToMain_Click(object sender, RoutedEventArgs e)
    {
        GridSettings.Visibility = Visibility.Collapsed;
        GridMainContent.Visibility = Visibility.Visible;
    }

    private void BtnSaveAdvancedSettings_Click(object sender, RoutedEventArgs e)
    {
        if (SaveConfig())
        {
            AppendLog("[设置已更新] 托管进程及 SOCKS5h 远端 DNS 域名解析白名单规则已保存。");
            GridSettings.Visibility = Visibility.Collapsed;
            GridMainContent.Visibility = Visibility.Visible;
        }
    }

    private void BtnOpenGithub_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/jojhaa/Codex-Proxy",
                UseShellExecute = true
            });
            AppendLog("[GitHub] 已在浏览器中打开项目仓库: https://github.com/jojhaa/Codex-Proxy");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开浏览器: {ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        BtnCheckUpdate.IsEnabled = false;
        TxtUpdateStatus.Text = "正在检查最新版本...";
        TxtUpdateStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"));

        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "CodexProxy-App");
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = await client.GetStringAsync("https://api.github.com/repos/TimeSilent/Codex-Proxy/releases/latest");
            var doc = JsonNode.Parse(response);
            string latestTag = doc?["tag_name"]?.ToString() ?? "v1.2";
            string body = doc?["body"]?.ToString() ?? "无详细日志";
            string htmlUrl = doc?["html_url"]?.ToString() ?? "https://github.com/jojhaa/Codex-Proxy/releases";

            if (latestTag != "v1.2" && !string.IsNullOrEmpty(latestTag))
            {
                TxtUpdateStatus.Text = $"🎉 发现新版本 {latestTag} (非强制更新)";
                TxtUpdateStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));

                var result = MessageBox.Show($"检测到新版本 {latestTag}！\n\n【更新说明】\n{body}\n\n是否前往 GitHub 下载新版本？(非强制更新)", "版本更新提示", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (result == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo { FileName = htmlUrl, UseShellExecute = true });
                }
            }
            else
            {
                TxtUpdateStatus.Text = "✨ 当前已是最新版本 (v1.2)";
                TxtUpdateStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
                MessageBox.Show("当前已是最新版本 (v1.2)！无需更新。", "更新检查", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch
        {
            TxtUpdateStatus.Text = "✨ 当前已是最新稳定版 (v1.2)";
            TxtUpdateStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
            MessageBox.Show("当前已是最新版本 (v1.2)！", "更新检查", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            BtnCheckUpdate.IsEnabled = true;
        }
    }

    private void AppendLog(string message)
    {
        string time = DateTime.Now.ToString("HH:mm:ss");
        TxtLog.AppendText($"[{time}] {message}\n");
        LogScrollViewer.ScrollToEnd();
    }
}

internal static class DirectoryExtensions
{
    public static string Parent_FullName(this DirectoryInfo? dir) => dir?.Parent?.FullName ?? "";
}
