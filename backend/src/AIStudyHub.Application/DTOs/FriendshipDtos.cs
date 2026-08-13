using System;

namespace AIStudyHub.Application.DTOs;

public class FriendshipDto
{
    public int FriendshipId
    {
        get; set;
    }
    public int RequesterId
    {
        get; set;
    }
    public int AddresseeId
    {
        get; set;
    }
    public string Status { get; set; } = null!;
    public int? BlockerId
    {
        get; set;
    }
    public DateTime? CreatedAt
    {
        get; set;
    }
    public DateTime? UpdatedAt
    {
        get; set;
    }
}

public class FriendDto
{
    public int UserId
    {
        get; set;
    }
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Status { get; set; } = null!; // Friendship status (ACCEPTED, PENDING, BLOCKED)
    public bool IsRequester
    {
        get; set;
    } // If current user sent the request
}

public class SendFriendRequestDto
{
    public int AddresseeId
    {
        get; set;
    }
}
