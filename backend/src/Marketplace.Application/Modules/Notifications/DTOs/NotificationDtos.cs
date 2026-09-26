using Marketplace.Application.Common.Models;
using Marketplace.Domain.Enums;

namespace Marketplace.Application.Modules.Notifications.DTOs;

public sealed record NotificationResponse(
    Guid Id,
    NotificationType Type,
    string Title,
    string Body,
    string? Link,
    NotificationAudience Audience,
    bool IsRead,
    DateTimeOffset CreatedAt);

public sealed record NotificationUnreadCountResponse(int UnreadCount, int TotalCount);

public sealed record AuditLogResponse(
    Guid Id,
    Guid? ActorId,
    string ActorEmail,
    AuditAction Action,
    string EntityType,
    Guid? EntityId,
    string? EntityName,
    string? ChangesJson,
    string? IpAddress,
    string CorrelationId,
    DateTimeOffset CreatedAt);

public sealed record InventoryItemResponse(
    Guid InventoryId,
    Guid ProductId,
    Guid ProductVariantId,
    string ProductName,
    string? ProductImageUrl,
    string VariantName,
    string Sku,
    int AvailableQuantity,
    int ReservedQuantity,
    int SoldQuantity,
    int SellableQuantity,
    int LowStockThreshold,
    bool IsLowStock,
    bool IsOutOfStock,
    DateTimeOffset UpdatedAt);

public sealed record InventoryTransactionResponse(
    Guid Id,
    InventoryTransactionType Type,
    int QuantityDelta,
    int QuantityBefore,
    int QuantityAfter,
    string? ReferenceType,
    Guid? ReferenceId,
    string? Reason,
    DateTimeOffset CreatedAt);

public sealed record AdjustStockRequest(int Delta, string Reason);

public sealed record SetThresholdRequest(int LowStockThreshold);
