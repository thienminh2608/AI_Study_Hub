using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Application.Services;
using AIStudyHub.Domain.Entities;
using Xunit;

namespace AIStudyHub.UnitTests;

public class HierarchicalSubjectTaxonomyTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly TestStudyHubDbContext _db;
    private readonly SubjectService _subjectService;

    public HierarchicalSubjectTaxonomyTests()
    {
        _factory = new TestDbContextFactory();
        _db = _factory.CreateContext();
        _subjectService = new SubjectService(_db, null!);
        SeedBaseDataAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _db.Dispose();
        _factory.Dispose();
    }

    private async Task SeedBaseDataAsync()
    {
        _db.Subscriptions.Add(new Subscription { TierId = 2, TierName = "Basic", Price = 0, MaxStorageMb = 100, AiPromptLimitPerDay = 10, TotalStorageMb = 100 });
        _db.Users.Add(new User { UserId = 1, Username = "admin1", Email = "admin1@test.com", Role = "ADMIN", Status = "ACTIVE", TierId = 2 });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateSubjectNode_Calculates_Depth_And_Enforces_Max_Depth_3()
    {
        // 1. Root node (Depth 0)
        var root = await _subjectService.CreateSubjectAsync("Toán học", 1, parentSubjectId: null, sortOrder: 1, autoApprove: true);
        Assert.Equal(0, root.Depth);
        Assert.Null(root.ParentSubjectId);

        // 2. Child node (Depth 1)
        var child = await _subjectService.CreateSubjectAsync("Đại số", 1, parentSubjectId: root.SubjectId, sortOrder: 1, autoApprove: true);
        Assert.Equal(1, child.Depth);
        Assert.Equal(root.SubjectId, child.ParentSubjectId);

        // 3. Grandchild node (Depth 2)
        var grandchild = await _subjectService.CreateSubjectAsync("Đại số tuyến tính", 1, parentSubjectId: child.SubjectId, sortOrder: 1, autoApprove: true);
        Assert.Equal(2, grandchild.Depth);

        // 4. Great-grandchild node (Depth 3)
        var leaf = await _subjectService.CreateSubjectAsync("Ma trận & Định thức", 1, parentSubjectId: grandchild.SubjectId, sortOrder: 1, autoApprove: true);
        Assert.Equal(3, leaf.Depth);

        // 5. Attempting to add 5th level (Depth 4) throws InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _subjectService.CreateSubjectAsync("Phép nhân ma trận", 1, parentSubjectId: leaf.SubjectId, autoApprove: true));
    }

    [Fact]
    public async Task MoveSubjectSubtree_Prevents_Cycles_And_Updates_Descendant_Depths()
    {
        // Setup tree: RootA (0) -> NodeB (1) -> NodeC (2)
        var rootA = await _subjectService.CreateSubjectAsync("Khoa học tự nhiên", 1, autoApprove: true);
        var nodeB = await _subjectService.CreateSubjectAsync("Vật lý", 1, parentSubjectId: rootA.SubjectId, autoApprove: true);
        var nodeC = await _subjectService.CreateSubjectAsync("Quang học", 1, parentSubjectId: nodeB.SubjectId, autoApprove: true);

        // Attempting to move RootA under NodeC must be rejected as cycle
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _subjectService.MoveSubjectSubtreeAsync(rootA.SubjectId, nodeC.SubjectId, 0));

        // Create RootD (0) and move NodeB under RootD
        var rootD = await _subjectService.CreateSubjectAsync("STEM", 1, autoApprove: true);
        bool moved = await _subjectService.MoveSubjectSubtreeAsync(nodeB.SubjectId, rootD.SubjectId, 1);
        Assert.True(moved);

        var updatedB = await _db.SubjectCategories.FindAsync(nodeB.SubjectId);
        var updatedC = await _db.SubjectCategories.FindAsync(nodeC.SubjectId);
        Assert.Equal(rootD.SubjectId, updatedB!.ParentSubjectId);
        Assert.Equal(1, updatedB.Depth);
        Assert.Equal(2, updatedC!.Depth);
    }

    [Fact]
    public async Task DeleteSubject_Fails_If_Has_Children_Or_Referenced_Documents()
    {
        var parent = await _subjectService.CreateSubjectAsync("Lịch sử", 1, autoApprove: true);
        var child = await _subjectService.CreateSubjectAsync("Lịch sử Việt Nam", 1, parentSubjectId: parent.SubjectId, autoApprove: true);

        // Cannot delete parent when child exists
        await Assert.ThrowsAsync<InvalidOperationException>(() => _subjectService.DeleteSubjectAsync(parent.SubjectId, 1));

        // Seed document under child
        _db.Users.Add(new User { UserId = 801, Username = "history_fan", Email = "h@test.com", Role = "STUDENT", Status = "ACTIVE", TierId = 2 });
        _db.Documents.Add(new Document
        {
            DocumentId = 401,
            UserId = 801,
            Title = "Doc Lich Su",
            Subject = "Lịch sử Việt Nam",
            CloudStorageUrl = "https://storage.test/doc.pdf",
            FileExtension = "pdf",
            SharingPermission = "PUBLIC",
            IsDeleted = false
        });
        await _db.SaveChangesAsync();

        // Cannot delete child when document exists
        await Assert.ThrowsAsync<InvalidOperationException>(() => _subjectService.DeleteSubjectAsync(child.SubjectId, 1));
    }

    [Fact]
    public async Task GetDescendantSubjectNames_Returns_Full_Subtree_Names_For_Document_Filtering()
    {
        var root = await _subjectService.CreateSubjectAsync("Công nghệ thông tin", 1, autoApprove: true);
        var child1 = await _subjectService.CreateSubjectAsync("Lập trình Web", 1, parentSubjectId: root.SubjectId, autoApprove: true);
        var child2 = await _subjectService.CreateSubjectAsync("Trí tuệ nhân tạo", 1, parentSubjectId: root.SubjectId, autoApprove: true);
        var leaf = await _subjectService.CreateSubjectAsync("Machine Learning", 1, parentSubjectId: child2.SubjectId, autoApprove: true);

        var names = await _subjectService.GetDescendantSubjectNamesAsync("Công nghệ thông tin");

        Assert.Contains("Công nghệ thông tin", names);
        Assert.Contains("Lập trình Web", names);
        Assert.Contains("Trí tuệ nhân tạo", names);
        Assert.Contains("Machine Learning", names);
        Assert.Equal(4, names.Count);
    }
}
