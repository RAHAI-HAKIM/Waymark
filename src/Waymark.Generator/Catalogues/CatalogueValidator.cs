using Waymark.Domain.Enums;
using Waymark.Domain.Values;
using Waymark.Generator.Configuration;

namespace Waymark.Generator.Catalogues;

/// <summary>
/// The referential and business checks on a loaded catalogue (D-046 §9): every variant
/// resolves a category, a unit, a VAT class, a supplier and a seasonality profile; every
/// supplier has a delivery day; identifiers are unique. Runs before a single row is
/// written, and reports every problem at once.
/// </summary>
internal static class CatalogueValidator
{
    public static List<string> Validate(Catalogue catalogue, GeneratorConfig config, RunWindow window)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(window);

        var problems = new List<string>();
        ValidateStore(catalogue.Store, window, problems);

        var units = UniqueBy(catalogue.Units, u => u.Code, u => u.Where, "unit_code", problems);
        foreach (var unit in catalogue.Units)
        {
            if (unit.BaseUnitCode is not null && !units.ContainsKey(unit.BaseUnitCode))
            {
                problems.Add($"{unit.Where}: base_unit_code '{unit.BaseUnitCode}' is not a unit in units.csv.");
            }

            if (unit.FactorToBase <= 0)
            {
                problems.Add($"{unit.Where}: factor_to_base must be positive.");
            }

            if (unit.DecimalPlaces is < 0 or > UnitPrecision.MaxDecimalPlaces)
            {
                problems.Add($"{unit.Where}: decimal_places must be 0 to {UnitPrecision.MaxDecimalPlaces}.");
            }
        }

        var subcategories = UniqueBy(catalogue.Subcategories, s => s.Subcategory, s => s.Where, "subcategory", problems);
        foreach (var subcategory in catalogue.Subcategories.Where(s => s.Sensitive && s.SensitiveReason is null))
        {
            problems.Add($"{subcategory.Where}: a sensitive subcategory needs a sensitive_reason.");
        }

        foreach (var category in catalogue.Categories.Where(c => subcategories.ContainsKey(c)))
        {
            problems.Add($"categories.csv: '{category}' is both a category and a subcategory name; slugs would collide.");
        }

        var suppliers = UniqueBy(catalogue.Suppliers, s => s.Code, s => s.Where, "supplier_code", problems);
        foreach (var supplier in catalogue.Suppliers)
        {
            if (supplier.DeliveryDays.Count == 0)
            {
                problems.Add($"{supplier.Where}: supplier {supplier.Code} has no delivery day, so nothing could ever be ordered from it.");
            }

            if (supplier.StatedLeadTimeDays < 0 || supplier.NetDays < 0)
            {
                problems.Add($"{supplier.Where}: lead time and net days are at least 0.");
            }
        }

        if (catalogue.Variants.Count == 0)
        {
            problems.Add("catalogue.csv has no variants.");
        }

        var variants = UniqueBy(catalogue.Variants, v => v.Sku, v => v.Where, "sku", problems);
        UniqueBy(catalogue.Variants, v => v.Barcode, v => v.Where, "barcode", problems);

        foreach (var v in catalogue.Variants)
        {
            if (!Ean13.IsValid(v.Barcode))
            {
                problems.Add($"{v.Where}: barcode '{v.Barcode}' is not a valid EAN-13 (thirteen digits with a correct check digit).");
            }

            if (!subcategories.TryGetValue(v.Subcategory, out var subcategory))
            {
                problems.Add($"{v.Where}: subcategory '{v.Subcategory}' is not in categories.csv, so the variant has no VAT class.");
            }
            else if (!string.Equals(subcategory.Category, v.Category, StringComparison.Ordinal))
            {
                problems.Add($"{v.Where}: subcategory '{v.Subcategory}' belongs to '{subcategory.Category}', not '{v.Category}'.");
            }

            if (!suppliers.ContainsKey(v.SupplierCode))
            {
                problems.Add($"{v.Where}: supplier '{v.SupplierCode}' is not in suppliers.csv.");
            }

            foreach (var (column, code) in new[] { ("selling_unit", v.SellingUnit), ("purchase_unit", v.PurchaseUnit) })
            {
                if (!units.ContainsKey(code))
                {
                    problems.Add($"{v.Where}: {column} '{code}' is not in units.csv.");
                }
            }

            if (units.TryGetValue(v.SellingUnit, out var selling) && selling.DecimalPlaces != 0)
            {
                problems.Add($"{v.Where}: selling unit '{v.SellingUnit}' is divisible; this generator sells whole units only.");
            }

            if (!config.SeasonalityProfiles.ContainsKey(v.SeasonalityProfile))
            {
                problems.Add($"{v.Where}: seasonality_profile '{v.SeasonalityProfile}' is not defined in the configuration.");
            }

            if (v.UnitsPerPurchaseUnit < 1)
            {
                problems.Add($"{v.Where}: units_per_purchase_unit must be at least 1.");
            }

            if (v.ShelfLifeDays is < 1)
            {
                problems.Add($"{v.Where}: shelf_life_days must be at least 1, or empty for a non-perishable.");
            }

            if (!(v.BaseDailyRate > 0))
            {
                problems.Add($"{v.Where}: base_daily_rate must be positive; a variant that never sells has no place in the catalogue.");
            }

            if (v.SubstitutabilityTier is < 1 or > 3)
            {
                problems.Add($"{v.Where}: substitutability_tier must be 1, 2 or 3.");
            }

            if (subcategory is not null)
            {
                CheckPrices(v.Where, v.RetailPrice, v.PurchasePrice, subcategory.Vat, catalogue.Store.RoundingPolicy, problems);
            }
        }

        foreach (var product in catalogue.Products)
        {
            if (product.Variants.Any(v => !string.Equals(v.Subcategory, product.Subcategory, StringComparison.Ordinal)))
            {
                problems.Add($"catalogue.csv: product '{product.Name}' has variants in more than one subcategory.");
            }

            if (product.Variants.Any(v => !string.Equals(v.SupplierCode, product.SupplierCode, StringComparison.Ordinal)))
            {
                problems.Add($"catalogue.csv: product '{product.Name}' has variants from more than one supplier; a batch is per product.");
            }
        }

        var lastChange = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        foreach (var change in catalogue.PriceChanges)
        {
            if (!variants.TryGetValue(change.Sku, out var variant))
            {
                problems.Add($"{change.Where}: sku '{change.Sku}' is not in catalogue.csv.");
                continue;
            }

            if (change.EffectiveDate <= window.CommissioningDate)
            {
                problems.Add($"{change.Where}: effective_date {change.EffectiveDate:yyyy-MM-dd} is not after commissioning ({window.CommissioningDate:yyyy-MM-dd}); the commissioning price is in catalogue.csv.");
            }

            if (lastChange.TryGetValue(change.Sku, out var previous) && change.EffectiveDate <= previous)
            {
                problems.Add($"{change.Where}: price changes for {change.Sku} must be in strictly increasing date order.");
            }

            lastChange[change.Sku] = change.EffectiveDate;

            if (subcategories.TryGetValue(variant.Subcategory, out var subcategory))
            {
                CheckPrices(change.Where, change.RetailPrice, change.PurchasePrice, subcategory.Vat, catalogue.Store.RoundingPolicy, problems);
            }
        }

        return problems;
    }

    private static void ValidateStore(StoreProfile store, RunWindow window, List<string> problems)
    {
        if (store.Terminals.Count == 0 || store.Terminals.Any(string.IsNullOrWhiteSpace))
        {
            problems.Add("store.json: terminals needs at least one named terminal.");
        }

        var roles = store.Roles.ToDictionary(r => r.Code, StringComparer.Ordinal);
        if (roles.Count != store.Roles.Count)
        {
            problems.Add("store.json: role codes must be unique.");
        }

        if (store.Roles.Select(r => r.Rank).Distinct().Count() != store.Roles.Count || store.Roles.Any(r => r.Rank < 1))
        {
            problems.Add("store.json: role ranks must be unique and at least 1.");
        }

        if (store.Staff.Count == 0)
        {
            problems.Add("store.json: staff needs at least one person.");
        }

        foreach (var staff in store.Staff)
        {
            if (!roles.ContainsKey(staff.Role))
            {
                problems.Add($"store.json: staff '{staff.Name}' has role '{staff.Role}', which roles does not define.");
            }

            if (staff.Joined > window.CommissioningDate)
            {
                problems.Add($"store.json: staff '{staff.Name}' joined {staff.Joined:yyyy-MM-dd}, after commissioning; they would appear before they were hired.");
            }
        }

        if (!store.Staff.Any(s => string.Equals(s.Role, store.ManagerRole, StringComparison.Ordinal)))
        {
            problems.Add($"store.json: no staff member has the manager_role '{store.ManagerRole}'.");
        }

        if (store.ReasonCodes.Select(r => r.Code).Distinct(StringComparer.Ordinal).Count() != store.ReasonCodes.Count)
        {
            problems.Add("store.json: reason codes must be unique.");
        }

        if (store.Notices.Select(n => n.Code).Distinct(StringComparer.Ordinal).Count() != store.Notices.Count)
        {
            problems.Add("store.json: notice codes must be unique.");
        }

        foreach (var type in Enum.GetValues<NoticeType>().Where(type => store.Notices.All(n => n.Type != type)))
        {
            problems.Add($"store.json: notices has no {type.ToString().ToLowerInvariant()} notice; consent and staff records need one of each.");
        }

        if (store.UtcOffsetHours is < -12 or > 14)
        {
            problems.Add($"store.json: utc_offset_hours is {store.UtcOffsetHours}; expected -12 to 14.");
        }
    }

    /// <summary>
    /// The purchase price must leave a margin on the retail price net of VAT. A catalogue that
    /// sells below cost is almost always a typo, and it would teach the engine nonsense margins.
    /// </summary>
    private static void CheckPrices(string where, Money retail, Money purchase, BasisPoints vat, Rounding policy, List<string> problems)
    {
        if (!retail.IsPositive)
        {
            problems.Add($"{where}: retail price must be positive.");
            return;
        }

        if (purchase.IsNegative)
        {
            problems.Add($"{where}: purchase price must not be negative.");
            return;
        }

        var net = retail.SplitTaxInclusive(vat, policy).Net;
        if (purchase >= net)
        {
            problems.Add($"{where}: purchase price {purchase} is not below the retail price net of VAT ({net}); the variant would sell at a loss.");
        }
    }

    private static Dictionary<string, T> UniqueBy<T>(IEnumerable<T> items, Func<T, string> key, Func<T, string> where, string name, List<string> problems)
    {
        var seen = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!seen.TryAdd(key(item), item))
            {
                problems.Add($"{where(item)}: {name} '{key(item)}' already appears at {where(seen[key(item)])}.");
            }
        }

        return seen;
    }
}

/// <summary>The EAN-13 check digit. Weights 1 and 3 from the left over the first twelve digits.</summary>
internal static class Ean13
{
    public static bool IsValid(string code)
    {
        if (code is null || code.Length != 13 || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            sum += (code[i] - '0') * (i % 2 == 0 ? 1 : 3);
        }

        return code[12] - '0' == (10 - (sum % 10)) % 10;
    }
}
