using DigitalRiseModel;
using Microsoft.Xna.Framework;

namespace OpenElexSharp.Graphics;

/// <summary>
/// One node in a row-vector transform hierarchy. A node's world transform is
/// <c>local * parentWorld</c>, matching the scene-sector matrix convention (see
/// <c>Documentation/Formats/Worlds.md</c>). Meshes are optional; a node without one only groups
/// children.
/// </summary>
public sealed class SceneNode
{
	private readonly List<SceneNode> _children = [];

	private SceneNode? _parent;
	private Matrix _local = Matrix.Identity;
	private Matrix _world = Matrix.Identity;
	private bool _worldDirty = true;
	private DrMesh? _mesh;
	private BoundingBox _localBounds;
	private bool _hasBounds;

	/// <summary>Creates a node with an identity local transform.</summary>
	/// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
	public SceneNode(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		Name = name;
	}

	/// <summary>Diagnostic name, normally the entity or mesh name.</summary>
	public string Name { get; }

	/// <summary>Parent node, or <see langword="null"/> for a root.</summary>
	public SceneNode? Parent => _parent;

	/// <summary>Direct children in insertion order.</summary>
	public IReadOnlyList<SceneNode> Children => _children;

	/// <summary>Transform relative to the parent. Setting it invalidates this subtree.</summary>
	public Matrix LocalTransform
	{
		get => _local;
		set
		{
			if (_local == value)
			{
				return;
			}

			_local = value;
			InvalidateWorld();
		}
	}

	/// <summary>Composed world transform, updated lazily.</summary>
	public Matrix WorldTransform
	{
		get
		{
			UpdateWorldSelf();
			return _world;
		}
	}

	/// <summary>Mesh drawn by this node, or <see langword="null"/>.</summary>
	public DrMesh? Mesh
	{
		get => _mesh;
		set
		{
			if (ReferenceEquals(_mesh, value))
			{
				return;
			}

			_mesh = value;
			RecomputeLocalBounds();
		}
	}

	/// <summary>Shading used when this node's mesh is submitted.</summary>
	public MeshShading Shading { get; set; } = MeshShading.Lit;

	/// <summary>Whether this node and its children are submitted.</summary>
	public bool Visible { get; set; } = true;

	/// <summary>Whether the node has a mesh with at least one part.</summary>
	public bool HasBounds => _hasBounds;

	/// <summary>Union of the mesh part boxes in local space.</summary>
	public BoundingBox LocalBounds => _localBounds;

	/// <summary>Local bounds transformed by <see cref="WorldTransform"/>.</summary>
	public BoundingBox WorldBounds => _hasBounds
		? TransformBounds(_localBounds, WorldTransform)
		: default;

	/// <summary>Number of mesh parts on this node.</summary>
	public int PartCount => _mesh?.MeshParts.Count ?? 0;

	/// <summary>Attaches <paramref name="child"/>, detaching it from any previous parent.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="child"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The child is this node or an ancestor of it.</exception>
	public void AddChild(SceneNode child)
	{
		ArgumentNullException.ThrowIfNull(child);
		if (ReferenceEquals(child, this) || IsAncestorOf(child))
		{
			throw new ArgumentException("A node cannot be added below itself.", nameof(child));
		}

		if (ReferenceEquals(child._parent, this))
		{
			return;
		}

		child._parent?._children.Remove(child);
		child._parent = this;
		_children.Add(child);
		child.InvalidateWorld();
	}

	/// <summary>Detaches <paramref name="child"/> from this node.</summary>
	public bool RemoveChild(SceneNode child)
	{
		ArgumentNullException.ThrowIfNull(child);
		if (!ReferenceEquals(child._parent, this))
		{
			return false;
		}

		_children.Remove(child);
		child._parent = null;
		child.InvalidateWorld();
		return true;
	}

	/// <summary>Forces the cached world transform and every descendant to update.</summary>
	public void UpdateWorldTransform()
	{
		UpdateWorldSelf();
		foreach (var child in _children)
		{
			child.UpdateWorldTransform();
		}
	}

	private void UpdateWorldSelf()
	{
		if (!_worldDirty)
		{
			return;
		}

		var parentWorld = _parent is null ? Matrix.Identity : _parent.WorldTransform;
		_world = _local * parentWorld;
		_worldDirty = false;
	}

	/// <summary>Visits this node and its descendants depth-first.</summary>
	public void ForEach(Action<SceneNode> action)
	{
		ArgumentNullException.ThrowIfNull(action);
		action(this);
		foreach (var child in _children)
		{
			child.ForEach(action);
		}
	}

	private bool IsAncestorOf(SceneNode node)
	{
		for (var current = _parent; current is not null; current = current._parent)
		{
			if (ReferenceEquals(current, node))
			{
				return true;
			}
		}

		return false;
	}

	private void InvalidateWorld()
	{
		_worldDirty = true;
		foreach (var child in _children)
		{
			child.InvalidateWorld();
		}
	}

	private void RecomputeLocalBounds()
	{
		_hasBounds = false;
		_localBounds = default;
		if (_mesh is null || _mesh.MeshParts.Count == 0)
		{
			return;
		}

		var bounds = _mesh.MeshParts[0].BoundingBox;
		for (var index = 1; index < _mesh.MeshParts.Count; index++)
		{
			bounds = BoundingBox.CreateMerged(bounds, _mesh.MeshParts[index].BoundingBox);
		}

		_localBounds = bounds;
		_hasBounds = true;
	}

	private static BoundingBox TransformBounds(BoundingBox bounds, Matrix transform)
	{
		var corners = bounds.GetCorners();
		for (var index = 0; index < corners.Length; index++)
		{
			corners[index] = Vector3.Transform(corners[index], transform);
		}

		return BoundingBox.CreateFromPoints(corners);
	}
}
