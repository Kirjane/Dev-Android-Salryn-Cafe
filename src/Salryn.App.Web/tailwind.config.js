/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{js,ts,jsx,tsx}"],
  theme: {
    extend: {
      colors: {
        navy: {
          950: "#091321",
          900: "#0f1a2a",
          800: "#162438",
          700: "#1e2a3a",
          600: "#334155",
        },
      },
      boxShadow: {
        card: "0 1px 2px 0 rgb(15 23 42 / 0.05), 0 3px 8px -6px rgb(15 23 42 / 0.16)",
        rail: "inset -1px 0 0 0 rgb(255 255 255 / 0.04)",
      },
    },
  },
  plugins: [],
};
