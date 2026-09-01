# AI Study Hub

Nền tảng quản lý và chia sẻ tài liệu học tập, tích hợp AI để trích xuất nội dung và hỏi đáp theo tài liệu.

## Tech stack

- **Backend:** ASP.NET Core 10, Entity Framework Core, SQL Server
- **Frontend:** React 19, TypeScript, Vite
- **AI & document processing:** Gemini, PdfPig, Tesseract OCR
- **Testing:** xUnit, SQLite in-memory

## Core capabilities

- Quản lý tài liệu, thư mục, phiên bản, chia sẻ và phân quyền.
- Trích xuất nội dung từ PDF, Office, text và hình ảnh bằng background worker.
- AI chat theo ngữ cảnh tài liệu kèm trích dẫn nguồn.
- Quy trình báo cáo, kiểm duyệt và kháng nghị.
- Quản lý tài khoản, gói dịch vụ, ví và giao dịch.

## Repository structure

```text
backend/
├── src/
│   ├── AIStudyHub.Api             # HTTP API và application bootstrap
│   ├── AIStudyHub.Application     # Use cases và business logic
│   ├── AIStudyHub.Domain          # Domain entities
│   └── AIStudyHub.Infrastructure  # Database và external services
└── tests/                          # Unit và integration tests

frontend/                           # React application
```

## Prerequisites

- .NET SDK 10
- Node.js và npm
- SQL Server; cấu hình mặc định sử dụng `SQLEXPRESS`

## Local development

Clone repository:

```bash
git clone https://github.com/thienminh2608/AI_Study_Hub.git
cd AI_Study_Hub
```

Cấu hình secrets cho backend:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "YOUR_CONNECTION_STRING" --project backend/src/AIStudyHub.Api
dotnet user-secrets set "Jwt:Key" "YOUR_JWT_SECRET" --project backend/src/AIStudyHub.Api
dotnet user-secrets set "Gemini:ApiKey" "YOUR_GEMINI_API_KEY" --project backend/src/AIStudyHub.Api
```

Chạy backend:

```bash
dotnet restore backend/AIStudyHub.slnx
dotnet run --project backend/src/AIStudyHub.Api --launch-profile http
```

- API: `http://localhost:5065`
- Swagger: `http://localhost:5065/swagger`

Chạy frontend trong terminal khác:

```bash
cd frontend
npm install
npm run dev
```

Web app: `http://localhost:5173`

Frontend sử dụng `VITE_API_BASE_URL`; giá trị development mặc định là `http://localhost:5065/api`.

## Configuration

Cấu hình backend nằm tại `backend/src/AIStudyHub.Api/appsettings*.json` và có thể được ghi đè bằng User Secrets hoặc biến môi trường.

| Key | Required | Description |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | Yes | SQL Server connection string |
| `Jwt:Key` | Yes | Khóa ký JWT |
| `Gemini:ApiKey` | Khi `Gemini:UseMock=false` | Gemini API key |
| `MailSettings:UseMock` | No | Chuyển giữa mail mock và SMTP |
| `PayOS:UseMock` | No | Chuyển giữa payment mock và PayOS |

Không commit credentials hoặc production secrets vào repository.

## Validation

```bash
dotnet test backend/AIStudyHub.slnx

cd frontend
npm run lint
npm run format:check
npm run build
```
