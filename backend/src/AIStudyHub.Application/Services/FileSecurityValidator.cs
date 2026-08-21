using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace AIStudyHub.Application.Services;

public static class FileSecurityValidator
{
    private const int MaxZipEntries = 500;
    private const long MaxUncompressedSizeBytes = 100L * 1024 * 1024; // 100 MB
    private const double MaxCompressionRatio = 100.0;

    private static readonly byte[] PdfMagic = { 0x25, 0x50, 0x44, 0x46, 0x2D }; // %PDF-
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] JpegMagic1 = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] ZipMagic = { 0x50, 0x4B, 0x03, 0x04 }; // PK\x03\x04

    public static void ValidateFile(Stream stream, string fileExtension)
    {
        if (stream == null || !stream.CanRead)
            throw new ArgumentException("Luồng tệp không hợp lệ hoặc không thể đọc.");

        long originalPosition = stream.CanSeek ? stream.Position : 0;
        try
        {
            byte[] header = new byte[16];
            int read = stream.Read(header, 0, header.Length);
            if (read < 4)
                throw new ArgumentException("Tệp tin quá nhỏ hoặc rỗng.");

            string ext = fileExtension.Trim().TrimStart('.').ToLowerInvariant();

            switch (ext)
            {
                case "pdf":
                    ValidatePdf(header);
                    break;
                case "docx":
                case "xlsx":
                case "pptx":
                    ValidateZipAndOoxmlStructure(stream, ext, header);
                    break;
                case "png":
                    ValidatePng(header);
                    break;
                case "jpg":
                case "jpeg":
                    ValidateJpeg(header);
                    break;
                case "webp":
                    ValidateWebp(header);
                    break;
                case "bmp":
                    ValidateBmp(header);
                    break;
                case "gif":
                    ValidateGif(header);
                    break;
                case "txt":
                case "md":
                case "svg":
                    ValidatePlainText(stream);
                    break;
                default:
                    throw new ArgumentException($"Định dạng tệp '.{ext}' không được hỗ trợ.");
            }
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = originalPosition;
            }
        }
    }

    private static void ValidatePdf(byte[] header)
    {
        if (header.Length < PdfMagic.Length || !header.Take(PdfMagic.Length).SequenceEqual(PdfMagic))
        {
            throw new ArgumentException("Nội dung tệp PDF không hợp lệ (sai Magic Bytes %PDF-).");
        }
    }

    private static void ValidatePng(byte[] header)
    {
        if (header.Length < PngMagic.Length || !header.Take(PngMagic.Length).SequenceEqual(PngMagic))
        {
            throw new ArgumentException("Nội dung tệp PNG không hợp lệ.");
        }
    }

    private static void ValidateJpeg(byte[] header)
    {
        if (header.Length < JpegMagic1.Length || !header.Take(JpegMagic1.Length).SequenceEqual(JpegMagic1))
        {
            throw new ArgumentException("Nội dung tệp JPEG không hợp lệ.");
        }
    }

    private static void ValidateWebp(byte[] header)
    {
        if (header.Length < 12 || header[0] != 0x52 || header[1] != 0x49 || header[2] != 0x46 || header[3] != 0x46
            || header[8] != 0x57 || header[9] != 0x45 || header[10] != 0x42 || header[11] != 0x50)
        {
            throw new ArgumentException("Nội dung tệp WEBP không hợp lệ.");
        }
    }

    private static void ValidateBmp(byte[] header)
    {
        if (header.Length < 2 || header[0] != 0x42 || header[1] != 0x4D)
        {
            throw new ArgumentException("Nội dung tệp BMP không hợp lệ.");
        }
    }

    private static void ValidateGif(byte[] header)
    {
        if (header.Length < 3 || header[0] != 0x47 || header[1] != 0x49 || header[2] != 0x46)
        {
            throw new ArgumentException("Nội dung tệp GIF không hợp lệ.");
        }
    }

    private static void ValidatePlainText(Stream stream)
    {
        if (stream.CanSeek)
            stream.Position = 0;

        byte[] buffer = new byte[4096];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);
        for (int i = 0; i < bytesRead; i++)
        {
            byte b = buffer[i];
            if (b == 0) // Null byte indicates binary file masquerading as text
            {
                throw new ArgumentException("Tệp văn bản thuần chứa ký tự nhị phân không hợp lệ.");
            }
        }
    }

    private static void ValidateZipAndOoxmlStructure(Stream stream, string ext, byte[] header)
    {
        if (header.Length < ZipMagic.Length || !header.Take(ZipMagic.Length).SequenceEqual(ZipMagic))
        {
            throw new ArgumentException($"Nội dung tệp {ext.ToUpper()} không phải là định dạng nén OpenXML hợp lệ (thiếu PK zip signature).");
        }

        if (stream.CanSeek)
            stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > MaxZipEntries)
        {
            throw new ArgumentException($"Tệp nén vượt quá giới hạn an toàn ({archive.Entries.Count} > {MaxZipEntries} entries).");
        }

        long totalUncompressedSize = 0;
        bool hasContentTypes = false;
        bool hasDocumentXml = false;
        bool hasWorkbookXml = false;
        bool hasPresentationXml = false;

        foreach (var entry in archive.Entries)
        {
            string normalizedName = entry.FullName.Replace('\\', '/');

            // Zip Slip protection
            if (normalizedName.Contains("..") || normalizedName.StartsWith("/"))
            {
                throw new ArgumentException($"Tệp nén chứa đường dẫn nguy hiểm: {entry.FullName}");
            }

            totalUncompressedSize += entry.Length;
            if (totalUncompressedSize > MaxUncompressedSizeBytes)
            {
                throw new ArgumentException($"Tổng dung lượng giải nén vượt quá giới hạn 100MB cho phép.");
            }

            if (entry.CompressedLength > 0)
            {
                double ratio = (double)entry.Length / entry.CompressedLength;
                if (ratio > MaxCompressionRatio)
                {
                    throw new ArgumentException($"Tỉ lệ nén bất thường phát hiện (Zip Bomb attack): {ratio:F1}x.");
                }
            }

            if (string.Equals(normalizedName, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
                hasContentTypes = true;
            if (string.Equals(normalizedName, "word/document.xml", StringComparison.OrdinalIgnoreCase))
                hasDocumentXml = true;
            if (string.Equals(normalizedName, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase))
                hasWorkbookXml = true;
            if (string.Equals(normalizedName, "ppt/presentation.xml", StringComparison.OrdinalIgnoreCase))
                hasPresentationXml = true;
        }

        if (!hasContentTypes)
        {
            throw new ArgumentException($"Tệp {ext.ToUpper()} không có cấu trúc OpenXML hợp lệ (thiếu [Content_Types].xml).");
        }

        if (ext == "docx" && !hasDocumentXml)
        {
            throw new ArgumentException("Tệp DOCX thiếu thành phần chính word/document.xml.");
        }
        else if (ext == "xlsx" && !hasWorkbookXml)
        {
            throw new ArgumentException("Tệp XLSX thiếu thành phần chính xl/workbook.xml.");
        }
        else if (ext == "pptx" && !hasPresentationXml)
        {
            throw new ArgumentException("Tệp PPTX thiếu thành phần chính ppt/presentation.xml.");
        }
    }
}
