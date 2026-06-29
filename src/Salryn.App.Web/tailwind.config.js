/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{js,ts,jsx,tsx}"],
  theme: {
    extend: {
      colors: {
        navy: {
          950: "#0b1422",
          900: "#0f1b2d",
          800: "#152238",
          700: "#1c2d47",
          600: "#24395a",
        },
      },
      boxShadow: {
        card: "0 1px 2px 0 rgb(15 27 45 / 0.06), 0 1px 3px 0 rgb(15 27 45 / 0.08)",
        rail: "inset -1px 0 0 0 rgb(255 255 255 / 0.04)",
      },
    },
  },
  plugins: [],
};
