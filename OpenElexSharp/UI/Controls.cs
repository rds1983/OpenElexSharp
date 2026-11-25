using Myra.Graphics2D.UI;

namespace OpenElexSharp.UI;

public partial class Controls
{
	public Controls()
	{
		BuildUI();
		Widgets.Add(new Label
		{
			Text = "1-4 - Lights/decals/water/veg",
			TextColor = new Microsoft.Xna.Framework.Color(232, 232, 232),
		});
	}
}
