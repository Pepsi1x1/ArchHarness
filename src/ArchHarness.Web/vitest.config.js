import { defineConfig } from 'vitest/config';
import { fileURLToPath } from 'node:url';

const webRoot = fileURLToPath(new URL('.', import.meta.url));

export default defineConfig({
  root: webRoot,
  test: {
    environment: 'jsdom',
    globals: true,
    include: ['tests/**/*.test.js'],
    restoreMocks: true,
    unstubGlobals: true,
    coverage: {
      provider: 'v8',
      reporter: ['text', 'lcov'],
      include: ['wwwroot/js/**/*.js']
    }
  }
});