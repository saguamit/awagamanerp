namespace Awagaman.Api.Helpers;

internal static class ChallanNumberFormatter
{
    public static string Normalize(string? input, DateTime challanDate)
    {
        var raw = (input ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        var leadingPart = raw.Split('/')[0];
        var digits = new string(leadingPart.Where(char.IsDigit).ToArray());

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

    public static int GetFinancialYearStart(DateTime challanDate)
    {
        return challanDate.Month >= 4 ? challanDate.Year : challanDate.Year - 1;
    }

    public static int GetFinancialYearStart(string? input, DateTime fallbackDate)
    {
        var raw = (input ?? string.Empty).Trim();
        var slashIndex = raw.IndexOf('/');
        if (slashIndex >= 0 && slashIndex + 1 < raw.Length)
        {
            var suffix = raw[(slashIndex + 1)..].Trim();
            var digits = new string(suffix.TakeWhile(char.IsDigit).ToArray());
            if (digits.Length >= 2 && int.TryParse(digits[..2], out var shortYear))
            {
                return shortYear >= 50 ? 1900 + shortYear : 2000 + shortYear;
            }
        }

        return GetFinancialYearStart(fallbackDate);
    }

    private static string GetFinancialYearSuffix(DateTime challanDate)
    {
        var startYear = GetFinancialYearStart(challanDate);
        var endYear = startYear + 1;
        return $"{startYear % 100:00}-{endYear % 100:00}";
    }
}
