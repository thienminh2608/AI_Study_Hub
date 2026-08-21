using System;
using System.Collections.Generic;

namespace AIStudyHub.Application.DTOs;

public class SubjectDto
{
    public int SubjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public int? ParentSubjectId { get; set; }
    public int Depth { get; set; }
    public int SortOrder { get; set; }
    public string Status { get; set; } = "APPROVED";
    public int? RequestedByUserId { get; set; }
    public string? RequestedByUsername { get; set; }
    public int? ApprovedByUserId { get; set; }
    public string? ApprovedByUsername { get; set; }
    public string? RejectionReason { get; set; }
    public int DocumentCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class SubjectTreeDto
{
    public int SubjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public int? ParentSubjectId { get; set; }
    public int Depth { get; set; }
    public int SortOrder { get; set; }
    public string Status { get; set; } = "APPROVED";
    public int DocumentCount { get; set; }
    public List<SubjectTreeDto> Children { get; set; } = new();
}

public class CreateSubjectDto
{
    public string Name { get; set; } = string.Empty;
    public int? ParentSubjectId { get; set; }
    public int SortOrder { get; set; } = 0;
}

public class ResolveSubjectPathDto
{
    public string SubjectName { get; set; } = string.Empty;
    public string? ChildSubjectName { get; set; }
}

public class MoveSubjectDto
{
    public int? NewParentSubjectId { get; set; }
    public int NewSortOrder { get; set; } = 0;
}

public class RejectSubjectDto
{
    public string Reason { get; set; } = string.Empty;
}
