namespace OpenElexSharp.Graphics;

/// <summary>
/// Tracks mesh nodes per visual category and applies category visibility toggles. The state is
/// independent of rendering, so it can be unit-tested with plain scene nodes.
/// </summary>
public sealed class SectorVisualState
{
	private readonly Dictionary<SectorVisualCategory, List<SceneNode>> _nodes = [];
	private readonly Dictionary<SectorVisualCategory, bool> _visible = [];

	/// <summary>Creates a state with every category visible.</summary>
	public SectorVisualState()
	{
		foreach (var category in Enum.GetValues<SectorVisualCategory>())
		{
			_visible[category] = true;
		}
	}

	/// <summary>Registers a mesh node under a category and applies the current visibility.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
	public void Register(SceneNode node, SectorVisualCategory category)
	{
		ArgumentNullException.ThrowIfNull(node);
		if (category == SectorVisualCategory.Other)
		{
			return;
		}

		if (!_nodes.TryGetValue(category, out var nodes))
		{
			nodes = [];
			_nodes[category] = nodes;
		}

		nodes.Add(node);
		node.Visible = IsVisible(category);
	}

	/// <summary>Whether a visual category is currently submitted.</summary>
	public bool IsVisible(SectorVisualCategory category) =>
		_visible.TryGetValue(category, out var visible) && visible;

	/// <summary>Shows or hides every registered node in a visual category.</summary>
	public void SetVisible(SectorVisualCategory category, bool visible)
	{
		_visible[category] = visible;
		if (_nodes.TryGetValue(category, out var nodes))
		{
			foreach (var node in nodes)
			{
				node.Visible = visible;
			}
		}
	}

	/// <summary>Flips the visibility of one category and returns the new state.</summary>
	public bool Toggle(SectorVisualCategory category)
	{
		var visible = !IsVisible(category);
		SetVisible(category, visible);
		return visible;
	}

	/// <summary>Number of registered mesh nodes in a category.</summary>
	public int GetCount(SectorVisualCategory category) =>
		_nodes.TryGetValue(category, out var nodes) ? nodes.Count : 0;

	/// <summary>Compact overlay summary of the visual categories and toggle states.</summary>
	public string Summary
	{
		get
		{
			static string Toggle(bool visible) => visible ? "on" : "off";
			return
				$"lights={Toggle(IsVisible(SectorVisualCategory.Lights))}/" +
				$"{GetCount(SectorVisualCategory.Lights)} " +
				$"decals={Toggle(IsVisible(SectorVisualCategory.Decals))}/" +
				$"{GetCount(SectorVisualCategory.Decals)} " +
				$"water={Toggle(IsVisible(SectorVisualCategory.Water))}/" +
				$"{GetCount(SectorVisualCategory.Water)} " +
				$"veg={Toggle(IsVisible(SectorVisualCategory.Vegetation))}/" +
				$"{GetCount(SectorVisualCategory.Vegetation)}";
		}
	}
}
