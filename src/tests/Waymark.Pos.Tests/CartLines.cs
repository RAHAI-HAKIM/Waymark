using Waymark.Pos.Checkout;

namespace Waymark.Pos.Tests;

/// <summary>
/// Finding a line by its product, for tests that scan each product once (D-087: the till names a
/// line by its own id, because two lines can hold the same product).
/// </summary>
internal static class CartLines
{
    /// <summary>The latest line of this product still in the sale.</summary>
    public static string LineOf(this Cart cart, string variantId) =>
        cart.Lines.Last(line => line.VariantId == variantId && !line.IsRemoved).LineId;
}
