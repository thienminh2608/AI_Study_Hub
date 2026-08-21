-- ============================================================================
-- Migration: V3__Add_DocumentVersionId_To_DocumentChunks.sql
-- Description: Self-healing idempotent migration for document_version_id
--              column, foreign key constraint, and index on document_chunks.
-- ============================================================================

-- 1. Ensure Column Exists
IF NOT EXISTS (
    SELECT 1 FROM sys.columns 
    WHERE object_id = OBJECT_ID('document_chunks') 
      AND name = 'document_version_id'
)
BEGIN
    ALTER TABLE document_chunks
    ADD document_version_id INT NULL;
END
GO

-- 2. Ensure Foreign Key Exists
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys 
    WHERE object_id = OBJECT_ID('FK_document_chunks_document_versions')
)
BEGIN
    ALTER TABLE document_chunks
    ADD CONSTRAINT FK_document_chunks_document_versions FOREIGN KEY (document_version_id)
        REFERENCES document_versions(version_id) ON DELETE SET NULL;
END
GO

-- 3. Ensure Index Exists
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE object_id = OBJECT_ID('document_chunks') 
      AND name = 'IX_document_chunks_document_version_id'
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_document_chunks_document_version_id
        ON document_chunks(document_version_id);
END
GO
