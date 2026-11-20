using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace DoodleNook.App.Controls;

/// <summary>
/// DoodleNook's line icons (24 × 24, round 1.7 px strokes), the same drawings as the Mission 003 playground.
/// Circles and rectangles from the original SVGs are written as path arcs.
/// </summary>
public static class Icons
{
    public static readonly IReadOnlyDictionary<string, string> Data = new Dictionary<string, string>
    {
        ["menu"] = "M4 7h16M4 12h16M4 17h16",
        ["brush"] = "M20 4l-8.3 8.3 M11.9 12.1l-1.9-1.9c-1.5 0-2.7.6-3.3 1.9-.7 1.5-.3 3.4-2.6 4.6 2.7 1 5.7.3 6.8-1.5.6-.9.8-2 .8-3.1z",
        ["eraser"] = "M9 20h11 M5.2 15.2l8.9-8.9a2 2 0 0 1 2.8 0l2.7 2.7a2 2 0 0 1 0 2.8L11.6 20H8.3z M9.7 10.7l5.5 5.5",
        ["eyedropper"] = "M14.6 6.4l2.9-2.9a2.1 2.1 0 0 1 3 3l-2.9 2.9 M13 8l-7.5 7.5L5 19l3.5-.5L16 11 M12 7l5 5",
        ["hand"] = "M8 13V6.6a1.5 1.5 0 0 1 3 0V12 M11 11.6V5.1a1.5 1.5 0 0 1 3 0v6.5 M14 11.6V6.6a1.5 1.5 0 0 1 3 0V14c0 3.6-2.4 6-6 6-2.4 0-3.9-1.2-5-3l-1.8-3a1.4 1.4 0 0 1 2.3-1.6L8 14",
        ["undo"] = "M9 14L4 9l5-5 M4 9h10.5a5.5 5.5 0 0 1 0 11H11",
        ["redo"] = "M15 14l5-5-5-5 M20 9H9.5a5.5 5.5 0 0 0 0 11H13",
        ["chevd"] = "M6 9l6 6 6-6",
        ["close"] = "M6 6l12 12M18 6L6 18",
        ["plus"] = "M12 5v14M5 12h14",
        ["minus"] = "M5 12h14",
        ["fit"] = "M6 6h12a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z M8 10.5V9h1.5M16 10.5V9h-1.5M8 13.5V15h1.5M16 13.5V15h-1.5",
        ["flip"] = "M12 3v18 M9 7L4 17h5z M15 7l5 10h-5z",
        ["expand"] = "M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5",
        ["pressure"] = "M4 20l1-3.5L15.5 6l2.5 2.5L7.5 19z M14 20c1.6-1.2 3.4-1.2 5 0",
        ["swap"] = "M7 7h11l-3-3M17 17H6l3 3",
        ["check"] = "M5 12.5l4.5 4.5L19 7.5",
        ["new"] = "M6 3.5h8l4 4v13H6z M14 3.5v4h4 M12 11v6M9 14h6",
        ["open"] = "M3.5 7a1.5 1.5 0 0 1 1.5-1.5h4l2 2h8a1.5 1.5 0 0 1 1.5 1.5v9a1.5 1.5 0 0 1-1.5 1.5H5a1.5 1.5 0 0 1-1.5-1.5z",
        ["save"] = "M5 4h11l3 3v13H5z M8 4v5h7V4 M8 20v-6h8v6",
        ["saveas"] = "M5 4h11l3 3v5 M5 4v16h6 M8 4v5h7V4 M13.5 20.5l.6-2.6 4.6-4.6a1.3 1.3 0 0 1 1.9 1.9l-4.6 4.6z",
        ["trash"] = "M4 7h16M9 7V4.5h6V7M6.5 7l1 13h9l1-13",
        ["zoom"] = "M4.5 10.5a6 6 0 1 0 12 0a6 6 0 1 0-12 0z M15 15l5 5",
        ["fullscreen"] = "M3.5 8V3.5H8 M16 3.5h4.5V8 M20.5 16v4.5H16 M8 20.5H3.5V16",
        ["pulse"] = "M3 12h4l2-5 4 10 2-5h6",
        ["info"] = "M3.5 12a8.5 8.5 0 1 0 17 0a8.5 8.5 0 1 0-17 0z M12 11v5 M12 7.7v.2",
        ["exit"] = "M14 4h4a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-4 M10 16l-4-4 4-4 M6 12h10",
        ["keyboard"] = "M4.5 6.5h15a1.5 1.5 0 0 1 1.5 1.5v8a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 16V8a1.5 1.5 0 0 1 1.5-1.5z M7 10h.01M10 10h.01M13 10h.01M16 10h.01M8 14h8",
        ["lasso"] = "M4 9.5a8 5 0 1 0 16 0a8 5 0 1 0-16 0z M8.5 14c-1.6 1.2-1.5 3 .3 3.6 1.4.5 1.2 2-.3 2.6",
        ["marquee"] = "M5 5h14a1 1 0 0 1 1 1v11a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z",
        ["ellipsesel"] = "M3.5 12a8.5 7 0 1 0 17 0a8.5 7 0 1 0-17 0z",
        ["move"] = "M12 3v18M3 12h18 M9 6l3-3 3 3M9 18l3 3 3-3M6 9l-3 3 3 3M18 9l3 3-3 3",
        ["fill"] = "M5 11.6L11.6 5l7 7-6.6 6.6a1.5 1.5 0 0 1-2.1 0L5 13.7a1.5 1.5 0 0 1 0-2.1z M5.3 12.5h13 M19.8 15.5s1.5 1.9 1.5 2.9a1.5 1.5 0 0 1-3 0c0-1 1.5-2.9 1.5-2.9z",
        ["gradient"] = "M6 5h12a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z M9 5v14M14 5v14",
        ["shape"] = "M5 9.5h7a1.5 1.5 0 0 1 1.5 1.5v7a1.5 1.5 0 0 1-1.5 1.5H5a1.5 1.5 0 0 1-1.5-1.5v-7A1.5 1.5 0 0 1 5 9.5z M10.5 8.5a5 5 0 1 0 10 0a5 5 0 1 0-10 0z",
        ["crop"] = "M6 3v13a2 2 0 0 0 2 2h13 M3 6h13a2 2 0 0 1 2 2v13",
        ["line"] = "M5.5 18.5l13-13 M3.4 19a1.6 1.6 0 1 0 3.2 0a1.6 1.6 0 1 0-3.2 0z M17.4 5a1.6 1.6 0 1 0 3.2 0a1.6 1.6 0 1 0-3.2 0z",
        ["rect"] = "M6 6h12a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1z",
        ["ellipse"] = "M3.5 12a8.5 6.5 0 1 0 17 0a8.5 6.5 0 1 0-17 0z",
        ["pixpencil"] = "M9 15l8.3-8.3a2 2 0 0 1 2.8 2.8L11.8 17.8z M4 16h4.5v4.5H4z",
        ["wand"] = "M4 20L14.5 9.5 M12.8 7.8l3.4 3.4 M17 3v3M15.5 4.5h3M20 9v3M18.5 10.5h3M10 3.5v2M9 4.5h2",
        ["layers"] = "M12 3.5l8.5 4.6L12 12.7 3.5 8.1z M3.5 12.1l8.5 4.6 8.5-4.6 M3.5 16l8.5 4.6 8.5-4.6",
        ["eye"] = "M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z M9 12a3 3 0 1 0 6 0a3 3 0 1 0-6 0z",
        ["eyeoff"] = "M3 3l18 18 M10.6 5.6A9.7 9.7 0 0 1 12 5.5c6 0 9.5 6.5 9.5 6.5a17 17 0 0 1-2.6 3.4M6.6 6.6C3.9 8.4 2.5 12 2.5 12S6 18.5 12 18.5c1.6 0 3-.4 4.2-1",
        ["lock"] = "M7.5 11V8a4.5 4.5 0 0 1 9 0v3 M5.5 11h13v9h-13z",
        ["unlock"] = "M7.5 11V8a4.5 4.5 0 0 1 8.6-1.9 M5.5 11h13v9h-13z",
        ["smudge"] = "M10.5 13V5a1.5 1.5 0 0 1 3 0v6 M13.5 8.5a1.5 1.5 0 0 1 3 0v3 M16.5 10a1.5 1.5 0 0 1 3 0V14c0 3.6-2.6 6.5-6.2 6.5h-.8c-2.3 0-3.6-1-5-3l-2.6-3.6a1.5 1.5 0 0 1 2.4-1.8l2.2 2.4 M3 5.5h4 M3.5 9h3",
        ["clone"] = "M3.5 8.5a4 4 0 1 0 8 0a4 4 0 1 0-8 0z M12.5 15.5a4 4 0 1 0 8 0a4 4 0 1 0-8 0z M10.4 11.4l3 3 M13.6 12.2v2.4h-2.4",
        ["repair"] = "M8 20L4 16a2 2 0 0 1 0-2.8L13.2 4a2 2 0 0 1 2.8 0l4 4a2 2 0 0 1 0 2.8L10.8 20a2 2 0 0 1-2.8 0z M9.5 9.5l5 5 M11.3 12.7h.01 M12.7 11.3h.01",
        ["lq-push"] = "M3.5 12h12 M11.5 7l5 5-5 5 M3.5 7.5c2-1 3.5-1 5 0 M3.5 16.5c2 1 3.5 1 5 0",
        ["lq-inflate"] = "M9 12a3 3 0 1 0 6 0a3 3 0 1 0-6 0z M12 2.5v4 M10.3 4.2L12 2.5l1.7 1.7 M12 21.5v-4 M10.3 19.8l1.7 1.7 1.7-1.7 M2.5 12h4 M4.2 10.3L2.5 12l1.7 1.7 M21.5 12h-4 M19.8 10.3l1.7 1.7-1.7 1.7",
        ["lq-pinch"] = "M12 2.5v6 M10 6.5l2 2 2-2 M12 21.5v-6 M10 17.5l2-2 2 2 M2.5 12h6 M6.5 10l2 2-2 2 M21.5 12h-6 M17.5 10l-2 2 2 2",
        ["lq-twirl"] = "M12 12.5a1.5 1.5 0 1 1 1.5-1.5c0 2.5-2 4-4 4-3 0-5-2.5-5-5.5 0-4 3-6.5 6.5-6.5 4.5 0 8 3.5 8 8 0 5-4 9-9 9",
        ["lq-smooth"] = "M3 12c3-4.5 6-4.5 9 0s6 4.5 9 0 M3 7c3-2 6-2 9 0s6 2 9 0 M3 17c3-2 6-2 9 0s6 2 9 0",
        ["liquify"] = "M4 7c3-3 5 3 8 0s5-3 8 0 M4 12c3-3 5 3 8 0s5-3 8 0 M4 17c3-3 5 3 8 0s5-3 8 0",
        ["tf-persp"] = "M7 6.5h10l3.5 12h-17z M9.7 6.5L8 18.5 M14.3 6.5l1.7 12 M5.3 12.5h13.4",
        ["tf-warp"] = "M4 6c5 2.5 11-2.5 16 0v12c-5-2.5-11 2.5-16 0z M9.3 5.8v12.4 M14.7 6.2v11.6 M4 12c5 2.5 11-2.5 16 0",
        ["alphalock"] = "M4.5 4.5h15v15h-15z M4.5 12h15 M12 4.5v15",
        ["clip"] = "M7 4v8a4 4 0 0 0 4 4h8 M16 13l3 3-3 3",
        ["copy"] = "M8.5 8.5h11v11h-11z M5 15.5V4.5h11",
        ["paste"] = "M8.5 3.5h7v3h-7z M7 5H5v15.5h14V5h-2",
        ["image"] = "M4.5 4.5h15v15h-15z M4.5 15.5l4.5-4.5 4 4 3-3 3.5 3.5",
        ["merge"] = "M7 4v5l5 5 5-5V4 M12 14v6",
        ["history"] = "M3.5 12a8.5 8.5 0 1 0 2.5-6 M3.5 4v4h4 M12 8v4l3 2",
        ["props"] = "M4 7h10M18 7h2M4 17h4M12 17h8 M14 7a2 2 0 1 0 4 0a2 2 0 1 0-4 0z M8 17a2 2 0 1 0 4 0a2 2 0 1 0-4 0z",
        ["dots"] = "M5 12h.01M12 12h.01M19 12h.01",
        ["chevl"] = "M15 6l-6 6 6 6",
        ["chevr"] = "M9 6l6 6-6 6",
        ["chevu"] = "M6 15l6-6 6 6",
        ["radial"] = "M3.5 12a8.5 8.5 0 1 0 17 0a8.5 8.5 0 1 0-17 0z M9.5 12a2.5 2.5 0 1 0 5 0a2.5 2.5 0 1 0-5 0z M12 3.5v3M12 17.5v3M3.5 12h3M17.5 12h3",
        ["rotate"] = "M20 12a8 8 0 1 1-2.4-5.7 M20 4v5h-5",
        ["rotateccw"] = "M4 12a8 8 0 1 0 2.4-5.7 M4 4v5h5",
        ["sym"] = "M12 3v18 M9 7L4 12l5 5z M15 7l5 5-5 5z",
        ["grid"] = "M4 4h16v16H4z M4 9.3h16M4 14.6h16M9.3 4v16M14.6 4v16",
        ["tiled"] = "M3 3h8v8H3z M13 3h8v8h-8z M3 13h8v8H3z M13 13h8v8h-8z",
        ["transform"] = "M6 6h12v12H6z M3.5 3.5h4v4h-4z M16.5 3.5h4v4h-4z M3.5 16.5h4v4h-4z M16.5 16.5h4v4h-4z",
        ["invert"] = "M4 4h16v16H4z M8 8h8v8H8z",
        ["export"] = "M12 15V4 M8 8l4-4 4 4 M5 14v6h14v-6",
        ["place"] = "M4.5 4.5h15v15h-15z M12 8v8 M8 12h8",
        ["paper"] = "M6 3.5h12v17H6z",
        ["wsA"] = "M5.5 5h13a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z",
        ["wsB"] = "M5.5 5h13a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z M3.5 8.5h17M7 8.5V19",
        ["wsC"] = "M5.5 5h13a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z M3.5 8.5h17M7 8.5V19M15.5 8.5V19",
        ["wsD"] = "M5.5 5h13a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z M3.5 8.2h17M3.5 10.8h17M7 10.8V19M15.5 10.8V19M3.5 16.6h17",
        ["wsE"] = "M5.5 5h13a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z M8 9h2v2H8z M12 9h2v2h-2z M10 11h2v2h-2z M14 11h2v2h-2z M8 13h2v2H8z",
        // Animation Studio
        ["wsF"] = "M4 5h16a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z M7.5 5v14 M16.5 5v14 M3 9.5h4.5 M3 14.5h4.5 M16.5 9.5H21 M16.5 14.5H21",
        ["play"] = "M8 5.5v13l10.5-6.5z",
        ["pause"] = "M8.5 5.5v13 M15.5 5.5v13",
        ["stop"] = "M7 7h10v10H7z",
        ["onion"] = "M9 12a6 6 0 1 0 12 0a6 6 0 1 0-12 0z M12 6.8a6 6 0 1 0 0 10.4 M6.5 7.6a6 6 0 0 0 0 8.8",
        ["newframe"] = "M4 6h9v12H4z M18.5 9v6 M15.5 12h6",
        ["dupframe"] = "M8 4h11v13H8z M5 7v13h11",
        ["first"] = "M6.5 6v12 M17 6l-6 6l6 6",
        ["last"] = "M17.5 6v12 M7 6l6 6l-6 6",
        ["loop"] = "M17 3l3 3l-3 3 M4 12v-1a5 5 0 0 1 5-5h11 M7 21l-3-3l3-3 M20 12v1a5 5 0 0 1-5 5H4",
        ["film"] = "M4 4h16v16H4z M4 9h16 M4 15h16 M9 4v5 M15 4v5 M9 15v5 M15 15v5",
        ["recover"] = "M3 12a9 9 0 1 0 3-6.7 M3 4v4h4 M12 8v4l3 2",
        ["clock"] = "M12 3.5a8.5 8.5 0 1 0 0 17a8.5 8.5 0 1 0 0-17z M12 7.5v5l3 2",
        ["settings"] = "M4 7h9 M17 7h3 M15 5v4 M4 17h3 M11 17h9 M9 15v4",
        ["stamp"] = "M9 4h6v5l2 3H7l2-3z M5 13h14v3H5z M4 19h16",
        ["star"] = "M12 3.5l2.6 5.3 5.9.9-4.3 4.1 1 5.8L12 16.9l-5.2 2.7 1-5.8-4.3-4.1 5.9-.9z",
        ["pattern"] = "M4 4h7v7H4z M13 13h7v7h-7z M13 4h7v7h-7z M4 13h7v7H4z",
        ["lab"] = "M9 3h6 M10 3v6l-5 9a2 2 0 0 0 2 3h10a2 2 0 0 0 2-3l-5-9V3 M7.5 15h9",
        ["board"] = "M3 5h18v13H3z M7 21h10 M12 18v3 M6 15l4-4 3 3 2-2 3 3",
        ["box"] = "M7 4h10a3 3 0 0 1 3 3v10a3 3 0 0 1-3 3H7a3 3 0 0 1-3-3V7a3 3 0 0 1 3-3z",
        ["boxcheck"] = "M7 4h10a3 3 0 0 1 3 3v10a3 3 0 0 1-3 3H7a3 3 0 0 1-3-3V7a3 3 0 0 1 3-3z M8 12.5l2.8 2.8L16 9.5",
        ["palette"] = "M12 3.5a8.5 8.5 0 1 0 0 17c1.4 0 2-1 1.5-2.2-.6-1.4.3-2.8 1.8-2.8H17a3.5 3.5 0 0 0 3.5-3.5c0-4.7-3.8-8.5-8.5-8.5z",
        // Mission 008: text, groups, masks, adjustment layers and drawing assistants.
        ["text"] = "M5 7V4.5h14V7 M12 4.5v15 M9 19.5h6",
        ["folder"] = "M3.5 6.5h6l2 2h9v10.5h-17z",
        ["mask"] = "M4 5h16v14H4z M12 8.5a3.5 3.5 0 0 0 0 7 M12 8.5a3.5 3.5 0 0 1 0 7z M10.2 10l1.8-1.5 M9 12.5l3-2.5 M9.6 14.7L12 12.7",
        ["adjust"] = "M12 4a8 8 0 1 0 0 16a8 8 0 1 0 0-16z M12 4v16 M12 8l4.6-2 M12 12l7.5-1 M12 16l6 1.5",
        ["ruler"] = "M3 16L16 3l5 5L8 21z M7 12l2 2 M10 9l1.5 1.5 M13 6l2 2",
        ["guides"] = "M8 3v18 M3 15h18 M3 3h2M19 3h2M3 21h2M19 21h2",
        ["persp"] = "M12 6L3 19 M12 6l9 13 M12 6v13 M3 6h18 M7.5 12.5h9",
        ["roundrect"] = "M8 6h8a4 4 0 0 1 4 4v4a4 4 0 0 1-4 4H8a4 4 0 0 1-4-4v-4a4 4 0 0 1 4-4z",
        ["polygon"] = "M12 3.5l7.4 4.25v8.5L12 20.5l-7.4-4.25v-8.5z",
        ["magnet"] = "M6 4v8a6 6 0 0 0 12 0V4 M10 4v8a2 2 0 0 0 4 0V4 M6 8h4 M14 8h4",
        ["alignl"] = "M4 4v16 M8 7h10v4H8z M8 13h6v4H8z",
        ["alignc"] = "M12 4v3 M12 11v2 M12 17v3 M7 7h10v4H7z M9 13h6v4H9z",
        ["alignr"] = "M20 4v16 M6 7h10v4H6z M10 13h6v4h-6z",
        ["alignt"] = "M4 4h16 M7 8h4v10H7z M13 8h4v6h-4z",
        ["alignm"] = "M4 12h3 M11 12h2 M17 12h3 M7 7h4v10H7z M13 9h4v6h-4z",
        ["alignb"] = "M4 20h16 M7 6h4v10H7z M13 10h4v6h-4z",
        ["assist"] = "M10.5 4.5a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0-3 0 M12 6L6.5 20 M12 6l5.5 14 M8.6 15h6.8",
    };

    private static readonly Dictionary<string, Geometry> Cache = new();

    public static Geometry? Get(string? name)
    {
        if (name is null || !Data.TryGetValue(name, out var path)) return null;
        if (!Cache.TryGetValue(name, out var g))
        {
            g = Geometry.Parse(path);
            Cache[name] = g;
        }
        return g;
    }
}

/// <summary>A line icon that takes its colour from the surrounding text colour (like CSS currentColor).</summary>
public sealed class DnIcon : Control
{
    /// <summary>Icons drawn with a dashed line (selection marquees), like the playground's stroke-dasharray.</summary>
    private static readonly HashSet<string> Dashed = ["marquee", "ellipsesel"];

    public static readonly StyledProperty<string?> KindProperty =
        AvaloniaProperty.Register<DnIcon, string?>(nameof(Kind));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<DnIcon>();

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<DnIcon, double>(nameof(StrokeThickness), 1.7);

    static DnIcon()
    {
        AffectsRender<DnIcon>(KindProperty, ForegroundProperty, StrokeThicknessProperty);
        WidthProperty.OverrideDefaultValue<DnIcon>(19);
        HeightProperty.OverrideDefaultValue<DnIcon>(19);
        IsHitTestVisibleProperty.OverrideDefaultValue<DnIcon>(false);
    }

    public string? Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var geometry = Icons.Get(Kind);
        if (geometry is null || Foreground is null) return;
        double s = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
        var pen = Kind is not null && Dashed.Contains(Kind)
            ? new Pen(Foreground, StrokeThickness, new DashStyle([1.8, 1.5], 0), PenLineCap.Flat, PenLineJoin.Round)
            : new Pen(Foreground, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation((Bounds.Width - 24 * s) / 2, (Bounds.Height - 24 * s) / 2)))
            context.DrawGeometry(null, pen, geometry);
    }
}
