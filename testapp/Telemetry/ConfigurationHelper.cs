namespace TestApp.Telemetry;

public static class ConfigurationHelper
{
    public static bool IsSpecified(this string str) =>
        !string.IsNullOrEmpty(str)
        && !str.StartsWith("set in ", StringComparison.OrdinalIgnoreCase)
        && Uri.TryCreate(str, UriKind.Absolute, out _);

    public static bool IsHeaderSpecified(this string str) =>
        !string.IsNullOrEmpty(str)
        && !str.StartsWith("set in ", StringComparison.OrdinalIgnoreCase);

    public static Uri ToValidUri(this string str, string settingName)
    {
        if (Uri.TryCreate(str, UriKind.Absolute, out var uri))
            return uri;

        throw new InvalidOperationException(
            $"Configuration value '{settingName}' is '{str}', which is not a valid absolute URI. " +
            "Set it via environment-specific appsettings or user-secrets, or leave the placeholder " +
            "to disable OTLP export for that signal.");
    }
}
