# AI Study Hub

Monorepo của hệ thống AI Study Hub.

- `backend/`: ASP.NET Core Web API (.NET 10)
- `frontend/`: React + TypeScript + Vite

## Backend

```bash
dotnet restore backend/AIStudyHub.slnx
dotnet run --project backend/src/AIStudyHub.Api
```

Cấu hình `ConnectionStrings__DefaultConnection` và `Jwt__Key` bằng biến môi trường hoặc user secrets trước khi chạy.

## Frontend

```bash
cd frontend
npm install
npm run dev
```
