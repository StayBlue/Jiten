// https://nuxt.com/docs/api/configuration/nuxt-config

import Aura from '@primeuix/themes/aura';
import { definePreset } from '@primeuix/styled';
import tailwindcss from '@tailwindcss/vite';
import path from 'node:path';
import * as fs from 'node:fs';

// Custom theming
const JitenPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '{purple.50}',
      100: '{purple.100}',
      200: '{purple.200}',
      300: '{purple.300}',
      400: '{purple.400}',
      500: '{purple.500}',
      600: '{purple.600}',
      700: '{purple.700}',
      800: '{purple.800}',
      900: '{purple.900}',
      950: '{purple.950}',
    },
  },
  components: {
    card: {
      caption: {
        gap: '0',
      },
      body: {
        padding: '1rem',
      },
    },
  },
});

export default defineNuxtConfig({
  compatibilityDate: '2025-07-14',
  devtools: { enabled: true },
  runtimeConfig: {
    public: {
      baseURL: 'https://localhost:7299/api/',
      recaptcha:{
        v2SiteKey: process.env.NUXT_PUBLIC_RECAPTCHA_V2_SITE_KEY || '',
      }
    },
  },
  modules: [
    '@primevue/nuxt-module',
    '@nuxt/icon',
    '@nuxtjs/google-fonts',
    '@pinia/nuxt',
    '@nuxtjs/seo',
    '@nuxt/scripts',
    'nuxt-umami',
    'nuxt-vue3-google-signin',
    'vue-recaptcha/nuxt'
  ],
  primevue: {
    options: {
      theme: {
        preset: JitenPreset,
        options: {
          darkModeSelector: '.dark-mode',
        },
      },
    },
  },
  googleFonts: {
    families: {
      'Noto+Sans+JP': {
        wght: [400, 700],
        ital: [400, 700] // if you need italic
      }
    },
    display: 'swap',
    preload: true,
    useStylesheet: true,
    download: false,
  },
  vite: {
    plugins: [tailwindcss()],
    build: {
      rollupOptions: {
        external: ['open'],
      },
    },
  },
  css: ['~/assets/css/main.css'],
  sitemap: {
    sources: ['/api/__sitemap__/urls'],
  },
  app: {
    head: {
      title: 'Jiten',
      htmlAttrs: {
        lang: 'en',
      },
      link: [{ rel: 'icon', type: 'image/x-icon', href: '/favicon.ico' }],
    },
  },
  site: {
    url: 'https://jiten.moe',
    name: 'Vocabulary lists and anki decks for all your Japanese media',
  },
  ogImage: {
    fonts: [
      {
        name: 'Noto Sans JP',
        weight: 400,
        path: '/fonts/NotoSansJP-Regular.ttf',
      },
    ],
  },
  umami: {
    id: process.env.NUXT_PUBLIC_SCRIPTS_UMAMI_ANALYTICS_WEBSITE_ID || '',
    host: process.env.NUXT_PUBLIC_SCRIPTS_UMAMI_ANALYTICS_HOST_URL || '',
    autoTrack: true,
    proxy: 'cloak',
    ignoreLocalhost: true,
  },
  googleSignIn:{
    clientId: process.env.NUXT_PUBLIC_GOOGLE_SIGNIN_CLIENT_ID || '',
  },
  devServer: process.env.NODE_ENV === 'development'
    ? {
      https: {
        key: fs.readFileSync(path.resolve(__dirname, 'localhost-key.pem')).toString(),
        cert: fs.readFileSync(path.resolve(__dirname, 'localhost.pem')).toString()
      }
    }
    : {}
});
