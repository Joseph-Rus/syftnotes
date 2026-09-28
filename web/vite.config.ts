/// <reference types="vitest/config" />
import { defineConfig, loadEnv } from 'vite';
import preact from '@preact/preset-vite';

// The phone app is served by the student's own library at https://<library>.ts.net/app/.
// `npm run dev` proxies /api to a real library: STUDYSTASH_LIBRARY=https://my-library.tail1234.ts.net npm run dev
// (and STUDYSTASH_KEY=<library password> to skip pairing while you work).
export default defineConfig(({ mode }) => {
  const env = { ...process.env, ...loadEnv(mode, process.cwd(), 'STUDYSTASH_') };
  const library = env.STUDYSTASH_LIBRARY || 'http://127.0.0.1:8787';
  const key = env.STUDYSTASH_KEY;
  return {
    base: '/app/',
    plugins: [preact()],
    build: {
      outDir: 'dist',
      emptyOutDir: true,
      target: 'es2022',
      sourcemap: false,
      assetsInlineLimit: 0,
    },
    server: {
      host: true,
      proxy: {
        '/api': {
          target: library,
          changeOrigin: true,
          secure: false,
          cookieDomainRewrite: '',
          headers: key ? { Authorization: `Bearer ${key}` } : {},
        },
      },
    },
    test: {
      environment: 'jsdom',
      globals: true,
      include: ['tests/**/*.test.{ts,tsx}'],
      setupFiles: ['tests/setup.ts'],
    },
  };
});
