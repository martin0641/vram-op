using System.Net;

namespace VramOp;

internal static class InputRules
{
    public static bool IsDisplayNameChar(char value) =>
        char.IsLetterOrDigit(value)
        || value is ' ' or '-' or '_' or '.' or '(' or ')';

    public static bool IsHostChar(char value) =>
        char.IsLetterOrDigit(value)
        || value is '-' or '.' or ':' or '[' or ']';

    public static bool IsBasicAuthUsernameChar(char value) =>
        char.IsLetterOrDigit(value)
        || value is '-' or '_' or '.' or '@';

    public static bool IsPasswordChar(char value) =>
        !char.IsControl(value);

    public static bool IsCertificateThumbprintChar(char value) =>
        Uri.IsHexDigit(value)
        || value is ':' or ' ';

    public static string Filter(string value, int maxLength, Func<char, bool> isAllowed)
    {
        var filtered = new string(value.Where(isAllowed).Take(maxLength).ToArray());
        return filtered;
    }

    public static string NormalizeHost(string value) =>
        value.Trim();

    public static string NormalizeBasicAuthUsername(string value) =>
        Filter(value.Trim(), 64, IsBasicAuthUsernameChar);

    public static string NormalizeDisplayName(string value) =>
        Filter(value.Trim(), 64, IsDisplayNameChar);

    public static string NormalizePassword(string value) =>
        Filter(value, 128, IsPasswordChar);

    public static string NormalizeThumbprint(string value) =>
        new(value.Where(Uri.IsHexDigit).Take(64).Select(char.ToUpperInvariant).ToArray());

    public static bool IsValidHost(string host)
    {
        host = host.Trim();
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253)
        {
            return false;
        }

        if (IPAddress.TryParse(host.Trim('[', ']'), out _))
        {
            return true;
        }

        return Uri.CheckHostName(host) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
    }

    public static bool IsValidThumbprint(string value)
    {
        var normalized = NormalizeThumbprint(value);
        return normalized.Length == 0 || normalized.Length == 64;
    }
}
