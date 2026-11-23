import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { fileURLToPath, URL } from 'node:url';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: {
    '@': fileURLToPath(new URL('.', import.meta.url)),
    '@line-language-learn/shared': fileURLToPath(new URL('./shared/index.ts', import.meta.url)),
  } },
  server: {
    port: 3000,
    strictPort: true,
    watch: { ignored: ["functions","bff",".uv",".swallowkit","shared/dist"].map(directory => `${fileURLToPath(new URL('.', import.meta.url)).replace(/\\/g, '/').replace(/\/$/, '')}/${directory}/**`) },
  },
  build: { outDir: 'dist' },
});
