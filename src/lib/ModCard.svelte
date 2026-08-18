<script lang="ts">
  import { createEventDispatcher } from "svelte";
  import { ModDatabase, type Mod } from "./mods";
  import StatusButton from "./StatusButton.svelte";
  import { processProgress, processing } from "./store";
  import { errorMessage, showMessageBox } from "./utils";

  export let mod: Mod;
  export let isGrid = false;

  let imageLoaded = false;
  let imageSource = mod.imageUrl || "/no-image.svg";
  const dispatch = createEventDispatcher<{ refreshMods: void }>();

  async function run(action: () => Promise<void>, errorTitle: string): Promise<void> {
    processing.set(true);
    processProgress.set(0);
    try {
      await action();
      dispatch("refreshMods");
    } catch (error) {
      await showMessageBox(errorTitle, errorMessage(error));
    } finally {
      processing.set(false);
    }
  }

  const install = () => run(() => ModDatabase.installMod(mod), `${mod.name} konnte nicht installiert werden`);

  const uninstall = () => {
    if (!mod.installedMod) return Promise.resolve();
    return run(() => ModDatabase.uninstallMod(mod.installedMod!), `${mod.name} konnte nicht deinstalliert werden`);
  };

  const update = async () => {
    if (!mod.installedMod) return;
    await run(async () => {
      await ModDatabase.installMod(mod);
    }, `${mod.name} konnte nicht aktualisiert werden`);
  };

  const enableMod = async () => {
    if (!mod.installedMod) return;
    await run(() => ModDatabase.toggleMod(mod.installedMod!, true), `${mod.name} konnte nicht aktiviert werden`);
  };

  const disableMod = async () => {
    if (!mod.installedMod) return;
    await run(() => ModDatabase.toggleMod(mod.installedMod!, false), `${mod.name} konnte nicht deaktiviert werden`);
  };

  function formatDate(value: string): string {
    if (!value) return "–";
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? "–" : date.toLocaleDateString("de-DE");
  }

  function imageFailed(): void {
    imageSource = "/no-image.svg";
    imageLoaded = true;
  }
</script>

<article class:grid-card={isGrid} class="card">
  <header>
    <div>
      <h3>{mod.name}</h3>
      <span class="mod-id">{mod.mod_id}</span>
    </div>
    {#if mod.slug && mod.user.slug}
      <button class="site-link" on:click={() => ModDatabase.openModPage(mod)}>Auf Website</button>
    {/if}
  </header>

  <p class="description">{mod.shortDescription || "Keine Kurzbeschreibung vorhanden."}</p>

  <div class="content">
    <div class="image-container">
      {#if !imageLoaded}<div class="image-skeleton">Bild wird geladen …</div>{/if}
      <img
        class:loaded={imageLoaded}
        class="cover-img"
        src={imageSource}
        alt={`Titelbild von ${mod.name}`}
        loading="lazy"
        on:load={() => (imageLoaded = true)}
        on:error={imageFailed}
      />
    </div>

    <dl>
      <div><dt>Autor</dt><dd>{mod.user.name}</dd></div>
      <div><dt>Version</dt><dd>{mod.latestVersion}</dd></div>
      <div><dt>Aktualisiert</dt><dd>{formatDate(mod.lastReleasedAt)}</dd></div>
      <div><dt>Kategorie</dt><dd>{mod.category.name}</dd></div>
      <div><dt>Typ</dt><dd>{mod.type}</dd></div>
      {#if mod.downloads > 0}<div><dt>Downloads</dt><dd>{mod.downloads.toLocaleString("de-DE")}</dd></div>{/if}
    </dl>
  </div>

  {#if mod.dependencies.length > 0}
    <div class="dependencies">Abhängigkeiten: {mod.dependencies.join(", ")}</div>
  {/if}

  {#if mod.isInstalled}
    <button
      class:enabled={mod.installedMod?.isEnabled}
      class="toggle-button"
      on:click={mod.installedMod?.isEnabled ? disableMod : enableMod}
    >
      {mod.installedMod?.isEnabled ? "Aktiviert" : "Deaktiviert"}
    </button>
  {/if}

  <footer>
    <StatusButton
      isUpdateAvailable={mod.hasUpdate}
      isModInstalled={mod.isInstalled}
      {update}
      {uninstall}
      {install}
    />
  </footer>
</article>

<style>
  .card { display: flex; flex-direction: column; gap: 12px; padding: 14px; border: 1px solid #303030; border-radius: 12px; background: #121212; margin-bottom: 14px; min-width: 0; }
  .grid-card { margin-bottom: 0; height: fit-content; }
  header { display: flex; justify-content: space-between; gap: 10px; align-items: flex-start; }
  h3 { margin: 0; color: #d0d0d0; font-size: 1.05rem; overflow-wrap: anywhere; }
  .mod-id { display: block; color: #666; font-size: .72rem; margin-top: 2px; }
  .site-link { margin: 0; padding: 6px 8px; font-size: .75rem; white-space: nowrap; }
  .description { color: #898989; font-size: .88rem; line-height: 1.4; margin: 0; min-height: 2.5em; }
  .content { display: grid; grid-template-columns: minmax(180px, 1.2fr) minmax(170px, 1fr); gap: 12px; align-items: start; }
  .grid-card .content { grid-template-columns: 1fr; }
  .image-container { position: relative; aspect-ratio: 3 / 2; overflow: hidden; border-radius: 9px; background: #0b0b0b; }
  .image-skeleton { position: absolute; inset: 0; display: grid; place-items: center; color: #555; font-size: .8rem; }
  .cover-img { width: 100%; height: 100%; object-fit: cover; opacity: 0; transition: opacity .2s ease; }
  .cover-img.loaded { opacity: 1; }
  dl { margin: 0; display: flex; flex-direction: column; gap: 5px; }
  dl div { display: flex; justify-content: space-between; gap: 12px; border-bottom: 1px solid #242424; padding-bottom: 4px; }
  dt { color: #696969; font-size: .78rem; }
  dd { margin: 0; color: #b5b5b5; font-size: .78rem; text-align: right; overflow-wrap: anywhere; }
  .dependencies { padding: 7px 9px; border-radius: 7px; background: #0c0c0c; color: #777; font-size: .75rem; overflow-wrap: anywhere; }
  .toggle-button { align-self: flex-start; margin: 0; padding: 6px 12px; font-size: .8rem; color: #fca5a5; }
  .toggle-button.enabled { color: #86efac; }
  footer { margin-top: auto; }
  @media (max-width: 800px) { .content { grid-template-columns: 1fr; } }
</style>
