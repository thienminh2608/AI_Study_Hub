using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIStudyHub.Application.Services;

public class SubjectService : ISubjectService
{
    private readonly IStudyHubDbContext _db;
    private readonly IPermissionService _permissionService;

    public SubjectService(IStudyHubDbContext db, IPermissionService permissionService)
    {
        _db = db;
        _permissionService = permissionService;
    }

    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string normalizedString = text.Trim().Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (char c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    public async Task<List<SubjectDto>> GetApprovedSubjectsAsync(CancellationToken cancellationToken = default)
    {
        return await _db.SubjectCategories
            .AsNoTracking()
            .Where(s => s.Status == "APPROVED")
            .OrderBy(s => s.Depth)
            .ThenBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .Select(s => new SubjectDto
            {
                SubjectId = s.SubjectId,
                Name = s.Name,
                NormalizedName = s.NormalizedName,
                ParentSubjectId = s.ParentSubjectId,
                Depth = s.Depth,
                SortOrder = s.SortOrder,
                Status = s.Status,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<SubjectTreeDto>> GetSubjectTreeAsync(string? status = "APPROVED", CancellationToken cancellationToken = default)
    {
        var query = _db.SubjectCategories.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
        {
            query = query.Where(s => s.Status == status);
        }

        var allSubjects = await query
            .OrderBy(s => s.Depth)
            .ThenBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);

        // Preload document counts by subject name
        var docCounts = await _db.Documents.AsNoTracking()
            .Where(d => d.IsDeleted != true)
            .GroupBy(d => d.Subject)
            .Select(g => new { SubjectName = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.SubjectName, g => g.Count, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var nodeLookup = allSubjects.ToDictionary(
            s => s.SubjectId,
            s => new SubjectTreeDto
            {
                SubjectId = s.SubjectId,
                Name = s.Name,
                NormalizedName = s.NormalizedName,
                ParentSubjectId = s.ParentSubjectId,
                Depth = s.Depth,
                SortOrder = s.SortOrder,
                Status = s.Status,
                DocumentCount = docCounts.TryGetValue(s.Name, out int cnt) ? cnt : 0,
                Children = new List<SubjectTreeDto>()
            });

        var rootNodes = new List<SubjectTreeDto>();

        foreach (var s in allSubjects)
        {
            if (s.ParentSubjectId.HasValue && nodeLookup.TryGetValue(s.ParentSubjectId.Value, out var parentNode))
            {
                parentNode.Children.Add(nodeLookup[s.SubjectId]);
            }
            else
            {
                rootNodes.Add(nodeLookup[s.SubjectId]);
            }
        }

        return rootNodes;
    }

    public async Task<List<SubjectDto>> GetSubjectsForModeratorAsync(string? status, string? search, CancellationToken cancellationToken = default)
    {
        var query = _db.SubjectCategories
            .AsNoTracking()
            .Include(s => s.RequestedByUser)
            .Include(s => s.ApprovedByUser)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
        {
            query = query.Where(s => s.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string keyword = Normalize(search);
            query = query.Where(s => s.NormalizedName.Contains(keyword) || s.Name.Contains(search));
        }

        return await query
            .OrderByDescending(s => s.Status == "PENDING")
            .ThenBy(s => s.Depth)
            .ThenBy(s => s.SortOrder)
            .ThenByDescending(s => s.CreatedAt)
            .Select(s => new SubjectDto
            {
                SubjectId = s.SubjectId,
                Name = s.Name,
                NormalizedName = s.NormalizedName,
                ParentSubjectId = s.ParentSubjectId,
                Depth = s.Depth,
                SortOrder = s.SortOrder,
                Status = s.Status,
                RequestedByUserId = s.RequestedByUserId,
                RequestedByUsername = s.RequestedByUser != null ? s.RequestedByUser.Username : null,
                ApprovedByUserId = s.ApprovedByUserId,
                ApprovedByUsername = s.ApprovedByUser != null ? s.ApprovedByUser.Username : null,
                RejectionReason = s.RejectionReason,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<int>> GetDescendantSubjectIdsAsync(int subjectId, CancellationToken cancellationToken = default)
    {
        var allSubjects = await _db.SubjectCategories.AsNoTracking().ToListAsync(cancellationToken);
        var result = new HashSet<int> { subjectId };
        var queue = new Queue<int>();
        queue.Enqueue(subjectId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            var children = allSubjects.Where(s => s.ParentSubjectId == currentId);
            foreach (var child in children)
            {
                if (result.Add(child.SubjectId))
                {
                    queue.Enqueue(child.SubjectId);
                }
            }
        }

        return result.ToList();
    }

    public async Task<List<string>> GetDescendantSubjectNamesAsync(string subjectName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subjectName)) return new List<string>();

        string normalized = Normalize(subjectName);
        var root = await _db.SubjectCategories.AsNoTracking()
            .FirstOrDefaultAsync(s => s.NormalizedName == normalized || s.Name == subjectName, cancellationToken);

        if (root == null)
        {
            return new List<string> { subjectName };
        }

        var descendantIds = await GetDescendantSubjectIdsAsync(root.SubjectId, cancellationToken);
        var names = await _db.SubjectCategories.AsNoTracking()
            .Where(s => descendantIds.Contains(s.SubjectId))
            .Select(s => s.Name)
            .ToListAsync(cancellationToken);

        if (!names.Contains(subjectName, StringComparer.OrdinalIgnoreCase))
        {
            names.Add(subjectName);
        }

        return names;
    }

    public async Task<string> CreateOrResolveSubjectAsync(string subjectName, int userId, int? parentSubjectId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subjectName))
            return "Khác";

        string cleanName = subjectName.Trim();
        string normalized = Normalize(cleanName);

        var existing = await _db.SubjectCategories
            .FirstOrDefaultAsync(s => s.NormalizedName == normalized && s.ParentSubjectId == parentSubjectId, cancellationToken);

        if (existing != null)
        {
            return existing.Name;
        }

        bool isMod = await _db.Users.AnyAsync(u => u.UserId == userId && (u.Role == "MODERATOR" || u.Role == "ADMIN"), cancellationToken);
        string status = isMod ? "APPROVED" : "PENDING";

        int depth = 0;
        if (parentSubjectId.HasValue)
        {
            var parent = await _db.SubjectCategories.FindAsync(new object[] { parentSubjectId.Value }, cancellationToken);
            if (parent != null)
            {
                depth = Math.Min(3, parent.Depth + 1);
            }
        }

        var newSubject = new SubjectCategory
        {
            Name = cleanName,
            NormalizedName = normalized,
            ParentSubjectId = parentSubjectId,
            Depth = depth,
            Status = status,
            RequestedByUserId = isMod ? null : userId,
            ApprovedByUserId = isMod ? userId : null,
            CreatedAt = DateTime.UtcNow
        };

        _db.SubjectCategories.Add(newSubject);
        await _db.SaveChangesAsync(cancellationToken);

        return cleanName;
    }

    public async Task<string> CreateOrResolveSubjectPathAsync(string subjectName, string? childSubjectName, int userId, CancellationToken cancellationToken = default)
    {
        var resolvedRootName = await CreateOrResolveSubjectAsync(subjectName, userId, null, cancellationToken);
        if (string.IsNullOrWhiteSpace(childSubjectName))
            return resolvedRootName;

        var normalizedRoot = Normalize(resolvedRootName);
        var root = await _db.SubjectCategories
            .FirstAsync(s => s.NormalizedName == normalizedRoot && s.ParentSubjectId == null, cancellationToken);
        return await CreateOrResolveSubjectAsync(childSubjectName, userId, root.SubjectId, cancellationToken);
    }

    public async Task<SubjectDto> CreateSubjectAsync(string subjectName, int userId, int? parentSubjectId = null, int sortOrder = 0, bool autoApprove = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subjectName))
            throw new ArgumentException("Tên môn học không được để trống.");

        string cleanName = subjectName.Trim();
        string normalized = Normalize(cleanName);

        var existing = await _db.SubjectCategories
            .FirstOrDefaultAsync(s => s.NormalizedName == normalized && s.ParentSubjectId == parentSubjectId && s.Status != "REJECTED", cancellationToken);

        if (existing != null)
        {
            throw new InvalidOperationException($"Môn học '{cleanName}' đã tồn tại trong danh mục.");
        }

        int depth = 0;
        if (parentSubjectId.HasValue)
        {
            var parent = await _db.SubjectCategories.FindAsync(new object[] { parentSubjectId.Value }, cancellationToken);
            if (parent == null)
            {
                throw new KeyNotFoundException("Không tìm thấy môn học cha.");
            }
            if (parent.Depth >= 3)
            {
                throw new InvalidOperationException("Hệ thống chỉ hỗ trợ cây danh mục tối đa 4 cấp (Depth 0 đến 3).");
            }
            depth = parent.Depth + 1;
        }

        bool isMod = autoApprove || await _db.Users.AnyAsync(u => u.UserId == userId && (u.Role == "MODERATOR" || u.Role == "ADMIN"), cancellationToken);
        string status = isMod ? "APPROVED" : "PENDING";

        var newSubject = new SubjectCategory
        {
            Name = cleanName,
            NormalizedName = normalized,
            ParentSubjectId = parentSubjectId,
            Depth = depth,
            SortOrder = sortOrder,
            Status = status,
            RequestedByUserId = isMod ? null : userId,
            ApprovedByUserId = isMod ? userId : null,
            CreatedAt = DateTime.UtcNow
        };

        _db.SubjectCategories.Add(newSubject);
        await _db.SaveChangesAsync(cancellationToken);

        return new SubjectDto
        {
            SubjectId = newSubject.SubjectId,
            Name = newSubject.Name,
            NormalizedName = newSubject.NormalizedName,
            ParentSubjectId = newSubject.ParentSubjectId,
            Depth = newSubject.Depth,
            SortOrder = newSubject.SortOrder,
            Status = newSubject.Status,
            CreatedAt = newSubject.CreatedAt
        };
    }

    public async Task<SubjectDto> ApproveSubjectAsync(int subjectId, int moderatorId, CancellationToken cancellationToken = default)
    {
        var subject = await _db.SubjectCategories.FindAsync(new object[] { subjectId }, cancellationToken);
        if (subject == null)
            throw new KeyNotFoundException("Không tìm thấy môn học.");

        subject.Status = "APPROVED";
        subject.ApprovedByUserId = moderatorId;
        subject.RejectionReason = null;
        subject.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return new SubjectDto
        {
            SubjectId = subject.SubjectId,
            Name = subject.Name,
            NormalizedName = subject.NormalizedName,
            ParentSubjectId = subject.ParentSubjectId,
            Depth = subject.Depth,
            SortOrder = subject.SortOrder,
            Status = subject.Status,
            ApprovedByUserId = moderatorId,
            UpdatedAt = subject.UpdatedAt
        };
    }

    public async Task<SubjectDto> RejectSubjectAsync(int subjectId, string reason, int moderatorId, CancellationToken cancellationToken = default)
    {
        var subject = await _db.SubjectCategories.FindAsync(new object[] { subjectId }, cancellationToken);
        if (subject == null)
            throw new KeyNotFoundException("Không tìm thấy môn học.");

        subject.Status = "REJECTED";
        subject.ApprovedByUserId = moderatorId;
        subject.RejectionReason = reason;
        subject.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return new SubjectDto
        {
            SubjectId = subject.SubjectId,
            Name = subject.Name,
            NormalizedName = subject.NormalizedName,
            ParentSubjectId = subject.ParentSubjectId,
            Depth = subject.Depth,
            SortOrder = subject.SortOrder,
            Status = subject.Status,
            RejectionReason = reason,
            UpdatedAt = subject.UpdatedAt
        };
    }

    public async Task<bool> MoveSubjectSubtreeAsync(int subjectId, int? newParentSubjectId, int newSortOrder, CancellationToken cancellationToken = default)
    {
        using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var target = await _db.SubjectCategories.FindAsync(new object[] { subjectId }, cancellationToken);
            if (target == null) return false;

            int newDepth = 0;
            if (newParentSubjectId.HasValue)
            {
                if (newParentSubjectId.Value == subjectId)
                {
                    throw new InvalidOperationException("Không thể đặt môn học làm cha của chính nó.");
                }

                // Detect cycles: check if newParent is inside descendant tree of subjectId
                var descendantIds = await GetDescendantSubjectIdsAsync(subjectId, cancellationToken);
                if (descendantIds.Contains(newParentSubjectId.Value))
                {
                    throw new InvalidOperationException("Phát hiện chu trình: Môn học cha mới không thể là con cháu của môn học hiện tại.");
                }

                var parent = await _db.SubjectCategories.FindAsync(new object[] { newParentSubjectId.Value }, cancellationToken);
                if (parent == null)
                {
                    throw new KeyNotFoundException("Không tìm thấy môn học cha mới.");
                }

                newDepth = parent.Depth + 1;
            }

            int depthDelta = newDepth - target.Depth;

            // Check maximum depth restriction across all subtree nodes
            var subtreeIds = await GetDescendantSubjectIdsAsync(subjectId, cancellationToken);
            var subtreeNodes = await _db.SubjectCategories
                .Where(s => subtreeIds.Contains(s.SubjectId))
                .ToListAsync(cancellationToken);

            int maxSubtreeDepth = subtreeNodes.Max(s => s.Depth) + depthDelta;
            if (maxSubtreeDepth > 3)
            {
                throw new InvalidOperationException($"Không thể di chuyển: Cây danh mục sau khi di chuyển vượt quá độ sâu tối đa cho phép (Depth {maxSubtreeDepth} > 3).");
            }

            // Apply updates
            target.ParentSubjectId = newParentSubjectId;
            target.SortOrder = newSortOrder;
            target.Depth = newDepth;
            target.UpdatedAt = DateTime.UtcNow;

            foreach (var node in subtreeNodes)
            {
                if (node.SubjectId != subjectId)
                {
                    node.Depth += depthDelta;
                    node.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception)
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task DeleteSubjectAsync(int subjectId, int moderatorId, CancellationToken cancellationToken = default)
    {
        var subject = await _db.SubjectCategories.FindAsync(new object[] { subjectId }, cancellationToken);
        if (subject == null)
            throw new KeyNotFoundException("Không tìm thấy môn học.");

        // Check if subject has children
        bool hasChildren = await _db.SubjectCategories.AnyAsync(s => s.ParentSubjectId == subjectId, cancellationToken);
        if (hasChildren)
        {
            throw new InvalidOperationException("Không thể xóa môn học khi vẫn còn các môn học con. Vui lòng di chuyển hoặc xóa các môn con trước.");
        }

        // Check if documents reference this subject
        bool hasDocuments = await _db.Documents.AnyAsync(d => d.Subject == subject.Name && d.IsDeleted != true, cancellationToken);
        if (hasDocuments)
        {
            throw new InvalidOperationException($"Không thể xóa môn học '{subject.Name}' vì hiện đang có tài liệu thuộc môn học này.");
        }

        _db.SubjectCategories.Remove(subject);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
