import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

const readViewerStyles = (fileName: string) => readFileSync(
  resolve(process.cwd(), 'src', 'components', 'chat', fileName),
  'utf8',
);

describe.each([
  'TechnicalSourcePdfDialog.module.css',
  'TechnicalVisualGallery.module.css',
])('%s', fileName => {
  it('overrides the Fluent surface size at desktop and tablet breakpoints', () => {
    const styles = readViewerStyles(fileName);
    expect(styles).toMatch(/width:\s*90dvw\s*!important/);
    expect(styles).toMatch(/height:\s*90dvh\s*!important/);
    expect(styles).toMatch(/max-width:\s*none\s*!important/);
    expect(styles).toMatch(/width:\s*95dvw\s*!important/);
    expect(styles).toMatch(/height:\s*95dvh\s*!important/);
  });

  it('clips the title container and ellipsizes its text', () => {
    const styles = readViewerStyles(fileName);
    expect(styles).toMatch(/\.title\s*\{[\s\S]*?max-width:\s*100%;[\s\S]*?overflow:\s*hidden;/);
    expect(styles).toMatch(/\.titleText\s*\{[\s\S]*?text-overflow:\s*ellipsis;[\s\S]*?white-space:\s*nowrap;/);
  });
});
