-- =============================================================================
-- Waymark — append-only enforcement
-- File: triggers.sql
-- =============================================================================
--
-- These triggers are NOT in the EF Core model, and cannot be. EF does not know
-- about triggers, and a table rebuild drops every trigger on the table it
-- rebuilds — DROP TABLE takes them with it. So they live here and are
-- re-applied after every Migrate(), which is the only thing that puts them
-- back (CLAUDE.md §3.7, decisions.md D-016 cost 5).
--
-- Every statement is idempotent: DROP IF EXISTS, then CREATE. Running this file
-- twice is a no-op, and running it against a database that has just lost its
-- triggers restores them.
--
-- Extracted verbatim from schema_v7_1.sql section 13. That file is frozen, so
-- this one is now the only place these definitions are maintained.
-- These tables are audit trails or financial ledgers. A correction is a new
-- row, never an edit. The database enforces it so no call site has to remember.
--
-- processing_log is deliberately NOT protected against UPDATE: erasure must
-- purge its identity columns.
-- outbox is deliberately NOT protected: rows are deleted after acknowledgement.
-- =============================================================================

DROP TRIGGER IF EXISTS trg_consent_events_no_update;
CREATE TRIGGER trg_consent_events_no_update
BEFORE UPDATE ON consent_events
BEGIN
    SELECT RAISE(ABORT, 'consent_events is append-only: withdrawal is a new event');
END;

DROP TRIGGER IF EXISTS trg_consent_events_no_delete;
CREATE TRIGGER trg_consent_events_no_delete
BEFORE DELETE ON consent_events
BEGIN
    SELECT RAISE(ABORT, 'consent_events is append-only');
END;

DROP TRIGGER IF EXISTS trg_stock_movements_no_update;
CREATE TRIGGER trg_stock_movements_no_update
BEFORE UPDATE ON stock_movements
BEGIN
    SELECT RAISE(ABORT, 'stock_movements is append-only: post a correcting movement');
END;

DROP TRIGGER IF EXISTS trg_stock_movements_no_delete;
CREATE TRIGGER trg_stock_movements_no_delete
BEFORE DELETE ON stock_movements
BEGIN
    SELECT RAISE(ABORT, 'stock_movements is append-only');
END;

DROP TRIGGER IF EXISTS trg_loyalty_movements_no_update;
CREATE TRIGGER trg_loyalty_movements_no_update
BEFORE UPDATE ON loyalty_movements
BEGIN
    SELECT RAISE(ABORT, 'loyalty_movements is append-only: post a reversing movement');
END;

DROP TRIGGER IF EXISTS trg_loyalty_movements_no_delete;
CREATE TRIGGER trg_loyalty_movements_no_delete
BEFORE DELETE ON loyalty_movements
BEGIN
    SELECT RAISE(ABORT, 'loyalty_movements is append-only');
END;

DROP TRIGGER IF EXISTS trg_credit_movements_no_update;
CREATE TRIGGER trg_credit_movements_no_update
BEFORE UPDATE ON credit_movements
BEGIN
    SELECT RAISE(ABORT, 'credit_movements is append-only: post a reversing movement');
END;

DROP TRIGGER IF EXISTS trg_credit_movements_no_delete;
CREATE TRIGGER trg_credit_movements_no_delete
BEFORE DELETE ON credit_movements
BEGIN
    SELECT RAISE(ABORT, 'credit_movements is append-only');
END;

DROP TRIGGER IF EXISTS trg_rec_decisions_no_update;
CREATE TRIGGER trg_rec_decisions_no_update
BEFORE UPDATE ON recommendation_decisions
    WHEN OLD.applied_at IS NOT NULL
BEGIN
    SELECT RAISE(ABORT, 'a decision cannot be changed once applied');
END;

DROP TRIGGER IF EXISTS trg_rec_decisions_no_delete;
CREATE TRIGGER trg_rec_decisions_no_delete
BEFORE DELETE ON recommendation_decisions
BEGIN
    SELECT RAISE(ABORT, 'recommendation_decisions is append-only');
END;

DROP TRIGGER IF EXISTS trg_erasure_ledger_no_delete;
CREATE TRIGGER trg_erasure_ledger_no_delete
BEFORE DELETE ON erasure_ledger
BEGIN
    SELECT RAISE(ABORT, 'erasure_ledger is the evidence of compliance and cannot be deleted');
END;
