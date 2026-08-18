import { invoke } from "@tauri-apps/api/tauri";
import { appWindow } from "@tauri-apps/api/window";

type ProgressPayload = {
  id: number;
  progress: number;
  total: number;
};

type ProgressHandler = (progress: number, total: number) => void;

const handlers = new Map<number, ProgressHandler>();
let listenerPromise: Promise<() => void> | null = null;

function ensureDownloadListener(): Promise<() => void> {
  listenerPromise ??= appWindow.listen<ProgressPayload>("download://progress", ({ payload }) => {
    handlers.get(payload.id)?.(payload.progress, payload.total);
  });
  return listenerPromise;
}

function createRequestId(): number {
  const values = new Uint32Array(1);
  window.crypto.getRandomValues(values);
  return values[0];
}

/**
 * Downloads a remote file through the native Tauri upload/download plugin.
 * `progress` is the byte count of the latest chunk; `total` is the content length.
 */
export async function download(
  url: string,
  filePath: string,
  progressHandler?: ProgressHandler,
  headers: Record<string, string> = {},
): Promise<void> {
  const id = createRequestId();
  if (progressHandler) {
    handlers.set(id, progressHandler);
  }

  await ensureDownloadListener();
  try {
    await invoke<number>("plugin:upload|download", {
      id,
      url,
      filePath,
      headers,
    });
  } finally {
    handlers.delete(id);
  }
}
