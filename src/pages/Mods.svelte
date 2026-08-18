<script lang="ts">
  import { onMount } from "svelte";
  import { debounce } from "lodash";
  import SvgSpinnersBlocksWave from "~icons/svg-spinners/blocks-wave";
  import InfiniteScroll from "../lib/InfiniteScroll.svelte";
  import ModCard from "../lib/ModCard.svelte";
  import { ModDatabase, Sorting, type Mod } from "../lib/mods";
  import { isPathValid } from "../lib/store";
  import { errorMessage } from "../lib/utils";

  let mods: Mod[] = [];
  let searchTerm = "";
  let onlineSelected = true;
  let installedSelected = false;
  let isGrid = false;
  let currentPage = 0;
  let hasMore = true;
  let isLoading = false;
  let errorText = "";
  let totalResults = 0;

  function appendUnique(current: Mod[], incoming: Mod[]): Mod[] {
    const byId = new Map(current.map((mod) => [mod.mod_id, mod]));
    for (const mod of incoming) byId.set(mod.mod_id, mod);
    return [...byId.values()];
  }

  async function fetchOnlinePage(page: number, reset = false): Promise<void> {
    if (isLoading || (!hasMore && !reset)) return;
    isLoading = true;
    errorText = "";

    try {
      const response = await ModDatabase.fetchMods(
        page,
        Sorting.newest,
        true,
        false,
        searchTerm,
      );
      ModDatabase.initModList(response.data);
      mods = reset ? response.data : appendUnique(mods, response.data);
      currentPage = response.meta.page;
      totalResults = response.meta.total;
      hasMore = response.meta.page < response.meta.pages;
    } catch (error) {
      errorText = errorMessage(error);
      hasMore = false;
    } finally {
      isLoading = false;
    }
  }

  async function showOnline(): Promise<void> {
    onlineSelected = true;
    installedSelected = false;
    mods = [];
    currentPage = 0;
    hasMore = true;
    totalResults = 0;
    await ModDatabase.loadInstalledMods();
    await fetchOnlinePage(1, true);
  }

  async function showInstalled(): Promise<void> {
    onlineSelected = false;
    installedSelected = true;
    isLoading = true;
    errorText = "";
    try {
      await ModDatabase.loadInstalledMods();
      const installed = await ModDatabase.getInstalledMods();
      const term = searchTerm.trim().toLowerCase();
      mods = term
        ? installed.filter((mod) =>
            `${mod.name} ${mod.user.name} ${mod.mod_id}`.toLowerCase().includes(term),
          )
        : installed;
      totalResults = mods.length;
      hasMore = false;
    } catch (error) {
      errorText = errorMessage(error);
      mods = [];
    } finally {
      isLoading = false;
    }
  }

  async function loadNextPage(): Promise<void> {
    if (onlineSelected && hasMore) {
      await fetchOnlinePage(currentPage + 1);
    }
  }

  const handleSearchInput = debounce(async (event: Event) => {
    searchTerm = (event.currentTarget as HTMLInputElement).value;
    if (onlineSelected) await showOnline();
    else await showInstalled();
  }, 450);

  async function refreshMods(): Promise<void> {
    if (onlineSelected) await showOnline();
    else await showInstalled();
  }

  onMount(async () => {
    isGrid = window.innerWidth > 1050;
    if ($isPathValid) {
      await ModDatabase.initDatabase();
      await showOnline();
    }
  });
</script>

<svelte:window on:resize={() => (isGrid = window.innerWidth > 1050)} />

<div class="column">
  {#if $isPathValid}
    <div class="toolbar">
      <input
        class="generic-input search-input"
        placeholder="Mods, Autoren oder IDs durchsuchen"
        type="search"
        on:input={handleSearchInput}
      />
      <button class:cat-btn-selected={onlineSelected} class="btn-left cat-btn" on:click={showOnline}>Online</button>
      <button class:cat-btn-selected={installedSelected} class="btn-right cat-btn" on:click={showInstalled}>Installiert</button>
    </div>

    <div class="result-bar">
      <span>{totalResults} {installedSelected ? "installierte" : "gefundene"} Mods</span>
      <button class="refresh" on:click={refreshMods}>Neu laden</button>
    </div>

    {#if errorText}
      <div class="error-box">
        <b>Mod-Datenbank konnte nicht geladen werden.</b>
        <span>{errorText}</span>
        <button on:click={refreshMods}>Erneut versuchen</button>
      </div>
    {/if}

    <div class:grid={isGrid} class="scroller">
      {#each mods as mod (mod.mod_id)}
        <ModCard {mod} {isGrid} on:refreshMods={refreshMods} />
      {/each}

      {#if !isLoading && !errorText && mods.length === 0}
        <div class="empty">Keine passenden Mods gefunden.</div>
      {/if}

      {#if isLoading}
        <div class="loading"><SvgSpinnersBlocksWave /></div>
      {/if}

      <InfiniteScroll
        {hasMore}
        threshold={140}
        on:loadMore={loadNextPage}
      />
    </div>
  {:else}
    <div class="empty path-empty">Wähle im Tab „Start“ zuerst die gültige SonsOfTheForest.exe aus.</div>
  {/if}
</div>

<style>
  .toolbar { display: flex; align-items: stretch; margin-bottom: 8px; }
  .search-input { flex: 1; margin: 0; border-radius: 8px 0 0 8px; min-width: 0; }
  .cat-btn { margin: 0; border-radius: 0; color: #a2a2a2; min-width: 95px; }
  .cat-btn:last-child { border-radius: 0 8px 8px 0; }
  .cat-btn-selected { background: #191919; color: #f87171; border-color: #7f1d1d; }
  .result-bar { display: flex; justify-content: space-between; align-items: center; color: #777; font-size: .82rem; margin: 0 4px 8px; }
  .refresh { padding: 5px 9px; margin: 0; font-size: .78rem; }
  .scroller { position: relative; height: calc(100vh - 190px); min-height: 420px; overflow-y: auto; overflow-x: hidden; padding-right: 4px; }
  .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); align-content: start; gap: 14px; }
  .loading { display: flex; justify-content: center; padding: 28px; font-size: 2rem; color: #ef4444; grid-column: 1 / -1; }
  .empty { grid-column: 1 / -1; padding: 32px; text-align: center; color: #8b8b8b; border: 1px dashed #3a3a3a; border-radius: 10px; }
  .path-empty { margin-top: 30px; }
  .error-box { display: flex; align-items: center; gap: 12px; padding: 10px 12px; margin-bottom: 10px; border-left: 3px solid #ef4444; background: rgba(239,68,68,.08); color: #c9c9c9; }
  .error-box span { flex: 1; color: #aaa; }
  .error-box button { margin: 0; padding: 7px 10px; white-space: nowrap; }
</style>
