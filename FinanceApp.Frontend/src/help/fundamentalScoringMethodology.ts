import type { HelpArticle } from './models';

export const FUNDAMENTAL_SCORING_METHODOLOGY_ARTICLE: HelpArticle = {
  slug: 'fundamental-scoring-methodology',
  categorySlug: 'analytics',
  title: 'Фундаментальный компонент Score: текущая методика',
  summary: 'Подробное объяснение текущей rules-based методики фундаментального компонента: входы, формулы, пороги, границы, ограничения и влияние на Score/Confidence по горизонтам.',
  keywords: [
    'фундаментальный анализ',
    'Score',
    'Confidence',
    'fundamentals',
    'Net Income TTM',
    'Free Cash Flow TTM',
    'Debt-to-EBITDA',
    'P/E',
    'P/B',
    'Dividend Yield',
    'renormalization',
    'FUNDAMENTAL_HISTORY_INSUFFICIENT',
    'сектор',
    'отрасль',
    'банки',
    'REIT',
  ],
  order: 4.5,
  sections: [
    {
      slug: 'fundamental-methodology-basics',
      title: 'Что измеряет фундаментальный анализ и чем он отличается от технического',
      blocks: [
        { type: 'paragraph', text: 'В текущем FinanceApp фундаментальный анализ — это часть итогового аналитического сигнала, которая оценивает базовое финансовое состояние компании по сохранённому snapshot фундаментальных полей. Технический анализ опирается на историю цен, а фундаментальный компонент — на отчётные и мультипликаторные поля (например, Net Income TTM, P/E).' },
        { type: 'list', items: [
          'Score (0..100) — это итоговая агрегированная оценка горизонта. Фундаментальный компонент влияет на неё только через свой вес в выбранном горизонте.',
          'Confidence (0..1, в UI как проценты) — это уверенность в качестве расчёта: длина истории, свежесть, полнота данных, доступность компонент.',
          'Высокий Score не гарантирует высокий Confidence, и наоборот.',
        ] },
        { type: 'callout', calloutType: 'important', body: [
          'Текущая реализация — rules-based эвристика по фиксированным порогам. Это не intrinsic valuation, не DCF, не прогноз будущей цены и не персональная инвестиционная рекомендация.',
        ] },
      ],
    },
    {
      slug: 'fundamental-methodology-inputs-and-refresh',
      title: 'Какие входные данные используются, как выбирается snapshot и что происходит при открытии панели',
      blocks: [
        { type: 'paragraph', text: 'Для фундаментального компонента backend берёт только последний сохранённый fundamentals snapshot (по FetchedAtUtc) и, отдельно, агрегированную информацию по истории financial periods (количество и диапазон дат PeriodEndDate).' },
        { type: 'table', columns: ['Вход', 'Как выбирается', 'Единицы/формат'], rows: [
          ['Fundamentals snapshot', 'Последняя запись FundamentalsSnapshots для акции', 'Числовые поля snapshot, null допускается'],
          ['Fundamental periods range', 'Count + Min/Max PeriodEndDate из FinancialPeriods для акции', 'count и даты'],
          ['Текущее время', 'UTC now на backend', 'для stale-проверок и confidence'],
        ] },
        { type: 'list', items: [
          'Если поле не заполнено (null), соответствующий сигнал не применяется.',
          'Если fundamentals snapshot отсутствует, для горизонтов 6 месяцев/1 год/2 года компонент возвращается как null с warning FUNDAMENTALS_MISSING.',
          'Fundamentals stale: если snapshot старше 35 дней, добавляется warning FUNDAMENTALS_STALE.',
          'Открытие панели «Аналитический сигнал» вызывает только GET /api/Stocks/{id}/technical-analysis и не запускает provider refresh.',
          'Открытие панели «Фундаментальные показатели» вызывает GET /api/Stocks/{id}/fundamentals; этот endpoint может запустить refresh при несвежем кэше.',
        ] },
      ],
    },
    {
      slug: 'fundamental-methodology-metric-net-income',
      title: 'Метрика 1: Net Income TTM',
      blocks: [
        { type: 'paragraph', text: 'Смысл: прибыль за последние 12 месяцев (TTM). В текущем правиле проверяется только знак значения.' },
        { type: 'paragraph', text: 'Правило: если NetIncomeTtm > 0, к фундаментальному score добавляется +8; иначе (0 или отрицательное) вычитается −8.' },
        { type: 'table', columns: ['Условие', 'Изменение score'], rows: [
          ['NetIncomeTtm > 0', '+8'],
          ['NetIncomeTtm <= 0', '−8'],
          ['NetIncomeTtm = null', '0 (сигнал не применяется)'],
        ] },
        { type: 'callout', calloutType: 'example', title: 'Пример', body: [
          'Стартовый фундаментальный score = 50.',
          'Если NetIncomeTtm = 120 000 000, после этой метрики score = 58.',
          'Если NetIncomeTtm = 0, после этой метрики score = 42.',
        ] },
      ],
    },
    {
      slug: 'fundamental-methodology-metric-fcf',
      title: 'Метрика 2: Free Cash Flow TTM',
      blocks: [
        { type: 'paragraph', text: 'Смысл: свободный денежный поток за 12 месяцев. Логика также проверяет только знак.' },
        { type: 'paragraph', text: 'Правило: если FreeCashFlowTtm > 0, +7; иначе (0 или отрицательное), −7.' },
        { type: 'table', columns: ['Условие', 'Изменение score'], rows: [
          ['FreeCashFlowTtm > 0', '+7'],
          ['FreeCashFlowTtm <= 0', '−7'],
          ['FreeCashFlowTtm = null', '0 (сигнал не применяется)'],
        ] },
        { type: 'callout', calloutType: 'example', title: 'Пример', body: [
          'Текущий score после прошлых шагов = 58.',
          'Если FreeCashFlowTtm = −15 000 000, новый score = 51.',
        ] },
      ],
    },
    {
      slug: 'fundamental-methodology-metric-debt-to-ebitda',
      title: 'Метрика 3: Debt-to-EBITDA (текущая реализация)',
      blocks: [
        { type: 'paragraph', text: 'Смысл: долговая нагрузка относительно EBITDA. Формула в текущем коде: DebtToEbitda = TotalDebt / EbitdaTtm.' },
        { type: 'paragraph', text: 'Сигнал применяется только если TotalDebt и EbitdaTtm заполнены и EbitdaTtm > 0.' },
        { type: 'table', columns: ['Условие', 'Изменение score', 'Граничные значения'], rows: [
          ['DebtToEbitda < 2', '+8', '2 не включается'],
          ['DebtToEbitda > 6', '−10', '6 не включается'],
          ['DebtToEbitda > 4 и <= 6', '−4', '4 не включается, 6 включается'],
          ['2 <= DebtToEbitda <= 4', '0', 'оба края включаются'],
          ['Нет TotalDebt или EbitdaTtm<=0 или null', '0', 'сигнал не применяется'],
        ] },
        { type: 'callout', calloutType: 'warning', title: 'Важно про cash', body: [
          'Сейчас не используется cash-adjusted debt (например, NetDebt = TotalDebt − Cash). Поле CashAndEquivalents загружается, но не участвует в формуле фундаментального score.',
        ] },
        { type: 'callout', calloutType: 'example', title: 'Пример', body: [
          'TotalDebt = 900, EbitdaTtm = 150 -> DebtToEbitda = 6,0.',
          'В текущих границах это не >6, но >4, поэтому изменение = −4.',
        ] },
      ],
    },
    {
      slug: 'fundamental-methodology-metric-pe-pb-dy',
      title: 'Метрики 4–6: P/E, P/B, Dividend Yield',
      blocks: [
        { type: 'paragraph', text: 'Эти мультипликаторы используются только при положительном значении (>0). Нулевые/отрицательные/отсутствующие значения не дают ни бонуса, ни штрафа.' },
        { type: 'table', columns: ['Метрика', 'Порог', 'Изменение score', 'Границы'], rows: [
          ['P/E', '5 <= P/E <= 35', '+5', '5 и 35 включаются'],
          ['P/E', 'P/E > 60', '−6', '60 не включается'],
          ['P/B', '0.5 <= P/B <= 8', '+3', '0.5 и 8 включаются'],
          ['P/B', 'P/B > 15', '−6', '15 не включается'],
          ['Dividend Yield', '1 <= DY <= 6', '+3', '1 и 6 включаются'],
          ['Dividend Yield', 'DY > 10', '−3 + warning DIVIDEND_YIELD_EXTREME', '10 не включается'],
        ] },
        { type: 'callout', calloutType: 'example', title: 'Пошаговый пример', body: [
          'Текущий score = 51.',
          'P/E = 60 -> ни бонуса, ни штрафа (условие строго >60). Score остаётся 51.',
          'P/B = 0,5 -> +3. Score = 54.',
          'Dividend Yield = 12 -> −3 и warning DIVIDEND_YIELD_EXTREME. Score = 51.',
        ] },
      ],
    },
    {
      slug: 'fundamental-methodology-component-calculation',
      title: 'Полный расчёт фундаментального компонента: шаг за шагом',
      blocks: [
        { type: 'paragraph', text: 'Базовый фундаментальный score всегда начинается с 50. Далее применяются доступные сигналы. После всех шагов результат ограничивается (clamp) диапазоном 0..100.' },
        { type: 'list', ordered: true, items: [
          'Начало: score = 50, appliedSignals = 0.',
          'Для каждой доступной метрики добавляется/вычитается фиксированный вклад и увеличивается appliedSignals.',
          'Если appliedSignals = 0 (все поля unusable/null), добавляется warning FUNDAMENTALS_UNUSABLE и компонент возвращается как null.',
          'Если component score получен, он clamp-ится в диапазон 0..100.',
        ] },
        { type: 'callout', calloutType: 'example', title: 'Мульти-метрический пример', body: [
          'Вход: NetIncomeTtm > 0 (+8), FreeCashFlowTtm > 0 (+7), DebtToEbitda = 1,8 (+8), P/E = 18 (+5), P/B = 2 (+3), Dividend Yield = 2 (+3).',
          'Расчёт: 50 + 8 + 7 + 8 + 5 + 3 + 3 = 84.',
          'Итог фундаментального компонента: 84/100.',
        ] },
      ],
    },
    {
      slug: 'fundamental-methodology-horizons-and-weights',
      title: 'Как фундаментальный компонент влияет на итоговый Score по горизонтам',
      blocks: [
        { type: 'table', columns: ['Горизонт', 'Вес Fundamentals', 'Особенность'], rows: [
          ['3 месяца', '0%', 'Фундаментальный компонент принудительно null'],
          ['6 месяцев', '5%', 'Небольшое влияние fundamentals'],
          ['1 год', '20%', 'Заметное влияние fundamentals'],
          ['2 года', '45%', 'Ключевое влияние fundamentals'],
        ] },
        { type: 'paragraph', text: 'Итоговый score горизонта считается как взвешенное среднее доступных компонент. Если компонент отсутствует (null), его вес исключается, а оставшиеся веса ренормализуются. Это сопровождается warning COMPONENTS_MISSING при суммарном доступном весе < 100%.' },
        { type: 'callout', calloutType: 'example', title: 'Пример ренормализации', body: [
          'Горизонт 1 год: веса Trend 30%, Momentum 15%, Returns 20%, Risk 15%, Fundamentals 20%.',
          'Если Fundamentals=null, доступный суммарный вес = 80%.',
          'Финальный score = weightedScore / 0,8 (а не деление на 1,0).',
        ] },
      ],
    },
    {
      slug: 'fundamental-methodology-sector-limitations',
      title: 'Сектор/отрасль сейчас не учитываются: почему это важно',
      blocks: [
        { type: 'paragraph', text: 'Текущий алгоритм фундаментального компонента не использует sector/industry компании и применяет единые универсальные пороги ко всем эмитентам.' },
        { type: 'callout', calloutType: 'warning', title: 'Промежуточное ограничение текущей версии', body: [
          'Единые пороги могут вводить в заблуждение для банков и страховщиков (другая природа баланса и долговых метрик).',
          'Для REIT/real estate и utilities высокий долг может быть структурной нормой модели бизнеса.',
          'Для technology/growth и иных быстрорастущих компаний высокие P/E и P/B не всегда означают «переоценку» в том же смысле, что для зрелых бизнесов.',
          'Для циклических и временно убыточных компаний отрицательные TTM-показатели могут быть частью фазы цикла, а не постоянным состоянием.',
          'Для loss-making компаний P/E часто неприменим или нестабилен; текущее правило просто не применяет сигнал при неположительном P/E.',
        ] },
        { type: 'paragraph', text: 'Это означает, что текущий фундаментальный score лучше интерпретировать как общий эвристический фильтр, а не как отраслевую норму качества.' },
      ],
    },
    {
      slug: 'fundamental-methodology-history-and-confidence',
      title: 'Как исторические фундаментальные периоды влияют на Score, Confidence и предупреждения',
      blocks: [
        { type: 'list', items: [
          'Фундаментальный component score считается только по последнему snapshot; тренды по history периодам (например, YoY рост выручки, маржи, EPS-тренд) сейчас не вычисляются.',
          'Для горизонта 2 года проверяется достаточность historical periods: нужно минимум 8 периодов и span >= 540 дней, иначе warning FUNDAMENTAL_HISTORY_INSUFFICIENT.',
          'На 2 года при insufficient history confidence дополнительно умножается на 0.8.',
          'На горизонтах 1 год и 2 года: если fundamentals отсутствуют, confidence умножается на 0.7.',
          'На горизонтах 1 год и 2 года: если snapshot stale (>35 дней), confidence умножается на 0.8.',
          'Эти правила влияют на confidence и warning-слои, но не пересчитывают сами пороги метрик fundamentals.',
        ] },
      ],
    },
    {
      slug: 'fundamental-methodology-disclaimer',
      title: 'Ограничения интерпретации и ответственность',
      blocks: [
        { type: 'callout', calloutType: 'important', body: [
          'Текущий фундаментальный компонент — это формализованная система фиксированных правил (heuristic).',
          'Он не оценивает справедливую стоимость компании, не строит персональный финансовый план, не гарантирует результат и не даёт индивидуальный инвестиционный совет.',
          'Перед решениями о сделках всегда проверяйте первичные отчётные данные, качество истории, предупреждения и контекст конкретной отрасли.',
        ] },
      ],
    },
  ],
  related: [
    { articleSlug: 'analytical-signal', sectionSlug: 'signal-components-weights', label: 'Как компоненты входят в итоговый Score' },
    { articleSlug: 'fundamentals', sectionSlug: 'fundamentals-limitations-and-signal-impact', label: 'Экран фундаментальных данных и ограничения' },
    { articleSlug: 'data-quality-and-freshness', sectionSlug: 'data-quality-common-warnings', label: 'Предупреждения и качество данных' },
  ],
};
