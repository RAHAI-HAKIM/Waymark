// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'accept_reorder', 'markdown_stage', 'adjust_quantity', 'dismiss', 'price_change', 'promotion', 'catalogue_edit', 'supplier_edit', 'rights_action'.
/// </summary>
public enum IntentType
{
    /// <summary>Stored as <c>accept_reorder</c>.</summary>
    AcceptReorder,

    /// <summary>Stored as <c>markdown_stage</c>.</summary>
    MarkdownStage,

    /// <summary>Stored as <c>adjust_quantity</c>.</summary>
    AdjustQuantity,

    /// <summary>Stored as <c>dismiss</c>.</summary>
    Dismiss,

    /// <summary>Stored as <c>price_change</c>.</summary>
    PriceChange,

    /// <summary>Stored as <c>promotion</c>.</summary>
    Promotion,

    /// <summary>Stored as <c>catalogue_edit</c>.</summary>
    CatalogueEdit,

    /// <summary>Stored as <c>supplier_edit</c>.</summary>
    SupplierEdit,

    /// <summary>Stored as <c>rights_action</c>.</summary>
    RightsAction
}
