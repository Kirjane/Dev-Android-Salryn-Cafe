/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{js,ts,jsx,tsx}"],
  theme: {
    extend: {
      colors: {
        navy: {
          900: "#0f1b2d",
          800: "#152238",
          700: "#1c2d47",
        },
      },
    },
  },
  plugins: [],
};
