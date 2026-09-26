using Microsoft.AspNetCore.Authorization;

namespace Marketplace.API.Security;

/// <summary>
/// Central policy catalogue. Endpoints reference these names, so the role matrix is
/// visible in one file and covered by the authorization integration tests.
/// </summary>
public static class AuthorizationPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string SuperAdminOnly = "SuperAdminOnly";
    public const string SellerOnly = "SellerOnly";
    public const string CustomerOnly = "CustomerOnly";
    public const string ActiveSellerOnly = "ActiveSellerOnly";
    public const string SellerOrAdmin = "SellerOrAdmin";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(AdminOnly, policy => policy.RequireRole(nameof(UserRole.Admin), nameof(UserRole.SuperAdmin)));
        options.AddPolicy(SuperAdminOnly, policy => policy.RequireRole(nameof(UserRole.SuperAdmin)));
        options.AddPolicy(SellerOnly, policy => policy.RequireRole(nameof(UserRole.Seller)));
        options.AddPolicy(CustomerOnly, policy => policy.RequireRole(nameof(UserRole.Customer)));
        options.AddPolicy(SellerOrAdmin, policy => policy.RequireRole(nameof(UserRole.Seller), nameof(UserRole.Admin), nameof(UserRole.SuperAdmin)));

        // "Active seller" is a status check that needs the database, so it is enforced in
        // the service layer via Seller.CanListProducts rather than here.
        options.AddPolicy(ActiveSellerOnly, policy => policy.RequireRole(nameof(UserRole.Seller)));
    }
}

file static class UserRole
{
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";
    public const string Seller = "Seller";
    public const string Customer = "Customer";
}
