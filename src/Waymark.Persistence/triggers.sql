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
-- processing_log refuses UPDATE but not DELETE. It never holds a direct
-- identifier (D-045), so erasure has nothing to purge in it; DELETE stays open
-- for the retention roll-up into processing_counters.
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

DROP TRIGGER IF EXISTS trg_receivable_movements_no_update;
CREATE TRIGGER trg_receivable_movements_no_update
BEFORE UPDATE ON receivable_movements
BEGIN
    SELECT RAISE(ABORT, 'receivable_movements is append-only: post a reversing movement');
END;

DROP TRIGGER IF EXISTS trg_receivable_movements_no_delete;
CREATE TRIGGER trg_receivable_movements_no_delete
BEFORE DELETE ON receivable_movements
BEGIN
    SELECT RAISE(ABORT, 'receivable_movements is append-only');
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

-- erasure_ledger is a record of what was asked and done, so its facts never change: who the
-- subject was, which request, when it was asked, what it covered, when the row was written.
-- Only the outcome moves, and only forward (D-060, F-18):
--   status      pending -> blocked | executed; blocked -> pending | executed; executed is final.
--   executed_at and executed_by are written with the move to executed, and not before.
--   cloud_confirmed_at is written once, after execution.
-- Times never run backwards: executed_at >= requested_at, cloud_confirmed_at >= executed_at.
DROP TRIGGER IF EXISTS trg_erasure_ledger_facts_fixed;
CREATE TRIGGER trg_erasure_ledger_facts_fixed
BEFORE UPDATE ON erasure_ledger
    WHEN NEW.erasure_id IS NOT OLD.erasure_id
      OR NEW.subject_type IS NOT OLD.subject_type
      OR NEW.subject_id IS NOT OLD.subject_id
      OR NEW.request_id IS NOT OLD.request_id
      OR NEW.requested_at IS NOT OLD.requested_at
      OR NEW.scope_json IS NOT OLD.scope_json
      OR NEW.created_at IS NOT OLD.created_at
BEGIN
    SELECT RAISE(ABORT, 'erasure_ledger facts are fixed: only the outcome of an erasure may be recorded');
END;

DROP TRIGGER IF EXISTS trg_erasure_ledger_executed_final;
CREATE TRIGGER trg_erasure_ledger_executed_final
BEFORE UPDATE ON erasure_ledger
    WHEN OLD.status = 'executed'
     AND (NEW.status IS NOT OLD.status
      OR NEW.executed_at IS NOT OLD.executed_at
      OR NEW.executed_by IS NOT OLD.executed_by
      OR NEW.blocked_reason IS NOT OLD.blocked_reason
      OR (OLD.cloud_confirmed_at IS NOT NULL AND NEW.cloud_confirmed_at IS NOT OLD.cloud_confirmed_at))
BEGIN
    SELECT RAISE(ABORT, 'an executed erasure is final: only the cloud confirmation may still be recorded');
END;

DROP TRIGGER IF EXISTS trg_erasure_ledger_time_flow;
CREATE TRIGGER trg_erasure_ledger_time_flow
BEFORE UPDATE ON erasure_ledger
    WHEN (NEW.status <> 'executed'
          AND (NEW.executed_at IS NOT NULL OR NEW.executed_by IS NOT NULL OR NEW.cloud_confirmed_at IS NOT NULL))
      OR (NEW.status = 'executed' AND NEW.executed_at < NEW.requested_at)
      OR (NEW.cloud_confirmed_at IS NOT NULL AND NEW.cloud_confirmed_at < NEW.executed_at)
BEGIN
    SELECT RAISE(ABORT, 'erasure_ledger runs forward: executed after it was requested, confirmed after it was executed');
END;

DROP TRIGGER IF EXISTS trg_erasure_ledger_time_flow_on_insert;
CREATE TRIGGER trg_erasure_ledger_time_flow_on_insert
BEFORE INSERT ON erasure_ledger
    WHEN (NEW.status <> 'executed'
          AND (NEW.executed_at IS NOT NULL OR NEW.executed_by IS NOT NULL OR NEW.cloud_confirmed_at IS NOT NULL))
      OR (NEW.status = 'executed' AND NEW.executed_at < NEW.requested_at)
      OR (NEW.cloud_confirmed_at IS NOT NULL AND NEW.cloud_confirmed_at < NEW.executed_at)
BEGIN
    SELECT RAISE(ABORT, 'erasure_ledger runs forward: executed after it was requested, confirmed after it was executed');
END;

-- rounding_variance is a ledger: a minor unit that was created or destroyed
-- cannot be un-created. A correction is a new row with the opposite amount.
-- Without these, the reconciliation in D-034 could be made to balance by
-- editing it, which is the one thing an audit trail must not allow.
-- processing_log is the Art. 41 bis 3 logbook the DPIA (§5.5) calls
-- append-only: an entry the audited party could edit is not evidence. No DELETE
-- guard, because after the statutory period detail is rolled up into
-- processing_counters and dropped (D-045).
DROP TRIGGER IF EXISTS trg_processing_log_no_update;
CREATE TRIGGER trg_processing_log_no_update
BEFORE UPDATE ON processing_log
BEGIN
    SELECT RAISE(ABORT, 'processing_log is append-only: an entry is never edited');
END;

DROP TRIGGER IF EXISTS trg_rounding_variance_no_update;
CREATE TRIGGER trg_rounding_variance_no_update
BEFORE UPDATE ON rounding_variance
BEGIN
    SELECT RAISE(ABORT, 'rounding_variance is append-only: post a correcting row');
END;

DROP TRIGGER IF EXISTS trg_rounding_variance_no_delete;
CREATE TRIGGER trg_rounding_variance_no_delete
BEFORE DELETE ON rounding_variance
BEGIN
    SELECT RAISE(ABORT, 'rounding_variance is append-only');
END;

-- -----------------------------------------------------------------------------
-- No REPLACE. `INSERT OR REPLACE` resolves a key conflict by deleting the old row,
-- and SQLite fires no DELETE trigger for that unless recursive_triggers is on, so
-- every guard above could be walked round with one statement: the row is
-- rewritten in place and nothing refuses. A BEFORE INSERT trigger runs before
-- conflict resolution, on any connection, whatever its pragmas. A plain duplicate
-- insert would fail on the key anyway; this only closes the REPLACE path. Found
-- in the Phase 0 final test. receivable_movements also has unique indexes, and
-- a conflict on those deletes a row just the same.
-- -----------------------------------------------------------------------------

DROP TRIGGER IF EXISTS trg_consent_events_no_replace;
CREATE TRIGGER trg_consent_events_no_replace
BEFORE INSERT ON consent_events
    WHEN EXISTS (SELECT 1 FROM consent_events WHERE consent_event_id = NEW.consent_event_id)
BEGIN
    SELECT RAISE(ABORT, 'consent_events is append-only: a row is never replaced');
END;

DROP TRIGGER IF EXISTS trg_stock_movements_no_replace;
CREATE TRIGGER trg_stock_movements_no_replace
BEFORE INSERT ON stock_movements
    WHEN EXISTS (SELECT 1 FROM stock_movements WHERE movement_id = NEW.movement_id)
BEGIN
    SELECT RAISE(ABORT, 'stock_movements is append-only: a row is never replaced');
END;

DROP TRIGGER IF EXISTS trg_loyalty_movements_no_replace;
CREATE TRIGGER trg_loyalty_movements_no_replace
BEFORE INSERT ON loyalty_movements
    WHEN EXISTS (SELECT 1 FROM loyalty_movements WHERE movement_id = NEW.movement_id)
BEGIN
    SELECT RAISE(ABORT, 'loyalty_movements is append-only: a row is never replaced');
END;

DROP TRIGGER IF EXISTS trg_credit_movements_no_replace;
CREATE TRIGGER trg_credit_movements_no_replace
BEFORE INSERT ON credit_movements
    WHEN EXISTS (SELECT 1 FROM credit_movements WHERE movement_id = NEW.movement_id)
BEGIN
    SELECT RAISE(ABORT, 'credit_movements is append-only: a row is never replaced');
END;

DROP TRIGGER IF EXISTS trg_receivable_movements_no_replace;
CREATE TRIGGER trg_receivable_movements_no_replace
BEFORE INSERT ON receivable_movements
    WHEN EXISTS (SELECT 1 FROM receivable_movements WHERE movement_id = NEW.movement_id)
      OR (NEW.payment_id IS NOT NULL AND EXISTS (SELECT 1 FROM receivable_movements WHERE payment_id = NEW.payment_id))
      OR (NEW.cash_movement_id IS NOT NULL AND EXISTS (SELECT 1 FROM receivable_movements WHERE cash_movement_id = NEW.cash_movement_id))
BEGIN
    SELECT RAISE(ABORT, 'receivable_movements is append-only: a row is never replaced');
END;

DROP TRIGGER IF EXISTS trg_recommendation_decisions_no_replace;
CREATE TRIGGER trg_recommendation_decisions_no_replace
BEFORE INSERT ON recommendation_decisions
    WHEN EXISTS (SELECT 1 FROM recommendation_decisions WHERE decision_id = NEW.decision_id)
BEGIN
    SELECT RAISE(ABORT, 'recommendation_decisions is append-only: a row is never replaced');
END;

DROP TRIGGER IF EXISTS trg_erasure_ledger_no_replace;
CREATE TRIGGER trg_erasure_ledger_no_replace
BEFORE INSERT ON erasure_ledger
    WHEN EXISTS (SELECT 1 FROM erasure_ledger WHERE erasure_id = NEW.erasure_id)
BEGIN
    SELECT RAISE(ABORT, 'erasure_ledger is append-only: a row is never replaced');
END;

DROP TRIGGER IF EXISTS trg_processing_log_no_replace;
CREATE TRIGGER trg_processing_log_no_replace
BEFORE INSERT ON processing_log
    WHEN EXISTS (SELECT 1 FROM processing_log WHERE log_id = NEW.log_id)
BEGIN
    SELECT RAISE(ABORT, 'processing_log is append-only: a row is never replaced');
END;

DROP TRIGGER IF EXISTS trg_rounding_variance_no_replace;
CREATE TRIGGER trg_rounding_variance_no_replace
BEFORE INSERT ON rounding_variance
    WHEN EXISTS (SELECT 1 FROM rounding_variance WHERE variance_id = NEW.variance_id)
BEGIN
    SELECT RAISE(ABORT, 'rounding_variance is append-only: a row is never replaced');
END;
