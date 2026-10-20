/** A bullet; a term renders bold before its text ("Account information: …"). */
export type LegalListItem = string | { readonly term: string; readonly text: string };

export type LegalBlock =
  | { readonly kind: 'paragraph'; readonly text: string }
  | { readonly kind: 'list'; readonly items: readonly LegalListItem[] };

export interface LegalSection {
  /** Anchor id; also the table of contents target. */
  readonly id: string;
  readonly title: string;
  readonly blocks: readonly LegalBlock[];
}

/** One public legal page (Privacy, Terms): hero, highlighted notice, sections and closing card. */
export interface LegalDocument {
  readonly title: string;
  readonly lead: string;
  readonly highlight: string;
  readonly sections: readonly LegalSection[];
  readonly contact: { readonly title: string; readonly text: string; readonly subject: string };
}
