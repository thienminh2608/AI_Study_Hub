using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIStudyHub.Application.Services;
using AIStudyHub.Domain.Entities;
using Xunit;

namespace AIStudyHub.UnitTests;

public class PermissionInheritanceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;

    public PermissionInheritanceTests()
    {
        _factory = new TestDbContextFactory();
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    private static void SeedUsers(AIStudyHub.Infrastructure.Persistence.StudyHubDbContext db)
    {
        if (!db.Subscriptions.Any())
        {
            db.Subscriptions.Add(new Subscription { TierId = 1, TierName = "Free", Price = 0, MaxStorageMb = 50, TotalStorageMb = 50, AiPromptLimitPerDay = 5 });
        }
        if (!db.Users.Any(u => u.UserId == 1))
        {
            db.Users.Add(new User { UserId = 1, Username = "ownerUser", Email = "owner@test.com", PasswordHash = "hash", Role = "STUDENT", Status = "ACTIVE", TierId = 1 });
        }
        if (!db.Users.Any(u => u.UserId == 2))
        {
            db.Users.Add(new User { UserId = 2, Username = "recipientUser", Email = "recipient@test.com", PasswordHash = "hash", Role = "STUDENT", Status = "ACTIVE", TierId = 1 });
        }
        if (!db.Users.Any(u => u.UserId == 3))
        {
            db.Users.Add(new User { UserId = 3, Username = "strangerUser", Email = "stranger@test.com", PasswordHash = "hash", Role = "STUDENT", Status = "ACTIVE", TierId = 1 });
        }
        if (!db.Users.Any(u => u.UserId == 4))
        {
            db.Users.Add(new User { UserId = 4, Username = "adminUser", Email = "admin@test.com", PasswordHash = "hash", Role = "ADMIN", Status = "ACTIVE", TierId = 1 });
        }
        db.SaveChanges();
    }

    [Fact]
    public async Task Owner_Has_Owner_Role_And_Full_Access()
    {
        using var db = _factory.CreateContext();
        SeedUsers(db);

        var doc = new Document
        {
            DocumentId = 10,
            UserId = 1,
            Title = "Owner Doc",
            FileExtension = "pdf",
            CloudStorageUrl = "/uploads/1/doc.pdf",
            SharingPermission = "PRIVATE"
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        var service = new PermissionService(db);
        var role = await service.GetEffectiveDocumentRoleAsync(10, 1);
        var canView = await service.CanViewDocumentAsync(10, 1);
        var canDownload = await service.CanDownloadDocumentAsync(10, 1);
        var canEdit = await service.CanEditDocumentAsync(10, 1);

        Assert.Equal("OWNER", role);
        Assert.True(canView);
        Assert.True(canDownload);
        Assert.True(canEdit);
    }

    [Fact]
    public async Task Direct_Document_Share_Grants_Specified_Role()
    {
        using var db = _factory.CreateContext();
        SeedUsers(db);

        var doc = new Document
        {
            DocumentId = 20,
            UserId = 1,
            Title = "Shared Doc",
            FileExtension = "docx",
            CloudStorageUrl = "/uploads/1/doc.docx",
            SharingPermission = "PRIVATE"
        };
        db.Documents.Add(doc);
        db.DocumentShares.Add(new DocumentShare
        {
            DocumentId = 20,
            OwnerUserId = 1,
            SharedWithUserId = 2,
            Role = "VIEWER",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new PermissionService(db);

        // Recipient (User 2)
        var role2 = await service.GetEffectiveDocumentRoleAsync(20, 2);
        var canView2 = await service.CanViewDocumentAsync(20, 2);
        var canEdit2 = await service.CanEditDocumentAsync(20, 2);

        Assert.Equal("VIEWER", role2);
        Assert.True(canView2);
        Assert.False(canEdit2);

        // Stranger (User 3)
        var role3 = await service.GetEffectiveDocumentRoleAsync(20, 3);
        var canView3 = await service.CanViewDocumentAsync(20, 3);

        Assert.Equal("NONE", role3);
        Assert.False(canView3);
    }

    [Fact]
    public async Task Parent_Folder_Share_Inherits_To_Child_Document()
    {
        using var db = _factory.CreateContext();
        SeedUsers(db);

        var folder = new Folder
        {
            FolderId = 100,
            UserId = 1,
            FolderName = "Parent Folder",
            ParentFolderId = null
        };
        db.Folders.Add(folder);

        var doc = new Document
        {
            DocumentId = 30,
            UserId = 1,
            FolderId = 100,
            Title = "Child Doc",
            FileExtension = "pdf",
            CloudStorageUrl = "/uploads/1/child.pdf",
            SharingPermission = "PRIVATE"
        };
        db.Documents.Add(doc);

        db.FolderShares.Add(new FolderShare
        {
            FolderId = 100,
            OwnerUserId = 1,
            SharedWithUserId = 2,
            Role = "VIEWER",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new PermissionService(db);

        var folderRole = await service.GetEffectiveFolderRoleAsync(100, 2);
        var docRole = await service.GetEffectiveDocumentRoleAsync(30, 2);
        var canView = await service.CanViewDocumentAsync(30, 2);
        var canDownload = await service.CanDownloadDocumentAsync(30, 2);

        Assert.Equal("VIEWER", folderRole);
        Assert.Equal("VIEWER", docRole);
        Assert.True(canView);
        Assert.True(canDownload);
    }

    [Fact]
    public async Task MultiLevel_Grandparent_Folder_Share_Inherits_Correctly()
    {
        using var db = _factory.CreateContext();
        SeedUsers(db);

        var grandParent = new Folder { FolderId = 1, UserId = 1, FolderName = "GrandParent", ParentFolderId = null };
        var parent = new Folder { FolderId = 2, UserId = 1, FolderName = "Parent", ParentFolderId = 1 };
        var child = new Folder { FolderId = 3, UserId = 1, FolderName = "Child", ParentFolderId = 2 };
        db.Folders.AddRange(grandParent, parent, child);

        var doc = new Document
        {
            DocumentId = 40,
            UserId = 1,
            FolderId = 3,
            Title = "Deep Child Doc",
            FileExtension = "pdf",
            CloudStorageUrl = "/uploads/1/deep.pdf",
            SharingPermission = "PRIVATE"
        };
        db.Documents.Add(doc);

        // Share GrandParent folder with User 2 as EDITOR
        db.FolderShares.Add(new FolderShare
        {
            FolderId = 1,
            OwnerUserId = 1,
            SharedWithUserId = 2,
            Role = "EDITOR",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new PermissionService(db);

        var docRole = await service.GetEffectiveDocumentRoleAsync(40, 2);
        var canEdit = await service.CanEditDocumentAsync(40, 2);

        Assert.Equal("EDITOR", docRole);
        Assert.True(canEdit);
    }

    [Fact]
    public async Task Folder_Cycle_Does_Not_Cause_Infinite_Loop()
    {
        using var db = _factory.CreateContext();
        SeedUsers(db);

        // Corrupted cyclic hierarchy: 11 -> 12 -> 11
        var f1 = new Folder { FolderId = 11, UserId = 1, FolderName = "F1", ParentFolderId = null };
        var f2 = new Folder { FolderId = 12, UserId = 1, FolderName = "F2", ParentFolderId = null };
        db.Folders.AddRange(f1, f2);
        await db.SaveChangesAsync();

        f1.ParentFolderId = 12;
        f2.ParentFolderId = 11;

        var doc = new Document
        {
            DocumentId = 50,
            UserId = 1,
            FolderId = 11,
            Title = "Cyclic Doc",
            FileExtension = "pdf",
            CloudStorageUrl = "/uploads/1/cyclic.pdf",
            SharingPermission = "PRIVATE"
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        var service = new PermissionService(db);

        // Should break safely without hanging
        var role = await service.GetEffectiveDocumentRoleAsync(50, 3);
        Assert.Equal("NONE", role);
    }

    [Fact]
    public async Task Admin_Bypasses_Document_View_And_Edit_Restrictions()
    {
        using var db = _factory.CreateContext();
        SeedUsers(db);

        var doc = new Document
        {
            DocumentId = 60,
            UserId = 1,
            Title = "Private Secret",
            FileExtension = "pdf",
            CloudStorageUrl = "/uploads/1/secret.pdf",
            SharingPermission = "PRIVATE"
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        var service = new PermissionService(db);

        // Admin (User 4)
        var canView = await service.CanViewDocumentAsync(60, 4);
        var canEdit = await service.CanEditDocumentAsync(60, 4);

        Assert.True(canView);
        Assert.True(canEdit);
    }

    [Fact]
    public async Task GetAccessibleDocumentIdsAsync_Returns_Owned_Shared_And_Inherited_Docs()
    {
        using var db = _factory.CreateContext();
        SeedUsers(db);

        // User 2 owns doc 101
        db.Documents.Add(new Document { DocumentId = 101, UserId = 2, Title = "Owned", FileExtension = "pdf", CloudStorageUrl = "/u/101.pdf" });

        // User 1 owns doc 102 (directly shared with User 2)
        db.Documents.Add(new Document { DocumentId = 102, UserId = 1, Title = "Direct Shared", FileExtension = "pdf", CloudStorageUrl = "/u/102.pdf" });
        db.DocumentShares.Add(new DocumentShare { DocumentId = 102, OwnerUserId = 1, SharedWithUserId = 2, Role = "VIEWER" });

        // User 1 owns folder 201 (shared with User 2) containing doc 103
        db.Folders.Add(new Folder { FolderId = 201, UserId = 1, FolderName = "Shared Folder" });
        db.FolderShares.Add(new FolderShare { FolderId = 201, OwnerUserId = 1, SharedWithUserId = 2, Role = "VIEWER" });
        db.Documents.Add(new Document { DocumentId = 103, UserId = 1, FolderId = 201, Title = "Inherited Doc", FileExtension = "pdf", CloudStorageUrl = "/u/103.pdf" });

        // User 1 owns doc 104 (not shared at all)
        db.Documents.Add(new Document { DocumentId = 104, UserId = 1, Title = "Private Unshared", FileExtension = "pdf", CloudStorageUrl = "/u/104.pdf" });

        await db.SaveChangesAsync();

        var service = new PermissionService(db);
        var accessibleIds = await service.GetAccessibleDocumentIdsAsync(2);

        Assert.Contains(101, accessibleIds);
        Assert.Contains(102, accessibleIds);
        Assert.Contains(103, accessibleIds);
        Assert.DoesNotContain(104, accessibleIds);
    }

    [Fact]
    public async Task Link_Only_Requires_Valid_Token_And_Denies_Bare_Access()
    {
        using var db = _factory.CreateContext();
        SeedUsers(db);

        var doc = new Document
        {
            DocumentId = 301,
            UserId = 1,
            Title = "Link Protected Doc",
            FileExtension = "pdf",
            CloudStorageUrl = "/u/link.pdf",
            SharingPermission = "PRIVATE",
            GeneralAccess = "LINK",
            ShareLinkToken = "valid-secret-token",
            IsShareLinkRevoked = false,
            ShareLinkExpiresAt = DateTime.UtcNow.AddDays(1)
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        var service = new PermissionService(db);

        // Stranger with no token -> forbidden
        var roleNoToken = await service.GetEffectiveDocumentRoleAsync(301, 3, null);
        var canViewNoToken = await service.CanViewDocumentAsync(301, 3, null);
        Assert.Equal("NONE", roleNoToken);
        Assert.False(canViewNoToken);

        // Stranger with wrong token -> forbidden
        var roleWrongToken = await service.GetEffectiveDocumentRoleAsync(301, 3, "wrong-token");
        Assert.Equal("NONE", roleWrongToken);

        // Stranger with valid token -> permitted
        var roleValidToken = await service.GetEffectiveDocumentRoleAsync(301, 3, "valid-secret-token");
        var canViewValidToken = await service.CanViewDocumentAsync(301, 3, "valid-secret-token");
        Assert.Equal("VIEWER", roleValidToken);
        Assert.True(canViewValidToken);
    }

    [Fact]
    public async Task CanManageDocumentAccess_Only_Permits_Owner_And_Admin()
    {
        using var db = _factory.CreateContext();
        SeedUsers(db);

        var doc = new Document
        {
            DocumentId = 401,
            UserId = 1, // owner
            Title = "Sensitive Doc",
            FileExtension = "pdf",
            CloudStorageUrl = "/u/sensitive.pdf",
            SharingPermission = "PRIVATE"
        };
        db.Documents.Add(doc);
        db.DocumentShares.Add(new DocumentShare
        {
            DocumentId = 401,
            OwnerUserId = 1,
            SharedWithUserId = 2, // editor
            Role = "EDITOR"
        });
        await db.SaveChangesAsync();

        var service = new PermissionService(db);

        // Owner (User 1)
        Assert.True(await service.CanManageDocumentAccessAsync(401, 1));

        // Editor (User 2) -> CANNOT manage shares
        Assert.False(await service.CanManageDocumentAccessAsync(401, 2));

        // Stranger (User 3) -> CANNOT manage shares
        Assert.False(await service.CanManageDocumentAccessAsync(401, 3));

        // Admin (User 4) -> CAN manage shares
        Assert.True(await service.CanManageDocumentAccessAsync(401, 4));
    }

    [Fact]
    public async Task GetSharedDocumentIdsAsync_Excludes_Public_And_Owned_Docs()
    {
        using var db = _factory.CreateContext();
        SeedUsers(db);

        // 1. User 2 owned
        db.Documents.Add(new Document { DocumentId = 501, UserId = 2, Title = "Owned", FileExtension = "pdf", CloudStorageUrl = "/u/501.pdf" });

        // 2. Public document (not shared directly)
        db.Documents.Add(new Document { DocumentId = 502, UserId = 1, Title = "Public Doc", SharingPermission = "PUBLIC", IsFlagged = false, FileExtension = "pdf", CloudStorageUrl = "/u/502.pdf" });

        // 3. Directly shared with User 2
        db.Documents.Add(new Document { DocumentId = 503, UserId = 1, Title = "Direct Shared", FileExtension = "pdf", CloudStorageUrl = "/u/503.pdf" });
        db.DocumentShares.Add(new DocumentShare { DocumentId = 503, OwnerUserId = 1, SharedWithUserId = 2, Role = "VIEWER" });

        // 4. Inherited from shared folder
        db.Folders.Add(new Folder { FolderId = 601, UserId = 1, FolderName = "Shared Folder" });
        db.FolderShares.Add(new FolderShare { FolderId = 601, OwnerUserId = 1, SharedWithUserId = 2, Role = "VIEWER" });
        db.Documents.Add(new Document { DocumentId = 504, UserId = 1, FolderId = 601, Title = "Inherited Doc", FileExtension = "pdf", CloudStorageUrl = "/u/504.pdf" });

        await db.SaveChangesAsync();

        var service = new PermissionService(db);
        var sharedIds = await service.GetSharedDocumentIdsAsync(2);

        // Must ONLY contain 503 and 504
        Assert.Contains(503, sharedIds);
        Assert.Contains(504, sharedIds);
        Assert.DoesNotContain(501, sharedIds); // owned
        Assert.DoesNotContain(502, sharedIds); // public
    }
}
