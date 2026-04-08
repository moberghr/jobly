import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const config: Config = {
  title: 'Jobly',
  tagline: 'Distributed job processing and message queue for .NET',
  favicon: 'img/favicon.ico',

  future: {
    v4: true,
  },

  url: 'https://moberghr.github.io',
  baseUrl: '/jobly/',

  organizationName: 'moberghr',
  projectName: 'jobly',

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
        href: '/jobly/llms.txt',
      },
    },
    {
      tagName: 'link',
      attributes: {
        rel: 'alternate',
        type: 'text/plain',
        title: 'LLM-friendly full reference',
        href: '/jobly/llms-full.txt',
      },
    },
  ],

  presets: [
    [
      'classic',
      {
        docs: {
          sidebarPath: './sidebars.ts',
          editUrl: 'https://github.com/moberghr/jobly/tree/main/website/',
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
      title: 'Jobly',
      items: [
        {
          type: 'docSidebar',
          sidebarId: 'tutorialSidebar',
          position: 'left',
          label: 'Docs',
        },
        {
          href: 'https://github.com/moberghr/jobly',
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
            { label: 'Dashboard', to: '/docs/dashboard/overview' },
          ],
        },
        {
          title: 'More',
          items: [
            { label: 'GitHub', href: 'https://github.com/moberghr/jobly' },
            { label: 'AI Docs (llms.txt)', href: 'pathname:///jobly/llms.txt' },
            { label: 'AI Full Reference', href: 'pathname:///jobly/llms-full.txt' },
          ],
        },
      ],
      copyright: `Copyright ${new Date().getFullYear()} Jobly. Built with Docusaurus.`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ['csharp'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
