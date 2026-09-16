<template>
  <div class="bg-white dark:bg-slate-900/90 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm dark:shadow-lg mb-3 transition-colors">
    <!-- 代理模式切换 -->
    <div class="flex items-center space-x-2 bg-slate-100 dark:bg-slate-950 p-1 rounded-lg border border-slate-200 dark:border-slate-800/80 mb-3.5 transition-colors">
      <button
        @click="selectMode('standard')"
        class="flex-1 py-1.5 text-xs font-semibold rounded-md transition-all duration-200"
        :class="currentMode === 'standard'
          ? 'bg-white dark:bg-slate-800 text-sky-600 dark:text-cyan-400 shadow-sm font-bold'
          : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'"
      >
        标准代理
      </button>
      <button
        @click="selectMode('process')"
        class="flex-1 py-1.5 text-xs font-semibold rounded-md transition-all duration-200"
        :class="currentMode === 'process'
          ? 'bg-white dark:bg-slate-800 text-sky-600 dark:text-cyan-400 shadow-sm font-bold'
          : 'text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-200'"
      >
        进程代理
      </button>
    </div>

    <!-- 协议选择与标题 -->
    <div class="flex items-center justify-between mb-3">
      <span class="text-xs font-semibold text-slate-700 dark:text-slate-300">代理节点参数配置</span>
      <div class="flex items-center bg-slate-100 dark:bg-slate-950 p-1 rounded-lg border border-slate-200 dark:border-slate-800 text-xs transition-colors">
        <button
          @click="config.proto = 'socks5'"
          class="px-2.5 py-0.5 rounded font-medium transition-colors"
          :class="config.proto === 'socks5'
            ? 'bg-white dark:bg-slate-800 text-sky-600 dark:text-cyan-400 font-bold shadow-sm'
            : 'text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-200'"
          title="推荐：包含远端 DNS 解析，解决域名污染"
        >
          SOCKS5h
        </button>
        <button
          @click="config.proto = 'http'"
          class="px-2.5 py-0.5 rounded font-medium transition-colors"
          :class="config.proto === 'http'
            ? 'bg-white dark:bg-slate-800 text-sky-600 dark:text-cyan-400 font-bold shadow-sm'
            : 'text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-200'"
        >
          HTTP
        </button>
      </div>
    </div>

    <!-- IP 与端口输入 -->
    <div class="grid grid-cols-12 gap-2.5 mb-3">
      <div class="col-span-8">
        <label class="block text-[11px] text-slate-500 dark:text-slate-400 mb-1">代理主机地址 (IP / 域名)</label>
        <input
          v-model="config.host"
          type="text"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded-lg px-3 py-1.5 text-xs text-slate-900 dark:text-slate-100 focus:outline-none focus:border-emerald-600 dark:focus:border-emerald-500 focus:bg-white dark:focus:bg-slate-950 transition-colors"
        />
      </div>
      <div class="col-span-4">
        <label class="block text-[11px] text-slate-500 dark:text-slate-400 mb-1">端口</label>
        <input
          v-model="config.port"
          type="text"
          class="w-full bg-slate-50 dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded-lg px-3 py-1.5 text-xs text-slate-900 dark:text-slate-100 focus:outline-none focus:border-emerald-600 dark:focus:border-emerald-500 focus:bg-white dark:focus:bg-slate-950 transition-colors"
        />
      </div>
    </div>

    <!-- 远端 DNS 说明提示卡片 -->
    <div class="flex items-center space-x-2 bg-emerald-50 dark:bg-emerald-950/40 border border-emerald-200 dark:border-emerald-800/40 rounded-lg p-2.5 mb-3 text-emerald-800 dark:text-emerald-300 text-xs transition-colors">
      <ShieldCheck class="w-4 h-4 shrink-0 text-emerald-600 dark:text-emerald-400" />
      <span class="text-[11px] leading-tight font-medium">仅 OpenAI / ChatGPT 名单使用远端 DNS 解析 (SOCKS5h 防污染)</span>
    </div>

    <!-- 底部操作按钮与测试结果 -->
    <div class="flex items-center justify-between">
      <span class="text-xs truncate max-w-[200px]" :class="testResultColor">
        {{ testResult }}
      </span>

      <div class="flex items-center space-x-2">
        <button
          @click="$emit('test-port')"
          class="px-3 py-1.5 text-xs font-semibold rounded-lg bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 border border-slate-300 dark:border-slate-700 text-slate-700 dark:text-slate-200 transition-colors"
        >
          检查端口
        </button>
        <button
          @click="$emit('save-config')"
          class="px-3 py-1.5 text-xs font-semibold rounded-lg bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 border border-slate-300 dark:border-slate-700 text-slate-700 dark:text-slate-200 transition-colors"
        >
          保存配置
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue";
import { ShieldCheck } from "lucide-vue-next";

const props = defineProps<{
  config: {
    host: string;
    port: string;
    proto: string;
  };
  currentMode: string;
  testResult: string;
}>();

const emit = defineEmits(["update:currentMode", "test-port", "save-config"]);

const selectMode = (mode: string) => {
  emit("update:currentMode", mode);
};

const testResultColor = computed(() => {
  if (props.testResult.includes("✓")) return "text-emerald-700 dark:text-emerald-400 font-bold";
  if (props.testResult.includes("✕")) return "text-rose-600 dark:text-rose-400 font-bold";
  return "text-slate-500 dark:text-slate-400";
});
</script>
