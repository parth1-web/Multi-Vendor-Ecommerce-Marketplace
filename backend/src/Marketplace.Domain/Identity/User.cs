using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Identity;

public class User : Entity
{
    private User()
    {
        Email = string.Empty;
        PasswordHash = string.Empty;
        FirstName = string.Empty;
        LastName = string.Empty;
    }

    private User(Guid id, string email, string passwordHash, string firstName, string lastName, UserRole role, string? phoneNumber, DateTimeOffset createdAt)
        : base(id)
    {
        Email = email;
        PasswordHash = passwordHash;
        FirstName = firstName;
        LastName = lastName;
        Role = role;
        PhoneNumber = phoneNumber;
        CreatedAt = createdAt;
    }

    public string Email { get; private set; }

    public string PasswordHash { get; private set; }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public string? PhoneNumber { get; private set; }

    public UserRole Role { get; private set; }

    public bool IsEmailConfirmed { get; private set; }

    public bool IsActive { get; private set; } = true;

    public string? AvatarUrl { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? SellerId { get; internal set; }

    /// <summary>Rows deleted by the user; kept soft for audit purposes.</summary>
    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public string FullName => $"{FirstName} {LastName}".Trim();

    public static User Register(
        string email,
        string passwordHash,
        string firstName,
        string lastName,
        string? phoneNumber,
        UserRole role,
        DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(email, nameof(email));
        Guard.NotNullOrWhiteSpace(passwordHash, nameof(passwordHash));
        Guard.NotNullOrWhiteSpace(firstName, nameof(firstName));
        Guard.NotNullOrWhiteSpace(lastName, nameof(lastName));

        if (role is not (UserRole.Customer or UserRole.Seller))
        {
            throw new ValidationException(nameof(role), "Self-registration is limited to Customer and Seller roles.");
        }

        return new User(SequentialGuid.New(), email.Trim().ToLowerInvariant(), passwordHash, firstName.Trim(), lastName.Trim(), role, phoneNumber?.Trim(), now);
    }

    /// <summary>Used by seeding and by admin-created accounts.</summary>
    public static User CreateAs(
        string email,
        string passwordHash,
        string firstName,
        string lastName,
        UserRole role,
        string? phoneNumber,
        DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(email, nameof(email));

        return new User(SequentialGuid.New(), email.Trim().ToLowerInvariant(), passwordHash, firstName.Trim(), lastName.Trim(), role, phoneNumber?.Trim(), now)
        {
            IsEmailConfirmed = true
        };
    }

    public void ChangePassword(string newPasswordHash, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(newPasswordHash, nameof(newPasswordHash));
        PasswordHash = newPasswordHash;
        UpdatedAt = now;
    }

    public void RecordLogin(DateTimeOffset now)
    {
        LastLoginAt = now;
        UpdatedAt = now;
    }

    public void ConfirmEmail(DateTimeOffset now)
    {
        IsEmailConfirmed = true;
        UpdatedAt = now;
    }

    public void UpdateProfile(string firstName, string lastName, string? phoneNumber, string? avatarUrl, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(firstName, nameof(firstName));
        Guard.NotNullOrWhiteSpace(lastName, nameof(lastName));

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        PhoneNumber = phoneNumber?.Trim();
        AvatarUrl = avatarUrl;
        UpdatedAt = now;
    }

    public void ChangeRole(UserRole role, DateTimeOffset now)
    {
        Role = role;
        UpdatedAt = now;
    }

    public void SetActive(bool isActive, DateTimeOffset now)
    {
        IsActive = isActive;
        UpdatedAt = now;
    }

    public void SoftDelete(DateTimeOffset now)
    {
        IsDeleted = true;
        IsActive = false;
        DeletedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Links the account to a seller record once an application has been created.</summary>
    public void AttachSeller(Guid sellerId, DateTimeOffset now)
    {
        Guard.NotEmpty(sellerId, nameof(sellerId));
        SellerId = sellerId;
        UpdatedAt = now;
    }
}
