using System.Text;
using AIStudyHub.Api.Middlewares;
using AIStudyHub.Application;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Infrastructure;
using AIStudyHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Keep the transport/form limit above the 50 MiB business file limit so
// multipart headers and boundaries do not cause otherwise valid uploads to be
// rejected before they reach DocumentController.
const long MaxUploadRequestSizeBytes = 55L * 1024 * 1024;
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = MaxUploadRequestSizeBytes);

// 0. Validate Startup Configuration (Fail-Closed)
ValidateStartupConfiguration(builder.Configuration, builder.Environment);

// 1. Configure Database Connection -> Registered via Infrastructure layer

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) && builder.Environment.IsEnvironment("Testing"))
{
    jwtKey = "m9uS6yBuZvrkIS8LcHlCvnJY7sbj9QEximY0oPcvKNM";
}
var key = Encoding.UTF8.GetBytes(jwtKey ?? "TestDefaultKeyMustBeThirtyTwoCharsLong!");
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !(builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"));
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

// 3. Register standard API components
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    options.MultipartBodyLengthLimit = MaxUploadRequestSizeBytes);

// 4. Configure Swagger with JWT Support
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "AI Study Hub API", Version = "v1" });

    // Add JWT Token Authorization in Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header
    });

    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
        }
    });
});

// 5. Register Clean Architecture Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// 6. Configure CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (allowedOrigins.Length == 0)
            throw new InvalidOperationException("Cors:AllowedOrigins must contain at least one trusted origin.");
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("""
        IF COL_LENGTH('users', 'downgrade_notice_pending') IS NULL
            ALTER TABLE users ADD downgrade_notice_pending BIT NOT NULL CONSTRAINT DF_users_downgrade_notice_pending DEFAULT 0;
        IF COL_LENGTH('users', 'expiry_notified') IS NULL
            ALTER TABLE users ADD expiry_notified BIT NOT NULL CONSTRAINT DF_users_expiry_notified DEFAULT 0;
        IF COL_LENGTH('users', 'expires_at') IS NULL
            ALTER TABLE users ADD expires_at DATETIME2 NULL;
        IF COL_LENGTH('documents', 'general_access') IS NULL
            ALTER TABLE documents ADD general_access VARCHAR(20) NOT NULL CONSTRAINT DF_documents_general_access DEFAULT 'RESTRICTED';
        IF COL_LENGTH('documents', 'lifecycle_status') IS NULL
            ALTER TABLE documents ADD lifecycle_status VARCHAR(30) NOT NULL CONSTRAINT DF_documents_lifecycle_status DEFAULT 'PRIVATE';
        IF COL_LENGTH('documents', 'is_deleted') IS NULL
            ALTER TABLE documents ADD is_deleted BIT NOT NULL CONSTRAINT DF_documents_is_deleted DEFAULT 0;
        IF COL_LENGTH('documents', 'deleted_at') IS NULL
            ALTER TABLE documents ADD deleted_at DATETIME2 NULL;
        IF COL_LENGTH('documents', 'deleted_by_user_id') IS NULL
            ALTER TABLE documents ADD deleted_by_user_id INT NULL;
        IF COL_LENGTH('documents', 'current_version_id') IS NULL
            ALTER TABLE documents ADD current_version_id INT NULL;
        IF COL_LENGTH('documents', 'share_link_expires_at') IS NULL
            ALTER TABLE documents ADD share_link_expires_at DATETIME2 NULL;
        IF COL_LENGTH('documents', 'is_share_link_revoked') IS NULL
            ALTER TABLE documents ADD is_share_link_revoked BIT NOT NULL CONSTRAINT DF_documents_is_share_link_revoked DEFAULT 0;
        IF COL_LENGTH('folders', 'general_access') IS NULL
            ALTER TABLE folders ADD general_access VARCHAR(20) NOT NULL CONSTRAINT DF_folders_general_access DEFAULT 'RESTRICTED';
        IF COL_LENGTH('folders', 'is_deleted') IS NULL
            ALTER TABLE folders ADD is_deleted BIT NOT NULL CONSTRAINT DF_folders_is_deleted DEFAULT 0;
        IF COL_LENGTH('folders', 'deleted_at') IS NULL
            ALTER TABLE folders ADD deleted_at DATETIME2 NULL;
        IF COL_LENGTH('document_shares', 'role') IS NULL
            ALTER TABLE document_shares ADD role VARCHAR(20) NOT NULL CONSTRAINT DF_document_shares_role DEFAULT 'VIEWER';
        IF OBJECT_ID('folder_shares', 'U') IS NULL
        BEGIN
            CREATE TABLE folder_shares (
                share_id BIGINT IDENTITY(1,1) PRIMARY KEY,
                folder_id INT NOT NULL,
                owner_user_id INT NOT NULL,
                shared_with_user_id INT NOT NULL,
                role VARCHAR(20) NOT NULL CONSTRAINT DF_folder_shares_role DEFAULT 'VIEWER',
                created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
                CONSTRAINT UQ_folder_shares UNIQUE(folder_id, shared_with_user_id),
                CONSTRAINT FK_folder_shares_folder FOREIGN KEY(folder_id) REFERENCES folders(folder_id) ON DELETE CASCADE,
                CONSTRAINT FK_folder_shares_user FOREIGN KEY(shared_with_user_id) REFERENCES users(user_id)
            );
        END
        IF OBJECT_ID('document_versions', 'U') IS NULL
        BEGIN
            CREATE TABLE document_versions (
                version_id INT IDENTITY(1,1) PRIMARY KEY,
                document_id INT NOT NULL,
                version_number INT NOT NULL,
                cloud_storage_url NVARCHAR(500) NOT NULL,
                file_extension NVARCHAR(10) NOT NULL,
                file_size_mb DECIMAL(5,2) NOT NULL,
                change_summary NVARCHAR(500) NULL,
                ai_parsing_status VARCHAR(20) NOT NULL CONSTRAINT DF_document_versions_ai_status DEFAULT 'PENDING',
                created_by_user_id INT NOT NULL,
                created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
                CONSTRAINT UQ_document_versions UNIQUE(document_id, version_number),
                CONSTRAINT FK_document_versions_document FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE CASCADE,
                CONSTRAINT FK_document_versions_user FOREIGN KEY(created_by_user_id) REFERENCES users(user_id)
            );
        END
        IF OBJECT_ID('document_versions', 'U') IS NOT NULL
        BEGIN
            IF COL_LENGTH('document_versions', 'ai_parsing_status') IS NULL
                ALTER TABLE document_versions ADD ai_parsing_status VARCHAR(20) NOT NULL CONSTRAINT DF_document_versions_ai_status DEFAULT 'PENDING';

        END
        IF OBJECT_ID('audit_logs', 'U') IS NULL
        BEGIN
            CREATE TABLE audit_logs (
                audit_id BIGINT IDENTITY(1,1) PRIMARY KEY,
                actor_user_id INT NOT NULL,
                action VARCHAR(50) NOT NULL,
                target_type VARCHAR(20) NOT NULL,
                target_id INT NOT NULL,
                details NVARCHAR(2000) NULL,
                created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
                CONSTRAINT FK_audit_logs_actor FOREIGN KEY(actor_user_id) REFERENCES users(user_id)
            );
        END
        IF OBJECT_ID('subject_categories', 'U') IS NULL
        BEGIN
            CREATE TABLE subject_categories (
                subject_id INT IDENTITY(1,1) PRIMARY KEY,
                name NVARCHAR(100) NOT NULL,
                normalized_name NVARCHAR(100) NOT NULL,
                status VARCHAR(20) NOT NULL CONSTRAINT DF_subject_categories_status DEFAULT 'APPROVED',
                requested_by_user_id INT NULL,
                approved_by_user_id INT NULL,
                rejection_reason NVARCHAR(500) NULL,
                created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
                updated_at DATETIME2 NULL,
                CONSTRAINT FK_subject_categories_req_user FOREIGN KEY(requested_by_user_id) REFERENCES users(user_id),
                CONSTRAINT FK_subject_categories_app_user FOREIGN KEY(approved_by_user_id) REFERENCES users(user_id)
            );
            INSERT INTO subject_categories (name, normalized_name, status, created_at)
            VALUES 
              (N'Toán học', N'toan hoc', 'APPROVED', GETDATE()),
              (N'Vật lý', N'vat ly', 'APPROVED', GETDATE()),
              (N'Hóa học', N'hoa hoc', 'APPROVED', GETDATE()),
              (N'Sinh học', N'sinh hoc', 'APPROVED', GETDATE()),
              (N'Ngữ văn', N'ngu van', 'APPROVED', GETDATE()),
              (N'Tiếng Anh', N'tieng anh', 'APPROVED', GETDATE()),
              (N'Tin học', N'tin hoc', 'APPROVED', GETDATE()),
              (N'Kinh tế', N'kinh te', 'APPROVED', GETDATE()),
              (N'Kỹ năng mềm', N'ky nang mem', 'APPROVED', GETDATE()),
              (N'Triết học', N'triet hoc', 'APPROVED', GETDATE()),
              (N'Lịch sử', N'lich su', 'APPROVED', GETDATE()),
              (N'Địa lý', N'dia ly', 'APPROVED', GETDATE()),
              (N'Khác', N'khac', 'APPROVED', GETDATE());
        END
        IF COL_LENGTH('documents', 'view_count') IS NULL
            ALTER TABLE documents ADD view_count INT NOT NULL CONSTRAINT DF_documents_view_count DEFAULT 0;
        IF COL_LENGTH('chat_sessions', 'attached_document_id') IS NULL
            ALTER TABLE chat_sessions ADD attached_document_id INT NULL;
        IF COL_LENGTH('chat_sessions', 'attached_document_version_id') IS NULL
            ALTER TABLE chat_sessions ADD attached_document_version_id INT NULL;
        IF COL_LENGTH('chat_sessions', 'current_attachment_epoch') IS NULL
            ALTER TABLE chat_sessions ADD current_attachment_epoch INT NOT NULL CONSTRAINT DF_chat_sessions_current_attachment_epoch DEFAULT 0;
        IF COL_LENGTH('chat_messages', 'attachment_epoch') IS NULL
            ALTER TABLE chat_messages ADD attachment_epoch INT NOT NULL CONSTRAINT DF_chat_messages_attachment_epoch DEFAULT 0;
        IF COL_LENGTH('chat_messages', 'context_document_id') IS NULL
            ALTER TABLE chat_messages ADD context_document_id INT NULL;
        IF COL_LENGTH('chat_messages', 'context_document_version_id') IS NULL
            ALTER TABLE chat_messages ADD context_document_version_id INT NULL;
        IF COL_LENGTH('chat_messages', 'message_kind') IS NULL
            ALTER TABLE chat_messages ADD message_kind VARCHAR(30) NOT NULL CONSTRAINT DF_chat_messages_message_kind DEFAULT 'USER_MESSAGE';
        IF OBJECT_ID('subscription_histories', 'U') IS NULL
        BEGIN
            CREATE TABLE subscription_histories (
                history_id INT IDENTITY(1,1) PRIMARY KEY,
                user_id INT NOT NULL,
                transaction_id INT NULL,
                old_tier_id INT NOT NULL,
                new_tier_id INT NOT NULL,
                tier_name_snapshot NVARCHAR(50) NULL,
                price_snapshot DECIMAL(18,2) NOT NULL DEFAULT 0.0,
                currency_snapshot VARCHAR(10) NOT NULL DEFAULT 'VND',
                duration_days_snapshot INT NOT NULL DEFAULT 30,
                storage_limit_snapshot INT NULL,
                ai_prompt_limit_snapshot INT NULL,
                pricing_policy_snapshot VARCHAR(50) NULL,
                purchase_type VARCHAR(50) NULL,
                change_reason NVARCHAR(100) NOT NULL,
                changed_at DATETIME2 NOT NULL DEFAULT GETDATE(),
                purchased_at DATETIME2 NULL,
                effective_from DATETIME2 NULL,
                effective_until DATETIME2 NULL,
                CONSTRAINT FK_subscription_histories_users FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE,
                CONSTRAINT FK_subscription_histories_transactions FOREIGN KEY (transaction_id) REFERENCES transactions(transaction_id) ON DELETE NO ACTION
            );
        END
        ELSE
        BEGIN
            IF COL_LENGTH('subscription_histories', 'transaction_id') IS NULL
                ALTER TABLE subscription_histories ADD transaction_id INT NULL;
            IF COL_LENGTH('subscription_histories', 'tier_name_snapshot') IS NULL
                ALTER TABLE subscription_histories ADD tier_name_snapshot NVARCHAR(50) NULL;
            IF COL_LENGTH('subscription_histories', 'price_snapshot') IS NULL
                ALTER TABLE subscription_histories ADD price_snapshot DECIMAL(18, 2) NOT NULL CONSTRAINT DF_subscription_histories_price_snapshot DEFAULT 0;
            IF COL_LENGTH('subscription_histories', 'currency_snapshot') IS NULL
                ALTER TABLE subscription_histories ADD currency_snapshot VARCHAR(10) NOT NULL CONSTRAINT DF_subscription_histories_currency_snapshot DEFAULT 'VND';
            IF COL_LENGTH('subscription_histories', 'duration_days_snapshot') IS NULL
                ALTER TABLE subscription_histories ADD duration_days_snapshot INT NOT NULL CONSTRAINT DF_subscription_histories_duration_days_snapshot DEFAULT 30;
            IF COL_LENGTH('subscription_histories', 'storage_limit_snapshot') IS NULL
                ALTER TABLE subscription_histories ADD storage_limit_snapshot INT NULL;
            IF COL_LENGTH('subscription_histories', 'ai_prompt_limit_snapshot') IS NULL
                ALTER TABLE subscription_histories ADD ai_prompt_limit_snapshot INT NULL;
            IF COL_LENGTH('subscription_histories', 'pricing_policy_snapshot') IS NULL
                ALTER TABLE subscription_histories ADD pricing_policy_snapshot VARCHAR(50) NULL;
            IF COL_LENGTH('subscription_histories', 'purchase_type') IS NULL
                ALTER TABLE subscription_histories ADD purchase_type VARCHAR(50) NULL;
            IF COL_LENGTH('subscription_histories', 'purchased_at') IS NULL
                ALTER TABLE subscription_histories ADD purchased_at DATETIME2 NULL;
            IF COL_LENGTH('subscription_histories', 'effective_from') IS NULL
                ALTER TABLE subscription_histories ADD effective_from DATETIME2 NULL;
            IF COL_LENGTH('subscription_histories', 'effective_until') IS NULL
                ALTER TABLE subscription_histories ADD effective_until DATETIME2 NULL;
        END
        IF COL_LENGTH('subject_categories', 'parent_subject_id') IS NULL
            ALTER TABLE subject_categories ADD parent_subject_id INT NULL;
        IF COL_LENGTH('subject_categories', 'depth') IS NULL
            ALTER TABLE subject_categories ADD depth INT NOT NULL CONSTRAINT DF_subject_categories_depth DEFAULT 0;
        IF COL_LENGTH('subject_categories', 'sort_order') IS NULL
            ALTER TABLE subject_categories ADD sort_order INT NOT NULL CONSTRAINT DF_subject_categories_sort_order DEFAULT 0;
        IF NOT EXISTS (
            SELECT 1
            FROM sys.foreign_key_columns fkc
            WHERE fkc.parent_object_id = OBJECT_ID('subject_categories')
              AND COL_NAME(fkc.parent_object_id, fkc.parent_column_id) = 'parent_subject_id'
        )
            ALTER TABLE subject_categories ADD CONSTRAINT FK_subject_categories_parent
                FOREIGN KEY (parent_subject_id) REFERENCES subject_categories(subject_id) ON DELETE NO ACTION;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('subject_categories') AND name = 'IX_subject_categories_parent_subject_id')
            CREATE NONCLUSTERED INDEX IX_subject_categories_parent_subject_id
                ON subject_categories(parent_subject_id, sort_order, name);

        DECLARE @seedMathSubjectId INT = (SELECT TOP 1 subject_id FROM subject_categories WHERE normalized_name = N'toan hoc' AND parent_subject_id IS NULL);
        DECLARE @seedPhysicsSubjectId INT = (SELECT TOP 1 subject_id FROM subject_categories WHERE normalized_name = N'vat ly' AND parent_subject_id IS NULL);
        DECLARE @seedComputingSubjectId INT = (SELECT TOP 1 subject_id FROM subject_categories WHERE normalized_name = N'tin hoc' AND parent_subject_id IS NULL);

        IF @seedMathSubjectId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM subject_categories WHERE parent_subject_id = @seedMathSubjectId AND normalized_name = N'toan hinh')
            INSERT INTO subject_categories (name, normalized_name, parent_subject_id, depth, sort_order, status, created_at)
            VALUES (N'Toán hình', N'toan hinh', @seedMathSubjectId, 1, 10, 'APPROVED', GETDATE());
        IF @seedMathSubjectId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM subject_categories WHERE parent_subject_id = @seedMathSubjectId AND normalized_name = N'toan so')
            INSERT INTO subject_categories (name, normalized_name, parent_subject_id, depth, sort_order, status, created_at)
            VALUES (N'Toán số', N'toan so', @seedMathSubjectId, 1, 20, 'APPROVED', GETDATE());

        IF @seedPhysicsSubjectId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM subject_categories WHERE parent_subject_id = @seedPhysicsSubjectId AND normalized_name = N'co hoc')
            INSERT INTO subject_categories (name, normalized_name, parent_subject_id, depth, sort_order, status, created_at)
            VALUES (N'Cơ học', N'co hoc', @seedPhysicsSubjectId, 1, 10, 'APPROVED', GETDATE());
        IF @seedPhysicsSubjectId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM subject_categories WHERE parent_subject_id = @seedPhysicsSubjectId AND normalized_name = N'dien hoc')
            INSERT INTO subject_categories (name, normalized_name, parent_subject_id, depth, sort_order, status, created_at)
            VALUES (N'Điện học', N'dien hoc', @seedPhysicsSubjectId, 1, 20, 'APPROVED', GETDATE());
        IF @seedPhysicsSubjectId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM subject_categories WHERE parent_subject_id = @seedPhysicsSubjectId AND normalized_name = N'quang hoc')
            INSERT INTO subject_categories (name, normalized_name, parent_subject_id, depth, sort_order, status, created_at)
            VALUES (N'Quang học', N'quang hoc', @seedPhysicsSubjectId, 1, 30, 'APPROVED', GETDATE());

        IF @seedComputingSubjectId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM subject_categories WHERE parent_subject_id = @seedComputingSubjectId AND normalized_name = N'lap trinh')
            INSERT INTO subject_categories (name, normalized_name, parent_subject_id, depth, sort_order, status, created_at)
            VALUES (N'Lập trình', N'lap trinh', @seedComputingSubjectId, 1, 10, 'APPROVED', GETDATE());
        IF @seedComputingSubjectId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM subject_categories WHERE parent_subject_id = @seedComputingSubjectId AND normalized_name = N'co so du lieu')
            INSERT INTO subject_categories (name, normalized_name, parent_subject_id, depth, sort_order, status, created_at)
            VALUES (N'Cơ sở dữ liệu', N'co so du lieu', @seedComputingSubjectId, 1, 20, 'APPROVED', GETDATE());
        IF @seedComputingSubjectId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM subject_categories WHERE parent_subject_id = @seedComputingSubjectId AND normalized_name = N'tri tue nhan tao')
            INSERT INTO subject_categories (name, normalized_name, parent_subject_id, depth, sort_order, status, created_at)
            VALUES (N'Trí tuệ nhân tạo', N'tri tue nhan tao', @seedComputingSubjectId, 1, 30, 'APPROVED', GETDATE());
        IF COL_LENGTH('documents', 'subject') IS NULL
            ALTER TABLE documents ADD subject NVARCHAR(100) NOT NULL CONSTRAINT DF_documents_subject DEFAULT N'Khác';
        IF COL_LENGTH('documents', 'requested_visibility') IS NULL ALTER TABLE documents ADD requested_visibility VARCHAR(20) NOT NULL CONSTRAINT DF_documents_requested_visibility DEFAULT 'PRIVATE';
        DECLARE @roleConstraint SYSNAME;
        SELECT TOP 1 @roleConstraint = cc.name FROM sys.check_constraints cc WHERE cc.parent_object_id = OBJECT_ID('users') AND cc.definition LIKE '%role%';
        IF @roleConstraint IS NOT NULL EXEC(N'ALTER TABLE users DROP CONSTRAINT [' + @roleConstraint + N']');
        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('users') AND name = 'CK_users_role')
            ALTER TABLE users WITH NOCHECK ADD CONSTRAINT CK_users_role CHECK (role IN ('STUDENT','MODERATOR','ADMIN'));

        DECLARE @aiParsingConstraint SYSNAME;
        SELECT TOP 1 @aiParsingConstraint = cc.name FROM sys.check_constraints cc 
          WHERE cc.parent_object_id = OBJECT_ID('documents') AND cc.definition LIKE '%ai_parsing_status%';
        IF @aiParsingConstraint IS NOT NULL EXEC(N'ALTER TABLE documents DROP CONSTRAINT [' + @aiParsingConstraint + N']');
        ALTER TABLE documents WITH NOCHECK ADD CONSTRAINT CK_documents_ai_parsing_status
          CHECK (ai_parsing_status IN ('QUEUED','PENDING','PROCESSING','CHUNKING','COMPLETED','FAILED','READY'));

        DECLARE @lifecycleConstraint SYSNAME;
        SELECT TOP 1 @lifecycleConstraint = cc.name FROM sys.check_constraints cc 
          WHERE cc.parent_object_id = OBJECT_ID('documents') AND cc.definition LIKE '%lifecycle_status%';
        IF @lifecycleConstraint IS NOT NULL EXEC(N'ALTER TABLE documents DROP CONSTRAINT [' + @lifecycleConstraint + N']');
        ALTER TABLE documents WITH NOCHECK ADD CONSTRAINT CK_documents_lifecycle_status
          CHECK (lifecycle_status IN ('DRAFT','PRIVATE','PENDING_APPROVAL','APPROVED','REJECTED','TRASHED','RESTRICTED'));

        IF COL_LENGTH('documents', 'moderation_status') IS NULL ALTER TABLE documents ADD moderation_status VARCHAR(30) NOT NULL CONSTRAINT DF_documents_moderation_status DEFAULT 'NOT_REQUESTED';
        IF COL_LENGTH('documents', 'moderation_submitted_at') IS NULL ALTER TABLE documents ADD moderation_submitted_at DATETIME2 NULL;
        IF COL_LENGTH('documents', 'moderated_at') IS NULL ALTER TABLE documents ADD moderated_at DATETIME2 NULL;
        IF COL_LENGTH('documents', 'moderated_by_user_id') IS NULL ALTER TABLE documents ADD moderated_by_user_id INT NULL;
        IF COL_LENGTH('documents', 'moderation_note') IS NULL ALTER TABLE documents ADD moderation_note NVARCHAR(1000) NULL;
        EXEC(N'UPDATE documents SET requested_visibility=''PUBLIC'', moderation_status=''APPROVED'' WHERE sharing_permission=''PUBLIC'' AND moderation_status=''NOT_REQUESTED''');
        IF COL_LENGTH('document_reports', 'report_type') IS NULL ALTER TABLE document_reports ADD report_type VARCHAR(20) NOT NULL CONSTRAINT DF_reports_type DEFAULT 'COMMUNITY';
        IF COL_LENGTH('document_reports', 'claimant_name') IS NULL ALTER TABLE document_reports ADD claimant_name NVARCHAR(150) NULL;
        IF COL_LENGTH('document_reports', 'claimant_email') IS NULL ALTER TABLE document_reports ADD claimant_email NVARCHAR(200) NULL;
        IF COL_LENGTH('document_reports', 'original_work_url') IS NULL ALTER TABLE document_reports ADD original_work_url NVARCHAR(1000) NULL;
        IF COL_LENGTH('document_reports', 'evidence_description') IS NULL ALTER TABLE document_reports ADD evidence_description NVARCHAR(2000) NULL;
        IF COL_LENGTH('document_reports', 'assigned_moderator_id') IS NULL ALTER TABLE document_reports ADD assigned_moderator_id INT NULL;
        IF COL_LENGTH('document_reports', 'moderator_note') IS NULL ALTER TABLE document_reports ADD moderator_note NVARCHAR(1000) NULL;
        IF COL_LENGTH('document_reports', 'previous_sharing_permission') IS NULL ALTER TABLE document_reports ADD previous_sharing_permission VARCHAR(20) NULL;
        IF COL_LENGTH('document_reports', 'restricted_at') IS NULL ALTER TABLE document_reports ADD restricted_at DATETIME2 NULL;
        IF COL_LENGTH('document_reports', 'reported_version_id') IS NULL ALTER TABLE document_reports ADD reported_version_id INT NULL;
        DECLARE @reportStatusConstraint SYSNAME;
        SELECT TOP 1 @reportStatusConstraint = cc.name FROM sys.check_constraints cc
          WHERE cc.parent_object_id = OBJECT_ID('document_reports') AND cc.definition LIKE '%status%';
        IF @reportStatusConstraint IS NOT NULL EXEC(N'ALTER TABLE document_reports DROP CONSTRAINT [' + @reportStatusConstraint + N']');
        ALTER TABLE document_reports WITH NOCHECK ADD CONSTRAINT CK_document_reports_status
          CHECK (status IN ('PENDING','IN_REVIEW','RESTRICTED','NO_VIOLATION','VIOLATION_CONFIRMED','APPEALED','RESTORED','CLOSED','DISMISSED','ACTIONED','ACTION_TAKEN'));
        IF OBJECT_ID('moderation_actions', 'U') IS NULL
            CREATE TABLE moderation_actions (action_id BIGINT IDENTITY PRIMARY KEY, actor_user_id INT NOT NULL, document_id INT NULL, report_id INT NULL, action VARCHAR(50) NOT NULL, previous_status VARCHAR(30) NULL, new_status VARCHAR(30) NULL, note NVARCHAR(1000) NULL, created_at DATETIME2 NOT NULL DEFAULT GETDATE(), CONSTRAINT FK_moderation_action_actor FOREIGN KEY(actor_user_id) REFERENCES users(user_id));
        IF OBJECT_ID('moderation_appeals', 'U') IS NULL
            CREATE TABLE moderation_appeals (appeal_id INT IDENTITY PRIMARY KEY, report_id INT NOT NULL UNIQUE, submitted_by_user_id INT NOT NULL, explanation NVARCHAR(2000) NOT NULL, evidence_url NVARCHAR(1000) NULL, status VARCHAR(20) NOT NULL DEFAULT 'PENDING', reviewed_by_user_id INT NULL, review_note NVARCHAR(1000) NULL, created_at DATETIME2 NOT NULL DEFAULT GETDATE(), reviewed_at DATETIME2 NULL, CONSTRAINT FK_moderation_appeal_report FOREIGN KEY(report_id) REFERENCES document_reports(report_id) ON DELETE CASCADE);
        IF OBJECT_ID('moderation_notices', 'U') IS NULL
            CREATE TABLE moderation_notices (notice_id BIGINT IDENTITY PRIMARY KEY, user_id INT NOT NULL, document_id INT NULL, report_id INT NULL, transaction_id INT NULL, related_user_id INT NULL, action_url NVARCHAR(500) NULL, type VARCHAR(50) NOT NULL, title NVARCHAR(200) NOT NULL, message NVARCHAR(1500) NOT NULL, can_appeal BIT NOT NULL DEFAULT 0, is_read BIT NOT NULL DEFAULT 0, created_at DATETIME2 NOT NULL DEFAULT GETDATE(), CONSTRAINT FK_moderation_notice_user FOREIGN KEY(user_id) REFERENCES users(user_id));
        IF OBJECT_ID('moderation_notices', 'U') IS NOT NULL
        BEGIN
            ALTER TABLE moderation_notices ALTER COLUMN document_id INT NULL;
            IF COL_LENGTH('moderation_notices', 'transaction_id') IS NULL ALTER TABLE moderation_notices ADD transaction_id INT NULL;
            IF COL_LENGTH('moderation_notices', 'related_user_id') IS NULL ALTER TABLE moderation_notices ADD related_user_id INT NULL;
            IF COL_LENGTH('moderation_notices', 'action_url') IS NULL ALTER TABLE moderation_notices ADD action_url NVARCHAR(500) NULL;
            ALTER TABLE moderation_notices ALTER COLUMN type VARCHAR(50) NOT NULL;
        END
        IF OBJECT_ID('document_activities', 'U') IS NULL
        BEGIN
            CREATE TABLE document_activities (
                activity_id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                document_id INT NOT NULL,
                user_id INT NOT NULL,
                activity_type VARCHAR(20) NOT NULL,
                created_at DATETIME2 NOT NULL CONSTRAINT DF_document_activities_created_at DEFAULT GETDATE(),
                CONSTRAINT FK_document_activities_document FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE CASCADE,
                CONSTRAINT FK_document_activities_user FOREIGN KEY(user_id) REFERENCES users(user_id)
            );
            CREATE INDEX IX_document_activities_document_type ON document_activities(document_id, activity_type);
        END
        IF OBJECT_ID('document_shares', 'U') IS NULL
        BEGIN
            CREATE TABLE document_shares (
                share_id BIGINT IDENTITY(1,1) PRIMARY KEY,
                document_id INT NOT NULL,
                owner_user_id INT NOT NULL,
                shared_with_user_id INT NOT NULL,
                created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
                CONSTRAINT UQ_document_shares UNIQUE(document_id, shared_with_user_id),
                CONSTRAINT FK_document_shares_document FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE CASCADE,
                CONSTRAINT FK_document_shares_user FOREIGN KEY(shared_with_user_id) REFERENCES users(user_id)
            );
        END
        IF OBJECT_ID('document_chunks', 'U') IS NULL
        BEGIN
            CREATE TABLE document_chunks (
                chunk_id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                document_id INT NOT NULL,
                chunk_index INT NOT NULL,
                heading_path NVARCHAR(MAX) NULL,
                page_number INT NULL,
                text NVARCHAR(MAX) NOT NULL,
                start_offset INT NOT NULL,
                end_offset INT NOT NULL,
                bounding_box_x FLOAT NULL,
                bounding_box_y FLOAT NULL,
                bounding_box_width FLOAT NULL,
                bounding_box_height FLOAT NULL,
                ocr_confidence FLOAT NULL,
                created_at DATETIME2 NOT NULL CONSTRAINT DF_document_chunks_created_at DEFAULT GETDATE(),
                CONSTRAINT FK_document_chunks_document FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE CASCADE,
                CONSTRAINT UQ_document_chunks_document_index UNIQUE(document_id, chunk_index)
            );
            CREATE INDEX IX_document_chunks_document ON document_chunks(document_id);
        END
        IF OBJECT_ID('document_extracted_text', 'U') IS NOT NULL
        BEGIN
            IF COL_LENGTH('document_extracted_text', 'document_version_id') IS NULL ALTER TABLE document_extracted_text ADD document_version_id INT NULL;
            IF COL_LENGTH('document_extracted_text', 'total_pages') IS NULL ALTER TABLE document_extracted_text ADD total_pages INT NULL;
            IF COL_LENGTH('document_extracted_text', 'readable_pages') IS NULL ALTER TABLE document_extracted_text ADD readable_pages INT NULL;
            IF COL_LENGTH('document_extracted_text', 'extraction_coverage') IS NULL ALTER TABLE document_extracted_text ADD extraction_coverage DECIMAL(5,4) NULL;
            IF COL_LENGTH('document_extracted_text', 'image_content_detected') IS NULL ALTER TABLE document_extracted_text ADD image_content_detected BIT NOT NULL CONSTRAINT DF_doc_ext_img DEFAULT 0;
            IF COL_LENGTH('document_extracted_text', 'unread_image_content_warning') IS NULL ALTER TABLE document_extracted_text ADD unread_image_content_warning NVARCHAR(500) NULL;
            IF COL_LENGTH('document_extracted_text', 'ocr_region_count') IS NULL ALTER TABLE document_extracted_text ADD ocr_region_count INT NOT NULL CONSTRAINT DF_doc_ext_ocr DEFAULT 0;

            DECLARE @legacyExtractedTextUnique SYSNAME;
            SELECT TOP 1 @legacyExtractedTextUnique = kc.name
            FROM sys.key_constraints kc
            INNER JOIN sys.index_columns ic
                ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
            WHERE kc.parent_object_id = OBJECT_ID('document_extracted_text')
              AND kc.type = 'UQ'
            GROUP BY kc.name
            HAVING COUNT(*) = 1
               AND MAX(CASE WHEN COL_NAME(ic.object_id, ic.column_id) = 'document_id' THEN 1 ELSE 0 END) = 1;

            IF @legacyExtractedTextUnique IS NOT NULL
                EXEC(N'ALTER TABLE document_extracted_text DROP CONSTRAINT [' + @legacyExtractedTextUnique + N']');

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('document_extracted_text') AND name = 'UQ_document_extracted_text_doc_ver')
                EXEC(N'CREATE UNIQUE INDEX UQ_document_extracted_text_doc_ver
                    ON document_extracted_text(document_id, document_version_id)
                    WHERE document_version_id IS NOT NULL');

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('document_extracted_text') AND name = 'UQ_document_extracted_text_doc_legacy')
                EXEC(N'CREATE UNIQUE INDEX UQ_document_extracted_text_doc_legacy
                    ON document_extracted_text(document_id)
                    WHERE document_version_id IS NULL');
        END

        IF OBJECT_ID('document_chunks', 'U') IS NOT NULL
        BEGIN
            ALTER TABLE document_chunks ALTER COLUMN heading_path NVARCHAR(MAX) NULL;
            IF COL_LENGTH('document_chunks', 'document_version_id') IS NULL ALTER TABLE document_chunks ADD document_version_id INT NULL;
            IF COL_LENGTH('document_chunks', 'bounding_box_x') IS NULL ALTER TABLE document_chunks ADD bounding_box_x FLOAT NULL;
            IF COL_LENGTH('document_chunks', 'bounding_box_y') IS NULL ALTER TABLE document_chunks ADD bounding_box_y FLOAT NULL;
            IF COL_LENGTH('document_chunks', 'bounding_box_width') IS NULL ALTER TABLE document_chunks ADD bounding_box_width FLOAT NULL;
            IF COL_LENGTH('document_chunks', 'bounding_box_height') IS NULL ALTER TABLE document_chunks ADD bounding_box_height FLOAT NULL;
            IF COL_LENGTH('document_chunks', 'ocr_confidence') IS NULL ALTER TABLE document_chunks ADD ocr_confidence FLOAT NULL;

            IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_document_chunks_document_index')
                ALTER TABLE document_chunks DROP CONSTRAINT UQ_document_chunks_document_index;
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID('document_chunks')
                  AND name = 'UQ_document_chunks_document_version_index')
                CREATE UNIQUE INDEX UQ_document_chunks_document_version_index
                    ON document_chunks(document_id, document_version_id, chunk_index)
                    WHERE document_version_id IS NOT NULL;

            EXEC(N'
                UPDATE v
                SET ai_parsing_status = CASE
                    WHEN EXISTS (SELECT 1 FROM document_chunks c WHERE c.document_version_id = v.version_id) THEN ''READY''
                    WHEN d.current_version_id = v.version_id THEN ISNULL(d.ai_parsing_status, ''PENDING'')
                    ELSE ISNULL(v.ai_parsing_status, ''PENDING'')
                END
                FROM document_versions v
                INNER JOIN documents d ON d.document_id = v.document_id;
            ');
        END

        IF OBJECT_ID('document_ocr_regions', 'U') IS NULL
        BEGIN
            CREATE TABLE document_ocr_regions (
                ocr_region_id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                document_id INT NOT NULL,
                page_number INT NOT NULL,
                region_type VARCHAR(30) NULL,
                bounding_box_left FLOAT NOT NULL,
                bounding_box_top FLOAT NOT NULL,
                bounding_box_width FLOAT NOT NULL,
                bounding_box_height FLOAT NOT NULL,
                confidence DECIMAL(5,4) NOT NULL,
                recognized_text NVARCHAR(MAX) NULL,
                source VARCHAR(30) NULL,
                created_at DATETIME2 NOT NULL CONSTRAINT DF_doc_ocr_created DEFAULT GETDATE(),
                CONSTRAINT FK_doc_ocr_document FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE CASCADE
            );
            EXEC(N'CREATE INDEX IX_doc_ocr_doc_page ON document_ocr_regions(document_id, page_number)');
        END
        IF OBJECT_ID('transfer_configurations', 'U') IS NULL
        BEGIN
            CREATE TABLE transfer_configurations (
                configuration_id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                bank_code NVARCHAR(30) NOT NULL,
                bank_name NVARCHAR(100) NOT NULL,
                account_number NVARCHAR(50) NOT NULL,
                account_name NVARCHAR(150) NOT NULL,
                qr_template NVARCHAR(30) NOT NULL CONSTRAINT DF_transfer_config_qr_template DEFAULT 'compact2',
                transfer_content_prefix NVARCHAR(50) NOT NULL CONSTRAINT DF_transfer_config_prefix DEFAULT 'AIStudyHub',
                is_active BIT NOT NULL CONSTRAINT DF_transfer_config_active DEFAULT 0,
                updated_at DATETIME2 NOT NULL CONSTRAINT DF_transfer_config_updated DEFAULT GETDATE()
            );
            INSERT INTO transfer_configurations (bank_code, bank_name, account_number, account_name, is_active)
            VALUES ('', '', '', '', 0);
        END
        IF COL_LENGTH('transactions', 'reference_code') IS NULL
            ALTER TABLE transactions ADD reference_code VARCHAR(100) NULL;
        IF COL_LENGTH('transactions', 'bank_id') IS NULL
            ALTER TABLE transactions ADD bank_id VARCHAR(50) NULL;
        IF COL_LENGTH('transactions', 'approver_id') IS NULL
            ALTER TABLE transactions ADD approver_id INT NULL;
        IF COL_LENGTH('transactions', 'failure_reason') IS NULL
            ALTER TABLE transactions ADD failure_reason NVARCHAR(500) NULL;
        IF COL_LENGTH('transactions', 'original_transaction_id') IS NULL
            ALTER TABLE transactions ADD original_transaction_id INT NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_transactions_original_tx_success' AND object_id = OBJECT_ID('transactions'))
            EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX UX_transactions_original_tx_success ON transactions(original_transaction_id) WHERE original_transaction_id IS NOT NULL AND status = ''SUCCESS''');
        IF OBJECT_ID('balance_ledgers', 'U') IS NULL
        BEGIN
            CREATE TABLE balance_ledgers (
                ledger_id BIGINT IDENTITY(1,1) PRIMARY KEY,
                user_id INT NOT NULL,
                ledger_sequence BIGINT NOT NULL,
                transaction_id INT NULL,
                amount DECIMAL(10,2) NOT NULL,
                previous_balance DECIMAL(10,2) NOT NULL,
                current_balance DECIMAL(10,2) NOT NULL,
                action_type VARCHAR(30) NOT NULL,
                description NVARCHAR(500) NULL,
                previous_hash VARCHAR(256) NOT NULL,
                current_hash VARCHAR(256) NOT NULL,
                hash_version INT NOT NULL CONSTRAINT DF_ledgers_hash_ver DEFAULT 1,
                key_version INT NOT NULL CONSTRAINT DF_ledgers_key_ver DEFAULT 1,
                created_at_utc DATETIME2 NOT NULL CONSTRAINT DF_ledgers_created_utc DEFAULT GETUTCDATE(),
                CONSTRAINT FK_balance_ledgers_user FOREIGN KEY(user_id) REFERENCES users(user_id) ON DELETE CASCADE,
                CONSTRAINT FK_balance_ledgers_tx FOREIGN KEY(transaction_id) REFERENCES transactions(transaction_id) ON DELETE SET NULL,
                CONSTRAINT UX_balance_ledgers_seq UNIQUE(user_id, ledger_sequence)
            );
        END
        IF OBJECT_ID('balance_ledgers', 'U') IS NOT NULL
        BEGIN
            IF COL_LENGTH('balance_ledgers', 'ledger_sequence') IS NULL
                ALTER TABLE balance_ledgers ADD ledger_sequence BIGINT NOT NULL CONSTRAINT DF_balance_ledgers_seq DEFAULT 1;
            IF COL_LENGTH('balance_ledgers', 'previous_hash') IS NULL
                ALTER TABLE balance_ledgers ADD previous_hash VARCHAR(256) NOT NULL CONSTRAINT DF_balance_ledgers_prev DEFAULT 'GENESIS';
            IF COL_LENGTH('balance_ledgers', 'current_hash') IS NULL
                ALTER TABLE balance_ledgers ADD current_hash VARCHAR(256) NOT NULL CONSTRAINT DF_balance_ledgers_curr DEFAULT '';
            IF COL_LENGTH('balance_ledgers', 'hash_version') IS NULL
                ALTER TABLE balance_ledgers ADD hash_version INT NOT NULL CONSTRAINT DF_balance_ledgers_hver DEFAULT 1;
            IF COL_LENGTH('balance_ledgers', 'key_version') IS NULL
                ALTER TABLE balance_ledgers ADD key_version INT NOT NULL CONSTRAINT DF_balance_ledgers_kver DEFAULT 1;
            IF COL_LENGTH('balance_ledgers', 'created_at_utc') IS NULL
                ALTER TABLE balance_ledgers ADD created_at_utc DATETIME2 NOT NULL CONSTRAINT DF_balance_ledgers_cutc DEFAULT GETUTCDATE();
        END
        IF NOT EXISTS (SELECT 1 FROM subscriptions)
        BEGIN
            SET IDENTITY_INSERT subscriptions ON;
            INSERT INTO subscriptions (tier_id, tier_name, price, max_storage_mb, ai_prompt_limit_per_day, total_storage_mb)
            VALUES 
                (1, 'Free', 0, 50, 5, 50),
                (2, 'Basic', 0, 200, 20, 200),
                (3, 'Premium', 100000, 500, 100, 500);
            SET IDENTITY_INSERT subscriptions OFF;
        END

        IF OBJECT_ID('document_processing_jobs', 'U') IS NULL
        BEGIN
            CREATE TABLE document_processing_jobs (
                job_id INT IDENTITY(1,1) PRIMARY KEY,
                document_id INT NOT NULL,
                document_version_id INT NULL,
                status VARCHAR(20) NOT NULL CONSTRAINT DF_doc_jobs_status DEFAULT 'QUEUED',
                attempt_count INT NOT NULL CONSTRAINT DF_doc_jobs_attempt DEFAULT 0,
                max_attempts INT NOT NULL CONSTRAINT DF_doc_jobs_max_attempt DEFAULT 3,
                available_at DATETIME2 NOT NULL CONSTRAINT DF_doc_jobs_avail DEFAULT GETUTCDATE(),
                locked_at DATETIME2 NULL,
                locked_until DATETIME2 NULL,
                locked_by NVARCHAR(100) NULL,
                last_error NVARCHAR(MAX) NULL,
                created_at DATETIME2 NOT NULL CONSTRAINT DF_doc_jobs_created DEFAULT GETUTCDATE(),
                completed_at DATETIME2 NULL,
                CONSTRAINT FK_doc_jobs_document FOREIGN KEY (document_id) REFERENCES documents(document_id) ON DELETE CASCADE,
                CONSTRAINT FK_doc_jobs_version FOREIGN KEY (document_version_id) REFERENCES document_versions(version_id) ON DELETE NO ACTION
            );
            EXEC(N'CREATE INDEX IX_doc_jobs_status_available ON document_processing_jobs(status, available_at)');
        END

        IF OBJECT_ID('ai_usages', 'U') IS NULL
        BEGIN
            CREATE TABLE ai_usages (
                usage_id BIGINT IDENTITY(1,1) PRIMARY KEY,
                user_id INT NOT NULL,
                provider VARCHAR(50) NOT NULL CONSTRAINT DF_ai_usages_provider DEFAULT 'Google',
                model NVARCHAR(100) NOT NULL,
                operation VARCHAR(50) NOT NULL CONSTRAINT DF_ai_usages_op DEFAULT 'CHAT',
                prompt_tokens INT NOT NULL,
                completion_tokens INT NOT NULL,
                cached_tokens INT NOT NULL CONSTRAINT DF_ai_usages_cached DEFAULT 0,
                total_tokens INT NOT NULL,
                latency_ms BIGINT NOT NULL,
                status VARCHAR(20) NOT NULL CONSTRAINT DF_ai_usages_status DEFAULT 'SUCCESS',
                error_code NVARCHAR(100) NULL,
                estimated_cost DECIMAL(18,6) NOT NULL CONSTRAINT DF_ai_usages_cost DEFAULT 0,
                currency VARCHAR(10) NOT NULL CONSTRAINT DF_ai_usages_curr DEFAULT 'USD',
                pricing_version VARCHAR(20) NOT NULL CONSTRAINT DF_ai_usages_pv DEFAULT '2026.1',
                request_id VARCHAR(100) NOT NULL,
                created_at DATETIME2 NOT NULL CONSTRAINT DF_ai_usages_created DEFAULT GETUTCDATE(),
                CONSTRAINT FK_ai_usages_user FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE
            );
        END
        IF OBJECT_ID('refresh_token_sessions', 'U') IS NULL
        BEGIN
            CREATE TABLE refresh_token_sessions (
                session_id BIGINT IDENTITY(1,1) PRIMARY KEY,
                user_id INT NOT NULL,
                token_family_id UNIQUEIDENTIFIER NOT NULL,
                parent_session_id BIGINT NULL,
                token_hash NVARCHAR(128) NOT NULL,
                expires_at DATETIME2 NOT NULL,
                created_at DATETIME2 NOT NULL CONSTRAINT DF_refresh_tokens_created DEFAULT GETUTCDATE(),
                created_by_ip NVARCHAR(45) NULL,
                user_agent NVARCHAR(500) NULL,
                revoked_at DATETIME2 NULL,
                revoked_reason NVARCHAR(100) NULL,
                revoked_by_ip NVARCHAR(45) NULL,
                replaced_by_token_hash NVARCHAR(128) NULL,
                is_used BIT NOT NULL CONSTRAINT DF_refresh_tokens_used DEFAULT 0,
                last_used_at DATETIME2 NULL,
                row_version ROWVERSION NOT NULL,
                CONSTRAINT FK_refresh_token_sessions_users FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE
            );
            EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX UQ_refresh_token_sessions_token_hash ON refresh_token_sessions(token_hash)');
            EXEC(N'CREATE NONCLUSTERED INDEX IX_refresh_token_sessions_user_family ON refresh_token_sessions(user_id, token_family_id)');
        END

        IF OBJECT_ID('auth_otp_challenges', 'U') IS NULL
        BEGIN
            CREATE TABLE auth_otp_challenges (
                challenge_id UNIQUEIDENTIFIER PRIMARY KEY,
                normalized_email_hash NVARCHAR(128) NOT NULL,
                purpose NVARCHAR(50) NOT NULL CONSTRAINT DF_auth_otp_purpose DEFAULT 'PASSWORD_RESET',
                otp_hash NVARCHAR(128) NOT NULL,
                attempts INT NOT NULL CONSTRAINT DF_auth_otp_attempts DEFAULT 0,
                max_attempts INT NOT NULL CONSTRAINT DF_auth_otp_max_attempts DEFAULT 5,
                cooldown_until DATETIME2 NOT NULL,
                expires_at DATETIME2 NOT NULL,
                consumed_at DATETIME2 NULL,
                created_at DATETIME2 NOT NULL CONSTRAINT DF_auth_otp_created DEFAULT GETUTCDATE(),
                row_version ROWVERSION NOT NULL
            );
            EXEC(N'CREATE NONCLUSTERED INDEX IX_auth_otp_challenges_email_purpose ON auth_otp_challenges(normalized_email_hash, purpose)');
        END

        IF OBJECT_ID('auth_otp_rate_limits', 'U') IS NULL
        BEGIN
            CREATE TABLE auth_otp_rate_limits (
                normalized_email_hash NVARCHAR(128) NOT NULL,
                purpose NVARCHAR(50) NOT NULL,
                cooldown_until DATETIME2 NOT NULL,
                last_sent_at DATETIME2 NOT NULL,
                request_count INT NOT NULL CONSTRAINT DF_otp_rate_req_count DEFAULT 1,
                CONSTRAINT PK_auth_otp_rate_limits PRIMARY KEY (normalized_email_hash, purpose)
            );
        END

        IF OBJECT_ID('password_reset_grants', 'U') IS NULL
        BEGIN
            CREATE TABLE password_reset_grants (
                grant_id BIGINT IDENTITY(1,1) PRIMARY KEY,
                user_id INT NOT NULL,
                challenge_id UNIQUEIDENTIFIER NOT NULL,
                grant_hash NVARCHAR(128) NOT NULL,
                expires_at DATETIME2 NOT NULL,
                is_consumed BIT NOT NULL CONSTRAINT DF_reset_grants_consumed DEFAULT 0,
                consumed_at DATETIME2 NULL,
                created_at DATETIME2 NOT NULL CONSTRAINT DF_reset_grants_created DEFAULT GETUTCDATE(),
                row_version ROWVERSION NOT NULL,
                CONSTRAINT FK_password_reset_grants_users FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE
            );
            EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX UQ_password_reset_grants_hash ON password_reset_grants(grant_hash)');
        END

        IF COL_LENGTH('transactions', 'payos_order_code') IS NULL
            ALTER TABLE transactions ADD payos_order_code BIGINT NULL;
        IF COL_LENGTH('transactions', 'payment_link_id') IS NULL
            ALTER TABLE transactions ADD payment_link_id NVARCHAR(100) NULL;
        IF COL_LENGTH('transactions', 'reconciliation_locked_until') IS NULL
            ALTER TABLE transactions ADD reconciliation_locked_until DATETIME2 NULL;
        IF COL_LENGTH('transactions', 'reconciliation_attempts') IS NULL
            ALTER TABLE transactions ADD reconciliation_attempts INT NOT NULL CONSTRAINT DF_tx_rec_attempts DEFAULT 0;
        IF COL_LENGTH('transactions', 'last_reconciliation_at') IS NULL
            ALTER TABLE transactions ADD last_reconciliation_at DATETIME2 NULL;
        IF COL_LENGTH('transactions', 'requires_manual_review') IS NULL
            ALTER TABLE transactions ADD requires_manual_review BIT NOT NULL CONSTRAINT DF_tx_manual_review DEFAULT 0;
        IF COL_LENGTH('transactions', 'review_reason') IS NULL
            ALTER TABLE transactions ADD review_reason NVARCHAR(500) NULL;
        IF COL_LENGTH('transactions', 'expected_amount') IS NULL
            ALTER TABLE transactions ADD expected_amount DECIMAL(18,2) NULL;
        IF COL_LENGTH('transactions', 'provider_reported_amount') IS NULL
            ALTER TABLE transactions ADD provider_reported_amount DECIMAL(18,2) NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_transactions_payos_order_code' AND object_id = OBJECT_ID('transactions'))
            EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX UQ_transactions_payos_order_code ON transactions (payos_order_code) WHERE payos_order_code IS NOT NULL');

        DECLARE @txStatusConstraint SYSNAME;
        SELECT TOP 1 @txStatusConstraint = cc.name FROM sys.check_constraints cc WHERE cc.parent_object_id = OBJECT_ID('transactions') AND cc.definition LIKE '%status%';
        IF @txStatusConstraint IS NOT NULL EXEC(N'ALTER TABLE transactions DROP CONSTRAINT [' + @txStatusConstraint + N']');
        ALTER TABLE transactions WITH NOCHECK ADD CONSTRAINT CK_transactions_status
            CHECK (status IN ('PENDING', 'SUCCESS', 'FAILED', 'CANCELLED', 'CREATING', 'CREATE_FAILED', 'EXPIRED'));

        IF OBJECT_ID('payment_webhook_events', 'U') IS NULL
        BEGIN
            CREATE TABLE payment_webhook_events (
                webhook_event_id BIGINT IDENTITY(1,1) PRIMARY KEY,
                provider NVARCHAR(50) NOT NULL CONSTRAINT DF_webhook_provider DEFAULT 'PAYOS',
                provider_event_id NVARCHAR(150) NOT NULL,
                merchant_order_code BIGINT NULL,
                payload_hash NVARCHAR(128) NULL,
                payload_sanitized NVARCHAR(MAX) NULL,
                expected_amount DECIMAL(18,2) NULL,
                received_amount DECIMAL(18,2) NULL,
                currency NVARCHAR(10) NULL,
                requires_manual_review BIT NOT NULL CONSTRAINT DF_webhook_manual_review DEFAULT 0,
                review_reason NVARCHAR(500) NULL,
                is_synthetic_reference BIT NOT NULL CONSTRAINT DF_webhook_synthetic DEFAULT 0,
                processed_at DATETIME2 NOT NULL,
                status NVARCHAR(30) NOT NULL CONSTRAINT DF_webhook_status DEFAULT 'RECEIVED',
                error_message NVARCHAR(1000) NULL,
                created_at DATETIME2 NOT NULL CONSTRAINT DF_webhook_created DEFAULT GETUTCDATE(),
                row_version ROWVERSION NOT NULL
            );
            EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX UQ_payment_webhook_provider_event ON payment_webhook_events (provider, provider_event_id)');
        END

        IF OBJECT_ID('payment_reconciliation_cases', 'U') IS NULL
        BEGIN
            CREATE TABLE payment_reconciliation_cases (
                case_id BIGINT IDENTITY(1,1) PRIMARY KEY,
                transaction_id INT NULL,
                payos_order_code BIGINT NULL,
                provider NVARCHAR(50) NOT NULL CONSTRAINT DF_rec_provider DEFAULT 'PAYOS',
                issue_type NVARCHAR(50) NOT NULL,
                expected_amount DECIMAL(18,2) NULL,
                provider_reported_amount DECIMAL(18,2) NULL,
                currency NVARCHAR(10) NOT NULL CONSTRAINT DF_rec_currency DEFAULT 'VND',
                details NVARCHAR(MAX) NOT NULL,
                status NVARCHAR(30) NOT NULL CONSTRAINT DF_rec_status DEFAULT 'OPEN',
                resolved_at DATETIME2 NULL,
                resolved_by_user_id INT NULL,
                resolution_notes NVARCHAR(MAX) NULL,
                created_at DATETIME2 NOT NULL CONSTRAINT DF_rec_created DEFAULT GETUTCDATE(),
                CONSTRAINT FK_rec_cases_tx FOREIGN KEY (transaction_id) REFERENCES transactions(transaction_id) ON DELETE SET NULL,
                CONSTRAINT FK_rec_cases_user FOREIGN KEY (resolved_by_user_id) REFERENCES users(user_id) ON DELETE SET NULL
            );
            EXEC(N'CREATE NONCLUSTERED INDEX IX_payment_reconciliation_cases_status ON payment_reconciliation_cases (status, created_at)');
        END

        IF OBJECT_ID('chat_message_citations', 'U') IS NULL
        BEGIN
            CREATE TABLE chat_message_citations (
                citation_id BIGINT IDENTITY(1,1) PRIMARY KEY,
                message_id INT NOT NULL,
                document_id INT NOT NULL,
                document_version_id INT NULL,
                chunk_id INT NULL,
                document_title_snapshot NVARCHAR(255) NOT NULL,
                version_number_snapshot INT NOT NULL,
                file_extension_snapshot NVARCHAR(20) NOT NULL,
                page_number_snapshot INT NULL,
                start_offset_snapshot INT NOT NULL,
                end_offset_snapshot INT NOT NULL,
                heading_path_snapshot NVARCHAR(500) NULL,
                snippet NVARCHAR(2000) NOT NULL,
                created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                CONSTRAINT FK_citations_message FOREIGN KEY (message_id) REFERENCES chat_messages(message_id) ON DELETE CASCADE,
                CONSTRAINT FK_citations_doc FOREIGN KEY (document_id) REFERENCES documents(document_id) ON DELETE NO ACTION,
                CONSTRAINT FK_citations_version FOREIGN KEY (document_version_id) REFERENCES document_versions(version_id) ON DELETE NO ACTION,
                CONSTRAINT FK_citations_chunk FOREIGN KEY (chunk_id) REFERENCES document_chunks(chunk_id) ON DELETE NO ACTION
            );
            EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX UQ_chat_message_citations_chunk ON chat_message_citations (message_id, chunk_id) WHERE chunk_id IS NOT NULL');
        END
        """);

    var missingChunks = await db.DocumentExtractedTexts.AsNoTracking()
        .Where(extracted => !db.DocumentChunks.Any(chunk => chunk.DocumentId == extracted.DocumentId))
        .Select(extracted => new { extracted.DocumentId, extracted.DocumentVersionId, extracted.ExtractedText, extracted.CreatedAt })
        .ToListAsync();
    foreach (var extracted in missingChunks)
    {
        db.DocumentChunks.AddRange(AIStudyHub.Application.Services.DocumentChunker.Chunk(
            extracted.DocumentId, extracted.ExtractedText, extracted.CreatedAt ?? DateTime.UtcNow, null, extracted.DocumentVersionId));
    }
    if (missingChunks.Count > 0)
        await db.SaveChangesAsync();

    // Backfill OPENING_BALANCE ledger entry for users who have balance but no ledger history
    var ledgerService = scope.ServiceProvider.GetRequiredService<IBalanceLedgerService>();
    var usersNeedingLedger = await db.Users
        .Where(u => (u.Balance ?? 0) > 0 && !db.BalanceLedgers.Any(l => l.UserId == u.UserId))
        .ToListAsync();
    foreach (var u in usersNeedingLedger)
    {
        decimal balance = u.Balance ?? 0;
        await ledgerService.AppendEntryAsync(u.UserId, null, balance, 0, balance, "OPENING_BALANCE", "Khởi tạo số dư ban đầu");
    }
    if (usersNeedingLedger.Count > 0)
        await db.SaveChangesAsync();

    if (app.Environment.IsDevelopment())
    {
        // 1. Seed Moderator
        const string moderatorEmail = "moderator@aistudyHub.local";
        var moderator = await db.Users.FirstOrDefaultAsync(user => user.Email == moderatorEmail);
        if (moderator == null)
        {
            db.Users.Add(new AIStudyHub.Domain.Entities.User
            {
                Username = "moderator.demo",
                Email = moderatorEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Moderator@123"),
                Role = "MODERATOR",
                Status = "ACTIVE",
                TierId = 2,
                Balance = 0,
                AiPromptsToday = 0,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });
            await db.SaveChangesAsync();
        }

        // 2. Seed Admin
        const string adminEmail = "admin@aistudyhub.local";
        var admin = await db.Users.FirstOrDefaultAsync(user => user.Email == adminEmail);
        if (admin == null)
        {
            var newAdmin = new AIStudyHub.Domain.Entities.User
            {
                Username = "admin.demo",
                Email = adminEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
                Role = "ADMIN",
                Status = "ACTIVE",
                TierId = 3,
                Balance = 1000000,
                AiPromptsToday = 0,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            db.Users.Add(newAdmin);
            await db.SaveChangesAsync();
            await ledgerService.AppendEntryAsync(newAdmin.UserId, null, 1000000, 0, 1000000, "OPENING_BALANCE", "Khởi tạo số dư admin");
            await db.SaveChangesAsync();
        }

        // 3. Seed Student
        const string studentEmail = "student@aistudyhub.local";
        var student = await db.Users.FirstOrDefaultAsync(user => user.Email == studentEmail);
        if (student == null)
        {
            var newStudent = new AIStudyHub.Domain.Entities.User
            {
                Username = "student.demo",
                Email = studentEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Student@123"),
                Role = "STUDENT",
                Status = "ACTIVE",
                TierId = 2,
                Balance = 500000,
                AiPromptsToday = 0,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            db.Users.Add(newStudent);
            await db.SaveChangesAsync();
            await ledgerService.AppendEntryAsync(newStudent.UserId, null, 500000, 0, 500000, "OPENING_BALANCE", "Khởi tạo số dư student demo");
            await db.SaveChangesAsync();
        }
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "AI Study Hub API v1");
    });
}

// Ensure wwwroot directory exists for uploads
string uploadsPath = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads");
if (!Directory.Exists(uploadsPath))
{
    Directory.CreateDirectory(uploadsPath);
}

app.UseHttpsRedirection();
app.UseCors("Frontend");

// Custom Global Exception Handling Middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

static void ValidateStartupConfiguration(IConfiguration config, IHostEnvironment env)
{
    if (env.IsEnvironment("Testing")) return;

    var jwtKey = config["Jwt:Key"];
    if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
        throw new InvalidOperationException("Startup Error: 'Jwt:Key' must be configured with at least 32 characters.");

    var otpPepper = config["Auth:OtpPepper"];
    if (string.IsNullOrWhiteSpace(otpPepper) || otpPepper.Length < 16)
        throw new InvalidOperationException("Startup Error: 'Auth:OtpPepper' must be configured with at least 16 characters.");

    var ledgerKey = config["Ledger:SecretKey"];
    if (string.IsNullOrWhiteSpace(ledgerKey) || ledgerKey.Length < 16)
        throw new InvalidOperationException("Startup Error: 'Ledger:SecretKey' must be configured with at least 16 characters.");

    if (env.IsProduction())
    {
        if (config.GetValue<bool>("PayOS:UseMock"))
            throw new InvalidOperationException("Startup Error: 'PayOS:UseMock' is strictly prohibited in Production.");

        if (string.IsNullOrWhiteSpace(config["PayOS:ClientId"]) ||
            string.IsNullOrWhiteSpace(config["PayOS:ApiKey"]) ||
            string.IsNullOrWhiteSpace(config["PayOS:ChecksumKey"]))
        {
            throw new InvalidOperationException("Startup Error: 'PayOS:ClientId', 'PayOS:ApiKey', and 'PayOS:ChecksumKey' are required in Production.");
        }

        var frontendBaseUrl = config["Frontend:BaseUrl"];
        if (string.IsNullOrWhiteSpace(frontendBaseUrl) || !Uri.TryCreate(frontendBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Startup Error: 'Frontend:BaseUrl' must be a valid HTTPS absolute URI in Production.");
        }
    }
    else if (env.IsDevelopment())
    {
        bool useMock = config.GetValue<bool>("PayOS:UseMock");
        if (!useMock && (string.IsNullOrWhiteSpace(config["PayOS:ClientId"]) || string.IsNullOrWhiteSpace(config["PayOS:ApiKey"]) || string.IsNullOrWhiteSpace(config["PayOS:ChecksumKey"])))
        {
            throw new InvalidOperationException("Startup Error: 'PayOS:ClientId', 'PayOS:ApiKey', and 'PayOS:ChecksumKey' are required when PayOS:UseMock is false in Development.");
        }
    }
}

public partial class Program { }
