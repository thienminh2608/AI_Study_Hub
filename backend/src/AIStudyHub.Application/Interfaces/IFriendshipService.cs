using System.Collections.Generic;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;

namespace AIStudyHub.Application.Interfaces;

public interface IFriendshipService
{
    Task<string> GetFriendshipStatusAsync(int myUserId, int targetUserId);
    Task<bool> SendFriendRequestAsync(int myUserId, int addresseeId);
    Task<bool> UpdateFriendshipStatusAsync(int myUserId, int targetUserId, string status);
    Task<bool> DeleteFriendshipAsync(int myUserId, int targetUserId);
    Task<List<FriendDto>> GetAcceptedFriendsAsync(int userId);
    Task<List<FriendDto>> GetPendingRequestsAsync(int userId);
    Task<List<FriendDto>> GetBlockedUsersAsync(int userId);
    Task<FriendDto?> FindUserByEmailAsync(int myUserId, string email);
}
