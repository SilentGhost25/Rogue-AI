using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ControlPlane.Services;

/// <summary>
/// Normalizes source code by stripping whitespace and comments to detect purely cosmetic changes.
/// </summary>
public static class CodeNormalizer
{
    private static readonly Regex SingleLineComments = new(@"//.*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex MultiLineComments = new(@"/\*[\s\S]*?\*/", RegexOptions.Compiled);
    private static readonly Regex HtmlComments = new(@"<!--[\s\S]*?-->", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static string Normalize(string text, string relPath)
    {
        var ext = Path.GetExtension(relPath).ToLowerInvariant();
        if (ext is ".js" or ".ts" or ".css")
        {
            text = MultiLineComments.Replace(text, "");
            text = SingleLineComments.Replace(text, "");
        }
        else if (ext is ".html" or ".ejs")
        {
            text = HtmlComments.Replace(text, "");
        }
        return Whitespace.Replace(text, "");
    }

    public static string ComputeNormalizedHash(byte[] content, string relPath)
    {
        try
        {
            var text = System.Text.Encoding.UTF8.GetString(content);
            var normalized = Normalize(text, relPath);
            var normBytes = System.Text.Encoding.UTF8.GetBytes(normalized);
            return Convert.ToHexString(SHA256.HashData(normBytes));
        }
        catch
        {
            return Convert.ToHexString(SHA256.HashData(content));
        }
    }
}

