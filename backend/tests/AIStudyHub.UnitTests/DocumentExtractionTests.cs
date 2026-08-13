using System.IO.Compression;
using System.Reflection;
using System.Text;
using AIStudyHub.Application.Services;

namespace AIStudyHub.UnitTests;

public class DocumentExtractionTests
{
    [Fact]
    public void ExtractTextFromPptx_ReadsTextFromSlides()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aistudyhub-{Guid.NewGuid():N}.pptx");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var slide = archive.CreateEntry("ppt/slides/slide1.xml");
                using var writer = new StreamWriter(slide.Open(), Encoding.UTF8);
                writer.Write("<?xml version=\"1.0\"?><p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><p:cSld><a:t>Noi dung PPTX AIStudyHub</a:t></p:cSld></p:sld>");
            }

            Assert.Contains("Noi dung PPTX AIStudyHub", Extract(path, "pptx"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ExtractTextFromPdf_ReadsPageText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aistudyhub-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(path, BuildSimplePdf("PDF AIStudyHub works"));
            Assert.Contains("PDF AIStudyHub works", Extract(path, "pdf"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static string Extract(string path, string extension)
    {
        var service = new DocumentService(null!, null!, null!);
        var method = typeof(DocumentService).GetMethod("ExtractTextFromFile", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (string)method.Invoke(service, [path, extension])!;
    }

    private static byte[] BuildSimplePdf(string text)
    {
        var escaped = text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {escaped.Length + 33} >>\nstream\nBT /F1 18 Tf 72 720 Td ({escaped}) Tj ET\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            builder.Append(offset.ToString("D10")).Append(" 00000 n \n");
        builder.Append("trailer << /Size 6 /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
