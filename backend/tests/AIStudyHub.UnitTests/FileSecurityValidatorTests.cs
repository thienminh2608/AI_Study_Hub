using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using AIStudyHub.Application.Services;
using Xunit;

namespace AIStudyHub.UnitTests;

public class FileSecurityValidatorTests
{
    [Fact]
    public void ValidateFile_ValidPdf_Passes()
    {
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\n%Test PDF Content");
        using var stream = new MemoryStream(pdfBytes);

        FileSecurityValidator.ValidateFile(stream, "pdf");
    }

    [Fact]
    public void ValidateFile_InvalidPdfMagic_ThrowsArgumentException()
    {
        var fakePdfBytes = Encoding.ASCII.GetBytes("This is not a PDF file at all");
        using var stream = new MemoryStream(fakePdfBytes);

        var ex = Assert.Throws<ArgumentException>(() => FileSecurityValidator.ValidateFile(stream, "pdf"));
        Assert.Contains("sai Magic Bytes", ex.Message);
    }

    [Fact]
    public void ValidateFile_ValidDocx_Passes()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var contentTypes = archive.CreateEntry("[Content_Types].xml");
            using (var writer = new StreamWriter(contentTypes.Open()))
            {
                writer.Write("<?xml version=\"1.0\"?><Types></Types>");
            }

            var docXml = archive.CreateEntry("word/document.xml");
            using (var writer = new StreamWriter(docXml.Open()))
            {
                writer.Write("<?xml version=\"1.0\"?><document></document>");
            }
        }

        stream.Position = 0;
        FileSecurityValidator.ValidateFile(stream, "docx");
    }

    [Fact]
    public void ValidateFile_DocxMissingDocumentXml_ThrowsArgumentException()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var contentTypes = archive.CreateEntry("[Content_Types].xml");
            using (var writer = new StreamWriter(contentTypes.Open()))
            {
                writer.Write("<?xml version=\"1.0\"?><Types></Types>");
            }
            // Missing word/document.xml
        }

        stream.Position = 0;
        var ex = Assert.Throws<ArgumentException>(() => FileSecurityValidator.ValidateFile(stream, "docx"));
        Assert.Contains("word/document.xml", ex.Message);
    }

    [Fact]
    public void ValidateFile_ZipSlipPathTraversal_ThrowsArgumentException()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var dangerousEntry = archive.CreateEntry("../../evil.exe");
            using (var writer = new StreamWriter(dangerousEntry.Open()))
            {
                writer.Write("malicious");
            }
        }

        stream.Position = 0;
        var ex = Assert.Throws<ArgumentException>(() => FileSecurityValidator.ValidateFile(stream, "docx"));
        Assert.Contains("đường dẫn nguy hiểm", ex.Message);
    }

    [Fact]
    public void ValidateFile_TextFileWithNullBytes_ThrowsArgumentException()
    {
        var textWithBinary = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x00, 0x57, 0x6F, 0x72, 0x6C, 0x64 };
        using var stream = new MemoryStream(textWithBinary);

        var ex = Assert.Throws<ArgumentException>(() => FileSecurityValidator.ValidateFile(stream, "txt"));
        Assert.Contains("ký tự nhị phân", ex.Message);
    }
}
