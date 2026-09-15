using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Customers;
using Waymark.Domain.Engine;
using Waymark.Domain.Enums;
using Waymark.Domain.Ids;
using Waymark.Domain.Organisation;
using Waymark.Domain.Pricing;
using Waymark.Domain.Purchasing;
using Waymark.Domain.Reference;
using Waymark.Domain.Values;
using Waymark.Generator.Calendar;
using Waymark.Generator.Catalogues;
using Waymark.Generator.Configuration;
using Waymark.Generator.Randomness;
using Waymark.Generator.Writing;

namespace Waymark.Generator.Simulation;

/// <summary>
/// Commissioning: everything a store has before its first sale (W10 S4).
///
/// <para>
/// Written at the commissioning date, in stages ordered by foreign key, with the clock
/// advancing through them so every id carries the moment its row was created. Customers
/// enrol over the commissioning weeks, in time order.
/// </para>
/// </summary>
internal static class ReferenceData
{
    /// <summary>
    /// A PIN hash nobody can log in with. Synthetic staff are records, not accounts; a real
    /// hash of a guessable PIN in demo data would be a credential waiting to be copied.
    /// </summary>
    public const string SyntheticPinHash = "synthetic:no-login";

    /// <summary>Commissioning happens at 08:00 store time; the store's own id is minted then.</summary>
    public const int CommissioningMinute = 8 * 60;

    public static GeneratedStore Write(
        StoreDatabase database,
        string storeId,
        Catalogue catalogue,
        GeneratorConfig config,
        RunWindow window,
        CalendarModel calendar,
        SimulatedClock clock,
        IIdGenerator ids,
        RandomSource random)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(random);

        var context = database.Context;
        var profile = catalogue.Store;
        var currency = Currency.FromCode(profile.Currency);
        var at = calendar.ToUtc(window.CommissioningDate, CommissioningMinute);
        clock.AdvanceTo(at);

        // 1. The store, without a manager: stores.manager_id and staff.store_id reference each
        //    other, and SQLite checks a foreign key per statement.
        context.Stores.Add(new Store
        {
            StoreId = storeId,
            StoreCode = profile.StoreCode,
            StoreName = profile.StoreName,
            StoreType = profile.StoreType,
            Address = profile.Address,
            Currency = currency.Code,
            RoundingPolicy = profile.RoundingPolicy,
            CreatedAt = at,
            UpdatedAt = at,
        });
        database.Save();

        // 2. Reference tables with natural keys.
        context.Roles.AddRange(profile.Roles.Select(role => new Role
        {
            RoleCode = role.Code,
            Rank = role.Rank,
            LabelFr = role.LabelFr,
            LabelAr = role.LabelAr,
            CreatedAt = at,
        }));

        context.UnitsOfMeasure.AddRange(catalogue.Units.Select(unit => new UnitOfMeasure
        {
            UnitCode = unit.Code,
            NameFr = unit.NameFr,
            NameAr = unit.NameAr,
            Dimension = unit.Dimension,
            BaseUnitCode = unit.BaseUnitCode,
            FactorToBase = unit.FactorToBase,
            DecimalPlaces = unit.DecimalPlaces,
            CreatedAt = at,
        }));

        context.ReasonCodes.AddRange(profile.ReasonCodes.Select((reason, order) => new ReasonCode
        {
            ReasonCodeValue = reason.Code,
            AppliesTo = reason.AppliesTo,
            LabelFr = reason.LabelFr,
            LabelAr = reason.LabelAr,
            RequiresNote = reason.RequiresNote,
            RequiresManager = reason.RequiresManager,
            DisplayOrder = order,
            CreatedAt = at,
        }));

        context.NoticeVersions.AddRange(profile.Notices.Select(notice => new NoticeVersion
        {
            VersionCode = notice.Code,
            NoticeType = notice.Type,
            Language = notice.Language,
            BodyText = notice.Body,
            EffectiveFrom = Date(window.CommissioningDate),
            PublishedAt = at,
        }));
        database.Save();

        var notices = profile.Notices
            .GroupBy(notice => notice.Type)
            .ToDictionary(group => group.Key, group => group.First().Code);

        // 3. People and tills, then the store's manager.
        var staff = new List<GeneratedStaff>();
        foreach (var person in profile.Staff)
        {
            var member = new GeneratedStaff(ids.NewId(), person.Name, person.Role);
            staff.Add(member);
            context.Staff.Add(new Staff
            {
                StaffId = member.StaffId,
                StoreId = storeId,
                StaffName = person.Name,
                Role = person.Role,
                PinHash = SyntheticPinHash,
                JoinDate = person.Joined,
                NoticeVersionAcknowledged = notices[NoticeType.Staff],
                NoticeAcknowledgedAt = at,
                CreatedAt = at,
                UpdatedAt = at,
            });
        }

        var terminalIds = new List<string>();
        foreach (var name in profile.Terminals)
        {
            var terminalId = ids.NewId();
            terminalIds.Add(terminalId);
            context.Terminals.Add(new Terminal
            {
                TerminalId = terminalId,
                StoreId = storeId,
                TerminalName = name,
                CreatedAt = at,
                UpdatedAt = at,
            });
        }

        database.Save();

        var manager = staff.First(member => string.Equals(member.Role, profile.ManagerRole, StringComparison.Ordinal));
        context.Stores
            .Where(store => store.StoreId == storeId)
            .ExecuteUpdate(setters => setters.SetProperty(store => store.ManagerId, manager.StaffId));

        // 4. Categories, parents first: categories.parent_id references the same table.
        var categoryIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in catalogue.Categories)
        {
            categoryIds[name] = ids.NewId();
            context.Categories.Add(new Category
            {
                CategoryId = categoryIds[name],
                CategoryName = name,
                Slug = Slug(name),
                CreatedAt = at,
                UpdatedAt = at,
            });
        }

        database.Save();

        var subcategoryIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var vatBySubcategory = new Dictionary<string, BasisPoints>(StringComparer.Ordinal);
        foreach (var subcategory in catalogue.Subcategories)
        {
            subcategoryIds[subcategory.Subcategory] = ids.NewId();
            vatBySubcategory[subcategory.Subcategory] = subcategory.Vat;
            context.Categories.Add(new Category
            {
                CategoryId = subcategoryIds[subcategory.Subcategory],
                CategoryName = subcategory.Subcategory,
                ParentId = categoryIds[subcategory.Category],
                Slug = Slug(subcategory.Category) + "--" + Slug(subcategory.Subcategory),
                TaxRate = subcategory.Vat.Value,
                SensitiveFlag = subcategory.Sensitive,
                SensitiveReason = subcategory.SensitiveReason,
                CreatedAt = at,
                UpdatedAt = at,
            });
        }

        database.Save();

        // 5. Suppliers, products, variants, supplier terms and prices.
        var supplierIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var supplier in catalogue.Suppliers)
        {
            supplierIds[supplier.Code] = ids.NewId();
            context.Suppliers.Add(new Supplier
            {
                SupplierId = supplierIds[supplier.Code],
                SupplierCode = supplier.Code,
                CompanyName = supplier.CompanyName,
                NetDays = supplier.NetDays,
                Currency = currency.Code,
                CreatedAt = at,
                UpdatedAt = at,
            });
        }

        var suppliersByCode = catalogue.Suppliers.ToDictionary(s => s.Code, StringComparer.Ordinal);
        var unitsByCode = catalogue.Units.ToDictionary(u => u.Code, StringComparer.Ordinal);
        var changesBySku = catalogue.PriceChanges.ToLookup(change => change.Sku, StringComparer.Ordinal);
        var productIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var variants = new List<GeneratedVariant>();

        foreach (var product in catalogue.Products)
        {
            productIds[product.Name] = ids.NewId();
            context.Products.Add(new Product
            {
                ProductId = productIds[product.Name],
                ProductName = product.Name,
                CreatedAt = at,
                UpdatedAt = at,
            });

            // Linked to its subcategory as primary and to the top category as well: sensitive
            // exclusion is evaluated across all links (CLAUDE.md §4), so the data has more than one.
            context.ProductCategory.Add(new ProductCategory
            {
                ProductId = productIds[product.Name],
                CategoryId = subcategoryIds[product.Subcategory],
                IsPrimary = true,
                AddedAt = at,
            });
            context.ProductCategory.Add(new ProductCategory
            {
                ProductId = productIds[product.Name],
                CategoryId = categoryIds[product.Variants[0].Category],
                IsPrimary = false,
                AddedAt = at,
            });
        }

        foreach (var (item, index) in catalogue.Variants.Select((item, index) => (item, index)))
        {
            var variantId = ids.NewId();
            var supplier = suppliersByCode[item.SupplierCode];
            var pack = item.UnitsPerPurchaseUnit;

            context.Variants.Add(new Variant
            {
                VariantId = variantId,
                ProductId = productIds[item.ProductName],
                VariantName = item.VariantName,
                Barcode = item.Barcode,
                Sku = item.Sku,
                SellingUnitCode = item.SellingUnit,
                CreatedAt = at,
                UpdatedAt = at,
            });

            context.SupplierVariant.Add(new SupplierVariant
            {
                SupplierId = supplierIds[item.SupplierCode],
                VariantId = variantId,
                PurchaseUnitCode = item.PurchaseUnit,
                UnitsPerPurchaseUnit = checked(pack * Quantity.Scale),
                MinimumOrderQuantity = Quantity.Scale,
                StatedLeadTimeDays = supplier.StatedLeadTimeDays,
                PurchasePrice = item.PurchasePrice * pack,
                Currency = currency.Code,
                NetDays = supplier.NetDays,
                LastPriceAt = at,
                CreatedAt = at,
                UpdatedAt = at,
            });

            var prices = new List<PricePoint> { new(window.CommissioningDate, item.RetailPrice, item.PurchasePrice) };
            prices.AddRange(changesBySku[item.Sku].Select(change => new PricePoint(change.EffectiveDate, change.RetailPrice, change.PurchasePrice)));

            for (var i = 0; i < prices.Count; i++)
            {
                context.Prices.Add(new Price
                {
                    VariantId = variantId,
                    StoreId = storeId,
                    ValidFrom = Date(prices[i].From),
                    ValidTo = i + 1 < prices.Count ? Date(prices[i + 1].From) : null,
                    PriceValue = prices[i].Retail,
                    Currency = currency.Code,
                    IsTaxInclusive = true,
                    CreatedBy = manager.StaffId,

                    // The row is keyed in the morning the price takes effect; a key-less table,
                    // so writing a future-dated row now mints nothing out of order.
                    CreatedAt = i == 0 ? at : calendar.ToUtc(prices[i].From, 7 * 60),
                });
            }

            variants.Add(new GeneratedVariant
            {
                Index = index,
                VariantId = variantId,
                ProductId = productIds[item.ProductName],
                SupplierId = supplierIds[item.SupplierCode],
                Catalogue = item,
                Vat = vatBySubcategory[item.Subcategory],
                SellingUnit = UnitPrecision.For(item.SellingUnit, unitsByCode[item.SellingUnit].DecimalPlaces),
                Prices = prices,
            });
        }

        database.Save();

        // 6. Customers, enrolled over the commissioning weeks in time order.
        var customers = WriteCustomers(database, config.Customers, window, calendar, clock, ids, random, staff, terminalIds[0], notices);

        return new GeneratedStore
        {
            StoreId = storeId,
            StoreCode = profile.StoreCode,
            RoundingPolicy = profile.RoundingPolicy,
            Currency = currency,
            TerminalIds = terminalIds,
            Staff = staff,
            Manager = manager,
            SupplierIds = supplierIds,
            Suppliers = [.. catalogue.Suppliers.Select((supplier, index) =>
                new GeneratedSupplier(index, supplier.Code, supplierIds[supplier.Code], supplier.DeliveryDays, supplier.StatedLeadTimeDays))],
            Subcategories = [.. catalogue.Subcategories.Select(subcategory => (subcategory.Subcategory, subcategoryIds[subcategory.Subcategory]))],
            ReasonCodes = profile.ReasonCodes.ToDictionary(reason => reason.Code, StringComparer.Ordinal),
            ProductIds = productIds,
            Variants = variants,
            Customers = customers,
            Notices = notices,
        };
    }

    /// <summary>
    /// The initial customer base. Non-objectors consent to processing, and a share also to
    /// marketing, each recorded as its own consent event (CLAUDE.md §4: two consents). An
    /// objector keeps an account — credit is a contract — with no consent and the objection
    /// flag set, so the downgrade path (D-043) has someone to downgrade.
    /// </summary>
    private static List<GeneratedCustomer> WriteCustomers(
        StoreDatabase database,
        CustomerSettings settings,
        RunWindow window,
        CalendarModel calendar,
        SimulatedClock clock,
        IIdGenerator ids,
        RandomSource random,
        List<GeneratedStaff> staff,
        string terminalId,
        Dictionary<NoticeType, string> notices)
    {
        var enrolment = random.Stream("customer_enrolment");
        var traits = random.Stream("customer_traits");
        var context = database.Context;
        var commissioningDays = window.FirstDay.DayNumber - window.CommissioningDate.DayNumber;

        var schedule = Enumerable.Range(0, settings.InitialCount.Value)
            .Select(i =>
            {
                var day = window.CommissioningDate.AddDays(Distributions.UniformInt(enrolment.Uniform(i, 0), 0, commissioningDays - 1));
                var minute = Distributions.UniformInt(enrolment.Uniform(i, 1), 9 * 60, (20 * 60) - 1);
                return (Draw: i, Day: day, At: calendar.ToUtc(day, minute));
            })
            .OrderBy(entry => entry.At)
            .ThenBy(entry => entry.Draw)
            .ToList();

        var customers = new List<GeneratedCustomer>();
        foreach (var (draw, day, at) in schedule)
        {
            clock.AdvanceTo(at);

            var number = customers.Count + 1;
            var objects = Distributions.Bernoulli(traits.Uniform(draw, 0), settings.ObjectionShare.Value);
            var marketing = !objects && Distributions.Bernoulli(traits.Uniform(draw, 1), settings.MarketingConsentShare.Value);
            var french = Distributions.Bernoulli(traits.Uniform(draw, 2), settings.FrenchLanguageShare.Value);
            var capturedBy = staff[Distributions.UniformInt(traits.Uniform(draw, 3), 0, staff.Count - 1)].StaffId;
            var method = Distributions.Bernoulli(traits.Uniform(draw, 4), 0.7) ? Method.Verbal : Method.Written;

            var customerId = ids.NewId();
            context.Customers.Add(new Customer
            {
                CustomerId = customerId,
                CustomerName = string.Create(CultureInfo.InvariantCulture, $"Client {number:000} (synthétique)"),
                PreferredLanguage = french ? PreferredLanguage.Fr : PreferredLanguage.Ar,
                JoinDate = day,
                LegalBasis = LegalBasis.Contract,
                ConsentProfiling = !objects,
                ConsentProfilingAt = objects ? null : at,
                ConsentProfilingNoticeVersion = objects ? null : notices[NoticeType.Processing],
                ConsentMarketing = marketing,
                ConsentMarketingAt = marketing ? at : null,
                ConsentMarketingNoticeVersion = marketing ? notices[NoticeType.Marketing] : null,
                ObjectionFlag = objects,
                CreatedAt = at,
                UpdatedAt = at,
            });

            if (!objects)
            {
                context.ConsentEvents.Add(Consent(ids.NewId(), customerId, at, ConsentType.Processing, notices[NoticeType.Processing], capturedBy, method, terminalId));
            }

            if (marketing)
            {
                context.ConsentEvents.Add(Consent(ids.NewId(), customerId, at, ConsentType.Marketing, notices[NoticeType.Marketing], capturedBy, method, terminalId));
            }

            customers.Add(new GeneratedCustomer(customerId, number, marketing, objects));
        }

        database.Save();
        return customers;
    }

    private static ConsentEvent Consent(string id, string customerId, DateTimeOffset at, ConsentType type, string notice, string capturedBy, Method method, string terminalId) => new()
    {
        ConsentEventId = id,
        CustomerId = customerId,
        OccurredAt = at,
        Action = ConsentEventAction.Granted,
        ConsentType = type,
        NoticeVersion = notice,
        CapturedBy = capturedBy,
        Method = method,
        TerminalId = terminalId,
    };

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// "Café, Thé &amp; Petit Déjeuner" becomes "cafe-the-petit-dejeuner". Accents are folded
    /// by a table rather than Unicode normalisation, which the build's invariant globalisation
    /// does not promise on every platform.
    /// </summary>
    internal static string Slug(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var slug = new StringBuilder(name.Length);
        var dash = false;

        foreach (var original in name)
        {
            var c = Fold(char.ToLowerInvariant(original));

            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(c);
                dash = false;
            }
            else if (!dash && slug.Length > 0)
            {
                slug.Append('-');
                dash = true;
            }
        }

        return slug.ToString().TrimEnd('-');
    }

    private static char Fold(char c) => c switch
    {
        'à' or 'â' or 'ä' or 'á' or 'ã' or 'å' => 'a',
        'ç' => 'c',
        'é' or 'è' or 'ê' or 'ë' => 'e',
        'í' or 'ì' or 'î' or 'ï' => 'i',
        'ñ' => 'n',
        'ó' or 'ò' or 'ô' or 'ö' or 'õ' or 'œ' => 'o',
        'ú' or 'ù' or 'û' or 'ü' => 'u',
        'ý' or 'ÿ' => 'y',
        _ => c,
    };
}
