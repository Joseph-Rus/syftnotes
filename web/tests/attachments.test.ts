import { kindWord } from '../src/screens/attachments';

test('a file is named by what it is, in a word', () => {
  expect(kindWord('application/pdf')).toBe('PDF');
  expect(kindWord('image/heic')).toBe('Photo');
  expect(kindWord('application/vnd.openxmlformats-officedocument.presentationml.presentation')).toBe('Slides');
  expect(kindWord('application/msword')).toBe('Document');
  expect(kindWord('application/octet-stream')).toBe('File');
});
