using System;
using AIStudyHub.Application.Interfaces;

namespace AIStudyHub.Infrastructure.Services.Time;

public class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
    public DateTime UtcNow => DateTime.UtcNow;
}
