<script lang="ts">
  export let isUpdateAvailable: boolean;
  export let isModInstalled: boolean;
  export let update: () => Promise<void>;
  export let uninstall: () => Promise<void>;
  export let install: () => Promise<void>;
</script>

{#if isUpdateAvailable}
  <div class="horizontal">
    <button class="update btn-left" on:click={update}>Aktualisieren</button>
    <button class="uninstall btn-right" on:click={uninstall}>Deinstallieren</button>
  </div>
{:else if isModInstalled}
  <button class="uninstall full" on:click={uninstall}>Deinstallieren</button>
{:else}
  <button class="install full" on:click={install}>Installieren</button>
{/if}

<style>
  .horizontal { display: flex; align-items: center; }
  .horizontal > * { flex: 1; }
  .full { width: 100%; }
</style>
