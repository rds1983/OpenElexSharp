namespace OpenElexSharp.Graphics;

/// <summary>Runtime visual categories used by the sector debug toggles.</summary>
public enum SectorVisualCategory
{
	/// <summary>Buildings, terrain props and other always-visible geometry.</summary>
	Other,
	/// <summary>Point-light source meshes and light proxies.</summary>
	Lights,
	/// <summary>Decal entities and their projection geometry.</summary>
	Decals,
	/// <summary>Water, lava and river surfaces.</summary>
	Water,
	/// <summary>Trees, bushes and other SpeedTree/vegetation geometry.</summary>
	Vegetation,
}
