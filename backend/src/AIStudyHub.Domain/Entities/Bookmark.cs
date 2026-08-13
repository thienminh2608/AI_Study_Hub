using System;
using System.Collections.Generic;

namespace AIStudyHub.Domain.Entities;

public partial class Bookmark
{
    public int BookmarkId
    {
        get; set;
    }

    public int UserId
    {
        get; set;
    }

    public int DocumentId
    {
        get; set;
    }

    public DateTime? CreatedAt
    {
        get; set;
    }

    public virtual Document Document { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
