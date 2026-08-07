import { marked } from 'marked';

/**
 * Renders a Modrinth project body. The result is meant for `[innerHTML]`, which runs Angular's own
 * HTML sanitiser: scripts, event handlers, iframes and unsafe URL schemes never survive it.
 *
 * marked plus that sanitiser rather than ngx-markdown, which lists zone.js as a peer dependency and
 * this application is zoneless.
 *
 * What the sanitiser does not do is stop an image or a link from reaching a third-party host, so a
 * project page can still see that someone opened it. That is the same exposure as visiting the page
 * on modrinth.com, which is what this replaces.
 */
export function renderProjectBody(body: string): string {
  return marked.parse(body, { async: false, gfm: true, breaks: false }) as string;
}

// Anything a project body links to is somebody else's site, so it opens in its own tab and cannot
// reach back through window.opener.
marked.use({
  renderer: {
    link({ href, title, tokens }) {
      const text = this.parser.parseInline(tokens);
      const titleAttr = title ? ` title="${title}"` : '';
      return `<a href="${href}"${titleAttr} target="_blank" rel="noopener noreferrer">${text}</a>`;
    },
  },
});
