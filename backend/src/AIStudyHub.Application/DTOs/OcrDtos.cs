using System.Collections.Generic;

namespace AIStudyHub.Application.DTOs;

public class OcrRegionDto
{
    public int PageNumber { get; set; }
    public string RegionType { get; set; } = "IMAGE";
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public decimal? Confidence { get; set; }
    public string? Text { get; set; }
}

public class OcrResultDto
{
    public bool IsConfigured { get; set; }
    public List<OcrRegionDto> Regions { get; set; } = [];
}