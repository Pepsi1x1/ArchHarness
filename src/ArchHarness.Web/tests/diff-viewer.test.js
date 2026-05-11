import { describe, expect, it } from 'vitest';
import {
  createGitDiffMessageView,
  createSideBySideDiffView,
  getGitChangeStatusClass,
  parseUnifiedDiff,
  setGitDiffPreviewContent
} from '../wwwroot/js/diff-viewer.js';

const sampleDiff = `diff --git a/src/app.js b/src/app.js
index 111..222 100644
--- a/src/app.js
+++ b/src/app.js
@@ -1,3 +1,4 @@
 const name = 'ArchHarness';
-console.log(name);
+console.info(name);
+console.info('ready');
 export default name;
`;

describe('diff viewer', () => {
  it('normalizes status text into CSS-safe classes', () => {
    expect(getGitChangeStatusClass('Renamed / Modified')).toBe('renamed-modified');
    expect(getGitChangeStatusClass()).toBe('modified');
  });

  it('parses unified diff files, headers, and hunks', () => {
    const sections = parseUnifiedDiff(sampleDiff);

    expect(sections).toHaveLength(1);
    expect(sections[0].headerLines[0]).toContain('diff --git');
    expect(sections[0].hunks[0].header).toBe('@@ -1,3 +1,4 @@');
    expect(sections[0].hunks[0].lines).toContain("+console.info('ready');");
  });

  it('renders side-by-side rows for context, modifications, and additions', () => {
    const view = createSideBySideDiffView(sampleDiff);

    expect(view.className).toBe('git-diff-side-by-side');
    expect(view.querySelector('.git-diff-section-header').textContent).toBe('+++ b/src/app.js');
    expect(view.querySelector('.git-diff-hunk-header').textContent).toBe('@@ -1,3 +1,4 @@');
    expect(view.querySelectorAll('.git-diff-row-context')).toHaveLength(2);
    expect(view.querySelectorAll('.git-diff-row-modify')).toHaveLength(2);
    expect(view.querySelectorAll('.git-diff-cell-modify-add')).toHaveLength(2);
    expect([...view.querySelectorAll('.git-diff-line-content')].map(node => node.textContent)).toContain("console.info('ready');");
  });

  it('shows a readable message for non-textual diffs', () => {
    const view = createSideBySideDiffView('Binary files differ');

    expect(view.className).toBe('git-diff-empty');
    expect(view.textContent).toBe('Binary files differ');
  });

  it('replaces preview content atomically', () => {
    const preview = document.createElement('div');
    preview.append(document.createElement('span'));
    setGitDiffPreviewContent(preview, createGitDiffMessageView('No diff'));

    expect(preview.children).toHaveLength(1);
    expect(preview.firstElementChild.className).toBe('git-diff-empty');
    expect(preview.textContent).toBe('No diff');
  });
});