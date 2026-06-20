<template>
  <div class="guide-grid">
    <section class="panel">
      <div class="panel-header">
        <h2 class="page-title">使用流程</h2>
      </div>
      <div class="panel-body steps">
        <div v-for="step in steps" :key="step.title">
          <strong>{{ step.title }}</strong>
          <p>{{ step.content }}</p>
        </div>
      </div>
    </section>

    <section class="panel">
      <div class="panel-header">
        <h2 class="page-title">接口顺序</h2>
      </div>
      <div class="panel-body">
        <el-timeline>
          <el-timeline-item timestamp="/api/auth/login">使用本地管理员密码登录前端控制台。</el-timeline-item>
          <el-timeline-item timestamp="/api/agent/register">注册 Agent，获取一次性展示的 Agent Key。</el-timeline-item>
          <el-timeline-item timestamp="/api/security/check-input">在用户输入进入 Agent 前进行提示词注入检测。</el-timeline-item>
          <el-timeline-item timestamp="/api/security/check-output">在模型输出返回用户前进行敏感信息过滤。</el-timeline-item>
          <el-timeline-item timestamp="/api/security/check-tool-call">在工具执行前校验工具名与参数。</el-timeline-item>
        </el-timeline>
      </div>
    </section>
  </div>
</template>

<script setup lang="ts">
const steps = [
  { title: '1. 登录本地管理端', content: '管理端只校验本地管理员密码，不再使用 AgentKey 加密码登录。' },
  { title: '2. 注册 Agent', content: '为每个接入应用生成独立 Agent Key，后端只保存哈希和指纹。' },
  { title: '3. 配置规则', content: '在规则管理页通过下拉选项维护输入、输出规则，规则保存到数据库并绑定到当前 Agent。' },
  { title: '4. 接入检测接口', content: '业务系统在调用大模型前后分别调用输入检测、输出过滤和工具防护接口。' },
  { title: '5. 查看审计日志', content: '命中规则、风险等级、处理动作和匿名主体哈希会写入审计记录。' }
]
</script>

<style scoped>
.guide-grid {
  display: grid;
  grid-template-columns: 0.9fr 1.1fr;
  gap: 20px;
}

.steps {
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.steps p {
  margin: 6px 0 0;
  color: var(--color-muted);
  line-height: 1.7;
}

@media (max-width: 900px) {
  .guide-grid {
    grid-template-columns: 1fr;
  }
}
</style>
