<script lang="ts">
  import { dialog, path } from "@tauri-apps/api";
  import { Command, open } from "@tauri-apps/api/shell";
  import { onMount } from "svelte";
  import FeatureList from "../lib/FeatureList.svelte";
  import { ueFeature } from "../lib/featureInstaller";
  import {
    gameExePath,
    isDotnetSdkInstalled,
    processName,
    processProgress,
    processing,
  } from "../lib/store";
  import { errorMessage, showMessageBox } from "../lib/utils";

  let isTemplateInstalled = false;
  let modName = "MySotfMod";
  let appendComments = true;

  async function checkForTemplate(): Promise<void> {
    try {
      const result = await new Command("dotnet-template-check", ["new", "sotfmod", "-h"]).execute();
      isTemplateInstalled = result.code === 0 && !result.stdout.includes("No templates");
    } catch (error) {
      console.warn("RedLoader-Template konnte nicht geprüft werden.", error);
      isTemplateInstalled = false;
    }
  }

  async function installTemplate(): Promise<void> {
    processing.set(true);
    processName.set("RedLoader-Mod-Template wird installiert …");
    processProgress.set(25);
    try {
      const result = await new Command("dotnet-install-template", [
        "new",
        "install",
        "RedLoader.Templates",
      ]).execute();
      if (result.code !== 0) {
        throw new Error(result.stderr || result.stdout || "Template-Installation fehlgeschlagen.");
      }
      processProgress.set(100);
      await checkForTemplate();
    } catch (error) {
      await showMessageBox("Template-Installation fehlgeschlagen", errorMessage(error));
    } finally {
      processing.set(false);
    }
  }

  async function createProject(): Promise<void> {
    const normalizedName = modName.trim();
    if (!/^[A-Za-z_][A-Za-z0-9_.-]*$/.test(normalizedName)) {
      await showMessageBox(
        "Ungültiger Projektname",
        "Der Name muss mit einem Buchstaben oder Unterstrich beginnen und darf nur Buchstaben, Zahlen, Punkt, Bindestrich und Unterstrich enthalten.",
      );
      return;
    }

    const selectedDirectory = await dialog.open({ directory: true, multiple: false });
    if (typeof selectedDirectory !== "string") return;

    processing.set(true);
    processName.set("Sons-of-the-Forest-Modprojekt wird erstellt …");
    processProgress.set(25);
    try {
      const gameDirectory = await path.dirname($gameExePath);
      const targetLocation = await path.join(selectedDirectory, normalizedName);
      const argumentsList = [
        "new",
        "sotfmod",
        "--name",
        normalizedName,
        "--gameDirPath",
        gameDirectory,
        "--output",
        targetLocation,
      ];
      if (!appendComments) argumentsList.push("--comments", "false");

      const commandName = appendComments
        ? "dotnet-create-project"
        : "dotnet-create-project-no-comments";
      const result = await new Command(commandName, argumentsList).execute();
      if (result.code !== 0) {
        throw new Error(result.stderr || result.stdout || "Projekt konnte nicht erstellt werden.");
      }
      processProgress.set(90);
      await new Command("open-explorer", [targetLocation]).execute();
      processProgress.set(100);
    } catch (error) {
      await showMessageBox("Modprojekt konnte nicht erstellt werden", errorMessage(error));
    } finally {
      processing.set(false);
    }
  }

  async function openDotnetDownload(): Promise<void> {
    await open("https://dotnet.microsoft.com/download/dotnet/6.0");
  }

  onMount(async () => {
    if ($isDotnetSdkInstalled) await checkForTemplate();
  });
</script>

<div class="page">
  <header>
    <span class="eyebrow">REDLOADER SDK</span>
    <h2>Mod-Erstellung</h2>
    <p>Erstellt ein offizielles RedLoader-Projekt, das bereits auf deine lokale Sons-of-the-Forest-Installation zeigt.</p>
  </header>

  {#if !$isDotnetSdkInstalled}
    <div class="warning">
      Für die Mod-Erstellung wird das <a href="https://dotnet.microsoft.com/download/dotnet/6.0" on:click|preventDefault={openDotnetDownload}>.NET 6 SDK</a> benötigt.
    </div>
  {:else if !isTemplateInstalled}
    <section class="panel">
      <p>Das offizielle Template <code>RedLoader.Templates</code> ist noch nicht installiert.</p>
      <button class="primary" on:click={installTemplate}>RedLoader-Template installieren</button>
    </section>
  {:else}
    <section class="panel">
      <label for="mod-name">Projektname</label>
      <input id="mod-name" class="generic-input" type="text" bind:value={modName} />
      <div class="form-checkbox">
        <input id="comments" type="checkbox" bind:checked={appendComments} />
        <label for="comments">Erklärende Kommentare in den Beispielcode einfügen</label>
      </div>
      <button class="primary" on:click={createProject}>Neues SOTF-Modprojekt erstellen</button>
    </section>
  {/if}

  <section class="panel secondary-panel">
    <h3>Entwicklerwerkzeuge</h3>
    <FeatureList features={[ueFeature]} />
  </section>
</div>

<style>
  .page { display: flex; flex-direction: column; gap: 16px; }
  header h2 { margin: 4px 0; }
  header p { color: #9b9b9b; margin: 0; }
  .eyebrow { color: #ef6a6a; font-size: .75rem; letter-spacing: .14em; font-weight: 800; }
  .panel { padding: 16px; border: 1px solid #343434; border-radius: 12px; background: #121212; }
  .panel > * { box-sizing: border-box; width: 100%; }
  .panel label { display: block; color: #aaa; margin-bottom: 6px; }
  .primary { color: #f7b0b0; border-color: #7f1d1d; }
  .warning { padding: 14px; border-left: 3px solid #f59e0b; background: rgba(245,158,11,.08); color: #bbb; }
  .secondary-panel h3 { margin-top: 0; font-size: .95rem; color: #bbb; }
  code { color: #f0a2a2; }
</style>
