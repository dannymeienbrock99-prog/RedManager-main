import semver from "semver";

type GithubAsset = {
  name: string;
  browser_download_url: string;
};

type GithubRelease = {
  tag_name: string;
  draft: boolean;
  prerelease: boolean;
  assets: GithubAsset[];
};

type ReleaseInfo = {
  version: string;
  downloadUrl: string;
};

export class GithubInfo {
  private releases: ReleaseInfo[] = [];

  public constructor(
    private readonly repo: string,
    private readonly githubFileName: string,
  ) {}

  private get apiPath(): string {
    return `https://api.github.com/repos/${this.repo}/releases`;
  }

  public async fetch(force = false): Promise<void> {
    if (!force && this.releases.length > 0) {
      return;
    }

    const response = await fetch(this.apiPath, {
      headers: {
        Accept: "application/vnd.github+json",
      },
    });
    if (!response.ok) {
      throw new Error(`GitHub antwortete mit HTTP ${response.status}.`);
    }

    const data = (await response.json()) as GithubRelease[];
    this.releases = data
      .filter((release) => !release.draft && !release.prerelease)
      .map((release) => {
        const asset = release.assets.find((candidate) => candidate.name === this.githubFileName);
        return asset
          ? {
              version: release.tag_name,
              downloadUrl: asset.browser_download_url,
            }
          : null;
      })
      .filter((release): release is ReleaseInfo => Boolean(release))
      .sort((left, right) => {
        const leftVersion = semver.valid(left.version) ?? semver.valid(semver.coerce(left.version));
        const rightVersion = semver.valid(right.version) ?? semver.valid(semver.coerce(right.version));
        if (leftVersion && rightVersion) {
          return semver.rcompare(leftVersion, rightVersion);
        }
        return right.version.localeCompare(left.version, undefined, { numeric: true });
      });
  }

  public async getLatest(): Promise<string | null> {
    await this.fetch();
    return this.releases[0]?.version ?? null;
  }

  public async getDownloadLink(version?: string | null): Promise<string> {
    await this.fetch();
    const release = version
      ? this.releases.find((candidate) => candidate.version === version)
      : this.releases[0];
    if (!release) {
      throw new Error(`Kein Release-Asset ${this.githubFileName} in ${this.repo} gefunden.`);
    }
    return release.downloadUrl;
  }
}

export const redLoaderInfo = new GithubInfo("ToniMacaroni/RedLoader", "RedLoader.zip");
export const unityExplorerInfo = new GithubInfo("ToniMacaroni/UnityExplorer_Sons", "UnityExplorer.zip");
