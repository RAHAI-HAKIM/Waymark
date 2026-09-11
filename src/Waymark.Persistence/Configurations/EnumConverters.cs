// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Waymark.Domain.Enums;
using Waymark.Domain.Values;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Enum to TEXT, one converter per enum.
///
/// <para>
/// Written out rather than using <c>HasConversion&lt;string&gt;()</c>, which
/// stores the C# member name: "Standard" into a column whose CHECK
/// constraint only allows "standard". The sync channels settle it — their
/// values are <c>A_statistics</c> and <c>D_intents</c>, which no mechanical
/// rule produces.
/// </para>
/// <para>
/// Both directions throw on an unknown value. A row carrying a spelling the
/// model does not know is a real problem, and silently mapping it to the
/// first member would hide it.
/// </para>
/// </summary>
internal static class EnumConverters
{
    public static readonly ValueConverter<ActionOnExpiry, string> ActionOnExpiryConverter =
        new(value => ToDatabase(value), text => ToActionOnExpiry(text));

    public static readonly ValueConverter<ActionType, string> ActionTypeConverter =
        new(value => ToDatabase(value), text => ToActionType(text));

    public static readonly ValueConverter<ActorType, string> ActorTypeConverter =
        new(value => ToDatabase(value), text => ToActorType(text));

    public static readonly ValueConverter<AttributeDefinitionAppliesTo, string> AttributeDefinitionAppliesToConverter =
        new(value => ToDatabase(value), text => ToAttributeDefinitionAppliesTo(text));

    public static readonly ValueConverter<AttributeDefinitionDataType, string> AttributeDefinitionDataTypeConverter =
        new(value => ToDatabase(value), text => ToAttributeDefinitionDataType(text));

    public static readonly ValueConverter<AttributeDefinitionStatus, string> AttributeDefinitionStatusConverter =
        new(value => ToDatabase(value), text => ToAttributeDefinitionStatus(text));

    public static readonly ValueConverter<BarcodeType, string> BarcodeTypeConverter =
        new(value => ToDatabase(value), text => ToBarcodeType(text));

    public static readonly ValueConverter<BatchStatus, string> BatchStatusConverter =
        new(value => ToDatabase(value), text => ToBatchStatus(text));

    public static readonly ValueConverter<BundleType, string> BundleTypeConverter =
        new(value => ToDatabase(value), text => ToBundleType(text));

    public static readonly ValueConverter<CashMovementType, string> CashMovementTypeConverter =
        new(value => ToDatabase(value), text => ToCashMovementType(text));

    public static readonly ValueConverter<CashSessionStatus, string> CashSessionStatusConverter =
        new(value => ToDatabase(value), text => ToCashSessionStatus(text));

    public static readonly ValueConverter<CategoryStatus, string> CategoryStatusConverter =
        new(value => ToDatabase(value), text => ToCategoryStatus(text));

    public static readonly ValueConverter<ConsentEventAction, string> ConsentEventActionConverter =
        new(value => ToDatabase(value), text => ToConsentEventAction(text));

    public static readonly ValueConverter<ConsentType, string> ConsentTypeConverter =
        new(value => ToDatabase(value), text => ToConsentType(text));

    public static readonly ValueConverter<CountType, string> CountTypeConverter =
        new(value => ToDatabase(value), text => ToCountType(text));

    public static readonly ValueConverter<CreditMovementType, string> CreditMovementTypeConverter =
        new(value => ToDatabase(value), text => ToCreditMovementType(text));

    public static readonly ValueConverter<CustomerStatus, string> CustomerStatusConverter =
        new(value => ToDatabase(value), text => ToCustomerStatus(text));

    public static readonly ValueConverter<DataSubjectRequestStatus, string> DataSubjectRequestStatusConverter =
        new(value => ToDatabase(value), text => ToDataSubjectRequestStatus(text));

    public static readonly ValueConverter<Decision, string> DecisionConverter =
        new(value => ToDatabase(value), text => ToDecision(text));

    public static readonly ValueConverter<Department, string> DepartmentConverter =
        new(value => ToDatabase(value), text => ToDepartment(text));

    public static readonly ValueConverter<Dimension, string> DimensionConverter =
        new(value => ToDatabase(value), text => ToDimension(text));

    public static readonly ValueConverter<EntityType, string> EntityTypeConverter =
        new(value => ToDatabase(value), text => ToEntityType(text));

    public static readonly ValueConverter<ErasureLedgerEntryStatus, string> ErasureLedgerEntryStatusConverter =
        new(value => ToDatabase(value), text => ToErasureLedgerEntryStatus(text));

    public static readonly ValueConverter<ErasureLedgerEntrySubjectType, string> ErasureLedgerEntrySubjectTypeConverter =
        new(value => ToDatabase(value), text => ToErasureLedgerEntrySubjectType(text));

    public static readonly ValueConverter<InboxMessageChannel, string> InboxMessageChannelConverter =
        new(value => ToDatabase(value), text => ToInboxMessageChannel(text));

    public static readonly ValueConverter<InboxMessageStatus, string> InboxMessageStatusConverter =
        new(value => ToDatabase(value), text => ToInboxMessageStatus(text));

    public static readonly ValueConverter<IntentStatus, string> IntentStatusConverter =
        new(value => ToDatabase(value), text => ToIntentStatus(text));

    public static readonly ValueConverter<IntentType, string> IntentTypeConverter =
        new(value => ToDatabase(value), text => ToIntentType(text));

    public static readonly ValueConverter<Language, string> LanguageConverter =
        new(value => ToDatabase(value), text => ToLanguage(text));

    public static readonly ValueConverter<LegalBasis, string> LegalBasisConverter =
        new(value => ToDatabase(value), text => ToLegalBasis(text));

    public static readonly ValueConverter<LoyaltyMovementType, string> LoyaltyMovementTypeConverter =
        new(value => ToDatabase(value), text => ToLoyaltyMovementType(text));

    public static readonly ValueConverter<Method, string> MethodConverter =
        new(value => ToDatabase(value), text => ToMethod(text));

    public static readonly ValueConverter<NoticeType, string> NoticeTypeConverter =
        new(value => ToDatabase(value), text => ToNoticeType(text));

    public static readonly ValueConverter<Operation, string> OperationConverter =
        new(value => ToDatabase(value), text => ToOperation(text));

    public static readonly ValueConverter<Origin, string> OriginConverter =
        new(value => ToDatabase(value), text => ToOrigin(text));

    public static readonly ValueConverter<OutboxMessageChannel, string> OutboxMessageChannelConverter =
        new(value => ToDatabase(value), text => ToOutboxMessageChannel(text));

    public static readonly ValueConverter<ParameterRegistryEntrySource, string> ParameterRegistryEntrySourceConverter =
        new(value => ToDatabase(value), text => ToParameterRegistryEntrySource(text));

    public static readonly ValueConverter<PaymentMethod, string> PaymentMethodConverter =
        new(value => ToDatabase(value), text => ToPaymentMethod(text));

    public static readonly ValueConverter<PreferredLanguage, string> PreferredLanguageConverter =
        new(value => ToDatabase(value), text => ToPreferredLanguage(text));

    public static readonly ValueConverter<PriceType, string> PriceTypeConverter =
        new(value => ToDatabase(value), text => ToPriceType(text));

    public static readonly ValueConverter<ProcessingLogEntrySubjectType, string> ProcessingLogEntrySubjectTypeConverter =
        new(value => ToDatabase(value), text => ToProcessingLogEntrySubjectType(text));

    public static readonly ValueConverter<ProductBundleItemStatus, string> ProductBundleItemStatusConverter =
        new(value => ToDatabase(value), text => ToProductBundleItemStatus(text));

    public static readonly ValueConverter<ProductBundleStatus, string> ProductBundleStatusConverter =
        new(value => ToDatabase(value), text => ToProductBundleStatus(text));

    public static readonly ValueConverter<ProductStatus, string> ProductStatusConverter =
        new(value => ToDatabase(value), text => ToProductStatus(text));

    public static readonly ValueConverter<PromotionProductStatus, string> PromotionProductStatusConverter =
        new(value => ToDatabase(value), text => ToPromotionProductStatus(text));

    public static readonly ValueConverter<PromotionProductValueType, string> PromotionProductValueTypeConverter =
        new(value => ToDatabase(value), text => ToPromotionProductValueType(text));

    public static readonly ValueConverter<PromotionStatus, string> PromotionStatusConverter =
        new(value => ToDatabase(value), text => ToPromotionStatus(text));

    public static readonly ValueConverter<PromotionType, string> PromotionTypeConverter =
        new(value => ToDatabase(value), text => ToPromotionType(text));

    public static readonly ValueConverter<PromotionVariantStatus, string> PromotionVariantStatusConverter =
        new(value => ToDatabase(value), text => ToPromotionVariantStatus(text));

    public static readonly ValueConverter<PromotionVariantValueType, string> PromotionVariantValueTypeConverter =
        new(value => ToDatabase(value), text => ToPromotionVariantValueType(text));

    public static readonly ValueConverter<PurchaseOrderSource, string> PurchaseOrderSourceConverter =
        new(value => ToDatabase(value), text => ToPurchaseOrderSource(text));

    public static readonly ValueConverter<PurchaseOrderStatus, string> PurchaseOrderStatusConverter =
        new(value => ToDatabase(value), text => ToPurchaseOrderStatus(text));

    public static readonly ValueConverter<ReasonCodeAppliesTo, string> ReasonCodeAppliesToConverter =
        new(value => ToDatabase(value), text => ToReasonCodeAppliesTo(text));

    public static readonly ValueConverter<RecommendationStatus, string> RecommendationStatusConverter =
        new(value => ToDatabase(value), text => ToRecommendationStatus(text));

    public static readonly ValueConverter<RecommendationSubjectType, string> RecommendationSubjectTypeConverter =
        new(value => ToDatabase(value), text => ToRecommendationSubjectType(text));

    public static readonly ValueConverter<RefundMethod, string> RefundMethodConverter =
        new(value => ToDatabase(value), text => ToRefundMethod(text));

    public static readonly ValueConverter<RequestType, string> RequestTypeConverter =
        new(value => ToDatabase(value), text => ToRequestType(text));

    public static readonly ValueConverter<ScopeType, string> ScopeTypeConverter =
        new(value => ToDatabase(value), text => ToScopeType(text));

    public static readonly ValueConverter<ShiftStatus, string> ShiftStatusConverter =
        new(value => ToDatabase(value), text => ToShiftStatus(text));

    public static readonly ValueConverter<StaffStatus, string> StaffStatusConverter =
        new(value => ToDatabase(value), text => ToStaffStatus(text));

    public static readonly ValueConverter<StockCountStatus, string> StockCountStatusConverter =
        new(value => ToDatabase(value), text => ToStockCountStatus(text));

    public static readonly ValueConverter<Rounding, string> RoundingConverter =
        new(value => ToDatabase(value), text => ToRounding(text));

    public static readonly ValueConverter<VarianceReferenceType, string> VarianceReferenceTypeConverter =
        new(value => ToDatabase(value), text => ToVarianceReferenceType(text));

    public static readonly ValueConverter<VarianceSource, string> VarianceSourceConverter =
        new(value => ToDatabase(value), text => ToVarianceSource(text));

    public static readonly ValueConverter<StockMovementType, string> StockMovementTypeConverter =
        new(value => ToDatabase(value), text => ToStockMovementType(text));

    public static readonly ValueConverter<StoreStatus, string> StoreStatusConverter =
        new(value => ToDatabase(value), text => ToStoreStatus(text));

    public static readonly ValueConverter<SupplierStatus, string> SupplierStatusConverter =
        new(value => ToDatabase(value), text => ToSupplierStatus(text));

    public static readonly ValueConverter<SystemConfigEntryDataType, string> SystemConfigEntryDataTypeConverter =
        new(value => ToDatabase(value), text => ToSystemConfigEntryDataType(text));

    public static readonly ValueConverter<TerminalStatus, string> TerminalStatusConverter =
        new(value => ToDatabase(value), text => ToTerminalStatus(text));

    public static readonly ValueConverter<Tier, string> TierConverter =
        new(value => ToDatabase(value), text => ToTier(text));

    public static readonly ValueConverter<TransactionStatus, string> TransactionStatusConverter =
        new(value => ToDatabase(value), text => ToTransactionStatus(text));

    public static readonly ValueConverter<Urgency, string> UrgencyConverter =
        new(value => ToDatabase(value), text => ToUrgency(text));

    public static readonly ValueConverter<VariantStatus, string> VariantStatusConverter =
        new(value => ToDatabase(value), text => ToVariantStatus(text));

    private static string ToDatabase(ActionOnExpiry value) => value switch
    {
        ActionOnExpiry.Delete => "delete",
        ActionOnExpiry.Unlink => "unlink",
        ActionOnExpiry.Archive => "archive",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped ActionOnExpiry.")
    };

    private static ActionOnExpiry ToActionOnExpiry(string text) => text switch
    {
        "delete" => ActionOnExpiry.Delete,
        "unlink" => ActionOnExpiry.Unlink,
        "archive" => ActionOnExpiry.Archive,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown ActionOnExpiry value in the database.")
    };

    private static string ToDatabase(ActionType value) => value switch
    {
        ActionType.Binary => "binary",
        ActionType.Menu => "menu",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped ActionType.")
    };

    private static ActionType ToActionType(string text) => text switch
    {
        "binary" => ActionType.Binary,
        "menu" => ActionType.Menu,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown ActionType value in the database.")
    };

    private static string ToDatabase(ActorType value) => value switch
    {
        ActorType.Staff => "staff",
        ActorType.System => "system",
        ActorType.Engine => "engine",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped ActorType.")
    };

    private static ActorType ToActorType(string text) => text switch
    {
        "staff" => ActorType.Staff,
        "system" => ActorType.System,
        "engine" => ActorType.Engine,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown ActorType value in the database.")
    };

    private static string ToDatabase(AttributeDefinitionAppliesTo value) => value switch
    {
        AttributeDefinitionAppliesTo.Product => "product",
        AttributeDefinitionAppliesTo.Variant => "variant",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped AttributeDefinitionAppliesTo.")
    };

    private static AttributeDefinitionAppliesTo ToAttributeDefinitionAppliesTo(string text) => text switch
    {
        "product" => AttributeDefinitionAppliesTo.Product,
        "variant" => AttributeDefinitionAppliesTo.Variant,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown AttributeDefinitionAppliesTo value in the database.")
    };

    private static string ToDatabase(AttributeDefinitionDataType value) => value switch
    {
        AttributeDefinitionDataType.Text => "text",
        AttributeDefinitionDataType.Number => "number",
        AttributeDefinitionDataType.Bool => "bool",
        AttributeDefinitionDataType.Date => "date",
        AttributeDefinitionDataType.Enum => "enum",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped AttributeDefinitionDataType.")
    };

    private static AttributeDefinitionDataType ToAttributeDefinitionDataType(string text) => text switch
    {
        "text" => AttributeDefinitionDataType.Text,
        "number" => AttributeDefinitionDataType.Number,
        "bool" => AttributeDefinitionDataType.Bool,
        "date" => AttributeDefinitionDataType.Date,
        "enum" => AttributeDefinitionDataType.Enum,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown AttributeDefinitionDataType value in the database.")
    };

    private static string ToDatabase(AttributeDefinitionStatus value) => value switch
    {
        AttributeDefinitionStatus.Active => "active",
        AttributeDefinitionStatus.Archived => "archived",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped AttributeDefinitionStatus.")
    };

    private static AttributeDefinitionStatus ToAttributeDefinitionStatus(string text) => text switch
    {
        "active" => AttributeDefinitionStatus.Active,
        "archived" => AttributeDefinitionStatus.Archived,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown AttributeDefinitionStatus value in the database.")
    };

    private static string ToDatabase(BarcodeType value) => value switch
    {
        BarcodeType.Standard => "standard",
        BarcodeType.WeightEmbedded => "weight_embedded",
        BarcodeType.PriceEmbedded => "price_embedded",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped BarcodeType.")
    };

    private static BarcodeType ToBarcodeType(string text) => text switch
    {
        "standard" => BarcodeType.Standard,
        "weight_embedded" => BarcodeType.WeightEmbedded,
        "price_embedded" => BarcodeType.PriceEmbedded,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown BarcodeType value in the database.")
    };

    private static string ToDatabase(BatchStatus value) => value switch
    {
        BatchStatus.Active => "active",
        BatchStatus.Depleted => "depleted",
        BatchStatus.WrittenOff => "written_off",
        BatchStatus.Quarantined => "quarantined",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped BatchStatus.")
    };

    private static BatchStatus ToBatchStatus(string text) => text switch
    {
        "active" => BatchStatus.Active,
        "depleted" => BatchStatus.Depleted,
        "written_off" => BatchStatus.WrittenOff,
        "quarantined" => BatchStatus.Quarantined,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown BatchStatus value in the database.")
    };

    private static string ToDatabase(BundleType value) => value switch
    {
        BundleType.Fixed => "fixed",
        BundleType.MixAndMatch => "mix_and_match",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped BundleType.")
    };

    private static BundleType ToBundleType(string text) => text switch
    {
        "fixed" => BundleType.Fixed,
        "mix_and_match" => BundleType.MixAndMatch,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown BundleType value in the database.")
    };

    private static string ToDatabase(CashMovementType value) => value switch
    {
        CashMovementType.PaidIn => "paid_in",
        CashMovementType.PaidOut => "paid_out",
        CashMovementType.Drop => "drop",
        CashMovementType.FloatAdd => "float_add",
        CashMovementType.FloatRemove => "float_remove",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped CashMovementType.")
    };

    private static CashMovementType ToCashMovementType(string text) => text switch
    {
        "paid_in" => CashMovementType.PaidIn,
        "paid_out" => CashMovementType.PaidOut,
        "drop" => CashMovementType.Drop,
        "float_add" => CashMovementType.FloatAdd,
        "float_remove" => CashMovementType.FloatRemove,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown CashMovementType value in the database.")
    };

    private static string ToDatabase(CashSessionStatus value) => value switch
    {
        CashSessionStatus.Open => "open",
        CashSessionStatus.Closed => "closed",
        CashSessionStatus.Suspended => "suspended",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped CashSessionStatus.")
    };

    private static CashSessionStatus ToCashSessionStatus(string text) => text switch
    {
        "open" => CashSessionStatus.Open,
        "closed" => CashSessionStatus.Closed,
        "suspended" => CashSessionStatus.Suspended,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown CashSessionStatus value in the database.")
    };

    private static string ToDatabase(CategoryStatus value) => value switch
    {
        CategoryStatus.Active => "active",
        CategoryStatus.Archived => "archived",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped CategoryStatus.")
    };

    private static CategoryStatus ToCategoryStatus(string text) => text switch
    {
        "active" => CategoryStatus.Active,
        "archived" => CategoryStatus.Archived,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown CategoryStatus value in the database.")
    };

    private static string ToDatabase(ConsentEventAction value) => value switch
    {
        ConsentEventAction.Granted => "granted",
        ConsentEventAction.Withdrawn => "withdrawn",
        ConsentEventAction.Renewed => "renewed",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped ConsentEventAction.")
    };

    private static ConsentEventAction ToConsentEventAction(string text) => text switch
    {
        "granted" => ConsentEventAction.Granted,
        "withdrawn" => ConsentEventAction.Withdrawn,
        "renewed" => ConsentEventAction.Renewed,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown ConsentEventAction value in the database.")
    };

    private static string ToDatabase(ConsentType value) => value switch
    {
        ConsentType.Processing => "processing",
        ConsentType.Marketing => "marketing",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped ConsentType.")
    };

    private static ConsentType ToConsentType(string text) => text switch
    {
        "processing" => ConsentType.Processing,
        "marketing" => ConsentType.Marketing,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown ConsentType value in the database.")
    };

    private static string ToDatabase(CountType value) => value switch
    {
        CountType.Full => "full",
        CountType.Cycle => "cycle",
        CountType.Spot => "spot",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped CountType.")
    };

    private static CountType ToCountType(string text) => text switch
    {
        "full" => CountType.Full,
        "cycle" => CountType.Cycle,
        "spot" => CountType.Spot,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown CountType value in the database.")
    };

    private static string ToDatabase(CreditMovementType value) => value switch
    {
        CreditMovementType.Issue => "issue",
        CreditMovementType.Redeem => "redeem",
        CreditMovementType.Adjust => "adjust",
        CreditMovementType.Expire => "expire",
        CreditMovementType.Reverse => "reverse",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped CreditMovementType.")
    };

    private static CreditMovementType ToCreditMovementType(string text) => text switch
    {
        "issue" => CreditMovementType.Issue,
        "redeem" => CreditMovementType.Redeem,
        "adjust" => CreditMovementType.Adjust,
        "expire" => CreditMovementType.Expire,
        "reverse" => CreditMovementType.Reverse,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown CreditMovementType value in the database.")
    };

    private static string ToDatabase(CustomerStatus value) => value switch
    {
        CustomerStatus.Active => "active",
        CustomerStatus.Inactive => "inactive",
        CustomerStatus.Erased => "erased",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped CustomerStatus.")
    };

    private static CustomerStatus ToCustomerStatus(string text) => text switch
    {
        "active" => CustomerStatus.Active,
        "inactive" => CustomerStatus.Inactive,
        "erased" => CustomerStatus.Erased,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown CustomerStatus value in the database.")
    };

    private static string ToDatabase(DataSubjectRequestStatus value) => value switch
    {
        DataSubjectRequestStatus.Open => "open",
        DataSubjectRequestStatus.InProgress => "in_progress",
        DataSubjectRequestStatus.Fulfilled => "fulfilled",
        DataSubjectRequestStatus.Refused => "refused",
        DataSubjectRequestStatus.Blocked => "blocked",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped DataSubjectRequestStatus.")
    };

    private static DataSubjectRequestStatus ToDataSubjectRequestStatus(string text) => text switch
    {
        "open" => DataSubjectRequestStatus.Open,
        "in_progress" => DataSubjectRequestStatus.InProgress,
        "fulfilled" => DataSubjectRequestStatus.Fulfilled,
        "refused" => DataSubjectRequestStatus.Refused,
        "blocked" => DataSubjectRequestStatus.Blocked,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown DataSubjectRequestStatus value in the database.")
    };

    private static string ToDatabase(Decision value) => value switch
    {
        Decision.Accept => "accept",
        Decision.Adjust => "adjust",
        Decision.Dismiss => "dismiss",
        Decision.Snooze => "snooze",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped Decision.")
    };

    private static Decision ToDecision(string text) => text switch
    {
        "accept" => Decision.Accept,
        "adjust" => Decision.Adjust,
        "dismiss" => Decision.Dismiss,
        "snooze" => Decision.Snooze,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown Decision value in the database.")
    };

    private static string ToDatabase(Department value) => value switch
    {
        Department.Inventory => "inventory",
        Department.SalesDemand => "sales_demand",
        Department.Supply => "supply",
        Department.Planning => "planning",
        Department.Customer => "customer",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped Department.")
    };

    private static Department ToDepartment(string text) => text switch
    {
        "inventory" => Department.Inventory,
        "sales_demand" => Department.SalesDemand,
        "supply" => Department.Supply,
        "planning" => Department.Planning,
        "customer" => Department.Customer,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown Department value in the database.")
    };

    private static string ToDatabase(Dimension value) => value switch
    {
        Dimension.Count => "count",
        Dimension.Weight => "weight",
        Dimension.Volume => "volume",
        Dimension.Length => "length",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped Dimension.")
    };

    private static Dimension ToDimension(string text) => text switch
    {
        "count" => Dimension.Count,
        "weight" => Dimension.Weight,
        "volume" => Dimension.Volume,
        "length" => Dimension.Length,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown Dimension value in the database.")
    };

    private static string ToDatabase(EntityType value) => value switch
    {
        EntityType.Transaction => "transaction",
        EntityType.Customer => "customer",
        EntityType.Staff => "staff",
        EntityType.ProcessingLog => "processing_log",
        EntityType.ConsentEvent => "consent_event",
        EntityType.Recommendation => "recommendation",
        EntityType.StockMovement => "stock_movement",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped EntityType.")
    };

    private static EntityType ToEntityType(string text) => text switch
    {
        "transaction" => EntityType.Transaction,
        "customer" => EntityType.Customer,
        "staff" => EntityType.Staff,
        "processing_log" => EntityType.ProcessingLog,
        "consent_event" => EntityType.ConsentEvent,
        "recommendation" => EntityType.Recommendation,
        "stock_movement" => EntityType.StockMovement,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown EntityType value in the database.")
    };

    private static string ToDatabase(ErasureLedgerEntryStatus value) => value switch
    {
        ErasureLedgerEntryStatus.Pending => "pending",
        ErasureLedgerEntryStatus.Executed => "executed",
        ErasureLedgerEntryStatus.Blocked => "blocked",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped ErasureLedgerEntryStatus.")
    };

    private static ErasureLedgerEntryStatus ToErasureLedgerEntryStatus(string text) => text switch
    {
        "pending" => ErasureLedgerEntryStatus.Pending,
        "executed" => ErasureLedgerEntryStatus.Executed,
        "blocked" => ErasureLedgerEntryStatus.Blocked,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown ErasureLedgerEntryStatus value in the database.")
    };

    private static string ToDatabase(ErasureLedgerEntrySubjectType value) => value switch
    {
        ErasureLedgerEntrySubjectType.Customer => "customer",
        ErasureLedgerEntrySubjectType.Staff => "staff",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped ErasureLedgerEntrySubjectType.")
    };

    private static ErasureLedgerEntrySubjectType ToErasureLedgerEntrySubjectType(string text) => text switch
    {
        "customer" => ErasureLedgerEntrySubjectType.Customer,
        "staff" => ErasureLedgerEntrySubjectType.Staff,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown ErasureLedgerEntrySubjectType value in the database.")
    };

    private static string ToDatabase(InboxMessageChannel value) => value switch
    {
        InboxMessageChannel.CRecommendations => "C_recommendations",
        InboxMessageChannel.DIntents => "D_intents",
        InboxMessageChannel.EControl => "E_control",
        InboxMessageChannel.FParameters => "F_parameters",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped InboxMessageChannel.")
    };

    private static InboxMessageChannel ToInboxMessageChannel(string text) => text switch
    {
        "C_recommendations" => InboxMessageChannel.CRecommendations,
        "D_intents" => InboxMessageChannel.DIntents,
        "E_control" => InboxMessageChannel.EControl,
        "F_parameters" => InboxMessageChannel.FParameters,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown InboxMessageChannel value in the database.")
    };

    private static string ToDatabase(InboxMessageStatus value) => value switch
    {
        InboxMessageStatus.Pending => "pending",
        InboxMessageStatus.Applied => "applied",
        InboxMessageStatus.Duplicate => "duplicate",
        InboxMessageStatus.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped InboxMessageStatus.")
    };

    private static InboxMessageStatus ToInboxMessageStatus(string text) => text switch
    {
        "pending" => InboxMessageStatus.Pending,
        "applied" => InboxMessageStatus.Applied,
        "duplicate" => InboxMessageStatus.Duplicate,
        "rejected" => InboxMessageStatus.Rejected,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown InboxMessageStatus value in the database.")
    };

    private static string ToDatabase(IntentStatus value) => value switch
    {
        IntentStatus.Pending => "pending",
        IntentStatus.Applied => "applied",
        IntentStatus.RejectedStale => "rejected_stale",
        IntentStatus.RejectedInvalid => "rejected_invalid",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped IntentStatus.")
    };

    private static IntentStatus ToIntentStatus(string text) => text switch
    {
        "pending" => IntentStatus.Pending,
        "applied" => IntentStatus.Applied,
        "rejected_stale" => IntentStatus.RejectedStale,
        "rejected_invalid" => IntentStatus.RejectedInvalid,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown IntentStatus value in the database.")
    };

    private static string ToDatabase(IntentType value) => value switch
    {
        IntentType.AcceptReorder => "accept_reorder",
        IntentType.MarkdownStage => "markdown_stage",
        IntentType.AdjustQuantity => "adjust_quantity",
        IntentType.Dismiss => "dismiss",
        IntentType.PriceChange => "price_change",
        IntentType.Promotion => "promotion",
        IntentType.CatalogueEdit => "catalogue_edit",
        IntentType.SupplierEdit => "supplier_edit",
        IntentType.RightsAction => "rights_action",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped IntentType.")
    };

    private static IntentType ToIntentType(string text) => text switch
    {
        "accept_reorder" => IntentType.AcceptReorder,
        "markdown_stage" => IntentType.MarkdownStage,
        "adjust_quantity" => IntentType.AdjustQuantity,
        "dismiss" => IntentType.Dismiss,
        "price_change" => IntentType.PriceChange,
        "promotion" => IntentType.Promotion,
        "catalogue_edit" => IntentType.CatalogueEdit,
        "supplier_edit" => IntentType.SupplierEdit,
        "rights_action" => IntentType.RightsAction,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown IntentType value in the database.")
    };

    private static string ToDatabase(Language value) => value switch
    {
        Language.Ar => "ar",
        Language.Fr => "fr",
        Language.En => "en",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped Language.")
    };

    private static Language ToLanguage(string text) => text switch
    {
        "ar" => Language.Ar,
        "fr" => Language.Fr,
        "en" => Language.En,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown Language value in the database.")
    };

    private static string ToDatabase(LegalBasis value) => value switch
    {
        LegalBasis.Consent => "consent",
        LegalBasis.Contract => "contract",
        LegalBasis.LegalObligation => "legal_obligation",
        LegalBasis.LegitimateInterest => "legitimate_interest",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped LegalBasis.")
    };

    private static LegalBasis ToLegalBasis(string text) => text switch
    {
        "consent" => LegalBasis.Consent,
        "contract" => LegalBasis.Contract,
        "legal_obligation" => LegalBasis.LegalObligation,
        "legitimate_interest" => LegalBasis.LegitimateInterest,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown LegalBasis value in the database.")
    };

    private static string ToDatabase(LoyaltyMovementType value) => value switch
    {
        LoyaltyMovementType.Earn => "earn",
        LoyaltyMovementType.Redeem => "redeem",
        LoyaltyMovementType.Adjust => "adjust",
        LoyaltyMovementType.Expire => "expire",
        LoyaltyMovementType.Reverse => "reverse",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped LoyaltyMovementType.")
    };

    private static LoyaltyMovementType ToLoyaltyMovementType(string text) => text switch
    {
        "earn" => LoyaltyMovementType.Earn,
        "redeem" => LoyaltyMovementType.Redeem,
        "adjust" => LoyaltyMovementType.Adjust,
        "expire" => LoyaltyMovementType.Expire,
        "reverse" => LoyaltyMovementType.Reverse,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown LoyaltyMovementType value in the database.")
    };

    private static string ToDatabase(Method value) => value switch
    {
        Method.Verbal => "verbal",
        Method.Written => "written",
        Method.Digital => "digital",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped Method.")
    };

    private static Method ToMethod(string text) => text switch
    {
        "verbal" => Method.Verbal,
        "written" => Method.Written,
        "digital" => Method.Digital,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown Method value in the database.")
    };

    private static string ToDatabase(NoticeType value) => value switch
    {
        NoticeType.Processing => "processing",
        NoticeType.Marketing => "marketing",
        NoticeType.Staff => "staff",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped NoticeType.")
    };

    private static NoticeType ToNoticeType(string text) => text switch
    {
        "processing" => NoticeType.Processing,
        "marketing" => NoticeType.Marketing,
        "staff" => NoticeType.Staff,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown NoticeType value in the database.")
    };

    private static string ToDatabase(Operation value) => value switch
    {
        Operation.Collection => "collection",
        Operation.Consultation => "consultation",
        Operation.Disclosure => "disclosure",
        Operation.Transmission => "transmission",
        Operation.Modification => "modification",
        Operation.Erasure => "erasure",
        Operation.Pseudonymisation => "pseudonymisation",
        Operation.ReIdentification => "re_identification",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped Operation.")
    };

    private static Operation ToOperation(string text) => text switch
    {
        "collection" => Operation.Collection,
        "consultation" => Operation.Consultation,
        "disclosure" => Operation.Disclosure,
        "transmission" => Operation.Transmission,
        "modification" => Operation.Modification,
        "erasure" => Operation.Erasure,
        "pseudonymisation" => Operation.Pseudonymisation,
        "re_identification" => Operation.ReIdentification,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown Operation value in the database.")
    };

    private static string ToDatabase(Origin value) => value switch
    {
        Origin.Store => "store",
        Origin.Cloud => "cloud",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped Origin.")
    };

    private static Origin ToOrigin(string text) => text switch
    {
        "store" => Origin.Store,
        "cloud" => Origin.Cloud,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown Origin value in the database.")
    };

    private static string ToDatabase(OutboxMessageChannel value) => value switch
    {
        OutboxMessageChannel.AStatistics => "A_statistics",
        OutboxMessageChannel.BOperational => "B_operational",
        OutboxMessageChannel.DDecisions => "D_decisions",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped OutboxMessageChannel.")
    };

    private static OutboxMessageChannel ToOutboxMessageChannel(string text) => text switch
    {
        "A_statistics" => OutboxMessageChannel.AStatistics,
        "B_operational" => OutboxMessageChannel.BOperational,
        "D_decisions" => OutboxMessageChannel.DDecisions,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown OutboxMessageChannel value in the database.")
    };

    private static string ToDatabase(ParameterRegistryEntrySource value) => value switch
    {
        ParameterRegistryEntrySource.Engine => "engine",
        ParameterRegistryEntrySource.ColdStartDefault => "cold_start_default",
        ParameterRegistryEntrySource.ManualOverride => "manual_override",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped ParameterRegistryEntrySource.")
    };

    private static ParameterRegistryEntrySource ToParameterRegistryEntrySource(string text) => text switch
    {
        "engine" => ParameterRegistryEntrySource.Engine,
        "cold_start_default" => ParameterRegistryEntrySource.ColdStartDefault,
        "manual_override" => ParameterRegistryEntrySource.ManualOverride,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown ParameterRegistryEntrySource value in the database.")
    };

    private static string ToDatabase(PaymentMethod value) => value switch
    {
        PaymentMethod.Cash => "cash",
        PaymentMethod.Card => "card",
        PaymentMethod.MobileWallet => "mobile_wallet",
        PaymentMethod.StoreCredit => "store_credit",
        PaymentMethod.OnAccount => "on_account",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped PaymentMethod.")
    };

    private static PaymentMethod ToPaymentMethod(string text) => text switch
    {
        "cash" => PaymentMethod.Cash,
        "card" => PaymentMethod.Card,
        "mobile_wallet" => PaymentMethod.MobileWallet,
        "store_credit" => PaymentMethod.StoreCredit,
        "on_account" => PaymentMethod.OnAccount,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown PaymentMethod value in the database.")
    };

    private static string ToDatabase(PreferredLanguage value) => value switch
    {
        PreferredLanguage.Ar => "ar",
        PreferredLanguage.Fr => "fr",
        PreferredLanguage.En => "en",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped PreferredLanguage.")
    };

    private static PreferredLanguage ToPreferredLanguage(string text) => text switch
    {
        "ar" => PreferredLanguage.Ar,
        "fr" => PreferredLanguage.Fr,
        "en" => PreferredLanguage.En,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown PreferredLanguage value in the database.")
    };

    private static string ToDatabase(PriceType value) => value switch
    {
        PriceType.Retail => "retail",
        PriceType.Wholesale => "wholesale",
        PriceType.Staff => "staff",
        PriceType.Promotional => "promotional",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped PriceType.")
    };

    private static PriceType ToPriceType(string text) => text switch
    {
        "retail" => PriceType.Retail,
        "wholesale" => PriceType.Wholesale,
        "staff" => PriceType.Staff,
        "promotional" => PriceType.Promotional,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown PriceType value in the database.")
    };

    private static string ToDatabase(ProcessingLogEntrySubjectType value) => value switch
    {
        ProcessingLogEntrySubjectType.Customer => "customer",
        ProcessingLogEntrySubjectType.Staff => "staff",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped ProcessingLogEntrySubjectType.")
    };

    private static ProcessingLogEntrySubjectType ToProcessingLogEntrySubjectType(string text) => text switch
    {
        "customer" => ProcessingLogEntrySubjectType.Customer,
        "staff" => ProcessingLogEntrySubjectType.Staff,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown ProcessingLogEntrySubjectType value in the database.")
    };

    private static string ToDatabase(ProductBundleItemStatus value) => value switch
    {
        ProductBundleItemStatus.Active => "active",
        ProductBundleItemStatus.Inactive => "inactive",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped ProductBundleItemStatus.")
    };

    private static ProductBundleItemStatus ToProductBundleItemStatus(string text) => text switch
    {
        "active" => ProductBundleItemStatus.Active,
        "inactive" => ProductBundleItemStatus.Inactive,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown ProductBundleItemStatus value in the database.")
    };

    private static string ToDatabase(ProductBundleStatus value) => value switch
    {
        ProductBundleStatus.Active => "active",
        ProductBundleStatus.Inactive => "inactive",
        ProductBundleStatus.Archived => "archived",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped ProductBundleStatus.")
    };

    private static ProductBundleStatus ToProductBundleStatus(string text) => text switch
    {
        "active" => ProductBundleStatus.Active,
        "inactive" => ProductBundleStatus.Inactive,
        "archived" => ProductBundleStatus.Archived,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown ProductBundleStatus value in the database.")
    };

    private static string ToDatabase(ProductStatus value) => value switch
    {
        ProductStatus.Active => "active",
        ProductStatus.Discontinued => "discontinued",
        ProductStatus.Archived => "archived",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped ProductStatus.")
    };

    private static ProductStatus ToProductStatus(string text) => text switch
    {
        "active" => ProductStatus.Active,
        "discontinued" => ProductStatus.Discontinued,
        "archived" => ProductStatus.Archived,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown ProductStatus value in the database.")
    };

    private static string ToDatabase(PromotionProductStatus value) => value switch
    {
        PromotionProductStatus.Active => "active",
        PromotionProductStatus.Paused => "paused",
        PromotionProductStatus.Ended => "ended",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped PromotionProductStatus.")
    };

    private static PromotionProductStatus ToPromotionProductStatus(string text) => text switch
    {
        "active" => PromotionProductStatus.Active,
        "paused" => PromotionProductStatus.Paused,
        "ended" => PromotionProductStatus.Ended,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown PromotionProductStatus value in the database.")
    };

    private static string ToDatabase(PromotionProductValueType value) => value switch
    {
        PromotionProductValueType.Percent => "percent",
        PromotionProductValueType.Amount => "amount",
        PromotionProductValueType.Bogo => "bogo",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped PromotionProductValueType.")
    };

    private static PromotionProductValueType ToPromotionProductValueType(string text) => text switch
    {
        "percent" => PromotionProductValueType.Percent,
        "amount" => PromotionProductValueType.Amount,
        "bogo" => PromotionProductValueType.Bogo,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown PromotionProductValueType value in the database.")
    };

    private static string ToDatabase(PromotionStatus value) => value switch
    {
        PromotionStatus.Draft => "draft",
        PromotionStatus.Scheduled => "scheduled",
        PromotionStatus.Active => "active",
        PromotionStatus.Ended => "ended",
        PromotionStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped PromotionStatus.")
    };

    private static PromotionStatus ToPromotionStatus(string text) => text switch
    {
        "draft" => PromotionStatus.Draft,
        "scheduled" => PromotionStatus.Scheduled,
        "active" => PromotionStatus.Active,
        "ended" => PromotionStatus.Ended,
        "cancelled" => PromotionStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown PromotionStatus value in the database.")
    };

    private static string ToDatabase(PromotionType value) => value switch
    {
        PromotionType.Discount => "discount",
        PromotionType.Bogo => "bogo",
        PromotionType.Bundle => "bundle",
        PromotionType.Markdown => "markdown",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped PromotionType.")
    };

    private static PromotionType ToPromotionType(string text) => text switch
    {
        "discount" => PromotionType.Discount,
        "bogo" => PromotionType.Bogo,
        "bundle" => PromotionType.Bundle,
        "markdown" => PromotionType.Markdown,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown PromotionType value in the database.")
    };

    private static string ToDatabase(PromotionVariantStatus value) => value switch
    {
        PromotionVariantStatus.Active => "active",
        PromotionVariantStatus.Paused => "paused",
        PromotionVariantStatus.Ended => "ended",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped PromotionVariantStatus.")
    };

    private static PromotionVariantStatus ToPromotionVariantStatus(string text) => text switch
    {
        "active" => PromotionVariantStatus.Active,
        "paused" => PromotionVariantStatus.Paused,
        "ended" => PromotionVariantStatus.Ended,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown PromotionVariantStatus value in the database.")
    };

    private static string ToDatabase(PromotionVariantValueType value) => value switch
    {
        PromotionVariantValueType.Percent => "percent",
        PromotionVariantValueType.Amount => "amount",
        PromotionVariantValueType.Bogo => "bogo",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped PromotionVariantValueType.")
    };

    private static PromotionVariantValueType ToPromotionVariantValueType(string text) => text switch
    {
        "percent" => PromotionVariantValueType.Percent,
        "amount" => PromotionVariantValueType.Amount,
        "bogo" => PromotionVariantValueType.Bogo,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown PromotionVariantValueType value in the database.")
    };

    private static string ToDatabase(PurchaseOrderSource value) => value switch
    {
        PurchaseOrderSource.Manual => "manual",
        PurchaseOrderSource.Recommendation => "recommendation",
        PurchaseOrderSource.ReorderRule => "reorder_rule",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped PurchaseOrderSource.")
    };

    private static PurchaseOrderSource ToPurchaseOrderSource(string text) => text switch
    {
        "manual" => PurchaseOrderSource.Manual,
        "recommendation" => PurchaseOrderSource.Recommendation,
        "reorder_rule" => PurchaseOrderSource.ReorderRule,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown PurchaseOrderSource value in the database.")
    };

    private static string ToDatabase(PurchaseOrderStatus value) => value switch
    {
        PurchaseOrderStatus.Draft => "draft",
        PurchaseOrderStatus.Sent => "sent",
        PurchaseOrderStatus.PartiallyReceived => "partially_received",
        PurchaseOrderStatus.Received => "received",
        PurchaseOrderStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped PurchaseOrderStatus.")
    };

    private static PurchaseOrderStatus ToPurchaseOrderStatus(string text) => text switch
    {
        "draft" => PurchaseOrderStatus.Draft,
        "sent" => PurchaseOrderStatus.Sent,
        "partially_received" => PurchaseOrderStatus.PartiallyReceived,
        "received" => PurchaseOrderStatus.Received,
        "cancelled" => PurchaseOrderStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown PurchaseOrderStatus value in the database.")
    };

    private static string ToDatabase(ReasonCodeAppliesTo value) => value switch
    {
        ReasonCodeAppliesTo.Discount => "discount",
        ReasonCodeAppliesTo.PriceOverride => "price_override",
        ReasonCodeAppliesTo.Adjustment => "adjustment",
        ReasonCodeAppliesTo.Void => "void",
        ReasonCodeAppliesTo.Return => "return",
        ReasonCodeAppliesTo.NoSale => "no_sale",
        ReasonCodeAppliesTo.CashMovement => "cash_movement",
        ReasonCodeAppliesTo.WriteOff => "write_off",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped ReasonCodeAppliesTo.")
    };

    private static ReasonCodeAppliesTo ToReasonCodeAppliesTo(string text) => text switch
    {
        "discount" => ReasonCodeAppliesTo.Discount,
        "price_override" => ReasonCodeAppliesTo.PriceOverride,
        "adjustment" => ReasonCodeAppliesTo.Adjustment,
        "void" => ReasonCodeAppliesTo.Void,
        "return" => ReasonCodeAppliesTo.Return,
        "no_sale" => ReasonCodeAppliesTo.NoSale,
        "cash_movement" => ReasonCodeAppliesTo.CashMovement,
        "write_off" => ReasonCodeAppliesTo.WriteOff,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown ReasonCodeAppliesTo value in the database.")
    };

    private static string ToDatabase(RecommendationStatus value) => value switch
    {
        RecommendationStatus.Pending => "pending",
        RecommendationStatus.Delivered => "delivered",
        RecommendationStatus.Decided => "decided",
        RecommendationStatus.Expired => "expired",
        RecommendationStatus.Superseded => "superseded",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped RecommendationStatus.")
    };

    private static RecommendationStatus ToRecommendationStatus(string text) => text switch
    {
        "pending" => RecommendationStatus.Pending,
        "delivered" => RecommendationStatus.Delivered,
        "decided" => RecommendationStatus.Decided,
        "expired" => RecommendationStatus.Expired,
        "superseded" => RecommendationStatus.Superseded,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown RecommendationStatus value in the database.")
    };

    private static string ToDatabase(RecommendationSubjectType value) => value switch
    {
        RecommendationSubjectType.Variant => "variant",
        RecommendationSubjectType.Batch => "batch",
        RecommendationSubjectType.Product => "product",
        RecommendationSubjectType.Supplier => "supplier",
        RecommendationSubjectType.Customer => "customer",
        RecommendationSubjectType.Store => "store",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped RecommendationSubjectType.")
    };

    private static RecommendationSubjectType ToRecommendationSubjectType(string text) => text switch
    {
        "variant" => RecommendationSubjectType.Variant,
        "batch" => RecommendationSubjectType.Batch,
        "product" => RecommendationSubjectType.Product,
        "supplier" => RecommendationSubjectType.Supplier,
        "customer" => RecommendationSubjectType.Customer,
        "store" => RecommendationSubjectType.Store,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown RecommendationSubjectType value in the database.")
    };

    private static string ToDatabase(RefundMethod value) => value switch
    {
        RefundMethod.Cash => "cash",
        RefundMethod.Card => "card",
        RefundMethod.StoreCredit => "store_credit",
        RefundMethod.Exchange => "exchange",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped RefundMethod.")
    };

    private static RefundMethod ToRefundMethod(string text) => text switch
    {
        "cash" => RefundMethod.Cash,
        "card" => RefundMethod.Card,
        "store_credit" => RefundMethod.StoreCredit,
        "exchange" => RefundMethod.Exchange,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown RefundMethod value in the database.")
    };

    private static string ToDatabase(RequestType value) => value switch
    {
        RequestType.Information => "information",
        RequestType.Access => "access",
        RequestType.Rectification => "rectification",
        RequestType.Objection => "objection",
        RequestType.Erasure => "erasure",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped RequestType.")
    };

    private static RequestType ToRequestType(string text) => text switch
    {
        "information" => RequestType.Information,
        "access" => RequestType.Access,
        "rectification" => RequestType.Rectification,
        "objection" => RequestType.Objection,
        "erasure" => RequestType.Erasure,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown RequestType value in the database.")
    };

    private static string ToDatabase(ScopeType value) => value switch
    {
        ScopeType.Global => "global",
        ScopeType.Store => "store",
        ScopeType.Category => "category",
        ScopeType.Variant => "variant",
        ScopeType.Supplier => "supplier",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped ScopeType.")
    };

    private static ScopeType ToScopeType(string text) => text switch
    {
        "global" => ScopeType.Global,
        "store" => ScopeType.Store,
        "category" => ScopeType.Category,
        "variant" => ScopeType.Variant,
        "supplier" => ScopeType.Supplier,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown ScopeType value in the database.")
    };

    private static string ToDatabase(ShiftStatus value) => value switch
    {
        ShiftStatus.Open => "open",
        ShiftStatus.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped ShiftStatus.")
    };

    private static ShiftStatus ToShiftStatus(string text) => text switch
    {
        "open" => ShiftStatus.Open,
        "closed" => ShiftStatus.Closed,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown ShiftStatus value in the database.")
    };

    private static string ToDatabase(StaffStatus value) => value switch
    {
        StaffStatus.Active => "active",
        StaffStatus.Suspended => "suspended",
        StaffStatus.Terminated => "terminated",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped StaffStatus.")
    };

    private static StaffStatus ToStaffStatus(string text) => text switch
    {
        "active" => StaffStatus.Active,
        "suspended" => StaffStatus.Suspended,
        "terminated" => StaffStatus.Terminated,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown StaffStatus value in the database.")
    };

    private static string ToDatabase(StockCountStatus value) => value switch
    {
        StockCountStatus.Draft => "draft",
        StockCountStatus.Counting => "counting",
        StockCountStatus.Review => "review",
        StockCountStatus.Posted => "posted",
        StockCountStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped StockCountStatus.")
    };

    private static StockCountStatus ToStockCountStatus(string text) => text switch
    {
        "draft" => StockCountStatus.Draft,
        "counting" => StockCountStatus.Counting,
        "review" => StockCountStatus.Review,
        "posted" => StockCountStatus.Posted,
        "cancelled" => StockCountStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown StockCountStatus value in the database.")
    };

    private static string ToDatabase(StockMovementType value) => value switch
    {
        StockMovementType.Receipt => "receipt",
        StockMovementType.Sale => "sale",
        StockMovementType.ReturnIn => "return_in",
        StockMovementType.ReturnOut => "return_out",
        StockMovementType.Adjustment => "adjustment",
        StockMovementType.Count => "count",
        StockMovementType.WriteOff => "write_off",
        StockMovementType.Expiry => "expiry",
        StockMovementType.Transfer => "transfer",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped StockMovementType.")
    };

    private static StockMovementType ToStockMovementType(string text) => text switch
    {
        "receipt" => StockMovementType.Receipt,
        "sale" => StockMovementType.Sale,
        "return_in" => StockMovementType.ReturnIn,
        "return_out" => StockMovementType.ReturnOut,
        "adjustment" => StockMovementType.Adjustment,
        "count" => StockMovementType.Count,
        "write_off" => StockMovementType.WriteOff,
        "expiry" => StockMovementType.Expiry,
        "transfer" => StockMovementType.Transfer,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown StockMovementType value in the database.")
    };

    private static string ToDatabase(StoreStatus value) => value switch
    {
        StoreStatus.Active => "active",
        StoreStatus.Suspended => "suspended",
        StoreStatus.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped StoreStatus.")
    };

    private static StoreStatus ToStoreStatus(string text) => text switch
    {
        "active" => StoreStatus.Active,
        "suspended" => StoreStatus.Suspended,
        "closed" => StoreStatus.Closed,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown StoreStatus value in the database.")
    };

    private static string ToDatabase(SupplierStatus value) => value switch
    {
        SupplierStatus.Active => "active",
        SupplierStatus.Inactive => "inactive",
        SupplierStatus.Blacklisted => "blacklisted",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped SupplierStatus.")
    };

    private static SupplierStatus ToSupplierStatus(string text) => text switch
    {
        "active" => SupplierStatus.Active,
        "inactive" => SupplierStatus.Inactive,
        "blacklisted" => SupplierStatus.Blacklisted,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown SupplierStatus value in the database.")
    };

    private static string ToDatabase(SystemConfigEntryDataType value) => value switch
    {
        SystemConfigEntryDataType.Text => "text",
        SystemConfigEntryDataType.Integer => "integer",
        SystemConfigEntryDataType.Money => "money",
        SystemConfigEntryDataType.Percent => "percent",
        SystemConfigEntryDataType.Bool => "bool",
        SystemConfigEntryDataType.Date => "date",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped SystemConfigEntryDataType.")
    };

    private static SystemConfigEntryDataType ToSystemConfigEntryDataType(string text) => text switch
    {
        "text" => SystemConfigEntryDataType.Text,
        "integer" => SystemConfigEntryDataType.Integer,
        "money" => SystemConfigEntryDataType.Money,
        "percent" => SystemConfigEntryDataType.Percent,
        "bool" => SystemConfigEntryDataType.Bool,
        "date" => SystemConfigEntryDataType.Date,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown SystemConfigEntryDataType value in the database.")
    };

    private static string ToDatabase(TerminalStatus value) => value switch
    {
        TerminalStatus.Active => "active",
        TerminalStatus.Inactive => "inactive",
        TerminalStatus.Retired => "retired",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped TerminalStatus.")
    };

    private static TerminalStatus ToTerminalStatus(string text) => text switch
    {
        "active" => TerminalStatus.Active,
        "inactive" => TerminalStatus.Inactive,
        "retired" => TerminalStatus.Retired,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown TerminalStatus value in the database.")
    };

    private static string ToDatabase(Tier value) => value switch
    {
        Tier.Basic => "basic",
        Tier.Pro => "pro",
        Tier.Enterprise => "enterprise",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped Tier.")
    };

    private static Tier ToTier(string text) => text switch
    {
        "basic" => Tier.Basic,
        "pro" => Tier.Pro,
        "enterprise" => Tier.Enterprise,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown Tier value in the database.")
    };

    private static string ToDatabase(TransactionStatus value) => value switch
    {
        TransactionStatus.Open => "open",
        TransactionStatus.Parked => "parked",
        TransactionStatus.Completed => "completed",
        TransactionStatus.Voided => "voided",
        TransactionStatus.Refunded => "refunded",
        TransactionStatus.PartiallyRefunded => "partially_refunded",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped TransactionStatus.")
    };

    private static TransactionStatus ToTransactionStatus(string text) => text switch
    {
        "open" => TransactionStatus.Open,
        "parked" => TransactionStatus.Parked,
        "completed" => TransactionStatus.Completed,
        "voided" => TransactionStatus.Voided,
        "refunded" => TransactionStatus.Refunded,
        "partially_refunded" => TransactionStatus.PartiallyRefunded,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown TransactionStatus value in the database.")
    };

    private static string ToDatabase(Urgency value) => value switch
    {
        Urgency.Quiet => "quiet",
        Urgency.Standard => "standard",
        Urgency.Warning => "warning",
        Urgency.Critical => "critical",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped Urgency.")
    };

    private static Urgency ToUrgency(string text) => text switch
    {
        "quiet" => Urgency.Quiet,
        "standard" => Urgency.Standard,
        "warning" => Urgency.Warning,
        "critical" => Urgency.Critical,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown Urgency value in the database.")
    };

    private static string ToDatabase(VariantStatus value) => value switch
    {
        VariantStatus.Active => "active",
        VariantStatus.Discontinued => "discontinued",
        VariantStatus.Archived => "archived",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped VariantStatus.")
    };

    private static VariantStatus ToVariantStatus(string text) => text switch
    {
        "active" => VariantStatus.Active,
        "discontinued" => VariantStatus.Discontinued,
        "archived" => VariantStatus.Archived,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown VariantStatus value in the database.")
    };

    private static string ToDatabase(Rounding value) => value switch
    {
        Rounding.HalfEven => "half_even",
        Rounding.HalfUp => "half_up",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped Rounding.")
    };

    private static Rounding ToRounding(string text) => text switch
    {
        "half_even" => Rounding.HalfEven,
        "half_up" => Rounding.HalfUp,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown Rounding value in the database.")
    };

    private static string ToDatabase(VarianceReferenceType value) => value switch
    {
        VarianceReferenceType.Transaction => "transaction",
        VarianceReferenceType.PurchaseOrder => "purchase_order",
        VarianceReferenceType.Batch => "batch",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped VarianceReferenceType.")
    };

    private static VarianceReferenceType ToVarianceReferenceType(string text) => text switch
    {
        "transaction" => VarianceReferenceType.Transaction,
        "purchase_order" => VarianceReferenceType.PurchaseOrder,
        "batch" => VarianceReferenceType.Batch,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown VarianceReferenceType value in the database.")
    };

    private static string ToDatabase(VarianceSource value) => value switch
    {
        VarianceSource.CashTender => "cash_tender",
        VarianceSource.CurrencyConversion => "currency_conversion",
        _ => throw new ArgumentOutOfRangeException(
            nameof(value), value, "Unmapped VarianceSource.")
    };

    private static VarianceSource ToVarianceSource(string text) => text switch
    {
        "cash_tender" => VarianceSource.CashTender,
        "currency_conversion" => VarianceSource.CurrencyConversion,
        _ => throw new ArgumentOutOfRangeException(
            nameof(text), text, "Unknown VarianceSource value in the database.")
    };
}
