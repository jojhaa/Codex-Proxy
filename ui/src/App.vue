<template>
  <div class="w-screen h-screen bg-slate-50 dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded-2xl flex flex-col overflow-hidden shadow-2xl transition-colors relative">
    <!-- 1. 沉浸式标题栏 -->
    <TitleBar
      :themeMode="themeMode"
      @toggle-settings="showSettings = !showSettings"
      @cycle-theme="cycleTheme"
      @request-close="showCloseConfirm = true"
      @drag-state="handleDragState"
    />

    <!-- 2. 主内容区 (左右双栏与设置页视角) -->
    <div class="flex-1 p-4 overflow-hidden relative">
      <!-- 主视图 (状态 + 配置 + 2x2卡片 + 操作大按钮 / 右侧日志控制台) -->
      <div v-if="!showSettings" class="grid grid-cols-12 gap-3.5 h-full">
        <!-- 左侧控制面板 (7 列) -->
        <div class="col-span-7 flex flex-col overflow-y-auto pr-0.5">
          <!-- 状态看板卡片 -->
          <StatusCard :status="status" />

          <!-- 代理配置卡片 -->
          <ProxyConfigCard
            :config="config"
            v-model:currentMode="currentMode"
            :testResult="testResult"
            @test-port="handleTestPort"
            @save-config="handleSaveConfig"
          />

          <!-- 2x2 指标卡片 -->
          <MetricsGrid />

          <!-- 单一状态切换按钮：未启动时为启动，启动后为关闭 -->
          <div class="mt-auto">
            <button
              @click="handleToggleProxy"
              :disabled="launching || stopping"
              class="w-full py-3.5 font-bold text-sm rounded-xl shadow-md flex items-center justify-center space-x-2 transition-all duration-200 active:scale-[0.99] cursor-pointer"
              :class="status.running
                ? 'bg-gradient-to-r from-rose-600 via-red-600 to-pink-600 hover:from-rose-500 hover:to-pink-500 text-white shadow-rose-500/20'
                : 'bg-gradient-to-r from-emerald-600 via-teal-600 to-sky-600 hover:from-emerald-500 hover:to-sky-500 text-white shadow-emerald-500/20'"
            >
              <RefreshCw v-if="launching || stopping" class="w-4 h-4 animate-spin mr-1" />
              <span v-if="launching">正在定位并启动…</span>
              <span v-else-if="stopping">正在关闭专属代理…</span>
              <span v-else-if="status.running">🛑 关闭专属代理</span>
              <span v-else>🚀 启动专属代理</span>
            </button>
          </div>
        </div>

        <!-- 右侧全高度实时诊断控制台 (5 列) -->
        <div class="col-span-5 h-full min-h-0 flex flex-col overflow-hidden">
          <LiveConsole :logs="logs" :full-logs="fullLogs" :paused="windowDragging" @clear="clearLogs" @copy="handleCopyLogs" @toggle-full="requestFullLogs" />
        </div>
      </div>

      <!-- 高级设置中心覆盖页 -->
      <div v-else class="h-full">
        <SettingsModal
          :config="config"
          :autoStartInitial="autoStart"
          :themeMode="themeMode"
          @close="showSettings = false"
          @save-all="handleSaveAllSettings"
          @toggle-autostart="handleToggleAutoStart"
          @create-shortcut="handleCreateShortcut"
          @update:themeMode="setTheme"
          @redetect-path="handleRedetectPath"
          @show-notice="showFirstLaunchModal = true"
        />
      </div>
    </div>

    <!-- 3. 底部 Status Footer -->
    <FooterBar @toggle-settings="showSettings = !showSettings" />

    <dialog ref="logDialog" aria-labelledby="full-log-title" class="m-auto max-w-md rounded-xl p-5 bg-white text-slate-900 dark:bg-slate-900 dark:text-slate-100 shadow-xl backdrop:bg-black/50">
      <h2 id="full-log-title" class="font-bold text-base">开启完整日志？</h2>
      <p class="my-3 text-sm leading-6">完整日志会保留开启后收到的日志明细，可能占用大量内存，并导致卡顿。仅建议排障时临时开启。此前已合并或丢失的日志无法恢复；关闭后只保留最近日志。此选项重启后自动关闭。</p>
      <div class="flex justify-end gap-3">
        <button autofocus class="rounded border px-3 py-2" @click="logDialog?.close()">取消</button>
        <button class="rounded bg-amber-700 text-white px-3 py-2" @click="enableFullLogs">确认开启</button>
      </div>
    </dialog>
    <!-- 4. 关闭拦截/建议最小化弹窗 -->
    <CloseConfirmModal
      v-if="showCloseConfirm"
      @minimize="handleConfirmMinimize"
      @cancel="showCloseConfirm = false"
    />

    <!-- 5. 首次启动开源与反倒卖提醒弹窗 -->
    <FirstLaunchModal
      v-if="showFirstLaunchModal"
      @confirm="handleConfirmFirstLaunch"
    />
  </div>
</template>

<script setup lang="ts">
// Copyright (c) Time Silent. SPDX-License-Identifier: GPL-3.0-only
import { diagnosticHeader } from './project-info';
import { ref, shallowRef, watch, onMounted, onUnmounted } from "vue";
import { LogBuffer, type LogEntry } from "./log-buffer";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWindow } from "@tauri-apps/api/window";

import TitleBar from "./components/TitleBar.vue";
import StatusCard from "./components/StatusCard.vue";
import ProxyConfigCard from "./components/ProxyConfigCard.vue";
import MetricsGrid from "./components/MetricsGrid.vue";
import LiveConsole from "./components/LiveConsole.vue";
import SettingsModal from "./components/SettingsModal.vue";
import FooterBar from "./components/FooterBar.vue";
import CloseConfirmModal from "./components/CloseConfirmModal.vue";
import FirstLaunchModal from "./components/FirstLaunchModal.vue";
import { RefreshCw } from "lucide-vue-next";

type ThemeMode = "light" | "dark" | "system";

const appWindow = getCurrentWindow();

// 默认明亮模式
const themeMode = ref<ThemeMode>("light");
const currentMode = ref("standard");
const launching = ref(false);
const stopping = ref(false);
let resolvingPath: Promise<void> | null = null;
const refreshClientPath = (forceAuto = false): Promise<void> => {
  if (resolvingPath) return resolvingPath;
  resolvingPath = (async () => {
    try {
      const targetParam = forceAuto ? null : (config.value.path_mode === "custom" && config.value.target_app_path ? config.value.target_app_path : null);
      const resolved = await invoke<string>("resolve_client_path", { targetPath: targetParam });
      config.value.target_app_path = resolved;
      if (forceAuto || resolved.includes("WindowsApps")) {
        config.value.path_mode = "auto";
      }
      appendLog(`[路径解析] 目标客户端: ${resolved}`);
    } catch (error) {
      appendLog(`获取主程序路径失败：${error}`);
    } finally { resolvingPath = null; }
  })();
  return resolvingPath;
};

const handleRedetectPath = async () => {
  appendLog("正在重新探测微软商店最新版 ChatGPT 物理路径...");
  await refreshClientPath(true);
};
const showSettings = ref(false);
const showCloseConfirm = ref(false);
const showFirstLaunchModal = ref(false);
const autoStart = ref(false);
const testResult = ref("TCP 端口待检查");

const config = ref({
  host: "127.0.0.1",
  port: "7890",
  proto: "socks5",
  remote_dns_domains: ["openai.com", "chatgpt.com", "oaistatic.com", "oaiusercontent.com"],
  sub_processes: "codex.exe; node.exe; electron.exe",
  bypass_rules: "<-loopback>; localhost; 127.0.0.1; ::1",
  target_app_path: "",
  path_mode: "auto",
});

const status = ref({
  running: false,
  title: "ChatGPT 客户端未运行",
  sub: "使用代理参数启动；实际联网效果需验证",
  badge_label: "空闲就绪",
  badge_color: "#64748B",
});

const logBuffer = new LogBuffer();
const logs = shallowRef<LogEntry[]>([]);
const fullLogs = ref(false);
const windowDragging = ref(false);
const flushVisibleLogs = () => {
  if (document.hidden || windowDragging.value) return;
  const snapshot = logBuffer.flush();
  if (snapshot) logs.value = snapshot;
};
const handleDragState = (active: boolean) => {
  windowDragging.value = active;
  if (!active) flushVisibleLogs();
};
let logModeChanging = false;
const logDialog = ref<HTMLDialogElement | null>(null);
const clearLogs = () => { logBuffer.clear(); logs.value = []; };
const applyFullLogs = async (enabled: boolean) => {
  if (logModeChanging) return;
  logModeChanging = true;
  try {
    await invoke("set_full_logs", { enabled });
    logBuffer.setFull(enabled); fullLogs.value = enabled;
    appendLog(enabled ? '[警告] 完整日志已开启，可能占用大量内存' : '[系统] 已恢复低内存日志模式');
  } catch (error) { appendLog(`[错误] 切换日志模式失败：${error}`); }
  finally { logModeChanging = false; }
};
const requestFullLogs = () => { if (fullLogs.value) void applyFullLogs(false); else logDialog.value?.showModal(); };
const enableFullLogs = () => { logDialog.value?.close(); void applyFullLogs(true); };

let statusTimer: number | null = null;
let unlistenLog: (() => void) | null = null;
let unlistenLogBatch: (() => void) | null = null;
let unlistenClientPath: (() => void) | null = null;
let unlistenCloseRequested: (() => void) | null = null;
let mediaQuery: MediaQueryList | null = null;

const appendLog = (msg: string) => {
  logBuffer.add(String(msg));
};
appendLog('[系统] 管理控制台已就绪，默认使用低内存日志');
const logTimer = window.setInterval(flushVisibleLogs, 500);

// 主题切换控制
const applyTheme = (mode: ThemeMode) => {
  themeMode.value = mode;
  localStorage.setItem("codex-theme-mode", mode);

  let isDark = false;
  if (mode === "dark") {
    isDark = true;
  } else if (mode === "system") {
    isDark = window.matchMedia("(prefers-color-scheme: dark)").matches;
  } else {
    isDark = false;
  }

  if (isDark) {
    document.documentElement.classList.add("dark");
  } else {
    document.documentElement.classList.remove("dark");
  }
};

const setTheme = (mode: ThemeMode) => {
  applyTheme(mode);
  const nameMap = { light: "明亮模式", dark: "暗黑模式", system: "跟随系统" };
  appendLog(`界面主题已切换为: ${nameMap[mode]}`);
};

const cycleTheme = () => {
  const nextMap: Record<ThemeMode, ThemeMode> = {
    light: "dark",
    dark: "system",
    system: "light",
  };
  setTheme(nextMap[themeMode.value]);
};

const handleSystemThemeChange = () => {
  if (themeMode.value === "system") {
    applyTheme("system");
    appendLog("检测到系统主题变更，已自动匹配");
  }
};

// 关闭确认弹窗处理（最小化到系统托盘）
const handleConfirmMinimize = async () => {
  showCloseConfirm.value = false;
  try {
    await invoke("hide_to_tray");
  } catch {
    await appWindow.hide();
  }
  appendLog("[窗口] 代理服务保持后台运行，窗口已最小化到系统托盘");
};

// 首次启动开源与反倒卖提醒确认处理
const handleConfirmFirstLaunch = () => {
  localStorage.setItem("codex-open-source-notice-ack", "true");
  showFirstLaunchModal.value = false;
  appendLog("[开源提醒] 用户已查阅并确认知晓永久免费与反倒卖声明。");
};

const loadModeAndConfig = async () => {
  try {
    const mode = await invoke<string>("get_launcher_mode");
    currentMode.value = mode;
    const cfg = await invoke<any>("get_proxy_config", { mode });
    config.value = cfg;
    await refreshClientPath();
    appendLog(`已加载模式 [${mode === "process" ? "进程代理" : "标准代理"}] 配置`);
  } catch (e: any) {
    appendLog(`加载配置异常: ${e}`);
  }
};

watch(currentMode, async (newMode) => {
  try {
    await invoke("set_launcher_mode", { mode: newMode });
    const cfg = await invoke<any>("get_proxy_config", { mode: newMode });
    config.value = cfg;
    await refreshClientPath();
    appendLog(`代理模式切换为: ${newMode === "process" ? "进程代理" : "标准代理"}`);
  } catch (e: any) {
    appendLog(`切换代理模式失败: ${e}`);
  }
});

const handleTestPort = async () => {
  testResult.value = "检查中...";
  try {
    const portNum = parseInt(config.value.port, 10) || 7890;
    const res = await invoke<string>("check_proxy_port", {
      host: config.value.host,
      port: portNum,
    });
    testResult.value = res;
    appendLog(res);
  } catch (err: any) {
    testResult.value = err;
    appendLog(err);
  }
};

const handleSaveConfig = async () => {
  try {
    await invoke("save_proxy_config", {
      mode: currentMode.value,
      config: config.value,
    });
    appendLog("代理配置保存成功");
  } catch (err: any) {
    appendLog(`保存配置失败: ${err}`);
  }
};

const handleSaveAllSettings = async () => {
  await handleSaveConfig();
  showSettings.value = false;
};

const handleToggleProxy = async () => {
  if (status.value.running) {
    await handleStop();
  } else {
    await handleLaunch();
  }
};

const handleLaunch = async () => {
  if (launching.value || stopping.value) return;
  launching.value = true;
  try {
    const targetParam = config.value.path_mode === "custom" && config.value.target_app_path ? config.value.target_app_path : null;
    const res = await invoke<string>("launch_client", {
      mode: currentMode.value,
      targetPath: targetParam,
    });
    appendLog(`[启动指令] ${res}`);
    status.value.running = true;
    await pollAppStatus();
  } catch (err: any) {
    appendLog(`[启动失败] ${err}`);
  } finally {
    launching.value = false;
  }
};

watch(showSettings, (visible) => { if (visible) void refreshClientPath(); });

const handleStop = async () => {
  if (stopping.value || launching.value) return;
  stopping.value = true;
  try {
    const res = await invoke<string>("stop_client");
    appendLog(`[停止指令] ${res}`);
    status.value.running = false;
    await pollAppStatus();
  } catch (err: any) {
    appendLog(`[停止失败] ${err}`);
  } finally {
    stopping.value = false;
  }
};

const handleToggleAutoStart = async (val: boolean) => {
  try {
    await invoke("toggle_auto_start", { enable: val });
    appendLog(`开机自启已 ${val ? "开启" : "关闭"}`);
  } catch (err: any) {
    appendLog(`设置开机自启失败: ${err}`);
  }
};

const handleCreateShortcut = async () => {
  try {
    const res = await invoke<string>("create_desktop_shortcut");
    appendLog(res);
  } catch (err: any) {
    appendLog(`创建桌面图标失败: ${err}`);
  }
};

const handleCopyLogs = () => {
  navigator.clipboard.writeText(diagnosticHeader() + '\n' + logBuffer.copyText());
  appendLog("日志已复制到剪贴板");
};

const pollAppStatus = async () => {
  try {
    const st = await invoke<any>("get_app_status");
    status.value = st;
  } catch (e) {
    // ignore
  }
};

onMounted(async () => {
  // 读取已保存的主题模式偏好，默认 'light' (明亮模式)
  const savedTheme = (localStorage.getItem("codex-theme-mode") as ThemeMode) || "light";
  applyTheme(savedTheme);

  // 监听系统主题变化
  mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
  mediaQuery.addEventListener("change", handleSystemThemeChange);

  // 拦截窗口关闭事件（Alt+F4 或任务栏关闭）
  try {
    unlistenCloseRequested = await appWindow.onCloseRequested(async (event) => {
      event.preventDefault();
      showCloseConfirm.value = true;
    });
  } catch (e) {
    // ignore
  }

  await loadModeAndConfig();

  try {
    autoStart.value = await invoke<boolean>("check_auto_start");
  } catch (e) {
    // ignore
  }

  unlistenLog = await listen<string>("log-line", (evt) => {
    appendLog(evt.payload);
  });
  unlistenLogBatch = await listen<string[]>("log-batch", (evt) => {
    for (const line of evt.payload) appendLog(line);
  });
  unlistenClientPath = await listen<string>("client-path", (evt) => {
    config.value.target_app_path = evt.payload;
    config.value.path_mode = "auto";
  });

  await pollAppStatus();
  statusTimer = window.setInterval(pollAppStatus, 3000);

  // 首次运行检测：未确认开源与反倒卖声明时主动弹窗提醒
  const noticeAck = localStorage.getItem("codex-open-source-notice-ack");
  if (!noticeAck) {
    showFirstLaunchModal.value = true;
  }
});

onUnmounted(() => {
  clearInterval(logTimer);
  if (unlistenLogBatch) unlistenLogBatch();
  if (statusTimer) clearInterval(statusTimer);
  if (unlistenLog) unlistenLog();
  if (unlistenClientPath) unlistenClientPath();
  if (unlistenCloseRequested) unlistenCloseRequested();
  if (mediaQuery) mediaQuery.removeEventListener("change", handleSystemThemeChange);
});
</script>
