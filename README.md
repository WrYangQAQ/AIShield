# AIShield

[English](README.md) | [简体中文](README.zh-CN.md)

AIShield is a security middleware for AI Agent applications. It provides an ASP.NET Core backend and a Vue 3 administration frontend for protecting Agent inputs, outputs, tool calls, memory, and RAG workflows.

## Features

- Agent registration, authentication, and lifecycle management
- Input prompt-injection and unsafe-content detection
- Output filtering and sensitive-data masking
- Tool-call authorization and parameter inspection
- Configurable security rules and rule testing
- Security audit logs, risk trends, and health metrics
- Memory conflict detection, decay, archive, restore, and synchronization
- RAG candidate filtering and reranking
- Multi-turn conversation topic-drift detection

## Documentation

- [Project Guide](docu/Project-Guide.md)
- [中文项目使用说明](docu/项目使用说明.md)
- [API Request and Response Reference](docu/接口说明（请求-响应）.md)
- [Todo](docu/todo.md)
- [Completed Work](docu/done.md)

## Tech Stack

- Backend: ASP.NET Core / .NET 9
- Frontend: Vue 3, TypeScript, Vite, Element Plus
- Database: SQL Server with Entity Framework Core

## Local Configuration

The repository does not store database passwords, administrator passwords, JWT secrets, or Agent Keys.

Copy:

```text
AIShield.Backend/AIShield.Backend/appsettings.Development.example.json
```

to:

```text
AIShield.Backend/AIShield.Backend/appsettings.Development.json
```

Replace the placeholders with your local database connection string, JWT secret, and administrator password.

## Run the Application

Start the backend:

```powershell
dotnet run --project AIShield.Backend/AIShield.Backend/AIShield.Backend.csproj
```

Start the frontend:

```powershell
cd Aishield.Frontend
npm install
npm run dev
```

Development URLs:

- Frontend: `http://127.0.0.1:5173`
- Backend: `http://localhost:5069`
- Swagger: `http://localhost:5069/swagger`

## Build

```powershell
dotnet build AIShield.Backend/AIShield.Backend.sln
cd Aishield.Frontend
npm run build
```

## Security

Never commit local configuration, credentials, Agent Keys, or production secrets. Use environment variables or ignored development configuration files for sensitive values.
