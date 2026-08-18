-- Run this script to update the database schema for OCR-related fields in the document_extracted_text table and create the document_ocr_regions table if they do not already exist.
-- This script checks for the existence of the specified columns and table before attempting to create them, ensuring that it can be run multiple times without causing errors.
-- Add new columns to the document_extracted_text table if they do not already exist
IF COL_LENGTH('document_extracted_text', 'total_pages') IS NULL
    ALTER TABLE document_extracted_text ADD total_pages int NOT NULL CONSTRAINT DF_document_extracted_text_total_pages DEFAULT 0;
IF COL_LENGTH('document_extracted_text', 'readable_pages') IS NULL
    ALTER TABLE document_extracted_text ADD readable_pages int NOT NULL CONSTRAINT DF_document_extracted_text_readable_pages DEFAULT 0;
IF COL_LENGTH('document_extracted_text', 'extraction_coverage') IS NULL
    ALTER TABLE document_extracted_text ADD extraction_coverage decimal(5, 4) NOT NULL CONSTRAINT DF_document_extracted_text_extraction_coverage DEFAULT 0;
IF COL_LENGTH('document_extracted_text', 'image_content_detected') IS NULL
    ALTER TABLE document_extracted_text ADD image_content_detected bit NOT NULL CONSTRAINT DF_document_extracted_text_image_content_detected DEFAULT 0;
IF COL_LENGTH('document_extracted_text', 'unread_image_content_warning') IS NULL
    ALTER TABLE document_extracted_text ADD unread_image_content_warning bit NOT NULL CONSTRAINT DF_document_extracted_text_unread_image_content_warning DEFAULT 0;
IF COL_LENGTH('document_extracted_text', 'ocr_region_count') IS NULL
    ALTER TABLE document_extracted_text ADD ocr_region_count int NOT NULL CONSTRAINT DF_document_extracted_text_ocr_region_count DEFAULT 0;
GO

IF OBJECT_ID('document_ocr_regions', 'U') IS NULL
BEGIN
    CREATE TABLE document_ocr_regions
    (
        ocr_region_id bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_document_ocr_regions PRIMARY KEY,
        document_id int NOT NULL,
        page_number int NOT NULL,
        region_type nvarchar(30) NOT NULL CONSTRAINT DF_document_ocr_regions_region_type DEFAULT 'IMAGE',
        bounding_box_left float NOT NULL,
        bounding_box_top float NOT NULL,
        bounding_box_width float NOT NULL,
        bounding_box_height float NOT NULL,
        confidence decimal(5, 4) NULL,
        recognized_text nvarchar(max) NULL,
        source nvarchar(30) NOT NULL CONSTRAINT DF_document_ocr_regions_source DEFAULT 'OCR',
        created_at datetime2 NOT NULL CONSTRAINT DF_document_ocr_regions_created_at DEFAULT getdate(),
        CONSTRAINT FK_document_ocr_regions_documents FOREIGN KEY (document_id) REFERENCES documents(document_id) ON DELETE CASCADE
    );
    CREATE INDEX IX_document_ocr_regions_document_page ON document_ocr_regions(document_id, page_number);
END
GO