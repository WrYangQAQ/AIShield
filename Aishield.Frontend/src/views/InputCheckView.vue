<template>
  <SecurityTester
    title="输入检测"
    subtitle="在用户提示词进入 Agent 之前进行注入、越权和敏感指令检测。"
    mode="input"
    placeholder="请输入待检测的用户提示词"
  />
</template>

<script setup lang="ts">
import SecurityTester from '../components/SecurityTester.vue'
</script>
