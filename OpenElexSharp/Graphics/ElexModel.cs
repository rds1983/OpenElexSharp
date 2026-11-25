using DigitalRiseModel;
using Microsoft.Xna.Framework;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Graphics;

/// <summary>
/// A DigitalRiseModel instance plus the draw material for every mesh part, mirroring
/// OpenGothicSharp's <c>GothicModel</c>. The materials start as the model's
/// <see cref="DrMeshPartTag"/> materials and individual entries can be replaced without copying
/// the shared geometry.
/// </summary>
public class ElexModel
{
	private DrModelInstance? _instance;

	/// <summary>Creates an empty wrapper. Assign <see cref="Instance"/> or <see cref="Model"/> next.</summary>
	public ElexModel()
	{
	}

	/// <summary>Creates a wrapper around a model by creating its instance.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
	public ElexModel(DrModel model)
	{
		ArgumentNullException.ThrowIfNull(model);
		Model = model;
	}

	/// <summary>The instance that supplies bones, skin transforms and the model itself.</summary>
	public DrModelInstance? Instance
	{
		get => _instance;
		set
		{
			if (ReferenceEquals(_instance, value))
			{
				return;
			}

			if (_instance is not null)
			{
				_instance.ModelChanged -= OnModelChanged;
			}

			_instance = value;

			if (_instance is not null)
			{
				_instance.ModelChanged += OnModelChanged;
			}

			SetMaterialsFromModel();
		}
	}

	/// <summary>The scene model, or <see langword="null"/> before one is assigned.</summary>
	public DrModel? Model
	{
		get => _instance?.Model;
		set
		{
			if (_instance is null)
			{
				Instance = new DrModelInstance();
			}

			_instance!.Model = value;
		}
	}

	/// <summary>Draw material per mesh and per part within the mesh.</summary>
	public ElexMaterial?[][] Materials { get; private set; } = [];

	/// <summary>Bounds of the current pose, or <see langword="null"/> without an instance.</summary>
	public BoundingBox? BoundingBox => _instance?.BoundingBox;

	/// <summary>Returns the material for one mesh part, or <see langword="null"/> when out of range.</summary>
	public ElexMaterial? GetMaterial(int meshIndex, int meshPartIndex)
	{
		if ((uint)meshIndex >= (uint)Materials.Length)
		{
			return null;
		}

		var row = Materials[meshIndex];
		return (uint)meshPartIndex < (uint)row.Length ? row[meshPartIndex] : null;
	}

	/// <summary>Re-reads the default materials from the current model.</summary>
	public void SetMaterialsFromModel() => Materials = CreateMaterials(_instance?.Model);

	private void OnModelChanged(object? sender, EventArgs e) => SetMaterialsFromModel();

	/// <summary>
	/// Builds the default material matrix from a model's mesh parts. A part without a
	/// <see cref="DrMeshPartTag"/> material stays <see langword="null"/>; the renderer then falls
	/// back to its own texture.
	/// </summary>
	internal static ElexMaterial?[][] CreateMaterials(DrModel? model)
	{
		if (model?.Meshes is not { } meshes)
		{
			return [];
		}

		var result = new ElexMaterial?[meshes.Length][];
		for (var meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
		{
			var parts = meshes[meshIndex].MeshParts;
			var row = new ElexMaterial?[parts.Count];
			for (var partIndex = 0; partIndex < parts.Count; partIndex++)
			{
				row[partIndex] = (parts[partIndex].Tag as DrMeshPartTag)?.Material;
			}

			result[meshIndex] = row;
		}

		return result;
	}
}
