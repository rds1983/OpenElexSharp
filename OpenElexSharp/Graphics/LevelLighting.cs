using Microsoft.Xna.Framework;

namespace OpenElexSharp.Graphics;

/// <summary>
/// Per-region sky, sun, ambient and fog defaults for the stock <see cref="BasicEffect"/> rig.
/// The palette is chosen from the loaded sector names; it deliberately stays simple until real
/// level lighting data is decoded.
/// </summary>
public sealed record LevelLighting(
	string Name,
	Color SkyTop,
	Color SkyHorizon,
	Vector3 FogColor,
	Vector3 SunDirection,
	Vector3 SunColor,
	Vector3 AmbientColor,
	Vector3 EmissiveColor,
	Vector3 FillDirection,
	Vector3 FillColor)
{
	/// <summary>Neutral blue-grey fallback for test scenes and unknown worlds.</summary>
	public static LevelLighting Default { get; } = new(
		"Default",
		new Color(58, 104, 158),
		new Color(180, 202, 218),
		ToVector(new Color(180, 202, 218)),
		Direction(-0.35f, -1f, 0.55f),
		new Vector3(0.82f, 0.80f, 0.72f),
		new Vector3(0.42f, 0.44f, 0.48f),
		new Vector3(0.04f, 0.045f, 0.05f),
		Direction(-0.6f, -0.4f, -0.45f),
		new Vector3(0.16f, 0.18f, 0.22f));

	/// <summary>Tavar: pale hazy horizon, warm low sun and dry dust-coloured fog.</summary>
	public static LevelLighting Tavar { get; } = new(
		"Tavar",
		new Color(48, 92, 150),
		new Color(208, 198, 172),
		ToVector(new Color(208, 198, 172)),
		Direction(-0.35f, -1f, 0.55f),
		new Vector3(0.95f, 0.88f, 0.72f),
		new Vector3(0.50f, 0.47f, 0.40f),
		new Vector3(0.055f, 0.050f, 0.040f),
		Direction(-0.6f, -0.4f, -0.45f),
		new Vector3(0.22f, 0.23f, 0.26f));

	/// <summary>Edan: temperate blue sky with a soft, slightly warm sun.</summary>
	public static LevelLighting Edan { get; } = Default with
	{
		Name = "Edan",
		SkyTop = new Color(58, 108, 164),
		SkyHorizon = new Color(184, 205, 220),
		FogColor = ToVector(new Color(184, 205, 220)),
		AmbientColor = new Vector3(0.50f, 0.53f, 0.58f),
		EmissiveColor = new Vector3(0.045f, 0.05f, 0.06f),
	};

	/// <summary>Ignadon: dark volcanic sky, orange haze and a strong ember-coloured sun.</summary>
	public static LevelLighting Ignadon { get; } = new(
		"Ignadon",
		new Color(74, 40, 36),
		new Color(198, 112, 72),
		ToVector(new Color(198, 112, 72)),
		Direction(-0.35f, -1f, 0.55f),
		new Vector3(1.00f, 0.66f, 0.42f),
		new Vector3(0.58f, 0.42f, 0.34f),
		new Vector3(0.12f, 0.07f, 0.05f),
		Direction(-0.6f, -0.4f, -0.45f),
		new Vector3(0.28f, 0.16f, 0.10f));

	/// <summary>Xacor: cold overcast sky and snow-reflected ambient light.</summary>
	public static LevelLighting Xacor { get; } = new(
		"Xacor",
		new Color(92, 112, 140),
		new Color(208, 216, 228),
		ToVector(new Color(208, 216, 228)),
		Direction(-0.35f, -1f, 0.55f),
		new Vector3(0.88f, 0.92f, 1.00f),
		new Vector3(0.66f, 0.70f, 0.78f),
		new Vector3(0.10f, 0.11f, 0.13f),
		Direction(-0.6f, -0.4f, -0.45f),
		new Vector3(0.26f, 0.28f, 0.32f));

	/// <summary>
	/// Picks a palette from the loaded sector names. The sector name is matched by its familiar
	/// three-letter region prefix; unknown or test worlds use <see cref="Default"/>.
	/// </summary>
	public static LevelLighting ForSector(string? sectorNames)
	{
		if (string.IsNullOrWhiteSpace(sectorNames))
		{
			return Default;
		}

		var upper = sectorNames.ToUpperInvariant();
		if (upper.Contains("IGN", StringComparison.Ordinal))
		{
			return Ignadon;
		}

		if (upper.Contains("XAC", StringComparison.Ordinal))
		{
			return Xacor;
		}

		if (upper.Contains("TAV", StringComparison.Ordinal))
		{
			return Tavar;
		}

		if (upper.Contains("ABE", StringComparison.Ordinal) ||
			upper.Contains("EDA", StringComparison.Ordinal))
		{
			return Edan;
		}

		return Default;
	}

	private static Vector3 Direction(float x, float y, float z) =>
		Vector3.Normalize(new Vector3(x, y, z));

	private static Vector3 ToVector(Color color) =>
		new(color.R / 255f, color.G / 255f, color.B / 255f);
}
