-- ============================================================================
-- Migration: V4__Support_MultiVersion_ExtractedTexts.sql
-- Description: Enable 1-to-N multi-version extracted text storage,
--              backfill existing records with validated current_version_id,
--              dynamically remove old unique constraint on document_id,
--              and create dual filtered unique indexes on document_extracted_text.
-- ============================================================================

-- 1. Ensure Column document_version_id Exists
IF NOT EXISTS (
    SELECT 1 FROM sys.columns 
    WHERE object_id = OBJECT_ID('document_extracted_text') 
      AND name = 'document_version_id'
)
BEGIN
    ALTER TABLE document_extracted_text
    ADD document_version_id INT NULL;
END
GO

-- 2. Backfill Existing Records with Validated CurrentVersionId (Belonging to Same Document)
UPDATE det
SET det.document_version_id = d.current_version_id
FROM document_extracted_text det
INNER JOIN documents d ON det.document_id = d.document_id
INNER JOIN document_versions dv ON d.document_id = dv.document_id AND d.current_version_id = dv.version_id
WHERE det.document_version_id IS NULL 
  AND d.current_version_id IS NOT NULL;
GO

-- 3. Dynamic Drop of Old Unique Constraint on document_id (if exists)
DECLARE @ConstraintName NVARCHAR(200);
SELECT TOP 1 @ConstraintName = kc.name
FROM sys.key_constraints kc
INNER JOIN sys.index_columns ic ON kc.parent_object_id = ic.object_id AND kc.unique_index_id = ic.index_id
INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE kc.parent_object_id = OBJECT_ID('document_extracted_text')
  AND c.name = 'document_id'
  AND kc.type = 'UQ';

IF @ConstraintName IS NOT NULL
BEGIN
    DECLARE @DropConstraintSql NVARCHAR(500) = N'ALTER TABLE document_extracted_text DROP CONSTRAINT ' + QUOTENAME(@ConstraintName);
    EXEC sp_executesql @DropConstraintSql;
END
GO

-- 4. Dynamic Drop of Old Standalone Unique Index on document_id (if exists)
DECLARE @IndexName NVARCHAR(200);
SELECT TOP 1 @IndexName = i.name
FROM sys.indexes i
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE i.object_id = OBJECT_ID('document_extracted_text')
  AND c.name = 'document_id'
  AND i.is_unique = 1
  AND i.is_primary_key = 0
  AND i.has_filter = 0
  AND NOT EXISTS (
      SELECT 1 FROM sys.index_columns ic2 
      WHERE ic2.object_id = i.object_id AND ic2.index_id = i.index_id AND ic2.key_ordinal > 1
  );

IF @IndexName IS NOT NULL
BEGIN
    DECLARE @DropIndexSql NVARCHAR(500) = N'DROP INDEX ' + QUOTENAME(@IndexName) + N' ON document_extracted_text';
    EXEC sp_executesql @DropIndexSql;
END
GO

-- 5. Create Dual Filtered Unique Indexes
-- 5a. Unique per Versioned Document
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE object_id = OBJECT_ID('document_extracted_text') 
      AND name = 'UQ_document_extracted_text_doc_ver'
)
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UQ_document_extracted_text_doc_ver
    ON document_extracted_text(document_id, document_version_id)
    WHERE document_version_id IS NOT NULL;
END
GO

-- 5b. Unique per Legacy Null-Version Document (Max 1 legacy row per doc)
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE object_id = OBJECT_ID('document_extracted_text') 
      AND name = 'UQ_document_extracted_text_doc_legacy'
)
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UQ_document_extracted_text_doc_legacy
    ON document_extracted_text(document_id)
    WHERE document_version_id IS NULL;
END
GO

-- 6. Create General Lookup Index on document_id
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE object_id = OBJECT_ID('document_extracted_text') 
      AND name = 'IX_document_extracted_text_doc_id'
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_document_extracted_text_doc_id
    ON document_extracted_text(document_id);
END
GO

-- 7. Ensure Foreign Key to document_versions with ON DELETE NO ACTION
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys 
    WHERE object_id = OBJECT_ID('FK_document_extracted_text_document_versions')
)
BEGIN
    ALTER TABLE document_extracted_text
    ADD CONSTRAINT FK_document_extracted_text_document_versions 
    FOREIGN KEY (document_version_id) REFERENCES document_versions(version_id) ON DELETE NO ACTION;
END
GO
