<template>
  <div class="bg-white dark:bg-slate-900/90 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm dark:shadow-lg mb-3 transition-colors">
    <div class="flex items-center justify-between">
      <div>
        <h3 class="text-sm font-bold text-slate-800 dark:text-slate-100 flex items-center space-x-2">
          <span>{{ status.title }}</span>
        </h3>
        <p class="text-xs text-slate-500 dark:text-slate-400 mt-1">
          {{ status.sub }}
        </p>
      </div>

      <div
        class="flex items-center space-x-2 px-3 py-1.5 rounded-full border text-xs font-semibold transition-colors"
        :class="status.running
          ? 'bg-emerald-50 dark:bg-emerald-950/60 border-emerald-200 dark:border-emerald-500/40 text-emerald-700 dark:text-emerald-300'
          : 'bg-slate-100 dark:bg-slate-800/80 border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300'"
      >
        <span
          class="w-2 h-2 rounded-full"
          :class="status.running ? 'bg-emerald-500 animate-pulse shadow-sm shadow-emerald-500' : 'bg-slate-400'"
        ></span>
        <span>{{ status.badge_label }}</span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
defineProps<{
  status: {
    running: boolean;
    title: string;
    sub: string;
    badge_label: string;
    badge_color: string;
  };
}>();
</script>
