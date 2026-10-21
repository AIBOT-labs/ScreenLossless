using System.Globalization;
using System.Text.Json;

namespace ScreenLossless;
internal enum AppLanguage { Automatic, Chinese, English }
internal sealed record LanguageOption(AppLanguage Language)
{
    public override string ToString() => Language switch
    {
        AppLanguage.Chinese => "\u4e2d\u6587",
        AppLanguage.English => "English",
        _ => L10n.T("跟随系统")
    };
}
internal static class L10n
{
    // Capture the Windows display language before a manual selection changes thread UI culture.
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;
    private static readonly Lazy<IReadOnlyDictionary<string,string>> English = new(() =>
    {
        using var stream=typeof(L10n).Assembly.GetManifestResourceStream("ScreenLossless.Localization.en.json")
            ?? throw new InvalidOperationException("English language resource is missing.");
        return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)
            ?? throw new InvalidDataException("English language resource is invalid.");
    });
    internal static AppLanguage Preference {get;private set;}
    internal static AppLanguage Language {get;private set;}=Resolve(AppLanguage.Automatic,SystemCulture);
    internal static bool IsEnglish=>Language==AppLanguage.English;
    internal static CultureInfo Culture=>CultureInfo.GetCultureInfo(IsEnglish?"en-US":"zh-CN");
    internal static IReadOnlyDictionary<string,string> Catalog=>English.Value;
    internal static AppLanguage Resolve(AppLanguage preference,CultureInfo? system=null)
    {
        if(preference is AppLanguage.Chinese or AppLanguage.English)return preference;
        return (system??SystemCulture).TwoLetterISOLanguageName.Equals("zh",StringComparison.OrdinalIgnoreCase)
            ?AppLanguage.Chinese:AppLanguage.English;
    }
    internal static void Configure(AppLanguage preference)
    {
        Preference=Enum.IsDefined(preference)?preference:AppLanguage.Automatic;
        Language=Resolve(Preference);
        CultureInfo.CurrentUICulture=Culture;
        CultureInfo.DefaultThreadCurrentUICulture=Culture;
        // CurrentCulture is intentionally untouched: technical parsing and persisted values retain their behavior.
    }
    internal static string T(string chinese)=>IsEnglish&&English.Value.TryGetValue(chinese,out var translated)?translated:chinese;
    internal static string F(FormattableString text)=>string.Format(Culture,T(text.Format),text.GetArguments());
}
