import { marked } from 'marked';

export function renderProjectBody(body: string): string {
  return marked.parse(body, { async: false, gfm: true, breaks: false }) as string;
}

marked.use({
  renderer: {
    link({ href, title, tokens }) {
      const text = this.parser.parseInline(tokens);
      const titleAttr = title ? ` title="${title}"` : '';
      return `<a href="${href}"${titleAttr} target="_blank" rel="noopener noreferrer">${text}</a>`;
    },
  },
});
