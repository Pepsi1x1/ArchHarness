import { describe, expect, it, vi } from 'vitest';
import {
  equalIgnoringCase,
  escapeHtml,
  formatRunTimestamp,
  getSelectDisplayLabel,
  populateSelect,
  readEventField,
  runDateFromId,
  sanitizeHtmlFragment,
  setSanitizedHtml,
  setSelectValue,
  summarizeWorkspacePath,
  timeAgo
} from '../wwwroot/js/utils.js';

describe('utils', () => {
  it('escapes text that will be written as HTML fallback content', () => {
    expect(escapeHtml(`<img src=x onerror="alert('x')">`)).toBe('&lt;img src=x onerror=&quot;alert(&#39;x&#39;)&quot;&gt;');
  });

  it('sanitizes rendered HTML before inserting it into the DOM', () => {
    const fragment = sanitizeHtmlFragment(`
      <article onclick="steal()" style="color:red">
        <a href="javascript:alert(1)" data-safe="yes">Open</a>
        <img src="data:text/html;base64,evil" srcset="/safe.png 1x, javascript:evil 2x">
        <script>evil()</script>
      </article>
    `);
    const host = document.createElement('div');
    host.append(fragment);

    expect(host.querySelector('script')).toBeNull();
    expect(host.querySelector('article')?.hasAttribute('onclick')).toBe(false);
    expect(host.querySelector('article')?.hasAttribute('style')).toBe(false);
    expect(host.querySelector('a')?.getAttribute('href')).toBeNull();
    expect(host.querySelector('a')?.getAttribute('data-safe')).toBe('yes');
    expect(host.querySelector('img')?.getAttribute('src')).toBeNull();
    expect(host.querySelector('img')?.getAttribute('srcset')).toBeNull();
  });

  it('replaces element children with sanitized HTML', () => {
    const host = document.createElement('div');
    host.textContent = 'old';

    setSanitizedHtml(host, '<strong>new</strong><iframe src="/bad"></iframe>');

    expect(host.textContent).toBe('new');
    expect(host.querySelector('strong')).not.toBeNull();
    expect(host.querySelector('iframe')).toBeNull();
  });

  it('formats and summarizes common display values', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-05-11T12:00:00Z'));

    expect(timeAgo(new Date('2026-05-11T11:58:00Z'))).toBe('2m ago');
    expect(formatRunTimestamp('20260511-093012')).toBe('2026-05-11 09:30');
    expect(runDateFromId('20260511-093012')?.getFullYear()).toBe(2026);
    expect(summarizeWorkspacePath('C:\\Users\\dev\\source\\repos\\ArchHarness')).toBe('.../source/repos/ArchHarness');
    expect(equalIgnoringCase('Planning', 'planning')).toBe(true);
    vi.useRealTimers();
  });

  it('reads event fields from camelCase or PascalCase payloads', () => {
    expect(readEventField({ agentId: 'frontend' }, 'agentId')).toBe('frontend');
    expect(readEventField({ AgentId: 'backend' }, 'agentId')).toBe('backend');
    expect(readEventField(null, 'agentId')).toBeNull();
  });

  it('populates selects without overwriting unknown values', () => {
    const select = document.createElement('select');
    populateSelect(select, ['standard', 'planning']);
    setSelectValue(select, 'planning');
    expect(select.value).toBe('planning');
    expect(getSelectDisplayLabel(select)).toBe('planning');

    setSelectValue(select, 'unknown');
    expect(select.value).toBe('planning');
  });
});