<script lang="ts">
  import { Command, open } from "@tauri-apps/api/shell";
  import InstallFeature from "../lib/InstallationComponent.svelte";
  import PathSelector from "../lib/PathSelector.svelte";
  import { bieFeature, loaderFeature, melonFeature } from "../lib/featureInstaller";
  import { getDirectoryPath, isDotnetInstalled, isPathValid } from "../lib/store";
  import redLogo from "/redlogo.png";

  const cleanupFeatures = [bieFeature, melonFeature];

  async function openFolder(): Promise<void> {
    const result = await new Command("open-explorer", [await getDirectoryPath()]).execute();
    if (result.code !== 0) throw new Error(result.stderr || "Spielordner konnte nicht geöffnet werden.");
  }

  async function startGame(): Promise<void> {
    await open("steam://rungameid/1326470");
  }

  async function openExternal(url: string): Promise<void> {
    await open(url);
  }
</script>

<div class="page">
  <header class="brand">
    <a href="https://github.com/ToniMacaroni/RedLoader" on:click|preventDefault={() => openExternal("https://github.com/ToniMacaroni/RedLoader")}>
      <img class="big-logo" src={redLogo} alt="RedLoader" />
    </a>
    <div>
      <span class="eyebrow">CRAZY_BATTO EDITION</span>
      <h1>RedManager für Sons of the Forest</h1>
      <p>RedLoader installieren, Mods aus sotf-mods.com verwalten und den integrierten Multiplayer-Todeszähler bauen.</p>
    </div>
  </header>

  <section class="panel">
    <h2>Spielinstallation</h2>
    <PathSelector />
    {#if !$isPathValid}
      <p class="hint">Die Steam-Installation wird automatisch gesucht. Alternativ die Datei <code>SonsOfTheForest.exe</code> auswählen.</p>
    {/if}
  </section>

  {#if $isPathValid}
    <section class="panel">
      <h2>RedLoader</h2>
      <InstallFeature feature={loaderFeature} />
    </section>

    <section class="panel subtle">
      <h2>Alte Modloader bereinigen</h2>
      <p class="hint">Diese Schaltflächen erscheinen nur, wenn eine alte BepInEx- oder MelonLoader-Installation erkannt wird. Der Ordner <code>Mods</code> wird dabei nicht mehr pauschal gelöscht.</p>
      {#each cleanupFeatures as feature}
        <InstallFeature {feature} />
      {/each}
    </section>

    <div class="tool-row">
      <button on:click={openFolder}>Spielordner öffnen</button>
      <button class="start" on:click={startGame}>Sons of the Forest starten</button>
    </div>
  {/if}

  {#if !$isDotnetInstalled}
    <div class="warning">
      Die <a href="https://dotnet.microsoft.com/download/dotnet/6.0" on:click|preventDefault={() => openExternal("https://dotnet.microsoft.com/download/dotnet/6.0")}>.NET-6-Runtime</a> wurde nicht gefunden. RedLoader benötigt sie.
    </div>
  {/if}
</div>

<style>
  .page { display: flex; flex-direction: column; gap: 16px; }
  .brand { display: flex; align-items: center; gap: 22px; padding: 12px 0 20px; }
  .big-logo { width: 180px; max-height: 125px; object-fit: contain; filter: drop-shadow(0 0 25px rgba(239,68,68,.22)); }
  .brand h1 { margin: 4px 0; text-align: left; }
  .brand p { margin: 0; max-width: 690px; color: #a7a7a7; }
  .eyebrow { color: #ef6a6a; font-size: .75rem; letter-spacing: .15em; font-weight: 800; }
  .panel { padding: 16px; border: 1px solid #303030; border-radius: 12px; background: rgba(15,15,15,.9); }
  .panel.subtle { background: rgba(15,15,15,.58); }
  .panel h2 { margin: 0 0 12px; font-size: 1rem; color: #d4d4d4; }
  .hint { color: #8f8f8f; font-size: .88rem; margin: 8px 0 0; }
  .tool-row { display: flex; gap: 10px; }
  .tool-row button { flex: 1; }
  .tool-row .start { color: #86efac; }
  .warning { padding: 12px; border-left: 3px solid #f59e0b; background: rgba(245,158,11,.08); color: #c6c6c6; }
  code { color: #f0a2a2; }
  @media (max-width: 760px) { .brand { flex-direction: column; align-items: flex-start; } .big-logo { width: 150px; } }
</style>
