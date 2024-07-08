// uno.config.ts
import { defineConfig, presetUno, presetAttributify } from "unocss";

export default defineConfig({
  cli: {
    entry: {
      patterns: ["./Components/**/*.razor"],
      outFile: "wwwroot/uno.css",
    },
  },
  content: {
    filesystem: ["**/*.{html,js,ts,jsx,tsx,vue,svelte,astro,.razor}"],
  },
  presets: [presetUno(), presetAttributify()],
});
