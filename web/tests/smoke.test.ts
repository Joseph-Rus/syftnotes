test('the test runner runs in a browser-like place', () => {
  expect(typeof document.createElement).toBe('function');
});
