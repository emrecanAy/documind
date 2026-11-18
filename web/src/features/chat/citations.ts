/** Turns "[2]" citation markers into markdown links (#cite-2) that the renderer shows as clickable chips. */
export function linkifyCitations(text: string) {
  return text.replace(/\[(\d{1,2})\](?!\()/g, '[$1](#cite-$1)')
}
