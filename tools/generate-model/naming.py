"""snake_case to C#, and the handful of table names the default rule gets wrong."""
import re

# Singularising "units_of_measure" needs the first word, not the last, and a
# few names are already singular or uncountable.
TABLE_OVERRIDES = {
    "units_of_measure": "UnitOfMeasure",
    "staff": "Staff",
    "inbox": "InboxMessage",
    "outbox": "OutboxMessage",
    "sync_state": "SyncState",
    "system_config": "SystemConfigEntry",
    "erasure_ledger": "ErasureLedgerEntry",
    "processing_log": "ProcessingLogEntry",
    "parameter_registry": "ParameterRegistryEntry",
    "product_category": "ProductCategory",
    "promotion_product": "PromotionProduct",
    "promotion_variant": "PromotionVariant",
    "supplier_variant": "SupplierVariant",
    # CA1711 forbids a type name ending in "Attribute": it would read as a
    # real .NET attribute. This is the link between a category and an
    # attribute definition, so Link says what it is.
    "category_attributes": "CategoryAttributeLink",
    "schema_migrations": "SchemaMigration",
    # CA1716: "Return" is a reserved word in VB. The section is Sales, so
    # the qualifier is the schema's own.
    "returns": "SalesReturn",
}

SECTION_FOLDERS = {
    "REFERENCE AND CONFIGURATION": "Reference",
    "ORGANISATION": "Organisation",
    "CATALOGUE": "Catalogue",
    "PRICING AND PROMOTIONS": "Pricing",
    "SUPPLIERS AND PURCHASING": "Purchasing",
    "INVENTORY": "Inventory",
    "CUSTOMERS AND COMPLIANCE": "Customers",
    "SALES": "Sales",
    "LEDGERS": "Ledgers",
    "ENGINE AND INTEGRATION LAYER": "Engine",
    "SYNC": "Sync",
    "SCHEMA VERSION": "Reference",
}


def singular(word: str) -> str:
    if word.endswith("ies"):
        return word[:-3] + "y"
    if re.search(r"(ss|ch|sh|x)es$", word):
        return word[:-2]
    if word.endswith("s") and not word.endswith("ss"):
        return word[:-1]
    return word


def pascal(text: str) -> str:
    return "".join(part[:1].upper() + part[1:] for part in text.split("_") if part)


def entity_name(table: str) -> str:
    if table in TABLE_OVERRIDES:
        return TABLE_OVERRIDES[table]
    words = table.split("_")
    words[-1] = singular(words[-1])
    return pascal("_".join(words))


def property_name(column: str, entity: str) -> str:
    name = pascal(column)
    # A property may not share its type's name.
    return name + "Value" if name == entity else name


def enum_member(value: str) -> str:
    return pascal(value)


def _words(pascal_name: str) -> list[str]:
    import re as _re
    return _re.findall(r"[A-Z][a-z0-9]*", pascal_name)


def qualified_enum_name(entity: str, column: str) -> str:
    """Entity name plus column name, without the stutter.

    CashMovement + movement_type is CashMovementType, not
    CashMovementMovementType: the trailing word of one is the leading word of
    the other, and repeating it reads like a typo.
    """
    left, right = _words(entity), _words(pascal(column))
    overlap = 0
    for k in range(min(len(left), len(right)), 0, -1):
        if [w.lower() for w in left[-k:]] == [w.lower() for w in right[:k]]:
            overlap = k
            break
    return "".join(left + right[overlap:])
