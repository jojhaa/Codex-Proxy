<template>
  <div class="fixed inset-0 bg-slate-950/60 backdrop-blur-sm z-50 flex items-center justify-center p-4 select-none">
    <div
      class="w-[380px] bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-5 shadow-2xl transition-all scale-100"
    >
      <!-- Icon and Title -->
      <div class="flex items-start space-x-3 mb-3">
        <div class="w-10 h-10 rounded-xl bg-amber-50 dark:bg-amber-950/60 border border-amber-200 dark:border-amber-800/60 flex items-center justify-center shrink-0">
          <AlertTriangle class="w-5 h-5 text-amber-600 dark:text-amber-400" />
        </div>
        <div>
          <h3 class="text-sm font-bold text-slate-900 dark:text-slate-100">提示</h3>
          <p class="text-xs text-slate-600 dark:text-slate-400 mt-1 leading-relaxed">
            关闭窗口会导致代理失败。是否最小化到系统托盘？
          </p>
        </div>
      </div>

      <!-- Action Buttons (不提供关闭程序，仅提供最小化到托盘与取消) -->
      <div class="flex items-center justify-end space-x-2.5 pt-3 border-t border-slate-100 dark:border-slate-800/80 mt-4">
        <button
          @click="$emit('cancel')"
          class="px-3.5 py-1.5 text-xs text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-100 bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 rounded-lg transition-colors"
        >
          取消
        </button>
        <button
          @click="$emit('minimize')"
          class="px-4 py-1.5 text-xs bg-gradient-to-r from-emerald-600 to-sky-600 hover:from-emerald-500 hover:to-sky-500 text-white font-bold rounded-lg shadow-sm transition-all"
        >
          最小化到托盘
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { AlertTriangle } from "lucide-vue-next";

defineEmits(["minimize", "cancel"]);
</script>
