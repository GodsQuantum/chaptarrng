const BIDI_CONTROL_CHARACTERS = /[\u061C\u200E\u200F\u202A-\u202E\u2066-\u206F]/g;

// Bidi controls from external values must not be able to close this isolate or
// reorder the application-authored text that follows it. This changes only the
// rendered value; callers retain the original API/history data.
export default function isolateBidiText(value) {
  if (typeof value !== 'string' || !value) {
    return value;
  }

  const sanitizedValue = value.replace(BIDI_CONTROL_CHARACTERS, '');

  return sanitizedValue ? `\u2068${sanitizedValue}\u2069` : '';
}
