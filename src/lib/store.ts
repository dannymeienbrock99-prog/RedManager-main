import { fs, path } from "@tauri-apps/api";
import { get, writable } from "svelte/store";

export const gameExePath = writable("");
export const isPathValid = writable(false);
export const isDotnetInstalled = writable(false);
export const isDotnetSdkInstalled = writable(false);

export const processing = writable(false);
export const processName = writable("");
export const processProgress = writable(0);

export async function getDirectoryPath(): Promise<string> {
  const executable = get(gameExePath);
  if (!executable) {
    throw new Error("Es wurde keine SonsOfTheForest.exe ausgewählt.");
  }
  return path.dirname(executable);
}

async function ensureDirectory(name: string): Promise<string> {
  const directory = await path.join(await getDirectoryPath(), name);
  if (!(await fs.exists(directory))) {
    await fs.createDir(directory, { recursive: true });
  }
  return directory;
}

export const getModsDir = (): Promise<string> => ensureDirectory("Mods");
export const getLibsDir = (): Promise<string> => ensureDirectory("Libs");
