using Marketplace.Domain.Identity;
using Marketplace.Domain.Sellers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence.Configurations;

public sealed class SellerConfiguration : IEntityTypeConfiguration<Seller>
{
    public void Configure(EntityTypeBuilder<Seller> builder)
    {
        builder.ToTable("sellers");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.BusinessName).HasColumnType("varchar(200)").IsRequired();
        builder.Property(s => s.LegalName).HasColumnType("varchar(200)");
        builder.Property(s => s.PhoneNumber).HasColumnType("varchar(32)");
        builder.Property(s => s.Address).HasColumnType("varchar(500)");
        builder.Property(s => s.TaxIdentityNumber).HasColumnType("varchar(64)");
        builder.Property(s => s.BankAccountName).HasColumnType("varchar(150)");
        builder.Property(s => s.BankAccountNumber).HasColumnType("varchar(64)");
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(s => s.DefaultCommissionRate).HasColumnType("numeric(5,2)").IsRequired();
        builder.Property(s => s.RejectionReason).HasColumnType("varchar(500)");
        builder.Property(s => s.SuspensionReason).HasColumnType("varchar(500)");
        builder.ConfigureRowVersion();

        builder.HasIndex(s => s.UserId).IsUnique().HasDatabaseName("ux_sellers_user");
        builder.HasIndex(s => s.Status).HasDatabaseName("ix_sellers_status");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SellerStoreConfiguration : IEntityTypeConfiguration<SellerStore>
{
    public void Configure(EntityTypeBuilder<SellerStore> builder)
    {
        builder.ToTable("seller_stores");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasColumnType("varchar(200)").IsRequired();
        builder.Property(s => s.SlugValue).HasColumnType("varchar(160)").IsRequired();
        builder.Property(s => s.Description).HasColumnType("varchar(4000)").IsRequired();
        builder.Property(s => s.LogoUrl).HasColumnType("varchar(512)");
        builder.Property(s => s.BannerUrl).HasColumnType("varchar(512)");
        builder.Property(s => s.SupportEmail).HasColumnType("varchar(256)");
        builder.Property(s => s.SupportPhone).HasColumnType("varchar(32)");
        builder.Property(s => s.ReturnPolicy).HasColumnType("varchar(4000)");
        builder.Property(s => s.ShippingPolicy).HasColumnType("varchar(4000)");
        builder.Property(s => s.RatingAverage).HasColumnType("numeric(3,2)").IsRequired();

        builder.HasIndex(s => s.SlugValue).IsUnique().HasDatabaseName("ux_seller_stores_slug");
        builder.HasIndex(s => s.SellerId).IsUnique().HasDatabaseName("ux_seller_stores_seller");
        builder.HasIndex(s => s.RatingAverage).HasDatabaseName("ix_seller_stores_rating");

        builder.Ignore(s => s.Slug);

        builder.HasOne<Seller>()
            .WithOne()
            .HasForeignKey<SellerStore>(s => s.SellerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SellerPayoutConfiguration : IEntityTypeConfiguration<SellerPayout>
{
    public void Configure(EntityTypeBuilder<SellerPayout> builder)
    {
        builder.ToTable("seller_payouts");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Reference).HasColumnType("varchar(64)").IsRequired();
        builder.Property(p => p.GrossAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(p => p.CommissionAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(p => p.NetAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.FailureReason).HasColumnType("varchar(500)");
        builder.Property(p => p.TransactionReference).HasColumnType("varchar(128)");

        builder.HasIndex(p => p.Reference).IsUnique().HasDatabaseName("ux_seller_payouts_reference");
    builder.HasIndex(p => new { p.SellerId, p.Status }).HasDatabaseName("ix_seller_payouts_seller_status");

    // The commission report groups completed payouts by the end of the period they cover, across
    // every seller at once. The index above is led by SellerId and so cannot narrow that range; this
    // one is the one the report's WHERE clause actually uses.
    builder.HasIndex(p => p.PeriodEnd).HasDatabaseName("ix_seller_payouts_period_end");

        builder.HasOne<Seller>()
            .WithMany()
            .HasForeignKey(p => p.SellerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
