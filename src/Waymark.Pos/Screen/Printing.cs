namespace Waymark.Pos.Screen;

/// <summary>What went wrong with the printer (D-126). Its cause read from the device (no paper, the cover open) is K1's.</summary>
public enum PrinterFault
{
    /// <summary>The printer did not take what was sent.</summary>
    NotResponding,
}

/// <summary>What became of an operation's paper.</summary>
public enum PaperOutcome
{
    Printed,

    /// <summary>Printing is switched off at this till (D-126): sale tickets only.</summary>
    SwitchedOff,

    /// <summary>Sent, and the printer did not take it: the operation is written, the paper is missing.</summary>
    Failed,
}

/// <summary>Which paper: the words differ, the rule does not (board 4).</summary>
public enum PaperKind
{
    Sale,
    Refund,
    Repayment,
}

/// <summary>An operation's paper, and whether the drawer opened for it.</summary>
public sealed record Paper(PaperOutcome Outcome, bool DrawerOpened);

/// <summary>
/// The till's printing for one frame (D2, D-126): whether it has a printer, whether printing is on,
/// what went wrong last, and the paper of the operation just ended, of each kind.
/// </summary>
/// <param name="Available">The till was started with a printer; false prints nothing and says nothing of paper.</param>
/// <param name="Sale">The paid ticket's paper; null when the paid ticket is not the one it was printed for.</param>
/// <param name="Refund">The refund whose ticket is open's paper; null otherwise.</param>
/// <param name="Repayment">The repayment just recorded's slip; null otherwise.</param>
/// <param name="NoSaleOpen">The no-sale panel is open: scans are ignored, and the slot says so.</param>
public sealed record PrintingState(
    bool Available, bool On, PrinterFault? Fault, Paper? Sale = null, Paper? Refund = null, Paper? Repayment = null, bool NoSaleOpen = false);

/// <summary>
/// The paper's lines in the panel where an operation ended (boards 3 and 4): "Ticket imprimé", or
/// what kept it from printing; the drawer's line; and the key that prints it after all.
/// </summary>
/// <param name="Status">"Ticket imprimé", "Ticket non imprimé — impression coupée"; null under a warning, which says it.</param>
/// <param name="Warning">The printer failed: a warning, never critical, since the operation is written.</param>
/// <param name="Print">"Imprimer le ticket", F7: the original, while no paper of it came out.</param>
public sealed record PaperBlock(string? Status, string? Drawer, PanelMessage? Warning, OperationKey? Print);

/// <summary>The printer's chip on the bar (board 2): neutral when printing is switched off, a warning when the printer fails.</summary>
public sealed record PrinterChip(string Label, Tone Tone);

/// <summary>The printing rules of the screen (D-126), apart from the window so they are tested without one.</summary>
public static class PrintScreen
{
    /// <summary>
    /// The bar's chip: a failure first, even with printing switched off (the cashier learns it before a
    /// customer asks for a ticket); then printing switched off; else none.
    /// </summary>
    public static PrinterChip? Chip(TillText text, PrintingState? printing)
    {
        ArgumentNullException.ThrowIfNull(text);
        return printing is not { Available: true } ? null
            : printing.Fault is { } fault ? new PrinterChip(text.PrinterFaultChip(fault), Tone.Warning)
            : !printing.On ? new PrinterChip(text.PrintingOffChip, Tone.Neutral)
            : null;
    }

    /// <summary>The paper's lines for one operation; null when the till has no printer or the operation printed nothing yet.</summary>
    public static PaperBlock? Block(TillText text, PrintingState? printing, Paper? paper, PaperKind kind)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (printing is not { Available: true } || paper is null)
        {
            return null;
        }

        var drawer = !paper.DrawerOpened ? null : kind == PaperKind.Repayment ? text.DrawerOpenedStash : text.DrawerOpenedLine;
        var print = new OperationKey(Operation.PrintTicket, kind == PaperKind.Repayment ? text.PrintReceiptKey : text.PrintTicketKey, "F7", true);
        return paper.Outcome switch
        {
            PaperOutcome.Printed => new PaperBlock(text.Printed(kind), drawer, null, null),
            PaperOutcome.SwitchedOff => new PaperBlock(text.TicketNotPrintedOff, drawer, null, print),
            _ => new PaperBlock(null, drawer, new PanelMessage(text.NotPrintedTitle(kind), text.NotPrintedBody(kind, printing.Fault ?? PrinterFault.NotResponding), Tone.Warning), print),
        };
    }
}
