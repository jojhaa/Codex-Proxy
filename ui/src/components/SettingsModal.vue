<template>
  <div class="h-full bg-white dark:bg-slate-900 border border-slate-300 dark:border-slate-800 rounded-xl p-4 flex flex-col overflow-hidden shadow-xl transition-colors">
    <!-- Header -->
    <div class="flex items-center justify-between pb-3 border-b border-slate-200 dark:border-slate-800 mb-4 shrink-0">
      <div class="flex items-center space-x-2">
        <Settings class="w-4 h-4 text-emerald-600 dark:text-emerald-400" />
        <h2 class="text-sm font-bold text-slate-900 dark:text-slate-100">进程与 DNS 域名规则设置中心</h2>
      </div>
      <button
        @click="$emit('close')"
        class="px-3 py-1 text-xs text-slate-700 dark:text-slate-300 hover:text-slate-900 dark:hover:text-slate-100 bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 rounded-lg border border-slate-300 dark:border-slate-700 transition-colors"
      >
        ← 返回主界面
      </button>
    </div>

    <!-- Scroll Content -->
    <div class="flex-1 overflow-y-auto pr-1 space-y-4 text-xs">
      <!-- 0. 外观与主题模式设置 -->
      <div>
        <h3 class="font-bold text-indigo-700 dark:text-indigo-400 mb-2 flex items-center space-x-1.5">
          <Palette class="w-3.5 h-3.5" />
          <span>界面外观与主题模式</span>
        </h3>
        <div class="bg-slate-50 dark:bg-slate-950 p-3 rounded-lg border border-slate-200 dark:border-slate-800 flex items-center justify-between transition-colors">
          <div class="flex items-center space-x-2">
            <span class="text-slate-800 dark:text-slate-200 font-medium">界面主题：</span>
            <span class="text-[11px] text-slate-500 dark:text-slate-400">(默认明亮模式)</span>
          </div>
          <div class="flex items-center bg-slate-200/70 dark:bg-slate-900 p-1 rounded-lg border border-slate-300 dark:border-slate-800">
            <button
              @click="$emit('update:themeMode', 'light')"
              class="flex items-center space-x-1 px-3 py-1 text-xs rounded-md font-medium transition-all"
              :class="themeMode === 'light'
                ? 'bg-white dark:bg-slate-800 text-amber-600 dark:text-amber-400 shadow-sm font-bold'
                : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'"
            >
              <Sun class="w-3 h-3" />
              <span>明亮</span>
            </button>
            <button
              @click="$emit('update:themeMode', 'dark')"
              class="flex items-center space-x-1 px-3 py-1 text-xs rounded-md font-medium transition-all"
              :class="themeMode === 'dark'
                ? 'bg-white dark:bg-slate-800 text-sky-600 dark:text-sky-400 shadow-sm font-bold'
                : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'"
            >
              <Moon class="w-3 h-3" />
              <span>暗黑</span>
            </button>
            <button
              @click="$emit('update:themeMode', 'system')"
              class="flex items-center space-x-1 px-3 py-1 text-xs rounded-md font-medium transition-all"
              :class="themeMode === 'system'
                ? 'bg-white dark:bg-slate-800 text-emerald-600 dark:text-emerald-400 shadow-sm font-bold'
                : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'"
            >
              <Monitor class="w-3 h-3" />
              <span>跟随系统</span>
            </button>
          </div>
        </div>
      </div>

      <!-- 1. 系统辅助 -->
      <div>
        <h3 class="font-bold text-amber-700 dark:text-amber-400 mb-2 flex items-center space-x-1.5">
          <Pin class="w-3.5 h-3.5" />
          <span>系统辅助与快捷操作</span>
        </h3>
        <div class="bg-slate-50 dark:bg-slate-950 p-3 rounded-lg border border-slate-200 dark:border-slate-800 flex items-center justify-between transition-colors">
          <label class="flex items-center space-x-2 cursor-pointer">
            <input
              type="checkbox"
              v-model="autoStart"
              @change="$emit('toggle-autostart', autoStart)"
              class="rounded bg-white dark:bg-slate-900 border-slate-300 dark:border-slate-700 text-emerald-600 focus:ring-0 focus:ring-offset-0"
            />
            <span class="text-slate-800 dark:text-slate-200 font-medium">开机自动启动 (Run 键值)</span>
          </label>
          <div class="flex items-center space-x-2">
            <button
              @click="$emit('create-shortcut')"
              class="px-2.5 py-1 text-[11px] bg-white dark:bg-slate-800 hover:bg-slate-100 dark:hover:bg-slate-700 text-slate-700 dark:text-slate-200 rounded border border-slate-300 dark:border-slate-700 transition-colors shadow-sm"
            >
              📌 生成桌面图标
            </button>
          </div>
        </div>
      </div>

      <!-- 2. 托管进程与环境变量配置 -->
      <div>
        <h3 class="font-bold text-emerald-700 dark:text-emerald-400 mb-2 flex items-center space-x-1.5">
          <Wrench class="w-3.5 h-3.5" />
          <span>托管进程与环境变量配置</span>
        </h3>
        <div class="space-y-2">
          <div>
            <div class="flex items-center justify-between mb-1">
              <label class="text-slate-600 dark:text-slate-400 text-[11px] font-medium flex items-center space-x-1.5">
                <span>主程序安装/启动路径</span>
                <span v-if="isAutoDetected" class="px-1.5 py-0.5 text-[10px] rounded bg-emerald-100 dark:bg-emerald-950 text-emerald-700 dark:text-emerald-300 font-normal">
                  ✓ 微软商店版 (每次更新自动适配物理路径)
                </span>
                <span v-else-if="config.target_app_path" class="px-1.5 py-0.5 text-[10px] rounded bg-blue-100 dark:bg-blue-950 text-blue-700 dark:text-blue-300 font-normal">
                  自定义路径
                </span>
              </label>
              <button
                type="button"
                @click="$emit('redetect-path')"
                class="text-[11px] text-emerald-600 dark:text-emerald-400 hover:text-emerald-700 dark:hover:text-emerald-300 flex items-center space-x-1 cursor-pointer transition-colors"
              >
                <span>🔄 重新探测商店版</span>
              </button>
            </div>
            <div class="relative">
              <input
                v-model="config.target_app_path"
                placeholder="点击重新探测或输入 ChatGPT.exe 路径"
                type="text"
                class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded-lg px-3 py-1.5 text-[11px] text-slate-900 dark:text-slate-100 focus:outline-none focus:border-emerald-600 dark:focus:border-emerald-500 focus:bg-white dark:focus:bg-slate-950 font-mono"
              />
            </div>
            <p class="text-[10px] text-slate-400 dark:text-slate-500 mt-1">
              提示：微软应用商店每次更新 ChatGPT 时均会变更版本号物理目录，系统已启用动态内核探测，每次启动都会自动寻找最新 <span class="font-mono">app\ChatGPT.exe</span>。
            </p>
          </div>

          <div>
            <label class="block text-slate-600 dark:text-slate-400 text-[11px] mb-1">自动继承代理的衍生子进程白名单 (分号分隔)</label>
            <input
              v-model="config.sub_processes"
              type="text"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded-lg px-3 py-1.5 text-slate-900 dark:text-slate-100 focus:outline-none focus:border-emerald-600 dark:focus:border-emerald-500 focus:bg-white dark:focus:bg-slate-950"
            />
          </div>
        </div>
      </div>

      <!-- 3. DNS 域名设置 -->
      <div>
        <h3 class="font-bold text-sky-700 dark:text-sky-400 mb-2 flex items-center space-x-1.5">
          <Globe class="w-3.5 h-3.5" />
          <span>SOCKS5h 远端 DNS 解析匹配域名名单 (分号 / 换行分隔)</span>
        </h3>
        <div class="space-y-2">
          <textarea
            v-model="domainsText"
            rows="3"
            class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded-lg p-2.5 text-slate-900 dark:text-slate-100 focus:outline-none focus:border-emerald-600 dark:focus:border-emerald-500 focus:bg-white dark:focus:bg-slate-950 font-mono text-[11px]"
          ></textarea>

          <div>
            <label class="block text-slate-600 dark:text-slate-400 text-[11px] mb-1">Bypass 本地直连与豁免白名单 (分号分隔)</label>
            <input
              v-model="config.bypass_rules"
              type="text"
              class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded-lg px-3 py-1.5 text-slate-900 dark:text-slate-100 focus:outline-none focus:border-emerald-600 dark:focus:border-emerald-500 focus:bg-white dark:focus:bg-slate-950"
            />
          </div>
        </div>
      </div>

      <!-- 4. 关于本项目与版本服务 & 更新检测 -->
      <div>
        <h3 class="font-bold text-violet-700 dark:text-violet-400 mb-2 flex items-center space-x-1.5">
          <Info class="w-3.5 h-3.5" />
          <span>关于本项目与版本服务</span>
        </h3>
        <div class="bg-slate-50 dark:bg-slate-950 p-3.5 rounded-lg border border-slate-200 dark:border-slate-800 space-y-3 transition-colors">
          <div class="flex items-center justify-between">
            <div>
              <div class="flex items-center space-x-2">
                <span class="font-bold text-slate-900 dark:text-slate-100">{{ projectInfo.name }} Pro</span>
                <span class="text-[10px] font-medium text-emerald-600 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-950/60 px-1.5 py-0.5 rounded border border-emerald-200 dark:border-emerald-800">织程代理</span>
                <span class="text-xs font-bold text-slate-600 dark:text-slate-300">v{{ projectInfo.version }}</span>
                <span class="text-[11px] text-slate-500 dark:text-slate-400">(Tauri 2.0 驱动)</span>
              </div>
              <p class="text-[11px] text-slate-500 dark:text-slate-400 mt-1">作者：{{ projectInfo.author }} · {{ projectInfo.license }}</p>
              <p class="text-[11px] text-slate-500 dark:text-slate-400 break-all">{{ projectInfo.repository }}</p>
              <p class="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">
                零驱动 · 进程精准代理 · SOCKS5h 防污染远端 DNS · 永久开源免费
              </p>
            </div>

            <div class="flex items-center space-x-2">
              <button
                type="button"
                @click="$emit('show-notice')"
                class="flex items-center space-x-1.5 px-2.5 py-1.5 text-xs rounded-lg font-medium bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-700 dark:text-slate-300 border border-slate-300 dark:border-slate-700 transition-all cursor-pointer shadow-xs active:scale-95"
              >
                <ShieldAlert class="w-3.5 h-3.5 text-emerald-600 dark:text-emerald-400" />
                <span>开源须知</span>
              </button>

              <!-- 检查更新按钮 -->
              <button
                @click="handleCheckUpdate"
                :disabled="checkingUpdate"
                class="flex items-center space-x-1.5 px-3 py-1.5 text-xs rounded-lg font-bold transition-all shadow-sm"
                :class="checkingUpdate
                  ? 'bg-slate-200 dark:bg-slate-800 text-slate-400 cursor-not-allowed'
                  : 'bg-emerald-600 hover:bg-emerald-500 active:scale-95 text-white'"
              >
                <RefreshCw class="w-3.5 h-3.5" :class="{ 'animate-spin': checkingUpdate }" />
                <span>{{ checkingUpdate ? '正在检测...' : '检查更新' }}</span>
              </button>
            </div>
          </div>

          <!-- 项目核心介绍与版权/图标声明 -->
          <div class="pt-2.5 border-t border-slate-200/80 dark:border-slate-800/80 space-y-2 text-[11px] leading-relaxed">
            <p class="text-slate-600 dark:text-slate-400">
              <span class="font-semibold text-slate-800 dark:text-slate-200">项目简介：</span>
              ProcWeaver Pro（织程代理）是一款专为 Windows 客户端量身定制的高性能代理控制台。支持标准环境变量桥接与底层进程拦截双引擎，实现免驱动、防 DNS 污染的低延迟稳定通信，全程不修改全局网络配置。
            </p>

            <div class="p-2.5 rounded-lg bg-indigo-50/60 dark:bg-indigo-950/30 border border-indigo-200/60 dark:border-indigo-900/40 text-[11px] text-indigo-900 dark:text-indigo-200 flex items-start space-x-2">
              <Palette class="w-4 h-4 text-indigo-600 dark:text-indigo-400 shrink-0 mt-0.5" />
              <div>
                <span class="font-bold text-indigo-800 dark:text-indigo-300">视觉资产声明：</span>
                本应用桌面与托盘图标均采用 AI 绘图制作，纯用于开源社区视觉呈现与技术交流。如涉及任何原创版权或视觉侵权疑虑，请随时联系作者（或通过 GitHub Issue 反馈），我们将在第一时间核实并配合调整与更换。
              </div>
            </div>
          </div>

          <!-- 更新状态卡片 -->
          <div
            v-if="updateInfo"
            class="pt-2.5 border-t border-slate-200 dark:border-slate-800/80 text-[11px]"
          >
            <!-- 发现新版本 -->
            <div v-if="updateInfo.hasUpdate" class="p-2.5 bg-amber-50 dark:bg-amber-950/40 border border-amber-200 dark:border-amber-800/60 rounded-lg">
              <div class="flex items-center justify-between">
                <div class="flex items-center space-x-1.5 font-bold text-amber-800 dark:text-amber-300">
                  <ArrowUpCircle class="w-4 h-4 text-amber-600 dark:text-amber-400" />
                  <span>发现新版本：{{ updateInfo.latestVersion }}</span>
                  <span v-if="updateInfo.usedProxy" class="text-[10px] font-normal text-amber-600 dark:text-amber-400">
                    (经 gh-proxy.org 代理加速)
                  </span>
                  <span v-else class="text-[10px] font-normal text-emerald-600 dark:text-emerald-400">
                    (GitHub 直连)
                  </span>
                </div>
                <button
                  @click="openExternalUrl(updateInfo.releaseUrl)"
                  class="px-2.5 py-1 text-xs bg-amber-600 hover:bg-amber-500 text-white font-bold rounded shadow-sm transition-all flex items-center space-x-1"
                >
                  <span>前往下载</span>
                  <ExternalLink class="w-3 h-3" />
                </button>
              </div>
              <p v-if="updateInfo.releaseNotes" class="mt-1.5 text-slate-700 dark:text-slate-300 whitespace-pre-wrap font-mono text-[10px] bg-white/60 dark:bg-slate-900/60 p-2 rounded border border-amber-100 dark:border-amber-900/50">
                {{ updateInfo.releaseNotes }}
              </p>
            </div>

            <!-- 已是最新版本 -->
            <div v-else-if="updateInfo.success" class="flex items-center justify-between p-2.5 bg-emerald-50 dark:bg-emerald-950/40 border border-emerald-200 dark:border-emerald-800/60 rounded-lg">
              <div class="flex items-center space-x-1.5 text-emerald-800 dark:text-emerald-300">
                <CheckCircle class="w-4 h-4 text-emerald-600 dark:text-emerald-400" />
                <span class="font-semibold">{{ updateInfo.message }}</span>
                <span class="text-[10px] text-slate-500 dark:text-slate-400">
                  {{ updateInfo.usedProxy ? '(通过 gh-proxy.org 代理通道)' : '(GitHub 直连)' }}
                </span>
              </div>
              <button
                @click="openExternalUrl(updateInfo.releaseUrl)"
                class="text-emerald-700 dark:text-emerald-400 hover:underline flex items-center space-x-0.5 text-[11px]"
              >
                <span>Releases 页面</span>
                <ExternalLink class="w-2.5 h-2.5 ml-0.5" />
              </button>
            </div>

            <!-- 检测失败 -->
            <div v-else class="flex items-center justify-between p-2.5 bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800/60 rounded-lg text-rose-800 dark:text-rose-300">
              <div class="flex items-center space-x-1.5">
                <AlertCircle class="w-4 h-4 text-rose-600 dark:text-rose-400 shrink-0" />
                <span>{{ updateInfo.message }}</span>
              </div>
              <button
                @click="handleCheckUpdate"
                class="px-2 py-0.5 text-xs bg-rose-100 dark:bg-rose-900/60 hover:bg-rose-200 rounded border border-rose-300 dark:border-rose-700 transition-colors shrink-0"
              >
                重试
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Save Button Footer -->
    <div class="pt-3 border-t border-slate-200 dark:border-slate-800 shrink-0 mt-2">
      <button
        @click="saveAll"
        class="w-full py-2.5 bg-gradient-to-r from-emerald-600 via-teal-600 to-cyan-600 hover:from-emerald-500 hover:to-cyan-500 text-white font-bold text-xs rounded-xl shadow-md transition-all duration-200 active:scale-[0.99]"
      >
        💾 保存进程、DNS 及系统设置
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { projectInfo } from '../project-info';
import { ref, watch, computed } from "vue";
import {
  Settings,
  Pin,
  Wrench,
  Globe,
  Info,
  Palette,
  Sun,
  Moon,
  Monitor,
  RefreshCw,
  CheckCircle,
  ArrowUpCircle,
  AlertCircle,
  ExternalLink,
  ShieldAlert,
} from "lucide-vue-next";
import { invoke } from "@tauri-apps/api/core";
import { openUrl } from "@tauri-apps/plugin-opener";

const props = defineProps<{
  config: {
    target_app_path: string;
    sub_processes: string;
    remote_dns_domains: string[];
    bypass_rules: string;
    path_mode?: string;
  };
  autoStartInitial: boolean;
  themeMode: "light" | "dark" | "system";
}>();

const emit = defineEmits(["close", "save-all", "toggle-autostart", "create-shortcut", "update:themeMode", "redetect-path", "show-notice"]);

const autoStart = ref(props.autoStartInitial);
const domainsText = ref(props.config.remote_dns_domains.join("; "));

const isAutoDetected = computed(() => {
  const p = props.config.target_app_path || "";
  return p.includes("WindowsApps") && p.includes("OpenAI.Codex");
});

interface UpdateInfo {
  success: boolean;
  hasUpdate: boolean;
  currentVersion: string;
  latestVersion: string;
  releaseUrl: string;
  releaseNotes: string;
  usedProxy: boolean;
  message: string;
}

const checkingUpdate = ref(false);
const updateInfo = ref<UpdateInfo | null>(null);

const handleCheckUpdate = async () => {
  checkingUpdate.value = true;
  try {
    const res = await invoke<UpdateInfo>("check_app_update");
    updateInfo.value = res;
  } catch (err: any) {
    updateInfo.value = {
      success: false,
      hasUpdate: false,
      currentVersion: `v${projectInfo.version}`,
      latestVersion: "",
      releaseUrl: `${projectInfo.repository}/releases`,
      releaseNotes: "",
      usedProxy: false,
      message: `检测异常: ${err}`,
    };
  } finally {
    checkingUpdate.value = false;
  }
};

const openExternalUrl = async (url: string) => {
  try {
    await openUrl(url);
  } catch {
    await invoke("open_external_url", { url });
  }
};

watch(
  () => props.config.remote_dns_domains,
  (val) => {
    domainsText.value = val.join("; ");
  }
);

const saveAll = () => {
  const parsedDomains = domainsText.value
    .split(/[;\n]/)
    .map((s) => s.trim())
    .filter((s) => s.length > 0);
  props.config.remote_dns_domains = parsedDomains;
  emit("save-all");
};
</script>
