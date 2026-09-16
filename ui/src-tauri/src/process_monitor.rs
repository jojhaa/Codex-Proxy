use std::collections::{HashMap, HashSet};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::Duration;
use tauri::{AppHandle, Emitter};

#[repr(C)]
struct ProcessEntry32W {
    dw_size: u32,
    cnt_usage: u32,
    th32_process_id: u32,
    th32_default_heap_id: usize,
    th32_module_id: u32,
    cnt_threads: u32,
    th32_parent_process_id: u32,
    pc_pri_class_base: i32,
    dw_flags: u32,
    sz_exe_file: [u16; 260],
}

extern "system" {
    fn CreateToolhelp32Snapshot(dw_flags: u32, th32_process_id: u32) -> isize;
    fn Process32FirstW(h_snapshot: isize, lppe: *mut ProcessEntry32W) -> i32;
    fn Process32NextW(h_snapshot: isize, lppe: *mut ProcessEntry32W) -> i32;
    fn CloseHandle(h_object: isize) -> i32;
    fn OpenProcess(dw_desired_access: u32, b_inherit_handle: i32, dw_process_id: u32) -> isize;
}

#[link(name = "ntdll")]
extern "system" {
    fn NtQueryInformationProcess(
        process_handle: isize,
        process_information_class: i32,
        process_information: *mut u8,
        process_information_length: u32,
        return_length: *mut u32,
    ) -> i32;
}

/// 读取指定进程的完整命令行，用于识别 Chromium / Electron 子进程的具体角色
fn get_process_command_line(pid: u32) -> Option<String> {
    unsafe {
        let handle = OpenProcess(0x1000, 0, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if handle == 0 {
            return None;
        }
        let mut ret_len: u32 = 0;
        // 先获取所需大小
        let _ = NtQueryInformationProcess(handle, 60, std::ptr::null_mut(), 0, &mut ret_len);
        if ret_len == 0 {
            CloseHandle(handle);
            return None;
        }

        let mut buf = vec![0u8; ret_len as usize + 16];
        let status = NtQueryInformationProcess(
            handle,
            60,
            buf.as_mut_ptr(),
            buf.len() as u32,
            &mut ret_len,
        );
        CloseHandle(handle);

        if status != 0 {
            return None;
        }

        // 解析 UNICODE_STRING 结构
        // 64 位下：Length (2 bytes), MaximumLength (2 bytes), 填充 (4 bytes), Buffer (8 bytes)
        #[repr(C)]
        struct UnicodeString64 {
            length: u16,
            maximum_length: u16,
            _padding: u32,
            buffer: *const u16,
        }

        if buf.len() < std::mem::size_of::<UnicodeString64>() {
            return None;
        }

        let ustr = &*(buf.as_ptr() as *const UnicodeString64);
        if ustr.buffer.is_null() || ustr.length == 0 {
            return None;
        }

        let char_count = (ustr.length / 2) as usize;
        let slice = std::slice::from_raw_parts(ustr.buffer, char_count);
        Some(String::from_utf16_lossy(slice))
    }
}

/// 根据命令行解析 Chromium / Electron 进程角色
fn identify_process_role(cmd: &str) -> &'static str {
    let lower = cmd.to_lowercase();
    if lower.contains("network.mojom") {
        "网络核心服务 (Network)"
    } else if lower.contains("--type=renderer") {
        "UI 界面渲染 (Renderer)"
    } else if lower.contains("--type=gpu-process") {
        "GPU 硬件加速 (GPU)"
    } else if lower.contains("crashpad") {
        "崩溃守护服务 (Crashpad)"
    } else if lower.contains("--type=utility") {
        "辅助工具组件 (Utility)"
    } else {
        "衍生工作进程"
    }
}

/// 抓取当前系统所有进程快照，返回 (pid, parent_pid) 列表
fn get_system_process_pairs() -> Vec<(u32, u32)> {
    let mut pairs = Vec::with_capacity(512);
    unsafe {
        let snapshot = CreateToolhelp32Snapshot(0x00000002, 0); // TH32CS_SNAPPROCESS
        if snapshot == -1 || snapshot == 0 {
            return pairs;
        }

        let mut entry = ProcessEntry32W {
            dw_size: std::mem::size_of::<ProcessEntry32W>() as u32,
            cnt_usage: 0,
            th32_process_id: 0,
            th32_default_heap_id: 0,
            th32_module_id: 0,
            cnt_threads: 0,
            th32_parent_process_id: 0,
            pc_pri_class_base: 0,
            dw_flags: 0,
            sz_exe_file: [0; 260],
        };

        if Process32FirstW(snapshot, &mut entry) != 0 {
            loop {
                pairs.push((entry.th32_process_id, entry.th32_parent_process_id));
                if Process32NextW(snapshot, &mut entry) == 0 {
                    break;
                }
            }
        }
        CloseHandle(snapshot);
    }
    pairs
}

/// 启动针对指定根进程 PID 的子进程树监控任务
pub fn monitor_client_process_tree(app_handle: AppHandle, root_pid: u32, cancel_flag: Arc<AtomicBool>) {
    tauri::async_runtime::spawn(async move {
        let mut known_children: HashMap<u32, String> = HashMap::new();
        let _ = app_handle.emit(
            "log-line",
            &format!("[进程树监控] 已挂载目标根进程 PID: {}，开始监测衍生子进程生命周期...", root_pid),
        );

        while !cancel_flag.load(Ordering::Relaxed) {
            tokio::time::sleep(Duration::from_millis(1000)).await;

            let pairs = get_system_process_pairs();

            // 检查根进程是否还存活
            let root_alive = pairs.iter().any(|(pid, _)| *pid == root_pid);
            if !root_alive {
                let _ = app_handle.emit(
                    "log-line",
                    &format!("[进程退出] ChatGPT 主进程 (PID: {}) 已退出，子进程树监控已结束", root_pid),
                );
                break;
            }

            // 递归或层级查找属于 root_pid 树的所有子孙 PID
            let mut family_pids = HashSet::new();
            family_pids.insert(root_pid);

            let mut expanded = true;
            while expanded {
                expanded = false;
                for &(pid, parent_pid) in &pairs {
                    if family_pids.contains(&parent_pid) && !family_pids.contains(&pid) {
                        family_pids.insert(pid);
                        expanded = true;
                    }
                }
            }

            // 去掉根进程自身，剩余均为子孙进程
            family_pids.remove(&root_pid);

            // 1. 检测新衍生的子进程
            for &child_pid in &family_pids {
                if !known_children.contains_key(&child_pid) {
                    let role = if let Some(cmd) = get_process_command_line(child_pid) {
                        identify_process_role(&cmd).to_string()
                    } else {
                        "衍生工作进程".to_string()
                    };

                    let _ = app_handle.emit(
                        "log-line",
                        &format!("[进程衍生] ChatGPT 衍生子进程 [{}] -> PID: {}", role, child_pid),
                    );
                    known_children.insert(child_pid, role);
                }
            }

            // 2. 检测已销毁的子进程
            let mut exited_pids = Vec::new();
            for (&child_pid, role) in &known_children {
                if !family_pids.contains(&child_pid) {
                    exited_pids.push((child_pid, role.clone()));
                }
            }

            for (child_pid, role) in exited_pids {
                known_children.remove(&child_pid);
                let _ = app_handle.emit(
                    "log-line",
                    &format!("[进程销毁] 子进程 [{}] (PID: {}) 已退出销毁", role, child_pid),
                );
            }
        }
    });
}

/// 启动 UDP 日志广播监听器 (监听 127.0.0.1:52252)
pub fn start_udp_log_receiver(app_handle: AppHandle) {
    tauri::async_runtime::spawn(async move {
        let addr = "127.0.0.1:52252";
        let socket = match tokio::net::UdpSocket::bind(addr).await {
            Ok(s) => s,
            Err(_) => {
                // 如果端口已被占用，不影响主程序运行
                return;
            }
        };

        let mut buf = [0u8; 65536];
        let mut batch = crate::log_buffer::LogBatch::default();
        let mut timer = tokio::time::interval(Duration::from_millis(200));
        timer.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Skip);
        let mut ticks = 0u8;
        loop {
            tokio::select! {
              _ = timer.tick() => {
                ticks = (ticks + 1) % 5;
                let lines = batch.take(ticks == 0);
                if !lines.is_empty() { let _ = app_handle.emit("log-batch", lines); }
              }
              received = socket.recv_from(&mut buf) => match received {
                Ok((size, _peer)) => {
                    if let Ok(text) = std::str::from_utf8(&buf[..size]) {
                        let trimmed = text.trim();
                        if !trimmed.is_empty() {
                            batch.add(trimmed);
                            if batch.flush_full() { let _ = app_handle.emit("log-batch", batch.take(false)); }
                        }
                    }
                }
                Err(_) => {
                    tokio::time::sleep(Duration::from_millis(100)).await;
                }
              }
            }
        }
    });
}
