import React, { useEffect, useMemo, useState } from 'react';
import { Alert, Button, Card, Empty, Input, Space, Tag, Typography } from 'antd';
import { BookOutlined, LinkOutlined } from '@ant-design/icons';
import { Link, useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import AuthenticatedShell from '../components/AuthenticatedShell';
import { useAuth } from '../contexts/AuthContext';
import { getPortfolios } from '../services/api';
import type { Portfolio } from '../types';
import { HELP_ARTICLES } from '../help/content';
import { buildHelpArticleUrl, getHelpArticleBySlug, getOrderedHelpArticles, getOrderedHelpCategories, normalizeHelpText, searchHelpArticles } from '../help/utils';
import type { HelpBlock } from '../help/models';
import './HelpPage.css';

const { Title, Paragraph, Text } = Typography;

const renderHelpBlock = (block: HelpBlock): React.ReactNode => {
  switch (block.type) {
    case 'paragraph':
      return <Paragraph className="help-page__body-text">{block.text}</Paragraph>;
    case 'list': {
      const ListTag = block.ordered ? 'ol' : 'ul';
      return (
        <ListTag className="help-page__body-list">
          {block.items.map((item) => <li key={item}>{item}</li>)}
        </ListTag>
      );
    }
    case 'table':
      return (
        <div className="help-page__table-wrap">
          <table className="help-page__table">
            <thead>
              <tr>{block.columns.map((col) => <th key={col}>{col}</th>)}</tr>
            </thead>
            <tbody>
              {block.rows.map((row, rowIdx) => (
                <tr key={`${rowIdx}-${row.join('|')}`}>
                  {row.map((cell, cellIdx) => <td key={`${rowIdx}-${cellIdx}-${cell}`}>{cell}</td>)}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      );
    case 'callout':
      return (
        <Alert
          type={block.calloutType === 'example' ? 'info' : block.calloutType === 'warning' ? 'warning' : 'info'}
          showIcon
          message={block.title}
          description={(
            <ul className="help-page__callout-list">
              {block.body.map((line) => <li key={line}>{line}</li>)}
            </ul>
          )}
        />
      );
    case 'qa':
      return (
        <div className="help-page__qa-block">
          <Text strong className="help-page__qa-question">{block.question}</Text>
          <Paragraph className="help-page__body-text help-page__qa-answer">{block.answer}</Paragraph>
        </div>
      );
    default:
      return <Paragraph className="help-page__body-text" type="secondary">Содержимое этого блока пока не поддерживается текущей версией справки.</Paragraph>;
  }
};

const HelpPage: React.FC = () => {
  const { user, logout } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const { articleSlug } = useParams<{ articleSlug?: string }>();
  const [searchParams, setSearchParams] = useSearchParams();

  const [portfolios, setPortfolios] = useState<Portfolio[]>([]);
  const [copyStatus, setCopyStatus] = useState<string>('');

  const rawQuery = searchParams.get('q') ?? '';
  const searchQuery = normalizeHelpText(rawQuery);

  useEffect(() => {
    let cancelled = false;
    getPortfolios().then((res) => {
      if (!cancelled) setPortfolios(res.data);
    }).catch(() => {
      // no-op: help center should stay available even if portfolio request failed
    });

    return () => {
      cancelled = true;
    };
  }, []);

  const categories = useMemo(() => getOrderedHelpCategories(), []);
  const orderedArticles = useMemo(() => getOrderedHelpArticles(), []);
  const selectedArticle = useMemo(() => getHelpArticleBySlug(articleSlug), [articleSlug]);
  const hasUnknownArticle = Boolean(articleSlug && !selectedArticle);

  const searchResults = useMemo(() => searchHelpArticles(rawQuery), [rawQuery]);

  const articlesToShow = useMemo(() => {
    if (!searchQuery) return orderedArticles;
    const slugSet = new Set(searchResults.map((result) => result.articleSlug));
    return orderedArticles.filter((article) => slugSet.has(article.slug));
  }, [orderedArticles, searchQuery, searchResults]);

  useEffect(() => {
    if (!location.hash) return;
    const id = location.hash.replace(/^#/, '');
    const node = document.getElementById(id);
    if (node) {
      if (typeof node.scrollIntoView === 'function') {
        node.scrollIntoView({ block: 'start' });
      }
      node.setAttribute('tabindex', '-1');
      if (typeof node.focus === 'function') {
        node.focus();
      }
    }
  }, [location.hash, selectedArticle?.slug]);

  const onSearchChange = (value: string) => {
    const next = new URLSearchParams(searchParams);
    if (value.trim()) {
      next.set('q', value);
    } else {
      next.delete('q');
    }
    setSearchParams(next, { replace: true });
  };

  const onSearchEnter = () => {
    if (!searchQuery || searchResults.length === 0) return;
    navigate(buildHelpArticleUrl(searchResults[0].articleSlug, searchResults[0].sectionSlug));
  };

  const copyCurrentLink = async (sectionSlug?: string) => {
    const href = `${window.location.origin}${window.location.pathname}${window.location.search}${sectionSlug ? `#${sectionSlug}` : window.location.hash}`;
    try {
      await navigator.clipboard.writeText(href);
      setCopyStatus('Ссылка скопирована.');
    } catch {
      setCopyStatus('Не удалось скопировать ссылку. Скопируйте адрес вручную.');
    }
  };

  const navByCategory = categories.map((category) => ({
    ...category,
    articles: articlesToShow.filter((article) => article.categorySlug === category.slug),
  })).filter((category) => category.articles.length > 0);

  return (
    <AuthenticatedShell
      portfolios={portfolios}
      selectedKeys={['help']}
      onLogout={logout}
      userName={user?.username}
      activePortfolioId={undefined}
      headerLeft={<Title level={4} style={{ margin: 0 }}>Справка FinanceApp</Title>}
    >
      <div className="help-page" data-responsive="stack-lg">
        <aside className="help-page__sidebar" aria-label="Навигация по справке">
          <Space direction="vertical" size={12} style={{ width: '100%' }}>
            <Paragraph type="secondary" style={{ marginBottom: 0 }}>
              Руководство по возможностям приложения, аналитике, качеству данных и типичным вопросам.
            </Paragraph>
            <Input.Search
              allowClear
              value={rawQuery}
              onChange={(e) => onSearchChange(e.target.value)}
              onSearch={onSearchEnter}
              placeholder="Поиск по разделам и тексту"
              aria-label="Поиск по справке"
            />
            {!searchQuery && (
              <Text type="secondary">Откройте статью из разделов ниже.</Text>
            )}
            {searchQuery && searchResults.length === 0 && (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description="Ничего не найдено. Попробуйте сократить запрос или использовать ключевые слова из заголовков."
              />
            )}
            <Button type="link" onClick={() => onSearchChange('')} disabled={!rawQuery.trim()}>
              Очистить поиск
            </Button>
            {navByCategory.map((category) => (
              <Card key={category.slug} size="small" title={category.title}>
                <ul className="help-page__article-list" aria-label={`Категория ${category.title}`}>
                  {category.articles.map((article) => (
                    <li key={article.slug}>
                      <Link to={buildHelpArticleUrl(article.slug)}>{article.title}</Link>
                      {searchQuery && searchResults.find((item) => item.articleSlug === article.slug)?.excerpt && (
                        <div className="help-page__excerpt">{searchResults.find((item) => item.articleSlug === article.slug)?.excerpt}</div>
                      )}
                    </li>
                  ))}
                </ul>
              </Card>
            ))}
          </Space>
        </aside>

        <section className="help-page__content" aria-label="Содержимое статьи">
          {hasUnknownArticle && (
            <Alert
              type="warning"
              showIcon
              message="Статья не найдена"
              description="Проверьте ссылку или выберите статью из списка слева."
              style={{ marginBottom: 16 }}
            />
          )}

          {!selectedArticle ? (
            <Card>
              <Title level={3}>Центр справки FinanceApp</Title>
              <Paragraph className="help-page__body-text">
                Выберите статью слева или начните с быстрого старта.
              </Paragraph>
              <Space direction="vertical" size={8}>
                {HELP_ARTICLES.slice(0, 3).map((article) => (
                  <Link key={article.slug} to={buildHelpArticleUrl(article.slug)}>
                    <BookOutlined /> {article.title}
                  </Link>
                ))}
              </Space>
            </Card>
          ) : (
            <article>
              <header className="help-page__article-header">
                <div>
                  <Title level={2}>{selectedArticle.title}</Title>
                  <Paragraph className="help-page__body-text">{selectedArticle.summary}</Paragraph>
                  <Space wrap>
                    {selectedArticle.keywords.map((keyword) => <Tag key={keyword}>{keyword}</Tag>)}
                  </Space>
                </div>
                <Button icon={<LinkOutlined />} onClick={() => { void copyCurrentLink(); }}>
                  Копировать ссылку
                </Button>
              </header>

              {selectedArticle.sections.length > 2 && (
                <Card size="small" title="Содержание" style={{ marginBottom: 16 }}>
                  <ol className="help-page__toc-list">
                    {selectedArticle.sections.map((section) => (
                      <li key={section.slug}>
                        <a href={`#${section.slug}`}>{section.title}</a>
                      </li>
                    ))}
                  </ol>
                </Card>
              )}

              <div className="help-page__live-status" aria-live="polite">{copyStatus}</div>

              {selectedArticle.sections.map((section) => (
                <section key={section.slug} id={section.slug} className="help-page__section" aria-labelledby={`heading-${section.slug}`}>
                  <div className="help-page__section-header">
                    <Title level={3} id={`heading-${section.slug}`}>{section.title}</Title>
                    <Button size="small" type="link" onClick={() => { void copyCurrentLink(section.slug); }}>
                      Ссылка на раздел
                    </Button>
                  </div>
                  <div className="help-page__section-body">
                    {section.blocks.map((block, index) => (
                      <div key={`${section.slug}-${index}`}>{renderHelpBlock(block)}</div>
                    ))}
                  </div>
                </section>
              ))}

              {(selectedArticle.related?.length ?? 0) > 0 && (
                <Card size="small" title="Связанные статьи">
                  <ul className="help-page__article-list">
                    {selectedArticle.related?.map((link) => (
                      <li key={`${link.articleSlug}-${link.sectionSlug ?? ''}`}>
                        <Link to={buildHelpArticleUrl(link.articleSlug, link.sectionSlug)}>{link.label}</Link>
                      </li>
                    ))}
                  </ul>
                </Card>
              )}
            </article>
          )}
        </section>
      </div>
    </AuthenticatedShell>
  );
};

export default HelpPage;
