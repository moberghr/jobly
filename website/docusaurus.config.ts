import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const config: Config = {
  title: 'Warp',
  tagline: 'Distributed job processing and message queue for .NET',
  favicon: 'img/favicon.ico',

  future: {
    v4: true,
  },

  url: 'https://moberghr.github.io',
  baseUrl: '/warp/',

  organizationName: 'moberghr',
  projectName: 'warp',

  onBrokenLinks: 'throw',

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  headTags: [
    {
      tagName: 'link',
      attributes: {
        rel: 'alternate',
        type: 'text/plain',
        title: 'LLM-friendly docs',
        href: '/warp/llms.txt',
      },
    },
    {
      tagName: 'link',
      attributes: {
        rel: 'alternate',
        type: 'text/plain',
        title: 'LLM-friendly full reference',
        href: '/warp/llms-full.txt',
      },
    },
  ],

  presets: [
    [
      'classic',
      {
        docs: {
          sidebarPath: './sidebars.ts',
          editUrl: 'https://github.com/moberghr/warp/tree/main/website/',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    image: 'img/screenshots/01-dashboard.png',
    colorMode: {
      respectPrefersColorScheme: true,
      disableSwitch: false,
    },
    navbar: {
      title: 'Warp',
      items: [
        {
          type: 'docSidebar',
          sidebarId: 'tutorialSidebar',
          position: 'left',
          label: 'Docs',
        },
        {
          href: 'https://github.com/moberghr/warp',
          label: 'GitHub',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Docs',
          items: [
            { label: 'Getting Started', to: '/docs/getting-started' },
            { label: 'Patterns', to: '/docs/patterns' },
            { label: 'UI', to: '/docs/ui/overview' },
            { label: 'Releases', to: '/docs/releases' },
          ],
        },
        {
          title: 'More',
          items: [
            { label: 'GitHub', href: 'https://github.com/moberghr/warp' },
            { label: 'AI Docs (llms.txt)', href: 'pathname:///warp/llms.txt' },
            { label: 'AI Full Reference', href: 'pathname:///warp/llms-full.txt' },
          ],
        },
      ],
      copyright: `Copyright ${new Date().getFullYear()} Warp. Built with Docusaurus.`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ['csharp'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
