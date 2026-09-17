using Waymark.Generator.Reporting;
using Waymark.Generator.Writing;
using static Waymark.Generator.Tests.Sql;

namespace Waymark.Generator.Tests;

/// <summary>
/// A commissioned store in a real database (W10 S4): reference data, customers and opening
/// stock, written through the migrated schema with foreign keys and triggers enforced.
/// </summary>
public sealed class CommissioningTests(GrocerySalesRun grocery) : IClassFixture<GrocerySalesRun>
{
    private static (RunResult Result, ScratchDirectory Scratch) Run(string config, int? seed = null, int? days = null)
    {
        var scratch = new ScratchDirectory();
        var result = GeneratorRun.Execute(
            new GeneratorArguments(config, Path.Combine(scratch.Path, "store"), seed, days),
            TextWriter.Null);
        return (result, scratch);
    }

    [Fact]
    public void The_grocery_store_is_commissioned_with_everything_the_catalogue_describes()
    {
        using var db = grocery.Open();

        Assert.Equal(1, Scalar(db, "SELECT count(*) FROM stores"));
        Assert.Equal(399, Scalar(db, "SELECT count(*) FROM variants"));
        Assert.Equal(399, Scalar(db, "SELECT count(*) FROM supplier_variant"));
        Assert.Equal(136, Scalar(db, "SELECT count(*) FROM products"));
        Assert.Equal(8, Scalar(db, "SELECT count(*) FROM categories WHERE parent_id IS NULL"));
        Assert.Equal(41, Scalar(db, "SELECT count(*) FROM categories WHERE parent_id IS NOT NULL AND tax_rate IS NOT NULL"));
        Assert.Equal(5, Scalar(db, "SELECT count(*) FROM suppliers"));
        Assert.Equal(3, Scalar(db, "SELECT count(*) FROM staff"));
        Assert.Equal(120, Scalar(db, "SELECT count(*) FROM customers"));

        // Every commissioning price plus every dated change, each change closing the previous row.
        Assert.Equal(399 + 307, Scalar(db, "SELECT count(*) FROM prices"));
        Assert.Equal(399, Scalar(db, "SELECT count(*) FROM prices WHERE valid_to IS NULL"));

        Assert.Equal(1, Scalar(db, "SELECT count(*) FROM stores WHERE manager_id IS NOT NULL AND rounding_policy = 'half_up'"));
    }

    [Fact]
    public void A_second_catalogue_commissions_a_different_kind_of_store()
    {
        var (result, scratch) = Run(TestInputs.MiniConfig, days: 1);
        using (scratch)
        using (var db = Open(result.DatabasePath))
        {
            Assert.Equal(["hardware"], Column(db, "SELECT store_type FROM stores"));
            Assert.Equal(["half_even"], Column(db, "SELECT rounding_policy FROM stores"));
            Assert.Equal(6, Scalar(db, "SELECT count(*) FROM variants"));
            Assert.Equal(["outillage", "peinture-colles"], Column(db, "SELECT slug FROM categories WHERE parent_id IS NULL ORDER BY slug"));
        }
    }

    [Fact]
    public void The_store_satisfies_every_constraint_and_keeps_its_triggers()
    {
        using var db = grocery.Open();

        Assert.Empty(Column(db, "SELECT \"table\" FROM pragma_foreign_key_check"));
        Assert.Equal(29, Scalar(db, "SELECT count(*) FROM sqlite_schema WHERE type = 'trigger'"));
    }

    [Fact]
    public void Consent_is_two_consents_recorded_as_events_and_objectors_have_none()
    {
        using var db = grocery.Open();

        // CLAUDE.md §4: each consent is its own event; the customer row mirrors the events.
        Assert.Equal(
            Scalar(db, "SELECT count(*) FROM customers WHERE consent_profiling = 1"),
            Scalar(db, "SELECT count(*) FROM consent_events WHERE consent_type = 'processing' AND action = 'granted'"));
        Assert.Equal(
            Scalar(db, "SELECT count(*) FROM customers WHERE consent_marketing = 1"),
            Scalar(db, "SELECT count(*) FROM consent_events WHERE consent_type = 'marketing' AND action = 'granted'"));
        Assert.Equal(0, Scalar(db, "SELECT count(*) FROM customers WHERE objection_flag = 1 AND (consent_profiling = 1 OR consent_marketing = 1)"));
        Assert.Equal(0, Scalar(db, "SELECT count(*) FROM consent_events e JOIN customers c USING (customer_id) WHERE e.occurred_at <> c.created_at"));
    }

    [Fact]
    public void Opening_stock_is_whole_cartons_received_as_day_zero_receipts()
    {
        using var db = grocery.Open();

        // Every opening batch item is an opening receipt, one for one, with a batch per product.
        var opening = $"(SELECT batch_id FROM batches WHERE supplier_document_ref = '{Simulation.OpeningStock.DocumentReference}')";
        Assert.Equal(
            Scalar(db, $"SELECT sum(quantity_received) FROM batch_items WHERE batch_id IN {opening}"),
            Scalar(db, $"SELECT sum(quantity_changed) FROM stock_movements WHERE movement_type = 'receipt' AND note = '{Simulation.OpeningStock.Note.Replace("'", "''", StringComparison.Ordinal)}'"));
        Assert.Equal(
            Scalar(db, $"SELECT count(*) FROM batch_items WHERE batch_id IN {opening}"),
            Scalar(db, $"SELECT count(*) FROM stock_movements WHERE movement_type = 'receipt' AND batch_id IN {opening}"));
        Assert.Equal(136, Scalar(db, $"SELECT count(*) FROM {opening}"));

        // closing = opening + Σ(deltas), with an opening of nothing.
        Assert.Equal(
            Scalar(db, "SELECT sum(quantity_changed) FROM stock_movements"),
            Scalar(db, "SELECT sum(quantity) FROM inventories"));

        // Whole cartons, opening stock and deliveries alike.
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM batch_items bi JOIN supplier_variant sv USING (variant_id)
            WHERE bi.quantity_received % sv.units_per_purchase_unit <> 0 OR bi.quantity_received = 0
            """));

        // A batch never carries more cover than it has shelf life, give or take the carton
        // rounding: nothing perishable arrives already expired.
        Assert.Equal(0, Scalar(db, "SELECT count(*) FROM batches WHERE expiration_date IS NOT NULL AND expiration_date <= received_date"));
    }

    [Fact]
    public void Every_id_carries_the_simulated_moment_its_row_was_created()
    {
        using var db = grocery.Open();

        // F-3 fixed: ULID time is business time, so an id sorts where its row happened.
        AssertIdsMatch(db, "SELECT customer_id, created_at FROM customers");
        AssertIdsMatch(db, "SELECT movement_id, created_at FROM stock_movements WHERE movement_type = 'receipt'");
        AssertIdsMatch(db, "SELECT store_id, created_at FROM stores");

        var customers = Column(db, "SELECT customer_id FROM customers ORDER BY created_at, customer_id");
        Assert.Equal(customers.Order(StringComparer.Ordinal), customers);
    }

    [Fact]
    public void The_same_seed_builds_the_same_store_and_the_same_latent_demand()
    {
        var (first, firstScratch) = Run(TestInputs.MiniConfig);
        var (second, secondScratch) = Run(TestInputs.MiniConfig);
        using (firstScratch)
        using (secondScratch)
        using (var a = Open(first.DatabasePath))
        using (var b = Open(second.DatabasePath))
        {
            Assert.Equal(CanonicalDump.Render(a), CanonicalDump.Render(b));
            Assert.Equal(File.ReadAllBytes(first.LatentDemandPath), File.ReadAllBytes(second.LatentDemandPath));
            Assert.Equal(File.ReadAllBytes(first.ReportPath), File.ReadAllBytes(second.ReportPath));
            Assert.True(Scalar(a, "SELECT count(*) FROM transactions") > 0, "The mini store sold nothing, so determinism was proven on commissioning alone.");
        }
    }

    [Fact]
    public void A_different_seed_builds_a_different_store()
    {
        var (first, firstScratch) = Run(TestInputs.MiniConfig, seed: 1, days: 5);
        var (second, secondScratch) = Run(TestInputs.MiniConfig, seed: 2, days: 5);
        using (firstScratch)
        using (secondScratch)
        using (var a = Open(first.DatabasePath))
        using (var b = Open(second.DatabasePath))
        {
            Assert.NotEqual(CanonicalDump.Render(a), CanonicalDump.Render(b));
        }
    }

    [Fact]
    public void A_previous_run_is_never_overwritten()
    {
        var (result, scratch) = Run(TestInputs.MiniConfig, days: 1);
        using (scratch)
        {
            var again = new GeneratorArguments(TestInputs.MiniConfig, Path.GetDirectoryName(result.DatabasePath)!, null, 1);
            var error = Assert.Throws<GeneratorInputException>(() => GeneratorRun.Execute(again, TextWriter.Null));
            Assert.Contains("never overwrites", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Invalid_input_writes_nothing()
    {
        using var scratch = new ScratchDirectory();
        var config = scratch.CopyMini();
        scratch.Edit(Path.Combine("catalogues", "quincaillerie", "catalogue.csv"), ",1.1,tools,", ",1.1,outils,");
        var output = Path.Combine(scratch.Path, "store");

        Assert.Throws<GeneratorInputException>(() => GeneratorRun.Execute(new GeneratorArguments(config, output, null, null), TextWriter.Null));
        Assert.False(Directory.Exists(output), "A run with invalid input created its output directory.");
    }

    [Fact]
    public void The_manifest_records_the_seed_hashes_steps_and_row_counts()
    {
        var (result, scratch) = Run(TestInputs.MiniConfig, seed: 99, days: 3);
        using (scratch)
        {
            var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(result.ManifestPath)).RootElement;

            Assert.Equal(99, manifest.GetProperty("seed").GetInt64());
            Assert.True(manifest.GetProperty("seed_overridden").GetBoolean());
            Assert.Equal(16, manifest.GetProperty("config_hash").GetString()!.Length);
            Assert.Equal(6, manifest.GetProperty("row_counts").GetProperty("variants").GetInt64());
            Assert.True(manifest.GetProperty("parameter_sources").GetProperty("guess").GetInt32() > 0);
            Assert.Contains("sales", manifest.GetProperty("steps_completed").EnumerateArray().Select(step => step.GetString()));
            Assert.Equal(LatentDemandCsv.FileName, Path.GetFileName(result.LatentDemandPath));
        }
    }
}
