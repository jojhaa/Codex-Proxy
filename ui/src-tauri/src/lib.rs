// Copyright (c) Time Silent. SPDX-License-Identifier: GPL-3.0-only
mod drag_state;
use serde::{Deserialize, Serialize};
use std::fs;
use std::net::{SocketAddr, TcpStream};
use std::os::windows::process::CommandExt;
use std::path::PathBuf;
use std::process::{Child, Command};
use std::sync::Mutex;
use std::time::Duration;
use tauri::menu::{Menu, MenuItem};
use tauri::tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent};
use tauri::{AppHandle, Emitter, Manager, State};
use winreg::enums::*;
use winreg::RegKey;
mod runtime_paths;
mod single_instance;
mod log_buffer;
mod client_host;
mod process_monitor;

const APP_NAME: &str = "CodexProxyLauncher";
const AUMID: &str = "OpenAI.Codex_2p2nqsd0c76g0!App";
const RUN_REGISTRY_KEY: &str = r"Software\Microsoft\Windows\CurrentVersion\Run";
const CREATE_NO_WINDOW: u32 = 0x08000000;

static DEFAULT_DOMAINS: &[&str] = &[
    "openai.com",
    "chatgpt.com",
    "oaistatic.com",
    "oaiusercontent.com",
];

#[derive(Debug, Serialize, Deserialize, Clone)]
pub struct ProxyConfig {
    pub host: String,
    pub port: String,
    pub proto: String,
    pub remote_dns_domains: Vec<String>,
    pub sub_processes: String,
    pub bypass_rules: String,
    pub target_app_path: String,
    pub path_mode: String,
}

#[derive(Debug, Serialize, Deserialize, Clone)]
pub struct AppStatusResponse {
    pub running: bool,
    pub title: String,
    pub sub: String,
    pub badge_label: String,
    pub badge_color: String,
}

#[derive(Debug, Serialize, Deserialize, Clone)]
pub struct UpdateCheckResult {
    pub success: bool,
    #[serde(rename = "hasUpdate")]
    pub has_update: bool,
    #[serde(rename = "currentVersion")]
    pub current_version: String,
    #[serde(rename = "latestVersion")]
    pub latest_version: String,
    #[serde(rename = "releaseUrl")]
    pub release_url: String,
    #[serde(rename = "releaseNotes")]
    pub release_notes: String,
    #[serde(rename = "usedProxy")]
    pub used_proxy: bool,
    pub message: String,
}

#[derive(Default)]
pub struct AppState {
    pub launch_guard: tokio::sync::Mutex<()>,
    pub dns_bridge_process: Mutex<Option<Child>>,
    pub launcher_process: Mutex<Option<Child>>,
    pub monitor_cancel: Mutex<Option<std::sync::Arc<std::sync::atomic::AtomicBool>>>,
}

fn get_base_dir() -> PathBuf {
    let executable = std::env::current_exe().unwrap_or_else(|_| PathBuf::from("./ProcWeaver.exe"));
    runtime_paths::runtime_dir(&executable)
}

fn get_config_path(mode: &str) -> PathBuf {
    runtime_paths::config_path(&get_base_dir(), mode)
}

fn get_launcher_settings_path() -> PathBuf {
    get_base_dir().join("launcher.settings.json")
}

#[tauri::command]
fn get_launcher_mode() -> String {
    let path = get_launcher_settings_path();
    if path.exists() {
        if let Ok(content) = fs::read_to_string(&path) {
            if let Ok(v) = serde_json::from_str::<serde_json::Value>(&content) {
                if let Some(mode) = v.get("mode").and_then(|m| m.as_str()) {
                    return mode.to_string();
                }
            }
        }
    }
    "standard".to_string()
}

#[tauri::command]
fn set_launcher_mode(mode: String) -> Result<(), String> {
    if mode != "standard" && mode != "process" {
        return Err("无效的代理模式".to_string());
    }
    let path = get_launcher_settings_path();
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).map_err(|e| format!("创建模式设置目录失败: {}", e))?;
    }
    let json = serde_json::json!({ "mode": mode });
    fs::write(path, serde_json::to_string_pretty(&json).unwrap())
        .map_err(|e| format!("保存模式设置失败: {}", e))
}

#[tauri::command]
fn get_proxy_config(mode: String) -> ProxyConfig {
    let path = get_config_path(&mode);
    let mut config = ProxyConfig {
        host: "127.0.0.1".to_string(),
        port: "7890".to_string(),
        proto: "socks5".to_string(),
        remote_dns_domains: DEFAULT_DOMAINS.iter().map(|s| s.to_string()).collect(),
        sub_processes: "codex.exe; node.exe; electron.exe".to_string(),
        bypass_rules: "<-loopback>; localhost; 127.0.0.1; ::1".to_string(),
        target_app_path: AUMID.to_string(),
        path_mode: "auto".to_string(),
    };

    if path.exists() {
        if let Ok(content) = fs::read_to_string(&path) {
            if let Ok(v) = serde_json::from_str::<serde_json::Value>(&content) {
                if let Some(proxy) = v.get("proxy") {
                    if let Some(h) = proxy.get("host").and_then(|x| x.as_str()) {
                        config.host = h.to_string();
                    }
                    if let Some(p) = proxy.get("port") {
                        if let Some(p_str) = p.as_str() {
                            config.port = p_str.to_string();
                        } else if let Some(p_num) = p.as_u64() {
                            config.port = p_num.to_string();
                        }
                    }
                    if let Some(t) = proxy.get("type").and_then(|x| x.as_str()) {
                        config.proto = t.to_string();
                    }
                    if let Some(domains) = proxy.get("remote_dns_domains").and_then(|x| x.as_array()) {
                        config.remote_dns_domains = domains
                            .iter()
                            .filter_map(|d| d.as_str().map(|s| s.to_string()))
                            .collect();
                    }
                }
                if let Some(proc) = v.get("process") {
                    if let Some(sub) = proc.get("sub_processes").and_then(|x| x.as_array()) {
                        config.sub_processes = sub
                            .iter()
                            .filter_map(|s| s.as_str())
                            .collect::<Vec<_>>()
                            .join("; ");
                    }
                    if let Some(bp) = proc.get("bypass_rules").and_then(|x| x.as_array()) {
                        config.bypass_rules = bp
                            .iter()
                            .filter_map(|s| s.as_str())
                            .collect::<Vec<_>>()
                            .join("; ");
                    }
                    if let Some(target) = proc.get("target_app_path").and_then(|x| x.as_str()) {
                        config.target_app_path = target.to_string();
                    }
                    if let Some(pm) = proc.get("path_mode").and_then(|x| x.as_str()) {
                        config.path_mode = pm.to_string();
                    }
                }
            }
        }
    }
    config
}

#[tauri::command]
fn save_proxy_config(mode: String, config: ProxyConfig) -> Result<(), String> {
    let path = get_config_path(&mode);
    let sub_proc_vec: Vec<&str> = config
        .sub_processes
        .split(';')
        .map(|s| s.trim())
        .filter(|s| !s.is_empty())
        .collect();
    let bypass_vec: Vec<&str> = config
        .bypass_rules
        .split(';')
        .map(|s| s.trim())
        .filter(|s| !s.is_empty())
        .collect();

    let json = serde_json::json!({
        "proxy": {
            "type": config.proto,
            "host": config.host,
            "port": config.port.parse::<u16>().unwrap_or(7890),
            "remote_dns_domains": config.remote_dns_domains
        },
        "process": {
            "target_app_path": config.target_app_path,
            "path_mode": config.path_mode,
            "sub_processes": sub_proc_vec,
            "bypass_rules": bypass_vec
        }
    });

    if let Some(parent) = path.parent() {
        let _ = fs::create_dir_all(parent);
    }
    fs::write(path, serde_json::to_string_pretty(&json).unwrap())
        .map_err(|e| format!("写入配置文件失败: {}", e))
}

#[tauri::command]
async fn check_proxy_port(host: String, port: u16) -> Result<String, String> {
    let addr_str = format!("{}:{}", host, port);
    if let Ok(addr) = addr_str.parse::<SocketAddr>() {
        if TcpStream::connect_timeout(&addr, Duration::from_millis(1500)).is_ok() {
            return Ok(format!("✓ 端口 {}:{} 可访问", host, port));
        }
    }
    Err(format!("✕ 端口 {}:{} 无法连接", host, port))
}

#[tauri::command]
fn get_app_status() -> AppStatusResponse {
    let output = Command::new("tasklist")
        .creation_flags(CREATE_NO_WINDOW)
        .args(["/FI", "IMAGENAME eq ChatGPT.exe", "/NH"])
        .output();
    let is_chatgpt_running = match output {
        Ok(out) => String::from_utf8_lossy(&out.stdout).contains("ChatGPT.exe"),
        Err(_) => false,
    };

    let output_codex = Command::new("tasklist")
        .creation_flags(CREATE_NO_WINDOW)
        .args(["/FI", "IMAGENAME eq codex.exe", "/NH"])
        .output();
    let is_codex_running = match output_codex {
        Ok(out) => String::from_utf8_lossy(&out.stdout).contains("codex.exe"),
        Err(_) => false,
    };

    let running = is_chatgpt_running || is_codex_running;
    if running {
        AppStatusResponse {
            running: true,
            title: "ChatGPT 客户端运行中".to_string(),
            sub: "已代理网络通信 · 实时远端 DNS 解析中".to_string(),
            badge_label: "已代理连接".to_string(),
            badge_color: "#059669".to_string(),
        }
    } else {
        AppStatusResponse {
            running: false,
            title: "ChatGPT 客户端未运行".to_string(),
            sub: "使用代理参数启动；实际联网效果需验证".to_string(),
            badge_label: "空闲就绪".to_string(),
            badge_color: "#64748B".to_string(),
        }
    }
}

#[tauri::command]
async fn resolve_client_path(target_path: Option<String>) -> Result<String, String> {
    Ok(client_host::request(&get_base_dir(), "resolve", target_path.as_deref()).await?.path)
}

#[tauri::command]
async fn launch_client(app_handle: AppHandle, state: State<'_, AppState>, mode: String, target_path: Option<String>) -> Result<String, String> {
    if mode != "standard" && mode != "process" { return Err("无效的代理模式".to_string()); }
    let _guard = state.launch_guard.try_lock().map_err(|_| "正在启动客户端，请勿重复点击")?;
    let result = client_host::request(&get_base_dir(), &mode, target_path.as_deref()).await?;
    let _ = app_handle.emit("client-path", &result.path);
    let _ = app_handle.emit("log-line", &format!("[启动成功] 目标程序: {} (PID: {})", result.path, result.pid));

    // 挂载子进程树生命周期实时监控
    if let Ok(mut lock) = state.monitor_cancel.lock() {
        if let Some(flag) = lock.take() {
            flag.store(true, std::sync::atomic::Ordering::Relaxed);
        }
        let flag = std::sync::Arc::new(std::sync::atomic::AtomicBool::new(false));
        *lock = Some(flag.clone());
        process_monitor::monitor_client_process_tree(app_handle.clone(), result.pid, flag);
    }

    Ok(format!("已启动目标客户端 PID {}：{}；实际联网效果需验证", result.pid, result.path))
}

fn cleanup_child_processes(state: &AppState) -> usize {
    if let Ok(mut lock) = state.monitor_cancel.lock() {
        if let Some(flag) = lock.take() {
            flag.store(true, std::sync::atomic::Ordering::Relaxed);
        }
    }

    let mut count = 0;
    if let Ok(mut lock) = state.dns_bridge_process.lock() {
        if let Some(mut child) = lock.take() {
            let _ = child.kill();
            count += 1;
        }
    }
    if let Ok(mut lock) = state.launcher_process.lock() {
        if let Some(mut child) = lock.take() {
            let _ = child.kill();
            count += 1;
        }
    }

    let _ = Command::new("taskkill")
        .creation_flags(CREATE_NO_WINDOW)
        .args(["/F", "/IM", "codex_dns_bridge.exe"])
        .output();
    count
}

#[tauri::command]
fn stop_client(app_handle: AppHandle, state: State<'_, AppState>) -> Result<String, String> {
    let count = cleanup_child_processes(&state);
    let _ = Command::new("taskkill")
        .creation_flags(CREATE_NO_WINDOW)
        .args(["/F", "/IM", "ChatGPT.exe"])
        .output();
    let _ = Command::new("taskkill")
        .creation_flags(CREATE_NO_WINDOW)
        .args(["/F", "/IM", "codex.exe"])
        .output();
    let _ = app_handle.emit("log-line", "[停止] 专属代理与目标客户端已成功关闭");
    Ok(format!("已停止代理服务及客户端 (终止 {} 个相关进程)", count))
}

#[tauri::command]
fn hide_to_tray(app: AppHandle) -> Result<(), String> {
    if let Some(window) = app.get_webview_window("main") {
        window.hide().map_err(|e| e.to_string())?;
    }
    Ok(())
}

#[tauri::command]
fn show_from_tray(app: AppHandle) -> Result<(), String> {
    if let Some(window) = app.get_webview_window("main") {
        window.show().map_err(|e| e.to_string())?;
        window.unminimize().map_err(|e| e.to_string())?;
        window.set_focus().map_err(|e| e.to_string())?;
    }
    Ok(())
}

#[tauri::command]
fn exit_app(app: AppHandle, state: State<'_, AppState>) {
    cleanup_child_processes(&state);
    app.exit(0);
}

#[tauri::command]
fn check_auto_start() -> bool {
    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    if let Ok(key) = hkcu.open_subkey(RUN_REGISTRY_KEY) {
        key.get_value::<String, _>(APP_NAME).is_ok()
    } else {
        false
    }
}

#[tauri::command]
fn toggle_auto_start(enable: bool) -> Result<(), String> {
    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    let key = hkcu
        .open_subkey_with_flags(RUN_REGISTRY_KEY, KEY_ALL_ACCESS)
        .map_err(|e| format!("打开注册表失败: {}", e))?;

    if enable {
        let exe_path = std::env::current_exe()
            .map_err(|e| format!("获取当前程序路径失败: {}", e))?
            .to_string_lossy()
            .to_string();
        key.set_value(APP_NAME, &exe_path)
            .map_err(|e| format!("设置开机自启失败: {}", e))?;
    } else {
        let _ = key.delete_value(APP_NAME);
    }
    Ok(())
}

#[tauri::command]
fn create_desktop_shortcut() -> Result<String, String> {
    let exe_path = std::env::current_exe()
        .map_err(|e| format!("获取当前 exe 路径失败: {}", e))?;
    let desktop_dir = dirs_next::desktop_dir()
        .unwrap_or_else(|| PathBuf::from(r"C:\Users\Public\Desktop"));
    let shortcut_path = desktop_dir.join("ProcWeaver.lnk");

    let script = format!(
        "$WshShell = New-Object -ComObject WScript.Shell; \
         $Shortcut = $WshShell.CreateShortcut('{}'); \
         $Shortcut.TargetPath = '{}'; \
         $Shortcut.Save()",
        shortcut_path.to_string_lossy(),
        exe_path.to_string_lossy()
    );

    let output = Command::new("powershell")
        .creation_flags(CREATE_NO_WINDOW)
        .args(["-NoProfile", "-Command", &script])
        .output()
        .map_err(|e| format!("执行创建快捷方式脚本失败: {}", e))?;

    if output.status.success() {
        Ok("桌面快捷方式生成成功".to_string())
        } else {
        Err(String::from_utf8_lossy(&output.stderr).to_string())
    }
}

#[tauri::command]
fn open_external_url(url: String) -> Result<(), String> {
    Command::new("cmd")
        .creation_flags(CREATE_NO_WINDOW)
        .args(["/c", "start", "", &url])
        .spawn()
        .map_err(|e| format!("唤起系统浏览器失败: {}", e))?;
    Ok(())
}

#[tauri::command]
fn check_app_update() -> Result<UpdateCheckResult, String> {
    let script = r#"
$directUrl = "https://api.github.com/repos/jojhaa/Codex-Proxy/releases"
$proxyUrl = "https://gh-proxy.org/" + $directUrl
$headers = @{ "User-Agent" = "ProcWeaver/1.0.11" }

$usedProxy = $false
$data = $null

try {
    $data = Invoke-RestMethod -Uri $directUrl -Headers $headers -TimeoutSec 4 -ErrorAction Stop
} catch {
    $usedProxy = $true
    try {
        $data = Invoke-RestMethod -Uri $proxyUrl -Headers $headers -TimeoutSec 6 -ErrorAction Stop
    } catch {
        $errObj = @{
            success = $false
            hasUpdate = $false
            currentVersion = "v1.0.11"
            latestVersion = ""
            releaseUrl = "https://github.com/jojhaa/Codex-Proxy/releases"
            releaseNotes = ""
            usedProxy = $true
            message = "直连与代理检测均失败: $($_.Exception.Message)"
        }
        Write-Output (ConvertTo-Json $errObj)
        exit 0
    }
}

$hasUpdate = $false
$latestTag = "v1.0.11"
$releaseUrl = "https://github.com/jojhaa/Codex-Proxy/releases"
$notes = ""
$msg = "当前已是最新版本"

if ($data -and $data.Count -gt 0) {
    $latest = $data[0]
    $latestTag = [string]$latest.tag_name
    $releaseUrl = [string]$latest.html_url
    $notes = [string]$latest.body
    $current = "v1.0.11"
    if ($latestTag.TrimStart('v') -ne $current.TrimStart('v')) {
        $hasUpdate = $true
        $msg = "发现新版本 $latestTag"
    } else {
        $msg = "当前已是最新版本"
    }
} else {
    $msg = "当前已是最新版本（暂无更新发布）"
}

$outObj = @{
    success = $true
    hasUpdate = $hasUpdate
    currentVersion = "v1.0.11"
    latestVersion = $latestTag
    releaseUrl = $releaseUrl
    releaseNotes = $notes
    usedProxy = $usedProxy
    message = $msg
}
Write-Output (ConvertTo-Json $outObj)
"#;

    let output = Command::new("powershell")
        .creation_flags(CREATE_NO_WINDOW)
        .args(["-NoProfile", "-Command", script])
        .output()
        .map_err(|e| format!("执行更新检测失败: {}", e))?;

    let stdout_str = String::from_utf8_lossy(&output.stdout);
    serde_json::from_str::<UpdateCheckResult>(&stdout_str)
        .map_err(|e| format!("解析更新响应失败: {} (raw: {})", e, stdout_str))
}

#[tauri::command]
fn set_full_logs(enabled: bool) -> Result<(), String> {
    let marker = get_base_dir().join("full-logs.enabled");
    if enabled {
        fs::write(&marker, std::process::id().to_string()).map_err(|e| format!("开启完整日志失败：{e}"))?;
    } else if marker.exists() {
        fs::remove_file(&marker).map_err(|e| format!("关闭完整日志失败：{e}"))?;
    }
    log_buffer::FULL_LOGS.store(enabled, std::sync::atomic::Ordering::Relaxed);
    Ok(())
}

pub fn run() {
    let instance = match single_instance::SingleInstance::acquire("Local\\CodexProxyUI.com.codex.proxy") {
        Ok(Some(instance)) => instance,
        Ok(None) => return,
        Err(error) => { eprintln!("单实例初始化失败：{error}"); return; }
    };
    tauri::Builder::default()
        .manage(instance)
        .setup(|app| {
            set_full_logs(false).map_err(std::io::Error::other)?;
            process_monitor::start_udp_log_receiver(app.handle().clone());
            let handle = app.handle().clone();
            app.state::<single_instance::SingleInstance>().listen(move || {
                let window_app = handle.clone();
                let _ = handle.run_on_main_thread(move || {
                    if let Some(window) = window_app.get_webview_window("main") {
                        let _ = window.show();
                        let _ = window.unminimize();
                        if let Ok(handle) = window.hwnd() {
                            single_instance::restore_window(handle.0 as isize);
                        }
                        let _ = window.set_focus();
                    }
                });
            });
            if let Some(icon) = app.default_window_icon() {
                for window in app.webview_windows().values() {
                    let _ = window.set_icon(icon.clone());
                }
            }

            let show_i = MenuItem::with_id(app, "show", "显示主窗口", true, None::<&str>)?;
            let quit_i = MenuItem::with_id(app, "quit", "退出程序", true, None::<&str>)?;
            let menu = Menu::with_items(app, &[&show_i, &quit_i])?;

            let mut tray_builder = TrayIconBuilder::new()
                .tooltip("ProcWeaver Pro - 织程代理")
                .menu(&menu)
                .show_menu_on_left_click(false)
                .on_menu_event(|app, event| {
                    match event.id.as_ref() {
                        "show" => {
                            if let Some(window) = app.get_webview_window("main") {
                                let _ = window.show();
                                let _ = window.unminimize();
                                let _ = window.set_focus();
                            }
                        }
                        "quit" => {
                            let state = app.state::<AppState>();
                            cleanup_child_processes(&state);
                            app.exit(0);
                        }
                        _ => {}
                    }
                })
                .on_tray_icon_event(|tray, event| {
                    if let TrayIconEvent::Click {
                        button: MouseButton::Left,
                        button_state: MouseButtonState::Up,
                        ..
                    } = event
                    {
                        let app = tray.app_handle();
                        if let Some(window) = app.get_webview_window("main") {
                            if let Ok(visible) = window.is_visible() {
                                if visible {
                                    let _ = window.hide();
                                } else {
                                    let _ = window.show();
                                    let _ = window.unminimize();
                                    let _ = window.set_focus();
                                }
                            } else {
                                let _ = window.show();
                                let _ = window.unminimize();
                                let _ = window.set_focus();
                            }
                        }
                    }
                });

            if let Some(icon) = app.default_window_icon() {
                tray_builder = tray_builder.icon(icon.clone());
            }

            let _ = tray_builder.build(app)?;

            Ok(())
        })
        .manage(AppState::default())
        .plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![
            drag_state::is_drag_button_down,
            get_launcher_mode,
            set_full_logs,
            set_launcher_mode,
            get_proxy_config,
            save_proxy_config,
            check_proxy_port,
            get_app_status,
            launch_client,
            resolve_client_path,
            stop_client,
            check_auto_start,
            toggle_auto_start,
            create_desktop_shortcut,
            hide_to_tray,
            show_from_tray,
            exit_app,
            open_external_url,
            check_app_update
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
