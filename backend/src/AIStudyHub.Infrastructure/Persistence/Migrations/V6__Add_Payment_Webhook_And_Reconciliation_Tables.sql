-- ============================================================================
-- Migration: V6__Add_Payment_Webhook_And_Reconciliation_Tables.sql
-- Description: Creates payment_webhook_events, payment_reconciliation_cases tables,
--              updates transactions table with PayOS and reconciliation fields,
--              and configures unique constraints.
-- ============================================================================

-- 1. Add PayOS & Reconciliation Columns to transactions
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
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UQ_transactions_payos_order_code
    ON transactions (payos_order_code)
    WHERE payos_order_code IS NOT NULL;
END
GO

-- 2. Update Check Constraint on transactions.status to allow CREATING, CREATE_FAILED, EXPIRED
DECLARE @txStatusConstraint SYSNAME;
SELECT TOP 1 @txStatusConstraint = cc.name 
FROM sys.check_constraints cc 
WHERE cc.parent_object_id = OBJECT_ID('transactions') AND cc.definition LIKE '%status%';

IF @txStatusConstraint IS NOT NULL 
    EXEC(N'ALTER TABLE transactions DROP CONSTRAINT [' + @txStatusConstraint + N']');

ALTER TABLE transactions WITH NOCHECK ADD CONSTRAINT CK_transactions_status
    CHECK (status IN ('PENDING', 'SUCCESS', 'FAILED', 'CANCELLED', 'CREATING', 'CREATE_FAILED', 'EXPIRED'));
GO

-- 3. Create payment_webhook_events table
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'payment_webhook_events')
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
END
GO

-- Create Unique Index on (provider, provider_event_id)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_payment_webhook_provider_event')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UQ_payment_webhook_provider_event
    ON payment_webhook_events (provider, provider_event_id);
END
GO

-- 4. Create payment_reconciliation_cases table
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'payment_reconciliation_cases')
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
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_payment_reconciliation_cases_status')
BEGIN
    CREATE NONCLUSTERED INDEX IX_payment_reconciliation_cases_status
    ON payment_reconciliation_cases (status, created_at);
END
GO
