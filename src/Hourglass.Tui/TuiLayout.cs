namespace Hourglass.Tui;

public sealed record TuiLayout(bool ShowSessions, bool Tiny, int ContentRows, int StatusRow)
{
    public static TuiLayout Calculate(int width, int height, bool dashboard) => new(
        dashboard && width >= 80 && height >= 24, width < 40 || height < 12,
        Math.Max(1, height - 6), Math.Max(0, height - 5));

    public static int FirstVisible(int selected, int count, int rows) => Math.Clamp(selected - Math.Max(1, rows) + 1, 0, Math.Max(0, count - Math.Max(1, rows)));

    public static string Wrap(string text, int columns)
    {
        int width = Math.Max(1, columns);
        var lines = new List<string>();
        foreach (string line in text.Split('\n'))
        {
            int[] offsets = System.Globalization.StringInfo.ParseCombiningCharacters(line);
            if (offsets.Length == 0) { lines.Add(string.Empty); }
            for (int start = 0; start < offsets.Length; start += width)
            {
                int end = start + width < offsets.Length ? offsets[start + width] : line.Length;
                lines.Add(line[offsets[start]..end]);
            }
        }
        return string.Join('\n', lines);
    }

    public static string Scroll(string text, int offset, int rows)
    {
        string[] lines = text.Split('\n');
        return string.Join('\n', lines.Skip(Math.Clamp(offset, 0, Math.Max(0, lines.Length - 1))).Take(Math.Max(1, rows)));
    }
}
