using System;
using AIStudyHub.Application.Interfaces;

namespace AIStudyHub.UnitTests;

public class TestClock : IClock
{
    public DateTime Now { get; set; } = DateTime.Now;
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;

    public void AdvanceDays(int days)
    {
        Now = Now.AddDays(days);
        UtcNow = UtcNow.AddDays(days);
    }

    public void AdvanceHours(int hours)
    {
        Now = Now.AddHours(hours);
        UtcNow = UtcNow.AddHours(hours);
    }
}
