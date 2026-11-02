using System;

namespace ControlPlane.Services;

public interface IClock
{
    DateTime Now { get; }
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
    public DateTime UtcNow => DateTime.UtcNow;
}
