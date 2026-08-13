# AI Study Hub

Monorepo của hệ thống AI Study Hub.

- `backend/`: ASP.NET Core Web API (.NET 10)
- `frontend/`: React, TypeScript và Vite

## Chạy Backend

```bash
dotnet restore backend/AIStudyHub.slnx
dotnet run --project backend/src/AIStudyHub.Api --launch-profile http
```

Cấu hình `ConnectionStrings__DefaultConnection` và `Jwt__Key` bằng biến môi trường
hoặc .NET User Secrets trước khi chạy.

## Chạy Frontend

```bash
cd frontend
npm install
npm run dev
```

Frontend mặc định kết nối API tại `http://localhost:5065/api`.

## Kiểm tra chất lượng

```bash
dotnet test backend/AIStudyHub.slnx
cd frontend
npm run lint
npm run format:check
npm run build
```
