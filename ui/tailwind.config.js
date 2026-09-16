/** @type {import('tailwindcss').Config} */
export default {
  darkMode: 'class',
  content: [
    "./index.html",
    "./src/**/*.{vue,js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        light: {
          bg: "#F8FAFC",
          card: "#FFFFFF",
          border: "#E2E8F0",
          inner: "#F1F5F9",
        },
        dark: {
          bg: "#0B0F19",
          card: "#111827",
          border: "#1F2937",
          inner: "#0F172A",
        },
        emerald: {
          main: "#059669",
          cyan: "#0284C7",
          light: "#10B981",
        }
      }
    },
  },
  plugins: [],
}
