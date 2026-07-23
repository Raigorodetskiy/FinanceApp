-- =============================================================================
-- Импорт транзакций Flatex в FinanceApp
-- Портфель: Flatex (ID=3), Пользователь: GR (ID=3)
-- Запуск: mysql -u financeapp_user -pStrongPassword123! financeapp < scripts/import_flatex.sql
-- =============================================================================

SET NAMES utf8mb4;

-- =============================================================================
-- 1. Акции
-- =============================================================================
INSERT INTO Stocks (Ticker, Name, Exchange, CurrentPrice, UpdatedAt) VALUES
  ('MU',      'Micron Technology Inc.',                    '', 292.20,   NOW()),
  ('HAG.DE',  'Hensoldt AG',                               '', 91.75,    NOW()),
  ('INTC',    'Intel Corp.',                               '', 41.96,    NOW()),
  ('STX',     'Seagate Technology Holdings PLC',           '', 280.90,   NOW()),
  ('SNDK',    'SanDisk Corp.',                             '', 793.75,   NOW()),
  ('PLTR',    'Palantir Technologies Inc.',                '', 115.20,   NOW()),
  ('WDC',     'Western Digital Corp.',                     '', 301.25,   NOW()),
  ('MBG.DE',  'Mercedes-Benz Group AG',                   '', 51.03,    NOW()),
  ('RHM.DE',  'Rheinmetall AG',                            '', 1362.20,  NOW()),
  ('META',    'Meta Platforms Inc.',                       '', 525.80,   NOW()),
  ('QCOM',    'Qualcomm Inc.',                             '', 181.32,   NOW()),
  ('BARC.L',  'Barclays PLC',                              '', 4.95,     NOW()),
  ('AMD',     'Advanced Micro Devices Inc.',               '', 394.80,   NOW()),
  ('TSM',     'Taiwan Semiconductor Manufacturing Co. ADR','', 341.50,   NOW()),
  ('HXSCL',   'SK Hynix Inc.',                             '', 1330.00,  NOW()),
  ('JPM',     'JPMorgan Chase & Co.',                      '', 254.75,   NOW()),
  ('6600.T',  'Kioxia Holdings Corporation',               '', 349.10,   NOW()),
  ('DELL',    'Dell Technologies Inc.',                    '', 409.00,   NOW()),
  ('SPACEX',  'Space Exploration Technologies Corp. A',    '', 180.32,   NOW())
ON DUPLICATE KEY UPDATE Name=VALUES(Name), CurrentPrice=VALUES(CurrentPrice), UpdatedAt=NOW();

-- =============================================================================
-- 2. Переменные ID акций
-- =============================================================================
SET @MU     = (SELECT id FROM Stocks WHERE Ticker='MU'      LIMIT 1);
SET @HAG    = (SELECT id FROM Stocks WHERE Ticker='HAG.DE'  LIMIT 1);
SET @INTC   = (SELECT id FROM Stocks WHERE Ticker='INTC'    LIMIT 1);
SET @STX    = (SELECT id FROM Stocks WHERE Ticker='STX'     LIMIT 1);
SET @SNDK   = (SELECT id FROM Stocks WHERE Ticker='SNDK'    LIMIT 1);
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

SET @PF = 3; -- Flatex Portfolio ID

-- =============================================================================
-- 3. Ордера (Type: 0=Buy, 1=Sell | Status: 0=Pending, 1=Executed, 2=Cancelled)
-- =============================================================================
INSERT INTO Orders (PortfolioId, StockId, Type, Status, Quantity, Price, StopLoss, StopMarket, CreatedAt, ExecutedAt) VALUES
-- Январь 2026
(@PF, @MU,    0, 1,   2,    292.20, NULL, NULL, '2026-01-12 12:00:00', '2026-01-14 12:00:00'),
(@PF, @HAG,   0, 1,   5,     91.75, NULL, NULL, '2026-01-12 12:00:00', '2026-01-14 12:00:00'),
(@PF, @INTC,  0, 1,  20,     41.96, NULL, NULL, '2026-01-14 12:00:00', '2026-01-16 12:00:00'),
(@PF, @STX,   0, 1,   2,    280.90, NULL, NULL, '2026-01-19 12:00:00', '2026-01-21 12:00:00'),
(@PF, @MU,    0, 1,   1,    320.55, NULL, NULL, '2026-01-20 12:00:00', '2026-01-22 12:00:00'),
(@PF, @SNDK,  0, 1,   2,    793.75, NULL, NULL, '2026-01-21 12:00:00', '2026-01-22 12:00:00'), -- 926.10 USD / 1.168
-- Февраль 2026
(@PF, @PLTR,  1, 1,  10,    115.20, NULL, NULL, '2026-02-06 12:00:00', '2026-02-10 12:00:00'),
(@PF, @STX,   0, 1,   2,    359.95, NULL, NULL, '2026-02-06 12:00:00', '2026-02-10 12:00:00'),
-- Апрель 2026
(@PF, @WDC,   0, 1,   5,    301.25, NULL, NULL, '2026-04-14 12:00:00', '2026-04-16 12:00:00'),
(@PF, @MBG,   1, 1,  10,     51.03, NULL, NULL, '2026-04-22 12:00:00', '2026-04-24 12:00:00'),
(@PF, @HAG,   1, 1,   5,     78.68, NULL, NULL, '2026-04-22 12:00:00', '2026-04-24 12:00:00'),
(@PF, @RHM,   1, 1,   2,   1362.20, NULL, NULL, '2026-04-24 12:00:00', '2026-04-28 12:00:00'),
(@PF, @WDC,   0, 1,   3,    351.85, NULL, NULL, '2026-04-24 12:00:00', '2026-04-28 12:00:00'),
(@PF, @MU,    0, 1,   2,    431.00, NULL, NULL, '2026-04-27 12:00:00', '2026-04-29 12:00:00'),
(@PF, @STX,   0, 1,   1,    510.00, NULL, NULL, '2026-04-27 12:00:00', '2026-04-29 12:00:00'),
-- Mai 2026
(@PF, @META,  1, 1,   2,    525.80, NULL, NULL, '2026-05-08 12:00:00', '2026-05-12 12:00:00'),
(@PF, @QCOM,  0, 1,   8,    181.32, NULL, NULL, '2026-05-08 12:00:00', '2026-05-12 12:00:00'),
(@PF, @BARC,  1, 1, 400,      4.95, NULL, NULL, '2026-05-11 12:00:00', '2026-05-13 12:00:00'),
(@PF, @QCOM,  0, 1,   7,    205.70, NULL, NULL, '2026-05-11 12:00:00', '2026-05-13 12:00:00'),
(@PF, @AMD,   0, 1,   3,    394.80, NULL, NULL, '2026-05-11 12:00:00', '2026-05-13 12:00:00'),
(@PF, @RHM,   1, 1,   2,   1155.00, NULL, NULL, '2026-05-12 12:00:00', '2026-05-14 12:00:00'),
(@PF, @TSM,   0, 1,   3,    341.50, NULL, NULL, '2026-05-13 12:00:00', '2026-05-15 12:00:00'),
(@PF, @TSM,   0, 1,   2,    354.00, NULL, NULL, '2026-05-22 12:00:00', '2026-05-26 12:00:00'),
(@PF, @MU,    0, 1,   1,    696.80, NULL, NULL, '2026-05-26 12:00:00', '2026-05-28 12:00:00'),
(@PF, @MU,    0, 1,   1,    806.00, NULL, NULL, '2026-05-27 12:00:00', '2026-05-29 12:00:00'),
(@PF, @HXSCL, 0, 1,   1,   1330.00, NULL, NULL, '2026-05-29 12:00:00', '2026-06-02 12:00:00'),
(@PF, @JPM,   1, 1,   5,    254.75, NULL, NULL, '2026-05-29 12:00:00', '2026-06-02 12:00:00'),
(@PF, @KXI,   0, 1,   3,    349.10, NULL, NULL, '2026-05-29 12:00:00', '2026-06-02 12:00:00'),
-- Juni 2026
(@PF, @RHM,   1, 1,   2,   1289.40, NULL, NULL, '2026-06-01 12:00:00', '2026-06-03 12:00:00'),
(@PF, @KXI,   0, 1,   5,    392.95, NULL, NULL, '2026-06-01 12:00:00', '2026-06-03 12:00:00'),
(@PF, @DELL,  0, 1,   2,    409.00, NULL, NULL, '2026-06-02 12:00:00', '2026-06-04 12:00:00'),
(@PF, @DELL,  1, 1,   2,    349.15, NULL, NULL, '2026-06-04 12:00:00', '2026-06-08 12:00:00'),
(@PF, @MU,    0, 1,   1,    877.60, NULL, NULL, '2026-06-04 12:00:00', '2026-06-08 12:00:00'),
(@PF, @STX,   0, 1,   1,    852.00, NULL, NULL, '2026-06-15 12:00:00', '2026-06-17 12:00:00'),
(@PF, @SPACEX,0, 1,   5,    180.32, NULL, NULL, '2026-06-17 12:00:00', '2026-06-19 12:00:00'),
(@PF, @KXI,   0, 1,   2,    527.90, NULL, NULL, '2026-06-18 12:00:00', '2026-06-22 12:00:00'),
(@PF, @SPACEX,1, 1,   3,    159.92, NULL, NULL, '2026-06-18 12:00:00', '2026-06-22 12:00:00'),
(@PF, @WDC,   0, 1,   2,    686.40, NULL, NULL, '2026-06-18 12:00:00', '2026-06-22 12:00:00'),
-- Juli 2026
(@PF, @KXI,   1, 1,   5,    404.05, NULL, NULL, '2026-07-02 12:00:00', '2026-07-06 12:00:00'),
(@PF, @KXI,   0, 1,   5,    411.00, NULL, NULL, '2026-07-02 12:00:00', '2026-07-06 12:00:00'),
(@PF, @QCOM,  1, 1,   5,    155.00, NULL, NULL, '2026-07-02 12:00:00', '2026-07-06 12:00:00'),
(@PF, @HXSCL, 1, 1,   1,   1240.00, NULL, NULL, '2026-07-02 12:00:00', '2026-07-06 12:00:00'),
(@PF, @KXI,   1, 1,   5,    390.00, NULL, NULL, '2026-07-02 12:00:00', '2026-07-06 12:00:00'),
(@PF, @WDC,   1, 1,  10,    509.00, NULL, NULL, '2026-07-03 12:00:00', '2026-07-07 12:00:00'),
(@PF, @WDC,   0, 1,   4,    499.95, NULL, NULL, '2026-07-03 12:00:00', '2026-07-07 12:00:00'),
(@PF, @WDC,   0, 1,   6,    518.80, NULL, NULL, '2026-07-06 12:00:00', '2026-07-08 12:00:00'),
(@PF, @STX,   1, 1,   3,    700.00, NULL, NULL, '2026-07-07 12:00:00', '2026-07-09 12:00:00'),
(@PF, @MU,    1, 1,   8,    800.00, NULL, NULL, '2026-07-07 12:00:00', '2026-07-09 12:00:00'),
(@PF, @KXI,   1, 1,   5,    380.00, NULL, NULL, '2026-07-07 12:00:00', '2026-07-09 12:00:00'),
(@PF, @INTC,  1, 1,  30,     94.73, NULL, NULL, '2026-07-08 12:00:00', '2026-07-10 12:00:00'),
(@PF, @WDC,   1, 1,  10,    447.75, NULL, NULL, '2026-07-08 12:00:00', '2026-07-10 12:00:00'),
(@PF, @STX,   1, 1,   3,    696.00, NULL, NULL, '2026-07-08 12:00:00', '2026-07-10 12:00:00'),
(@PF, @INTC,  1, 1,  20,     91.84, NULL, NULL, '2026-07-08 12:00:00', '2026-07-10 12:00:00'),
(@PF, @AMD,   1, 1,   3,    440.00, NULL, NULL, '2026-07-08 12:00:00', '2026-07-10 12:00:00'),
(@PF, @STX,   0, 1,   4,    760.00, NULL, NULL, '2026-07-09 12:00:00', '2026-07-13 12:00:00'),
(@PF, @INTC,  0, 1,  20,     98.80, NULL, NULL, '2026-07-09 12:00:00', '2026-07-13 12:00:00'),
(@PF, @MU,    0, 1,   2,    855.60, NULL, NULL, '2026-07-09 12:00:00', '2026-07-13 12:00:00'),
(@PF, @MU,    0, 1,   2,    868.00, NULL, NULL, '2026-07-09 12:00:00', '2026-07-13 12:00:00'),
(@PF, @INTC,  0, 1,  20,    101.44, NULL, NULL, '2026-07-09 12:00:00', '2026-07-13 12:00:00'),
(@PF, @MU,    1, 1,   4,    798.30, NULL, NULL, '2026-07-13 12:00:00', '2026-07-15 12:00:00'),
(@PF, @INTC,  1, 1,  40,     91.54, NULL, NULL, '2026-07-13 12:00:00', '2026-07-15 12:00:00'),
(@PF, @TSM,   1, 1,   5,    370.00, NULL, NULL, '2026-07-13 12:00:00', '2026-07-15 12:00:00'),
(@PF, @STX,   1, 1,   4,    732.00, NULL, NULL, '2026-07-15 12:00:00', '2026-07-17 12:00:00'),
(@PF, @QCOM,  1, 1,  10,    149.54, NULL, NULL, '2026-07-16 12:00:00', '2026-07-20 12:00:00'),
(@PF, @SPACEX,1, 1,   2,    114.92, NULL, NULL, '2026-07-16 12:00:00', '2026-07-20 12:00:00');

-- =============================================================================
-- 4. PortfolioItems — текущие позиции (итоговый баланс)
-- MU:    2+1+2+1+1+1 - 8+2+2-4 = 0  => нет
-- INTC:  20 - 30-20 + 20+20-40 = -30 => нет (всё продано)
-- STX:   2+2+1+1+1 - 3-3+4-4 = 1    => 1 шт.
-- SNDK:  2                            => 2 шт.
-- WDC:   5+3+2 - 10+4+6-10+2 = 2    => 2 шт.
-- QCOM:  8+7 - 5-10 = 0             => нет
-- KXI:   3+5+2 - 5+5-5-5 = 0       => нет
-- SPACEX:5-3-2 = 0                  => нет
-- =============================================================================
INSERT INTO PortfolioItems (PortfolioId, StockId, Quantity, BuyPrice, BoughtAt) VALUES
  (@PF, @STX,  1, 852.00, '2026-06-15 12:00:00'),
  (@PF, @SNDK, 2, 793.75, '2026-01-21 12:00:00'),
  (@PF, @WDC,  2, 686.40, '2026-06-18 12:00:00');

SELECT CONCAT('Импорт завершён. Добавлено акций: ', COUNT(*)) AS result FROM Stocks WHERE Ticker IN ('MU','HAG.DE','INTC','STX','SNDK','PLTR','WDC','MBG.DE','RHM.DE','META','QCOM','BARC.L','AMD','TSM','HXSCL','JPM','6600.T','DELL','SPACEX');
