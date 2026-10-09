/** Starts a browser download of a blob through a temporary anchor. */
export function downloadBlob(doc: Document, blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = doc.createElement('a');
  link.href = url;
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(url);
}

/**
 * Opens a PDF blob in a new tab; falls back to a download when the browser blocks the tab.
 * The object URL outlives the call so the new tab can finish loading it.
 */
export function openBlob(doc: Document, blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const opened = doc.defaultView?.open(url, '_blank') ?? null;
  if (opened === null) {
    URL.revokeObjectURL(url);
    downloadBlob(doc, blob, fileName);
    return;
  }
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
