namespace Salryn.Core.Models;

public sealed class Role
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; init; }

    public bool IsSystemRole { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class User
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Username { get; init; }

    public required string DisplayName { get; init; }

    public required string PasswordHash { get; init; }

    public Guid RoleId { get; init; }

    public UserStatus Status { get; init; } = UserStatus.Active;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed class StoreSettings
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string StoreName { get; init; }

    public string? BusinessName { get; init; }

    public string? Address { get; init; }

    public string? ContactNumber { get; init; }

    public string? Tin { get; init; }

    public string CurrencyLabel { get; init; } = "PHP";

    public int ReceiptWidthMm { get; init; } = 80;

    public string? ReceiptHeader { get; init; }

    public string? ReceiptFooter { get; init; }

    public bool TaxModeEnabled { get; init; }

    public bool ShiftModeEnabled { get; init; } = true;

    public bool AllowNegativeStock { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; init; }
}
