using static Waymark.Generator.Tests.Sql;

namespace Waymark.Generator.Tests;

/// <summary>
/// A drawer never pays out cash it does not hold (Phase 0 final test). Seed 8 of the mini shop
/// refunded about 2,900 DZD in cash on a morning when the drawer held its 2,000 DZD float, and
/// closed below zero; the other fixtures never met that morning.
/// </summary>
public sealed class DrawerTests : IDisposable
{
    private readonly ScratchDirectory _scratch = new();

    [Fact]
    public void A_cash_refund_waits_for_cash_and_no_drawer_ever_closes_below_zero()
    {
        var run = GeneratorRun.Execute(new GeneratorArguments(TestInputs.MiniConfig, Path.Combine(_scratch.Path, "seed-8"), 8, 30), TextWriter.Null);
        using var db = Open(run.DatabasePath);

        Assert.Equal(0, Scalar(db, "SELECT count(*) FROM cash_sessions WHERE expected_cash < 0 OR counted_cash < 0"));
        Assert.True(Scalar(db, "SELECT count(*) FROM returns WHERE refund_method = 'cash'") > 0, "No cash refund was paid, so nothing was checked.");

        // Between opening and close, CashDrawer itself refuses to go below zero, so a refund that
        // was not checked fails the run rather than reaching this assertion.
    }

    public void Dispose() => _scratch.Dispose();
}
