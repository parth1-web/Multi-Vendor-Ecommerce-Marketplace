using Marketplace.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).HasColumnType("varchar(256)").IsRequired();
        builder.Property(u => u.PasswordHash).HasColumnType("varchar(512)").IsRequired();
        builder.Property(u => u.FirstName).HasColumnType("varchar(100)").IsRequired();
        builder.Property(u => u.LastName).HasColumnType("varchar(100)").IsRequired();
        builder.Property(u => u.PhoneNumber).HasColumnType("varchar(32)");
        builder.Property(u => u.AvatarUrl).HasColumnType("varchar(512)");
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName("ux_users_email");
        builder.HasIndex(u => u.Role).HasDatabaseName("ix_users_role");

        builder.Ignore(u => u.FullName);

        // There is deliberately no User.SellerId column. The link between an account and
        // its seller record is Seller.UserId, which keeps the two tables free of a
        // circular foreign key and removes any insert-ordering requirement.
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).HasColumnType("varchar(128)").IsRequired();
        builder.Property(t => t.FamilyId).IsRequired();
        builder.Property(t => t.RevokedReason).HasColumnType("varchar(100)");
        builder.Property(t => t.CreatedByIp).HasColumnType("varchar(64)");
        builder.Property(t => t.UserAgent).HasColumnType("varchar(400)");

        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_hash");
        builder.HasIndex(t => new { t.UserId, t.FamilyId }).HasDatabaseName("ix_refresh_tokens_family");
        builder.HasIndex(t => t.ExpiresAt).HasDatabaseName("ix_refresh_tokens_expires");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).HasColumnType("varchar(128)").IsRequired();
        builder.Property(t => t.CreatedByIp).HasColumnType("varchar(64)");

        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ux_password_reset_tokens_hash");
        builder.HasIndex(t => new { t.UserId, t.UsedAt }).HasDatabaseName("ix_password_reset_tokens_user");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UserAddressConfiguration : IEntityTypeConfiguration<UserAddress>
{
    public void Configure(EntityTypeBuilder<UserAddress> builder)
    {
        builder.ToTable("user_addresses");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Label).HasColumnType("varchar(60)").IsRequired();
        builder.Property(a => a.RecipientName).HasColumnType("varchar(120)").IsRequired();
        builder.Property(a => a.PhoneNumber).HasColumnType("varchar(32)").IsRequired();
        builder.Property(a => a.Line1).HasColumnType("varchar(200)").IsRequired();
        builder.Property(a => a.Line2).HasColumnType("varchar(200)");
        builder.Property(a => a.City).HasColumnType("varchar(100)").IsRequired();
        builder.Property(a => a.State).HasColumnType("varchar(100)");
        builder.Property(a => a.PostalCode).HasColumnType("varchar(20)").IsRequired();
        builder.Property(a => a.Country).HasColumnType("char(2)").IsRequired();

        builder.HasIndex(a => new { a.UserId, a.IsDefault }).HasDatabaseName("ix_user_addresses_user_default");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
