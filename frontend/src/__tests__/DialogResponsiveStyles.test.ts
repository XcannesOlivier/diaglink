import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

describe('styles mobiles des modales', () => {
  const css = readFileSync(resolve('src/index.css'), 'utf8');
  const mobileRules = css.match(/@media \(max-width: 767px\) \{[\s\S]*$/)?.[0] ?? '';

  it('ancre les croix au coin de la modale avec une cible tactile uniforme', () => {
    expect(css).toMatch(
      /\.fui-DialogSurface \[data-dialog-close-button\]\s*\{[\s\S]*?position:\s*absolute;[\s\S]*?top:\s*16px;[\s\S]*?right:\s*16px;/,
    );
    expect(css).toMatch(
      /\.fui-DialogSurface \[data-dialog-close-button\]\s*\{[\s\S]*?width:\s*44px;[\s\S]*?height:\s*44px;/,
    );
  });

  it('reserve au titre la place necessaire pour ne pas chevaucher la croix', () => {
    expect(css).toMatch(
      /\.fui-DialogSurface \.fui-DialogTitle\s*\{[\s\S]*?padding-right:\s*60px;[\s\S]*?overflow-wrap:\s*anywhere;/,
    );
  });

  it('conserve une marge horizontale de 16 px sous 768 px', () => {
    expect(mobileRules).toMatch(/\.fui-DialogSurface\s*\{[\s\S]*?width:\s*calc\(100vw - 32px\)\s*!important;/);
    expect(mobileRules).toMatch(/max-width:\s*calc\(100vw - 32px\)\s*!important;/);
    expect(mobileRules).toMatch(/margin-inline:\s*auto;/);
  });

  it('rend les modales hautes accessibles sans débordement horizontal', () => {
    expect(mobileRules).toMatch(/max-height:\s*calc\(100dvh - 32px\)\s*!important;/);
    expect(mobileRules).toMatch(/overflow-x:\s*hidden;/);
    expect(mobileRules).toMatch(/\.fui-DialogSurface\s*\{[\s\S]*?overflow-y:\s*hidden;/);
    expect(mobileRules).toMatch(/\.fui-DialogSurface \.fui-DialogContent\s*\{[\s\S]*?overflow-y:\s*auto;/);
  });

  it('autorise le repli des contenus et actions trop larges', () => {
    expect(mobileRules).toMatch(/\.fui-DialogSurface \.fui-DialogActions\s*\{\s*flex-wrap:\s*wrap;/);
    expect(mobileRules).toMatch(/\.fui-DialogSurface \.fui-DialogActions \.fui-Button\s*\{[\s\S]*?white-space:\s*normal;/);
    expect(mobileRules).toMatch(/\.fui-DialogSurface \.fui-DialogTitle,[\s\S]*?overflow-wrap:\s*anywhere;/);
  });
});
