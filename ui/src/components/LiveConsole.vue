<template>
  <div class="h-full min-h-0 bg-white dark:bg-slate-950 border border-slate-200 dark:border-slate-800 rounded-xl flex flex-col overflow-hidden shadow-sm dark:shadow-xl transition-colors relative">
    <!-- 顶部主标题栏 (Row 1) -->
    <div class="h-10 bg-slate-50/90 dark:bg-slate-900/90 px-3 flex items-center justify-between border-b border-slate-200 dark:border-slate-800 shrink-0 transition-colors">
      <div class="flex items-center space-x-2">
        <span class="relative flex h-2 w-2">
          <span class="relative inline-flex rounded-full h-2 w-2 bg-emerald-500"></span>
        </span>
        <span class="text-xs font-bold text-slate-800 dark:text-slate-100 tracking-wide">实时诊断控制台</span>
        <span class="px-1.5 py-0.5 text-[10px] font-mono rounded-full bg-slate-200/70 dark:bg-slate-800 text-slate-600 dark:text-slate-300 font-medium">
          {{ filteredLogs.length }}
        </span>
      </div>

      <!-- 右侧快捷操作按钮 -->
      <div class="flex items-center space-x-1.5">
        <button
          @click="handleCopy"
          :title="copied ? '已复制到剪贴板' : '复制当前日志'"
          class="px-2 py-1 text-[11px] font-medium text-slate-600 dark:text-slate-300 hover:text-slate-900 dark:hover:text-white bg-white dark:bg-slate-800 hover:bg-slate-100 dark:hover:bg-slate-700 rounded-lg border border-slate-200 dark:border-slate-700 transition-all flex items-center space-x-1 shadow-xs cursor-pointer active:scale-95"
        >
          <Check v-if="copied" class="w-3 h-3 text-emerald-600 dark:text-emerald-400" />
          <Copy v-else class="w-3 h-3 text-slate-500" />
          <span>{{ copied ? '已复制' : '复制' }}</span>
        </button>

        <button
          @click="$emit('clear')"
          title="清空控制台日志"
          class="px-2 py-1 text-[11px] font-medium text-slate-600 dark:text-slate-300 hover:text-rose-600 dark:hover:text-rose-400 bg-white dark:bg-slate-800 hover:bg-rose-50 dark:hover:bg-rose-950/40 rounded-lg border border-slate-200 dark:border-slate-700 hover:border-rose-200 dark:hover:border-rose-900 transition-all flex items-center space-x-1 shadow-xs cursor-pointer active:scale-95"
        >
          <Trash2 class="w-3 h-3 text-slate-400 group-hover:text-rose-500" />
          <span>清空</span>
        </button>
      </div>
    </div>

    <div class="flex items-center gap-2 px-3 py-1.5 text-xs border-b border-slate-200 dark:border-slate-800">
      <button type="button" role="switch" :aria-checked="fullLogs" aria-label="完整日志" @click="$emit('toggle-full')" class="flex items-center gap-2 rounded focus-visible:outline-2 focus-visible:outline-emerald-600">
        <span class="w-4 h-4 rounded border border-slate-400 flex items-center justify-center" :class="fullLogs ? 'bg-emerald-600 text-white' : 'bg-white dark:bg-slate-900'"><Check v-if="fullLogs" class="w-3 h-3" /></span>
        <span>完整日志</span>
      </button>
      <span v-if="fullLogs" class="text-amber-700 dark:text-amber-400">已开启 · 内存占用可能较高</span>
      <span v-else class="text-slate-500">默认关闭 · 低内存模式</span>
    </div>
    <!-- 二级分类药丸切换条 (Row 2, 4 等宽栅格，永不折行) -->
    <div class="bg-slate-100/70 dark:bg-slate-900/60 p-1.5 border-b border-slate-200/80 dark:border-slate-800/80 shrink-0">
      <div class="grid grid-cols-4 gap-1 w-full text-[11px]">
        <button
          v-for="tab in filterTabs"
          :key="tab.id"
          @click="activeFilter = tab.id"
          :class="[
            'py-1 px-1 rounded-md text-center font-medium transition-all cursor-pointer flex items-center justify-center space-x-1 truncate',
            activeFilter === tab.id
              ? 'bg-white dark:bg-slate-800 text-emerald-700 dark:text-emerald-400 shadow-xs border border-slate-200/70 dark:border-slate-700 font-semibold'
              : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200 hover:bg-white/50 dark:hover:bg-slate-800/40'
          ]"
        >
          <component :is="tab.icon" class="w-3 h-3 shrink-0" />
          <span class="truncate">{{ tab.label }}</span>
          <span
            class="text-[9px] font-mono px-1 rounded-full shrink-0"
            :class="activeFilter === tab.id
              ? 'bg-emerald-50 dark:bg-emerald-950 text-emerald-600 dark:text-emerald-400'
              : 'bg-slate-200/60 dark:bg-slate-800 text-slate-500 dark:text-slate-400'"
          >
            {{ tabCounts[tab.id] || 0 }}
          </span>
        </button>
      </div>
    </div>

    <!-- 日志流内容展示区 (核心：min-h-0 + flex-1 + overflow-y-auto + custom-scrollbar) -->
    <div
      ref="logContainer"
      @scroll="handleUserScroll"
      class="flex-1 min-h-0 p-2.5 overflow-y-auto font-mono text-[11px] leading-relaxed text-slate-700 dark:text-slate-300 space-y-1 bg-slate-50/40 dark:bg-slate-950 select-text transition-colors custom-scrollbar"
    >
      <div v-if="filteredLogs.length === 0" class="flex flex-col items-center justify-center h-48 text-slate-400 dark:text-slate-600 text-xs italic select-none">
        <Activity class="w-6 h-6 mb-2 opacity-40 text-slate-400" />
        <span>暂无该分类下的实时事件…</span>
      </div>

      <div
        v-for="item in filteredLogs"
        :key="item.id"
        :style="{ contentVisibility: 'auto', containIntrinsicSize: 'auto 32px' }"
        class="group flex items-start space-x-2 py-1 px-1.5 rounded-lg hover:bg-white dark:hover:bg-slate-900/80 transition-colors border border-transparent hover:border-slate-200/60 dark:hover:border-slate-800/60"
      >
        <!-- 时间戳 -->
        <span class="text-[10px] text-slate-400 dark:text-slate-500 font-mono shrink-0 select-none pt-0.5 tracking-tighter">
          {{ item.time || '--:--:--' }}
        </span>

        <!-- 结构化彩色徽章 -->
        <span
          v-if="item.badge"
          :class="['px-1.5 py-0.5 rounded text-[10px] font-semibold shrink-0 select-none border leading-none', item.badgeColorClass]"
        >
          {{ item.badge }}
        </span>

        <!-- 正文消息体 -->
        <span :class="['flex-1 break-all text-[11px] leading-5', item.textColorClass]">
          {{ item.text }}
        </span>
      </div>
    </div>

    <!-- 浮动悬挂的“回到最新”按钮 -->
    <transition name="fade">
      <button
        v-if="isUserScrolledUp"
        @click="handleScrollToLatest"
        class="absolute bottom-8 right-3 px-2.5 py-1 text-[11px] font-semibold bg-emerald-600 hover:bg-emerald-500 text-white rounded-full shadow-lg flex items-center space-x-1.5 transition-colors active:scale-95 z-20 cursor-pointer border border-emerald-400"
      >
        <ArrowDown class="w-3.5 h-3.5" />
        <span>滚动至最新</span>
      </button>
    </transition>

    <!-- 底部微状态指示 (Row 3) -->
    <div class="h-6 bg-slate-100/90 dark:bg-slate-900/90 px-3 flex items-center justify-between border-t border-slate-200/80 dark:border-slate-800/80 shrink-0 text-[10px] text-slate-500 dark:text-slate-400 select-none">
      <div class="flex items-center space-x-1.5">
        <span class="w-1.5 h-1.5 rounded-full bg-emerald-500"></span>
        <span>{{ fullLogs ? '完整日志缓存中 · 显示最近500条' : '低内存 · 最多200条 · 回环日志聚合' }}</span>
      </div>

      <div class="flex items-center space-x-1">
        <button
          v-if="isUserScrolledUp"
          @click="handleScrollToLatest"
          class="text-amber-600 dark:text-amber-400 font-semibold hover:underline flex items-center space-x-0.5 cursor-pointer"
        >
          <span>[已暂停贴底 · 查看最新]</span>
        </button>
        <span v-else class="text-emerald-600 dark:text-emerald-400 font-medium">
          {{ paused ? '[拖动中 · 暂停显示]' : '[实时自动跟随中]' }}
        </span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount, nextTick } from "vue";
import type { LogEntry } from '../log-buffer';
import {
  Copy,
  Check,
  Trash2,
  Activity,
  Globe,
  Cpu,
  AlertTriangle,
  ListFilter,
  ArrowDown
} from "lucide-vue-next";

const props = defineProps<{
  logs: LogEntry[];
  fullLogs: boolean;
  paused?: boolean;
}>();

const emit = defineEmits(["clear", "copy", "toggle-full"]);

type FilterType = "all" | "proxy" | "process" | "error";

const filterTabs = [
  { id: "all" as FilterType, label: "全部", icon: ListFilter },
  { id: "proxy" as FilterType, label: "代理", icon: Globe },
  { id: "process" as FilterType, label: "进程", icon: Cpu },
  { id: "error" as FilterType, label: "警报", icon: AlertTriangle },
];

const activeFilter = ref<FilterType>("all");
const logContainer = ref<HTMLDivElement | null>(null);
const isUserScrolledUp = ref(false);
const copied = ref(false);

interface ParsedLog {
  raw: string;
  time: string;
  badge: string;
  badgeColorClass: string;
  text: string;
  textColorClass: string;
  category: FilterType;
}

const parseLogLine = (line: string): ParsedLog => {
  let rest = line.trim();
  let time = "";

  // 匹配开头的时间戳如 [12:34:56]
  const timeMatch = rest.match(/^\[(\d{1,2}:\d{2}:\d{2})\]\s*/);
  if (timeMatch) {
    time = timeMatch[1];
    rest = rest.slice(timeMatch[0].length);
  }

  // 匹配紧随其后的首个徽章标签如 [代理转发]
  let badge = "";
  let badgeColorClass = "bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400 border-slate-200 dark:border-slate-700";
  let textColorClass = "text-slate-700 dark:text-slate-300";
  let category: FilterType = "all";

  const badgeMatch = rest.match(/^\[([^\]]+)\]\s*/);
  if (badgeMatch) {
    badge = badgeMatch[1];
    rest = rest.slice(badgeMatch[0].length);

    if (badge.includes("代理转发")) {
      category = "proxy";
      badgeColorClass = "bg-emerald-50 dark:bg-emerald-950/80 text-emerald-700 dark:text-emerald-300 border-emerald-300 dark:border-emerald-800";
      textColorClass = "text-emerald-900 dark:text-emerald-200 font-medium";
    } else if (badge.includes("远端DNS")) {
      category = "proxy";
      badgeColorClass = "bg-purple-50 dark:bg-purple-950/80 text-purple-700 dark:text-purple-300 border-purple-300 dark:border-purple-800";
      textColorClass = "text-purple-900 dark:text-purple-200";
    } else if (badge.includes("本地直连")) {
      category = "proxy";
      badgeColorClass = "bg-slate-100 dark:bg-slate-900 text-slate-500 dark:text-slate-400 border-slate-200 dark:border-slate-800";
      textColorClass = "text-slate-500 dark:text-slate-400";
    } else if (badge.includes("进程衍生")) {
      category = "process";
      badgeColorClass = "bg-sky-50 dark:bg-sky-950/80 text-sky-700 dark:text-sky-300 border-sky-300 dark:border-sky-800";
      textColorClass = "text-sky-900 dark:text-sky-200 font-medium";
    } else if (badge.includes("进程销毁")) {
      category = "process";
      badgeColorClass = "bg-amber-50 dark:bg-amber-950/80 text-amber-700 dark:text-amber-300 border-amber-300 dark:border-amber-800";
      textColorClass = "text-amber-900 dark:text-amber-200";
    } else if (badge.includes("进程退出") || badge.includes("停止")) {
      category = "process";
      badgeColorClass = "bg-rose-50 dark:bg-rose-950/80 text-rose-700 dark:text-rose-300 border-rose-300 dark:border-rose-800";
      textColorClass = "text-rose-800 dark:text-rose-300";
    } else if (badge.includes("启动成功") || badge.includes("启动指令")) {
      category = "process";
      badgeColorClass = "bg-teal-50 dark:bg-teal-950/80 text-teal-700 dark:text-teal-300 border-teal-300 dark:border-teal-800";
      textColorClass = "text-teal-900 dark:text-teal-200 font-medium";
    } else if (badge.includes("失败") || badge.includes("错误")) {
      category = "error";
      badgeColorClass = "bg-rose-50 dark:bg-rose-950 text-rose-700 dark:text-rose-300 border-rose-300 dark:border-rose-800";
      textColorClass = "text-rose-900 dark:text-rose-200 font-semibold";
    } else if (badge.includes("系统") || badge.includes("路径解析") || badge.includes("进程树监控")) {
      category = "process";
      badgeColorClass = "bg-indigo-50 dark:bg-indigo-950/80 text-indigo-700 dark:text-indigo-300 border-indigo-300 dark:border-indigo-800";
      textColorClass = "text-slate-800 dark:text-slate-200";
    }
  } else {
    // 普通无方括号文本
    if (rest.includes("失败") || rest.includes("异常") || rest.includes("错误")) {
      category = "error";
      textColorClass = "text-rose-700 dark:text-rose-300 font-medium";
    } else if (rest.includes("代理") || rest.includes("DNS")) {
      category = "proxy";
    } else if (rest.includes("进程") || rest.includes("PID")) {
      category = "process";
    }
  }

  return {
    raw: line,
    time,
    badge,
    badgeColorClass,
    text: rest,
    textColorClass,
    category,
  };
};

const parsedCache = new WeakMap<LogEntry, ParsedLog & { id: number }>();
const parsedLogs = computed(() => {
  return props.logs.map(entry => {
    let parsed = parsedCache.get(entry);
    if (!parsed) { parsed = { ...parseLogLine(entry.raw), id: entry.id }; parsedCache.set(entry, parsed); }
    return parsed;
  });
});

const tabCounts = computed(() => {
  const counts: Record<string, number> = {
    all: parsedLogs.value.length,
    proxy: 0,
    process: 0,
    error: 0,
  };
  for (const item of parsedLogs.value) {
    if (item.category === "proxy") counts.proxy++;
    else if (item.category === "process") counts.process++;
    else if (item.category === "error") counts.error++;
  }
  return counts;
});

const filteredLogs = computed(() => {
  if (activeFilter.value === "all") {
    return parsedLogs.value;
  }
  return parsedLogs.value.filter((item) => item.category === activeFilter.value);
});

let scrollFrame = 0;
const scrollToBottom = () => {
  if (props.paused || scrollFrame) return;
  scrollFrame = requestAnimationFrame(() => {
    scrollFrame = 0;
    if (props.paused || isUserScrolledUp.value) return;
    const element = logContainer.value;
    if (element) element.scrollTop = element.scrollHeight;
  });
};

const handleUserScroll = () => {
  if (props.paused) return;
  if (!logContainer.value) return;
  const { scrollTop, scrollHeight, clientHeight } = logContainer.value;
  const distanceFromBottom = scrollHeight - (scrollTop + clientHeight);
  // 用户向上滚动超过 40px 则暂停强制贴底
  if (distanceFromBottom > 40) {
    isUserScrolledUp.value = true;
  } else {
    isUserScrolledUp.value = false;
  }
};

const handleScrollToLatest = () => {
  isUserScrolledUp.value = false;
  scrollToBottom();
};

const handleCopy = () => {
  emit("copy");
  copied.value = true;
  setTimeout(() => {
    copied.value = false;
  }, 1500);
};

watch(
  () => props.logs[props.logs.length - 1]?.id,
  () => {
    if (!isUserScrolledUp.value) {
      void nextTick(scrollToBottom);
    }
  }
);

watch(activeFilter, () => {
  isUserScrolledUp.value = false;
  void nextTick(scrollToBottom);
});

onMounted(() => {
  scrollToBottom();
});
watch(() => props.paused, paused => {
  if (paused) { cancelAnimationFrame(scrollFrame); scrollFrame = 0; }
  else if (!isUserScrolledUp.value) void nextTick(scrollToBottom);
}, { flush: 'sync' });
onBeforeUnmount(() => cancelAnimationFrame(scrollFrame));
</script>

<style scoped>
/* 显式精美滚动条样式，确保可见且美观 */
.custom-scrollbar {
  scrollbar-width: thin;
  scrollbar-color: rgba(148, 163, 184, 0.6) transparent;
}

.custom-scrollbar::-webkit-scrollbar {
  width: 6px;
  height: 6px;
}

.custom-scrollbar::-webkit-scrollbar-track {
  background: rgba(0, 0, 0, 0.04);
  border-radius: 9999px;
}

.custom-scrollbar::-webkit-scrollbar-thumb {
  background-color: rgba(148, 163, 184, 0.6);
  border-radius: 9999px;
}

.custom-scrollbar::-webkit-scrollbar-thumb:hover {
  background-color: rgba(100, 116, 139, 0.9);
}

:global(.dark) .custom-scrollbar {
  scrollbar-color: rgba(100, 116, 139, 0.6) transparent;
}

:global(.dark) .custom-scrollbar::-webkit-scrollbar-track {
  background: rgba(255, 255, 255, 0.04);
}

:global(.dark) .custom-scrollbar::-webkit-scrollbar-thumb {
  background-color: rgba(100, 116, 139, 0.6);
}

:global(.dark) .custom-scrollbar::-webkit-scrollbar-thumb:hover {
  background-color: rgba(148, 163, 184, 0.9);
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s ease, transform 0.2s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
  transform: translateY(8px);
}
</style>
