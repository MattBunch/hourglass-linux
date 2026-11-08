namespace Hourglass.Linux.Services;

using System.Reflection;

public static class ApplicationVersion
{
    public static string Read(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return Display(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3) ?? "unknown");
    }

    public static string Display(string informationalVersion)
    {
        ArgumentNullException.ThrowIfNull(informationalVersion);
        return informationalVersion.Split('+')[0];
    }
}
