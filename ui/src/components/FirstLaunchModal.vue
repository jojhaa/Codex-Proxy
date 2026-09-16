<template>
  <div class="fixed inset-0 bg-slate-950/70 backdrop-blur-md z-50 flex items-center justify-center p-4 select-none animate-in fade-in duration-200">
    <div
      class="w-[460px] max-w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-2xl transition-all scale-100 flex flex-col"
    >
      <!-- 头部：盾牌图标与醒目标题 -->
      <div class="flex items-center space-x-3 mb-4">
        <div class="w-11 h-11 rounded-xl bg-emerald-50 dark:bg-emerald-950/70 border border-emerald-200 dark:border-emerald-800/80 flex items-center justify-center shrink-0">
          <ShieldAlert class="w-6 h-6 text-emerald-600 dark:text-emerald-400" />
        </div>
        <div>
          <div class="flex items-center space-x-2">
            <h3 class="text-base font-bold text-slate-900 dark:text-slate-100">开源与反倒卖重要提醒</h3>
            <span class="px-2 py-0.5 text-[10px] font-semibold bg-emerald-100 dark:bg-emerald-950 text-emerald-700 dark:text-emerald-300 rounded-full border border-emerald-300 dark:border-emerald-800">
              永久免费
            </span>
          </div>
          <p class="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5 font-mono">
            ProcWeaver · Open-Source Notice & Anti-Resell Alert
          </p>
        </div>
      </div>

      <!-- 正文卡片组 (精炼、有力、层次分明) -->
      <div class="space-y-2.5 text-xs leading-relaxed">
        <!-- 1. 永久免费 -->
        <div class="p-3 bg-slate-50 dark:bg-slate-950/60 rounded-xl border border-slate-200/80 dark:border-slate-800/80 flex items-start space-x-2.5">
          <HeartHandshake class="w-4 h-4 text-emerald-600 dark:text-emerald-400 shrink-0 mt-0.5" />
          <div class="text-slate-700 dark:text-slate-300 text-[11px]">
            <span class="font-bold text-slate-900 dark:text-slate-100">作者独立开发 · 承诺永久免费：</span>
            本程序由作者 <span class="font-bold text-emerald-600 dark:text-emerald-400">{{ projectInfo.author }}</span> 独立倾力开发与维护，纯属开源技术交流分享，供所有用户<span class="underline font-semibold decoration-emerald-500">无条件免费使用</span>。
          </div>
        </div>

        <!-- 2. 唯一分发 -->
        <div class="p-3 bg-slate-50 dark:bg-slate-950/60 rounded-xl border border-slate-200/80 dark:border-slate-800/80 flex items-start space-x-2.5">
          <Github class="w-4 h-4 text-sky-600 dark:text-sky-400 shrink-0 mt-0.5" />
          <div class="text-slate-700 dark:text-slate-300 text-[11px]">
            <span class="font-bold text-slate-900 dark:text-slate-100">官方唯一渠道 · 无其它分发：</span>
            本程序<span class="font-semibold text-slate-900 dark:text-slate-100">仅在 GitHub 唯一官方仓库发布</span>。作者从未设立过任何第三方付费网盘、私域社群或收费分发镜像。
          </div>
        </div>

        <!-- 3. 严禁倒卖与退款警示 -->
        <div class="p-3 bg-rose-50/70 dark:bg-rose-950/30 rounded-xl border border-rose-200/80 dark:border-rose-900/60 flex items-start space-x-2.5">
          <AlertCircle class="w-4 h-4 text-rose-600 dark:text-rose-400 shrink-0 mt-0.5" />
          <div class="text-rose-900 dark:text-rose-200 text-[11px]">
            <span class="font-bold text-rose-700 dark:text-rose-300">付费购买者请立即维权退款：</span>
            开源项目坚决反对任何商业倒卖！若您系通过<span class="font-bold">淘宝、闲鱼、拼多多、个人微信/QQ 或付费社群</span>购买所得，您已遭遇黑心盗卖，<span class="font-bold underline decoration-rose-500">请立即向对应平台举报并申请全额退款</span>！
          </div>
        </div>

        <!-- 4. 图标制作与版权声明 -->
        <div class="p-3 bg-indigo-50/60 dark:bg-indigo-950/30 rounded-xl border border-indigo-200/80 dark:border-indigo-900/50 flex items-start space-x-2.5">
          <Palette class="w-4 h-4 text-indigo-600 dark:text-indigo-400 shrink-0 mt-0.5" />
          <div class="text-indigo-950 dark:text-indigo-200 text-[11px]">
            <span class="font-bold text-indigo-900 dark:text-indigo-300">图标制作与版权声明：</span>
            本应用图标采用 AI 绘图制作，仅作为开源项目视觉标识与技术交流使用。若涉及任何潜在版权或视觉侵权疑虑，请直接联系作者或提交 Issue 反馈，我们将在第一时间核实并配合调整或更换。
          </div>
        </div>
      </div>

      <!-- 底部操作按钮 -->
      <div class="flex items-center justify-between pt-4 border-t border-slate-100 dark:border-slate-800 mt-4">
        <!-- 访问官方仓库链接按钮 -->
        <button
          @click="openRepo"
          class="px-3 py-2 text-xs font-medium text-slate-600 dark:text-slate-300 hover:text-slate-900 dark:hover:text-white bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 rounded-xl transition-all flex items-center space-x-1.5 shadow-xs cursor-pointer"
        >
          <Github class="w-3.5 h-3.5" />
          <span>GitHub 官方仓库</span>
          <ExternalLink class="w-3 h-3 ml-0.5 text-slate-400" />
        </button>

        <!-- 确认进入主程序按钮 -->
        <button
          @click="$emit('confirm')"
          class="px-5 py-2 text-xs bg-gradient-to-r from-emerald-600 to-teal-600 hover:from-emerald-500 hover:to-teal-500 text-white font-bold rounded-xl shadow-md transition-all active:scale-[0.98] cursor-pointer flex items-center space-x-1.5"
        >
          <span>我已知晓并继续使用</span>
          <ArrowRight class="w-3.5 h-3.5" />
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import {
  ShieldAlert,
  HeartHandshake,
  Github,
  AlertCircle,
  Palette,
  ExternalLink,
  ArrowRight
} from "lucide-vue-next";
import { openUrl } from "@tauri-apps/plugin-opener";
import { invoke } from "@tauri-apps/api/core";
import { projectInfo } from '../project-info';

defineEmits(["confirm"]);

const REPO_URL = projectInfo.repository;

const openRepo = async () => {
  try {
    await openUrl(REPO_URL);
  } catch {
    await invoke("open_external_url", { url: REPO_URL });
  }
};
</script>
