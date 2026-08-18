import { fs, path, shell } from "@tauri-apps/api";
import semver from "semver";
import { getDirectoryPath, getLibsDir, getModsDir, processName, processProgress } from "./store";
import { downloadAndInstall, showMessageBox } from "./utils";

export type ModCategory = {
  name: string;
  slug: string;
};

export type ModAuthor = {
  name: string;
  slug: string;
};

export type ModVersion = {
  version: string;
  isLatest?: boolean;
  downloadUrl?: string;
  filename?: string;
  extension?: string;
};

export type Mod = {
  name: string;
  slug: string;
  mod_id: string;
  shortDescription: string;
  isApproved: boolean;
  category: ModCategory;
  user: ModAuthor;
  imageUrl: string;
  latestVersion: string;
  lastReleasedAt: string;
  type: "Mod" | "Library" | string;
  dependencies: string[];
  downloads: number;
  versions: ModVersion[];
  versionDownloadUrl?: string;

  isInstalled: boolean;
  installedMod?: InstalledMod;
  hasUpdate: boolean;
};

export type RequestMeta = {
  limit: number;
  next_page: number;
  page: number;
  pages: number;
  prev_page: number;
  total: number;
};

export type EndpointResponse = {
  meta: RequestMeta;
  data: Mod[];
};

export type ModManifest = {
  id: string;
  name?: string;
  author: string;
  version: string;
  type: string;
};

export type InstallRoot = "Mods" | "Libs";

export type InstalledMod = {
  modName: string;
  isEnabled: boolean;
  installRoot: InstallRoot;
  manifest: ModManifest;
};

export enum Sorting {
  newest = "newest",
  mostDownloaded = "most_downloaded",
  highestRated = "highest_rating",
}

type ApiResponse<T> = {
  status?: boolean;
  data: T;
  meta?: Partial<RequestMeta>;
  message?: string;
};

type ApiImage = {
  url?: string;
  isPrimary?: boolean;
  isThumbnail?: boolean;
};

type ApiMod = Record<string, unknown> & {
  id?: string;
  mod_id?: string;
  name?: string;
  slug?: string;
  shortDescription?: string;
  short_description?: string;
  description?: string;
  isApproved?: boolean;
  category?: Partial<ModCategory>;
  category_name?: string;
  category_slug?: string;
  user?: Partial<ModAuthor>;
  user_name?: string;
  user_slug?: string;
  imageUrl?: string;
  images?: ApiImage[];
  latestVersion?: string;
  latest_version?: string;
  lastReleasedAt?: string;
  updatedAt?: string;
  type?: string;
  dependencies?: string[] | string;
  downloads?: number;
  versions?: ModVersion[];
};

const API_BASE = "https://api.sotf-mods.com/api";
const SITE_BASE = "https://sotf-mods.com";
const PAGE_SIZE = 20;
const FALLBACK_IMAGE = "/no-image.svg";

function asNonEmptyString(value: unknown, fallback = ""): string {
  return typeof value === "string" && value.trim() ? value.trim() : fallback;
}

function normalizeVersion(value: unknown): string {
  return asNonEmptyString(value, "0.0.0").replace(/^v(?=\d)/i, "");
}

function comparableVersion(value: string): string | null {
  const normalized = normalizeVersion(value);
  return semver.valid(normalized) ?? semver.valid(semver.coerce(normalized));
}

function compareVersionsDescending(left: ModVersion, right: ModVersion): number {
  const leftVersion = comparableVersion(left.version);
  const rightVersion = comparableVersion(right.version);
  if (leftVersion && rightVersion) {
    return semver.rcompare(leftVersion, rightVersion);
  }
  return right.version.localeCompare(left.version, undefined, { numeric: true, sensitivity: "base" });
}

function hasNewerVersion(remoteVersion: string, localVersion: string): boolean {
  const remote = comparableVersion(remoteVersion);
  const local = comparableVersion(localVersion);
  if (remote && local) {
    return semver.gt(remote, local);
  }
  return normalizeVersion(remoteVersion) !== normalizeVersion(localVersion);
}

function normalizeDependencies(value: ApiMod["dependencies"]): string[] {
  const entries = Array.isArray(value)
    ? value
    : typeof value === "string"
      ? value.split(",")
      : [];

  return [...new Set(entries.map((item) => asNonEmptyString(item)).filter(Boolean))];
}

function normalizeVersions(raw: ApiMod): ModVersion[] {
  const versions = Array.isArray(raw.versions)
    ? raw.versions
        .filter((item): item is ModVersion => Boolean(item && typeof item.version === "string"))
        .map((item) => ({ ...item, version: normalizeVersion(item.version) }))
    : [];

  const legacyVersion = asNonEmptyString(raw.latestVersion ?? raw.latest_version);
  if (legacyVersion && !versions.some((item) => item.version === normalizeVersion(legacyVersion))) {
    versions.push({ version: normalizeVersion(legacyVersion), isLatest: true });
  }

  return versions.sort(compareVersionsDescending);
}

function normalizeMod(raw: ApiMod): Mod {
  const versions = normalizeVersions(raw);
  const selectedVersion = versions.find((item) => item.isLatest) ?? versions[0];
  const images = Array.isArray(raw.images) ? raw.images : [];
  const selectedImage =
    images.find((item) => item.isPrimary)?.url ??
    images.find((item) => item.isThumbnail)?.url ??
    images[0]?.url;

  const userName = asNonEmptyString(raw.user?.name ?? raw.user_name, "Unbekannt");
  const userSlug = asNonEmptyString(raw.user?.slug ?? raw.user_slug, userName);
  const categoryName = asNonEmptyString(raw.category?.name ?? raw.category_name, "Ohne Kategorie");
  const categorySlug = asNonEmptyString(raw.category?.slug ?? raw.category_slug, "uncategorized");
  const slug = asNonEmptyString(raw.slug);
  const modId = asNonEmptyString(raw.mod_id ?? raw.id, slug);

  return {
    name: asNonEmptyString(raw.name, modId || "Unbenannte Mod"),
    slug,
    mod_id: modId,
    shortDescription: asNonEmptyString(
      raw.shortDescription ?? raw.short_description ?? raw.description,
    ),
    isApproved: raw.isApproved !== false,
    category: { name: categoryName, slug: categorySlug },
    user: { name: userName, slug: userSlug },
    imageUrl: asNonEmptyString(raw.imageUrl ?? selectedImage, FALLBACK_IMAGE),
    latestVersion: selectedVersion?.version ?? "0.0.0",
    lastReleasedAt: asNonEmptyString(raw.lastReleasedAt ?? raw.updatedAt),
    type: asNonEmptyString(raw.type, "Mod"),
    dependencies: normalizeDependencies(raw.dependencies),
    downloads: typeof raw.downloads === "number" ? raw.downloads : 0,
    versions,
    versionDownloadUrl: selectedVersion?.downloadUrl,
    isInstalled: false,
    hasUpdate: false,
  };
}

function normalizeMeta(meta: Partial<RequestMeta> | undefined, page: number, itemCount: number): RequestMeta {
  const limit = Number(meta?.limit) || PAGE_SIZE;
  const pages = Math.max(1, Number(meta?.pages) || 1);
  const currentPage = Math.max(1, Number(meta?.page) || page);
  return {
    limit,
    page: currentPage,
    pages,
    total: Math.max(itemCount, Number(meta?.total) || itemCount),
    next_page: Math.min(pages, Number(meta?.next_page) || currentPage + 1),
    prev_page: Math.max(1, Number(meta?.prev_page) || currentPage - 1),
  };
}

async function fetchJson<T>(url: string): Promise<T> {
  const controller = new AbortController();
  const timeout = window.setTimeout(() => controller.abort(), 20_000);
  try {
    const response = await fetch(url, {
      signal: controller.signal,
      headers: { Accept: "application/json" },
    });
    if (!response.ok) {
      throw new Error(`HTTP ${response.status} ${response.statusText}`);
    }
    return (await response.json()) as T;
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") {
      throw new Error("Zeitüberschreitung beim Abrufen der Mod-Datenbank.");
    }
    throw error;
  } finally {
    window.clearTimeout(timeout);
  }
}

export class ModDatabase {
  private static mods: Mod[] = [];
  private static installedMods: InstalledMod[] = [];

  public static async fetchMods(
    page: number,
    sorting: Sorting = Sorting.newest,
    approved = true,
    nsfw = false,
    searchTerm: string | null = null,
  ): Promise<EndpointResponse> {
    const parameters = new URLSearchParams({
      approved: String(approved),
      orderby: sorting,
      page: String(Math.max(1, page)),
      limit: String(PAGE_SIZE),
      nsfw: String(nsfw),
      type: "Both",
    });

    const normalizedSearch = searchTerm?.trim();
    if (normalizedSearch) {
      parameters.set("search", normalizedSearch);
    }

    const result = await fetchJson<ApiResponse<ApiMod[]>>(`${API_BASE}/mods?${parameters.toString()}`);
    if (result.status === false || !Array.isArray(result.data)) {
      throw new Error(result.message || "Die Mod-Datenbank lieferte keine gültige Liste.");
    }

    const data = result.data.map(normalizeMod);
    return {
      data,
      meta: normalizeMeta(result.meta, page, data.length),
    };
  }

  public static async fetchMod(id: string): Promise<Mod | null> {
    if (!id.trim()) {
      return null;
    }

    try {
      const result = await fetchJson<ApiResponse<ApiMod>>(`${API_BASE}/mods/${encodeURIComponent(id)}`);
      if (result.status === false || !result.data) {
        return null;
      }
      return normalizeMod(result.data);
    } catch (error) {
      console.warn(`Mod ${id} konnte nicht aus der Online-Datenbank geladen werden.`, error);
      return null;
    }
  }

  public static async fetchAllMods(
    sorting: Sorting = Sorting.newest,
    approved = true,
    nsfw = false,
  ): Promise<Mod[]> {
    processName.set("Erste Mod-Seite wird geladen");
    processProgress.set(0);
    const firstPage = await this.fetchMods(1, sorting, approved, nsfw);
    let mods = [...firstPage.data];

    for (let page = 2; page <= firstPage.meta.pages; page += 1) {
      processName.set(`Mod-Seite ${page}/${firstPage.meta.pages} wird geladen`);
      const pageResult = await this.fetchMods(page, sorting, approved, nsfw);
      mods = mods.concat(pageResult.data);
      processProgress.set((page / firstPage.meta.pages) * 100);
    }

    return mods;
  }

  public static getMods(): Mod[] {
    return this.mods;
  }

  public static async getInstalledMods(): Promise<Mod[]> {
    const results = await Promise.all(
      this.installedMods.map(async (installed) => {
        const remote = await this.fetchMod(installed.manifest.id);
        if (remote) {
          return this.applyInstalledState(remote, installed);
        }

        return {
          name: installed.manifest.name || installed.modName,
          slug: "",
          mod_id: installed.manifest.id,
          shortDescription: "Lokal installierte Mod; kein passender Online-Eintrag gefunden.",
          isApproved: true,
          category: { name: installed.installRoot === "Libs" ? "Library" : "Lokal", slug: "local" },
          user: { name: installed.manifest.author || "Unbekannt", slug: "" },
          imageUrl: FALLBACK_IMAGE,
          latestVersion: installed.manifest.version || "0.0.0",
          lastReleasedAt: "",
          type: installed.manifest.type || (installed.installRoot === "Libs" ? "Library" : "Mod"),
          dependencies: [],
          downloads: 0,
          versions: [{ version: installed.manifest.version || "0.0.0", isLatest: true }],
          isInstalled: true,
          installedMod: installed,
          hasUpdate: false,
        } satisfies Mod;
      }),
    );

    return results.sort((left, right) => left.name.localeCompare(right.name));
  }

  public static async loadMods(force = false): Promise<void> {
    if (force || this.mods.length === 0) {
      this.mods = await this.fetchAllMods();
    }
  }

  public static async openModPage(mod: Mod): Promise<void> {
    if (!mod.slug || !mod.user.slug) {
      return;
    }
    await shell.open(`${SITE_BASE}/mods/${encodeURIComponent(mod.user.slug)}/${encodeURIComponent(mod.slug)}`);
  }

  private static async initInstalledMod(
    folderPath: string,
    isEnabled: boolean,
    installRoot: InstallRoot,
  ): Promise<InstalledMod | null> {
    try {
      const manifestPath = await path.join(folderPath, "manifest.json");
      const manifest = JSON.parse(await fs.readTextFile(manifestPath)) as ModManifest;
      if (!manifest?.id) {
        throw new Error("manifest.json enthält keine Mod-ID.");
      }
      return {
        manifest,
        isEnabled,
        installRoot,
        modName: await path.basename(folderPath),
      };
    } catch (error) {
      console.warn(`Installierte Mod in ${folderPath} konnte nicht gelesen werden.`, error);
      return null;
    }
  }

  private static async scanInstallRoot(root: InstallRoot, rootPath: string): Promise<void> {
    const files = await fs.readDir(rootPath);
    for (const file of files) {
      const fileName = file.name ?? "";
      if (!fileName.endsWith(".dll") && !fileName.endsWith(".disabled")) {
        continue;
      }

      const isEnabled = fileName.endsWith(".dll");
      const assemblyName = fileName.replace(/\.(dll|disabled)$/i, "");
      const folderPath = await path.join(rootPath, assemblyName);
      if (!(await fs.exists(folderPath))) {
        continue;
      }

      const installed = await this.initInstalledMod(folderPath, isEnabled, root);
      if (installed) {
        this.installedMods.push(installed);
      }
    }
  }

  public static async loadInstalledMods(): Promise<void> {
    this.installedMods = [];
    const modsPath = await getModsDir();
    const libsPath = await getLibsDir();
    await this.scanInstallRoot("Mods", modsPath);
    await this.scanInstallRoot("Libs", libsPath);
  }

  public static async refreshAll(forceRefresh = false): Promise<void> {
    processName.set("Mod-Ordner werden geprüft");
    await getModsDir();
    await getLibsDir();

    processName.set("Mod-Datenbank wird geladen");
    await this.loadMods(forceRefresh);

    processName.set("Installierte Mods werden geprüft");
    await this.loadInstalledMods();
    this.initModList(this.mods);
  }

  public static async initDatabase(): Promise<void> {
    if (this.installedMods.length === 0) {
      await this.loadInstalledMods();
    }
  }

  private static applyInstalledState(mod: Mod, installed?: InstalledMod): Mod {
    mod.isInstalled = Boolean(installed);
    mod.installedMod = installed;
    mod.hasUpdate = Boolean(
      installed && hasNewerVersion(mod.latestVersion, installed.manifest.version),
    );
    return mod;
  }

  public static initModList(modList: Mod[]): void {
    for (const mod of modList) {
      const installed = this.installedMods.find(
        (candidate) => candidate.manifest.id.toLowerCase() === mod.mod_id.toLowerCase(),
      );
      this.applyInstalledState(mod, installed);
    }
  }

  public static getInstalledMod(modId: string): InstalledMod | undefined {
    const normalized = modId.toLowerCase();
    return this.installedMods.find(
      (mod) => mod.manifest.id.toLowerCase() === normalized || mod.modName.toLowerCase() === normalized,
    );
  }

  public static async installMod(mod: Mod): Promise<void> {
    await this.installModInternal(mod, new Set<string>());
  }

  private static async installModInternal(mod: Mod, visited: Set<string>): Promise<void> {
    const identity = (mod.mod_id || `${mod.user.slug}/${mod.slug}`).toLowerCase();
    if (visited.has(identity)) {
      return;
    }
    visited.add(identity);

    for (const dependencyId of mod.dependencies) {
      const installedDependency = this.getInstalledMod(dependencyId);
      if (installedDependency) {
        continue;
      }

      processName.set(`Abhängigkeit ${dependencyId} wird geladen`);
      const dependency = await this.fetchMod(dependencyId);
      if (!dependency) {
        throw new Error(`Abhängigkeit ${dependencyId} wurde in der Mod-Datenbank nicht gefunden.`);
      }
      await this.installModInternal(dependency, visited);
    }

    if (!mod.user.slug || !mod.slug || !mod.latestVersion) {
      throw new Error(`Für ${mod.name} fehlen Autor, Slug oder Versionsdaten.`);
    }

    const gamePath = await getDirectoryPath();
    const downloadUrl = `${API_BASE}/mods/slug/${encodeURIComponent(mod.user.slug)}/${encodeURIComponent(mod.slug)}/download/${encodeURIComponent(mod.latestVersion)}`;

    // Keep the current installation in place until the replacement package has
    // been downloaded successfully. This prevents a temporary network error from
    // removing an otherwise working mod.
    await downloadAndInstall(gamePath, downloadUrl, mod.name);
  }

  private static async getPathsForMod(mod: InstalledMod): Promise<[string, string]> {
    const gamePath = await getDirectoryPath();
    const assemblyPath = await path.join(
      gamePath,
      mod.installRoot,
      `${mod.modName}${mod.isEnabled ? ".dll" : ".disabled"}`,
    );
    const folderPath = await path.join(gamePath, mod.installRoot, mod.modName);
    return [assemblyPath, folderPath];
  }

  public static async uninstallMod(mod: InstalledMod): Promise<void> {
    const [assemblyPath, folderPath] = await this.getPathsForMod(mod);
    if (await fs.exists(assemblyPath)) {
      await fs.removeFile(assemblyPath);
    }
    if (await fs.exists(folderPath)) {
      await fs.removeDir(folderPath, { recursive: true });
    }
  }

  public static async toggleMod(mod: InstalledMod, shouldEnable: boolean): Promise<void> {
    const gamePath = await getDirectoryPath();
    const enabledPath = await path.join(gamePath, mod.installRoot, `${mod.modName}.dll`);
    const disabledPath = await path.join(gamePath, mod.installRoot, `${mod.modName}.disabled`);

    if (shouldEnable && (await fs.exists(disabledPath))) {
      await fs.renameFile(disabledPath, enabledPath);
      mod.isEnabled = true;
    } else if (!shouldEnable && (await fs.exists(enabledPath))) {
      await fs.renameFile(enabledPath, disabledPath);
      mod.isEnabled = false;
    }
  }

  public static async showInstallError(error: unknown): Promise<void> {
    const message = error instanceof Error ? error.message : String(error);
    await showMessageBox("Mod-Installation fehlgeschlagen", message);
  }
}
