using System;

namespace AIStudyHub.Application.Interfaces;

public interface IClock
{
    DateTime Now
    {
        get;
    }
    DateTime UtcNow
    {
        get;
    }
}
