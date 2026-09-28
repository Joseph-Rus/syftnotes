import { damp, progress, Pull, PULL_THRESHOLD } from '../src/ui/pull';

test('the pull follows the finger less the further it goes, and never goes up', () => {
  expect(damp(-20)).toBe(0);
  expect(damp(40)).toBeLessThan(40);
  expect(damp(400) - damp(300)).toBeLessThan(damp(100) - damp(0));
  expect(damp(10_000)).toBeLessThanOrEqual(140);
});

test('a long enough pull from the top refreshes', () => {
  const p = new Pull();
  p.start(100, 0);
  expect(p.move(300, 0)).toBeGreaterThanOrEqual(PULL_THRESHOLD);
  expect(p.active).toBe(true);
  expect(p.end()).toBe(true);
});

test('a short pull springs back without refreshing', () => {
  const p = new Pull();
  p.start(100, 0);
  p.move(130, 0);
  expect(p.end()).toBe(false);
});

test('a touch that starts down the page is a scroll, not a pull', () => {
  const p = new Pull();
  p.start(100, 250);
  expect(p.move(400, 250)).toBe(0);
  expect(p.end()).toBe(false);
});

test('once the page scrolls, the pull is off for that touch', () => {
  const p = new Pull();
  p.start(100, 0);
  p.move(150, 0);
  expect(p.move(400, 10)).toBe(0);
  expect(p.end()).toBe(false);
});

test("the spinner's turn goes from nothing to whole at the threshold", () => {
  expect(progress(0)).toBe(0);
  expect(progress(PULL_THRESHOLD / 2)).toBe(0.5);
  expect(progress(PULL_THRESHOLD * 3)).toBe(1);
});
