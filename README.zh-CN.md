# AIShield

[English](README.md) | [简体中文](README.zh-CN.md)

AIShield 是一个面向 AI Agent 应用的安全加固中间件，包含 ASP.NET Core 后端和 Vue 3 管理前端，用于保护 Agent 的输入、输出、工具调用、记忆及 RAG 工作流。

## 主要功能

- Agent 注册、鉴权和生命周期管理
- 输入提示词注入及危险内容检测
- 输出过滤和敏感信息脱敏
- 工具调用鉴权及参数检查
- 安全规则配置与规则测试
- 安全审计日志、风险趋势和健康指标
- 记忆冲突检测、衰减、归档、恢复与同步
- RAG 候选内容过滤与重排
- 多轮对话主题漂移检测

## 文档

- [项目使用说明](docu/项目使用说明.md)
- [Project Guide](docu/Project-Guide.md)
- [接口说明（请求—响应）](docu/接口说明（请求-响应）.md)
- [待办事项](docu/todo.md)
- [完成记录](docu/done.md)

## 技术栈

- 后端：ASP.NET Core / .NET 9
- 前端：Vue 3、TypeScript、Vite、Element Plus
- 数据库：SQL Server、Entity Framework Core

## 本地配置

仓库不会保存数据库密码、管理员密码、JWT Secret 或 Agent Key。

复制：

```text
AIShield.Backend/AIShield.Backend/appsettings.Development.example.json
```

为：

```text
AIShield.Backend/AIShield.Backend/appsettings.Development.json
```

将占位符替换为本地数据库连接串、JWT Secret 和管理员密码。

## 启动项目

启动后端：

```powershell
dotnet run --project AIShield.Backend/AIShield.Backend/AIShield.Backend.csproj
```

启动前端：

```powershell
cd Aishield.Frontend
npm install
npm run dev
```

开发环境地址：

- 前端：`http://127.0.0.1:5173`
- 后端：`http://localhost:5069`
- Swagger：`http://localhost:5069/swagger`

## 快速接入示例

AIShield 通过 HTTP API 接入，因此可以保护任意语言或框架编写的 Agent。

先在管理页面注册 Agent，复制系统生成的 Agent Key，并通过环境变量保存：

```powershell
$env:AI_SHIELD_URL="http://localhost:5069"
$env:AI_SHIELD_AGENT_KEY="你的-Agent-Key"
```

下面的 JavaScript 示例会在调用 Agent 前拦截用户输入，并在响应用户前过滤模型输出：

```js
const baseUrl = process.env.AI_SHIELD_URL ?? 'http://localhost:5069'
const agentKey = process.env.AI_SHIELD_AGENT_KEY

async function inspect(path, content) {
  const response = await fetch(`${baseUrl}${path}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-API-Key': agentKey
    },
    body: JSON.stringify({ content })
  })

  if (!response.ok) {
    throw new Error(`AIShield 请求失败：${response.status}`)
  }

  return response.json()
}

async function handleUserMessage(userInput) {
  // 1. 用户输入进入 Agent 或大模型前，先进行安全检查。
  const inputCheck = await inspect('/api/security/check-input', userInput)
  if (!inputCheck.allowed) {
    return `请求已拦截：${inputCheck.reason}`
  }

  // 替换为你的 OpenAI、LangChain、Semantic Kernel 或 Agent 调用。
  const modelOutput = await callYourAgentOrModel(
    inputCheck.processedContent ?? userInput
  )

  // 2. 模型输出返回用户前，再进行输出过滤。
  const outputCheck = await inspect('/api/security/check-output', modelOutput)
  if (!outputCheck.allowed) {
    return `响应已拦截：${outputCheck.reason}`
  }

  // 如果 AIShield 执行了脱敏或替换，应使用处理后的内容。
  return outputCheck.processedContent ?? modelOutput
}
```

工具调用也应在真正执行前拦截：

```js
const toolCheck = await fetch(`${baseUrl}/api/security/check-tool-call`, {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'X-API-Key': agentKey
  },
  body: JSON.stringify({
    toolName: 'send_email',
    arguments: { recipient: 'user@example.com', content: '你好' }
  })
}).then(response => response.json())

if (!toolCheck.allowed) {
  throw new Error(`工具调用已拦截：${toolCheck.reason}`)
}

await executeTool()
```

接入原则很简单：在受保护操作执行前立即调用 AIShield，只有 `allowed` 为 `true` 时才继续；返回 `processedContent` 时，应使用处理后的内容替代原文。

## 构建

```powershell
dotnet build AIShield.Backend/AIShield.Backend.sln
cd Aishield.Frontend
npm run build
```

## 安全提示

不要提交本地配置、登录凭证、Agent Key 或生产环境 Secret。敏感信息应通过环境变量或被 Git 忽略的开发配置文件提供。
