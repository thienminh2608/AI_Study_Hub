using System;
using System.Collections.Generic;

namespace AIStudyHub.Domain.Entities;

public partial class Folder
{
    public int FolderId { get; set; }
    public int UserId { get; set; }
    public int? ParentFolderId { get; set; }
    public string FolderName { get; set; } = null!;
    public string? SharingPermission { get; set; }

    public string GeneralAccess { get; set; } = "RESTRICTED"; // RESTRICTED, LINK, PUBLIC
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
    public virtual ICollection<Folder> InverseParentFolder { get; set; } = new List<Folder>();
    public virtual ICollection<FolderShare> FolderShares { get; set; } = new List<FolderShare>();
    public virtual Folder? ParentFolder { get; set; }
    public virtual User User { get; set; } = null!;
}
