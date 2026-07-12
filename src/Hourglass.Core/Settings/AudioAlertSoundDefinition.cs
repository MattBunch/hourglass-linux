#nullable enable

using System.Collections.ObjectModel;

namespace Hourglass.Settings;

public sealed record AudioAlertSoundDefinition(
    string Id,
    string DisplayName,
    string? AssetFileName,
    bool IsNone = false);

public static class BuiltInAudioAlertSounds
{
    public const string None = "none";
    public const string LoudBeep = "resource:Loud beep";
    public const string NormalBeep = "resource:Normal beep";
    public const string QuietBeep = "resource:Quiet beep";

    public static readonly AudioAlertSoundDefinition NoSound = new(None, "None", null, IsNone: true);
    public static readonly AudioAlertSoundDefinition Loud = new(LoudBeep, "Loud beep", "BeepLoud.wav");
    public static readonly AudioAlertSoundDefinition Normal = new(NormalBeep, "Normal beep", "BeepNormal.wav");
    public static readonly AudioAlertSoundDefinition Quiet = new(QuietBeep, "Quiet beep", "BeepQuiet.wav");

    public static IReadOnlyList<AudioAlertSoundDefinition> All { get; } =
        new ReadOnlyCollection<AudioAlertSoundDefinition>(
        [
            NoSound,
            Loud,
            Normal,
            Quiet
        ]);

    public static AudioAlertSoundDefinition Default => Normal;

    public static string NormalizeId(string? soundId)
    {
        return TryGet(soundId, out AudioAlertSoundDefinition? sound)
            ? sound?.Id ?? Default.Id
            : Default.Id;
    }

    public static bool TryGet(string? soundId, out AudioAlertSoundDefinition? sound)
    {
        if (string.IsNullOrWhiteSpace(soundId))
        {
            sound = null;
            return false;
        }

        string normalized = soundId.Trim();
        foreach (AudioAlertSoundDefinition candidate in All)
        {
            if (StringComparer.Ordinal.Equals(candidate.Id, normalized))
            {
                sound = candidate;
                return true;
            }
        }

        sound = null;
        return false;
    }

    public static bool IsNone(string? soundId)
    {
        return StringComparer.Ordinal.Equals(NormalizeId(soundId), None);
    }
}
