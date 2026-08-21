import type { HelpArticle } from './models';

/**
 * Beginner-friendly reference grounded in the implementation of
 * GET /api/stocks/{id}/technical-analysis.
 */
export const TECHNICAL_INDICATOR_FORMULAS_ARTICLE: HelpArticle = {
  slug: 'technical-indicator-formulas',
  categorySlug: 'analytics',
  title: 'Технические показатели: формулы, примеры и ограничения',
  summary: 'Учебное объяснение SMA, EMA, RSI14, MACD, доходности, волатильности, просадки и ATR14 — от входных свечей до Score и Confidence.',
  keywords: [
    'формулы', 'технический анализ', 'что такое скользящая средняя', 'SMA20', 'SMA50', 'SMA200',
    'EMA12', 'EMA26', 'экспоненциальная средняя', 'RSI14', 'формула RSI', 'Relative Strength Index',
    'Wilder', 'формула Уайлдера', 'MACD 12/26/9', 'MACD Signal Histogram', 'доходность', 'логарифмическая доходность',
    'Volatility20', 'Volatility60', 'Current Drawdown', 'MaxDrawdown', 'ATR14', 'True Range',
    'AdjustedClose fallback', 'Close', 'Score', 'Confidence', 'Недостаточно данных',
  ],
  order: 3.5,
  sections: [
    {
      slug: 'indicator-methodology',
      title: 'Сначала о данных: свечи, цены и торговые дни',
      keywords: ['backend', 'daily candles', 'provider refresh', 'AdjustedClose', 'Close', 'дедупликация'],
      blocks: [
        { type: 'paragraph', text: 'Технический показатель — это математическое описание уже произошедшего движения цены. Он помогает одинаково измерять тренд, импульс и риск, но не предсказывает будущее и не является командой Buy или Sell.' },
        { type: 'list', items: [
          'Backend загружает до 800 сохранённых дневных свечей 1d, сортирует их по времени и при одинаковом timestamp оставляет запись с наибольшим Id.',
          'Для каждой свечи effective price выбирается отдельно: положительный AdjustedClose, иначе положительный Close. Это per-candle fallback, а не единый режим для всего ряда.',
          'AdjustedClose coverage показывает долю точек, где использован AdjustedClose. Неполное покрытие может снизить Confidence и добавить ADJUSTED_CLOSE_FALLBACK.',
          'Открытие аналитики только читает сохранённые данные и не запускает запрос к провайдеру или обновление истории.',
          'Торговое наблюдение — строка дневной истории, а не календарный день. Поэтому 21 наблюдение примерно соответствует месяцу, но праздники и пропуски меняют календарную длину.',
          'Если истории недостаточно, метрика возвращается как null и показывается как «Недостаточно данных». null не равен нулю.',
          'Свежесть оценивается по UTC: последняя свеча считается потенциально устаревшей, если её дата отстаёт от текущей UTC-даты более чем на 3 дня.',
        ] },
        { type: 'table', columns: ['Метрика', 'Минимум данных', 'Единица'], rows: [
          ['SMA20 / SMA50 / SMA200', '20 / 50 / 200 закрытий', 'Цена'],
          ['EMA12 / EMA26', '12 / 26 закрытий', 'Цена'],
          ['RSI14', '15 закрытий (14 изменений)', '0–100'],
          ['MACD line', '26 закрытий', 'Цена'],
          ['MACD Signal / Histogram', '34 закрытия', 'Цена'],
          ['Доходность 1м / 3м / 6м / 1г', '22 / 64 / 127 / 253 закрытия', 'Процентные пункты'],
          ['Volatility20 / Volatility60', '21 / 61 закрытие', 'Доля; UI показывает % годовых'],
          ['Current drawdown (поле MaxDrawdown)', '1 закрытие; окно до 252', 'Процентные пункты, ≤ 0%'],
          ['ATR14', '15 свечей OHLC', 'Абсолютные единицы цены'],
        ] },
        { type: 'callout', calloutType: 'warning', title: 'Почему цифры могут отличаться', body: [
          'Брокер, Yahoo или TradingView могут использовать другую биржу, timezone, набор свечей, adjusted/raw режим, EMA seed, округление или длину истории. Сначала сравнивайте входные свечи и методику, а не только итоговое число.',
        ] },
      ],
    },
    {
      slug: 'indicator-sma-ema',
      title: 'SMA20/50/200 и EMA12/26: средняя цена и тренд',
      keywords: ['simple moving average', 'exponential moving average', 'seed SMA', 'k = 2 / (N + 1)'],
      blocks: [
        { type: 'paragraph', text: 'SMA отвечает на вопрос «какова средняя цена последних N наблюдений?». Формула: SMA(N) = (P₁ + … + Pₙ) / N. Например, для цен 100, 102, 101, 103 и 104: SMA5 = 510 / 5 = 102.' },
        { type: 'paragraph', text: 'EMA сильнее учитывает свежие цены. FinanceApp сначала берёт SMA первых N значений как seed, затем применяет EMAₜ = Priceₜ × k + EMAₜ₋₁ × (1 − k), где k = 2 / (N + 1). Для N=12 k≈0,1538. Если предыдущая EMA12=100, а новая цена=103, новая EMA≈103×0,1538+100×0,8462=100,46.' },
        { type: 'table', columns: ['Проверка', 'Влияние'], rows: [
          ['Цена > SMA50', 'Trend +10; иначе, включая равенство, −10'],
          ['Цена > SMA200', 'Trend +10; иначе −10'],
          ['SMA50 > SMA200', 'Trend +15; иначе −15'],
          ['SMA20 > SMA50', 'Trend +8; иначе −8'],
          ['EMA12 > EMA26', 'Momentum +8; иначе −8'],
        ] },
        { type: 'callout', calloutType: 'important', body: ['Средняя запаздывает за ценой. Пересечение описывает текущую структуру ряда, но не гарантирует продолжение движения.'] },
      ],
    },
    {
      slug: 'indicator-rsi14',
      title: 'RSI14: сила недавних ростов и падений',
      keywords: ['Relative Strength Index', 'RS', 'AvgGain', 'AvgLoss', 'перекупленность', 'перепроданность'],
      blocks: [
        { type: 'paragraph', text: 'RSI14 сравнивает сглаженные положительные и отрицательные изменения. Seed AvgGain и AvgLoss — средние первых 14 приростов и модулей падений. Затем применяется Wilder smoothing: Avgₜ = (Avgₜ₋₁ × 13 + Currentₜ) / 14. RS = AvgGain / AvgLoss; RSI = 100 − 100 / (1 + RS).' },
        { type: 'callout', calloutType: 'example', title: 'Числовой пример', body: ['Если суммарный рост первых 14 изменений равен 7, а суммарное падение — 7, AvgGain=AvgLoss=0,5, RS=1 и RSI=50. Если AvgLoss=0, FinanceApp возвращает RSI=100; если AvgGain=0 — RSI=0.'] },
        { type: 'table', columns: ['RSI14', 'Momentum', 'Смысл правила'], rows: [
          ['55–70 включительно', '+10', 'Положительный импульс'],
          ['> 70', '+2 и warning', 'Перекупленность'],
          ['45–<55', '0', 'Нейтральная зона'],
          ['30–<45', '−8', 'Слабый импульс'],
          ['< 30', '−2 и warning', 'Перепроданность'],
        ] },
        { type: 'callout', calloutType: 'warning', body: ['Перекупленность не означает автоматическую продажу, а перепроданность — автоматическую покупку. Сильный тренд может долго удерживать RSI у края диапазона.'] },
      ],
    },
    {
      slug: 'indicator-macd',
      title: 'MACD 12/26/9, Signal и Histogram',
      keywords: ['MACD line', 'Signal line', 'Histogram', '26 закрытий', '34 закрытия'],
      blocks: [
        { type: 'paragraph', text: 'MACD line = EMA12 − EMA26. Signal line — EMA9 последовательности MACD. Histogram = MACD line − Signal line. MACD показывает расхождение быстрой и медленной средних; Histogram — положение MACD относительно Signal.' },
        { type: 'list', items: [
          'MACD line появляется с 26 закрытий.',
          'Signal и Histogram требуют 9 значений MACD и появляются с 34 закрытий. Signal seed — среднее первых 9 MACD, далее EMA9 с k=2/10.',
          'Histogram ≥ 0 даёт Momentum +12; Histogram < 0 даёт −12. Равенство относится к положительной ветке.',
        ] },
        { type: 'callout', calloutType: 'example', title: 'Пример', body: ['MACD=1,10 и Signal=0,80: Histogram=1,10−0,80=0,30, поэтому правило Histogram добавляет +12. Положительный результат не гарантирует рост цены.'] },
      ],
    },
    {
      slug: 'indicator-returns',
      title: 'Доходность 1м, 3м, 6м и 1г',
      keywords: ['Return1Month', 'Return3Months', 'Return6Months', 'Return1Year', '21 63 126 252'],
      blocks: [
        { type: 'paragraph', text: 'Формула: Return(N) = (Pпоследняя / P N торговых дней назад − 1) × 100. При цене 100 двадцать один торговый день назад и 108 сейчас: (108/100−1)×100=8%. Результат хранится в процентных пунктах: 8 означает 8%, а не 800%.' },
        { type: 'table', columns: ['Период', 'Lookback', 'Закрытий'], rows: [
          ['1 месяц', '21 торговый день', '22'], ['3 месяца', '63', '64'], ['6 месяцев', '126', '127'], ['1 год', '252', '253'],
        ] },
        { type: 'paragraph', text: 'Returns использует доступные окна с весами выбранного горизонта; взвешенный результат ограничивается clamp в −25…+25 и прибавляется к базовым 50. Недоступные окна не превращаются в ноль.' },
      ],
    },
    {
      slug: 'indicator-volatility-drawdown',
      title: 'Volatility20/60 и текущая просадка',
      keywords: ['log return', 'population standard deviation', 'sqrt(252)', 'Current Drawdown', 'MaxDrawdown'],
      blocks: [
        { type: 'paragraph', text: 'Дневная логарифмическая доходность rₜ = ln(Pₜ/Pₜ₋₁). Volatility = σpopulation(r) × √252. Population standard deviation делит сумму квадратов отклонений на N. √252 переводит дневной разброс в условный годовой: 0,24 в API показывается как 24%, а не 0,24%.' },
        { type: 'table', columns: ['Volatility60', 'Risk'], rows: [['≤ 20%', '+12'], ['>20%–≤30%', '0'], ['>30%–≤40%', '−10'], ['>40%', '−20']] },
        { type: 'paragraph', text: 'Поле API/UI называется MaxDrawdown, но реализация считает Current Drawdown: (Pпоследняя / MaxCloseокна − 1) × 100 по окну до 252 закрытий. Это НЕ максимальная историческая просадка peak-to-trough. При максимуме 120 и последней цене 90 результат (90/120−1)×100=−25%.' },
        { type: 'table', columns: ['Current drawdown', 'Risk'], rows: [['≥ −10%', '+10'], ['<−10%–≥−20%', '0'], ['< −20%–≥−35%', '−10'], ['< −35%', '−20']] },
        { type: 'callout', calloutType: 'important', body: ['Высокая волатильность означает большой разброс изменений, а не обязательно падение: быстро растущая цена тоже может быть волатильной.'] },
      ],
    },
    {
      slug: 'indicator-atr14',
      title: 'True Range и ATR14: абсолютный дневной диапазон',
      keywords: ['Average True Range', 'TR', 'raw OHLC', 'Wilder smoothing'],
      blocks: [
        { type: 'paragraph', text: 'TR = max(High − Low, |High − Previous Close|, |Low − Previous Close|). Гэпы учитываются через расстояние до предыдущего Close. Seed ATR — среднее первых 14 TR; далее ATRₜ = (ATRₜ₋₁ × 13 + TRₜ) / 14.' },
        { type: 'callout', calloutType: 'example', title: 'Пример', body: ['High=105, Low=99, Previous Close=100: TR=max(6,5,1)=6. ATR имеет абсолютные единицы цены. Для сравнения инструментов Risk использует ATR14/latest price: ATR=2 и цена=100 дают 2%.'] },
        { type: 'table', columns: ['ATR14 / latest price', 'Risk'], rows: [['≤ 2%', '+6'], ['>2%–≤5%', '0'], ['>5%', '−12']] },
        { type: 'callout', calloutType: 'warning', body: ['ATR использует не скорректированные High, Low и Previous Close, тогда как остальные close-based показатели предпочитают AdjustedClose. Из-за отсутствия adjusted OHLC сплит может временно исказить ATR.'] },
      ],
    },
    {
      slug: 'indicator-scoring',
      title: 'Как показатели превращаются в Score',
      keywords: ['Trend', 'Momentum', 'Returns', 'Risk', 'normalized weights', 'renormalization'],
      blocks: [
        { type: 'paragraph', text: 'Каждый компонент стартует с 50, получает описанные бонусы и штрафы и ограничивается диапазоном 0…100. Итог: Score = Σ(ComponentScoreᵢ × NormalizedWeightᵢ). Если компонент недоступен, он исключается, а веса доступных компонентов ренормализуются до суммы 1.' },
        { type: 'callout', calloutType: 'example', title: 'End-to-end пример для горизонта 3 месяца', body: [
          'Предположим: Trend=83, Momentum=80, Returns=58, Risk=50. Веса 0,35 / 0,35 / 0,20 / 0,10.',
          'Score = 83×0,35 + 80×0,35 + 58×0,20 + 50×0,10 = 73,65.',
          'Если Momentum=null, остаётся сумма весов 0,65. Новый Score = (83×0,35 + 58×0,20 + 50×0,10) / 0,65 = 70,23.',
        ] },
        { type: 'paragraph', text: 'Score и Confidence отвечают на разные вопросы. Score описывает направление правил, Confidence — достаточность и качество данных. Высокий Score при низком Confidence не становится надёжным прогнозом.' },
      ],
    },
    {
      slug: 'indicator-missing-confidence',
      title: 'Confidence, предупреждения и границы интерпретации',
      keywords: ['null', 'stale', 'coverage', 'warning codes', 'disclaimer'],
      blocks: [
        { type: 'list', items: [
          'Confidence складывается из покрытия истории (45%), свежести (20%), AdjustedClose coverage (15%) и доступности компонентов (20%).',
          'Среди предупреждений возможны HISTORY_STALE, HISTORY_INSUFFICIENT, ADJUSTED_CLOSE_FALLBACK и CONSTANT_PRICE_SERIES.',
          'Постоянный ценовой ряд математически даёт volatility=0, но предупреждение сообщает, что это может быть признаком плохих данных.',
          'Технические показатели используют исторические цены и не учитывают автоматически новости, ликвидность, отчётность или индивидуальный риск инвестора.',
        ] },
        { type: 'callout', calloutType: 'important', title: 'Не инвестиционная рекомендация', body: ['Score — детерминированная эвристика, а не справедливая стоимость, прогноз, гарантия результата или персональная инвестиционная рекомендация. Проверяйте исходные данные и принимайте решение в контексте компании и рынка.'] },
      ],
    },
  ],
  related: [
    { articleSlug: 'analytical-signal', sectionSlug: 'signal-components-weights', label: 'Как показатели входят в итоговый сигнал' },
    { articleSlug: 'data-quality-and-freshness', label: 'Качество и свежесть входных данных' },
    { articleSlug: 'technical-indicators', label: 'Краткий обзор технических показателей' },
  ],
};
