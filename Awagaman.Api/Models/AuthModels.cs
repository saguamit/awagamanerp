namespace Awagaman.Api.Models;

public sealed class AppUserEntry
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = "Operator";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginUtc { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public string PasswordPreview { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public AppUserInfo User { get; set; } = new AppUserInfo();
}

public sealed class AppUserInfo
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = "Operator";
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginUtc { get; set; }
}

public sealed class UserPasswordPreviewResponse
{
    public string Password { get; set; } = string.Empty;
    public bool Available { get; set; }
}

public sealed class CreateUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = "Operator";
}

public sealed class ResetPasswordRequest
{
    public string Password { get; set; } = string.Empty;
}

public sealed class UpdateUserStatusRequest
{
    public bool IsActive { get; set; }
}

public sealed class AuthenticatedUser
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = "Operator";
    public bool IsActive { get; set; } = true;
    public DateTime ExpiresUtc { get; set; }
}

public sealed class AuditLogEntry
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string ActionArea { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string EntityKey { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AuditUserSummaryEntry
{
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int AddedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int DeletedCount { get; set; }
    public DateTime? LastActivityUtc { get; set; }
}
