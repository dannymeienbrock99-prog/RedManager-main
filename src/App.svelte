<script lang="ts">
  import { invoke } from "@tauri-apps/api/tauri";
  import { onMount } from "svelte";
  import { fade } from "svelte/transition";
  import MdiTools from "~icons/mdi/tools";
  import UilArrowCircleDown from "~icons/uil/arrow-circle-down";
  import UilBox from "~icons/uil/box";
  import UilBriefcase from "~icons/uil/briefcase";
  import BattoTools from "./pages/BattoTools.svelte";
  import MainPage from "./pages/MainPage.svelte";
  import Modding from "./pages/Modding.svelte";
  import Mods from "./pages/Mods.svelte";
  import {
    gameExePath,
    isDotnetInstalled,
    isDotnetSdkInstalled,
    isPathValid,
    processName,
    processProgress,
    processing,
  } from "./lib/store";

  type Tab = {
    label: string;
    component: typeof MainPage;
    icon: typeof UilArrowCircleDown;
  };

  const tabs: Tab[] = [
    { label: "Start", component: MainPage, icon: UilArrowCircleDown },
    { label: "Mods", component: Mods, icon: UilBox },
    { label: "Crazy_Batto", component: BattoTools, icon: MdiTools },
    { label: "Mod-Erstellung", component: Modding, icon: UilBriefcase },
  ];

  let activeTabComponent = tabs[0].component;

  function selectTab(component: Tab["component"]): void {
    activeTabComponent = component;
  }

  function handleKeyPress(event: KeyboardEvent, component: Tab["component"]): void {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      selectTab(component);
    }
  }

  onMount(async () => {
    processing.set(true);
    processName.set("Sons of the Forest wird gesucht …");
    processProgress.set(15);

    try {
      const steamPath = await invoke<string | null>("get_steam_path");
      if (steamPath && (await invoke<boolean>("validate_game_executable", { path: steamPath }))) {
        gameExePath.set(steamPath);
        isPathValid.set(true);
      } else {
        gameExePath.set("");
        isPathValid.set(false);
      }

      processName.set(".NET-Komponenten werden geprüft …");
      processProgress.set(65);
      isDotnetInstalled.set(await invoke<boolean>("is_dotnet6_installed"));
      isDotnetSdkInstalled.set(await invoke<boolean>("is_dotnet6_sdk_installed"));
      processProgress.set(100);
    } catch (error) {
      console.warn("Initialisierung konnte nicht vollständig abgeschlossen werden.", error);
      isPathValid.set(false);
    } finally {
      processing.set(false);
    }
  });
</script>

<main>
  <nav class="tabs" aria-label="Hauptnavigation">
    {#each tabs as tab}
      <div
        class:activetab={tab.component === activeTabComponent}
        class="tab"
        tabindex="0"
        role="button"
        aria-pressed={tab.component === activeTabComponent}
        on:click={() => selectTab(tab.component)}
        on:keydown={(event) => handleKeyPress(event, tab.component)}
      >
        <svelte:component this={tab.icon} />
        {tab.label}
      </div>
    {/each}
  </nav>

  <div class="container">
    <svelte:component this={activeTabComponent} />
  </div>

  {#if $processing}
    <div class="loading-overlay" transition:fade={{ duration: 150 }}>
      <strong>{$processName}</strong>
      <div class="progress-bar" aria-label="Fortschritt">
        <div class="progress" style:width={`${Math.max(0, Math.min(100, $processProgress))}%`}></div>
      </div>
    </div>
  {/if}
</main>

<style>
  .loading-overlay {
    position: fixed;
    inset: 0;
    z-index: 1000;
    background: rgba(0, 0, 0, 0.82);
    backdrop-filter: blur(5px);
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    color: white;
    font-size: 1.25rem;
    gap: 20px;
  }
  .progress-bar {
    background: rgba(255, 255, 255, 0.16);
    width: min(620px, 72vw);
    height: 16px;
    border-radius: 999px;
    overflow: hidden;
  }
  .progress {
    height: 100%;
    background: linear-gradient(90deg, #7f1d1d, #ef4444);
    transition: width 0.25s ease;
  }
</style>
