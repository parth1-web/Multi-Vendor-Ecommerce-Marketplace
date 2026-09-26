using Marketplace.Domain.Commissions;
using Marketplace.Domain.Coupons;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;
using Marketplace.Domain.Reviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.OrderNumber).HasColumnType("varchar(40)").IsRequired();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(o => o.Subtotal).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.DiscountAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.ShippingAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.TaxAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.TotalAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.RefundedAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(o => o.PaymentMethod).HasColumnType("varchar(40)").IsRequired();
        builder.Property(o => o.CouponCode).HasColumnType("varchar(50)");
        builder.Property(o => o.CustomerNote).HasColumnType("varchar(1000)");
        builder.Property(o => o.CancellationReason).HasColumnType("varchar(500)");
        builder.ConfigureRowVersion();

        // The address is snapshotted into its own table so later edits to the address book
        // can never rewrite what was shipped.
        builder.OwnsOne(o => o.ShippingAddressSnapshot, address =>
        {
            address.ToTable("order_address_snapshots");
            address.WithOwner().HasForeignKey("OrderId");
            address.Property(a => a.Label).HasColumnType("varchar(60)").IsRequired();
            address.Property(a => a.RecipientName).HasColumnType("varchar(120)").IsRequired();
            address.Property(a => a.PhoneNumber).HasColumnType("varchar(32)").IsRequired();
            address.Property(a => a.Line1).HasColumnType("varchar(200)").IsRequired();
            address.Property(a => a.Line2).HasColumnType("varchar(200)");
            address.Property(a => a.City).HasColumnType("varchar(100)").IsRequired();
            address.Property(a => a.State).HasColumnType("varchar(100)");
            address.Property(a => a.PostalCode).HasColumnType("varchar(20)").IsRequired();
            address.Property(a => a.Country).HasColumnType("char(2)").IsRequired();
        });

        builder.Ignore(o => o.IsCancelled);
        builder.Ignore(o => o.IsRefundable);
        builder.Ignore(o => o.RefundableAmount);
        builder.Ignore(o => o.SellerOrders);
        builder.Ignore(o => o.Items);
        builder.Ignore(o => o.History);

        builder.HasIndex(o => o.OrderNumber).IsUnique().HasDatabaseName("ux_orders_number");
        builder.HasIndex(o => new { o.CustomerId, o.PlacedAt }).HasDatabaseName("ix_orders_customer");
        builder.HasIndex(o => new { o.Status, o.PlacedAt }).HasDatabaseName("ix_orders_status");

        builder.HasMany(o => o.SellerOrders)
            .WithOne(so => so.Order!)
            .HasForeignKey(so => so.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.SellerOrders).HasField("_sellerOrders").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(o => o.Items)
            .WithOne(i => i.Order!)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(o => o.History)
            .WithOne()
            .HasForeignKey(h => h.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.History).HasField("_history").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class SellerOrderConfiguration : IEntityTypeConfiguration<SellerOrder>
{
    public void Configure(EntityTypeBuilder<SellerOrder> builder)
    {
        builder.ToTable("seller_orders");
        builder.HasKey(so => so.Id);

        builder.Property(so => so.SellerOrderNumber).HasColumnType("varchar(48)").IsRequired();
        builder.Property(so => so.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(so => so.Subtotal).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(so => so.DiscountAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(so => so.ShippingAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(so => so.TaxAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(so => so.TotalAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(so => so.CommissionRate).HasColumnType("numeric(5,2)").IsRequired();
        builder.Property(so => so.CommissionAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(so => so.SellerEarnings).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(so => so.CarrierName).HasColumnType("varchar(100)");
        builder.Property(so => so.TrackingNumber).HasColumnType("varchar(100)");
        builder.Property(so => so.TrackingUrl).HasColumnType("varchar(512)");
        builder.Property(so => so.CancellationReason).HasColumnType("varchar(500)");
        builder.Property(so => so.SellerNote).HasColumnType("varchar(1000)");
        builder.ConfigureRowVersion();

        builder.HasIndex(so => so.SellerOrderNumber).IsUnique().HasDatabaseName("ux_seller_orders_number");
        builder.HasIndex(so => new { so.SellerId, so.Status, so.CreatedAt }).HasDatabaseName("ix_seller_orders_seller");
        builder.HasIndex(so => so.OrderId).HasDatabaseName("ix_seller_orders_order");

        builder.HasMany(so => so.Items)
            .WithOne(i => i.SellerOrder!)
            .HasForeignKey(i => i.SellerOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(so => so.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(so => so.History)
            .WithOne()
            .HasForeignKey(h => h.SellerOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(so => so.History).HasField("_history").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.ProductName).HasColumnType("varchar(200)").IsRequired();
        builder.Property(i => i.ProductImage).HasColumnType("varchar(512)");
        builder.Property(i => i.VariantName).HasColumnType("varchar(200)").IsRequired();
        builder.Property(i => i.Sku).HasColumnType("varchar(64)").IsRequired();
        builder.Property(i => i.UnitPrice).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(i => i.LineTotal).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(i => i.DiscountAmount).HasColumnType("numeric(18,2)").IsRequired();

        builder.HasIndex(i => new { i.OrderId, i.SellerId }).HasDatabaseName("ix_order_items_order_seller");
        builder.HasIndex(i => i.ProductId).HasDatabaseName("ix_order_items_product");

        builder.HasOne(i => i.Order!)
            .WithMany(o => o.Items)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.SellerOrder!)
            .WithMany(so => so.Items)
            .HasForeignKey(i => i.SellerOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable("order_status_history");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.OrderNumber).HasColumnType("varchar(40)").IsRequired();
        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(h => h.Note).HasColumnType("varchar(500)");

        builder.HasIndex(h => new { h.OrderId, h.CreatedAt }).HasDatabaseName("ix_order_history_order");
    }
}

public sealed class SellerOrderStatusHistoryConfiguration : IEntityTypeConfiguration<SellerOrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<SellerOrderStatusHistory> builder)
    {
        builder.ToTable("seller_order_status_history");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.SellerOrderNumber).HasColumnType("varchar(48)").IsRequired();
        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(h => h.Note).HasColumnType("varchar(500)");

        builder.HasIndex(h => new { h.SellerOrderId, h.CreatedAt }).HasDatabaseName("ix_seller_order_history");
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Provider).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(p => p.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(p => p.TransactionReference).HasColumnType("varchar(64)").IsRequired();
        builder.Property(p => p.IdempotencyKey).HasColumnType("varchar(100)");
        builder.Property(p => p.GatewayPaymentId).HasColumnType("varchar(128)");
        builder.Property(p => p.GatewayRedirectUrl).HasColumnType("varchar(512)");
        builder.Property(p => p.FailureReason).HasColumnType("varchar(500)");
        builder.Property(p => p.RefundedAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.ConfigureRowVersion();

        builder.HasIndex(p => p.TransactionReference).IsUnique().HasDatabaseName("ux_payments_reference");
        builder.HasIndex(p => new { p.Provider, p.GatewayPaymentId }).HasDatabaseName("ix_payments_gateway");
        builder.HasIndex(p => new { p.OrderId, p.Status }).HasDatabaseName("ix_payments_order_status");
        builder.HasIndex(p => p.IdempotencyKey).HasDatabaseName("ix_payments_idempotency");

        builder.Ignore(p => p.IsSettled);
        builder.Ignore(p => p.IsCashOnDelivery);
        builder.Ignore(p => p.RefundableAmount);
        builder.Ignore(p => p.Transactions);

        builder.HasMany(p => p.Transactions)
            .WithOne()
            .HasForeignKey(t => t.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Transactions).HasField("_transactions").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.ToTable("payment_transactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(48).IsRequired();
        builder.Property(t => t.RequestPayload).HasColumnType("text");
        builder.Property(t => t.ResponsePayload).HasColumnType("text");
        builder.Property(t => t.ErrorMessage).HasColumnType("varchar(500)");

        builder.HasIndex(t => new { t.PaymentId, t.CreatedAt }).HasDatabaseName("ix_payment_tx_payment");
    }
}

public sealed class PaymentWebhookConfiguration : IEntityTypeConfiguration<PaymentWebhook>
{
    public void Configure(EntityTypeBuilder<PaymentWebhook> builder)
    {
        builder.ToTable("payment_webhooks");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Provider).HasColumnType("varchar(32)").IsRequired();
        builder.Property(w => w.ProviderEventId).HasColumnType("varchar(128)").IsRequired();
        builder.Property(w => w.Signature).HasColumnType("varchar(256)");
        builder.Property(w => w.Payload).HasColumnType("text").IsRequired();
        builder.Property(w => w.EventType).HasColumnType("varchar(64)");
        builder.Property(w => w.FailureReason).HasColumnType("varchar(300)");

        // The idempotency guarantee for webhook processing lives here.
        builder.HasIndex(w => new { w.Provider, w.ProviderEventId }).IsUnique().HasDatabaseName("ux_payment_webhooks_event");
        builder.HasIndex(w => w.ReceivedAt).HasDatabaseName("ix_payment_webhooks_received");
    }
}

public sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("refunds");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(r => r.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(r => r.Reason).HasColumnType("varchar(200)").IsRequired();
        builder.Property(r => r.Description).HasColumnType("varchar(2000)");
        builder.Property(r => r.ReviewNote).HasColumnType("varchar(1000)");
        builder.Property(r => r.RejectionReason).HasColumnType("varchar(1000)");
        builder.Property(r => r.GatewayRefundId).HasColumnType("varchar(128)");
        builder.Property(r => r.FailureReason).HasColumnType("varchar(500)");
        builder.ConfigureRowVersion();

        builder.HasIndex(r => new { r.Status, r.RequestedAt }).HasDatabaseName("ix_refunds_status");
        builder.HasIndex(r => r.CustomerId).HasDatabaseName("ix_refunds_customer");

        builder.Ignore(r => r.IsFinal);
        builder.Ignore(r => r.Items);

        builder.HasMany(r => r.Items)
            .WithOne()
            .HasForeignKey(i => i.RefundId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class RefundItemConfiguration : IEntityTypeConfiguration<RefundItem>
{
    public void Configure(EntityTypeBuilder<RefundItem> builder)
    {
        builder.ToTable("refund_items");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.ProductName).HasColumnType("varchar(200)").IsRequired();
        builder.Property(i => i.Sku).HasColumnType("varchar(64)").IsRequired();
        builder.Property(i => i.Amount).HasColumnType("numeric(18,2)").IsRequired();

        // One refund request per order item, enforced by the database.
        builder.HasIndex(i => i.OrderItemId).IsUnique().HasDatabaseName("ux_refund_items_order_item");
    }
}

public sealed class CommissionConfiguration : IEntityTypeConfiguration<Commission>
{
    public void Configure(EntityTypeBuilder<Commission> builder)
    {
        builder.ToTable("commissions");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Rate).HasColumnType("numeric(5,2)").IsRequired();
        builder.Property(c => c.GrossAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(c => c.CommissionAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(c => c.SellerAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(c => c.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(c => c.ReversalReason).HasColumnType("varchar(300)");

        builder.HasIndex(c => c.SellerOrderId).IsUnique().HasDatabaseName("ux_commissions_seller_order");
        builder.HasIndex(c => new { c.SellerId, c.Status, c.CreatedAt }).HasDatabaseName("ix_commissions_seller");
        builder.HasIndex(c => c.OrderId).HasDatabaseName("ix_commissions_order");
    }
}

public sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Title).HasColumnType("varchar(150)").IsRequired();
        builder.Property(r => r.Body).HasColumnType("text").IsRequired();
        builder.Property(r => r.ModerationNote).HasColumnType("varchar(500)");

        // One review per purchased order item, enforced by the database.
        builder.HasIndex(r => r.OrderItemId).IsUnique().HasDatabaseName("ux_reviews_order_item");
        builder.HasIndex(r => new { r.ProductId, r.IsVisible, r.CreatedAt }).HasDatabaseName("ix_reviews_product");
        builder.HasIndex(r => r.SellerId).HasDatabaseName("ix_reviews_seller");

        builder.HasOne(r => r.Reply!)
            .WithOne()
            .HasForeignKey<ReviewReply>(x => x.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ReviewReplyConfiguration : IEntityTypeConfiguration<ReviewReply>
{
    public void Configure(EntityTypeBuilder<ReviewReply> builder)
    {
        builder.ToTable("review_replies");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Body).HasColumnType("text").IsRequired();

        builder.HasIndex(r => r.ReviewId).IsUnique().HasDatabaseName("ux_review_replies_review");
    }
}

public sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable("coupons");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Scope).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(c => c.Code).HasColumnType("varchar(32)").IsRequired();
        builder.Property(c => c.Description).HasColumnType("varchar(500)").IsRequired();
        builder.Property(c => c.DiscountType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(c => c.DiscountValue).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(c => c.MinimumOrderAmount).HasColumnType("numeric(18,2)");
        builder.Property(c => c.MaximumDiscountAmount).HasColumnType("numeric(18,2)");
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.HasIndex(c => c.Code).IsUnique().HasDatabaseName("ux_coupons_code");
        builder.HasIndex(c => new { c.SellerId, c.Status }).HasDatabaseName("ix_coupons_seller");
        builder.HasIndex(c => new { c.Status, c.StartsAt, c.EndsAt }).HasDatabaseName("ix_coupons_window");

        builder.Ignore(c => c.IsGlobal);
        builder.Ignore(c => c.ProductIds);
    }
}

public sealed class CouponUsageConfiguration : IEntityTypeConfiguration<CouponUsage>
{
    public void Configure(EntityTypeBuilder<CouponUsage> builder)
    {
        builder.ToTable("coupon_usages");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.DiscountAmount).HasColumnType("numeric(18,2)").IsRequired();

        builder.HasIndex(u => new { u.CouponId, u.UserId }).HasDatabaseName("ix_coupon_usages_user");
        builder.HasIndex(u => u.OrderId).HasDatabaseName("ix_coupon_usages_order");
    }
}

public sealed class CouponProductConfiguration : IEntityTypeConfiguration<CouponProduct>
{
    public void Configure(EntityTypeBuilder<CouponProduct> builder)
    {
        builder.ToTable("coupon_products");
        builder.HasKey(cp => new { cp.CouponId, cp.ProductId });
    }
}
