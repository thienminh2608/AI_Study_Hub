using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIStudyHub.Application.Services;

public class FriendshipService : IFriendshipService
{
    private readonly IStudyHubDbContext _dbContext;

    public FriendshipService(IStudyHubDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string> GetFriendshipStatusAsync(int myUserId, int targetUserId)
    {
        var rel = await _dbContext.Friendships
            .FirstOrDefaultAsync(f => (f.RequesterId == myUserId && f.AddresseeId == targetUserId) ||
                                      (f.RequesterId == targetUserId && f.AddresseeId == myUserId));

        if (rel == null)
            return "NONE";
        return rel.Status ?? "NONE";
    }

    public async Task<bool> SendFriendRequestAsync(int myUserId, int addresseeId)
    {
        if (myUserId == addresseeId)
            return false;
        if (!await _dbContext.Users.AnyAsync(u => u.UserId == addresseeId && u.Status == "ACTIVE"))
            return false;

        // Check if there is an existing friendship
        var existing = await _dbContext.Friendships
            .FirstOrDefaultAsync(f => (f.RequesterId == myUserId && f.AddresseeId == addresseeId) ||
                                      (f.RequesterId == addresseeId && f.AddresseeId == myUserId));

        if (existing != null)
            return false;

        var fship = new Friendship
        {
            RequesterId = myUserId,
            AddresseeId = addresseeId,
            Status = "PENDING",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        _dbContext.Friendships.Add(fship);
        var requesterName = await _dbContext.Users.Where(u => u.UserId == myUserId).Select(u => u.Username).FirstAsync();
        _dbContext.ModerationNotices.Add(new ModerationNotice
        {
            UserId = addresseeId,
            RelatedUserId = myUserId,
            Type = "FRIEND_REQUEST",
            Title = "Lời mời kết bạn mới",
            Message = $"{requesterName} đã gửi cho bạn một lời mời kết bạn.",
            ActionUrl = "/friends?tab=pending",
            IsRead = false,
            CreatedAt = DateTime.Now
        });
        return await _dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> UpdateFriendshipStatusAsync(int myUserId, int targetUserId, string status)
    {
        status = status.ToUpper();
        if (status != "ACCEPTED" && status != "BLOCKED")
            return false;

        var rel = await _dbContext.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == myUserId && f.AddresseeId == targetUserId) ||
            (f.RequesterId == targetUserId && f.AddresseeId == myUserId));

        if (rel == null)
            return false;

        if (status == "ACCEPTED" &&
            (rel.Status != "PENDING" || rel.AddresseeId != myUserId))
        {
            return false;
        }

        if (status == "BLOCKED" && rel.Status == "BLOCKED" && rel.BlockerId != myUserId)
        {
            return false;
        }

        if (status == "ACCEPTED")
        {
            var accepterName = await _dbContext.Users.Where(u => u.UserId == myUserId).Select(u => u.Username).FirstAsync();
            _dbContext.ModerationNotices.Add(new ModerationNotice
            {
                UserId = rel.RequesterId,
                RelatedUserId = myUserId,
                Type = "FRIEND_ACCEPTED",
                Title = "Lời mời kết bạn đã được chấp nhận",
                Message = $"{accepterName} và bạn đã trở thành bạn bè.",
                ActionUrl = "/friends",
                IsRead = false,
                CreatedAt = DateTime.Now
            });
        }

        rel.Status = status;
        if (status == "BLOCKED")
        {
            rel.BlockerId = myUserId;
        }
        else
        {
            rel.BlockerId = null;
        }
        rel.UpdatedAt = DateTime.Now;

        return await _dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> DeleteFriendshipAsync(int myUserId, int targetUserId)
    {
        var rel = await _dbContext.Friendships
            .FirstOrDefaultAsync(f => (f.RequesterId == myUserId && f.AddresseeId == targetUserId) ||
                                      (f.RequesterId == targetUserId && f.AddresseeId == myUserId));

        if (rel == null)
            return false;

        // If blocked, only blocker can unblock (delete)
        if (rel.Status == "BLOCKED" && rel.BlockerId.HasValue && rel.BlockerId.Value != myUserId)
        {
            return false;
        }

        _dbContext.Friendships.Remove(rel);
        return await _dbContext.SaveChangesAsync() > 0;
    }

    public async Task<List<FriendDto>> GetAcceptedFriendsAsync(int userId)
    {
        var fships = await _dbContext.Friendships
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .Where(f => f.Status == "ACCEPTED" && (f.RequesterId == userId || f.AddresseeId == userId))
            .ToListAsync();

        var friends = new List<FriendDto>();
        foreach (var f in fships)
        {
            var otherUser = f.RequesterId == userId ? f.Addressee : f.Requester;
            if (otherUser != null)
            {
                friends.Add(new FriendDto
                {
                    UserId = otherUser.UserId,
                    Username = otherUser.Username,
                    Email = otherUser.Email ?? "",
                    Status = "ACCEPTED",
                    IsRequester = f.RequesterId == userId
                });
            }
        }
        return friends;
    }

    public async Task<List<FriendDto>> GetPendingRequestsAsync(int userId)
    {
        // Requests sent to me
        var fships = await _dbContext.Friendships
            .Include(f => f.Requester)
            .Where(f => f.Status == "PENDING" && f.AddresseeId == userId)
            .ToListAsync();

        return fships.Select(f => new FriendDto
        {
            UserId = f.Requester.UserId,
            Username = f.Requester.Username,
            Email = f.Requester.Email ?? "",
            Status = "PENDING",
            IsRequester = false
        }).ToList();
    }

    public async Task<List<FriendDto>> GetBlockedUsersAsync(int userId)
    {
        // Users blocked by me
        var fships = await _dbContext.Friendships
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .Where(f => f.Status == "BLOCKED" && f.BlockerId == userId)
            .ToListAsync();

        var blocked = new List<FriendDto>();
        foreach (var f in fships)
        {
            var otherUser = f.RequesterId == userId ? f.Addressee : f.Requester;
            if (otherUser != null)
            {
                blocked.Add(new FriendDto
                {
                    UserId = otherUser.UserId,
                    Username = otherUser.Username,
                    Email = otherUser.Email ?? "",
                    Status = "BLOCKED",
                    IsRequester = f.RequesterId == userId
                });
            }
        }
        return blocked;
    }

    public async Task<FriendDto?> FindUserByEmailAsync(int myUserId, string email)
    {
        var targetUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (targetUser == null)
            return null;

        var fship = await _dbContext.Friendships
            .FirstOrDefaultAsync(f => (f.RequesterId == myUserId && f.AddresseeId == targetUser.UserId) ||
                                      (f.RequesterId == targetUser.UserId && f.AddresseeId == myUserId));

        string status = "NONE";
        bool isRequester = false;

        if (targetUser.UserId == myUserId)
        {
            status = "SELF";
        }
        else if (fship != null)
        {
            status = fship.Status ?? "NONE";
            isRequester = fship.RequesterId == myUserId;

            if (status == "BLOCKED")
            {
                status = fship.BlockerId == myUserId ? "BLOCKED_BY_ME" : "BLOCKED_BY_THEM";
            }
            else if (status == "PENDING")
            {
                status = isRequester ? "PENDING_SENT" : "PENDING_RECEIVED";
            }
        }

        return new FriendDto
        {
            UserId = targetUser.UserId,
            Username = targetUser.Username,
            Email = targetUser.Email ?? "",
            Status = status,
            IsRequester = isRequester
        };
    }

    public async Task<PagedResult<FriendDto>> GetAcceptedFriendsPagedAsync(int userId, int pageNumber, int pageSize)
    {
        var all = await GetAcceptedFriendsAsync(userId);
        int total = all.Count;
        var paged = all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResult<FriendDto>(paged, total, pageNumber, pageSize);
    }

    public async Task<PagedResult<FriendDto>> GetPendingRequestsPagedAsync(int userId, int pageNumber, int pageSize)
    {
        var all = await GetPendingRequestsAsync(userId);
        int total = all.Count;
        var paged = all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResult<FriendDto>(paged, total, pageNumber, pageSize);
    }

    public async Task<PagedResult<FriendDto>> GetBlockedUsersPagedAsync(int userId, int pageNumber, int pageSize)
    {
        var all = await GetBlockedUsersAsync(userId);
        int total = all.Count;
        var paged = all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResult<FriendDto>(paged, total, pageNumber, pageSize);
    }
}
