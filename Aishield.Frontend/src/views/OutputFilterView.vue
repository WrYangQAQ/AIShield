<template>
  <SecurityTester
    title="输出过滤"
    subtitle="在模型响应返回用户之前进行敏感信息、越权数据和不当内容过滤。"
    mode="output"
    placeholder="请输入待过滤的模型输出"
  />
</template>

<script setup lang="ts">
import SecurityTester from '../components/SecurityTester.vue'
</script>
