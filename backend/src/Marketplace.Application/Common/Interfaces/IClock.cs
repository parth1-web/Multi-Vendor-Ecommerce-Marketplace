namespace Marketplace.Application.Common.Interfaces;

/// <summary>Abstraction over the system clock so time-dependent rules stay unit testable.</summary>
public interface IClock
{
    /// <summary>Current instant in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>Default implementation backed by the operating system.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
