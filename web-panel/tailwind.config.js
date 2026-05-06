/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,ts,svelte}'],
  theme: {
    extend: {
      colors: {
        sentinel: {
          bg: '#0a0a0a',
          surface: '#141414',
          primary: '#fafafa',
          secondary: '#aab2c0',
          accent: '#3b82f6',
          danger: '#ef4444',
          success: '#22c55e',
        },
      },
      fontFamily: {
        sans: ['Inter', 'system-ui', 'sans-serif'],
      },
    },
  },
  plugins: [],
}
