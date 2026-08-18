import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  base: '/financeapp/',
  plugins: [react()],
  server: {
    port: 3000,
  },
  test: {
    environment: 'node',
    environmentMatchGlobs: [
      ['**/*.interaction.test.tsx', 'jsdom'],
    ],
    setupFiles: ['./src/test-setup.ts'],
  },
});
