// Help page (§ Help nav): renders the full User & Administrator Guide in-app, in the active
// UI language. The Markdown is the canonical docs/USER_GUIDE{,.en}.md, copied under src/content
// (the frontend Docker build context is ./frontend only, so it cannot import from ../docs — a
// sync test guards the copy against drift, see content/userGuide.sync.test.ts).

import { useMemo } from 'react';
import type { AnchorHTMLAttributes } from 'react';
import { useTranslation } from 'react-i18next';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import rehypeSlug from 'rehype-slug';
import { currentLanguage } from '@/i18n/config';
import ukGuide from '@/content/user-guide.uk.md?raw';
import enGuide from '@/content/user-guide.en.md?raw';

// The in-app page supplies its own <h1> (the "Help" nav title) and the language switcher in the
// header, so we drop the guide's own top-level title and its cross-language link bullet. Everything
// below (intro, the ## table of contents, and the ## sections) renders as-is.
function prepareGuide(markdown: string): string {
  return markdown
    .replace(/^#\s.*\r?\n/, '') // leading H1 title
    .replace(/^-\s.*(USER_GUIDE\.en\.md|USER_GUIDE\.md).*\r?\n/m, ''); // cross-language link bullet
}

// Links inside the guide are either in-page anchors (the table of contents → rehype-slug ids) or
// external URLs. Anchors keep the default behaviour (the browser scrolls to the id); external links
// open in a new tab with a safe rel.
function GuideLink({ href, children, ...rest }: AnchorHTMLAttributes<HTMLAnchorElement>) {
  if (href && !href.startsWith('#')) {
    return (
      <a href={href} target="_blank" rel="noopener noreferrer" {...rest}>
        {children}
      </a>
    );
  }
  return (
    <a href={href} {...rest}>
      {children}
    </a>
  );
}

export function HelpPage() {
  const { t } = useTranslation('common');
  const lang = currentLanguage();
  const markdown = useMemo(() => prepareGuide(lang === 'en' ? enGuide : ukGuide), [lang]);

  return (
    <div className="page-container">
      <div className="page-header">
        <h1>{t('help.title')}</h1>
      </div>
      <p className="page-note">{t('help.subtitle')}</p>
      <article className="markdown-body">
        <ReactMarkdown
          remarkPlugins={[remarkGfm]}
          rehypePlugins={[rehypeSlug]}
          components={{ a: GuideLink }}
        >
          {markdown}
        </ReactMarkdown>
      </article>
    </div>
  );
}
