using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Modules.Auth.Abstractions;
using Marketplace.Application.Modules.Sellers.Abstractions;
using Marketplace.Application.Modules.Sellers.Services;
using Marketplace.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace Marketplace.API.Security;

/// <summary>
/// Adds the "seller must be approved" rule to <c>ActiveSellerOnly</c>. The check needs
/// the database, so it is an authorization handler rather than a policy expression.
/// </summary>
public sealed class SellerStatusAuthorizationHandler(
    ICurrentUser currentUser,
    ISellerService sellers) : AuthorizationHandler<ActiveSellerRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveSellerRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (currentUser.IsAdmin)
        {
            context.Succeed(requirement);
            return;
        }

        if (currentUser.SellerId is not { } sellerId)
        {
            return;
        }

        var result = await sellers.GetByIdAsync(sellerId).ConfigureAwait(false);
        if (result.IsSuccess && result.Value is { Status: SellerStatus.Active })
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Requirement satisfied only by an active (approved) seller.</summary>
public sealed class ActiveSellerRequirement : IAuthorizationRequirement
{
    public const string PolicyName = AuthorizationPolicies.ActiveSellerOnly;
}
