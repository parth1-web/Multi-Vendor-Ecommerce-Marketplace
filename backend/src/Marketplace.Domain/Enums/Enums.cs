namespace Marketplace.Domain.Enums;

/// <summary>Marketplace role carried by every authenticated user.</summary>
public enum UserRole
{
    Customer = 0,
    Seller = 1,
    Admin = 2,
    SuperAdmin = 3
}

/// <summary>Lifecycle of a seller account.</summary>
public enum SellerStatus
{
    Pending = 0,
    Active = 1,
    Rejected = 2,
    Suspended = 3
}

/// <summary>Approval state of a listed product.</summary>
public enum ProductStatus
{
    Draft = 0,
    PendingApproval = 1,
    Published = 2,
    Rejected = 3,
    Archived = 4
}

/// <summary>Why a product left the published state, when applicable.</summary>
public enum ProductRejectionReason
{
    None = 0,
    InaccurateDescription = 1,
    ProhibitedItem = 2,
    CopyrightConcern = 3,
    PricingIssue = 4,
    MissingDocumentation = 5,
    Other = 6
}

/// <summary>Type of inventory movement recorded in the immutable ledger.</summary>
public enum InventoryTransactionType
{
    InitialStock = 0,
    Restock = 1,
    Reservation = 2,
    ReservationRelease = 3,
    Sale = 4,
    SaleReversal = 5,
    ManualAdjustment = 6,
    Return = 7,
    Damage = 8
}

/// <summary>Where a cart belongs.</summary>
public enum CartOwnerType
{
    Guest = 0,
    Customer = 1
}

/// <summary>Marketplace order state. Transitions are guarded by <c>OrderStatusTransition</c>.</summary>
public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Processing = 2,
    Packed = 3,
    Shipped = 4,
    Delivered = 5,
    Cancelled = 6,
    Returned = 7,
    Completed = 8
}

/// <summary>Per-seller fulfilment state.</summary>
public enum SellerOrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Processing = 2,
    Packed = 3,
    Shipped = 4,
    Delivered = 5,
    Cancelled = 6,
    Returned = 7,
    Completed = 8
}

/// <summary>Payment lifecycle.</summary>
public enum PaymentStatus
{
    Initiated = 0,
    Pending = 1,
    Processing = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
    Refunded = 6,
    PartiallyRefunded = 7
}

/// <summary>Payment provider in use.</summary>
public enum PaymentProvider
{
    Mock = 0,
    CashOnDelivery = 1,
    Khalti = 2,
    ESewa = 3,
    Stripe = 4
}

/// <summary>Payment provider operation recorded in the transaction ledger.</summary>
public enum PaymentTransactionType
{
    Initiated = 0,
    GatewayRequest = 1,
    GatewayResponse = 2,
    Verification = 3,
    WebhookReceived = 4,
    WebhookProcessed = 5,
    RefundInitiated = 6,
    RefundCompleted = 7
}

/// <summary>Refund request lifecycle.</summary>
public enum RefundStatus
{
    Requested = 0,
    UnderReview = 1,
    Approved = 2,
    Rejected = 3,
    Processing = 4,
    Completed = 5,
    Failed = 6,
    Cancelled = 7
}

/// <summary>Commission ledger status.</summary>
public enum CommissionStatus
{
    Pending = 0,
    Accrued = 1,
    Payable = 2,
    Paid = 3,
    Reversed = 4
}

/// <summary>Seller payout status.</summary>
public enum PayoutStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>Whether a coupon takes a percentage or a fixed amount.</summary>
public enum CouponDiscountType
{
    Percentage = 0,
    FixedAmount = 1
}

/// <summary>Who may create and manage a coupon.</summary>
public enum CouponScope
{
    Global = 0,
    Seller = 1
}

/// <summary>Coupon validity.</summary>
public enum CouponStatus
{
    Draft = 0,
    Active = 1,
    Paused = 2,
    Expired = 3,
    Exhausted = 4
}

/// <summary>Notification categories used for filtering and iconography.</summary>
public enum NotificationType
{
    OrderCreated = 0,
    OrderConfirmed = 1,
    OrderShipped = 2,
    OrderDelivered = 3,
    OrderCancelled = 4,
    PaymentSuccessful = 5,
    PaymentFailed = 6,
    RefundRequested = 7,
    RefundApproved = 8,
    RefundRejected = 9,
    SellerApproved = 10,
    SellerRejected = 11,
    SellerSuspended = 12,
    ProductApproved = 13,
    ProductRejected = 14,
    LowStock = 15,
    NewReview = 16,
    SystemNotification = 17
}

/// <summary>Audience of an audit entry.</summary>
public enum AuditAction
{
    Login = 0,
    LoginFailed = 1,
    Logout = 2,
    Register = 3,
    PasswordChanged = 4,
    RoleChanged = 5,
    UserStatusChanged = 6,
    SellerApplied = 7,
    SellerApproved = 8,
    SellerRejected = 9,
    SellerSuspended = 10,
    SellerResumed = 11,
    StoreUpdated = 12,
    CategoryCreated = 13,
    CategoryUpdated = 14,
    CategoryDeleted = 15,
    ProductCreated = 16,
    ProductUpdated = 17,
    ProductDeleted = 18,
    ProductApproved = 19,
    ProductRejected = 20,
    ProductFeatured = 21,
    InventoryAdjusted = 22,
    OrderCreated = 23,
    OrderStatusChanged = 24,
    OrderCancelled = 25,
    PaymentCreated = 26,
    PaymentVerified = 27,
    PaymentFailed = 28,
    WebhookReceived = 29,
    RefundRequested = 30,
    RefundApproved = 31,
    RefundRejected = 32,
    CommissionCreated = 33,
    CommissionReversed = 34,
    CommissionPaid = 35,
    ReviewCreated = 36,
    ReviewUpdated = 37,
    ReviewDeleted = 38,
    ReviewModerated = 39,
    CouponCreated = 40,
    CouponUpdated = 41,

    /// <summary>
    /// Retired. Stopping a coupon does not remove it, so this said something untrue; rows already
    /// written keep the name they were written with, and new ones use
    /// <see cref="CouponDeactivated"/>.
    /// </summary>
    CouponDeleted = 42,

    CouponDeactivated = 47,
    SettingsUpdated = 43,
    ReportExported = 44,
    PasswordResetRequested = 45,
    PasswordResetCompleted = 46,

    /// <summary>
    /// A seller record's business identity, contact details, tax or bank information was edited.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="SellerApproved"/> on purpose: a profile edit is not an approval,
    /// and recording one as the other made the audit log claim moderators had approved sellers who
    /// had simply corrected their address.
    /// </remarks>
    SellerProfileUpdated = 48,

    /// <summary>An administrator changed an account's role.</summary>
    UserRoleChanged = 49
}

/// <summary>Audience grouping of a notification.</summary>
public enum NotificationAudience
{
    Customer = 0,
    Seller = 1,
    Admin = 2
}

/// <summary>Time granularity used by analytics endpoints.</summary>
public enum AnalyticsInterval
{
    Day = 0,
    Week = 1,
    Month = 2,
    Quarter = 3,
    Year = 4
}
