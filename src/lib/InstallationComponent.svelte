<script lang="ts">
  import { onMount } from "svelte";
  import { InstallMode, type FeatureInstaller } from "./featureInstaller";
  import { processProgress, processing } from "./store";
  import { errorMessage, showMessageBox } from "./utils";

  export let feature: FeatureInstaller;

  let currentMode = feature.currentModeState;
  let currentClass = currentMode.toLowerCase();
  let shouldBeVisible = false;
  let featureLabel = feature.getName();
  let loading = true;

  $: currentClass = currentMode.toLowerCase();

  async function run(callback: () => Promise<void>): Promise<void> {
    processing.set(true);
    processProgress.set(0);
    try {
      await callback();
      currentMode = feature.currentModeState;
      await refreshVisibility();
    } catch (error) {
      await showMessageBox(`${feature.getName()} – Fehler`, errorMessage(error));
    } finally {
      processing.set(false);
    }
  }

  async function refreshVisibility(): Promise<void> {
    shouldBeVisible = await feature.canDoAction();
  }

  onMount(async () => {
    try {
      await feature.refreshMode();
      currentMode = feature.currentModeState;
      featureLabel = (await feature.getRemoteVersionString(true)) ?? feature.getName();
      await refreshVisibility();
    } catch (error) {
      console.warn(`${feature.getName()} konnte nicht geprüft werden.`, error);
      shouldBeVisible = true;
    } finally {
      loading = false;
    }
  });
</script>

{#if loading}
  <div class="feature-container">
    <span class="description-content">{feature.getName()} wird geprüft …</span>
  </div>
{:else if shouldBeVisible}
  <div class:description={Boolean(feature.description)} class="feature-container">
    {#if feature.description}
      <span class="description-content">{feature.description}</span>
    {/if}

    {#if currentMode === "Update"}
      <div class="horizontal">
        <button class="update btn-left" on:click={() => run(() => feature.handle(InstallMode.Update))}>
          {featureLabel} aktualisieren
        </button>
        <button class="uninstall btn-right" on:click={() => run(() => feature.handle(InstallMode.Uninstall))}>
          {feature.getName()} deinstallieren
        </button>
      </div>
    {:else}
      <button class={currentClass} on:click={() => run(() => feature.handleCurrentMode())}>
        {currentMode === "Install" ? `${featureLabel} installieren` : `${feature.getName()} deinstallieren`}
      </button>
    {/if}
  </div>
{/if}

<style>
  .horizontal { display: flex; align-items: center; }
  .horizontal > * { flex: 1; }
  .feature-container > * { width: 100%; }
  .description { padding: 12px; border-radius: 10px; border: 1px dashed #414141; }
  .description-content { margin-bottom: 1em; display: block; text-align: center; font-size: 0.9em; color: #a2a2a2; }
</style>
