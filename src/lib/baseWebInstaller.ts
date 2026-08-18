import { download } from "./nativeDownload";
import { BaseZipInstaller } from "./baseZipInstaller";
import { getDirectoryPath, processName, processProgress } from "./store";
import { TempFileCache } from "./tempFileCache";

export abstract class BaseWebInstaller extends BaseZipInstaller {
  protected constructor(name: string) {
    super(name);
  }

  public async install(): Promise<void> {
    const version = await this.getTargetVersion();
    if (!version) {
      throw new Error(`Für ${this.getName()} konnte kein aktuelles Release gefunden werden.`);
    }

    const destination = await getDirectoryPath();
    const downloadUrl = await this.getDownloadUrl(version);
    const temporaryPath = await TempFileCache.createFile();

    try {
      processName.set(`${this.getName()} ${version} wird heruntergeladen …`);
      processProgress.set(0);
      let downloadedBytes = 0;
      await download(downloadUrl, temporaryPath, (progress, total) => {
        downloadedBytes += progress;
        processProgress.set(total > 0 ? Math.min(100, (downloadedBytes / total) * 100) : 0);
      });

      processName.set(`${this.getName()} wird installiert …`);
      await this.unzip(temporaryPath, destination);
      processProgress.set(100);
    } finally {
      await TempFileCache.clearCache();
    }
  }

  protected abstract getDownloadUrl(version: string): Promise<string>;
}
