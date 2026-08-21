-- Adds the self-reference used by the subject taxonomy and seeds a small,
-- idempotent starter tree. Subject categories are labels only; they do not
-- create folders or change documents.folder_id.

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

DECLARE @mathId INT = (SELECT TOP 1 subject_id FROM subject_categories WHERE normalized_name = N'toan hoc' AND parent_subject_id IS NULL);
DECLARE @physicsId INT = (SELECT TOP 1 subject_id FROM subject_categories WHERE normalized_name = N'vat ly' AND parent_subject_id IS NULL);
DECLARE @computingId INT = (SELECT TOP 1 subject_id FROM subject_categories WHERE normalized_name = N'tin hoc' AND parent_subject_id IS NULL);

IF @mathId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM subject_categories WHERE parent_subject_id = @mathId AND normalized_name = N'toan hinh')
    INSERT INTO subject_categories (name, normalized_name, parent_subject_id, depth, sort_order, status, created_at)
    VALUES (N'Toán hình', N'toan hinh', @mathId, 1, 10, 'APPROVED', GETDATE());
IF @mathId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM subject_categories WHERE parent_subject_id = @mathId AND normalized_name = N'toan so')
    INSERT INTO subject_categories (name, normalized_name, parent_subject_id, depth, sort_order, status, created_at)
    VALUES (N'Toán số', N'toan so', @mathId, 1, 20, 'APPROVED', GETDATE());

IF @physicsId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM subject_categories WHERE parent_subject_id = @physicsId AND normalized_name = N'co hoc')
    INSERT INTO subject_categories (name, normalized_name, parent_subject_id, depth, sort_order, status, created_at)
    VALUES (N'Cơ học', N'co hoc', @physicsId, 1, 10, 'APPROVED', GETDATE());
IF @physicsId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM subject_categories WHERE parent_subject_id = @physicsId AND normalized_name = N'dien hoc')
    INSERT INTO subject_categories (name, normalized_name, parent_subject_id, depth, sort_order, status, created_at)
    VALUES (N'Điện học', N'dien hoc', @physicsId, 1, 20, 'APPROVED', GETDATE());
IF @physicsId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM subject_categories WHERE parent_subject_id = @physicsId AND normalized_name = N'quang hoc')
    INSERT INTO subject_categories (name, normalized_name, parent_subject_id, depth, sort_order, status, created_at)
    VALUES (N'Quang học', N'quang hoc', @physicsId, 1, 30, 'APPROVED', GETDATE());

IF @computingId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM subject_categories WHERE parent_subject_id = @computingId AND normalized_name = N'lap trinh')
    INSERT INTO subject_categories (name, normalized_name, parent_subject_id, depth, sort_order, status, created_at)
    VALUES (N'Lập trình', N'lap trinh', @computingId, 1, 10, 'APPROVED', GETDATE());
IF @computingId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM subject_categories WHERE parent_subject_id = @computingId AND normalized_name = N'co so du lieu')
    INSERT INTO subject_categories (name, normalized_name, parent_subject_id, depth, sort_order, status, created_at)
    VALUES (N'Cơ sở dữ liệu', N'co so du lieu', @computingId, 1, 20, 'APPROVED', GETDATE());
IF @computingId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM subject_categories WHERE parent_subject_id = @computingId AND normalized_name = N'tri tue nhan tao')
    INSERT INTO subject_categories (name, normalized_name, parent_subject_id, depth, sort_order, status, created_at)
    VALUES (N'Trí tuệ nhân tạo', N'tri tue nhan tao', @computingId, 1, 30, 'APPROVED', GETDATE());
