/** A downloaded CSV: the server's file name (when exposed) or the client fallback. */
export interface DownloadedCsv {
  readonly blob: Blob;
  readonly fileName: string;
}

/** Saves a downloaded CSV through a temporary object URL. */
export function saveCsv(file: DownloadedCsv): void {
  const url = URL.createObjectURL(file.blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = file.fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
