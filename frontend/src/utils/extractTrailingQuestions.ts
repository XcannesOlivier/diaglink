/**
 * Extracts trailing question lines (e.g. suggested follow-up questions) from
 * the end of an assistant response, so they can be rendered as clickable prompts
 * instead of duplicated as plain text.
 */

// Matches a heading line introducing the trailing question block (e.g. "**Questions suggérées :**").
const HEADING_PATTERN = /^#{0,6}\s*\**\s*(questions?( suggérées| suggestions)?|suggested questions|follow-?up questions)\s*:?\s*\**$/i;

export interface TrailingQuestionsResult {
  questions: string[];
  contentWithoutQuestions: string;
}

export function extractTrailingQuestions(content: string, maxQuestions = 3): TrailingQuestionsResult {
  if (!content) return { questions: [], contentWithoutQuestions: content };

  const rawLines = content.split(/\r?\n/);

  // Skip trailing blank lines.
  let i = rawLines.length - 1;
  while (i >= 0 && rawLines[i].trim() === '') i--;

  // Collect a contiguous block of question lines from the bottom up.
  const questions: string[] = [];
  let blockStart = i + 1;
  while (i >= 0 && questions.length < maxQuestions) {
    const trimmed = rawLines[i].trim();
    if (trimmed === '') break;

    const cleaned = trimmed.replace(/^[-*\d.)\s]+/, '').trim();
    if (!cleaned.endsWith('?') || cleaned.length < 8) break;

    questions.unshift(cleaned);
    blockStart = i;
    i--;
  }

  if (questions.length === 0) {
    return { questions: [], contentWithoutQuestions: content };
  }

  // Also strip an optional heading line (and blank line) right above the question list.
  let cutIndex = blockStart;
  let j = blockStart - 1;
  while (j >= 0 && rawLines[j].trim() === '') j--;
  if (j >= 0 && HEADING_PATTERN.test(rawLines[j].trim())) {
    cutIndex = j;
  }

  const contentWithoutQuestions = rawLines.slice(0, cutIndex).join('\n').replace(/\s+$/, '');

  return { questions: Array.from(new Set(questions)), contentWithoutQuestions };
}
