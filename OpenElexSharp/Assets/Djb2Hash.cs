using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>DJB2 hash used by ELEX resource databases to derive ROM file names.</summary>
public static class Djb2Hash
{
    /// <summary>Computes the case-insensitive DJB2 hash of a logical resource name.</summary>
    public static uint Compute(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var hash = 5381u;
        foreach (var valueByte in Encoding.Latin1.GetBytes(value.ToLowerInvariant()))
        {
            hash = unchecked((hash * 33) + valueByte);
        }

        return hash;
    }
}
