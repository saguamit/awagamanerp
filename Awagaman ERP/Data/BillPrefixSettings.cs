using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Awagaman_ERP.Data
{
    internal static class BillPrefixSettings
    {
        private static readonly Regex FyRegex = new Regex(@"(\d{2})-(\d{2})$", RegexOptions.Compiled);
        public static string DefaultPrefix => "FBD " + GetCurrentFinancialYearLabel();
        private static readonly string SettingsPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Awagaman ERP", "bill_prefix.txt");

        public static string GetPrefix()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return DefaultPrefix;
                var value = (File.ReadAllText(SettingsPath) ?? string.Empty).Trim();
                var normalized = Normalize(value);
                // Persist updated FY text if needed.
                if (!string.Equals(normalized, value, StringComparison.Ordinal))
                {
                    SavePrefix(normalized);
                }
                return normalized;
            }
            catch
            {
                return DefaultPrefix;
            }
        }

        public static void SavePrefix(string prefix)
        {
            var normalized = Normalize(prefix);
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, normalized);
        }

        private static string Normalize(string prefix)
        {
            var value = (prefix ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value)) value = DefaultPrefix;
            while (value.EndsWith("/", StringComparison.Ordinal)) value = value.Substring(0, value.Length - 1).TrimEnd();
            if (string.IsNullOrWhiteSpace(value)) value = DefaultPrefix;

            var fy = GetCurrentFinancialYearLabel();
            if (FyRegex.IsMatch(value))
            {
                // Replace any trailing FY label with current FY.
                value = FyRegex.Replace(value, fy);
            }
            else
            {
                // Ensure FY is present for automatic rollover.
                value = value + " " + fy;
            }

            return value.Trim();
        }

        private static string GetCurrentFinancialYearLabel()
        {
            var today = DateTime.Today;
            var startYear = today.Month >= 4 ? today.Year : today.Year - 1;
            var endYear = startYear + 1;
            return (startYear % 100).ToString("D2") + "-" + (endYear % 100).ToString("D2");
        }
    }
}
