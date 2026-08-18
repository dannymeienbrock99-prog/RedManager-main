import { fs, path } from "@tauri-apps/api";
import { invoke } from "@tauri-apps/api/tauri";
import semver from "semver";
import { get } from "svelte/store";
import type { BaseInstaller } from "./baseInstaller";
import { BaseUninstaller } from "./baseUninstaller";
import { GithubInstaller } from "./githubInstaller";
import { redLoaderInfo, unityExplorerInfo } from "./githubInfo";
import { gameExePath, getDirectoryPath, processName } from "./store";

export enum InstallMode {
  Install,
  Uninstall,
  Update,
}

export enum VersionResult {
  Equal,
  Greater,
  Lesser,
}

function validVersion(value: string): string | null {
  return semver.valid(value) ?? semver.valid(semver.coerce(value));
}

export class FeatureInstaller {
  private readonly installer: BaseInstaller | null;
  private readonly uninstaller: BaseUninstaller;
  private readonly versionCheckPath: string | null;

  public currentMode = InstallMode.Install;
  public currentModeState = "Install";
  public description: string | null = null;
  public expectedMode: InstallMode | null = null;
  public additionalFoldersToCreate: string[] | null = null;

  public constructor(
    installer: BaseInstaller | null,
    uninstaller: BaseUninstaller,
    versionCheckPath: string | null = null,
  ) {
    this.installer = installer;
    this.uninstaller = uninstaller;
    this.versionCheckPath = versionCheckPath;
  }

  public async install(): Promise<void> {
    if (!this.installer) {
      return;
    }

    // Install over the existing version. This keeps a working installation intact
    // when a download fails before extraction starts.
    processName.set(`${this.getName()} wird installiert …`);
    await this.installer.install();

    for (const folder of this.additionalFoldersToCreate ?? []) {
      const directory = await path.join(await getDirectoryPath(), folder);
      if (!(await fs.exists(directory))) {
        await fs.createDir(directory, { recursive: true });
      }
    }
  }

  public async uninstall(): Promise<void> {
    processName.set(`${this.getName()} wird entfernt …`);
    await this.uninstaller.uninstall();
  }

  public async handle(mode: InstallMode): Promise<void> {
    if (mode === InstallMode.Uninstall) {
      await this.uninstall();
    } else {
      await this.install();
    }
    await this.refreshMode();
  }

  public handleCurrentMode(): Promise<void> {
    return this.handle(this.currentMode);
  }

  public getName(): string {
    return this.installer?.getName() ?? this.uninstaller.getName();
  }

  public getMode(): InstallMode {
    return this.currentMode;
  }

  private setMode(mode: InstallMode): void {
    this.currentMode = mode;
    this.currentModeState = this.getModeString();
  }

  public getModeString(): string {
    switch (this.currentMode) {
      case InstallMode.Install:
        return "Install";
      case InstallMode.Update:
        return "Update";
      case InstallMode.Uninstall:
        return "Uninstall";
      default:
        return "Install";
    }
  }

  public async refreshMode(): Promise<void> {
    if (!(await this.uninstaller.isInstalled())) {
      this.setMode(InstallMode.Install);
      return;
    }

    this.setMode((await this.checkRemoteVersion()) === VersionResult.Greater
      ? InstallMode.Update
      : InstallMode.Uninstall);
  }

  public async checkRemoteVersion(): Promise<VersionResult | null> {
    if (!this.versionCheckPath || !this.installer) {
      return null;
    }

    const localVersion = await this.getLocalVersion();
    const remoteVersion = await this.installer.getTargetVersion();
    if (!localVersion || !remoteVersion) {
      return null;
    }

    const local = validVersion(localVersion);
    const remote = validVersion(remoteVersion);
    if (!local || !remote) {
      return localVersion === remoteVersion ? VersionResult.Equal : null;
    }

    if (semver.gt(remote, local)) return VersionResult.Greater;
    if (semver.lt(remote, local)) return VersionResult.Lesser;
    return VersionResult.Equal;
  }

  private async getLocalVersion(): Promise<string | null> {
    if (!this.versionCheckPath) {
      return null;
    }

    try {
      const executableDirectory = await path.dirname(get(gameExePath));
      const filePath = await path.join(executableDirectory, this.versionCheckPath);
      return await invoke<string>("get_file_version", { path: filePath });
    } catch (error) {
      console.warn(`Lokale Version von ${this.getName()} konnte nicht gelesen werden.`, error);
      return null;
    }
  }

  public async canDoAction(): Promise<boolean> {
    return this.expectedMode === null || this.expectedMode === this.currentMode;
  }

  public async getRemoteVersionString(withPrefix: boolean): Promise<string | null> {
    const version = await this.installer?.getTargetVersion();
    return version ? (withPrefix ? `${this.getName()} ${version}` : version) : null;
  }
}

const loaderUninstaller = new BaseUninstaller(
  ["_RedLoader"],
  ["dobby.dll", "version.dll"],
  "RedLoader",
);
const loaderInstaller = new GithubInstaller(redLoaderInfo, "RedLoader");

const unityExplorerUninstaller = new BaseUninstaller(
  ["Mods\\sinai-dev-UnityExplorer", "Mods\\UnityExplorer"],
  ["Mods\\UnityExplorer.dll", "Mods\\UnityExplorer.disabled"],
  "UnityExplorer",
);
const unityExplorerInstaller = new GithubInstaller(unityExplorerInfo, "UnityExplorer");

export const loaderFeature = new FeatureInstaller(
  loaderInstaller,
  loaderUninstaller,
  "_RedLoader\\net6\\RedLoader.dll",
);
loaderFeature.additionalFoldersToCreate = ["Mods", "Libs"];

export const ueFeature = new FeatureInstaller(unityExplorerInstaller, unityExplorerUninstaller);
ueFeature.description = "UnityExplorer analysiert und verändert Unity-Objekte zur Laufzeit. Nur für Mod-Entwicklung verwenden.";

const bepInExUninstaller = new BaseUninstaller(["BepInEx"], ["winhttp.dll"], "BepInEx");
export const bieFeature = new FeatureInstaller(null, bepInExUninstaller);
bieFeature.expectedMode = InstallMode.Uninstall;

const melonUninstaller = new BaseUninstaller(
  ["MelonLoader"],
  ["dobby.dll", "version.dll"],
  "MelonLoader",
);
melonUninstaller.overrideCheckFiles = ["MelonLoader"];
melonUninstaller.preserveFilesWhenFolderExists = "_RedLoader";
export const melonFeature = new FeatureInstaller(null, melonUninstaller);
melonFeature.expectedMode = InstallMode.Uninstall;
