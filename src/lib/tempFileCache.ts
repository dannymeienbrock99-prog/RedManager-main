import { fs, path } from "@tauri-apps/api";

export class TempFileCache {
  private static cache: string[] = [];

  public static async createFile(): Promise<string> {
    const cacheDirectory = await path.appCacheDir();
    if (!(await fs.exists(cacheDirectory))) {
      await fs.createDir(cacheDirectory, { recursive: true });
    }

    const temporaryPath = await path.join(
      cacheDirectory,
      `${Date.now()}-${Math.random().toString(16).slice(2)}.tmp`,
    );
    this.cache.push(temporaryPath);
    return temporaryPath;
  }

  public static async clearCache(): Promise<void> {
    const paths = [...this.cache];
    this.cache = [];
    for (const filePath of paths) {
      try {
        if (await fs.exists(filePath)) {
          await fs.removeFile(filePath);
        }
      } catch (error) {
        console.warn(`Temporäre Datei ${filePath} konnte nicht gelöscht werden.`, error);
      }
    }
  }
}
