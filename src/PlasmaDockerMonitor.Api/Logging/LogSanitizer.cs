namespace PlasmaDockerMonitor.Api.Logging;

/// <summary>
/// Guards log entries built from user-supplied values against log forging
/// </summary>
internal static class LogSanitizer
{
    /// <summary>
    /// Strips carriage returns and line feeds, so a value can't break onto a new, fake log line
    /// </summary>
    public static string SanitizeForLog(this string value) =>
        value.Replace("\r", string.Empty).Replace("\n", string.Empty);
}
