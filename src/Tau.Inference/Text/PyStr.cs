namespace Tau.Inference.Text;

/// <summary>Python <c>str</c> helpers the reference runtimes rely on, where .NET's behaviour differs.</summary>
public static class PyStr
{
    /// <summary>
    /// Python's <c>str.isspace()</c> for one character: bidirectional class WS, B or S, or category Zs. This
    /// includes U+001C–U+001F, which .NET's <see cref="char.IsWhiteSpace(char)"/> does not.
    /// </summary>
    /// <param name="c">Character.</param>
    public static bool IsSpace(char c) => (int)c switch
    {
        0x09 or 0x0A or 0x0B or 0x0C or 0x0D or 0x1C or 0x1D or 0x1E or 0x1F or 0x20 => true,
        0x85 or 0xA0 or 0x1680 or 0x2028 or 0x2029 or 0x202F or 0x205F or 0x3000 => true,
        >= 0x2000 and <= 0x200A => true,
        _ => false,
    };

    /// <summary>Python's <c>str.strip()</c> with no arguments.</summary>
    /// <param name="s">Text.</param>
    public static string Strip(string s)
    {
        int start = 0, end = s.Length;
        while (start < end && IsSpace(s[start])) start++;
        while (end > start && IsSpace(s[end - 1])) end--;
        return s[start..end];
    }

    /// <summary>Python truthiness of a string: non-empty.</summary>
    /// <param name="s">Text or null.</param>
    public static bool Truthy(string? s) => !string.IsNullOrEmpty(s);
}
