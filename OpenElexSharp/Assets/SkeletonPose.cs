using System.Numerics;

namespace OpenElexSharp.Assets;

/// <summary>
/// A complete skeleton pose with local translations, rotations and parent indices, plus world-matrix
/// composition for rendering and validation.
/// </summary>
public sealed class SkeletonPose
{
	private readonly Vector3[] _positions;
	private readonly Quaternion[] _rotations;
	private readonly int[] _parents;

	/// <summary>Creates a pose from local transforms and parent indices.</summary>
	/// <exception cref="ArgumentNullException">A list argument is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The lists have different lengths or an invalid parent.</exception>
	public SkeletonPose(
		IReadOnlyList<Vector3> positions,
		IReadOnlyList<Quaternion> rotations,
		IReadOnlyList<int> parents)
	{
		ArgumentNullException.ThrowIfNull(positions);
		ArgumentNullException.ThrowIfNull(rotations);
		ArgumentNullException.ThrowIfNull(parents);
		if (positions.Count != rotations.Count || rotations.Count != parents.Count)
		{
			throw new ArgumentException("Positions, rotations and parents must have the same length.");
		}

		_positions = [.. positions];
		_rotations = [.. rotations];
		_parents = [.. parents];
		for (var index = 0; index < _parents.Length; index++)
		{
			if (_parents[index] < -1 || _parents[index] >= _parents.Length ||
				_parents[index] == index)
			{
				throw new ArgumentException(
					$"Bone {index} has invalid parent {_parents[index]}.");
			}
		}
	}

	/// <summary>Local translations in bone order.</summary>
	public IReadOnlyList<Vector3> Positions => _positions;

	/// <summary>Local rotations in bone order.</summary>
	public IReadOnlyList<Quaternion> Rotations => _rotations;

	/// <summary>Parent index per bone; <c>-1</c> for roots.</summary>
	public IReadOnlyList<int> Parents => _parents;

	/// <summary>
	/// Creates a pose for a decoded skeleton and a retargeted motion pose. Bind-pose translations are
	/// used when the skeleton stores them; otherwise all local translations are zero.
	/// </summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The pose and skeleton bone counts differ.</exception>
	public static SkeletonPose FromMotionPose(MotionPose pose, Skeleton skeleton)
	{
		ArgumentNullException.ThrowIfNull(pose);
		ArgumentNullException.ThrowIfNull(skeleton);
		if (pose.Count != skeleton.Names.Count)
		{
			throw new ArgumentException("The pose must have one rotation per skeleton bone.", nameof(pose));
		}

		var count = skeleton.Names.Count;
		var positions = new Vector3[count];
		if (skeleton.Bones.Count == count)
		{
			for (var index = 0; index < count; index++)
			{
				positions[index] = skeleton.Bones[index].Position;
			}
		}

		var parents = new int[count];
		parents[0] = -1;
		for (var index = 1; index < count; index++)
		{
			var parent = index - 1 < skeleton.ParentIndices.Count
				? (int)skeleton.ParentIndices[index - 1]
				: index - 1;
			parents[index] = parent >= 0 && parent < count && parent != index ? parent : -1;
		}

		return new SkeletonPose(positions, pose.Rotations, parents);
	}

	/// <summary>Composes local transforms into world matrices for every bone.</summary>
	/// <exception cref="InvalidOperationException">The parent graph contains a cycle.</exception>
	public Matrix4x4[] ComputeWorldMatrices() =>
		ComputeWorldMatrices(Matrix4x4.Identity, removeRootTranslation: false);

	/// <summary>
	/// Composes local transforms with an object (NPC/world) matrix. When
	/// <paramref name="removeRootTranslation"/> is set, the root bone's X/Z offset is removed before the
	/// object matrix is applied, matching the reference NPC path that keeps the character at the object
	/// origin while preserving its vertical offset.
	/// </summary>
	/// <exception cref="InvalidOperationException">The parent graph contains a cycle.</exception>
	public Matrix4x4[] ComputeWorldMatrices(Matrix4x4 objectMatrix, bool removeRootTranslation)
	{
		var world = new Matrix4x4[_rotations.Length];
		var state = new byte[world.Length];
		var baseTranslation = Vector3.Zero;
		if (removeRootTranslation)
		{
			var root = Array.FindIndex(_parents, parent => parent < 0);
			if (root >= 0)
			{
				baseTranslation = new Vector3(-_positions[root].X, 0f, -_positions[root].Z);
			}
		}

		for (var index = 0; index < world.Length; index++)
		{
			ComputeWorld(index, world, state, objectMatrix, baseTranslation);
		}

		return world;
	}

	private void ComputeWorld(
		int index,
		Matrix4x4[] world,
		byte[] state,
		Matrix4x4 objectMatrix,
		Vector3 baseTranslation)
	{
		if (state[index] == 2)
		{
			return;
		}

		if (state[index] == 1)
		{
			throw new InvalidOperationException($"Bone hierarchy contains a cycle at {index}.");
		}

		state[index] = 1;
		var parent = _parents[index];
		var position = parent < 0 ? _positions[index] + baseTranslation : _positions[index];
		var local = Matrix4x4.CreateFromQuaternion(_rotations[index]) *
			Matrix4x4.CreateTranslation(position);
		if (parent < 0)
		{
			world[index] = local * objectMatrix;
		}
		else
		{
			ComputeWorld(parent, world, state, objectMatrix, baseTranslation);
			world[index] = local * world[parent];
		}

		state[index] = 2;
	}
}
