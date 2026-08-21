import type { HelpArticle } from './models';

export const FUNDAMENTAL_SCORING_METHODOLOGY_ARTICLE: HelpArticle = {
  slug: 'fundamental-scoring-methodology',
  categorySlug: 'analytics',
  title: 'Как FinanceApp считает фундаментальный компонент Score',
  summary: 'Подробное учебное объяснение текущего rules-based алгоритма фундаментальной оценки: какие поля реально используются, какие пороги применяются, как считается Confidence и какие ограничения уже подтверждены в коде.',
  keywords: [
    'фундаментальный анализ простыми словами', 'fundamental score', 'confidence',
    'Net Income TTM', 'Free Cash Flow TTM', 'Debt/EBITDA', 'P/E', 'P/B', 'Dividend Yield',
    'horizon weights', 'renormalization', 'sector', 'industry', 'история периодов', 'эвристика',
  ],
  order: 3.6,
  sections: [
    {
      slug: 'fundamentals-what-it-means',
      title: 'Что такое фундаментальный анализ и чем он отличается от технического',
      blocks: [
        { type: 'paragraph', text: 'Фундаментальный анализ оценивает бизнес компании: прибыль, денежный поток, долг и мультипликаторы. Технический анализ смотрит в первую очередь на поведение цены и волатильности на графике.' },
        { type: 'list', items: [
          'Фундаментальный компонент в FinanceApp — это правила (эвристики) по нескольким финансовым полям, а не оценка справедливой стоимости.',
          'Технические компоненты (Trend/Momentum/Returns/Risk) считают цену и историю свечей, а фундаментальный — финансовый snapshot компании.',
          'Итоговый Score объединяет компоненты по весам горизонта; Confidence показывает надёжность данных и полноту расчёта.',
        ] },
      ],
    },
    {
      slug: 'fundamentals-score-vs-confidence',
      title: 'Score и Confidence: это разные показатели',
      blocks: [
        { type: 'table', columns: ['Показатель', 'Что означает', 'Диапазон'], rows: [
          ['Score', 'Оценка «насколько конструктивна картина» по текущим правилам', '0–100'],
          ['Confidence', 'Насколько расчёту можно доверять с учётом полноты/свежести данных', '0–1 (в UI показывается как %)'],
        ] },
        { type: 'callout', calloutType: 'important', body: [
          'Высокий Score при низком Confidence — это сигнал «правила сработали, но данных мало/они устарели».',
          'Score не является прогнозом, гарантией доходности или командой Buy/Sell.',
        ] },
      ],
    },
    {
      slug: 'fundamentals-data-selection-and-fields',
      title: 'Какие записи и поля берутся из базы (и что реально влияет на счёт)',
      blocks: [
        { type: 'paragraph', text: 'Для фундаментального компонента backend читает только сохранённые данные из БД и не вызывает провайдера при запросе technical-analysis.' },
        { type: 'list', items: [
          'Выбирается последний fundamentals snapshot по stockId с сортировкой по FetchedAtUtc по убыванию (берётся первая запись).',
          'История FinancialPeriods не используется в формулах Score; из неё берутся только count/min/max даты для warning и штрафа Confidence на горизонте 2 года.',
          'Открытие аналитического сигнала (GET /api/stocks/{id}/technical-analysis) — read-only операция без refresh провайдера.',
        ] },
        { type: 'table', columns: ['Поле snapshot', 'Используется сейчас', 'Где именно'], rows: [
          ['FetchedAtUtc', 'Да', 'Проверка устаревания >35 дней, warning FUNDAMENTALS_STALE, штраф Confidence'],
          ['NetIncomeTtm', 'Да', 'Сигнал прибыльности (+8 / −8)'],
          ['FreeCashFlowTtm', 'Да', 'Сигнал денежного потока (+7 / −7)'],
          ['TotalDebt + EbitdaTtm', 'Да', 'Debt/EBITDA: <2, >4, >6'],
          ['PeRatio', 'Да', 'Диапазоны 5–35 и >60'],
          ['PbRatio', 'Да', 'Диапазоны 0,5–8 и >15'],
          ['DividendYield', 'Да', 'Диапазоны 1–6 и >10'],
          ['AsOfDate', 'Нет', 'Загружается, но не участвует в формулах Score/Confidence'],
          ['MarketCap', 'Нет', 'Загружается, но не участвует в формулах Score/Confidence'],
          ['CashAndEquivalents', 'Нет', 'Загружается, но сейчас не вычитается из долга (net debt не считается)'],
        ] },
      ],
    },
    {
      slug: 'fundamentals-net-income-ttm',
      title: 'Метрика 1: Net Income TTM',
      blocks: [
        { type: 'paragraph', text: 'Определение: Net Income TTM — чистая прибыль за последние 12 месяцев. Почему важно: положительная прибыль означает, что компания в сумме за год заработала, а не сожгла капитал.' },
        { type: 'paragraph', text: 'Правило FinanceApp: если NetIncomeTtm > 0, к фундаментальному score добавляется +8; иначе (0 или отрицательное) вычитается −8.' },
        { type: 'callout', calloutType: 'example', title: 'Числовой пример', body: [
          'Стартовый фундаментальный score = 50.',
          'Если NetIncomeTtm = +120 млн USD, условие >0 выполнено: score = 50 + 8 = 58.',
          'Если NetIncomeTtm = 0, применяется ветка «иначе»: score = 50 − 8 = 42.',
        ] },
        { type: 'paragraph', text: 'Единицы: денежная сумма (валюта эмитента/источника). Ограничение: величина прибыли (насколько она большая) не нормализуется — учитывается только знак.' },
      ],
    },
    {
      slug: 'fundamentals-fcf-ttm',
      title: 'Метрика 2: Free Cash Flow TTM',
      blocks: [
        { type: 'paragraph', text: 'Определение: Free Cash Flow TTM — свободный денежный поток за 12 месяцев. Почему важно: FCF показывает, остаются ли у бизнеса реальные деньги после операционных и капитальных затрат.' },
        { type: 'paragraph', text: 'Правило FinanceApp: если FreeCashFlowTtm > 0, +7; иначе (0 или отрицательное) −7.' },
        { type: 'callout', calloutType: 'example', title: 'Числовой пример', body: [
          'После предыдущих правил score = 58.',
          'FCF = −30 млн: условие >0 не выполнено, score = 58 − 7 = 51.',
        ] },
        { type: 'paragraph', text: 'Единицы: денежная сумма. Ограничение: стабильность FCF по периодам и качество кэша против прибыли сейчас не оцениваются.' },
      ],
    },
    {
      slug: 'fundamentals-debt-to-ebitda',
      title: 'Метрика 3: Debt / EBITDA',
      blocks: [
        { type: 'paragraph', text: 'Определение: Debt/EBITDA = TotalDebt / EbitdaTtm. Почему важно: грубая оценка долговой нагрузки относительно операционной прибыли до процентов/налогов/амортизации.' },
        { type: 'paragraph', text: 'Считается только если TotalDebt задан, EbitdaTtm задан и EbitdaTtm > 0.' },
        { type: 'table', columns: ['Условие', 'Изменение score', 'Граница'], rows: [
          ['Debt/EBITDA < 2', '+8', 'строго меньше (2 не входит)'],
          ['Debt/EBITDA > 6', '−10', 'строго больше (6 не входит)'],
          ['Debt/EBITDA > 4 (и не >6)', '−4', 'строго больше (4 не входит)'],
          ['Ровно 2, 4 или 6', '0', 'ни одно условие не срабатывает'],
        ] },
        { type: 'callout', calloutType: 'example', title: 'Числовой пример', body: [
          'TotalDebt = 900 млн, EBITDA = 300 млн ⇒ Debt/EBITDA = 3,0.',
          '3,0 не <2, не >4, не >6 ⇒ вклад 0.',
          'Если бы EBITDA = 120 млн, отношение 7,5 ⇒ сработал бы штраф −10.',
        ] },
        { type: 'paragraph', text: 'Ограничение: CashAndEquivalents не вычитается из долга, то есть net debt не применяется.' },
      ],
    },
    {
      slug: 'fundamentals-pe-ratio',
      title: 'Метрика 4: P/E',
      blocks: [
        { type: 'paragraph', text: 'Определение: P/E (Price/Earnings) — сколько рынки платят за единицу прибыли. Почему важно: очень высокий мультипликатор может означать завышенные ожидания.' },
        { type: 'paragraph', text: 'Сигнал участвует только если PeRatio > 0 (нулевой/отрицательный P/E не используется).' },
        { type: 'table', columns: ['P/E', 'Изменение score', 'Тип границ'], rows: [
          ['5 ≤ P/E ≤ 35', '+5', 'границы включительно'],
          ['P/E > 60', '−6', 'строго больше'],
          ['Остальные положительные значения', '0', 'нейтрально'],
        ] },
        { type: 'callout', calloutType: 'example', title: 'Числовой пример', body: [
          'Текущий score = 51.',
          'P/E = 22 ⇒ попадает в 5–35 включительно ⇒ score = 56.',
        ] },
      ],
    },
    {
      slug: 'fundamentals-pb-ratio',
      title: 'Метрика 5: P/B',
      blocks: [
        { type: 'paragraph', text: 'Определение: P/B (Price/Book) — отношение рыночной цены к балансовой стоимости капитала. Почему важно: помогает грубо сравнивать оценку бизнеса с бухгалтерским капиталом.' },
        { type: 'paragraph', text: 'Сигнал участвует только если PbRatio > 0.' },
        { type: 'table', columns: ['P/B', 'Изменение score', 'Тип границ'], rows: [
          ['0,5 ≤ P/B ≤ 8', '+3', 'границы включительно'],
          ['P/B > 15', '−6', 'строго больше'],
          ['Остальные положительные значения', '0', 'нейтрально'],
        ] },
        { type: 'callout', calloutType: 'example', title: 'Числовой пример', body: [
          'Текущий score = 56.',
          'P/B = 4 ⇒ диапазон 0,5–8 включительно ⇒ score = 59.',
        ] },
      ],
    },
    {
      slug: 'fundamentals-dividend-yield',
      title: 'Метрика 6: Dividend Yield',
      blocks: [
        { type: 'paragraph', text: 'Определение: Dividend Yield — дивидендная доходность в процентах. Почему важно: умеренная доходность может быть признаком зрелого cash-flow профиля, слишком высокая — иногда симптом падения цены и риска устойчивости выплат.' },
        { type: 'paragraph', text: 'Сигнал участвует только если DividendYield > 0. Значение интерпретируется как проценты (например, 1.8 означает 1,8%).' },
        { type: 'table', columns: ['Dividend Yield', 'Изменение score', 'Дополнительно'], rows: [
          ['1 ≤ DY ≤ 6', '+3', 'границы включительно'],
          ['DY > 10', '−3', 'добавляется warning DIVIDEND_YIELD_EXTREME'],
          ['Остальные положительные значения', '0', 'нейтрально'],
        ] },
        { type: 'callout', calloutType: 'example', title: 'Числовой пример', body: [
          'Текущий score = 59.',
          'DY = 12 ⇒ score = 59 − 3 = 56 и warning о необычно высокой доходности.',
        ] },
      ],
    },
    {
      slug: 'fundamentals-complete-score-example',
      title: 'Полный расчёт фундаментального компонента (пошаговый пример)',
      blocks: [
        { type: 'paragraph', text: 'Стартовый фундаментальный score всегда 50. Далее правила применяются по доступным полям snapshot. В конце результат ограничивается диапазоном 0–100 (clamp).' },
        { type: 'table', columns: ['Шаг', 'Расчёт', 'Результат'], rows: [
          ['Старт', 'base', '50'],
          ['NetIncomeTtm > 0', '+8', '58'],
          ['FreeCashFlowTtm > 0', '+7', '65'],
          ['Debt/EBITDA = 5,2', '−4 (ветка >4)', '61'],
          ['P/E = 24', '+5', '66'],
          ['P/B = 16', '−6', '60'],
          ['DividendYield = 1,8', '+3', '63'],
          ['Clamp 0..100', 'не меняет', '63'],
        ] },
        { type: 'paragraph', text: 'Если ни одно правило не удалось применить (например, поля отсутствуют или не проходят условия применимости), компонент Fundamentals становится null, добавляется warning FUNDAMENTALS_UNUSABLE, а итоговый общий Score ренормализует веса доступных компонентов.' },
      ],
    },
    {
      slug: 'fundamentals-horizon-weights-and-missing-behavior',
      title: 'Веса по горизонтам и что происходит при отсутствии fundamentals',
      blocks: [
        { type: 'table', columns: ['Горизонт', 'Вес Fundamentals', 'Если компонент недоступен'], rows: [
          ['3 месяца', '0%', 'Компонент не участвует (всегда null для этого горизонта)'],
          ['6 месяцев', '5%', 'Вес исключается, общий Score ренормализуется'],
          ['1 год', '20%', 'Вес исключается, общий Score ренормализуется'],
          ['2 года', '45%', 'Вес исключается, общий Score ренормализуется'],
        ] },
        { type: 'list', items: [
          'Ренормализация означает деление суммы вкладов на сумму доступных весов, а не на 1,0.',
          'Если доступных компонентов нет вообще, общий Score по горизонту возвращается как 50.',
          'При неполном наборе компонентов добавляется warning COMPONENTS_MISSING.',
        ] },
      ],
    },
    {
      slug: 'fundamentals-freshness-confidence-and-warnings',
      title: 'Свежесть, история и предупреждения: что меняет Confidence и warning',
      blocks: [
        { type: 'table', columns: ['Проверка', 'Порог', 'Эффект'], rows: [
          ['Старый snapshot fundamentals', 'FetchedAtUtc старше 35 дней', 'warning FUNDAMENTALS_STALE; для 1г/2г confidence × 0,8'],
          ['Нет fundamentals snapshot', 'snapshot отсутствует', 'warning FUNDAMENTALS_MISSING; компонент null; для 1г/2г confidence × 0,7'],
          ['История периодов для 2 лет', 'PeriodCount < 8 или span < 540 дней', 'warning FUNDAMENTAL_HISTORY_INSUFFICIENT; для 2г confidence × 0,8'],
        ] },
        { type: 'paragraph', text: 'Важно: historical FinancialPeriods сейчас не меняют числовой фундаментальный Score напрямую. Они влияют только на warning и confidence для горизонта 2 года.' },
      ],
    },
    {
      slug: 'fundamentals-sector-industry-limitations',
      title: 'Ограничение по сектору/отрасли (важно)',
      blocks: [
        { type: 'callout', calloutType: 'warning', title: 'Текущее поведение', body: [
          'Сектор и отрасль в текущем фундаментальном алгоритме не используются.',
          'Нет peer-нормализации, отраслевых медиан или разных наборов правил по типам компаний.',
        ] },
        { type: 'list', items: [
          'Банки и страховщики: Debt/EBITDA и часть порогов могут быть концептуально неприменимы.',
          'REIT/недвижимость и инфраструктура/utility: более высокий долг может быть нормой бизнес-модели.',
          'Технологические и growth-компании: «высокий» P/E не всегда означает переоценку.',
          'Циклические компании: EBITDA и прибыль могут резко колебаться по фазам цикла.',
          'Убыточные компании: отрицательные/нулевые valuation-поля часто исключаются, а не интерпретируются секторно.',
        ] },
        { type: 'paragraph', text: 'Peer-нормализация и sector-aware scoring — возможные будущие улучшения, но в текущей реализации их нет.' },
      ],
    },
    {
      slug: 'fundamentals-history-limitations',
      title: 'Ограничения исторических фундаментальных данных',
      blocks: [
        { type: 'paragraph', text: 'Даже на горизонте 2 года фундаментальный score может быть рассчитан по одному текущему snapshot. История периодов не используется для вычисления трендов в формуле score.' },
        { type: 'list', items: [
          'Не считаются тренды выручки (revenue growth).',
          'Не считаются тренды маржинальности и прибыли.',
          'Не считается динамика долга и net debt.',
          'Не считается стабильность free cash flow по периодам.',
          'Не рассчитывается отраслевое сравнение мультипликаторов.',
        ] },
      ],
    },
    {
      slug: 'fundamentals-disclaimer',
      title: 'Дисклеймер',
      blocks: [
        { type: 'callout', calloutType: 'important', body: [
          'Текущий фундаментальный компонент FinanceApp — rules-based эвристика.',
          'Это не intrinsic value модель, не прогноз, не гарантия и не персональная инвестиционная рекомендация.',
          'Результат не является самостоятельной командой Buy/Sell и должен использоваться вместе с контекстом, риском и дополнительной проверкой данных.',
        ] },
      ],
    },
  ],
  related: [
    { articleSlug: 'analytical-signal', sectionSlug: 'signal-components-weights', label: 'Общие веса компонентов сигнала' },
    { articleSlug: 'technical-indicator-formulas', sectionSlug: 'indicator-missing-confidence', label: 'Как считаются Confidence и ренормализация по техкомпонентам' },
  ],
};
