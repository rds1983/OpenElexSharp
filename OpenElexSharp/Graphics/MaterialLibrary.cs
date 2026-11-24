using Microsoft.Xna.Framework.Graphics;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Graphics;

/// <summary>
/// Resolves material names to <see cref="ElexMaterial"/> instances and uploads their diffuse textures
/// on demand. One library owns every texture it creates and must be disposed with the scene.
/// </summary>
public sealed class MaterialLibrary : IDisposable
{
	private readonly GraphicsDevice _device;
	private readonly AssetFileSystem _assets;
	private readonly ElexResourceIndex _resources;
	private readonly ResourceClassBlockReader _classBlocks;
	private readonly Dictionary<string, ElexMaterial?> _materials = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<string> _warnings = [];
	private bool _disposed;

	/// <summary>Creates a material library over the mounted assets.</summary>
	public MaterialLibrary(GraphicsDevice device, AssetFileSystem assets, ElexResourceIndex resources)
	{
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(assets);
		ArgumentNullException.ThrowIfNull(resources);
		_device = device;
		_assets = assets;
		_resources = resources;
		_classBlocks = new ResourceClassBlockReader(assets);
	}

	/// <summary>Non-fatal material or texture problems encountered while loading.</summary>
	public IReadOnlyList<string> Warnings => _warnings;

	/// <summary>Number of materials created by this library.</summary>
	public int Count => _materials.Count;

	/// <summary>
	/// Loads a material by logical name. Failures are recorded in <see cref="Warnings"/> and return
	/// <see langword="null"/>.
	/// </summary>
	public ElexMaterial? Load(string materialName)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (string.IsNullOrWhiteSpace(materialName))
		{
			return null;
		}

		if (_materials.TryGetValue(materialName, out var cached))
		{
			return cached;
		}

		ElexMaterial? material = null;
		try
		{
			if (!_resources.TryResolve(materialName, "mat", null, out var resource))
			{
				_warnings.Add($"Material '{materialName}' was not found.");
			}
			else
			{
				material = ElexMaterial.Parse(resource.Name, ReadAll(resource.VirtualPath));
				material.Shader = TryReadShaderName(resource);
				var primary = material.PrimaryTexture;
				material.Texture = primary is null ? null : LoadTexture(primary.Name);
			}
		}
		catch (Exception exception) when (
			exception is Gar5FormatException or DdsFormatException or NotSupportedException or
				IOException or UnauthorizedAccessException or AssetNotFoundException or InvalidOperationException)
		{
			_warnings.Add($"Material '{materialName}': {exception.Message}");
		}

		_materials[materialName] = material;
		return material;
	}

	/// <summary>Loads an image resource as a 2D texture, caching the result.</summary>
	public Texture2D? LoadTexture(string imageName)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (string.IsNullOrWhiteSpace(imageName))
		{
			return null;
		}

		if (_textures.TryGetValue(imageName, out var cached))
		{
			return cached;
		}

		Texture2D? texture = null;
		try
		{
			if (!_resources.TryResolve(imageName, "img", null, out var resource))
			{
				_warnings.Add($"Texture '{imageName}' was not found.");
			}
			else
			{
				var image = DdsImage.Parse(ReadAll(resource.VirtualPath));
				var normalMap = imageName.Contains("_Nm", StringComparison.OrdinalIgnoreCase);
				texture = TextureLoader.Load(_device, image, new DdsDecodeOptions(NormalMap: normalMap));
			}
		}
		catch (Exception exception) when (
			exception is Gar5FormatException or DdsFormatException or NotSupportedException or
				IOException or UnauthorizedAccessException or AssetNotFoundException or InvalidOperationException)
		{
			_warnings.Add($"Texture '{imageName}': {exception.Message}");
		}

		_textures[imageName] = texture;
		return texture;
	}

	private string? TryReadShaderName(ElexResource resource)
	{
		try
		{
			return MaterialMetadata.Parse(_classBlocks.Read(resource)).InstanceMaterial;
		}
		catch (Exception exception) when (exception is Gar5FormatException or GenomeFormatException)
		{
			_warnings.Add($"Material metadata '{resource.Name}': {exception.Message}");
			return null;
		}
	}

	private byte[] ReadAll(string virtualPath)
	{
		using var stream = _assets.OpenRead(virtualPath);
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		return buffer.ToArray();
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		foreach (var texture in _textures.Values)
		{
			texture?.Dispose();
		}

		_textures.Clear();
		_materials.Clear();
		_classBlocks.Dispose();
	}
}
