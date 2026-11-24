namespace OpenElexSharp.Assets;

/// <summary>
/// Reads <c>GEC2</c> class blocks from <c>RT02</c> resource databases. Databases are cached by
/// virtual path because every mesh or material lookup needs the same <c>w_*.db</c> file.
/// </summary>
public sealed class ResourceClassBlockReader : IDisposable
{
	private readonly AssetFileSystem _assets;
	private readonly Dictionary<string, byte[]> _databases = new(StringComparer.OrdinalIgnoreCase);
	private bool _disposed;

	/// <summary>Creates a reader over one asset file system.</summary>
	public ResourceClassBlockReader(AssetFileSystem assets)
	{
		ArgumentNullException.ThrowIfNull(assets);
		_assets = assets;
	}

	/// <summary>Reads the class block of a resolved resource.</summary>
	/// <exception cref="ArgumentNullException">The resource is <see langword="null"/>.</exception>
	/// <exception cref="Gar5FormatException">The class block is missing or truncated.</exception>
	public byte[] Read(ElexResource resource)
	{
		ArgumentNullException.ThrowIfNull(resource);
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (resource.ClassBlockOffset <= 0 || resource.ClassBlockSize <= 0)
		{
			throw new Gar5FormatException($"Resource '{resource.Name}' has no class block.");
		}

		var database = GetDatabase(resource.DatabasePath);
		if (resource.ClassBlockOffset + resource.ClassBlockSize > database.Length)
		{
			throw new Gar5FormatException($"Resource '{resource.Name}' has a truncated class block.");
		}

		return database.AsSpan(resource.ClassBlockOffset, resource.ClassBlockSize).ToArray();
	}

	private byte[] GetDatabase(string virtualPath)
	{
		if (_databases.TryGetValue(virtualPath, out var database))
		{
			return database;
		}

		using var stream = _assets.OpenRead(virtualPath);
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		database = buffer.ToArray();
		_databases[virtualPath] = database;
		return database;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_disposed = true;
		_databases.Clear();
	}
}
