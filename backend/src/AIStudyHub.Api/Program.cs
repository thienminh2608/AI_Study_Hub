using System.Text;
using AIStudyHub.Api.Middlewares;
using AIStudyHub.Application;
using AIStudyHub.Infrastructure;
using AIStudyHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure Database Connection -> Registered via Infrastructure layer

// 2. Configure JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be configured and contain at least 32 characters.");
var key = Encoding.UTF8.GetBytes(jwtKey);
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
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
    options.MultipartBodyLengthLimit = 50L * 1024 * 1024);

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

using (var scope = app.Services.CreateScope())
{
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
                created_by_user_id INT NOT NULL,
                created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
                CONSTRAINT UQ_document_versions UNIQUE(document_id, version_number),
                CONSTRAINT FK_document_versions_document FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE CASCADE,
                CONSTRAINT FK_document_versions_user FOREIGN KEY(created_by_user_id) REFERENCES users(user_id)
            );
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
        IF COL_LENGTH('documents', 'view_count') IS NULL
            ALTER TABLE documents ADD view_count INT NOT NULL CONSTRAINT DF_documents_view_count DEFAULT 0;
        IF COL_LENGTH('chat_sessions', 'attached_document_id') IS NULL
            ALTER TABLE chat_sessions ADD attached_document_id INT NULL;
        IF COL_LENGTH('documents', 'subject') IS NULL
            ALTER TABLE documents ADD subject NVARCHAR(100) NOT NULL CONSTRAINT DF_documents_subject DEFAULT N'Khác';
        IF COL_LENGTH('documents', 'requested_visibility') IS NULL ALTER TABLE documents ADD requested_visibility VARCHAR(20) NOT NULL CONSTRAINT DF_documents_requested_visibility DEFAULT 'PRIVATE';
        DECLARE @roleConstraint SYSNAME;
        SELECT TOP 1 @roleConstraint = cc.name FROM sys.check_constraints cc WHERE cc.parent_object_id = OBJECT_ID('users') AND cc.definition LIKE '%role%';
        IF @roleConstraint IS NOT NULL EXEC(N'ALTER TABLE users DROP CONSTRAINT [' + @roleConstraint + N']');
        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('users') AND name = 'CK_users_role')
            ALTER TABLE users WITH NOCHECK ADD CONSTRAINT CK_users_role CHECK (role IN ('STUDENT','MODERATOR','ADMIN'));
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
                heading_path NVARCHAR(1000) NULL,
                page_number INT NULL,
                text NVARCHAR(MAX) NOT NULL,
                start_offset INT NOT NULL,
                end_offset INT NOT NULL,
                created_at DATETIME2 NOT NULL CONSTRAINT DF_document_chunks_created_at DEFAULT GETDATE(),
                CONSTRAINT FK_document_chunks_document FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE CASCADE,
                CONSTRAINT UQ_document_chunks_document_index UNIQUE(document_id, chunk_index)
            );
            CREATE INDEX IX_document_chunks_document ON document_chunks(document_id);
        END
        IF OBJECT_ID('document_chunks', 'U') IS NOT NULL ALTER TABLE document_chunks ALTER COLUMN heading_path NVARCHAR(MAX) NULL;
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
        """);

    var missingChunks = await db.DocumentExtractedTexts.AsNoTracking()
        .Where(extracted => !db.DocumentChunks.Any(chunk => chunk.DocumentId == extracted.DocumentId))
        .Select(extracted => new { extracted.DocumentId, extracted.ExtractedText, extracted.CreatedAt })
        .ToListAsync();
    foreach (var extracted in missingChunks)
    {
        db.DocumentChunks.AddRange(AIStudyHub.Application.Services.DocumentChunker.Chunk(
            extracted.DocumentId, extracted.ExtractedText, extracted.CreatedAt ?? DateTime.UtcNow));
    }
    if (missingChunks.Count > 0)
        await db.SaveChangesAsync();

    if (app.Environment.IsDevelopment())
    {
        // 1. Seed Moderator
        const string moderatorEmail = "moderator@aistudyhub.local";
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
        }

        // 2. Seed Admin
        const string adminEmail = "admin@aistudyhub.local";
        var admin = await db.Users.FirstOrDefaultAsync(user => user.Email == adminEmail);
        if (admin == null)
        {
            db.Users.Add(new AIStudyHub.Domain.Entities.User
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
            });
        }

        // 3. Seed Student
        const string studentEmail = "student@aistudyhub.local";
        var student = await db.Users.FirstOrDefaultAsync(user => user.Email == studentEmail);
        if (student == null)
        {
            db.Users.Add(new AIStudyHub.Domain.Entities.User
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
            });
        }

        await db.SaveChangesAsync();
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
