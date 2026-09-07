using System.Globalization;

namespace RecompSetup;

/// <summary>
/// One localization the Xbox 360 build can actually resolve. LanguageId is the
/// console language setting and CountryId is the console country setting. The
/// latter matters for regional variants that share a language ID.
/// </summary>
public sealed record GameLanguageOption(
    string Key,
    string DisplayName,
    uint LanguageId,
    uint CountryId,
    bool RequiresRussianMod,
    string Availability,
    string Detail)
{
    public override string ToString() => DisplayName;
}

public static class GameLanguages
{
    // Country IDs are the Xbox 360 XConfig country values used by ReXGlue.
    // Danish has no dedicated Xbox 360 language ID; the retail disc carries a
    // Danish localization and selects it through the Denmark locale.
    public static readonly GameLanguageOption English = new(
        "en", "English", 1, 103, false, "Official · voice + text",
        "Original English localization.");

    public static readonly GameLanguageOption German = new(
        "de", "Deutsch", 3, 24, false, "Official · voice + text",
        "German voice and interface text.");

    public static readonly GameLanguageOption French = new(
        "fr", "Français", 4, 34, false, "Official · voice + text",
        "French voice and interface text.");

    public static readonly GameLanguageOption SpanishSpain = new(
        "es-ES", "Español (España)", 5, 31, false, "Official · voice + text",
        "Spanish localization for Spain.");

    public static readonly GameLanguageOption SpanishLatam = new(
        "es-419", "Español (Latinoamérica)", 5, 71, false, "Official · voice + text",
        "Latin American Spanish localization. The Xbox locale is set to Mexico.");

    public static readonly GameLanguageOption Italian = new(
        "it", "Italiano", 6, 50, false, "Official · voice + text",
        "Italian voice and interface text.");

    public static readonly GameLanguageOption Dutch = new(
        "nl", "Nederlands", 16, 74, false, "Official · text / subtitles",
        "Dutch interface text and subtitles; spoken dialogue remains English.");

    public static readonly GameLanguageOption Danish = new(
        "da", "Dansk", 1, 25, false, "Official · voice + text",
        "The Xbox 360 has no separate Danish language ID, so the game uses the Denmark locale.");

    public static readonly GameLanguageOption Russian = new(
        "ru-community", "Русский", 1, 103, true, "Community translation · text",
        "Community Russian translation. It replaces the English text column; spoken dialogue remains English.");

    public static readonly IReadOnlyList<GameLanguageOption> All = new[]
    {
        English,
        German,
        French,
        SpanishSpain,
        SpanishLatam,
        Italian,
        Dutch,
        Danish,
        Russian,
    };

    public static IReadOnlyList<GameLanguageOption> AvailableFor(PayloadSource payload) =>
        All.Where(l => !l.RequiresRussianMod || (payload.HasRussian && payload.HasMods)).ToList();

    public static GameLanguageOption DetectWindowsLanguage(PayloadSource payload)
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        string lang = culture.TwoLetterISOLanguageName.ToLowerInvariant();
        GameLanguageOption result = lang switch
        {
            "de" => German,
            "fr" => French,
            "it" => Italian,
            "nl" => Dutch,
            "da" => Danish,
            "ru" => Russian,
            "es" => IsSpain(culture) ? SpanishSpain : SpanishLatam,
            _ => English,
        };
        if (result.RequiresRussianMod && !(payload.HasRussian && payload.HasMods)) return English;
        return result;
    }

    static bool IsSpain(CultureInfo culture)
    {
        try
        {
            if (culture.IsNeutralCulture) return false;
            return new RegionInfo(culture.Name).TwoLetterISORegionName.Equals("ES", StringComparison.OrdinalIgnoreCase);
        }
        catch { return culture.Name.Equals("es-ES", StringComparison.OrdinalIgnoreCase); }
    }
}
