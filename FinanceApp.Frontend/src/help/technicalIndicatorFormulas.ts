import type { HelpArticle } from './models';

/**
 * Detailed, implementation-aligned reference for the metrics returned by
 * GET /api/stocks/{id}/technical-analysis.
 */
export const TECHNICAL_INDICATOR_FORMULAS_ARTICLE: HelpArticle = {
  slug: 'technical-indicator-formulas',
  categorySlug: 'analytics',
  title: 'Формулы технических показателей FinanceApp',
  summary: 'Точные формулы, требования к истории, единицы измерения и влияние SMA, EMA, RSI14, MACD, доходности, волатильности, просадки и ATR14 на аналитический сигнал.',
  keywords: [
    'формулы', 'SMA20', 'SMA50', 'SMA200', 'EMA12', 'EMA26', 'RSI14', 'Wilder',
    'MACD 12/26/9', 'Signal', 'Histogram', 'доходность', 'Volatility20',
    'Volatility60', 'Current Drawdown', 'Max Drawdown', 'ATR14', 'True Range',
    'AdjustedClose', 'Close', 'Confidence', 'Недостаточно данных',
  ],
  order: 3.5,
  sections: [
    {
      slug: 'indicator-methodology',
      title: 'Где и по каким данным выполняется расчёт',
      keywords: ['backend', 'persisted data', 'daily candles', 'provider refresh', 'дедупликация'],
      blocks: [
        { type: 'paragraph', text: 'Показатели рассчитывает backend FinanceApp по сохранённым дневным свечам (интервал 1d). Yahoo/Finnhub не передают RSI, MACD или другие показатели в готовом виде. Открытие блока аналитики выполняет read-only чтение базы и не запускает запрос к провайдеру, обновление истории или изменение расписания.' },
        { type: 'list', items: [
          'Загружается до 800 последних дневных свечей; затем они сортируются по времени по возрастанию.',
          'При одинаковом timestamp сохраняется запись с наибольшим Id («последняя» запись); остальные дают предупреждение DUPLICATE_CANDLES.',
          'Для каждой свечи effective close выбирается отдельно: положительный AdjustedClose, иначе положительный Close. Если оба непригодны, свеча исключается.',
          'AdjustedClose coverage — доля использованных свечей, для которых был выбран AdjustedClose. Неполное покрытие добавляет предупреждение и снижает Confidence.',
          'ATR14 — исключение: он использует исходные, не скорректированные High, Low и предыдущий Close, потому что adjusted OHLC в базе не хранится.',
          'Свеча считается потенциально устаревшей, если последняя дата старше текущей UTC-даты более чем на 3 дня.',
          'Отсутствующее значение возвращается как null и показывается как «Недостаточно данных»; это не ноль и не отрицательная оценка.',
        ] },
        { type: 'callout', calloutType: 'important', title: 'Какая семантика актуальна для экрана', body: [
          'Экран аналитического сигнала использует per-candle fallback: AdjustedClose выбирается отдельно для каждой свечи, иначе используется Close этой свечи.',
          'Значения могут отличаться от брокера или графического сервиса из-за набора и времени свечей, исправлений провайдера, adjusted/raw базы, инициализации EMA и округления.',
        ] },
        { type: 'table', columns: ['Метрика', 'Минимум данных', 'Единица'], rows: [
          ['SMA20 / SMA50 / SMA200', '20 / 50 / 200 закрытий', 'Цена инструмента'],
          ['EMA12 / EMA26', '12 / 26 закрытий', 'Цена инструмента'],
          ['RSI14', '15 закрытий (14 изменений)', '0–100, безразмерная'],
          ['MACD line', '26 закрытий', 'Цена инструмента'],
          ['MACD Signal / Histogram', '34 закрытия', 'Цена инструмента'],
          ['Доходность 1м / 3м / 6м / 1г', '22 / 64 / 127 / 253 закрытия', 'Процентные пункты, %'],
          ['Volatility20 / Volatility60', '21 / 61 закрытие', 'Доля; UI показывает % годовых'],
          ['Current drawdown (поле MaxDrawdown)', '1 закрытие; окно до 252', 'Процентные пункты, ≤ 0%'],
          ['ATR14', '15 свечей OHLC', 'Абсолютные единицы цены'],
        ] },
      ],
    },
    {
      slug: 'indicator-sma-ema',
      title: 'SMA20/50/200 и EMA12/26',
      keywords: ['simple moving average', 'exponential moving average', 'k=2/(N+1)', 'seed SMA'],
      blocks: [
        { type: 'paragraph', text: 'Что измеряет. SMA показывает простое среднее последних N закрытий. EMA быстрее реагирует на новые цены, потому что последним наблюдениям назначается больший вес.' },
        { type: 'paragraph', text: 'Формула FinanceApp: SMA(N) = (P₁ + … + Pₙ) / N по последним N значениям. EMA сначала инициализируется как SMA первых N значений используемой последовательности, затем EMAₜ = Pₜ × k + EMAₜ₋₁ × (1 − k), где k = 2 / (N + 1).' },
        { type: 'list', items: [
          'SMA20, SMA50 и SMA200 требуют соответственно 20, 50 и 200 положительных закрытий.',
          'EMA12 и EMA26 требуют минимум 12 и 26 положительных закрытий. Рекурсия выполняется по всей загруженной нормализованной последовательности.',
          'Trend начинается с 50: цена выше SMA50 даёт +10, иначе −10; цена выше SMA200 даёт +10, иначе −10.',
          'SMA50 > SMA200 даёт Trend +15, иначе −15. SMA20 > SMA50 даёт +8, иначе −8.',
          'Momentum: EMA12 > EMA26 даёт +8, иначе (включая равенство) −8.',
        ] },
        { type: 'callout', calloutType: 'warning', title: 'Ограничение', body: ['SMA200 = «Недостаточно данных» обычно означает менее 200 пригодных дневных закрытий, а не ошибку UI. Разная инициализация EMA — частая причина небольших расхождений с внешними сервисами.'] },
      ],
    },
    {
      slug: 'indicator-rsi14',
      title: 'RSI14: формула Уайлдера и пороги Momentum',
      keywords: ['Relative Strength Index', 'RS', 'avgGain', 'avgLoss', 'перекупленность', 'перепроданность'],
      blocks: [
        { type: 'paragraph', text: 'Что измеряет. RSI14 сравнивает сглаженные средние положительных и отрицательных изменений цены. FinanceApp рассчитывает его самостоятельно; требуется минимум 15 закрытий, то есть 14 изменений.' },
        { type: 'paragraph', text: 'Формула FinanceApp: seed AvgGain и AvgLoss — средние первых 14 приростов и модулей падений. Далее применяется сглаживание Уайлдера: AvgGainₜ = (AvgGainₜ₋₁ × 13 + Gainₜ) / 14 и аналогично для AvgLoss. RS = AvgGain / AvgLoss; RSI = 100 − 100 / (1 + RS).' },
        { type: 'table', columns: ['RSI14', 'Изменение Momentum', 'Интерпретация FinanceApp'], rows: [
          ['55–70 включительно', '+10', 'Конструктивный бычий диапазон'],
          ['> 70', '+2 и warning', 'Перекупленность; импульс может быть растянут'],
          ['45–<55', '0', 'Околонейтральная зона'],
          ['30–<45', '−8', 'Слабый импульс'],
          ['< 30', '−2 и warning', 'Перепроданность; снижение может быть растянуто'],
        ] },
        { type: 'callout', calloutType: 'example', title: 'Пример RSI', body: [
          'Если за первые 14 изменений суммарный рост равен 7, а суммарное падение равно 7, AvgGain = AvgLoss = 0,5, RS = 1 и RSI = 50.',
          'Если падений нет (AvgLoss = 0), FinanceApp возвращает RSI = 100. Если приростов нет, RS = 0 и RSI = 0.',
        ] },
        { type: 'callout', calloutType: 'important', body: ['«Перекупленность» и «перепроданность» — предупреждения о состоянии импульса, а не автоматические команды Buy/Sell и не прогноз разворота. Неположительная цена делает расчёт недоступным.'] },
      ],
    },
    {
      slug: 'indicator-macd',
      title: 'MACD 12/26/9, Signal и Histogram',
      keywords: ['MACD line', 'signal line', 'histogram', '26 свечей', '34 свечи'],
      blocks: [
        { type: 'paragraph', text: 'Что измеряет. MACD отражает расхождение быстрой EMA12 и медленной EMA26. FinanceApp рассчитывает MACD line = EMA12 − EMA26, Signal line = EMA9 последовательности MACD, Histogram = MACD line − Signal line.' },
        { type: 'list', items: [
          'EMA12 инициализируется SMA первых 12 закрытий и продвигается до 26-й точки; EMA26 инициализируется SMA первых 26 закрытий.',
          'MACD line доступна с 26 закрытий. При 26–33 закрытиях Signal и Histogram остаются null: это корректный частичный результат.',
          'С 34 закрытий Signal инициализируется средним первых 9 значений MACD, затем сглаживается как EMA9 с k = 2 / 10.',
          'Histogram ≥ 0 добавляет Momentum +12; Histogram < 0 вычитает 12.',
        ] },
        { type: 'callout', calloutType: 'example', title: 'Пример гистограммы', body: ['Если MACD line = 1,10, а Signal line = 0,80, Histogram = 1,10 − 0,80 = 0,30. Положительное значение добавляет +12 к внутренней Momentum-оценке.'] },
        { type: 'callout', calloutType: 'warning', body: ['Положительная гистограмма означает, что MACD сейчас выше Signal, но сама по себе не гарантирует рост цены. При 26–33 свечах линия MACD есть, однако Histogram ещё не участвует в Momentum.'] },
      ],
    },
    {
      slug: 'indicator-returns',
      title: 'Доходности 1м, 3м, 6м и 1г',
      keywords: ['Return1Month', 'Return3Months', 'Return6Months', 'Return1Year', '21 63 126 252'],
      blocks: [
        { type: 'paragraph', text: 'Формула FinanceApp: Return(N) = (Pпоследняя / P N торговых дней назад − 1) × 100. Результат уже выражен в процентных пунктах: 5 означает 5%, а не 500%.' },
        { type: 'table', columns: ['Период API/UI', 'Lookback', 'Требуемые закрытия'], rows: [
          ['1 месяц', '21 торговый день', '22'],
          ['3 месяца', '63 торговых дня', '64'],
          ['6 месяцев', '126 торговых дней', '127'],
          ['1 год', '252 торговых дня', '253'],
        ] },
        { type: 'paragraph', text: 'Компонент Returns использует взвешенную доходность: для 3м — 1м×0,4 + 3м×0,6; для 6м — 3м×0,4 + 6м×0,6; для 1г — 6м×0,4 + 1г×0,6; для 2г — доступную 1г×1,0. Если одно окно отсутствует, веса доступных окон ренормализуются. Вклад ограничивается диапазоном −25…+25, затем Returns score = 50 + вклад.' },
        { type: 'callout', calloutType: 'example', title: 'Пример доходности', body: ['Цена 21 торговый день назад = 100, последняя = 108. Return1Month = (108 / 100 − 1) × 100 = 8%. Для расчёта нужны 22 точки: начальная, 20 промежуточных и последняя.'] },
        { type: 'paragraph', text: 'Значение может отличаться от изменения текущей intraday-цены: используются сохранённые дневные закрытия и торговые наблюдения, а не календарные месяцы.' },
      ],
    },
    {
      slug: 'indicator-volatility-drawdown',
      title: 'Volatility20/60 и текущая просадка',
      keywords: ['log returns', 'population variance', 'sqrt 252', 'CurrentDrawdown', 'MaxDrawdown'],
      blocks: [
        { type: 'paragraph', text: 'Volatility измеряет разброс дневных логарифмических доходностей. Для каждой пары rₜ = ln(Pₜ / Pₜ₋₁); затем берётся population standard deviation с делителем N и годовая нормализация: Volatility(N) = σpopulation(r) × √252. Volatility20 требует 21 закрытие, Volatility60 — 61.' },
        { type: 'callout', calloutType: 'example', title: 'Пример волатильности', body: ['Если population standard deviation дневных log returns равен 0,01, годовая волатильность = 0,01 × √252 ≈ 0,1587; UI показывает 15,9%. Постоянный ценовой ряд корректно даёт 0%.'] },
        { type: 'table', columns: ['Volatility60 в UI', 'Изменение Risk'], rows: [
          ['≤ 20%', '+12'],
          ['>20%–≤30%', '0'],
          ['>30%–≤40%', '−10'],
          ['>40%', '−20'],
        ] },
        { type: 'paragraph', text: 'Поле API/UI называется MaxDrawdown, но текущая реализация вычисляет Current Drawdown: отклонение последней цены от максимального закрытия в окне до 252 последних наблюдений. Формула: (Pпоследняя / MaxCloseокна − 1) × 100. Это значение ≤ 0%; 0% означает, что последняя цена равна максимуму окна. Это НЕ максимальная историческая просадка peak-to-trough.' },
        { type: 'table', columns: ['Current drawdown', 'Изменение Risk'], rows: [
          ['≥ −10%', '+10'],
          ['<−10%–≥−20%', '0'],
          ['< −20%–≥−35%', '−10'],
          ['< −35%', '−20'],
        ] },
        { type: 'callout', calloutType: 'example', title: 'Пример просадки', body: ['Максимальное закрытие окна = 120, последнее = 90. Current drawdown = (90 / 120 − 1) × 100 = −25%; Risk получает −10.'] },
      ],
    },
    {
      slug: 'indicator-atr14',
      title: 'ATR14 и True Range',
      keywords: ['Average True Range', 'TR', 'raw OHLC', 'Wilder smoothing'],
      blocks: [
        { type: 'paragraph', text: 'ATR14 измеряет типичный абсолютный дневной диапазон с учётом гэпов. Для каждой свечи со второй: TR = max(High − Low, |High − Previous Close|, |Low − Previous Close|). Нужны минимум 15 свечей, чтобы получить 14 TR.' },
        { type: 'paragraph', text: 'Seed ATR — среднее первых 14 TR. Далее сглаживание Уайлдера: ATRₜ = (ATRₜ₋₁ × 13 + TRₜ) / 14. Результат выражен в абсолютных единицах цены, не в процентах.' },
        { type: 'callout', calloutType: 'example', title: 'Пример True Range и нормализации', body: ['High = 105, Low = 99, Previous Close = 100: TR = max(6, 5, 1) = 6. Если итоговый ATR14 = 2,5, а latest price = 100, ATR% для Risk = 2,5 / 100 × 100 = 2,5%.'] },
        { type: 'table', columns: ['ATR14 / latest price', 'Изменение Risk'], rows: [
          ['≤ 2%', '+6'],
          ['>2%–≤5%', '0'],
          ['>5%', '−12'],
        ] },
        { type: 'callout', calloutType: 'warning', body: ['ATR использует raw High/Low/previous Close, поскольку adjusted OHLC не хранится. Поэтому сплиты и другие корпоративные события могут временно искажать ATR сильнее, чем close-based показатели. ATR не показывает направление движения.'] },
      ],
    },
    {
      slug: 'indicator-missing-confidence',
      title: 'Недостающие данные, Confidence и ограничения',
      keywords: ['null', 'renormalization', 'stale', 'coverage', 'comparability', 'disclaimer'],
      blocks: [
        { type: 'list', items: [
          'Недоступная метрика возвращается как null, а не 0. Компонент без доступных метрик исключается; итоговый Score ренормализует веса оставшихся компонентов.',
          'Confidence учитывает покрытие истории (45%), свежесть (20%), AdjustedClose coverage (15%) и долю доступных компонентов (20%). Для длинных горизонтов дополнительно учитываются фундаментальные данные.',
          'Неполный AdjustedClose coverage снижает соответствующий factor Confidence и добавляет предупреждение ADJUSTED_CLOSE_FALLBACK.',
          'Постоянный ряд даёт volatility = 0 и предупреждение CONSTANT_PRICE_SERIES; это математический результат, но качество источника всё равно следует проверить.',
          'Сравнивая с внешним сервисом, сверяйте биржу, timezone/timestamp свечей, adjusted/raw режим, длину истории, EMA seed и округление.',
        ] },
        { type: 'callout', calloutType: 'important', body: ['Технические показатели описывают исторические данные. Они не являются прогнозом, гарантией результата, персональной инвестиционной рекомендацией или самостоятельной командой Buy/Sell.'] },
      ],
    },
  ],
  related: [
    { articleSlug: 'analytical-signal', sectionSlug: 'signal-components-weights', label: 'Как показатели входят в итоговый сигнал' },
    { articleSlug: 'data-quality-and-freshness', label: 'Качество и свежесть входных данных' },
    { articleSlug: 'technical-indicators', label: 'Краткий обзор технических показателей' },
  ],
};
