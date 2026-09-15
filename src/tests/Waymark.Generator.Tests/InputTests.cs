using System.Text.Json;
using Waymark.Generator.Catalogues;
using Waymark.Generator.Configuration;

namespace Waymark.Generator.Tests;

/// <summary>
/// Loading and validating the configuration and the catalogue (D-046 §1–9). A wrong input
/// must fail loudly, name its file and line, and fail before a single row is written.
/// </summary>
public sealed class InputTests
{
    // ------------------------------------------------------------ shipped inputs

    [Fact]
    public void The_grocery_configuration_and_catalogue_load_and_validate()
    {
        var (config, catalogue, window) = TestInputs.Load(TestInputs.GroceryConfig);

        Assert.Empty(TestInputs.Problems(config, catalogue, window));
        Assert.Equal(399, catalogue.Variants.Count);
        Assert.Equal(8, catalogue.Categories.Count);
        Assert.Equal(5, catalogue.Suppliers.Count);
        Assert.Contains("Café, Thé & Petit Déjeuner", catalogue.Categories);
        Assert.Contains("Détergents & Produits d'Entretien", catalogue.Categories);
        Assert.Equal(365, window.Days);
    }

    [Fact]
    public void Every_grocery_variant_resolves_everything_demand_needs()
    {
        var (config, catalogue, _) = TestInputs.Load(TestInputs.GroceryConfig);
        var subcategories = catalogue.Subcategories.ToDictionary(s => s.Subcategory, StringComparer.Ordinal);

        Assert.All(catalogue.Variants, v =>
        {
            Assert.True(Ean13.IsValid(v.Barcode), $"{v.Sku} barcode");
            Assert.True(subcategories.ContainsKey(v.Subcategory), $"{v.Sku} subcategory");
            Assert.True(config.SeasonalityProfiles.ContainsKey(v.SeasonalityProfile), $"{v.Sku} profile");
            Assert.True(v.BaseDailyRate > 0, $"{v.Sku} rate");
        });
    }

    [Fact]
    public void A_second_catalogue_with_different_categories_validates_with_no_code_change()
    {
        // D-046 review item: the catalogue is data. A hardware shop loads through the same code.
        var (config, catalogue, window) = TestInputs.Load(TestInputs.MiniConfig);

        Assert.Empty(TestInputs.Problems(config, catalogue, window));
        Assert.Equal(["Outillage", "Peinture, Colles"], catalogue.Categories);
        Assert.DoesNotContain(catalogue.Variants, v => v.Category.StartsWith("Épicerie", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ configuration

    [Fact]
    public void A_parameter_without_a_source_is_refused()
    {
        using var scratch = new ScratchDirectory();
        var config = scratch.CopyMini();
        scratch.Edit("configs/mini.json", "\"initial_count\": { \"value\": 5, \"source\": \"guess\", \"note\": \"test\" }", "\"initial_count\": { \"value\": 5, \"note\": \"test\" }");

        var error = Assert.Throws<GeneratorInputException>(() => GeneratorJson.Read<GeneratorConfig>(config));
        Assert.Contains("missing its \"source\"", error.Message, StringComparison.Ordinal);
        Assert.Contains("mini.json", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"source\": \"guess\", \"note\": \"test\" }", "\"source\": \"hunch\", \"note\": \"test\" }", "guess, literature or interview")]
    [InlineData("\"value\": 5, \"source\": \"guess\", \"note\": \"test\"", "\"value\": 5, \"source\": \"guess\", \"note\": \"\"", "missing its \"note\"")]
    [InlineData("\"initial_count\": { \"value\": 5,", "\"initial_count\": 5, \"x\": {", "not a bare value")]
    public void A_parameter_must_carry_value_source_and_a_reason(string find, string replace, string expected)
    {
        using var scratch = new ScratchDirectory();
        var config = scratch.CopyMini();
        scratch.Edit("configs/mini.json", find, replace);

        var error = Assert.Throws<GeneratorInputException>(() => GeneratorJson.Read<GeneratorConfig>(config));
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_manifest_counts_every_sourced_parameter_in_the_configuration()
    {
        // The count walks the configuration tree, so a parameter added later cannot be left out.
        var text = File.ReadAllText(TestInputs.GroceryConfig);
        var declared = System.Text.RegularExpressions.Regex.Count(text, "\"source\"\\s*:");

        Assert.Equal(declared, GeneratorRun.ParameterSources(TestInputs.Load(TestInputs.GroceryConfig).Config).Values.Sum());
    }

    [Fact]
    public void A_misspelt_field_is_refused_rather_than_defaulted()
    {
        using var scratch = new ScratchDirectory();
        var config = scratch.CopyMini();
        scratch.Edit("configs/mini.json", "\"history_months\"", "\"history_month\"");

        var error = Assert.Throws<GeneratorInputException>(() => GeneratorJson.Read<GeneratorConfig>(config));
        Assert.IsType<JsonException>(error.InnerException);
    }

    [Fact]
    public void A_run_longer_than_the_configured_ramadans_is_refused()
    {
        // The mini configuration knows only Ramadan 1446. A 24-month run would contain 1447.
        var (config, catalogue, window) = TestInputs.Load(TestInputs.MiniConfig, days: 800);

        Assert.Contains(TestInputs.Problems(config, catalogue, window), p => p.Contains("Add the next Ramadan", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"events\": { \"salon\": 1.5 }", "\"events\": { \"foire\": 1.5 }", "which calendar.events does not define")]
    [InlineData("\"months\": [0.8,0.8,1,1.2,1.3,1.2,1,1,1.1,1,0.9,0.8]", "\"months\": [0.8,0.8,1,1.2,1.3,1.2,1,1,1.1,1,0.9]", "it needs 12")]
    [InlineData("\"objection_share\": { \"value\": 0.2", "\"objection_share\": { \"value\": 1.2", "a share is between 0 and 1")]
    [InlineData("\"aid_start\": \"2025-03-30\"", "\"aid_start\": \"2025-04-02\"", "must be the day after Ramadan ends")]
    [InlineData("\"thursday\": 1.2,", "", "calendar.weekday_factors is missing thursday")]
    [InlineData("\"trough_last_day\": { \"value\": 19", "\"trough_last_day\": { \"value\": 5", "trough is day 10 to 5")]
    [InlineData("\"cash\": 0.7,", "\"cash\": 0.8,", "shares of every basket sum to 1")]
    [InlineData("\"basket_units\": { \"value\": [0.5, 0.3, 0.2]", "\"basket_units\": { \"value\": []", "sales.basket_units has 0 weights")]
    [InlineData("\"value\": { \"1\": 0.9,", "\"value\": { \"4\": 0.9,", "sales.substitution_by_tier is missing 1")]
    [InlineData("\"customer_attach_share\": { \"value\": 0.3", "\"customer_attach_share\": { \"value\": -0.3", "sales.customer_attach_share is -0.3")]
    [InlineData("\"fill_rate\": { \"value\": 0.7", "\"fill_rate\": { \"value\": 1.7", "supply.fill_rate is 1.7")]
    [InlineData("\"drain_interval_minutes\": { \"value\": 10", "\"drain_interval_minutes\": { \"value\": 0", "connectivity.drain_interval_minutes is 0")]
    [InlineData("\"offline_days\": { \"value\": 10", "\"offline_days\": { \"value\": 0", "connectivity.offline_days is 0")]
    [InlineData("\"order_up_to_cover_days\": { \"value\": 15", "\"order_up_to_cover_days\": { \"value\": 3", "must exceed reorder_cover_days")]
    [InlineData("\"return_delay_days\": { \"value\": { \"min\": 1", "\"return_delay_days\": { \"value\": { \"min\": 0", "mess.return_delay_days is 0")]
    [InlineData("\"void\": [ \"ANNULATION\" ]", "\"void\": [ \"REMISE\" ]", "names 'REMISE', which applies to Discount, not Void")]
    [InlineData("\"drop\": [ \"DEPOT\" ]", "\"drop\": [ \"COFFRE\" ]", "names 'COFFRE', which store.json's reason_codes does not define")]
    public void Configuration_rules_are_reported_by_name(string find, string replace, string expected)
    {
        using var scratch = new ScratchDirectory();
        var configPath = scratch.CopyMini();
        scratch.Edit("configs/mini.json", find, replace);

        var (config, catalogue, window) = TestInputs.Load(configPath);
        Assert.Contains(TestInputs.Problems(config, catalogue, window), p => p.Contains(expected, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ catalogue

    [Theory]
    [InlineData("catalogue.csv", ",Outils à main,Marteau Menuisier,Manche Bois", ",Outils,Marteau Menuisier,Manche Bois", "is not in categories.csv, so the variant has no VAT class")]
    [InlineData("catalogue.csv", ",Q-OUT,boite,12,,1.1,", ",Q-XXX,boite,12,,1.1,", "supplier 'Q-XXX' is not in suppliers.csv")]
    [InlineData("catalogue.csv", ",piece,850.00,600.00", ",kilo,850.00,600.00", "selling_unit 'kilo' is not in units.csv")]
    [InlineData("catalogue.csv", "MQ-02,", "MQ-01,", "sku 'MQ-01' already appears")]
    [InlineData("catalogue.csv", ",1.1,tools,", ",1.1,outils,", "seasonality_profile 'outils' is not defined")]
    [InlineData("catalogue.csv", ",850.00,600.00,", ",850.00,720.00,", "would sell at a loss")]
    [InlineData("catalogue.csv", ",850.00,600.00,", ",850.005,600.00,", "more than two decimals")]
    [InlineData("catalogue.csv", ",0.6,tools,", ",0,tools,", "base_daily_rate must be positive")]
    [InlineData("suppliers.csv", "Peintures Distribution,sun,", "Peintures Distribution,,", "has no delivery day")]
    [InlineData("suppliers.csv", "mon|wed", "mon|mercredi", "has 'mercredi'")]
    [InlineData("price_changes.csv", "MQ-03,2025-03-20", "MQ-03,2025-02-25", "strictly increasing date order")]
    [InlineData("price_changes.csv", "MQ-03,2025-03-01", "MQ-03,2025-02-01", "is not after commissioning")]
    [InlineData("store.json", "\"manager_role\": \"owner\"", "\"manager_role\": \"boss\"", "no staff member has the manager_role 'boss'")]
    public void Catalogue_rules_are_reported_with_where_they_broke(string file, string find, string replace, string expected)
    {
        using var scratch = new ScratchDirectory();
        var configPath = scratch.CopyMini();
        scratch.Edit(Path.Combine("catalogues", "quincaillerie", file), find, replace);

        var problems = new List<string>();
        try
        {
            var (config, catalogue, window) = TestInputs.Load(configPath);
            problems = TestInputs.Problems(config, catalogue, window);
        }
        catch (GeneratorInputException error)
        {
            problems.AddRange(error.Problems);
        }

        Assert.Contains(problems, p => p.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void A_barcode_with_a_wrong_check_digit_is_refused()
    {
        using var scratch = new ScratchDirectory();
        var configPath = scratch.CopyMini();
        var (_, original, _) = TestInputs.Load(configPath);
        var barcode = original.Variants[0].Barcode;
        var wrong = barcode[..12] + (char)('0' + ((barcode[12] - '0' + 1) % 10));
        scratch.Edit(Path.Combine("catalogues", "quincaillerie", "catalogue.csv"), barcode, wrong);

        var (config, catalogue, window) = TestInputs.Load(configPath);
        Assert.Contains(TestInputs.Problems(config, catalogue, window), p => p.Contains("not a valid EAN-13", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_problem_is_reported_not_just_the_first()
    {
        using var scratch = new ScratchDirectory();
        var configPath = scratch.CopyMini();
        scratch.Edit(Path.Combine("catalogues", "quincaillerie", "catalogue.csv"), ",Q-OUT,boite,6,,0.6,", ",Q-XXX,boite,6,,0.6,");
        scratch.Edit(Path.Combine("catalogues", "quincaillerie", "catalogue.csv"), ",1.1,tools,", ",1.1,outils,");

        var (config, catalogue, window) = TestInputs.Load(configPath);
        var problems = TestInputs.Problems(config, catalogue, window);

        Assert.Contains(problems, p => p.Contains("Q-XXX", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("outils", StringComparison.Ordinal));
    }

    [Fact]
    public void An_input_open_for_writing_in_another_program_still_loads()
    {
        // Reviewing catalogue.csv in Excel while running the generator is the normal workflow;
        // Excel holds the file open for writing.
        using var scratch = new ScratchDirectory();
        var configPath = scratch.CopyMini();
        var csv = Path.Combine(scratch.Path, "mini", "catalogues", "quincaillerie", "catalogue.csv");

        using (new FileStream(csv, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            var (_, catalogue, _) = TestInputs.Load(configPath);
            Assert.Equal(6, catalogue.Variants.Count);
        }
    }

    // ------------------------------------------------------------ CSV

    [Fact]
    public void A_quoted_field_keeps_its_commas_quotes_and_line_breaks()
    {
        var table = CsvTable.Parse(
            "t.csv",
            "\uFEFFa,b\r\n\"Café, Thé & Petit Déjeuner\",\"say \"\"hi\"\"\"\r\n\"two\nlines\",x\n",
            ["a", "b"]);

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("Café, Thé & Petit Déjeuner", table.Rows[0]["a"]);
        Assert.Equal("say \"hi\"", table.Rows[0]["b"]);
        Assert.Equal("two\nlines", table.Rows[1]["a"]);
        Assert.Equal(3, table.Rows[1].Line);
    }

    [Fact]
    public void An_unquoted_comma_is_reported_with_its_line()
    {
        // Exactly the defect in the original products.csv.
        var error = Assert.Throws<GeneratorInputException>(() =>
            CsvTable.Parse("t.csv", "category,product\nCafé, Thé & Petit Déjeuner,Facto\n", ["category", "product"]));

        Assert.Contains("t.csv line 2: 3 fields where the header has 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_and_unexpected_columns_are_both_reported()
    {
        var error = Assert.Throws<GeneratorInputException>(() => CsvTable.Parse("t.csv", "a,c\n1,2\n", ["a", "b"]));

        Assert.Contains(error.Problems, p => p.Contains("missing column 'b'", StringComparison.Ordinal));
        Assert.Contains(error.Problems, p => p.Contains("unexpected column 'c'", StringComparison.Ordinal));
    }
}
