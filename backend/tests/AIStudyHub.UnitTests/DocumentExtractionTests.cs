using System.IO.Compression;
using System.Reflection;
using System.Text;
using AIStudyHub.Application.Services;
using OfficeOpenXml;

namespace AIStudyHub.UnitTests;

public class DocumentExtractionTests
{
    [Fact]
    public void ExtractTextFromXlsx_ReadsFormattedTextAcrossWorksheets()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aistudyhub-{Guid.NewGuid():N}.xlsx");
        try
        {
            ExcelPackage.License.SetNonCommercialPersonal("AIStudyHub Tests");
            using (var package = new ExcelPackage())
            {
                var overview = package.Workbook.Worksheets.Add("Tong quan");
                overview.Cells[1, 1].Value = "Nội dung tiếng Việt";
                overview.Cells[2, 1].Value = 0.25;
                overview.Cells[2, 1].Style.Numberformat.Format = "0%";
                var details = package.Workbook.Worksheets.Add("Chi tiet");
                details.Cells[1, 1].Value = "Dữ liệu sheet thứ hai";
                package.SaveAs(new FileInfo(path));
            }

            var extracted = Extract(path, "xlsx");

            Assert.Contains("--- Worksheet: Tong quan ---", extracted);
            Assert.Contains("Nội dung tiếng Việt", extracted);
            Assert.Contains("25%", extracted);
            Assert.Contains("--- Worksheet: Chi tiet ---", extracted);
            Assert.Contains("Dữ liệu sheet thứ hai", extracted);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

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
        var service = new DocumentService(null!, null!, null!, null!, new AIStudyHub.Infrastructure.Services.Gemini.MockGeminiService(), null!);
        var method = typeof(DocumentService).GetMethod("ExtractTextFromFileAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task)method.Invoke(service, [path, extension])!;
        task.GetAwaiter().GetResult();
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var textProperty = result.GetType().GetProperty("Text")!;
        return (string)textProperty.GetValue(result)!;
    }

    [Fact]
    public void EncodeBgr24Bmp_ProducesBottomUpBitmapWithCorrectHeaderAndPixels()
    {
        // 2x2 BGRA source (top-down, row-major): row0 = [Blue, Green], row1 = [Red, White]
        byte[] bgra =
        [
            255, 0, 0, 255, // (0,0) Blue: B,G,R,A
            0, 255, 0, 255, // (1,0) Green
            0, 0, 255, 255, // (0,1) Red
            255, 255, 255, 255, // (1,1) White
        ];

        var method = typeof(DocumentService).GetMethod("EncodeBgr24Bmp", BindingFlags.Static | BindingFlags.NonPublic)!;
        var bmp = (byte[])method.Invoke(null, [bgra, 2, 2])!;

        Assert.Equal((byte)'B', bmp[0]);
        Assert.Equal((byte)'M', bmp[1]);
        Assert.Equal(70, BitConverter.ToInt32(bmp, 2)); // file size: 54 header + 2 rows * 8 bytes
        Assert.Equal(54, BitConverter.ToInt32(bmp, 10)); // pixel data offset
        Assert.Equal(40, BitConverter.ToInt32(bmp, 14)); // DIB header size
        Assert.Equal(2, BitConverter.ToInt32(bmp, 18)); // width
        Assert.Equal(2, BitConverter.ToInt32(bmp, 22)); // height
        Assert.Equal(24, BitConverter.ToInt16(bmp, 28)); // bit depth
        Assert.Equal(16, BitConverter.ToInt32(bmp, 34)); // pixel data size

        // Bottom-up storage: first pixel row in the file is the source's bottom row (Red, White).
        Assert.Equal([0, 0, 255], bmp[54..57]); // Red in BGR
        Assert.Equal([255, 255, 255], bmp[57..60]); // White in BGR

        // Second pixel row in the file is the source's top row (Blue, Green).
        Assert.Equal([255, 0, 0], bmp[62..65]); // Blue in BGR
        Assert.Equal([0, 255, 0], bmp[65..68]); // Green in BGR
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
