import { renderMath } from '../src/ui/math';

test('KaTeX draws a formula PhoneNotes marked up, replacing the plain text it read before', () => {
  document.body.innerHTML = '<div id="root"><span class="math" data-tex="x^2">x²</span></div>';
  const root = document.getElementById('root')!;
  renderMath(root);
  const span = root.querySelector('.math')!;
  expect(span.querySelector('.katex')).not.toBeNull();
});

test('a display block gets KaTeX\'s display mode', () => {
  document.body.innerHTML = '<div id="root"><div class="math display" data-tex="a+b">a + b</div></div>';
  const root = document.getElementById('root')!;
  renderMath(root);
  expect(root.querySelector('.katex-display')).not.toBeNull();
});

test('a formula KaTeX cannot parse leaves the plain text alone instead of throwing', () => {
  document.body.innerHTML = '<div id="root"><span class="math" data-tex="\\notacommand{x}">notacommand(x)</span></div>';
  const root = document.getElementById('root')!;
  expect(() => renderMath(root)).not.toThrow();
});

test('an element with no data-tex is left alone', () => {
  document.body.innerHTML = '<div id="root"><span class="math">plain</span></div>';
  const root = document.getElementById('root')!;
  renderMath(root);
  expect(root.querySelector('.math')!.textContent).toBe('plain');
});
