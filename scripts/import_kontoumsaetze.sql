-- =============================================================================
-- Импорт Kontoumsätze Flatex
-- Портфель: Flatex (ID=3)
-- Транзакции (Deposit), Дивиденды, Zinsabschluss
-- Запуск: mysql -u financeapp_user -pStrongPassword123! financeapp < scripts/import_kontoumsaetze.sql
-- =============================================================================

SET NAMES utf8mb4;

SET @PF = 3; -- Flatex Portfolio ID

-- Переменные ID акций
SET @MU     = (SELECT id FROM Stocks WHERE Ticker='MU'      LIMIT 1);
SET @INTC   = (SELECT id FROM Stocks WHERE Ticker='INTC'    LIMIT 1);
SET @STX    = (SELECT id FROM Stocks WHERE Ticker='STX'     LIMIT 1);
SET @PLTR   = (SELECT id FROM Stocks WHERE Ticker='PLTR'    LIMIT 1);
SET @WDC    = (SELECT id FROM Stocks WHERE Ticker='WDC'     LIMIT 1);
SET @MBG    = (SELECT id FROM Stocks WHERE Ticker='MBG.DE'  LIMIT 1);
SET @RHM    = (SELECT id FROM Stocks WHERE Ticker='RHM.DE'  LIMIT 1);
SET @META   = (SELECT id FROM Stocks WHERE Ticker='META'    LIMIT 1);
SET @QCOM   = (SELECT id FROM Stocks WHERE Ticker='QCOM'    LIMIT 1);
SET @BARC   = (SELECT id FROM Stocks WHERE Ticker='BARC.L'  LIMIT 1);
SET @AMD    = (SELECT id FROM Stocks WHERE Ticker='AMD'     LIMIT 1);
SET @TSM    = (SELECT id FROM Stocks WHERE Ticker='TSM'     LIMIT 1);
SET @HXSCL  = (SELECT id FROM Stocks WHERE Ticker='HXSCL'   LIMIT 1);
SET @JPM    = (SELECT id FROM Stocks WHERE Ticker='JPM'     LIMIT 1);
SET @KXI    = (SELECT id FROM Stocks WHERE Ticker='6600.T'  LIMIT 1);
SET @DELL   = (SELECT id FROM Stocks WHERE Ticker='DELL'    LIMIT 1);
SET @SPACEX = (SELECT id FROM Stocks WHERE Ticker='SPACEX'  LIMIT 1);
SET @SNDK   = (SELECT id FROM Stocks WHERE Ticker='SNDK'    LIMIT 1);

-- Дополнительные акции для дивидендов (возможно не в Stocks — добавляем если нужно)
-- US46625H1005 = JPM  (уже есть)
-- US0378331005 = AAPL (уже есть)
-- US5949181045 = MSFT
-- US02079K3059 = GOOGL (уже есть как GOOGL)
-- US30303M1027 = META (уже есть)
-- US38141G1040 = Goldman Sachs (GS)
-- GB0031348658 = BARC (уже есть)
-- US67066G1040 = NFLX
-- DE0007100000 = MBG (уже есть)
-- DE0007030009 = RHM (уже есть)
-- US5951121038 = MU  (уже есть)
-- IE00BKVD2N49 = STX (уже есть)
-- US9581021055 = WDC (уже есть)
-- US8740391003 = TSM (уже есть)
-- US7475251036 = QCOM (уже есть)
-- GB0005405286 = Shell PLC (SHEL)
-- US84615Q1031 = SPACEX (уже есть)

-- Добавляем акции которых нет, только для дивидендов
INSERT INTO Stocks (Ticker, Name, Exchange, CurrentPrice, UpdatedAt) VALUES
  ('MSFT',   'Microsoft Corp.',      '', 0.00, NOW()),
  ('GS',     'Goldman Sachs Group',  '', 0.00, NOW()),
  ('NFLX',   'Netflix Inc.',         '', 0.00, NOW()),
  ('SHEL',   'Shell PLC',            '', 0.00, NOW())
ON DUPLICATE KEY UPDATE Name=VALUES(Name), UpdatedAt=NOW();

SET @AAPL   = (SELECT id FROM Stocks WHERE Ticker='AAPL'  LIMIT 1);
SET @GOOGL  = (SELECT id FROM Stocks WHERE Ticker='GOOGL' LIMIT 1);
SET @MSFT   = (SELECT id FROM Stocks WHERE Ticker='MSFT'  LIMIT 1);
SET @GS     = (SELECT id FROM Stocks WHERE Ticker='GS'    LIMIT 1);
SET @NFLX   = (SELECT id FROM Stocks WHERE Ticker='NFLX'  LIMIT 1);
SET @SHEL   = (SELECT id FROM Stocks WHERE Ticker='SHEL'  LIMIT 1);

-- =============================================================================
-- 1. Пополнения счёта (Deposit)
-- TransactionType: 0=Deposit, 1=Withdrawal
-- =============================================================================
INSERT INTO Transactions (PortfolioId, Type, Amount, Description, CreatedAt) VALUES
  (@PF, 0, 1000.00, 'Kundennummer 8368195 - 120126',  '2026-01-13 12:00:00'),
  (@PF, 0,  800.00, 'Kundennummer 8368195 - 180126',  '2026-01-19 12:00:00'),
  (@PF, 0, 1000.00, 'Kundennummer 8368195 - 220126',  '2026-01-22 12:00:00'),
  (@PF, 0, 1000.00, 'Kundennummer 8368195 - 220126',  '2026-05-25 12:00:00'),
  (@PF, 0, 1000.00, 'Kundennummer 8368195 - 260526',  '2026-05-27 12:00:00'),
  (@PF, 0, 1000.00, 'Kundennummer 8368195 - 010626',  '2026-06-01 12:00:00'),
  (@PF, 0, 1000.00, 'Kundennummer 8368195 - 120626',  '2026-06-15 12:00:00'),
  (@PF, 0, 1000.00, 'Kundennummer 8368195 - 160626',  '2026-06-16 12:00:00'),
  (@PF, 0, 1000.00, 'Kundennummer 8368195 - 180626',  '2026-06-18 12:00:00');

-- =============================================================================
-- 2. Zinsabschluss (Zinsen — отрицательный = Withdrawal, 0 = игнорируем)
-- =============================================================================
INSERT INTO Transactions (PortfolioId, Type, Amount, Description, CreatedAt) VALUES
  (@PF, 1, 2.67, 'Zinsabschluss 01.04.2026 - 30.06.2026', '2026-07-01 12:00:00');
-- Zinsabschluss 01.01-31.03 = 0.00 EUR — пропускаем
-- Zinsabschluss 01.10-31.12 = 0.00 EUR — пропускаем

-- =============================================================================
-- 3. Дивиденды
-- =============================================================================
INSERT INTO Dividends (PortfolioId, StockId, Amount, PaidAt, CreatedAt) VALUES
  -- 02.02.2026 JPMorgan Dividende US46625H1005
  (@PF, @JPM,   10.69, '2026-02-02 00:00:00', NOW()),
  -- 12.02.2026 Apple Dividende US0378331005
  (@PF, @AAPL,   1.85, '2026-02-12 00:00:00', NOW()),
  -- 12.03.2026 Microsoft Dividende US5949181045
  (@PF, @MSFT,   6.68, '2026-03-12 00:00:00', NOW()),
  -- 16.03.2026 Alphabet Dividende US02079K3059
  (@PF, @GOOGL,  2.33, '2026-03-16 00:00:00', NOW()),
  -- 27.03.2026 Meta Dividende US30303M1027
  (@PF, @META,   0.77, '2026-03-27 00:00:00', NOW()),
  -- 30.03.2026 Goldman Sachs Dividende US38141G1040
  (@PF, @GS,     9.96, '2026-03-30 00:00:00', NOW()),
  -- 31.03.2026 Barclays Dividende GB0031348658
  (@PF, @BARC,  25.81, '2026-03-31 00:00:00', NOW()),
  -- 01.04.2026 Netflix Dividende US67066G1040
  (@PF, @NFLX,   0.59, '2026-04-01 00:00:00', NOW()),
  -- 09.04.2026 Seagate Dividende IE00BKVD2N49
  (@PF, @STX,    2.53, '2026-04-09 00:00:00', NOW()),
  -- 15.04.2026 Micron Dividende US5951121038
  (@PF, @MU,     0.32, '2026-04-15 00:00:00', NOW()),
  -- 21.04.2026 Mercedes-Benz Dividende DE0007100000
  (@PF, @MBG,   35.00, '2026-04-21 00:00:00', NOW()),
  -- 30.04.2026 JPMorgan Dividende US46625H1005
  (@PF, @JPM,   10.89, '2026-04-30 00:00:00', NOW()),
  -- 30.04.2026 Shell PLC Dividende GB0005405286
  (@PF, @SHEL,  76.87, '2026-04-30 00:00:00', NOW()),
  -- 15.05.2026 Apple Dividende US0378331005
  (@PF, @AAPL,   1.96, '2026-05-15 00:00:00', NOW()),
  -- 15.05.2026 Rheinmetall Dividende DE0007030009
  (@PF, @RHM,   23.00, '2026-05-15 00:00:00', NOW()),
  -- 12.06.2026 Microsoft Dividende US5949181045
  (@PF, @MSFT,   6.71, '2026-06-12 00:00:00', NOW()),
  -- 16.06.2026 Alphabet Dividende US02079K3059
  (@PF, @GOOGL,  2.41, '2026-06-16 00:00:00', NOW()),
  -- 18.06.2026 Western Digital Dividende US9581021055
  (@PF, @WDC,    0.88, '2026-06-18 00:00:00', NOW()),
  -- 26.06.2026 Qualcomm Dividende US7475251036
  (@PF, @QCOM,  10.34, '2026-06-26 00:00:00', NOW()),
  -- 26.06.2026 Shell PLC Dividende GB0005405286
  (@PF, @SHEL,  17.29, '2026-06-26 00:00:00', NOW()),
  -- 29.06.2026 Netflix Dividende US67066G1040
  (@PF, @NFLX,  14.91, '2026-06-29 00:00:00', NOW()),
  -- 30.06.2026 Goldman Sachs Dividende US38141G1040
  (@PF, @GS,    10.06, '2026-06-30 00:00:00', NOW()),
  -- 08.07.2026 Seagate Dividende IE00BKVD2N49
  (@PF, @STX,    3.88, '2026-07-08 00:00:00', NOW()),
  -- 11.07.2026 TSMC Dividende US8740391003
  (@PF, @TSM,    3.25, '2026-07-11 00:00:00', NOW()),
  -- 22.07.2026 Micron Dividende US5951121038
  (@PF, @MU,     0.89, '2026-07-22 00:00:00', NOW());

SELECT 'Импорт Kontoumsätze завершён.' AS result;
SELECT CONCAT('Пополнений: ', COUNT(*)) AS deposits FROM Transactions WHERE PortfolioId=3 AND Type=0;
SELECT CONCAT('Дивидендов: ', COUNT(*)) AS dividends FROM Dividends WHERE PortfolioId=3;
