import React, { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ConversationSidebar, formatDate } from '../ConversationSidebar';

const localDate = (year: number, month: number, day: number, hour: number, minute: number, second = 0) =>
  new Date(year, month - 1, day, hour, minute, second);

const unixSeconds = (date: Date) => Math.floor(date.getTime() / 1000);

describe('ConversationSidebar formatDate', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(localDate(2026, 10, 8, 14, 0));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("formats a conversation from today as Aujourd'hui", () => {
    expect(formatDate(unixSeconds(localDate(2026, 10, 8, 9, 0)))).toBe("Aujourd'hui");
  });

  it('formats a conversation from yesterday as Hier', () => {
    expect(formatDate(unixSeconds(localDate(2026, 10, 7, 18, 26)))).toBe('Hier');
  });

  it('uses the previous local calendar day just after midnight', () => {
    vi.setSystemTime(localDate(2026, 10, 8, 0, 15));

    expect(formatDate(unixSeconds(localDate(2026, 10, 7, 23, 50)))).toBe('Hier');
  });

  it("keeps an early conversation in Aujourd'hui late on the same day", () => {
    vi.setSystemTime(localDate(2026, 10, 8, 23, 50));

    expect(formatDate(unixSeconds(localDate(2026, 10, 8, 0, 5)))).toBe("Aujourd'hui");
  });

  it('switches from today to yesterday at the local day boundary', () => {
    const timestamp = unixSeconds(localDate(2026, 10, 7, 23, 59, 59));

    vi.setSystemTime(localDate(2026, 10, 7, 23, 59, 59));
    expect(formatDate(timestamp)).toBe("Aujourd'hui");

    vi.setSystemTime(localDate(2026, 10, 8, 0, 0));
    expect(formatDate(timestamp)).toBe('Hier');
  });

  it('keeps the relative day format for recent older conversations', () => {
    expect(formatDate(unixSeconds(localDate(2026, 10, 5, 18, 0)))).toBe('Il y a 3 jours');
  });

  it('uses the explicit French locale for older conversations', () => {
    expect(formatDate(unixSeconds(localDate(2026, 9, 30, 18, 0)))).toBe('30/09/2026');
  });
});

describe('ConversationSidebar activity date', () => {
  let host: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(localDate(2026, 10, 8, 14, 0));
    host = document.createElement('div');
    document.body.append(host);
    root = createRoot(host);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    host.remove();
    vi.useRealTimers();
  });

  const renderConversation = async (createdAt: number, lastActivityAt: number) => {
    await act(async () => root.render(React.createElement(ConversationSidebar, {
      isOpen: true,
      onOpenChange: vi.fn(),
      conversations: [{ id: 'conversation-1', title: 'Conversation test', createdAt, lastActivityAt }],
      isLoading: false,
      hasMore: false,
      currentConversationId: null,
      onSelectConversation: vi.fn(),
      onNewChat: vi.fn(),
      onDeleteConversation: vi.fn(),
      onLoadMore: vi.fn(),
      onCollapse: vi.fn(),
      canCollapse: false,
    })));
  };

  it("displays today's last activity for a conversation created earlier", async () => {
    await renderConversation(
      unixSeconds(localDate(2026, 10, 1, 9, 0)),
      unixSeconds(localDate(2026, 10, 8, 9, 0)),
    );

    expect(document.body.textContent).toContain("Aujourd'hui");
    expect(document.body.textContent).not.toContain('01/10/2026');
  });

  it('displays yesterday from lastActivityAt even when createdAt is today', async () => {
    await renderConversation(
      unixSeconds(localDate(2026, 10, 8, 9, 0)),
      unixSeconds(localDate(2026, 10, 7, 18, 26)),
    );

    expect(document.body.textContent).toContain('Hier');
    expect(document.body.textContent).not.toContain("Aujourd'hui");
  });
});
