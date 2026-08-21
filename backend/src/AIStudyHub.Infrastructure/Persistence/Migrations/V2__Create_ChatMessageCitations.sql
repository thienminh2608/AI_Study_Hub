-- ============================================================================
-- Migration: V2__Create_ChatMessageCitations.sql
-- Description: Create chat_message_citations table with snapshot fields,
--              referential integrity constraints, and filtered unique index.
-- ============================================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'chat_message_citations')
BEGIN
    CREATE TABLE chat_message_citations (
        citation_id BIGINT IDENTITY(1,1) NOT NULL,
        message_id INT NOT NULL,
        document_id INT NOT NULL,
        document_version_id INT NOT NULL,
        chunk_id INT NULL,
        document_title_snapshot NVARCHAR(255) NOT NULL,
        version_number_snapshot INT NOT NULL,
        file_extension_snapshot NVARCHAR(20) NOT NULL,
        page_number_snapshot INT NULL,
        start_offset_snapshot INT NULL,
        end_offset_snapshot INT NULL,
        heading_path_snapshot NVARCHAR(500) NULL,
        snippet NVARCHAR(2000) NOT NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),

        CONSTRAINT PK_chat_message_citations PRIMARY KEY CLUSTERED (citation_id),
        CONSTRAINT FK_chat_message_citations_chat_messages FOREIGN KEY (message_id)
            REFERENCES chat_messages(message_id) ON DELETE CASCADE,
        CONSTRAINT FK_chat_message_citations_documents FOREIGN KEY (document_id)
            REFERENCES documents(document_id) ON DELETE NO ACTION,
        CONSTRAINT FK_chat_message_citations_document_versions FOREIGN KEY (document_version_id)
            REFERENCES document_versions(version_id) ON DELETE NO ACTION,
        CONSTRAINT FK_chat_message_citations_document_chunks FOREIGN KEY (chunk_id)
            REFERENCES document_chunks(chunk_id) ON DELETE SET NULL
    );

    CREATE NONCLUSTERED INDEX IX_chat_message_citations_message_id
        ON chat_message_citations(message_id);

    CREATE NONCLUSTERED INDEX IX_chat_message_citations_document_id
        ON chat_message_citations(document_id);

    CREATE NONCLUSTERED INDEX IX_chat_message_citations_document_version_id
        ON chat_message_citations(document_version_id);

    CREATE UNIQUE NONCLUSTERED INDEX UQ_chat_message_citations_message_chunk
        ON chat_message_citations(message_id, chunk_id)
        WHERE chunk_id IS NOT NULL;
END
GO
