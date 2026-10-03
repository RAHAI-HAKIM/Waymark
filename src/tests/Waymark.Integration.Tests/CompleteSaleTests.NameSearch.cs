using Microsoft.EntityFrameworkCore;
using Waymark.Application.Customers;
using Waymark.Domain.Enums;
using Waymark.Persistence.Customers;
using Waymark.Persistence.Organisation;

namespace Waymark.Integration.Tests;

/// <summary>
/// D-100: customers found by their full name, against a real database. The silent failures: a list
/// of strangers shown, and logged, for a common name; a whole number handed to whoever types a name.
/// </summary>
public sealed partial class CompleteSaleTests
{
    /// <summary>A surname no other test has: customers are the tenant's, shared by this class's database.</summary>
    private static string Surname() => "Zed" + new string([.. Guid.NewGuid().ToString("N")[..8].Select(c => (char)('a' + (c % 26)))]);

    private Task<IReadOnlyList<CustomerSummary>> FindByName(Shop shop, string name) =>
        Run(shop, (context, _) => new FindCustomersHandler(
                new CustomerLedger(context), new TenantConfigurationStore(context, TabChargesFor.Dzd), TabChargesFor.Pseudonymiser, TabChargesFor.Dzd),
            new FindCustomers(shop.StaffId, string.Empty, shop.TerminalId, name));

    [Fact]
    public async Task A_full_name_finds_the_customer_with_the_number_masked_and_the_look_logged()
    {
        var shop = new Shop(database);
        await ConfigureAsync(shop);
        var surname = Surname();
        var amel = await Create(shop, $"Amel {surname}", "0550123456");

        var found = Assert.Single(await FindByName(shop, $"amel {surname.ToUpperInvariant()}"));

        Assert.Equal((amel.CustomerId, "•••• •• 34 56"), (found.CustomerId, found.Phone));
        using var read = Read(shop);
        Assert.Equal(1, await read.ProcessingLog.CountAsync(entry => entry.SubjectId == PseudonymOf(amel.CustomerId) && entry.Operation == Operation.Consultation));
    }

    [Fact]
    public async Task Half_a_name_is_refused_and_nobody_is_looked_at()
    {
        var shop = new Shop(database);
        await ConfigureAsync(shop);
        var amel = await Create(shop, $"Amel {Surname()}", NewPhone());

        var refused = await Assert.ThrowsAsync<CustomerRefusedException>(() => FindByName(shop, "Amel"));

        Assert.Equal(CustomerRefusal.Invalid, refused.Refusal);
        using var read = Read(shop);
        Assert.False(await read.ProcessingLog.AnyAsync(entry => entry.SubjectId == PseudonymOf(amel.CustomerId) && entry.Operation == Operation.Consultation));
    }

    [Fact]
    public async Task More_than_five_with_the_name_lists_nobody_and_logs_nobody()
    {
        var shop = new Shop(database);
        await ConfigureAsync(shop);
        var surname = Surname();
        var six = new List<CustomerSummary>();
        for (var i = 0; i < 6; i++)
        {
            six.Add(await Create(shop, $"Nour {surname}", NewPhone()));
        }

        var refused = await Assert.ThrowsAsync<CustomerRefusedException>(() => FindByName(shop, $"Nour {surname}"));

        Assert.Equal(CustomerRefusal.TooMany, refused.Refusal);
        using var read = Read(shop);
        var pseudonyms = six.Select(customer => PseudonymOf(customer.CustomerId)).ToList();
        Assert.False(await read.ProcessingLog.AnyAsync(entry => pseudonyms.Contains(entry.SubjectId!) && entry.Operation == Operation.Consultation));
    }
}
