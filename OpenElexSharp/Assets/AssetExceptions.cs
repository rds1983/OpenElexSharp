namespace OpenElexSharp.Assets;

/// <summary>The configured ELEX installation could not be found or used.</summary>
public sealed class ElexInstallationException : Exception
{
    public ElexInstallationException(string message)
        : base(message)
    {
    }

    public ElexInstallationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A mount configuration file is malformed or uses an unsupported feature.</summary>
public sealed class MountConfigurationException : Exception
{
    public MountConfigurationException(string message)
        : base(message)
    {
    }

    public MountConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A requested asset does not exist in any mounted source.</summary>
public sealed class AssetNotFoundException : Exception
{
    public AssetNotFoundException(string message)
        : base(message)
    {
    }
}
