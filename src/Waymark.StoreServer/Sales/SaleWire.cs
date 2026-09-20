using Waymark.Application.Sales;
using Waymark.Contracts.Pos;

namespace Waymark.StoreServer.Sales;

/// <summary>The sale's answer as the till reads it (D-070). Pure, so it is tested without a server.</summary>
public static class SaleWire
{
    public static CompleteSale ToCommand(SaleRequest request) => new(
        request.TerminalId,
        request.StaffId,
        [.. request.Lines.Select(line => new SaleLineRequest(line.Barcode, line.Count))]);

    public static SaleOutcome Completed(CompletedSale sale) => new(
        SaleOutcomes.Completed,
        sale.TransactionId,
        sale.InvoiceNumber,
        WireText.Figure(sale.Total),
        WireText.Figure(sale.TaxTotal),
        WireText.Figure(sale.Cash.Tendered),
        sale.Total.Currency.Code,
        Reason: null);

    public static SaleOutcome Refused(string reason) =>
        new(SaleOutcomes.Refused, null, null, null, null, null, null, reason);
}
