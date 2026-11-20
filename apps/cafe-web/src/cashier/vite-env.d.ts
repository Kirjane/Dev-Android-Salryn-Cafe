/// <reference types="vite/client" />

declare module "@salryn-brand/*.png" {
  const src: string;
  export default src;
}

declare module "@salryn-brand/colors.json" {
  const colors: {
    espresso: string;
    cream: string;
    copper: string;
    darkCopper: string;
    latte: string;
    mocha: string;
  };
  export default colors;
}
