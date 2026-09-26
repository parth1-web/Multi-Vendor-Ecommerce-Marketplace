using System.Reflection;
using FluentValidation;
using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Helpers;
using Marketplace.Application.Modules.Analytics.Abstractions;
using Marketplace.Application.Modules.Analytics.Services;
using Marketplace.Application.Modules.Auth.Abstractions;
using Marketplace.Application.Modules.Auth.Services;
using Marketplace.Application.Modules.Cart.Abstractions;
using Marketplace.Application.Modules.Cart.Services;
using Marketplace.Application.Modules.Catalog.Abstractions;
using Marketplace.Application.Modules.Catalog.Services;
using Marketplace.Application.Modules.Coupons.Abstractions;
using Marketplace.Application.Modules.Coupons.Services;
using Marketplace.Application.Modules.Inventory.Abstractions;
using Marketplace.Application.Modules.Inventory.Services;
using Marketplace.Application.Modules.Notifications.Abstractions;
using Marketplace.Application.Modules.Notifications.Services;
using Marketplace.Application.Modules.Orders.Abstractions;
using Marketplace.Application.Modules.Orders.Services;
using Marketplace.Application.Modules.Payments.Abstractions;
using Marketplace.Application.Modules.Payments.Services;
using Marketplace.Application.Modules.Reviews.Abstractions;
using Marketplace.Application.Modules.Reviews.Services;
using Marketplace.Application.Modules.Sellers.Abstractions;
using Marketplace.Application.Modules.Sellers.Services;
using Marketplace.Domain.Auditing;
using Marketplace.Domain.Cart;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Commissions;
using Marketplace.Domain.Coupons;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Notifications;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;
using Marketplace.Domain.Reviews;
using Marketplace.Domain.Sellers;
using Microsoft.Extensions.DependencyInjection;
using CartEntity = Marketplace.Domain.Cart.Cart;
using InventoryRecord = Marketplace.Domain.Inventory.Inventory;
using OrderEntity = Marketplace.Domain.Orders.Order;
using SellerOrderEntity = Marketplace.Domain.Orders.SellerOrder;
using WishlistEntity = Marketplace.Domain.Cart.Wishlist;

namespace Marketplace.Application;

/// <summary>
/// Single registration point for the Application layer. Repositories, the unit of work,
/// cache, gateway, scheduler and notifier ports are implemented in Infrastructure; only
/// use cases and validators live here.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly(), includeInternalTypes: true);

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<SlugGenerator>();

        // Use cases
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISellerService, SellerService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IWishlistService, WishlistService>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IAddressService, AddressService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IRefundService, RefundService>();
        services.AddScoped<ICommissionService, CommissionService>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<ICouponService, CouponService>();
        services.AddScoped<ISellerAnalyticsService, SellerAnalyticsService>();
        services.AddScoped<IAdminAnalyticsService, AdminAnalyticsService>();
        services.AddScoped<ICustomerAnalyticsService, CustomerAnalyticsService>();
        services.AddScoped<IReportService, ReportService>();

        // Cross-cutting services that also satisfy their own port
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IRefreshTokenProtector, RefreshTokenProtector>();
        services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationQueryService, NotificationService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();

        // Repositories, the unit of work, cache, gateways, scheduler and the real-time
        // notifier are ports implemented in Infrastructure and registered there.

        return services;
    }

    private static void RegisterRepositories(IServiceCollection services)
    {
    }
}
