# Salryn Cafe color palette

A proposed application palette coordinated with the generated espresso, cream, and copper artwork. Generated images contain subtle tonal variation; these values are the consistent UI tokens to use going forward.

| Color | Hex | Suggested use |
| --- | --- | --- |
| Espresso | `#3B1204` | Primary text, navigation, primary buttons |
| Cream | `#FFF4E1` | Main background, text on espresso |
| Copper | `#CC7436` | Decorative accents, icons, highlights |
| Dark copper | `#934719` | Links and accent buttons on cream |
| Latte | `#E9D5BB` | Borders and soft surface fills |
| Mocha | `#735447` | Secondary text on cream |

## Usage

Use cream for most of the screen, espresso for readable content and primary actions, and copper sparingly for brand details. Place the transparent logo on a light background. Preserve each PNG's aspect ratio.

For normal-sized text, use espresso or mocha on cream. Use cream text on espresso or dark copper buttons. Reserve copper on cream for decorative details and large display text.

## CSS

Import `colors.css` into the application's stylesheet. It declares brand variables and semantic aliases under `:root`.

```css
body {
  background: var(--salryn-background);
  color: var(--salryn-text);
}

.primary-button {
  background: var(--salryn-primary);
  color: var(--salryn-on-primary);
}
```
