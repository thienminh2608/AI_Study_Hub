using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Domain.Entities;
using AIStudyHub.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIStudyHub.IntegrationTests;

public class PermissionInheritanceIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PermissionInheritanceIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task SeedUsersAndDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestStudyHubDbContext>();
        db.Database.EnsureCreated();

        if (!db.Subscriptions.Any())
        {
            db.Subscriptions.Add(new Subscription
            {
                TierId = 1,
                TierName = "Free",
                Price = 0,
                MaxStorageMb = 50,
                TotalStorageMb = 50,
                AiPromptLimitPerDay = 5
            });
            await db.SaveChangesAsync();
        }

        if (!db.Users.Any(u => u.UserId == 10))
        {
            db.Users.Add(new User
            {
                UserId = 10,
                Username = "userOwner",
                Email = "owner@test.com",
                PasswordHash = "hash",
                Role = "STUDENT",
                Status = "ACTIVE",
                TierId = 1
            });
        }

        if (!db.Users.Any(u => u.UserId == 20))
        {
            db.Users.Add(new User
            {
                UserId = 20,
                Username = "userRecipient",
                Email = "recipient@test.com",
                PasswordHash = "hash",
                Role = "STUDENT",
                Status = "ACTIVE",
                TierId = 1
            });
        }

        if (!db.Users.Any(u => u.UserId == 30))
        {
            db.Users.Add(new User
            {
                UserId = 30,
                Username = "userStranger",
                Email = "stranger@test.com",
                PasswordHash = "hash",
                Role = "STUDENT",
                Status = "ACTIVE",
                TierId = 1
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Folder_Recipient_Can_Access_Child_Document_And_Revocation_Denies_Access()
    {
        await SeedUsersAndDataAsync();

        int folderId = 501;
        int docId = 601;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestStudyHubDbContext>();

            var folder = new Folder
            {
                FolderId = folderId,
                UserId = 10,
                FolderName = "Project Shared Folder",
                ParentFolderId = null,
                CreatedAt = DateTime.UtcNow
            };
            db.Folders.Add(folder);

            var doc = new Document
            {
                DocumentId = docId,
                UserId = 10,
                FolderId = folderId,
                Title = "Inherited Secret Specification",
                FileExtension = "pdf",
                CloudStorageUrl = "/uploads/10/spec.pdf",
                SharingPermission = "PRIVATE",
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow
            };
            db.Documents.Add(doc);

            // Share folder with User 20
            db.FolderShares.Add(new FolderShare
            {
                FolderId = folderId,
                OwnerUserId = 10,
                SharedWithUserId = 20,
                Role = "VIEWER",
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var recipientToken = _factory.GenerateJwtToken(20, "userRecipient", "STUDENT");
        var strangerToken = _factory.GenerateJwtToken(30, "userStranger", "STUDENT");

        // 1. Recipient (User 20) requests document details -> should succeed (200 OK)
        var recipientRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/document/{docId}");
        recipientRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", recipientToken);
        var recipientResponse = await _client.SendAsync(recipientRequest);
        Assert.Equal(HttpStatusCode.OK, recipientResponse.StatusCode);

        // 2. Stranger (User 30) requests document details -> should be forbidden (403 Forbidden)
        var strangerRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/document/{docId}");
        strangerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", strangerToken);
        var strangerResponse = await _client.SendAsync(strangerRequest);
        Assert.Equal(HttpStatusCode.Forbidden, strangerResponse.StatusCode);

        // 3. User 20 requests Shared With Me -> should contain the document
        var sharedWithMeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/document/shared-with-me");
        sharedWithMeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", recipientToken);
        var sharedWithMeResponse = await _client.SendAsync(sharedWithMeRequest);
        Assert.Equal(HttpStatusCode.OK, sharedWithMeResponse.StatusCode);
        var sharedDocs = await sharedWithMeResponse.Content.ReadFromJsonAsync<System.Collections.Generic.List<DocumentResponseDto>>();
        Assert.NotNull(sharedDocs);
        Assert.Contains(sharedDocs, d => d.DocumentId == docId);

        // 4. Revoke the folder share in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestStudyHubDbContext>();
            var share = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                db.FolderShares, s => s.FolderId == folderId && s.SharedWithUserId == 20);
            if (share != null)
            {
                db.FolderShares.Remove(share);
                await db.SaveChangesAsync();
            }
        }

        // 5. Recipient (User 20) requests document again -> should now be forbidden (403 Forbidden)
        var postRevokeRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/document/{docId}");
        postRevokeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", recipientToken);
        var postRevokeResponse = await _client.SendAsync(postRevokeRequest);
        Assert.Equal(HttpStatusCode.Forbidden, postRevokeResponse.StatusCode);
    }

    [Fact]
    public async Task PublicDocuments_Paged_Api_Filters_By_Subject()
    {
        await SeedUsersAndDataAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestStudyHubDbContext>();

            db.Documents.Add(new Document
            {
                DocumentId = 701,
                UserId = 10,
                Title = "Public Math Doc",
                Subject = "Toán học",
                FileExtension = "pdf",
                CloudStorageUrl = "/u/math.pdf",
                SharingPermission = "PUBLIC",
                IsFlagged = false,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow
            });

            db.Documents.Add(new Document
            {
                DocumentId = 702,
                UserId = 10,
                Title = "Public Physics Doc",
                Subject = "Vật lý",
                FileExtension = "pdf",
                CloudStorageUrl = "/u/physics.pdf",
                SharingPermission = "PUBLIC",
                IsFlagged = false,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        // Anonymous call with subject=Toán học
        var response = await _client.GetAsync("/api/document/public/paged?subject=Toán học");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pagedResult = await response.Content.ReadFromJsonAsync<PagedResult<DocumentResponseDto>>();
        Assert.NotNull(pagedResult);
        Assert.All(pagedResult.Items, doc => Assert.Equal("Toán học", doc.Subject));
        Assert.DoesNotContain(pagedResult.Items, doc => doc.DocumentId == 702);
    }

    [Fact]
    public async Task Inherited_Folder_Recipient_Can_Access_ExtractedText_While_Stranger_Is_Forbidden()
    {
        await SeedUsersAndDataAsync();
        int folderId = 502;
        int docId = 602;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestStudyHubDbContext>();
            db.Folders.Add(new Folder { FolderId = folderId, UserId = 10, FolderName = "Text Shared Folder" });
            db.Documents.Add(new Document
            {
                DocumentId = docId,
                UserId = 10,
                FolderId = folderId,
                Title = "Shared Text Doc",
                FileExtension = "txt",
                CloudStorageUrl = "/u/text.txt",
                SharingPermission = "PRIVATE"
            });
            db.DocumentExtractedTexts.Add(new DocumentExtractedText
            {
                DocumentId = docId,
                ExtractedText = "Bí mật chuyên đề công nghệ phần mềm 2026",
                CreatedAt = DateTime.UtcNow
            });
            db.FolderShares.Add(new FolderShare { FolderId = folderId, OwnerUserId = 10, SharedWithUserId = 20, Role = "VIEWER" });
            await db.SaveChangesAsync();
        }

        var recipientToken = _factory.GenerateJwtToken(20, "userRecipient", "STUDENT");
        var strangerToken = _factory.GenerateJwtToken(30, "userStranger", "STUDENT");

        // Recipient can access extracted text
        var reqRecipient = new HttpRequestMessage(HttpMethod.Get, $"/api/document/{docId}/text");
        reqRecipient.Headers.Authorization = new AuthenticationHeaderValue("Bearer", recipientToken);
        var resRecipient = await _client.SendAsync(reqRecipient);
        Assert.Equal(HttpStatusCode.OK, resRecipient.StatusCode);

        // Stranger cannot access extracted text
        var reqStranger = new HttpRequestMessage(HttpMethod.Get, $"/api/document/{docId}/text");
        reqStranger.Headers.Authorization = new AuthenticationHeaderValue("Bearer", strangerToken);
        var resStranger = await _client.SendAsync(reqStranger);
        Assert.Equal(HttpStatusCode.Forbidden, resStranger.StatusCode);
    }

    [Fact]
    public async Task Documents_Added_To_Folder_After_Share_Are_Immediately_Inherited()
    {
        await SeedUsersAndDataAsync();
        int folderId = 503;
        int docId = 603;

        // 1. Share folder first
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestStudyHubDbContext>();
            db.Folders.Add(new Folder { FolderId = folderId, UserId = 10, FolderName = "Future Uploads Folder" });
            db.FolderShares.Add(new FolderShare { FolderId = folderId, OwnerUserId = 10, SharedWithUserId = 20, Role = "VIEWER" });
            await db.SaveChangesAsync();
        }

        // 2. Later, owner adds a document to the folder
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestStudyHubDbContext>();
            db.Documents.Add(new Document
            {
                DocumentId = docId,
                UserId = 10,
                FolderId = folderId,
                Title = "Late Arrival Document",
                FileExtension = "pdf",
                CloudStorageUrl = "/u/late.pdf",
                SharingPermission = "PRIVATE"
            });
            await db.SaveChangesAsync();
        }

        var recipientToken = _factory.GenerateJwtToken(20, "userRecipient", "STUDENT");

        // Recipient can access the new document immediately
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/document/{docId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", recipientToken);
        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Viewer_Cannot_Delete_Or_Manage_Shares()
    {
        await SeedUsersAndDataAsync();
        int folderId = 504;
        int docId = 604;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestStudyHubDbContext>();
            db.Folders.Add(new Folder { FolderId = folderId, UserId = 10, FolderName = "Restricted Folder" });
            db.Documents.Add(new Document
            {
                DocumentId = docId,
                UserId = 10,
                FolderId = folderId,
                Title = "Protected Document",
                FileExtension = "pdf",
                CloudStorageUrl = "/u/prot.pdf",
                SharingPermission = "PRIVATE"
            });
            db.FolderShares.Add(new FolderShare { FolderId = folderId, OwnerUserId = 10, SharedWithUserId = 20, Role = "VIEWER" });
            await db.SaveChangesAsync();
        }

        var recipientToken = _factory.GenerateJwtToken(20, "userRecipient", "STUDENT");

        // 1. Cannot Delete document
        var deleteReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/document/{docId}");
        deleteReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", recipientToken);
        var deleteRes = await _client.SendAsync(deleteReq);
        Assert.Equal(HttpStatusCode.BadRequest, deleteRes.StatusCode);

        // 2. Cannot view shares
        var sharesReq = new HttpRequestMessage(HttpMethod.Get, $"/api/document/{docId}/shares");
        sharesReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", recipientToken);
        var sharesRes = await _client.SendAsync(sharesReq);
        Assert.Equal(HttpStatusCode.Forbidden, sharesRes.StatusCode);

        // 3. Cannot share with other friends
        var shareFriendReq = new HttpRequestMessage(HttpMethod.Post, $"/api/document/{docId}/shares/30");
        shareFriendReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", recipientToken);
        var shareFriendRes = await _client.SendAsync(shareFriendReq);
        Assert.Equal(HttpStatusCode.Forbidden, shareFriendRes.StatusCode);

        // 4. Cannot view audience
        var audienceReq = new HttpRequestMessage(HttpMethod.Get, $"/api/document/{docId}/audience");
        audienceReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", recipientToken);
        var audienceRes = await _client.SendAsync(audienceReq);
        Assert.Equal(HttpStatusCode.Forbidden, audienceRes.StatusCode);
    }

    [Fact]
    public async Task Link_Protected_Document_Requires_Valid_Token_On_Api_Endpoints()
    {
        await SeedUsersAndDataAsync();
        int docId = 605;
        string token = "secure-link-key-123";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestStudyHubDbContext>();
            db.Documents.Add(new Document
            {
                DocumentId = docId,
                UserId = 10,
                Title = "Link Only Secret Doc",
                FileExtension = "pdf",
                CloudStorageUrl = "/u/linkonly.pdf",
                SharingPermission = "PRIVATE",
                GeneralAccess = "LINK",
                ShareLinkToken = token,
                IsShareLinkRevoked = false
            });
            await db.SaveChangesAsync();
        }

        var strangerToken = _factory.GenerateJwtToken(30, "userStranger", "STUDENT");

        // 1. Without query token -> 403 Forbidden
        var noTokenReq = new HttpRequestMessage(HttpMethod.Get, $"/api/document/{docId}");
        noTokenReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", strangerToken);
        var noTokenRes = await _client.SendAsync(noTokenReq);
        Assert.Equal(HttpStatusCode.Forbidden, noTokenRes.StatusCode);

        // 2. With invalid query token -> 403 Forbidden
        var invalidTokenReq = new HttpRequestMessage(HttpMethod.Get, $"/api/document/{docId}?token=wrong-token");
        invalidTokenReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", strangerToken);
        var invalidTokenRes = await _client.SendAsync(invalidTokenReq);
        Assert.Equal(HttpStatusCode.Forbidden, invalidTokenRes.StatusCode);

        // 3. With valid query token -> 200 OK
        var validTokenReq = new HttpRequestMessage(HttpMethod.Get, $"/api/document/{docId}?token={token}");
        validTokenReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", strangerToken);
        var validTokenRes = await _client.SendAsync(validTokenReq);
        Assert.Equal(HttpStatusCode.OK, validTokenRes.StatusCode);
    }
}
