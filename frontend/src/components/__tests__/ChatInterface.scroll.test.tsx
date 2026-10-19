import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ChatInterface } from '../ChatInterface';
import type { IChatItem } from '../../types/chat';
import type { AppState } from '../../types/appState';

vi.mock('../animations/Waves', () => ({ Waves: () => null }));
vi.mock('../chat/ChatInput', () => ({ ChatInput: () => <div data-chat-input /> }));
vi.mock('../chat/DropZone', () => ({ DropZone: () => null }));
vi.mock('../chat/StarterMessages', () => ({ StarterMessages: () => null }));
vi.mock('../chat/UserMessage', () => ({
  UserMessage: ({ message }: { message: IChatItem }) => <div data-message-id={message.id} />,
}));
vi.mock('../chat/AssistantMessage', () => ({ AssistantMessage: () => <div data-assistant-message /> }));
vi.mock('../core/BuiltWithBadge', () => ({ BuiltWithBadge: () => null }));

const VIEWPORT_HEIGHT = 500;
const MARGIN = 16;

// Simulated layout of the scrolling messages area (jsdom has no layout engine).
const layout = {
  contentHeight: 0,
  offsets: {} as Record<string, number>,
  scrollTop: 0,
};

const isLog = (el: Element) => el.getAttribute('role') === 'log';
const getLog = () => document.querySelector<HTMLElement>('[role="log"]');
const spacerHeight = () =>
  parseFloat(document.querySelector<HTMLElement>('[data-scroll-anchor-spacer]')?.style.height ?? '0') || 0;
const scrollHeight = () => Math.max(VIEWPORT_HEIGHT, layout.contentHeight + spacerHeight());
const rect = (top: number, bottom: number) => ({ top, bottom, left: 0, right: 0, width: 0, height: bottom - top, x: 0, y: top, toJSON: () => ({}) }) as DOMRect;

const patched = ['clientHeight', 'scrollHeight', 'scrollTop', 'getBoundingClientRect'] as const;
const originals = new Map<string, PropertyDescriptor | undefined>();

const message = (id: string, role: 'user' | 'assistant', content = ''): IChatItem => ({ id, role, content });

describe('ChatInterface auto-scroll', () => {
  let container: HTMLDivElement;
  let root: Root;
  const scrollIntoView = vi.fn();
  const windowScrollTo = vi.fn();

  const render = async (messages: IChatItem[], status: AppState['chat']['status']) => {
    await act(async () => root.render(
      <ChatInterface messages={messages} status={status} error={null} onSendMessage={vi.fn()} disabled={false} streamingMessageId="a2" />,
    ));
  };

  const userScrollTo = async (top: number) => {
    const log = getLog()!;
    log.scrollTop = top;
    await act(async () => { log.dispatchEvent(new Event('scroll')); });
  };

  beforeEach(() => {
    layout.contentHeight = 0;
    layout.offsets = {};
    layout.scrollTop = 0;
    for (const key of patched) originals.set(key, Object.getOwnPropertyDescriptor(HTMLElement.prototype, key));

    Object.defineProperty(HTMLElement.prototype, 'clientHeight', {
      configurable: true,
      get(this: HTMLElement) { return isLog(this) ? VIEWPORT_HEIGHT : 0; },
    });
    Object.defineProperty(HTMLElement.prototype, 'scrollHeight', {
      configurable: true,
      get(this: HTMLElement) { return isLog(this) ? scrollHeight() : 0; },
    });
    Object.defineProperty(HTMLElement.prototype, 'scrollTop', {
      configurable: true,
      get(this: HTMLElement) { return isLog(this) ? layout.scrollTop : 0; },
      set(this: HTMLElement, value: number) {
        if (isLog(this)) layout.scrollTop = Math.min(Math.max(0, value), scrollHeight() - VIEWPORT_HEIGHT);
      },
    });
    Object.defineProperty(HTMLElement.prototype, 'getBoundingClientRect', {
      configurable: true,
      value(this: HTMLElement) {
        if (isLog(this)) return rect(0, VIEWPORT_HEIGHT);
        const id = this.dataset.messageId;
        if (id !== undefined) {
          const top = (layout.offsets[id] ?? 0) - layout.scrollTop;
          return rect(top, top + 50);
        }
        if (this.parentElement && isLog(this.parentElement) && !this.hasAttribute('data-scroll-anchor-spacer')) {
          return rect(-layout.scrollTop, layout.contentHeight - layout.scrollTop);
        }
        return rect(0, 0);
      },
    });

    Object.defineProperty(Element.prototype, 'scrollIntoView', { configurable: true, value: scrollIntoView });
    vi.stubGlobal('scrollTo', windowScrollTo);
    vi.stubGlobal('ResizeObserver', class { observe() {} unobserve() {} disconnect() {} });
    scrollIntoView.mockClear();
    windowScrollTo.mockClear();

    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
    vi.unstubAllGlobals();
    Reflect.deleteProperty(Element.prototype, 'scrollIntoView');
    for (const key of patched) {
      const original = originals.get(key);
      if (original) Object.defineProperty(HTMLElement.prototype, key, original);
      else Reflect.deleteProperty(HTMLElement.prototype, key);
    }
  });

  const history = [message('u1', 'user', 'Q1'), message('a1', 'assistant', 'R1')];

  it('shows the end of a loaded conversation', async () => {
    layout.contentHeight = 1200;
    await render(history, 'idle');
    expect(layout.scrollTop).toBe(700);
    expect(spacerHeight()).toBe(0);
  });

  it('places a newly sent question at the top of the chat area', async () => {
    layout.contentHeight = 1200;
    await render(history, 'idle');

    layout.contentHeight = 1300;
    layout.offsets.u2 = 1100;
    await render([...history, message('u2', 'user', 'Q2')], 'sending');

    expect(layout.scrollTop).toBe(1100 - MARGIN);
    expect(spacerHeight()).toBe(VIEWPORT_HEIGHT - (1300 - 1100) - MARGIN);
    expect(scrollIntoView).not.toHaveBeenCalled();
    expect(windowScrollTo).not.toHaveBeenCalled();
  });

  it('keeps the question in place while the answer fits, then follows the stream', async () => {
    layout.contentHeight = 1200;
    await render(history, 'idle');
    layout.contentHeight = 1300;
    layout.offsets.u2 = 1100;
    const sent = [...history, message('u2', 'user', 'Q2')];
    await render(sent, 'sending');

    layout.contentHeight = 1500;
    await render([...sent, message('a2', 'assistant', 'Recherche…')], 'streaming');
    expect(layout.scrollTop).toBe(1100 - MARGIN);

    layout.contentHeight = 2000;
    await render([...sent, message('a2', 'assistant', 'Recherche… réponse longue')], 'streaming');
    expect(spacerHeight()).toBe(0);
    expect(layout.scrollTop).toBe(2000 - VIEWPORT_HEIGHT);
  });

  it('does not force scrolling while the user reads older messages, and re-enables it on the next question', async () => {
    layout.contentHeight = 1200;
    await render(history, 'idle');
    layout.contentHeight = 1300;
    layout.offsets.u2 = 1100;
    const sent = [...history, message('u2', 'user', 'Q2')];
    await render(sent, 'sending');

    await userScrollTo(200);
    layout.contentHeight = 2500;
    const answered = [...sent, message('a2', 'assistant', 'Longue réponse')];
    await render(answered, 'streaming');
    expect(layout.scrollTop).toBe(200);
    expect(container.querySelector('[aria-label="Défiler vers les nouveaux messages"]')).not.toBeNull();

    await render(answered, 'idle');
    layout.contentHeight = 2600;
    layout.offsets.u3 = 2500;
    await render([...answered, message('u3', 'user', 'Q3')], 'sending');
    expect(layout.scrollTop).toBe(2500 - MARGIN);
    expect(container.querySelector('[aria-label="Défiler vers les nouveaux messages"]')).toBeNull();
  });

  it('resets the anchor and shows the end when switching conversation', async () => {
    layout.contentHeight = 1200;
    await render(history, 'idle');
    layout.contentHeight = 1300;
    layout.offsets.u2 = 1100;
    await render([...history, message('u2', 'user', 'Q2')], 'sending');
    await userScrollTo(0);

    layout.contentHeight = 900;
    await render([message('c2-0', 'user', 'Autre'), message('c2-1', 'assistant', 'Réponse')], 'idle');
    expect(spacerHeight()).toBe(0);
    expect(layout.scrollTop).toBe(400);
  });
});
