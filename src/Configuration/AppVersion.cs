namespace ScreenLossless;

internal static class AppVersion
{
    internal static readonly string Display = "v" + typeof(AppVersion).Assembly.GetName().Version!.ToString(3);
}
