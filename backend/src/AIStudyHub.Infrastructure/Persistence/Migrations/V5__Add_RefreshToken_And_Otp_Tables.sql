-- ============================================================================
-- Migration: V5__Add_RefreshToken_And_Otp_Tables.sql
-- Description: Creates refresh_token_sessions, auth_otp_challenges, and
--              password_reset_grants tables with unique indices and constraints.
-- ============================================================================

-- 1. Create refresh_token_sessions table
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'refresh_token_sessions')
BEGIN
    CREATE TABLE refresh_token_sessions (
        session_id BIGINT IDENTITY(1,1) PRIMARY KEY,
        user_id INT NOT NULL,
        token_family_id UNIQUEIDENTIFIER NOT NULL,
        parent_session_id BIGINT NULL,
        token_hash NVARCHAR(128) NOT NULL,
        expires_at DATETIME2 NOT NULL,
        created_at DATETIME2 NOT NULL,
        created_by_ip NVARCHAR(45) NULL,
        user_agent NVARCHAR(500) NULL,
        revoked_at DATETIME2 NULL,
        revoked_reason NVARCHAR(100) NULL,
        revoked_by_ip NVARCHAR(45) NULL,
        replaced_by_token_hash NVARCHAR(128) NULL,
        is_used BIT NOT NULL DEFAULT 0,
        last_used_at DATETIME2 NULL,
        row_version ROWVERSION NOT NULL,
        CONSTRAINT FK_refresh_token_sessions_users FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE
    );
END
GO

-- Create Unique Index on token_hash
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_refresh_token_sessions_token_hash')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UQ_refresh_token_sessions_token_hash
    ON refresh_token_sessions (token_hash);
END
GO

-- Create Index on user_id and token_family_id
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_refresh_token_sessions_user_family')
BEGIN
    CREATE NONCLUSTERED INDEX IX_refresh_token_sessions_user_family
    ON refresh_token_sessions (user_id, token_family_id);
END
GO

-- 2. Create auth_otp_challenges table
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'auth_otp_challenges')
BEGIN
    CREATE TABLE auth_otp_challenges (
        challenge_id UNIQUEIDENTIFIER PRIMARY KEY,
        normalized_email_hash NVARCHAR(128) NOT NULL,
        purpose NVARCHAR(50) NOT NULL DEFAULT 'PASSWORD_RESET',
        otp_hash NVARCHAR(128) NOT NULL,
        attempts INT NOT NULL DEFAULT 0,
        max_attempts INT NOT NULL DEFAULT 5,
        cooldown_until DATETIME2 NOT NULL,
        expires_at DATETIME2 NOT NULL,
        consumed_at DATETIME2 NULL,
        created_at DATETIME2 NOT NULL,
        row_version ROWVERSION NOT NULL
    );
END
GO

-- Create Index on normalized_email_hash and purpose
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_auth_otp_challenges_email_purpose')
BEGIN
    CREATE NONCLUSTERED INDEX IX_auth_otp_challenges_email_purpose
    ON auth_otp_challenges (normalized_email_hash, purpose);
END
GO

-- 3. Create password_reset_grants table
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'password_reset_grants')
BEGIN
    CREATE TABLE password_reset_grants (
        grant_id BIGINT IDENTITY(1,1) PRIMARY KEY,
        user_id INT NOT NULL,
        challenge_id UNIQUEIDENTIFIER NOT NULL,
        grant_hash NVARCHAR(128) NOT NULL,
        expires_at DATETIME2 NOT NULL,
        is_consumed BIT NOT NULL DEFAULT 0,
        consumed_at DATETIME2 NULL,
        created_at DATETIME2 NOT NULL,
        row_version ROWVERSION NOT NULL,
        CONSTRAINT FK_password_reset_grants_users FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE
    );
END
GO

-- Create Unique Index on grant_hash
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_password_reset_grants_hash')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UQ_password_reset_grants_hash
    ON password_reset_grants (grant_hash);
END
GO
