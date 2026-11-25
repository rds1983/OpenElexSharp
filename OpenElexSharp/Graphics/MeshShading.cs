namespace OpenElexSharp.Graphics;

/// <summary>Shading applied to static mesh parts.</summary>
public enum MeshShading
{
	/// <summary>Textured and lit with the stock <c>BasicEffect</c>.</summary>
	Lit,

	/// <summary>Unlit vertex colors, used to inspect the decoded color stream.</summary>
	VertexColor,

	/// <summary>Alpha-blended water/lava surface with a cool tint.</summary>
	Water,

	/// <summary>Alpha-blended lit geometry.</summary>
	Transparent,

	/// <summary>Unlit alpha-blended decal quad.</summary>
	Decal,
}
