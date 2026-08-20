import type { HelpArticle, HelpCategory } from './models';

export const HELP_CATEGORIES: HelpCategory[] = [
  {
    slug: 'quick-start',
    title: 'Быстрый старт',
    description: 'Первые шаги в интерфейсе, портфели, акции и безопасный старт.',
    order: 1,
  },
  {
    slug: 'analytics',
    title: 'Аналитика и показатели',
    description: 'Аналитический сигнал, технические и фундаментальные показатели.',
    order: 2,
  },
  {
    slug: 'data-quality',
    title: 'Качество данных',
    description: 'Свежесть, ограничения данных и безопасное обновление.',
    order: 3,
  },
  {
    slug: 'stocks-and-indices',
    title: 'Акции и индексы',
    description: 'Каталог, отслеживание бумаг и работа с мировыми индексами.',
    order: 4,
  },
  {
    slug: 'portfolios',
    title: 'Портфели и операции',
    description: 'Как вести портфель, позиции и транзакции в текущем UI.',
    order: 5,
  },
  {
    slug: 'faq',
    title: 'FAQ и устранение проблем',
    description: 'Короткие ответы на частые вопросы.',
    order: 6,
  },
];

export const HELP_ARTICLES: HelpArticle[] = [
  {
    slug: 'quick-start',
    categorySlug: 'quick-start',
    title: 'Быстрый старт в FinanceApp',
    summary: 'Как войти в приложение, создать портфель, добавить акции и начать анализ без лишнего риска.',
    keywords: ['вход', 'навигация', 'портфель', 'позиции', 'транзакции', 'отслеживаемые акции', 'каталог', 'EUR'],
    order: 1,
    sections: [
      {
        slug: 'quick-start-auth-navigation',
        title: 'Вход и базовая навигация',
        keywords: ['login', 'sidebar', 'меню'],
        blocks: [
          { type: 'paragraph', text: 'После входа открывается защищённая часть приложения. Основная навигация находится в левом сайдбаре: «Главная», «Портфели», «Акции» и «Справочники».' },
          { type: 'list', items: [
            'На мобильных устройствах навигация открывается кнопкой «Открыть навигацию».',
            'Пункт «Справочники» содержит «Секторы и отрасли», «Финансовые показатели» и эту страницу справки.',
            'Навигация работает внутри SPA, поэтому переходы не требуют полной перезагрузки страницы.',
          ] },
        ],
      },
      {
        slug: 'quick-start-portfolios',
        title: 'Создание и выбор портфеля',
        keywords: ['portfolio', 'create', 'select'],
        blocks: [
          { type: 'paragraph', text: 'В разделе «Портфели» создайте портфель и откройте его. Для активного портфеля доступны подразделы «Позиции» и «Транзакции».' },
          { type: 'paragraph', text: 'Позиция отражает количество и среднюю цену покупки бумаги в выбранном портфеле. Транзакции используются для фиксации операций.' },
          {
            type: 'callout',
            calloutType: 'example',
            title: 'Безопасный сценарий первого дня',
            body: [
              'Создайте тестовый портфель с понятным названием.',
              'Добавьте 1–2 бумаги небольшими объёмами.',
              'Проверьте карточку позиции и таблицу транзакций.',
              'Сравните данные по бумаге на горизонтах 3 месяца и 1 год в блоке аналитического сигнала.',
            ],
          },
        ],
      },
      {
        slug: 'quick-start-stocks-catalog',
        title: 'Отслеживаемые акции и каталог',
        keywords: ['stocks', 'catalog', 'tracking'],
        blocks: [
          { type: 'paragraph', text: 'В «Акции» есть два потока: «Отслеживаемые акции» (ваш рабочий список) и «Список акций» (общий каталог).' },
          { type: 'list', items: [
            'Каталог нужен для поиска и добавления бумаг в отслеживание.',
            'Отслеживаемые акции — основной экран с быстрыми действиями, графиком и аналитикой.',
            'Открытие расширенной строки акции показывает график, фундаментальные данные и «Аналитический сигнал».',
          ] },
        ],
      },
      {
        slug: 'quick-start-fundamentals-and-currency',
        title: 'Где смотреть фундаментальные данные, сигнал и валюту EUR',
        keywords: ['fundamentals', 'technical-analysis', 'EUR', 'курс'],
        blocks: [
          { type: 'paragraph', text: 'Фундаментальные показатели открываются в отдельной панели/окне по акции. «Аналитический сигнал» находится в расширенной области графика акции.' },
          { type: 'paragraph', text: 'Отображение в EUR зависит от наличия актуальных курсов. Если курс недоступен, приложение показывает предупреждение и сохраняет исходные значения без недостоверной конвертации.' },
          {
            type: 'callout',
            calloutType: 'important',
            body: [
              'Для новых пользователей безопасно начинать с наблюдения: добавьте бумаги в отслеживание, проверьте свежесть данных и только затем анализируйте горизонты.',
            ],
          },
        ],
      },
    ],
    related: [
      { articleSlug: 'analytical-signal', label: 'Подробно об аналитическом сигнале' },
      { articleSlug: 'portfolios-and-operations', label: 'Портфели и операции' },
    ],
  },
  {
    slug: 'analytical-signal',
    categorySlug: 'analytics',
    title: 'Аналитический сигнал: как читать',
    summary: 'Подробная расшифровка Score, Confidence, горизонтов, весов компонентов, ограничений и предупреждений.',
    keywords: ['Score', 'Confidence', '3 месяца', '6 месяцев', '1 год', '2 года', 'веса', 'ренормализация', 'disclaimer'],
    order: 2,
    sections: [
      {
        slug: 'signal-location',
        title: 'Где находится блок «Аналитический сигнал»',
        keywords: ['где находится', 'chart', 'expanded'],
        blocks: [
          { type: 'paragraph', text: 'Блок находится в расширенной области выбранной акции рядом с графиком цены. Это НЕ вкладка модального окна фундаментальных показателей.' },
          { type: 'list', items: [
            'Откройте раздел «Акции».',
            'Разверните нужную бумагу.',
            'Найдите заголовок «Аналитический сигнал» и переключатели горизонта.',
          ] },
        ],
      },
      {
        slug: 'signal-horizons-and-thresholds',
        title: 'Горизонты, Signal и пороги Score',
        keywords: ['пороги', 'StrongBullish', 'Neutral', 'StrongBearish'],
        blocks: [
          { type: 'paragraph', text: 'Сервис считает сигнал сразу по четырём горизонтам: 3 месяца, 6 месяцев, 1 год, 2 года. Один и тот же тикер может давать разные результаты на разных горизонтах.' },
          {
            type: 'table',
            columns: ['Диапазон Score', 'Код сигнала', 'Текст в UI'],
            rows: [
              ['80–100', 'StrongBullish', 'Сильный бычий'],
              ['65–<80', 'ModeratelyBullish', 'Умеренно бычий'],
              ['45–<65', 'Neutral', 'Нейтральный'],
              ['30–<45', 'ModeratelyBearish', 'Умеренно медвежий'],
              ['0–<30', 'StrongBearish', 'Сильный медвежий'],
            ],
          },
          { type: 'paragraph', text: 'Score — агрегированная оценка (0..100). Confidence — степень уверенности модели в текущем расчёте (0..1, в UI — проценты). Высокий Score не гарантирует высокий Confidence.' },
        ],
      },
      {
        slug: 'signal-components-weights',
        title: 'Компоненты и точные веса по горизонтам',
        keywords: ['Trend', 'Momentum', 'Returns', 'Risk', 'Fundamentals', 'weights'],
        blocks: [
          { type: 'paragraph', text: 'Компоненты: Trend, Momentum, Returns, Risk, Fundamentals. Вес зависит от горизонта.' },
          {
            type: 'table',
            columns: ['Горизонт', 'Trend', 'Momentum', 'Returns', 'Risk', 'Fundamentals'],
            rows: [
              ['3 месяца', '35%', '35%', '20%', '10%', '0%'],
              ['6 месяцев', '35%', '25%', '20%', '15%', '5%'],
              ['1 год', '30%', '15%', '20%', '15%', '20%'],
              ['2 года', '15%', '5%', '15%', '20%', '45%'],
            ],
          },
          { type: 'paragraph', text: 'Если компонент недоступен, он не приравнивается к нулю. Сервис ренормализует веса по доступным компонентам и добавляет предупреждение о неполной компонентной базе.' },
          {
            type: 'callout',
            calloutType: 'warning',
            title: 'Почему это важно',
            body: [
              '«Недостаточно данных» у компонента означает отсутствие входных данных, а не «очень плохо».',
              'Итоговый Score в таком случае сравним только с оговорками по confidence и warning-факторам.',
            ],
          },
        ],
      },
      {
        slug: 'signal-factors-confidence-stale',
        title: 'Факторы, confidence и stale-состояния',
        keywords: ['positive factors', 'risk factors', 'warnings', 'stale'],
        blocks: [
          { type: 'paragraph', text: 'Положительные факторы и факторы риска показывают, что именно подтолкнуло оценку вверх или вниз. Блок «Предупреждения» сообщает о качестве данных и ограничениях расчёта.' },
          { type: 'list', items: [
            'Confidence снижается при недостаточной длине истории, stale-данных, пропусках компонентов и проблемах фундаментальных данных на длинных горизонтах.',
            'Если история/фундаментал устарели, в UI отображаются предупреждения о потенциально устаревшем сигнале.',
            'Endpoint и чтение UI не запускают автоматически обновление провайдера только ради аналитики.',
          ] },
          {
            type: 'callout',
            calloutType: 'important',
            body: ['Аналитическая информация не является персональной инвестиционной рекомендацией, сигналом Buy/Sell и не гарантирует будущий результат.'],
          },
        ],
      },
      {
        slug: 'signal-worked-examples',
        title: 'Разбор примеров',
        keywords: ['пример', 'высокий score низкий confidence', 'разные горизонты'],
        blocks: [
          {
            type: 'callout',
            calloutType: 'example',
            title: 'Пример 1: высокий Score, низкий Confidence',
            body: [
              'На 6 месяцев Score = 78, но Confidence = 42%.',
              'Причина: часть компонент недоступна, а история неполная/устаревшая.',
              'Вывод: направление кажется позитивным, но устойчивость оценки низкая — нужна дополнительная проверка данных.',
            ],
          },
          {
            type: 'callout',
            calloutType: 'example',
            title: 'Пример 2: 3 месяца и 2 года противоречат',
            body: [
              '3 месяца: ModeratelyBullish (краткосрочный тренд и momentum сильные).',
              '2 года: ModeratelyBearish/StrongBearish (вес fundamentals и риск выше, длинный контекст слабее).',
              'Вывод: короткое восстановление не отменяет долгосрочные фундаментальные ограничения.',
            ],
          },
        ],
      },
    ],
    related: [
      { articleSlug: 'technical-indicators', label: 'Технические показатели: детали' },
      { articleSlug: 'data-quality-and-freshness', label: 'Качество и свежесть данных' },
    ],
  },
  {
    slug: 'technical-indicators',
    categorySlug: 'analytics',
    title: 'Технические показатели в FinanceApp',
    summary: 'Подробная методология расчёта SMA/EMA/RSI/MACD, доходностей, волатильности, drawdown и ATR в FinanceApp с формулами, требованиями к данным, единицами и влиянием на сигнал.',
    keywords: ['SMA20', 'SMA50', 'SMA200', 'EMA12', 'EMA26', 'RSI14', 'MACD', 'ATR14', 'AdjustedClose', 'Close', 'volatility', 'drawdown', 'True Range', 'Wilder'],
    order: 3,
    sections: [
      {
        slug: 'indicators-methodology',
        title: 'Методология backend: какие данные берутся и что происходит до формул',
        blocks: [
          { type: 'paragraph', text: 'Источник — уже сохранённые daily-свечи в БД (`StockHistoricalPrices`, интервал `1d`), максимум 800 последних наблюдений. Чтение technical-analysis не вызывает провайдеров и не запускает авто-refresh.' },
          { type: 'table', columns: ['Этап', 'Фактическое поведение FinanceApp'], rows: [
            ['Сортировка', 'Свечи сортируются по времени (старые → новые).'],
            ['Дубликаты', 'Свечи с одинаковым timestamp схлопываются: остаётся последняя (last value kept).'],
            ['Проверка валидности', 'Если `AdjustedClose > 0`, берётся он; иначе fallback на `Close > 0`; иначе свеча исключается.'],
            ['Coverage', '`AdjustedCloseCoverage = (число свечей с валидным AdjustedClose) / (число нормализованных свечей)`.'],
            ['ATR', 'ATR14 всегда считает True Range по raw OHLC (Open/High/Low/Close), потому что adjusted OHLC не хранится.'],
            ['Stale', 'Если последняя свеча старше 3 дней относительно UTC now — `HISTORY_STALE`.'],
            ['Constant series', 'Если после нормализации все effective close одинаковы — warning `CONSTANT_PRICE_SERIES`.'],
            ['Неполная история', 'Часть метрик возвращается `null`, компоненты сигнала ренормализуются по доступным весам.'],
          ] },
          { type: 'callout', calloutType: 'important', body: ['Сервис аналитики только читает БД (`AsNoTracking`) и не пишет новые свечи/метрики при открытии экрана.'] },
        ],
      },
      {
        slug: 'indicators-sma',
        title: 'SMA20 / SMA50 / SMA200 (Simple Moving Average)',
        blocks: [
          { type: 'paragraph', text: 'Алиасы: Simple Moving Average, простая скользящая средняя.' },
          { type: 'list', items: [
            'Что измеряет: усреднённый уровень цены за окно 20/50/200 торговых дней.',
            'Формула FinanceApp: `SMA(N) = (Σ Close_i за последние N точек) / N`.',
            'Требуемые данные: минимум N валидных close в окне (предпочтительно AdjustedClose, иначе fallback на Close).',
            'Использование в сигнале (Trend): Price>SMA50: +10; Price<SMA50: -10. Price>SMA200: +10; Price<SMA200: -10. SMA50>SMA200: +15, иначе -15. SMA20>SMA50: +8, иначе -8.',
            'Ограничения: при недостатке истории SMA200 = null (обычно это данные, а не баг UI).',
          ] },
          { type: 'callout', calloutType: 'info', body: ['SMA — запаздывающий индикатор: резкие развороты часто отражает позднее цены.'] },
        ],
      },
      {
        slug: 'indicators-ema',
        title: 'EMA12 / EMA26 (Exponential Moving Average)',
        blocks: [
          { type: 'paragraph', text: 'Алиасы: Exponential Moving Average, экспоненциальная скользящая средняя.' },
          { type: 'list', items: [
            'Что измеряет: сглаженный тренд с большим весом последних цен.',
            'Формула FinanceApp: `EMA_t = Price_t × k + EMA_(t-1) × (1-k)`, где `k = 2/(period+1)`.',
            'Инициализация (seed): EMA стартует с SMA первых N значений (N=12 или N=26).',
            'Требуемые данные: минимум N положительных close; неположительное значение делает EMA недоступной.',
            'Использование в сигнале (Momentum): EMA12>EMA26 даёт +8, иначе -8.',
            'Ограничения: EMA чувствительна к последним барам и не эквивалентна intraday EMA из внешних терминалов.',
          ] },
        ],
      },
      {
        slug: 'indicators-rsi14',
        title: 'RSI14 (Relative Strength Index)',
        blocks: [
          { type: 'paragraph', text: 'Алиасы: Relative Strength Index, индекс относительной силы.' },
          { type: 'list', items: [
            'Что измеряет: баланс средних приростов и падений за 14 шагов.',
            'Формула FinanceApp: seed = средний gain/loss за первые 14 изменений; затем Wilder smoothing: `avgGain=(prevAvgGain×13+gain)/14`, `avgLoss=(prevAvgLoss×13+loss)/14`; `RS=avgGain/avgLoss`; `RSI=100-100/(1+RS)`.',
            'Требуемые данные: минимум 15 положительных close (14 изменений).',
            'Edge cases: при `avgLoss=0` результат = 100; при неположительной цене RSI = null.',
            'Использование в сигнале (Momentum): RSI 55..70 = +10; >70 = +2 и warning RSI_OVERBOUGHT; 45..55 = 0; 30..45 = -8; <30 = -2 и warning RSI_OVERSOLD.',
            'Ограничения: RSI не предсказывает разворот сам по себе; «overbought/oversold» не равны сигналам мгновенной продажи/покупки.',
          ] },
          { type: 'callout', calloutType: 'example', title: 'Пример RSI', body: [
            'Если за 14 шагов средний прирост = 1.2, средний спад = 0.8, то RS = 1.5.',
            'RSI = 100 - 100/(1+1.5) = 60: это конструктивная бычья зона (в FinanceApp даёт вклад +10 в Momentum).',
          ] },
        ],
      },
      {
        slug: 'indicators-macd',
        title: 'MACD 12/26/9, signal и histogram',
        blocks: [
          { type: 'paragraph', text: 'Алиасы: Moving Average Convergence Divergence, сигнальная линия, гистограмма MACD.' },
          { type: 'list', items: [
            'Что измеряет: расхождение быстрых/медленных EMA и изменение импульса.',
            'Формула FinanceApp: `MACD line = EMA12 - EMA26`; `Signal = EMA9(MACD line)`; `Histogram = MACD line - Signal`.',
            'Инициализация: EMA12 и EMA26 стартуют с SMA первых 12/26 close; Signal EMA9 стартует с SMA первых 9 значений MACD-линии.',
            'Требуемые данные: от 26 close доступна только MACD line; от 34 close доступны Signal и Histogram (26 + 9 - 1).',
            'Поведение partial-result: при 26..33 наблюдениях `macdSignal` и `macdHistogram` возвращаются null.',
            'Использование в сигнале (Momentum): Histogram >= 0 даёт +12; Histogram < 0 даёт -12.',
            'Ограничения: сравнение с внешними сервисами может отличаться из-за инициализации EMA и базы цен (AdjustedClose/Close).',
          ] },
          { type: 'callout', calloutType: 'example', title: 'Пример MACD/Histogram', body: [
            'Если EMA12 = 105, EMA26 = 100, то MACD line = 5.',
            'Если Signal = 4.2, то Histogram = 0.8 (положительная, в FinanceApp это бычий вклад Momentum).',
          ] },
        ],
      },
      {
        slug: 'indicators-returns',
        title: 'Доходности (Returns): backend-периоды и периоды, видимые в UI',
        blocks: [
          { type: 'paragraph', text: 'Алиасы: Return %, доходность за период, trading-day return.' },
          { type: 'table', columns: ['Период', 'Trading days lookback', 'Статус'], rows: [
            ['1 неделя', '5', 'Считается в Core (`Return1Week`), в текущем DTO/панели не показывается.'],
            ['1 месяц', '21', 'Показывается как `return1Month`.'],
            ['3 месяца', '63', 'Показывается как `return3Months`.'],
            ['6 месяцев', '126', 'Показывается как `return6Months`.'],
            ['12 месяцев / 1 год', '252', 'Показывается как `return1Year` / `Return1Year`.'],
          ] },
          { type: 'list', items: [
            'Формула FinanceApp: `Return% = (latest / price_N_days_ago - 1) × 100`.',
            'Единицы: процентные пункты (%) относительно цены N торговых дней назад.',
            'Требуемые данные: минимум N+1 положительных close.',
            'Использование в сигнале (Returns component): weighted return ограничивается диапазоном [-25; +25], затем score = 50 + boundedReturn; знак return формирует положительный/отрицательный фактор.',
            'Почему может отличаться от «видимого изменения цены»: окно строго по торговым дням, а не календарю; также может использоваться AdjustedClose вместо raw Close.',
          ] },
          { type: 'callout', calloutType: 'example', title: 'Пример Return%', body: [
            'Цена 21 торговый день назад = 100, последняя цена = 108.',
            'Return1Month = (108/100 - 1) × 100 = 8%.',
          ] },
        ],
      },
      {
        slug: 'indicators-volatility20-60',
        title: 'Volatility20 / Volatility60 (annualized)',
        blocks: [
          { type: 'paragraph', text: 'Алиасы: historical volatility, σ annualized, волатильность (годовая).' },
          { type: 'list', items: [
            'Что измеряет: разброс лог-доходностей за окно 20 или 60 торговых дней.',
            'Формула FinanceApp: `r_t = ln(P_t / P_(t-1))`; `variance = average((r_t - mean(r))²)` (population, делитель N); `vol = sqrt(variance) × sqrt(252)`.',
            'Требуемые данные: минимум 21/61 положительных close.',
            'Единицы: доля в диапазоне 0..∞ (в UI форматируется как % в год).',
            'Constant-series: если все лог-доходности равны 0, vol = 0 (это корректный результат).',
            'Использование в сигнале (Risk, по Volatility60): <=20% даёт +12; >30% даёт -10; >40% даёт -20.',
          ] },
          { type: 'callout', calloutType: 'example', title: 'Пример Volatility', body: [
            'Если 60-дневная annualized volatility = 0.31, в UI это 31.0%.',
            'Для Risk это зона выше умеренной, поэтому применяется отрицательный вклад.',
          ] },
        ],
      },
      {
        slug: 'indicators-drawdown',
        title: 'Drawdown: что именно считает FinanceApp',
        blocks: [
          { type: 'paragraph', text: 'Алиасы: drawdown, текущая просадка, current drawdown.' },
          { type: 'list', items: [
            'Что измеряет: текущую просадку последней цены от максимума за окно до 252 последних наблюдений.',
            'Формула FinanceApp: `Drawdown% = (latest / maxCloseInWindow - 1) × 100`.',
            'Окно и единицы: максимум 252 торговых дня, результат в % и обычно <= 0.',
            'Семантический нюанс: поле DTO/UI называется `MaxDrawdown`, но реализация вычисляет именно текущую drawdown относительно максимума окна, а не «исторически максимальную просадку внутри окна».',
            'Использование в сигнале (Risk): drawdown >= -10% даёт +10; < -20% даёт -10; < -35% даёт -20.',
            'Ограничения: метрика зависит от выбранного окна и не эквивалентна классическому peak-to-trough maximum drawdown за произвольный интервал.',
          ] },
          { type: 'callout', calloutType: 'example', title: 'Пример Drawdown', body: [
            'Если максимум в окне = 100, а последняя цена = 80, то drawdown = (80/100 - 1) × 100 = -20%.',
            'Такое значение попадёт в граничную зону повышенного риска.',
          ] },
        ],
      },
      {
        slug: 'indicators-atr14',
        title: 'ATR14 (Average True Range)',
        blocks: [
          { type: 'paragraph', text: 'Алиасы: Average True Range, True Range, ATR.' },
          { type: 'list', items: [
            'Что измеряет: типичную абсолютную «ширину» дневного движения цены.',
            'True Range: `TR = max(High-Low, |High-PrevClose|, |Low-PrevClose|)`.',
            'Формула FinanceApp: seed ATR = среднее первых 14 TR; далее Wilder smoothing: `ATR_t = (ATR_(t-1)×13 + TR_t)/14`.',
            'Требуемые данные: минимум 15 дневных свечей (14 TR).',
            'База цен: всегда raw OHLC (Open/High/Low/Close), adjusted OHLC не используется.',
            'Единицы: абсолютная цена инструмента (не %). Для Risk нормализуется: `atrPct = ATR14 / LatestPrice × 100`.',
            'Порог Risk: atrPct <= 2% даёт +6; atrPct > 5% даёт -12.',
            'Ограничения: при сплитах raw OHLC может давать скачки ATR до выравнивания истории.',
          ] },
          { type: 'callout', calloutType: 'example', title: 'Пример ATR и нормализации', body: [
            'ATR14 = 2.4 при LatestPrice = 120 ⇒ atrPct = 2.0%.',
            'В таком случае ATR считается сдержанным риском и может дать положительный вклад в Risk.',
          ] },
        ],
      },
      {
        slug: 'indicators-confidence-limitations',
        title: 'Missing/null данные, confidence, сопоставимость с внешними графиками и дисклеймер',
        blocks: [
          { type: 'paragraph', text: 'Когда отдельные метрики недоступны (`null`), компонент может быть исключён из итогового score; оставшиеся веса ренормализуются по доступным компонентам (`COMPONENTS_MISSING`).' },
          { type: 'paragraph', text: 'Confidence учитывает coverage истории, свежесть последней свечи, покрытие AdjustedClose, доступность компонентов и (для длинных горизонтов) свежесть/достаточность fundamentals.' },
          { type: 'list', items: [
            'Влияние на confidence: неполная история даёт `HISTORY_INSUFFICIENT`; неполный AdjustedClose даёт `ADJUSTED_CLOSE_FALLBACK`; stale и проблемы fundamentals дополнительно снижают уверенность.',
            'Сравнение с внешними сервисами: возможны расхождения из-за trading-day окон, seed-правил EMA/RSI/MACD, базы цен (AdjustedClose/Close) и raw OHLC для ATR.',
            'Обычные ошибки интерпретации: «высокий Score = гарантированный рост» и «overbought/oversold = немедленный разворот». Обе трактовки неверны.',
          ] },
          { type: 'callout', calloutType: 'important', body: ['Аналитический сигнал и показатели в приложении — это расчётная оценка по историческим данным, а не персональная инвестиционная рекомендация.'] },
        ],
      },
    ],
    related: [
      { articleSlug: 'analytical-signal', sectionSlug: 'signal-components-weights', label: 'Как эти показатели влияют на сигнал и его веса' },
      { articleSlug: 'data-quality-and-freshness', label: 'Почему метрики бывают недоступны' },
    ],
  },
  {
    slug: 'fundamentals',
    categorySlug: 'analytics',
    title: 'Фундаментальные показатели и экраны',
    summary: 'Какие поля доступны в фундаментальных данных, как читать вкладки и что означает свежесть/пропуски.',
    keywords: ['Market Cap', 'Enterprise Value', 'TTM', 'P/E', 'P/B', 'Dividend Yield', 'As of'],
    order: 4,
    sections: [
      {
        slug: 'fundamentals-where-and-fields',
        title: 'Где смотреть и какие поля есть',
        blocks: [
          { type: 'paragraph', text: 'Фундаментальные данные открываются в панели «фундаментальные показатели» для конкретной акции.' },
          {
            type: 'table',
            columns: ['Группа', 'Поля в текущем UI'],
            rows: [
              ['Размер/структура', 'Market Cap, Enterprise Value, Total Debt, Cash'],
              ['TTM', 'TTM Revenue, TTM Net Income, TTM EBITDA, TTM FCF'],
              ['Мультипликаторы', 'P/E, Forward P/E, P/B, Dividend Yield'],
              ['Баланс', 'Total Assets, Total Liabilities'],
            ],
          },
          { type: 'paragraph', text: 'Также доступны вкладки с периодами: «Годовые», «Квартальные», «Отчёты». Не каждый провайдер заполняет все поля для каждой бумаги.' },
        ],
      },
      {
        slug: 'fundamentals-source-freshness-refresh',
        title: 'Источник, As-of, свежесть и обновление',
        blocks: [
          { type: 'list', items: [
            'Верхняя часть панели показывает источник и символ провайдера.',
            'Тег Fresh/Stale отражает состояние снимка данных.',
            'As-of показывает дату актуальности отчётных данных, если доступна.',
            'Кнопка «Обновить» выполняет ручной refresh и может вернуть stale-снимок с предупреждением.',
          ] },
          { type: 'callout', calloutType: 'warning', body: ['Один snapshot не формирует тренд сам по себе — для тренда используйте историю периодов и сравнение нескольких дат.'] },
        ],
      },
      {
        slug: 'fundamentals-limitations-and-signal-impact',
        title: 'Ограничения и влияние на длинные горизонты сигнала',
        blocks: [
          { type: 'paragraph', text: 'При отсутствии/устаревании fundamentals компонент Fundamentals может быть недоступен или понижен по confidence, особенно на горизонтах 1 год и 2 года.' },
          { type: 'paragraph', text: 'Если исторических фундаментальных периодов недостаточно, система явно добавляет предупреждения и уменьшает надёжность длинного горизонта.' },
        ],
      },
    ],
    related: [
      { articleSlug: 'analytical-signal', sectionSlug: 'signal-components-weights', label: 'Веса компонентов сигнала' },
    ],
  },
  {
    slug: 'data-quality-and-freshness',
    categorySlug: 'data-quality',
    title: 'Качество и свежесть данных',
    summary: 'Как понимать stale/fresh, почему confidence падает и что делать пользователю при проблемах данных.',
    keywords: ['stale', 'fresh', 'As-of', 'timestamp', 'insufficient history', 'SMA200 unavailable', 'AdjustedClose coverage'],
    order: 5,
    sections: [
      {
        slug: 'data-quality-labels-and-timestamps',
        title: 'Source/As-of, quote timestamp и даты свечей',
        blocks: [
          { type: 'paragraph', text: 'Подписи Source и As-of описывают происхождение и датировку данных. Для цены и графика важно отличать timestamp последней котировки и дату последней исторической свечи.' },
          { type: 'list', items: [
            'Свежесть котировки и свежесть дневной истории — разные вещи.',
            'As-of может относиться к фундаментальному снимку, а не к intraday-цене.',
            'Для аналитического сигнала stale определяется относительно доступной дневной истории.',
          ] },
        ],
      },
      {
        slug: 'data-quality-common-warnings',
        title: 'Типичные предупреждения и их смысл',
        blocks: [
          { type: 'list', items: [
            'Недостаточно истории: часть метрик/компонент недоступна.',
            'SMA200 unavailable: мало дневных свечей для окна 200.',
            'AdjustedClose coverage incomplete: часть свечей посчитана по Close fallback.',
            'Duplicate/invalid/constant series warnings: входной ряд содержит аномалии качества.',
            'Missing/stale fundamentals: длинные горизонты теряют уверенность.',
          ] },
          { type: 'paragraph', text: 'Чем больше таких warning-факторов, тем ниже confidence даже при визуально неплохом Score.' },
        ],
      },
      {
        slug: 'data-quality-safe-user-actions',
        title: 'Что безопасно делать пользователю при stale-данных',
        blocks: [
          { type: 'list', items: [
            'Запустить ручное обновление там, где UI явно предоставляет кнопку refresh.',
            'Сравнить несколько горизонтов вместо решения по одному числу.',
            'Проверить предупреждения в блоке сигнала и в фундаментальной панели.',
            'При критичных решениях перепроверить данные внешним источником.',
          ] },
          { type: 'callout', calloutType: 'important', body: ['Обычное открытие экрана/endpoint чтения не запускает автоматическое обновление провайдера для technical-analysis.'] },
        ],
      },
    ],
    related: [
      { articleSlug: 'technical-indicators', label: 'Технические показатели и требования к истории' },
      { articleSlug: 'faq', label: 'FAQ по устаревшим данным' },
    ],
  },
  {
    slug: 'stocks-and-indices',
    categorySlug: 'stocks-and-indices',
    title: 'Акции и мировые индексы',
    summary: 'Как работать с отслеживаемыми акциями, каталогом, индексами и их составляющими.',
    keywords: ['tracked stocks', 'catalog', 'market indices', 'constituents', 'history refresh'],
    order: 6,
    sections: [
      {
        slug: 'stocks-tracked-vs-catalog',
        title: 'Отслеживаемые акции и каталог',
        blocks: [
          { type: 'paragraph', text: 'Отслеживаемые акции — ваш рабочий набор. Каталог — более широкий список бумаг, из которого обычно добавляют бумаги в отслеживание.' },
          { type: 'list', items: [
            'Добавление/удаление отслеживания меняет ваш рабочий список, но не удаляет саму бумагу из общего каталога.',
            'Для разных листингов важно ориентироваться на тикер/биржу/идентичность инструмента.',
            'Если истории мало, часть показателей и периодных сравнений может быть недоступна.',
          ] },
        ],
      },
      {
        slug: 'stocks-chart-ranges-refresh',
        title: 'Диапазоны графика и ручное обновление истории',
        blocks: [
          { type: 'paragraph', text: 'На графике доступны диапазоны истории; периодные проценты в таблицах/карточках зависят от наличия данных за соответствующее окно.' },
          { type: 'paragraph', text: 'Ручное обновление истории (если доступно в текущем экране) безопасно использовать, когда значения явно устарели.' },
        ],
      },
      {
        slug: 'indices-navigation-and-constituents',
        title: 'Мировые индексы: навигация и constituents',
        blocks: [
          { type: 'paragraph', text: 'В разделе «Акции» есть вложенное меню «Мировые индексы»: экран управления и страницы конкретных индексов.' },
          { type: 'list', items: [
            'На страницах индексов доступны графики и список constituents.',
            'Состав индекса не равен списку ваших отслеживаемых бумаг.',
            'Если у constituent недостаточно истории, показатели/доходности могут быть пустыми.',
          ] },
        ],
      },
    ],
    related: [
      { articleSlug: 'quick-start', label: 'Первые шаги с акциями' },
    ],
  },
  {
    slug: 'portfolios-and-operations',
    categorySlug: 'portfolios',
    title: 'Портфели и операции',
    summary: 'Практическая работа с портфелями, позициями, транзакциями и валютными оговорками в текущем продукте.',
    keywords: ['портфель', 'позиция', 'транзакции', 'average purchase price', 'EUR conversion'],
    order: 7,
    sections: [
      {
        slug: 'portfolio-create-select',
        title: 'Создание и выбор портфеля',
        blocks: [
          { type: 'paragraph', text: 'Создайте портфель, выберите его в сайдбаре, затем переходите в «Позиции» и «Транзакции».' },
          { type: 'list', items: [
            'Позиции показывают количество и среднюю цену покупки.',
            'Транзакции отражают историю операций в портфеле.',
            'Редактирование/удаление записей немедленно влияет на расчёты, связанные с портфелем.',
          ] },
        ],
      },
      {
        slug: 'portfolio-operation-safety-workflow',
        title: 'Безопасный рабочий сценарий',
        blocks: [
          { type: 'list', ordered: true, items: [
            'Сначала проверьте, что выбран правильный портфель.',
            'Добавляйте/редактируйте операции небольшими шагами.',
            'После каждого изменения перепроверьте количество и среднюю цену по позиции.',
            'При валютных расхождениях проверьте предупреждения о недостающем EUR-курсе.',
          ] },
          { type: 'paragraph', text: 'В текущем UI не следует предполагать автоматические налоговые/брокерские сверки, если они явно не отображаются.' },
        ],
      },
      {
        slug: 'portfolio-eur-and-missing-rates',
        title: 'EUR-конвертация и предупреждения о курсах',
        blocks: [
          { type: 'paragraph', text: 'Часть экранов показывает значения в EUR при доступных курсах. Если курсов не хватает, приложение предупреждает и избегает некорректной конвертации.' },
          { type: 'paragraph', text: 'При анализе результатов учитывайте валюту источника и дату котировки/отчёта.' },
        ],
      },
    ],
    related: [
      { articleSlug: 'quick-start', sectionSlug: 'quick-start-portfolios', label: 'Быстрый старт по портфелям' },
    ],
  },
  {
    slug: 'faq',
    categorySlug: 'faq',
    title: 'FAQ и устранение проблем',
    summary: 'Короткие ответы на частые вопросы пользователей FinanceApp.',
    keywords: ['FAQ', 'где аналитический сигнал', 'нет SMA200', '401', 'network error', 'hard refresh'],
    order: 8,
    sections: [
      {
        slug: 'faq-common',
        title: 'Частые вопросы',
        blocks: [
          { type: 'qa', question: 'Где находится аналитический сигнал?', answer: 'В расширенной области акции рядом с графиком цены. Не в окне фундаментальных показателей.' },
          { type: 'qa', question: 'Почему его нет в окне фундаментальных показателей?', answer: 'Фундаментальные данные и технический сигнал разделены: fundamentals — отдельная панель, сигнал — часть расширенного блока графика.' },
          { type: 'qa', question: 'Почему Score высокий, а Confidence низкий?', answer: 'Оценка может быть высокой, но при этом уверенность снижается из-за stale-данных, недостающих компонент или неполной истории.' },
          { type: 'qa', question: 'Почему результаты на 3 месяца и 2 года отличаются?', answer: 'Разные горизонты используют разные веса компонент. На 2 года значительно выше вес fundamentals и риска.' },
          { type: 'qa', question: 'Почему компонент показывает «Недостаточно данных»?', answer: 'Для компонента не хватило входных данных. Это не равно нулевому score.' },
          { type: 'qa', question: 'Почему нет SMA200?', answer: 'Недостаточно длины дневной истории для окна 200 свечей.' },
          { type: 'qa', question: 'Почему фундаментальные данные отсутствуют или устарели?', answer: 'Провайдер мог не вернуть полный набор полей или свежий snapshot. Проверьте state и предупреждения в панели fundamentals.' },
          { type: 'qa', question: 'Почему цена/график выглядит устаревшим?', answer: 'Последняя свеча/котировка может отставать от текущего времени. Проверьте метки времени и при необходимости выполните доступный refresh.' },
          { type: 'qa', question: 'Почему доходность отличается от изменения текущей цены?', answer: 'Доходность считается по фиксированным историческим окнам и базе цен (AdjustedClose/Close fallback), а не только по двум последним отображаемым значениям.' },
          { type: 'qa', question: 'Что означает AdjustedClose?', answer: 'Это скорректированная цена закрытия; в FinanceApp close-based метрики предпочитают её, с fallback на Close по каждой свече.' },
          { type: 'qa', question: 'Как обновить данные безопасно?', answer: 'Используйте только явные кнопки обновления в интерфейсе и после обновления перепроверьте warning-метки и confidence.' },
          { type: 'qa', question: 'После обновления приложения видна старая версия. Что делать?', answer: 'Сделайте hard refresh страницы (Ctrl/Cmd+Shift+R) или откройте приложение в приватном окне, чтобы исключить кэш браузера.' },
        ],
      },
      {
        slug: 'faq-auth-and-network-errors',
        title: '401/session expiry и сетевые ошибки',
        blocks: [
          { type: 'paragraph', text: 'При 401 обычно требуется повторная авторизация. Если сессия истекла, войдите заново.' },
          { type: 'paragraph', text: 'При сетевых/серверных ошибках попробуйте повторить запрос позже. Если ошибка стабильна, проверьте подключение и обратитесь к команде поддержки с указанием шага и времени ошибки.' },
        ],
      },
    ],
    related: [
      { articleSlug: 'analytical-signal', sectionSlug: 'signal-location', label: 'Где находится сигнал (подробно)' },
      { articleSlug: 'data-quality-and-freshness', label: 'Раздел про свежесть данных' },
    ],
  },
];
