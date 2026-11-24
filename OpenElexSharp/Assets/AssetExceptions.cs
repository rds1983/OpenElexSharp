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

/// <summary>A PAK archive is malformed or uses an unsupported variant.</summary>
public sealed class PakFormatException : Exception
{
	public PakFormatException(string message)
		: base(message)
	{
	}

	public PakFormatException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}

/// <summary>A GAR5 resource wrapper or resource database is malformed.</summary>
public sealed class Gar5FormatException : Exception
{
	public Gar5FormatException(string message)
		: base(message)
	{
	}

	public Gar5FormatException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}

/// <summary>A DirectDraw Surface (DDS) texture is malformed or uses an unsupported format.</summary>
public sealed class DdsFormatException : Exception
{
	public DdsFormatException(string message)
		: base(message)
	{
	}

	public DdsFormatException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}

/// <summary>A GMSH mesh resource is malformed or uses an unsupported variant.</summary>
public sealed class GmshFormatException : Exception
{
	public GmshFormatException(string message)
		: base(message)
	{
	}

	public GmshFormatException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}

/// <summary>A Genome GEC2 object or DT01 document is malformed.</summary>
public sealed class GenomeFormatException : Exception
{
	public GenomeFormatException(string message)
		: base(message)
	{
	}

	public GenomeFormatException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
