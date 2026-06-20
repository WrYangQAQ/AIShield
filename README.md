# AIShield

AIShield 是一个面向 AI Agent 应用的安全加固中间件项目，包括：

- ASP.NET Core 后端管理与安全接口
- Vue 3 管理前端

## 文档

- [项目使用说明](docu/项目使用说明.md)
- [Project Guide](docu/Project-Guide.md)
- [接口说明（请求-响应）](docu/接口说明（请求-响应）.md)
- [待办事项](docu/todo.md)
- [完成记录](docu/done.md)

## 本地配置

仓库不会保存数据库密码、管理员密码或 API Key。

1. 复制后端示例配置：

   `AIShield.Backend/AIShield.Backend/appsettings.Development.example.json`

   为：

   `AIShield.Backend/AIShield.Backend/appsettings.Development.json`

2. 将示例中的数据库连接串、JWT Secret 和管理员密码替换为本地真实值。

## 启动

后端：

```powershell
dotnet run --project AIShield.Backend/AIShield.Backend/AIShield.Backend.csproj
```

前端：

```powershell
cd Aishield.Frontend
npm install
npm run dev
```
