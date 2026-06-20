# AIShield

AIShield 是一个面向 AI Agent 应用的安全加固中间件项目，包括：

- ASP.NET Core 后端管理与安全接口
- Vue 3 管理前端

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
