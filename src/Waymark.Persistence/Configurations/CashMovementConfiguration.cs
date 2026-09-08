using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Waymark.Domain.Cash;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// <see cref="CashMovement"/> — the money case, and the one worth reading
/// closely before writing the other 56.
/// </summary>
internal sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    private static readonly ValueConverter<CashMovementType, string> MovementTypeConverter = new(
        type => type == CashMovementType.PaidIn ? "paid_in"
            : type == CashMovementType.PaidOut ? "paid_out"
            : type == CashMovementType.Drop ? "drop"
            : type == CashMovementType.FloatAdd ? "float_add"
            : "float_remove",
        text => text == "paid_in" ? CashMovementType.PaidIn
            : text == "paid_out" ? CashMovementType.PaidOut
            : text == "drop" ? CashMovementType.Drop
            : text == "float_add" ? CashMovementType.FloatAdd
            : CashMovementType.FloatRemove);

    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("cash_movements", table =>
        {
            table.HasCheckConstraint(
                "ck_cash_movements_movement_type",
                "movement_type IN ('paid_in','paid_out','drop','float_add','float_remove')");

            // The direction is in movement_type, so the amount is always
            // positive. Losing this constraint in a rebuild would let a
            // negative paid_in silently reverse a drawer total.
            table.HasCheckConstraint("ck_cash_movements_amount", "amount > 0");
        });

        builder.HasKey(movement => movement.MovementId);

        builder.Property(movement => movement.MovementId).HasColumnName("movement_id");
        builder.Property(movement => movement.SessionId).HasColumnName("session_id");

        builder.Property(movement => movement.MovementType)
            .HasColumnName("movement_type")
            .HasConversion(MovementTypeConverter);

        // The property is AmountCentimes and the column is "amount": the unit
        // is explicit in code, where getting it wrong is a real bug, and left
        // implicit in the schema, where the header documents it once.
        //
        // When Money lands (O-4) this becomes:
        //     builder.Property(movement => movement.Amount)
        //         .HasColumnName("amount")
        //         .HasConversion(money => money.Centimes, value => Money.FromCentimes(value));
        // The column is unaffected, so no migration is needed for that change.
        builder.Property(movement => movement.AmountCentimes).HasColumnName("amount");

        builder.Property(movement => movement.ReasonCode).HasColumnName("reason_code");
        builder.Property(movement => movement.Note).HasColumnName("note");
        builder.Property(movement => movement.StaffId).HasColumnName("staff_id");
        builder.Property(movement => movement.AuthorisedBy).HasColumnName("authorised_by");

        builder.Property(movement => movement.OccurredAt)
            .HasColumnName("occurred_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // Declared because a rebuild recreates only the indexes the model knows
        // about. The database name is pinned so it matches the schema rather
        // than EF's IX_cash_movements_session_id convention.
        builder.HasIndex(movement => movement.SessionId)
            .HasDatabaseName("ix_cash_movements_session");

        // Not configured yet, and deliberately: session_id, reason_code,
        // staff_id and authorised_by reference cash_sessions, reason_codes and
        // staff, none of which are entities yet. Foreign keys have to be in the
        // model for the same reason indexes and CHECKs do, so these must be
        // added as those entities land — the columns are mapped, the
        // relationships are not.
    }
}
