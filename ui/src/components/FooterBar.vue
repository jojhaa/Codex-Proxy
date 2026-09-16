<template>
  <div class="h-9 bg-slate-100 dark:bg-slate-950 px-4 flex items-center justify-between border-t border-slate-200 dark:border-slate-800 text-[11px] text-slate-600 dark:text-slate-400 select-none shrink-0 rounded-b-2xl transition-colors">
    <div class="flex items-center">
      <span>作者：</span>
      <button
        @click="openAuthorLink"
        :title="'打开项目：' + projectInfo.repository"
        class="text-slate-800 dark:text-slate-200 font-semibold hover:text-emerald-600 dark:hover:text-emerald-400 cursor-pointer underline decoration-dotted underline-offset-2 transition-colors inline-flex items-center space-x-0.5 active:opacity-80"
      >
        <span>{{ projectInfo.author }}</span>
        <ExternalLink class="w-3 h-3 opacity-60 ml-0.5" />
      </button>
      <span class="mx-2 text-slate-300 dark:text-slate-700">|</span>
      <span>版本号：</span>
      <span class="text-emerald-700 dark:text-emerald-400 font-semibold">v{{ projectInfo.version }} (Tauri 2.0)</span>
    </div>

    <button
      @click="$emit('toggle-settings')"
      class="flex items-center space-x-1 px-2.5 py-1 rounded bg-white dark:bg-slate-900 hover:bg-slate-200 dark:hover:bg-slate-800 border border-slate-300 dark:border-slate-800 text-slate-700 dark:text-slate-300 transition-colors shadow-sm font-medium"
    >
      <Settings class="w-3 h-3" />
      <span>高级设置中心</span>
    </button>
  </div>
</template>

<script setup lang="ts">
import { Settings, ExternalLink } from "lucide-vue-next";
import { openUrl } from "@tauri-apps/plugin-opener";
import { invoke } from "@tauri-apps/api/core";
import { projectInfo } from '../project-info';

defineEmits(["toggle-settings"]);

const GITHUB_URL = projectInfo.repository;

const openAuthorLink = async () => {
  try {
    await openUrl(GITHUB_URL);
  } catch {
    await invoke("open_external_url", { url: GITHUB_URL });
  }
};
</script>
