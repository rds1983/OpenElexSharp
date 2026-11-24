using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>DJB2 hash used by ELEX resource databases and Genome object names.</summary>
public static class Djb2Hash
{
    /// <summary>Computes the case-insensitive DJB2 hash of a logical resource name.</summary>
    public static uint Compute(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return ComputeCore(value.ToLowerInvariant());
    }

    /// <summary>Computes the case-sensitive DJB2 hash used for Genome class and property names.</summary>
    public static uint ComputeCaseSensitive(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return ComputeCore(value);
    }

    private static uint ComputeCore(string value)
    {
        var hash = 5381u;
        foreach (var valueByte in Encoding.Latin1.GetBytes(value))
        {
            hash = unchecked((hash * 33) + valueByte);
        }

        return hash;
    }
}
