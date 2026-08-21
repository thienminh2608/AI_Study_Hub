using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIStudyHub.Domain.Entities;

[Table("subject_categories")]
public class SubjectCategory
{
    [Key]
    [Column("subject_id")]
    public int SubjectId { get; set; }

    [Required]
    [MaxLength(100)]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [Column("normalized_name")]
    public string NormalizedName { get; set; } = string.Empty;

    [Column("parent_subject_id")]
    public int? ParentSubjectId { get; set; }

    [Column("depth")]
    public int Depth { get; set; } = 0; // 0 (Root) to 3 (Max leaf)

    [Column("sort_order")]
    public int SortOrder { get; set; } = 0;

    [Required]
    [MaxLength(20)]
    [Column("status")]
    public string Status { get; set; } = "APPROVED"; // APPROVED, PENDING, REJECTED

    [Column("requested_by_user_id")]
    public int? RequestedByUserId { get; set; }

    [ForeignKey(nameof(RequestedByUserId))]
    public virtual User? RequestedByUser { get; set; }

    [Column("approved_by_user_id")]
    public int? ApprovedByUserId { get; set; }

    [ForeignKey(nameof(ApprovedByUserId))]
    public virtual User? ApprovedByUser { get; set; }

    [MaxLength(500)]
    [Column("rejection_reason")]
    public string? RejectionReason { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(ParentSubjectId))]
    public virtual SubjectCategory? ParentSubject { get; set; }

    public virtual ICollection<SubjectCategory> ChildSubjects { get; set; } = new List<SubjectCategory>();
}
