import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'HOPPER',
  description: 'Self-hosted mod sync for Minecraft servers. One jar in mods/, and the client keeps itself in step.',
  lastUpdated: true,
  cleanUrls: true,
  // The same files are read on GitHub, where links are written as "docs/*.md".
  ignoreDeadLinks: true,
  head: [
    ['link', { rel: 'icon', href: '/favicon.svg' }],
  ],
  themeConfig: {
    logo: '/logo.svg',
    nav: [
      { text: 'Intro', link: '/intro' },
      { text: 'Setup', link: '/self-host' },
      { text: 'How it works', link: '/how-it-works' },
      { text: 'Development', link: '/dev-setup' },
    ],
    sidebar: [
      { text: 'What is HOPPER?', link: '/intro' },
      {
        text: 'Setup',
        collapsed: false,
        items: [
          { text: 'Self-hosting', link: '/self-host' },
        ],
      },
      {
        text: 'How it works',
        collapsed: false,
        items: [
          { text: 'Overview', link: '/how-it-works' },
          { text: 'The locator build', link: '/locator' },
        ],
      },
      {
        text: 'Development',
        collapsed: false,
        items: [
          { text: 'Developer setup', link: '/dev-setup' },
        ],
      },
    ],
    socialLinks: [
      { icon: 'github', link: 'https://github.com/PianoNic/HOPPER' },
    ],
    search: { provider: 'local' },
    editLink: {
      pattern: 'https://github.com/PianoNic/HOPPER/edit/main/docs/:path',
      text: 'Edit this page on GitHub',
    },
    footer: {
      message: 'Made with care by PianoNic.',
      copyright: 'HOPPER',
    },
  },
})
