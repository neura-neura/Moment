export class TFolder {
  public readonly path = "";
}

export class TFile {
  public readonly path = "";
}

export function normalizePath(path: string): string {
  return path.replaceAll("\\", "/").replace(/\/{2,}/g, "/").replace(/^\.\//, "").replace(/\/$/, "");
}
