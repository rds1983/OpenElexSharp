using Microsoft.Xna.Framework;

namespace OpenElexSharp.Graphics;

/// <summary>Decoded decal projection parameters from an <c>eCDecal2_PS</c> entity.</summary>
/// <param name="MaterialName">Material resource name, when present.</param>
/// <param name="Extends">Projection box extents in model units.</param>
/// <param name="Offset">Local offset from the entity origin.</param>
/// <param name="Rotation">Local Euler rotation in radians.</param>
public sealed record SectorDecalData(
	string? MaterialName,
	Vector3 Extends,
	Vector3 Offset,
	Vector3 Rotation);
