<template>
  <div
    @mousedown="onMouseDown"
    class="h-11 bg-slate-100/90 dark:bg-slate-900/90 px-4 flex items-center justify-between select-none cursor-move border-b border-slate-200 dark:border-slate-800/80 transition-colors"
  >
    <div class="flex items-center space-x-2.5 pointer-events-none">
      <AppLogo class="w-[18px] h-[18px] shrink-0" />
      <span class="text-xs font-bold text-slate-800 dark:text-slate-100 tracking-wide">ProcWeaver Pro</span>
      <span class="text-[10px] font-semibold bg-emerald-50 dark:bg-emerald-950/80 text-emerald-700 dark:text-emerald-400 px-2 py-0.5 rounded border border-emerald-200 dark:border-emerald-700/50">
        SOCKS5h 远端 DNS
      </span>
    </div>

    <div class="flex items-center space-x-1 cursor-default">
      <!-- 快捷主题切换按钮 -->
      <button
        @click="$emit('cycle-theme')"
        :title="themeTitle"
        class="w-8 h-7 flex items-center justify-center rounded-lg text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-100 hover:bg-slate-200/80 dark:hover:bg-slate-800/80 transition-colors"
      >
        <Sun v-if="themeMode === 'light'" class="w-3.5 h-3.5 text-amber-500" />
        <Moon v-else-if="themeMode === 'dark'" class="w-3.5 h-3.5 text-sky-400" />
        <Monitor v-else class="w-3.5 h-3.5 text-slate-500 dark:text-slate-400" />
      </button>

      <!-- 设置中心按钮 -->
      <button
        @click="$emit('toggle-settings')"
        title="进程与 DNS 域名规则设置"
        class="w-8 h-7 flex items-center justify-center rounded-lg text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-100 hover:bg-slate-200/80 dark:hover:bg-slate-800/80 transition-colors"
      >
        <Settings class="w-3.5 h-3.5" />
      </button>

      <!-- 最小化 -->
      <button
        @click="minimize"
        title="最小化"
        class="w-8 h-7 flex items-center justify-center rounded-lg text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-100 hover:bg-slate-200/80 dark:hover:bg-slate-800/80 transition-colors"
      >
        <Minus class="w-3.5 h-3.5" />
      </button>

      <!-- 关闭按钮：触发确认弹窗 -->
      <button
        @click="$emit('request-close')"
        title="关闭"
        class="w-8 h-7 flex items-center justify-center rounded-lg text-slate-500 dark:text-slate-400 hover:text-rose-600 hover:bg-rose-50 dark:hover:bg-rose-950/40 transition-colors"
      >
        <X class="w-3.5 h-3.5" />
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount } from "vue";
import { invoke } from '@tauri-apps/api/core';
import { Settings, Minus, X, Sun, Moon, Monitor } from "lucide-vue-next";
import { getCurrentWindow } from "@tauri-apps/api/window";
import AppLogo from "./AppLogo.vue";

const props = defineProps<{
  themeMode: "light" | "dark" | "system";
}>();

const emit = defineEmits(["toggle-settings", "cycle-theme", "request-close", "drag-state"]);

const appWindow = getCurrentWindow();

let dragActive = false;
let dragTimer: ReturnType<typeof setTimeout> | undefined;
let dragGeneration = 0;
const finishDrag = () => {
  if (!dragActive) return;
  dragActive = false;
  dragGeneration++;
  clearTimeout(dragTimer);
  window.removeEventListener('mouseup', finishDrag, true);
  window.removeEventListener('keydown', cancelDrag, true);
  emit('drag-state', false);
};
const cancelDrag = (event: KeyboardEvent) => { if (event.key === 'Escape') finishDrag(); };
const pollRelease = async (generation: number) => {
  try {
    const down = await invoke<boolean>('is_drag_button_down');
    if (generation !== dragGeneration || !dragActive) return;
    if (!down) { finishDrag(); return; }
    dragTimer = setTimeout(() => void pollRelease(generation), 100);
  } catch { if (generation === dragGeneration) finishDrag(); }
};
const onMouseDown = (e: MouseEvent) => {
  if (e.button !== 0 || dragActive || (e.target as HTMLElement).closest("button")) return;
  dragActive = true;
  const generation = ++dragGeneration;
  emit('drag-state', true);
  window.addEventListener('mouseup', finishDrag, true);
  window.addEventListener('keydown', cancelDrag, true);
  dragTimer = setTimeout(() => void pollRelease(generation), 100);
  // startDragging's return is not treated as proof that the native drag ended.
  void appWindow.startDragging().catch(() => { if (generation === dragGeneration) finishDrag(); });
};
onBeforeUnmount(finishDrag);

const minimize = () => appWindow.minimize();

const themeTitle = computed(() => {
  if (props.themeMode === "light") return "当前：明亮模式 (点击切换为暗黑)";
  if (props.themeMode === "dark") return "当前：暗黑模式 (点击切换为跟随系统)";
  return "当前：跟随系统 (点击切换为明亮模式)";
});
</script>
