using System;

namespace Awagaman_ERP.Models
{
    internal static class SecurityModelTimeHelper
    {
        public static DateTime? ToLocalTime(DateTime? value)
        {
            if (!value.HasValue) return null;
            var dt = value.Value;
            if (dt.Kind == DateTimeKind.Unspecified)
            {
                dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
            else if (dt.Kind == DateTimeKind.Local)
            {
                return dt;
            }

            return dt.ToLocalTime();
        }
    }

    public sealed class AppUserInfo
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }
        public bool IsActive { get; set; }
        public DateTime? LastLoginUtc { get; set; }
        public DateTime? LastLoginLocal => SecurityModelTimeHelper.ToLocalTime(LastLoginUtc);
    }

    public sealed class UserPasswordPreviewResponse
    {
        public string Password { get; set; }
        public bool Available { get; set; }
    }

    public sealed class LoginRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public sealed class LoginResponse
    {
        public string Token { get; set; }
        public AppUserInfo User { get; set; }
    }

    public sealed class CreateUserRequest
    {
        public string Username { get; set; }
        public string FullName { get; set; }
        public string Password { get; set; }
        public string Role { get; set; }
    }

    public sealed class UpdateUserStatusRequest
    {
        public bool IsActive { get; set; }
    }

    public sealed class ResetPasswordRequest
    {
        public string Password { get; set; }
    }

    public sealed class AuditLogEntry
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }
        public string ActionArea { get; set; }
        public string ActionType { get; set; }
        public string EntityKey { get; set; }
        public string Details { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime CreatedLocal => SecurityModelTimeHelper.ToLocalTime(CreatedUtc) ?? CreatedUtc;
    }

    public sealed class AuditUserSummaryEntry
    {
        public string Username { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }
        public int AddedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int DeletedCount { get; set; }
        public DateTime? LastActivityUtc { get; set; }
        public DateTime? LastActivityLocal => SecurityModelTimeHelper.ToLocalTime(LastActivityUtc);
    }
}
