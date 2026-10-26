import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

const readStyle = (path: string) => readFileSync(resolve('src', path), 'utf8');

describe('styles des champs selon le thème', () => {
  it('laisse Fluent UI gérer les couleurs des champs hors connexion', () => {
    const globalStyles = readStyle('App.css');

    expect(globalStyles).not.toMatch(/\.fui-Input(?:\s|\{|\b)/);
  });

  it('conserve les champs de connexion blancs avec un texte sombre', () => {
    const loginStyles = readStyle('App.module.css');

    expect(loginStyles).toMatch(
      /\.fullWidthInput\s*\{[\s\S]*?background-color:\s*#ffffff\s*!important;[\s\S]*?color:\s*#1a1a1a\s*!important;/,
    );
    expect(loginStyles).toMatch(
      /\.fullWidthInput\s+input\s*\{[\s\S]*?background-color:\s*#ffffff\s*!important;[\s\S]*?color:\s*#1a1a1a\s*!important;[\s\S]*?caret-color:\s*#1a1a1a\s*!important;/,
    );
    expect(loginStyles).toMatch(
      /\.fullWidthInput\s+input::placeholder\s*\{[\s\S]*?color:\s*#777777\s*!important;/,
    );
  });
});
