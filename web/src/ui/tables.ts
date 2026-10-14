/**
 * Puts each table in a lecture's rendered notes (or an assignment's instructions) in a box of its own that scrolls
 * sideways. A table is never narrower than its longest words, so one with a few columns is wider than a narrow
 * phone: left as it was it ran under the screen's right edge, where its last column couldn't be reached.
 */
export function scrollTables(root: HTMLElement) {
  for (const table of root.querySelectorAll('table')) {
    if (table.parentElement?.classList.contains('table-scroll')) continue;
    const box = document.createElement('div');
    box.className = 'table-scroll';
    table.replaceWith(box);
    box.append(table);
  }
}
