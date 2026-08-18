import { invoke } from "@tauri-apps/api/tauri";

export type BundledModStatus = {
  installed: boolean;
  enabled: boolean;
  version: string | null;
  assemblyPath: string;
  manifestPath: string;
  settingsPath: string;
  statsPath: string;
  overlayUrl: string;
};

export type BundledModBuildResult = {
  success: boolean;
  message: string;
  stdout: string;
  stderr: string;
  buildDirectory: string;
  installedPath: string | null;
};

export const getBundledDeathCounterStatus = (gameExe: string): Promise<BundledModStatus> =>
  invoke("get_bundled_mod_status", { gameExe });

export const buildAndInstallBundledDeathCounter = (
  gameExe: string,
): Promise<BundledModBuildResult> =>
  invoke("build_and_install_bundled_mod", { gameExe });

export const uninstallBundledDeathCounter = (gameExe: string): Promise<BundledModStatus> =>
  invoke("uninstall_bundled_mod", { gameExe });
