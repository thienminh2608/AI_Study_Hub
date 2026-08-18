using System;

namespace AIStudyHub.Application.DTOs;

public class TrashItemDto
{
    public int ItemId { get; set; }
    public string ItemType { get; set; } = null!; // DOCUMENT, FOLDER
    public string Name { get; set; } = null!;
    public string? FileExtension { get; set; }
    public decimal? FileSizeMb { get; set; }
    public DateTime DeletedAt { get; set; }
    public int DeletedByUserId { get; set; }
    public string DeletedByName { get; set; } = null!;
}
