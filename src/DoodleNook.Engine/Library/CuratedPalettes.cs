using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Library;

/// <summary>
/// Mission 012: palettes by other artists, bundled only where their creator has said they may be used freely (see
/// docs/PALETTE-ATTRIBUTION.md for every source and the exact words). Colours are exactly as published on Lospec
/// (lospec.com/palette-list), copied from each palette's own download, in the published order. Nothing is fetched at
/// run time. Palettes whose terms are unclear are not bundled; they can still be downloaded from Lospec and imported
/// (Palettes › Import… reads .hex and .gpl).
/// </summary>
public static class CuratedPalettes
{
    public const string Collection = "Lospec";

    public static readonly IReadOnlyList<PaletteAsset> All =
    [
        new("builtin:lospec-pico-8", "PICO-8 palette", Collection, Hex("#000000", "#1D2B53", "#7E2553", "#008751", "#AB5236", "#5F574F", "#C2C3C7", "#FFF1E8", "#FF004D", "#FFA300", "#FFEC27", "#00E436", "#29ADFF", "#83769C", "#FF77A8", "#FFCCAA"), true,
            new PaletteCredit("Lexaloffle Games", "https://lospec.com/palette-list/pico-8", "CC0 1.0 (public domain), stated by Lexaloffle", "https://www.lexaloffle.com/pico-8.php?page=faq", "bright, playful")),
        new("builtin:lospec-resurrect-64", "Resurrect 64", Collection, Hex("#2E222F", "#3E3546", "#625565", "#966C6C", "#AB947A", "#694F62", "#7F708A", "#9BABB2", "#C7DCD0", "#FFFFFF", "#6E2727", "#B33831", "#EA4F36", "#F57D4A", "#AE2334", "#E83B3B", "#FB6B1D", "#F79617", "#F9C22B", "#7A3045", "#9E4539", "#CD683D", "#E6904E", "#FBB954", "#4C3E24", "#676633", "#A2A947", "#D5E04B", "#FBFF86", "#165A4C", "#239063", "#1EBC73", "#91DB69", "#CDDF6C", "#313638", "#374E4A", "#547E64", "#92A984", "#B2BA90", "#0B5E65", "#0B8A8F", "#0EAF9B", "#30E1B9", "#8FF8E2", "#323353", "#484A77", "#4D65B4", "#4D9BE6", "#8FD3FF", "#45293F", "#6B3E75", "#905EA9", "#A884F3", "#EAADED", "#753C54", "#A24B6F", "#CF657F", "#ED8099", "#831C5D", "#C32454", "#F04F78", "#F68181", "#FCA790", "#FDCBB0"), true,
            new PaletteCredit("Kerrie Lake", "https://lospec.com/palette-list/resurrect-64", "Free to use, by the creator's permission (informal; she calls her palettes open source)", "https://lospec.com/palette-list/resurrect-64#comment-26712", "vibrant, general purpose")),
        new("builtin:lospec-arq16", "ARQ16", Collection, Hex("#FFFFFF", "#FFD19D", "#AEB5BD", "#4D80C9", "#E93841", "#100820", "#511E43", "#054494", "#F1892D", "#823E2C", "#FFA9A9", "#5AE150", "#FFE947", "#7D3EBF", "#EB6C82", "#1E8A4C"), true,
            new PaletteCredit("ENDESGA", "https://lospec.com/palette-list/arq16", "Open and free to use, for anyone and anything (creator's statement)", "https://x.com/ENDESGA/status/996702967054004224", "bright, near full spectrum")),
        new("builtin:lospec-dawnbringer-16", "DawnBringer 16", Collection, Hex("#140C1C", "#442434", "#30346D", "#4E4A4E", "#854C30", "#346524", "#D04648", "#757161", "#597DCE", "#D27D2C", "#8595A1", "#6DAA2C", "#D2AA99", "#6DC2CA", "#DAD45E", "#DEEED6"), true,
            new PaletteCredit("DawnBringer", "https://lospec.com/palette-list/dawnbringer-16", "Free to use (creator's statement, informal)", "https://pixeljoint.com/forum/forum_posts.asp?TID=12795", "muted, earthy")),
        new("builtin:lospec-cl8uds", "CL8UDS", Collection, Hex("#FCB08C", "#EF9D7F", "#D6938A", "#B48D92", "#A597A1", "#8FA0BF", "#9AABC9", "#A5B7D4"), true,
            new PaletteCredit("_Nicola", "https://lospec.com/palette-list/cl8uds", "Free to use in any way (creator's statement)", "https://lospec.com/palette-list/cl8uds", "soft, warm sunset")),
        new("builtin:lospec-japanese-woodblock", "Japanese Woodblock", Collection, Hex("#2B2821", "#624C3C", "#D9AC8B", "#E3CFB4", "#243D5C", "#5D7275", "#5C8B93", "#B1A58D", "#B03A48", "#D4804D", "#E0C872", "#3E6958"), true,
            new PaletteCredit("Polyducks", "https://lospec.com/palette-list/japanese-woodblock", "Free to use in any way; credit optional (creator's statement)", "https://itch.io/post/8583183", "muted, painterly print")),
        new("builtin:lospec-ayy4", "AYY4", Collection, Hex("#00303B", "#FF7777", "#FFCE96", "#F1F2DA"), true,
            new PaletteCredit("Polyducks", "https://lospec.com/palette-list/ayy4", "Free to use in any way; credit optional (creator's statement)", "https://itch.io/post/8583183", "soft, handheld")),
        new("builtin:lospec-cave4", "Cave4", Collection, Hex("#E4CBBF", "#938282", "#4F4E80", "#2C0016"), true,
            new PaletteCredit("Polyducks", "https://lospec.com/palette-list/cave4", "Free to use in any way; credit optional (creator's statement)", "https://itch.io/post/8583183", "cold, gloomy")),
        new("builtin:lospec-a-blood-steeped-corridor", "A Blood-Steeped Corridor", Collection, Hex("#17152E", "#3C566C", "#84AEA4", "#D4E2CC", "#E43254"), true,
            new PaletteCredit("Polyducks", "https://lospec.com/palette-list/a-blood-steeped-corridor", "Free to use in any way; credit optional (creator's statement)", "https://itch.io/post/8583183", "horror, night")),
        new("builtin:lospec-1bit-monitor-glow", "1bit Monitor Glow", Collection, Hex("#222323", "#F0F6F0"), true,
            new PaletteCredit("Polyducks", "https://lospec.com/palette-list/1bit-monitor-glow", "Free to use in any way; credit optional (creator's statement)", "https://itch.io/post/8583183", "1-bit, cool glow")),
    ];

    private static ColorRgb[] Hex(params string[] hex) => hex.Select(ColorRgb.Parse).ToArray();
}
