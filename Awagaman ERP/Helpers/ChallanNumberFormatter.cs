using System;
using System.Linq;

namespace Awagaman_ERP.Helpers
{
    internal static class ChallanNumberFormatter
    {
        public static string Normalize(string input, DateTime challanDate)
        {
            var raw = (input ?? string.Empty).Trim();
            if (raw.Length == 0)
            {
                return string.Empty;
            }

            var digits = new string(raw
                .TakeWhile(c => c != '/')
                .Where(char.IsDigit)
                .ToArray());

            if (digits.Length == 0)
            {
                digits = new string(raw.Where(char.IsDigit).ToArray());
            }

            if (digits.Length == 0)
            {
                return raw;
            }

            return $"{digits.PadLeft(Math.Max(3, digits.Length), '0')}/{GetFinancialYearSuffix(challanDate)}";
        }

        public static int GetSequence(string input)
        {
            var raw = (input ?? string.Empty).Trim();
            if (raw.Length == 0)
            {
                return 0;
            }

            var digits = new string(raw
                .TakeWhile(c => c != '/')
                .Where(char.IsDigit)
                .ToArray());

            return int.TryParse(digits, out var sequence) ? sequence : 0;
        }

        public static int GetFinancialYearStart(DateTime challanDate)
        {
            return challanDate.Month >= 4 ? challanDate.Year : challanDate.Year - 1;
        }

        public static int GetFinancialYearStart(string input, DateTime fallbackDate)
        {
            var raw = (input ?? string.Empty).Trim();
            var slashIndex = raw.IndexOf('/');
            if (slashIndex >= 0 && slashIndex + 1 < raw.Length)
            {
                var suffix = raw.Substring(slashIndex + 1).Trim();
                var digits = new string(suffix.TakeWhile(char.IsDigit).ToArray());
                if (digits.Length >= 2 && int.TryParse(digits.Substring(0, 2), out var shortYear))
                {
                    return shortYear >= 50 ? 1900 + shortYear : 2000 + shortYear;
                }
            }

            return GetFinancialYearStart(fallbackDate);
        }

        public static string GetFinancialYearSuffix(DateTime challanDate)
        {
            var startYear = GetFinancialYearStart(challanDate);
            var endYear = startYear + 1;
            return $"{startYear % 100:00}-{endYear % 100:00}";
        }
    }
}
