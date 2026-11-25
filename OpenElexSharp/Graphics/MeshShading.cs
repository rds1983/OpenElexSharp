namespace OpenElexSharp.Graphics;

/// <summary>Shading applied to static mesh parts.</summary>
public enum MeshShading
{
	/// <summary>Textured and lit with the stock <c>BasicEffect</c>.</summary>
	Lit,

	/// <summary>Unlit vertex colors, used to inspect the decoded color stream.</summary>
	VertexColor,
}
