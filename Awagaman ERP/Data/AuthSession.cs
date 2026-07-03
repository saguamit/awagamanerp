using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    internal static class AuthSession
    {
        public static string Token { get; private set; }
        public static AppUserInfo CurrentUser { get; private set; }

        public static bool IsAuthenticated => !string.IsNullOrWhiteSpace(Token) && CurrentUser != null;
        public static bool IsAdmin => CurrentUser != null && string.Equals(CurrentUser.Role ?? string.Empty, "Admin", System.StringComparison.OrdinalIgnoreCase);

        public static void Set(LoginResponse response)
        {
            Token = response?.Token ?? string.Empty;
            CurrentUser = response?.User;
        }

        public static void Clear()
        {
            Token = string.Empty;
            CurrentUser = null;
        }
    }
}
