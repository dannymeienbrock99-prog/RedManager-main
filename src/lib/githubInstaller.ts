import { BaseWebInstaller } from "./baseWebInstaller";
import type { GithubInfo } from "./githubInfo";

export class GithubInstaller extends BaseWebInstaller {
  public constructor(
    private readonly repository: GithubInfo,
    name: string,
  ) {
    super(name);
  }

  protected async getDownloadUrl(version: string): Promise<string> {
    return this.repository.getDownloadLink(version);
  }

  public getTargetVersion(): Promise<string | null> {
    return this.repository.getLatest();
  }
}
