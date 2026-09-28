// Lint: the recommended rules for JavaScript and TypeScript, nothing more. Formatting is Prettier's job.
import js from '@eslint/js';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  { ignores: ['dist', 'node_modules'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    rules: {
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
    },
  },
  {
    // The service worker: its own globals (self, caches, clients), not a page's or Node's.
    files: ['public/sw.js'],
    languageOptions: {
      globals: { self: 'readonly', caches: 'readonly', fetch: 'readonly', clients: 'readonly', URL: 'readonly', Promise: 'readonly' },
    },
  },
);
