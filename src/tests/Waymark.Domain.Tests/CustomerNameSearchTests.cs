using Waymark.Domain.Customers;

namespace Waymark.Domain.Tests;

/// <summary>
/// D-100: a customer found by name at the till. The silent failures: half a name listing every
/// Samira in the shop; "Benali" finding "Benaliche"; a whole number shown to whoever types a name.
/// </summary>
public sealed class CustomerNameSearchTests
{
    [Theory]
    [InlineData("Samira Benali", true)]
    [InlineData("  samira   BENALI ", true)]
    [InlineData("Samira", false)]          // a first name alone
    [InlineData("Samira B", false)]        // an initial is not a name
    [InlineData("Sa Be", true)]            // two letters each: the shortest full name
    [InlineData("Samira 0550", false)]     // digits are not a name
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_full_name_searches(string? typed, bool full)
    {
        Assert.Equal(full, CustomerNameSearch.IsFullName(typed));
    }

    [Theory]
    [InlineData("samira benali", "Samira Benali", true)]
    [InlineData("benali samira", "Samira Benali", true)]           // the order typed does not matter
    [InlineData("SAMIRA BENALI", "Samira Benali Kaci", true)]      // every word typed is one of hers
    [InlineData("samira benali", "Samira Benaliche", false)]       // whole words, never the start of one
    [InlineData("samira benali", "Samir Benali", false)]
    [InlineData("helene ait", "Hélène Aït-Ahmed", true)]            // accents and hyphens as the product search reads them
    [InlineData("samira", "Samira Benali", false)]                 // not a full name: nothing matches
    public void Every_word_typed_is_a_whole_word_of_the_name(string typed, string name, bool matches)
    {
        Assert.Equal(matches, CustomerNameSearch.Matches(typed, name));
    }

    [Theory]
    [InlineData("+213550123456", "•••• •• 34 56")]
    [InlineData("0550123456", "•••• •• 34 56")]
    [InlineData("12", "••••")]
    [InlineData(null, null)]
    public void A_name_search_shows_the_last_four_digits_only(string? phone, string? masked)
    {
        Assert.Equal(masked, CustomerNameSearch.Masked(phone));
    }

    [Fact]
    public void Five_is_the_most_ever_listed()
    {
        Assert.Equal(5, CustomerNameSearch.MaximumShown);
    }
}
