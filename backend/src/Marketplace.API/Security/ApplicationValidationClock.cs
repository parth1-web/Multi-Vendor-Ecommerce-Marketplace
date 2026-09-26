using Marketplace.Application.Common.Interfaces;

namespace Marketplace.API.Security;

/// <summary>
/// Bridges the application clock into components that can only be handed a
/// <see cref="Func{DateTime}"/>, such as the JWT bearer token validator.
/// Tokens are stamped with <see cref="IClock"/>, so validation has to read the same
/// source; otherwise any clock that is frozen, shifted or under test would reject the
/// tokens the application itself just issued.
/// </summary>
internal sealed class ApplicationValidationClock
{
    private IServiceProvider? _provider;

    /// <summary>Attaches the built application's service provider.</summary>
    public void Attach(IServiceProvider provider) => _provider = provider;

    /// <summary>Current application time, or the system time before the host is built.</summary>
    public DateTimeOffset UtcNow => _provider?.GetService<IClock>()?.UtcNow ?? DateTimeOffset.UtcNow;
}
