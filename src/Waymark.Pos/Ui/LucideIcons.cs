// Icon outlines from Lucide (https://lucide.dev), taken from the G1 kit, which draws them at a
// 1.5 px stroke on a 24 px grid. Circles, rectangles and lines are rewritten as path commands so
// Avalonia can parse them; no outline is redrawn.
//
// ISC License
//
// Copyright (c) for portions of Lucide are held by Cole Bemis 2013-2022 as part of Feather (MIT).
// All other copyright (c) for Lucide are held by Lucide Contributors 2022.
//
// Permission to use, copy, modify, and/or distribute this software for any purpose with or
// without fee is hereby granted, provided that the above copyright notice and this permission
// notice appear in all copies.
//
// THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS
// SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE
// AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
// WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT,
// NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR
// PERFORMANCE OF THIS SOFTWARE.

namespace Waymark.Pos.Ui;

/// <summary>
/// The till's one icon set (G1 kit §3): Lucide, outline, 1.5 px on a 24 px grid, never mixed with
/// another set. Each constant is SVG path data for a 24 × 24 box; <see cref="TillTheme.Icon"/>
/// draws it. At rest an icon is <c>text-muted</c>, active it is <c>action</c>, unavailable
/// <c>text-secondary</c>.
/// </summary>
public static class LucideIcons
{
    /// <summary>Lucide <c>search</c>.</summary>
    public const string Search = "M3 11a8 8 0 1 0 16 0a8 8 0 1 0 -16 0Z M21 21l-4.3-4.3";

    /// <summary>Lucide <c>scan-barcode</c>.</summary>
    public const string ScanBarcode = "M3 7V5a2 2 0 0 1 2-2h2 M17 3h2a2 2 0 0 1 2 2v2 M21 17v2a2 2 0 0 1-2 2h-2 M7 21H5a2 2 0 0 1-2-2v-2 M8 7v10 M12 7v10 M17 7v10";

    /// <summary>Lucide <c>scale</c>.</summary>
    public const string Scale = "M16 16l3-8 3 8c-.87.65-1.92 1-3 1s-2.13-.35-3-1Z M2 16l3-8 3 8c-.87.65-1.92 1-3 1s-2.13-.35-3-1Z M7 21h10 M12 3v18 M3 7h2c2 0 5-1 7-2 2 1 5 2 7 2h2";

    /// <summary>Lucide <c>user</c>.</summary>
    public const string User = "M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2 M8 7a4 4 0 1 0 8 0a4 4 0 1 0 -8 0Z";

    /// <summary>Lucide <c>users</c>.</summary>
    public const string Users = "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M5 7a4 4 0 1 0 8 0a4 4 0 1 0 -8 0Z M22 21v-2a4 4 0 0 0-3-3.87 M16 3.13a4 4 0 0 1 0 7.75";

    /// <summary>Lucide <c>pause</c>.</summary>
    public const string Pause = "M15 4h2a1 1 0 0 1 1 1v14a1 1 0 0 1 -1 1h-2a1 1 0 0 1 -1 -1v-14a1 1 0 0 1 1 -1Z M7 4h2a1 1 0 0 1 1 1v14a1 1 0 0 1 -1 1h-2a1 1 0 0 1 -1 -1v-14a1 1 0 0 1 1 -1Z";

    /// <summary>Lucide <c>percent</c>.</summary>
    public const string Percent = "M19 5 5 19 M4 6.5a2.5 2.5 0 1 0 5 0a2.5 2.5 0 1 0 -5 0Z M15 17.5a2.5 2.5 0 1 0 5 0a2.5 2.5 0 1 0 -5 0Z";

    /// <summary>Lucide <c>tag</c>.</summary>
    public const string Tag = "M12.586 2.586A2 2 0 0 0 11.172 2H4a2 2 0 0 0-2 2v7.172a2 2 0 0 0 .586 1.414l8.704 8.704a2.426 2.426 0 0 0 3.42 0l6.58-6.58a2.426 2.426 0 0 0 0-3.42z M6.5 7.5a1 1 0 1 0 2 0a1 1 0 1 0 -2 0Z";

    /// <summary>Lucide <c>package</c>.</summary>
    public const string Package = "M11 21.73a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73z M12 22V12 M3.3 7l8.7 5 8.7-5 M7.5 4.27l9 5.15";

    /// <summary>Lucide <c>printer</c>.</summary>
    public const string Printer = "M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2 M6 9V3a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v6 M7 14h10a1 1 0 0 1 1 1v6a1 1 0 0 1 -1 1h-10a1 1 0 0 1 -1 -1v-6a1 1 0 0 1 1 -1Z";

    /// <summary>Lucide <c>banknote</c>.</summary>
    public const string Banknote = "M4 6h16a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-16a2 2 0 0 1 -2 -2v-8a2 2 0 0 1 2 -2Z M10 12a2 2 0 1 0 4 0a2 2 0 1 0 -4 0Z M6 12h.01M18 12h.01";

    /// <summary>Lucide <c>undo-2</c>.</summary>
    public const string Undo2 = "M9 14 4 9l5-5 M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5a5.5 5.5 0 0 1-5.5 5.5H11";

    /// <summary>Lucide <c>coins</c>.</summary>
    public const string Coins = "M2 8a6 6 0 1 0 12 0a6 6 0 1 0 -12 0Z M18.09 10.37A6 6 0 1 1 10.34 18 M7 6h1v4 M16.71 13.88l.7.71-2.82 2.82";

    /// <summary>Lucide <c>ellipsis</c>.</summary>
    public const string Ellipsis = "M11 12a1 1 0 1 0 2 0a1 1 0 1 0 -2 0Z M18 12a1 1 0 1 0 2 0a1 1 0 1 0 -2 0Z M4 12a1 1 0 1 0 2 0a1 1 0 1 0 -2 0Z";

    /// <summary>Lucide <c>ban</c>.</summary>
    public const string Ban = "M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0Z M4.9 4.9l14.2 14.2";

    /// <summary>Lucide <c>trash-2</c>.</summary>
    public const string Trash2 = "M3 6h18 M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6 M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2";

    /// <summary>Lucide <c>credit-card</c>.</summary>
    public const string CreditCard = "M4 5h16a2 2 0 0 1 2 2v10a2 2 0 0 1 -2 2h-16a2 2 0 0 1 -2 -2v-10a2 2 0 0 1 2 -2Z M2 10h20";

    /// <summary>Lucide <c>smartphone</c>.</summary>
    public const string Smartphone = "M7 2h10a2 2 0 0 1 2 2v16a2 2 0 0 1 -2 2h-10a2 2 0 0 1 -2 -2v-16a2 2 0 0 1 2 -2Z M12 18h.01";

    /// <summary>Lucide <c>notebook</c>.</summary>
    public const string Notebook = "M2 6h4 M2 10h4 M2 14h4 M2 18h4 M6 2h12a2 2 0 0 1 2 2v16a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2v-16a2 2 0 0 1 2 -2Z M16 2v20";

    /// <summary>Lucide <c>lock</c>.</summary>
    public const string Lock = "M5 11h14a2 2 0 0 1 2 2v7a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-7a2 2 0 0 1 2 -2Z M7 11V7a5 5 0 0 1 10 0v4";

    /// <summary>Lucide <c>chevron-down</c>.</summary>
    public const string ChevronDown = "M6 9l6 6 6-6";

    /// <summary>Lucide <c>chevron-up</c>.</summary>
    public const string ChevronUp = "M18 15l-6-6-6 6";

    /// <summary>Lucide <c>minus</c>.</summary>
    public const string Minus = "M5 12h14";

    /// <summary>Lucide <c>plus</c>.</summary>
    public const string Plus = "M5 12h14 M12 5v14";

    /// <summary>Lucide <c>wifi-off</c>.</summary>
    public const string WifiOff = "M12 20h.01 M8.5 16.429a5 5 0 0 1 7 0 M5 12.859a10 10 0 0 1 5.17-2.69 M19 12.859a10 10 0 0 0-2.007-1.523 M2 8.82a15 15 0 0 1 4.177-2.643 M22 8.82a15 15 0 0 0-11.288-3.764 M2 2l20 20";

    /// <summary>Lucide <c>delete</c>.</summary>
    public const string Delete = "M10 5a2 2 0 0 0-1.344.519l-6.328 5.74a1 1 0 0 0 0 1.481l6.328 5.741A2 2 0 0 0 10 19h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2z M12 9l6 6 M18 9l-6 6";

    /// <summary>Lucide <c>rotate-cw</c>.</summary>
    public const string RotateCw = "M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8 M21 3v5h-5";

    /// <summary>Lucide <c>check</c>.</summary>
    public const string Check = "M20 6 9 17l-5-5";
}
