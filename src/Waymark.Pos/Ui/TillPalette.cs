namespace Waymark.Pos.Ui;

/// <summary>Which of the two palettes the till draws with.</summary>
public enum TillThemeKind
{
    Light,
    Dark,
}

/// <summary>A label and the ground it sits on, or a signal and its fill: the pairs a contrast rule applies to.</summary>
public sealed record Signal(string Mark, string Text, string? Fill);

/// <summary>
/// Every colour the till uses, and <b>the only file in <c>Waymark.Pos</c> that may name one</b>
/// (<c>TillPaletteTests</c> fails otherwise), so the brand rules can be checked here without
/// reading a view. Values are the G1 kit's table (§1, "Couleur"), which takes the design
/// system's tokens first and replaces its dark column where marked NOUVEAU.
///
/// <para>
/// Two facts the table encodes that a view must not undo:
/// </para>
/// <list type="bullet">
///   <item><description>
///   <b>The bars are a dark surface in both themes.</b> Everything on them — Encaisser, the
///   labels, the bar keys — takes the dark roles, which is why Encaisser is accent violet with an
///   ink label even in the light theme (<c>royal-violet</c> on the bar is 1.86:1).
///   </description></item>
///   <item><description>
///   <b>In the dark theme the bars go to ink-black</b> and the page lifts a step, so the bars
///   stay the darkest anchor on screen (kit §1).
///   </description></item>
/// </list>
/// </summary>
public sealed record TillPalette(
    TillThemeKind Kind,
    string Page,
    string Card,
    string Tile,
    string Border,
    string BorderSubtle,
    string Bar,
    string BarText,
    string BarLabel,
    string BarKeyBorder,
    string Text,
    string TextSecondary,
    string TextMuted,
    string Collect,
    string CollectLabel,
    string Action,
    string ActionLabel,
    string FocusRing,
    string DisabledFill,
    string DisabledLabel,
    string BarDisabledFill,
    string BarDisabledLabel,
    Signal Warning,
    Signal Critical,
    Signal BarCritical,
    Signal Almanac)
{
    public static TillPalette Light { get; } = new(
        TillThemeKind.Light,
        // Not the kit's #FBFAFC: a white card on it was barely visible, so the Almanac card
        // disappeared (Hakim, 23/09). One step darker keeps every text pair above its floor.
        Page: "#F7F5F9",
        Card: "#ffffff",
        Tile: "#EDE7FA",
        Border: "#E4E1E9",
        BorderSubtle: "#EDEBF1",
        Bar: "#2B1E4E",
        BarText: "#FFFFFF",
        BarLabel: "#B5AEC4",
        BarKeyBorder: "#8D849F",
        Text: "#14101F",
        TextSecondary: "#44404F",
        TextMuted: "#6B6478",
        Collect: "#9B5CF0",
        CollectLabel: "#14101F",
        Action: "#5A3AA8",
        ActionLabel: "#FFFFFF",
        FocusRing: "#9B5CF0",
        DisabledFill: "#E4E1E9",
        DisabledLabel: "#44404F",
        BarDisabledFill: "#3A3646",
        BarDisabledLabel: "#C7C0D7",
        Warning: new Signal("#BA8823", "#7C580A", "#FBF1E3"),
        Critical: new Signal("#C03F44", "#93292F", "#FFEEED"),
        // "HORS LIGNE" sits on the bar, which is dark in both themes: the dark critical roles.
        // The light critical text there is 1.86:1.
        BarCritical: new Signal("#E2666B", "#F79F9A", null),
        Almanac: new Signal("#0E8C86", "#0A5F5B", null));

    public static TillPalette Dark { get; } = new(
        TillThemeKind.Dark,
        Page: "#28233A",
        Card: "#342E4B",
        Tile: "#4A406E",
        Border: "#554C72",
        BorderSubtle: "#41395A",
        Bar: "#14101F",
        BarText: "#FFFFFF",
        BarLabel: "#B5AEC4",
        BarKeyBorder: "#8D849F",
        Text: "#FFFFFF",
        TextSecondary: "#C7C0D7",
        TextMuted: "#A69EBB",
        Collect: "#9B5CF0",
        CollectLabel: "#14101F",
        Action: "#9B5CF0",
        ActionLabel: "#14101F",
        // Not the kit's #9B5CF0: on the lifted dark tile it is 2.30:1, under the 3:1 every ring
        // must hold, and the search field's focus border sits on exactly that tile. #C6B6EE is
        // the design system's own dark nav-active, 5.03:1 on the tile. For Hakim to confirm.
        FocusRing: "#C6B6EE",
        DisabledFill: "#3A3646",
        DisabledLabel: "#C7C0D7",
        BarDisabledFill: "#3A3646",
        BarDisabledLabel: "#C7C0D7",
        // No tinted fill in the dark theme: the design system defines none, and the kit keeps it
        // that way ("sans fond"). The signal edge and the label carry the state.
        Warning: new Signal("#BA8823", "#F2C57E", null),
        Critical: new Signal("#E2666B", "#F79F9A", null),
        BarCritical: new Signal("#E2666B", "#F79F9A", null),
        Almanac: new Signal("#0E8C86", "#73D0CA", null));

    public static TillPalette For(TillThemeKind kind) => kind == TillThemeKind.Dark ? Dark : Light;
}
