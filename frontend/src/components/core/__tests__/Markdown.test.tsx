import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { Markdown } from '../Markdown';

describe('Markdown links', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
  });

  it('renders sandbox links as inert text', async () => {
    await act(async () => root.render(
      <Markdown content="[rapport](sandbox:/mnt/data/rapport.pdf)" />,
    ));

    expect(container.textContent).toContain('rapport');
    expect(container.querySelector('a')).toBeNull();
  });

  it('keeps ordinary links external and protected against opener access', async () => {
    await act(async () => root.render(
      <Markdown content="[source](https://example.com/document)" />,
    ));

    const link = container.querySelector<HTMLAnchorElement>('a');
    expect(link?.href).toBe('https://example.com/document');
    expect(link?.target).toBe('_blank');
    expect(link?.rel).toBe('noopener noreferrer');
  });
});
