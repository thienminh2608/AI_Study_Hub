using AIStudyHub.Application.Interfaces;

using System;

namespace AIStudyHub.Infrastructure.Services.Time;

public class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
    public DateTime UtcNow => DateTime.UtcNow;
}
