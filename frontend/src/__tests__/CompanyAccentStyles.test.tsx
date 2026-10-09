import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

const read = (path: string) => readFileSync(resolve('src', path), 'utf8');

describe('authenticated company accent surfaces', () => {
  it.each([
    ['chat/StarterMessages.module.css', '--colorBrandForeground1'],
    ['chat/DropZone.module.css', '--colorBrandStroke1'],
    ['chat/UserMessage.module.css', '--colorBrandStroke1'],
    ['chat/AssistantMessage.module.css', '--colorBrandForegroundLink'],
    ['core/Markdown.module.css', '--colorBrandForeground1'],
    ['chat/MessageActions.module.css', '--colorBrandStroke1'],
    ['core/BuiltWithBadge.module.css', '--colorBrandForeground1'],
    ['ChatInterface.module.css', '--colorBrandForeground1'],
  ])('uses scoped brand tokens in %s rather than hardcoded brand colors', (path, token) => {
    const css = read(`components/${path}`);
    expect(css).toContain(token);
    expect(css).not.toMatch(/#(?:007BB4|0098D4|0099DE|00A8F4)\b/i);
  });

  it('keeps user-message and chat-input idle surfaces neutral, focusing only enabled, non-error input', () => {
    const input = read('components/chat/ChatInput.module.css');
    expect(input).toContain('border: 1px solid var(--colorNeutralStroke1)');
    expect(input).toContain(".inputWrapper:not([data-disabled]):not(:has([aria-invalid='true'])):focus-within");
    expect(input).toContain('border-color: var(--colorBrandStroke1)');
    expect(read('components/chat/UserMessage.module.css')).toContain('var(--colorNeutralForeground1)');
    expect(read('components/chat/ChatInput.tsx')).toContain('data-disabled={disabled || undefined}');
  });

  it('preserves semantic controls but lets mobile selection inherit the authenticated brand', () => {
    for (const path of ['components/chat/VoiceInput.tsx', 'components/chat/FilePreview.tsx',
      'components/chat/MessageQueue.tsx', 'components/ConversationSidebar.tsx',
      'components/views/UsersView.tsx', 'components/views/MachineSubscriptionDialog.tsx']) {
      expect(read(path)).toContain('data-company-accent-exempt');
    }
    expect(read('components/chat/VoiceInput.module.css')).toContain('var(--colorPaletteRedForeground1)');
    expect(read('components/layout/AppShell.tsx')).not.toContain('data-company-accent-exempt');
    expect(read('components/views/CompanyFinancePanel.module.css')).toContain('--diaglink-status-info-foreground');
    for (const palette of ['Green', 'DarkOrange', 'Red', 'Teal']) {
      expect(read('components/views/CompanyFinancePanel.module.css')).toContain(`--colorPalette${palette}Foreground`);
    }
  });

  it('scopes brand changes to the authenticated provider, never the public/global theme', () => {
    const shell = read('components/layout/AppShell.tsx');
    expect(shell).toContain('<CompanyAccentProvider');
    expect(shell).toContain("currentUser?.companyBranding?.accentColor ?? 'var(--colorBrandForeground1)'");
    expect(read('components/layout/CompanyAccentProvider.tsx')).toContain('theme={theme}');
    for (const path of ['config/themes.ts', 'components/ThemeProvider.tsx', 'index.css']) {
      expect(read(path)).not.toContain('createCompanyBrandTheme');
    }
  });

  it('removes decorative wave blue only with an accent and previews the true default base theme', () => {
    expect(read('components/animations/Waves.tsx')).toContain('if (accentColor)');
    expect(read('components/animations/Waves.tsx')).toContain('channels.join');
    expect(read('components/views/CompanyBrandingView.tsx')).toContain('draftAccent ?? themeStyles.colorBrandForeground1');
  });
});
