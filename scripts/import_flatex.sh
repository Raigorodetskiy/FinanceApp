#!/bin/bash
# =============================================================================
# Импорт транзакций Flatex в FinanceApp
# Использование: bash import_flatex.sh <username> <password>
# =============================================================================

set -e

API="http://localhost:5000/api"
USERNAME="${1:-admin}"
PASSWORD="${2:-admin}"

echo "=== Авторизация ==="
LOGIN_RESP=$(curl -s -X POST "$API/auth/login" \
  -H "Content-Type: application/json" \
  -d "{\"username\":\"$USERNAME\",\"password\":\"$PASSWORD\"}")

TOKEN=$(echo "$LOGIN_RESP" | grep -o '"token":"[^"]*"' | cut -d'"' -f4)
if [ -z "$TOKEN" ]; then
  echo "Ошибка авторизации: $LOGIN_RESP"
  exit 1
fi
echo "Токен получен."

AUTH="-H \"Authorization: Bearer $TOKEN\""

call_api() {
  local method="$1"
  local url="$2"
  local data="$3"
  if [ -n "$data" ]; then
    curl -s -X "$method" "$API$url" \
      -H "Authorization: Bearer $TOKEN" \
      -H "Content-Type: application/json" \
      -d "$data"
  else
    curl -s -X "$method" "$API$url" \
      -H "Authorization: Bearer $TOKEN"
  fi
}

# =============================================================================
# Получить или создать акцию, вернуть её ID
# get_or_create_stock <ticker> <name> <price>
# =============================================================================
get_or_create_stock() {
  local ticker="$1"
  local name="$2"
  local price="$3"

  local stocks
  stocks=$(call_api GET "/stocks")
  local id
  id=$(echo "$stocks" | grep -o "\"id\":[0-9]*,\"ticker\":\"$ticker\"" | grep -o '"id":[0-9]*' | grep -o '[0-9]*' | head -1)

  if [ -z "$id" ]; then
    echo "  Создаю акцию $ticker..." >&2
    local resp
    resp=$(call_api POST "/stocks" "{\"ticker\":\"$ticker\",\"name\":\"$name\",\"currentPrice\":$price,\"exchange\":\"\"}")
    id=$(echo "$resp" | grep -o '"id":[0-9]*' | grep -o '[0-9]*' | head -1)
    echo "  Акция $ticker создана с ID=$id" >&2
    sleep 1
  else
    echo "  Акция $ticker уже существует, ID=$id" >&2
  fi
  echo "$id"
}

# =============================================================================
# Найти портфель Flatex
# =============================================================================
echo ""
echo "=== Поиск портфеля Flatex ==="
PORTFOLIOS=$(call_api GET "/portfolios")
PORTFOLIO_ID=$(echo "$PORTFOLIOS" | grep -o '"id":[0-9]*[^}]*"name":"Flatex"' | grep -o '"id":[0-9]*' | grep -o '[0-9]*' | head -1)

if [ -z "$PORTFOLIO_ID" ]; then
  # Попробуем другой порядок полей
  PORTFOLIO_ID=$(echo "$PORTFOLIOS" | python3 -c "
import sys, json
data = json.load(sys.stdin)
for p in data:
    if p.get('name','').lower() == 'flatex':
        print(p['id'])
        break
" 2>/dev/null)
fi

if [ -z "$PORTFOLIO_ID" ]; then
  echo "Портфель 'Flatex' не найден. Создаю..."
  RESP=$(call_api POST "/portfolios" '{"name":"Flatex"}')
  PORTFOLIO_ID=$(echo "$RESP" | grep -o '"id":[0-9]*' | grep -o '[0-9]*' | head -1)
  echo "Портфель создан с ID=$PORTFOLIO_ID"
else
  echo "Портфель Flatex найден, ID=$PORTFOLIO_ID"
fi

# =============================================================================
# Создать/найти все акции
# =============================================================================
echo ""
echo "=== Создание акций ==="

MU_ID=$(get_or_create_stock "MU" "Micron Technology Inc." 292.20)
HAG_ID=$(get_or_create_stock "HAG.DE" "Hensoldt AG" 91.75)
INTC_ID=$(get_or_create_stock "INTC" "Intel Corp." 41.96)
STX_ID=$(get_or_create_stock "STX" "Seagate Technology Holdings PLC" 280.90)
SNDK_ID=$(get_or_create_stock "SNDK" "SanDisk Corp." 793.75)
PLTR_ID=$(get_or_create_stock "PLTR" "Palantir Technologies Inc." 115.20)
WDC_ID=$(get_or_create_stock "WDC" "Western Digital Corp." 301.25)
MBG_ID=$(get_or_create_stock "MBG.DE" "Mercedes-Benz Group AG" 51.03)
RHM_ID=$(get_or_create_stock "RHM.DE" "Rheinmetall AG" 1362.20)
META_ID=$(get_or_create_stock "META" "Meta Platforms Inc." 525.80)
QCOM_ID=$(get_or_create_stock "QCOM" "Qualcomm Inc." 181.32)
BARC_ID=$(get_or_create_stock "BARC.L" "Barclays PLC" 4.95)
AMD_ID=$(get_or_create_stock "AMD" "Advanced Micro Devices Inc." 394.80)
TSM_ID=$(get_or_create_stock "TSM" "Taiwan Semiconductor Manufacturing Co. ADR" 341.50)
HXSCL_ID=$(get_or_create_stock "HXSCL" "SK Hynix Inc." 1330.00)
JPM_ID=$(get_or_create_stock "JPM" "JPMorgan Chase & Co." 254.75)
KXI_ID=$(get_or_create_stock "6600.T" "Kioxia Holdings Corporation" 349.10)
DELL_ID=$(get_or_create_stock "DELL" "Dell Technologies Inc." 409.00)
SPACEX_ID=$(get_or_create_stock "SPACEX" "Space Exploration Technologies Corp. A" 180.32)

echo ""
echo "=== Импорт транзакций ==="

# Функция создания ордера (Executed)
# create_order <stockId> <type Buy|Sell> <quantity> <price> <date>
create_order() {
  local stock_id="$1"
  local type="$2"
  local qty="$3"
  local price="$4"
  local date="$5"

  # Создаём ордер
  local resp
  resp=$(call_api POST "/portfolios/$PORTFOLIO_ID/orders" \
    "{\"stockId\":$stock_id,\"type\":\"$type\",\"quantity\":$qty,\"price\":$price}")
  local order_id
  order_id=$(echo "$resp" | grep -o '"id":[0-9]*' | grep -o '[0-9]*' | head -1)

  if [ -z "$order_id" ]; then
    echo "  ОШИБКА создания ордера: $resp" >&2
    return
  fi

  # Помечаем как Executed
  call_api PUT "/portfolios/$PORTFOLIO_ID/orders/$order_id" \
    "{\"type\":\"$type\",\"status\":\"Executed\",\"quantity\":$qty,\"price\":$price}" > /dev/null

  echo "  ✓ $type $qty шт. по $price EUR (Stock ID=$stock_id, Order ID=$order_id)"
}

# --- Январь 2026 ---
echo "Январь 2026:"
create_order "$MU_ID"    "Buy"  2    292.20  "2026-01-12"   # Micron 2 шт.
create_order "$HAG_ID"   "Buy"  5     91.75  "2026-01-12"   # Hensoldt 5 шт.
create_order "$INTC_ID"  "Buy"  20    41.96  "2026-01-14"   # Intel 20 шт.
create_order "$STX_ID"   "Buy"  2    280.90  "2026-01-19"   # Seagate 2 шт.
create_order "$MU_ID"    "Buy"  1    320.55  "2026-01-20"   # Micron 1 шт.
create_order "$SNDK_ID"  "Buy"  2    793.75  "2026-01-21"   # SanDisk 2 шт. (926.10 USD / 1.168)

# --- Февраль 2026 ---
echo "Февраль 2026:"
create_order "$PLTR_ID"  "Sell" 10   115.20  "2026-02-06"   # Palantir -10 шт.
create_order "$STX_ID"   "Buy"  2    359.95  "2026-02-06"   # Seagate 2 шт.

# --- Апрель 2026 ---
echo "Апрель 2026:"
create_order "$WDC_ID"   "Buy"  5    301.25  "2026-04-14"   # Western Digital 5 шт.
create_order "$MBG_ID"   "Sell" 10    51.03  "2026-04-22"   # Mercedes -10 шт.
create_order "$HAG_ID"   "Sell" 5     78.68  "2026-04-22"   # Hensoldt -5 шт.
create_order "$RHM_ID"   "Sell" 2   1362.20  "2026-04-24"   # Rheinmetall -2 шт.
create_order "$WDC_ID"   "Buy"  3    351.85  "2026-04-24"   # Western Digital 3 шт.
create_order "$MU_ID"    "Buy"  2    431.00  "2026-04-27"   # Micron 2 шт.
create_order "$STX_ID"   "Buy"  1    510.00  "2026-04-27"   # Seagate 1 шт.

# --- Май 2026 ---
echo "Mai 2026:"
create_order "$META_ID"  "Sell" 2    525.80  "2026-05-08"   # Meta -2 шт.
create_order "$QCOM_ID"  "Buy"  8    181.32  "2026-05-08"   # Qualcomm 8 шт.
create_order "$BARC_ID"  "Sell" 400    4.95  "2026-05-11"   # Barclays -400 шт.
create_order "$QCOM_ID"  "Buy"  7    205.70  "2026-05-11"   # Qualcomm 7 шт.
create_order "$AMD_ID"   "Buy"  3    394.80  "2026-05-11"   # AMD 3 шт.
create_order "$RHM_ID"   "Sell" 2   1155.00  "2026-05-12"   # Rheinmetall -2 шт.
create_order "$TSM_ID"   "Buy"  3    341.50  "2026-05-13"   # TSMC 3 шт.
create_order "$TSM_ID"   "Buy"  2    354.00  "2026-05-22"   # TSMC 2 шт.
create_order "$MU_ID"    "Buy"  1    696.80  "2026-05-26"   # Micron 1 шт.
create_order "$MU_ID"    "Buy"  1    806.00  "2026-05-27"   # Micron 1 шт.
create_order "$HXSCL_ID" "Buy"  1   1330.00  "2026-05-29"   # SK Hynix 1 шт.
create_order "$JPM_ID"   "Sell" 5    254.75  "2026-05-29"   # JPMorgan -5 шт.
create_order "$KXI_ID"   "Buy"  3    349.10  "2026-05-29"   # Kioxia 3 шт.

# --- Июнь 2026 ---
echo "Juni 2026:"
create_order "$RHM_ID"   "Sell" 2   1289.40  "2026-06-01"   # Rheinmetall -2 шт.
create_order "$KXI_ID"   "Buy"  5    392.95  "2026-06-01"   # Kioxia 5 шт.
create_order "$DELL_ID"  "Buy"  2    409.00  "2026-06-02"   # Dell 2 шт.
create_order "$DELL_ID"  "Sell" 2    349.15  "2026-06-04"   # Dell -2 шт.
create_order "$MU_ID"    "Buy"  1    877.60  "2026-06-04"   # Micron 1 шт.
create_order "$STX_ID"   "Buy"  1    852.00  "2026-06-15"   # Seagate 1 шт.
create_order "$SPACEX_ID" "Buy" 5    180.32  "2026-06-17"   # SpaceX 5 шт.
create_order "$KXI_ID"   "Buy"  2    527.90  "2026-06-18"   # Kioxia 2 шт.
create_order "$SPACEX_ID" "Sell" 3   159.92  "2026-06-18"   # SpaceX -3 шт.
create_order "$WDC_ID"   "Buy"  2    686.40  "2026-06-18"   # Western Digital 2 шт.

# --- Июль 2026 ---
echo "Juli 2026:"
create_order "$KXI_ID"   "Sell" 5    404.05  "2026-07-02"   # Kioxia -5 шт.
create_order "$KXI_ID"   "Buy"  5    411.00  "2026-07-02"   # Kioxia 5 шт.
create_order "$QCOM_ID"  "Sell" 5    155.00  "2026-07-02"   # Qualcomm -5 шт.
create_order "$HXSCL_ID" "Sell" 1   1240.00  "2026-07-02"   # SK Hynix -1 шт.
create_order "$KXI_ID"   "Sell" 5    390.00  "2026-07-02"   # Kioxia -5 шт.
create_order "$WDC_ID"   "Sell" 10   509.00  "2026-07-03"   # Western Digital -10 шт.
create_order "$WDC_ID"   "Buy"  4    499.95  "2026-07-03"   # Western Digital 4 шт.
create_order "$WDC_ID"   "Buy"  6    518.80  "2026-07-06"   # Western Digital 6 шт.
create_order "$STX_ID"   "Sell" 3    700.00  "2026-07-07"   # Seagate -3 шт.
create_order "$MU_ID"    "Sell" 8    800.00  "2026-07-07"   # Micron -8 шт.
create_order "$KXI_ID"   "Sell" 5    380.00  "2026-07-07"   # Kioxia -5 шт.
create_order "$INTC_ID"  "Sell" 30    94.73  "2026-07-08"   # Intel -30 шт.
create_order "$WDC_ID"   "Sell" 10   447.75  "2026-07-08"   # Western Digital -10 шт.
create_order "$STX_ID"   "Sell" 3    696.00  "2026-07-08"   # Seagate -3 шт.
create_order "$INTC_ID"  "Sell" 20    91.84  "2026-07-08"   # Intel -20 шт.
create_order "$AMD_ID"   "Sell" 3    440.00  "2026-07-08"   # AMD -3 шт.
create_order "$STX_ID"   "Buy"  4    760.00  "2026-07-09"   # Seagate 4 шт.
create_order "$INTC_ID"  "Buy"  20    98.80  "2026-07-09"   # Intel 20 шт.
create_order "$MU_ID"    "Buy"  2    855.60  "2026-07-09"   # Micron 2 шт.
create_order "$MU_ID"    "Buy"  2    868.00  "2026-07-09"   # Micron 2 шт.
create_order "$INTC_ID"  "Buy"  20   101.44  "2026-07-09"   # Intel 20 шт.
create_order "$MU_ID"    "Sell" 4    798.30  "2026-07-13"   # Micron -4 шт.
create_order "$INTC_ID"  "Sell" 40    91.54  "2026-07-13"   # Intel -40 шт.
create_order "$TSM_ID"   "Sell" 5    370.00  "2026-07-13"   # TSMC -5 шт.
create_order "$STX_ID"   "Sell" 4    732.00  "2026-07-15"   # Seagate -4 шт.
create_order "$QCOM_ID"  "Sell" 10   149.54  "2026-07-16"   # Qualcomm -10 шт.
create_order "$SPACEX_ID" "Sell" 2   114.92  "2026-07-16"   # SpaceX -2 шт.

echo ""
echo "=== Импорт завершён! ==="
echo "Портфель Flatex ID=$PORTFOLIO_ID"
