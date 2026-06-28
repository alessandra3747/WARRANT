using System.Text.RegularExpressions;
using Warrant.Domain;

namespace Warrant.Guardians;

public static class FindingSanitizer
{
    private static readonly Regex Email = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
    private static readonly Regex Phone = new(@"(?<!\d)(\+?\d[\d\s\-()]{6,}\d)(?!\d)", RegexOptions.Compiled);
    private static readonly Regex LongNum = new(@"\b\d{5,}\b", RegexOptions.Compiled);

    public static string Scrub(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return "";
        }

        var s = Email.Replace(detail, "[email]");
        s = Phone.Replace(s, "[phone]");
        s = LongNum.Replace(s, "[number]");

        return s.Length > 240 ? s[..240] : s;
    }

    public static Finding Clean(Finding f) => f with { Detail = Scrub(f.Detail) };
    public static IReadOnlyList<Finding> Clean(IEnumerable<Finding> fs)  => fs.Select(Clean).ToList();
}