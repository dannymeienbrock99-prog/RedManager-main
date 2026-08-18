import { dialog } from "@tauri-apps/api";
import { exists } from "@tauri-apps/api/fs";
import { invoke } from "@tauri-apps/api/tauri";
import { download } from "./nativeDownload";
import { processName, processProgress } from "./store";
import { TempFileCache } from "./tempFileCache";

export async function unzip(sourcePath: string, destinationPath: string): Promise<void> {
  const source = sourcePath.replace(/\\/g, "/");
  const destination = destinationPath.replace(/\\/g, "/");
  if (!(await exists(source))) {
    throw new Error("Die heruntergeladene Archivdatei existiert nicht.");
  }
  if (!(await exists(destination))) {
    throw new Error("Der Zielordner existiert nicht.");
  }

  await invoke("unzip_handler", { source, destination });
}

export async function downloadAndInstall(
  destination: string,
  downloadUrl: string,
  downloadName: string,
): Promise<void> {
  if (!downloadUrl) {
    throw new Error(`Für ${downloadName} wurde keine Downloadadresse gefunden.`);
  }

  processName.set(`${downloadName} wird heruntergeladen …`);
  processProgress.set(0);
  const temporaryPath = await TempFileCache.createFile();

  try {
    let downloadedBytes = 0;
    await download(downloadUrl, temporaryPath, (progress, total) => {
      downloadedBytes += progress;
      processProgress.set(total > 0 ? Math.min(100, (downloadedBytes / total) * 100) : 0);
    });

    processName.set(`${downloadName} wird entpackt …`);
    await unzip(temporaryPath, destination);
    processProgress.set(100);
  } finally {
    await TempFileCache.clearCache();
  }
}

export async function showMessageBox(title: string, message: string): Promise<void> {
  await dialog.message(message, { title });
}

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
