# AIShield Project Guide

## 1. Overview

AIShield is a security middleware for AI Agent applications. It consists of an ASP.NET Core backend and a Vue 3 administration frontend.

Main capabilities include:

- Agent registration and management
- Input, output, and tool-call security checks
- Security rule configuration and testing
- Audit logs, risk trends, and operational metrics
- Memory conflict detection, decay, archive, restore, and synchronization
- RAG candidate filtering and reranking
- Topic-drift detection for multi-turn conversations

## 2. Requirements

- .NET 9 SDK
- SQL Server
- Node.js 18 or later
- npm

## 3. Backend Configuration

Copy:

```text
AIShield.Backend/AIShield.Backend/appsettings.Development.example.json
```

to:

```text
AIShield.Backend/AIShield.Backend/appsettings.Development.json
```

Set your local database connection string, JWT secret, and administrator password. The development configuration is ignored by Git.

Environment variables may also be used:

```powershell
$env:ConnectionStrings__DefaultConnection="Server=...;Database=AIShieldDB;User Id=...;Password=...;TrustServerCertificate=True;"
$env:Jwt__Secret="your-long-random-secret"
$env:Admin__Password="your-admin-password"
```

## 4. Database Setup

From the repository root:

```powershell
dotnet ef database update --project AIShield.Backend/AIShield.Backend/AIShield.Backend.csproj
```

Install the EF Core CLI first if necessary:

```powershell
dotnet tool install --global dotnet-ef
```

## 5. Run the Application

Backend:

```powershell
dotnet run --project AIShield.Backend/AIShield.Backend/AIShield.Backend.csproj
```

Development URLs:

- HTTP: `http://localhost:5069`
- HTTPS: `https://localhost:7123`
- Swagger: `http://localhost:5069/swagger`

Frontend:

```powershell
cd Aishield.Frontend
npm install
npm run dev
```

Frontend URL: `http://127.0.0.1:5173`

## 6. Typical Workflow

1. Sign in with the administrator password.
2. Register an Agent in the administration UI.
3. Store the returned Agent Key securely.
4. Configure and enable security rules.
5. Send the Agent Key in the `X-API-Key` header when calling `/api/security/*`.
6. Review audit records and risk metrics in the administration UI.

Administrator requests:

```http
Authorization: Bearer <ADMIN_JWT>
```

Agent security requests:

```http
X-API-Key: <AGENT_KEY>
```

See [API Request and Response Reference](接口说明（请求-响应）.md) for endpoint details.

## 7. Build

```powershell
dotnet build AIShield.Backend/AIShield.Backend.sln
cd Aishield.Frontend
npm run build
```

## 8. Security Notes

- Never commit secrets, Agent Keys, or local development configuration.
- Replace all example secrets before production deployment.
- Revoke and recreate an Agent if its key is exposed.
- Use HTTPS and a reverse proxy in production.
