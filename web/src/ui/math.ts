import katex from 'katex';

/**
 * Runs KaTeX over every `.math[data-tex]` element PhoneNotes.Render left in a lecture's rendered notes (the plain
 * text already there reads fine before this runs, and stays if KaTeX throws on a formula it can't parse).
 */
export function renderMath(root: HTMLElement) {
  for (const el of root.querySelectorAll<HTMLElement>('.math[data-tex]')) {
    const tex = el.dataset.tex;
    if (tex === undefined) continue;
    try {
      katex.render(tex, el, { displayMode: el.classList.contains('display'), throwOnError: false, output: 'html' });
    } catch {
      // the plain text PhoneNotes wrote is left as it was
    }
  }
}
