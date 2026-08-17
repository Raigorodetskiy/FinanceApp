-- ============================================================
-- 04-rollback.sql — rollback one specific repair run
-- ============================================================
-- Restores only rows from the selected RepairRunId.
-- Requires explicit confirmation and expected restore row count.
-- ============================================================

USE FinanceApp;

SET @confirm = 0;                           -- REQUIRED: set to 1
SET @repair_run_id = 'REPLACE_WITH_RUN_ID'; -- REQUIRED: exact RepairRunId from apply output
SET @expected_rows_to_restore = -1;         -- REQUIRED: expected active log rows for this run

DROP PROCEDURE IF EXISTS financeapp_rollback_tracking_status_repair;
DELIMITER $$
CREATE PROCEDURE financeapp_rollback_tracking_status_repair()
BEGIN
    DECLARE v_run_exists INT DEFAULT 0;
    DECLARE v_pending_rows INT DEFAULT 0;
    DECLARE v_rows_restored INT DEFAULT 0;
    DECLARE v_rows_marked_rolled_back INT DEFAULT 0;

    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        RESIGNAL;
    END;

    IF COALESCE(@confirm, 0) <> 1 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: set @confirm = 1 before rollback.';
    END IF;

    IF @repair_run_id IS NULL
       OR TRIM(@repair_run_id) = ''
       OR UPPER(TRIM(@repair_run_id)) = 'REPLACE_WITH_RUN_ID' THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: set @repair_run_id to the exact run id.';
    END IF;

    IF COALESCE(@expected_rows_to_restore, -1) < 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: set @expected_rows_to_restore before rollback.';
    END IF;

    SELECT COUNT(*) INTO v_run_exists
    FROM financeapp_repair_audit.tracking_status_repair_runs
    WHERE RepairRunId = TRIM(@repair_run_id);

    IF v_run_exists = 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: unknown RepairRunId.';
    END IF;

    SELECT COUNT(*) INTO v_pending_rows
    FROM financeapp_repair_audit.tracking_status_repair_log
    WHERE RepairRunId = TRIM(@repair_run_id)
      AND RolledBackAt IS NULL;

    IF v_pending_rows = 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: run has no active rows to roll back (already rolled back or empty).';
    END IF;

    IF v_pending_rows <> @expected_rows_to_restore THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'BLOCKED: expected rollback row count mismatch.';
    END IF;

    START TRANSACTION;

    UPDATE Stocks s
    JOIN financeapp_repair_audit.tracking_status_repair_log l
      ON l.StockId = s.Id
     AND l.RepairRunId = TRIM(@repair_run_id)
     AND l.RolledBackAt IS NULL
    SET s.TrackingStatus = l.PreviousStatus
    WHERE s.TrackingStatus = l.NewStatus;

    SET v_rows_restored = ROW_COUNT();

    IF v_rows_restored <> v_pending_rows THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'ABORTED: restored row count mismatch; rollback transaction reverted.';
    END IF;

    UPDATE financeapp_repair_audit.tracking_status_repair_log
    SET RolledBackAt = UTC_TIMESTAMP(6)
    WHERE RepairRunId = TRIM(@repair_run_id)
      AND RolledBackAt IS NULL;

    SET v_rows_marked_rolled_back = ROW_COUNT();

    IF v_rows_marked_rolled_back <> v_pending_rows THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'ABORTED: audit row marking mismatch; rollback transaction reverted.';
    END IF;

    COMMIT;

    SELECT
        TRIM(@repair_run_id) AS RepairRunId,
        v_pending_rows AS ExpectedRows,
        v_rows_restored AS RowsRestored,
        v_rows_marked_rolled_back AS AuditRowsMarkedRolledBack;
END$$
DELIMITER ;

CALL financeapp_rollback_tracking_status_repair();
DROP PROCEDURE financeapp_rollback_tracking_status_repair;
