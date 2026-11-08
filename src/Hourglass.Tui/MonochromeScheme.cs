namespace Hourglass.Tui;

using Terminal.Gui.Drawing;

public static class MonochromeScheme
{
    public static Scheme Create()
    {
        Attribute normal = new(Color.None, Color.None);
        Attribute focused = new(Color.None, Color.None, TextStyle.Reverse);
        return new(normal)
        {
            Normal = normal,
            HotNormal = normal,
            Focus = focused,
            HotFocus = focused,
            Active = focused,
            HotActive = focused,
            Highlight = focused,
            Editable = normal,
            ReadOnly = normal,
            Disabled = normal
        };
    }
}
