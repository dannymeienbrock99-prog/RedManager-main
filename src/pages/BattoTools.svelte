<script lang="ts">
  import { path } from "@tauri-apps/api";
  import { Command, open } from "@tauri-apps/api/shell";
  import { invoke } from "@tauri-apps/api/tauri";
  import { onMount } from "svelte";
  import {
    buildAndInstallBundledDeathCounter,
    getBundledDeathCounterStatus,
    type BundledModBuildResult,
    type BundledModStatus,
    uninstallBundledDeathCounter,
  } from "../lib/bundledMods";
  import {
    gameExePath,
    isDotnetSdkInstalled,
    isPathValid,
    processName,
    processProgress,
    processing,
  } from "../lib/store";
  import { errorMessage, showMessageBox } from "../lib/utils";

  let status: BundledModStatus | null = null;
  let result: BundledModBuildResult | null = null;
  let loading = false;

  async function refreshStatus(): Promise<void> {
    if (!$isPathValid || !$gameExePath) {
      status = null;
      return;
    }

    loading = true;
    try {
      status = await getBundledDeathCounterStatus($gameExePath);
      isDotnetSdkInstalled.set(await invoke<boolean>("is_dotnet6_sdk_installed"));
    } catch (error) {
      status = null;
      console.warn("Status des gebündelten Todeszählers konnte nicht gelesen werden.", error);
    } finally {
      loading = false;
    }
  }

  async function buildAndInstall(): Promise<void> {
    processing.set(true);
    processName.set("CrazyBatto Todeszähler wird gegen deine Spielversion gebaut …");
    processProgress.set(15);
    result = null;
    try {
      result = await buildAndInstallBundledDeathCounter($gameExePath);
      processProgress.set(result.success ? 100 : 0);
      if (!result.success) {
        await showMessageBox("Build fehlgeschlagen", result.message);
      }
      await refreshStatus();
    } catch (error) {
      await showMessageBox("Todeszähler – Fehler", errorMessage(error));
    } finally {
      processing.set(false);
    }
  }

  async function uninstall(): Promise<void> {
    processing.set(true);
    processName.set("CrazyBatto Todeszähler wird entfernt …");
    processProgress.set(40);
    try {
      status = await uninstallBundledDeathCounter($gameExePath);
      processProgress.set(100);
    } catch (error) {
      await showMessageBox("Deinstallation fehlgeschlagen", errorMessage(error));
    } finally {
      processing.set(false);
    }
  }

  async function openOverlay(): Promise<void> {
    if (status) await open(status.overlayUrl);
  }

  async function copyOverlayUrl(): Promise<void> {
    if (!status) return;
    try {
      await navigator.clipboard.writeText(status.overlayUrl);
      await showMessageBox("OBS-Adresse kopiert", status.overlayUrl);
    } catch {
      await showMessageBox("OBS-Adresse", status.overlayUrl);
    }
  }

  async function openDataFolder(): Promise<void> {
    if (!status) return;
    const result = await new Command("open-explorer", [await path.dirname(status.settingsPath)]).execute();
    if (result.code !== 0) {
      await showMessageBox("Datenordner konnte nicht geöffnet werden", result.stderr || result.stdout);
    }
  }

  onMount(refreshStatus);
</script>

<div class="page">
  <section class="hero-panel">
    <div>
      <span class="eyebrow">INTEGRIERTE REDLOADER-MOD</span>
      <h2>CrazyBatto SOTF Death Counter</h2>
      <p>
        Erkennt Host und Mitspieler automatisch, zählt bestätigte Tode getrennt pro Spieler und stellt die Werte als lokale OBS-Browserquelle bereit.
      </p>
    </div>
    <div class="status-block">
      {#if loading}
        <span class="badge neutral">Wird geprüft …</span>
      {:else if status?.installed}
        <span class:disabled={!status.enabled} class="badge installed">
          {status.enabled ? "Installiert und aktiv" : "Installiert, aber deaktiviert"}
        </span>
        <strong>Version {status.version ?? "unbekannt"}</strong>
      {:else}
        <span class="badge neutral">Nicht installiert</span>
      {/if}
    </div>
  </section>

  {#if !$isPathValid}
    <div class="notice warning">Wähle zuerst im Tab „Start“ die gültige <code>SonsOfTheForest.exe</code> aus.</div>
  {:else}
    <section class="requirements">
      <div class="requirement">
        <span>RedLoader</span>
        <b>muss installiert und einmal gestartet worden sein</b>
      </div>
      <div class="requirement">
        <span>.NET 6 SDK</span>
        <b class:missing={!$isDotnetSdkInstalled}>{$isDotnetSdkInstalled ? "gefunden" : "nicht gefunden"}</b>
      </div>
      <div class="requirement">
        <span>Spielstatus</span>
        <b>Sons of the Forest vor Build/Update schließen</b>
      </div>
    </section>

    {#if !$isDotnetSdkInstalled}
      <div class="notice warning">
        Für diesen einmaligen lokalen Build wird das <b>.NET 6 SDK</b> benötigt. Der normale RedLoader benötigt nur die Runtime.
      </div>
    {/if}

    <div class="actions">
      <button class="primary" disabled={!$isDotnetSdkInstalled} on:click={buildAndInstall}>
        {status?.installed ? "Todeszähler neu bauen / aktualisieren" : "Todeszähler bauen und installieren"}
      </button>
      {#if status?.installed}
        <button class="danger" on:click={uninstall}>Todeszähler deinstallieren</button>
      {/if}
      <button class="secondary" on:click={refreshStatus}>Status neu prüfen</button>
    </div>

    {#if status}
      <section class="info-grid">
        <article>
          <span>OBS-Browserquelle</span>
          <code>{status.overlayUrl}</code>
          <div class="mini-actions">
            <button on:click={copyOverlayUrl}>Adresse kopieren</button>
            <button on:click={openOverlay}>Im Browser öffnen</button>
          </div>
        </article>
        <article>
          <span>Statistik und Einstellungen</span>
          <code>{status.statsPath}</code>
          <code>{status.settingsPath}</code>
          <div class="mini-actions"><button on:click={openDataFolder}>Datenordner öffnen</button></div>
        </article>
      </section>
    {/if}

    <div class="notice">
      Die Mod wird auf deinem PC gegen genau die RedLoader- und Spiel-DLLs deiner Installation gebaut. Dadurch enthält dieses Repository keine kopierten Spieldateien. Das Universal-Heart-Rate-Modul ist hier bewusst nicht enthalten.
    </div>

    {#if result}
      <section class:failed={!result.success} class="build-result">
        <b>{result.message}</b>
        <span>Build-Ordner: {result.buildDirectory || "–"}</span>
        <details>
          <summary>Build-Ausgabe anzeigen</summary>
          <pre>{result.stdout}{result.stderr ? `\n${result.stderr}` : ""}</pre>
        </details>
      </section>
    {/if}
  {/if}
</div>

<style>
  .page { display: flex; flex-direction: column; gap: 18px; }
  .hero-panel { display: flex; justify-content: space-between; gap: 24px; padding: 22px; border: 1px solid #343434; border-radius: 14px; background: linear-gradient(135deg, rgba(239,68,68,.13), rgba(15,15,15,.9)); }
  .hero-panel h2 { margin: 4px 0 8px; }
  .hero-panel p { margin: 0; color: #b5b5b5; max-width: 650px; }
  .eyebrow { color: #f87171; font-size: .75rem; font-weight: 800; letter-spacing: .12em; }
  .status-block { min-width: 210px; display: flex; flex-direction: column; justify-content: center; align-items: flex-end; gap: 8px; color: #cfcfcf; }
  .badge { padding: 5px 10px; border-radius: 999px; font-size: .82rem; font-weight: 700; }
  .badge.installed { color: #86efac; background: rgba(34,197,94,.14); border: 1px solid rgba(34,197,94,.35); }
  .badge.installed.disabled { color: #fca5a5; background: rgba(239,68,68,.14); border-color: rgba(239,68,68,.35); }
  .badge.neutral { color: #d4d4d4; background: #242424; border: 1px solid #3a3a3a; }
  .requirements, .info-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }
  .requirement, article { padding: 14px; border: 1px solid #323232; border-radius: 10px; background: #131313; display: flex; flex-direction: column; gap: 6px; }
  .requirement span, article > span { color: #8f8f8f; font-size: .82rem; }
  .requirement b { color: #d4d4d4; font-size: .9rem; }
  .requirement b.missing { color: #fca5a5; }
  .actions { display: flex; gap: 10px; flex-wrap: wrap; }
  .actions button { flex: 1; min-width: 190px; }
  .primary { background: #8f1d1d; border-color: #d14343; }
  .danger { color: #fecaca; }
  .secondary { color: #b8c8e8; }
  .info-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  code { overflow-wrap: anywhere; color: #f3a7a7; background: #0b0b0b; border-radius: 5px; padding: 3px 6px; }
  .mini-actions { display: flex; gap: 8px; margin-top: 6px; }
  .mini-actions button { padding: 7px 10px; font-size: .82rem; }
  .notice { padding: 12px 14px; border-left: 3px solid #6366f1; background: rgba(99,102,241,.08); color: #bcbcbc; }
  .notice.warning { border-left-color: #f59e0b; background: rgba(245,158,11,.08); }
  .build-result { display: flex; flex-direction: column; gap: 8px; padding: 14px; border: 1px solid rgba(34,197,94,.35); border-radius: 10px; background: rgba(34,197,94,.08); }
  .build-result.failed { border-color: rgba(239,68,68,.35); background: rgba(239,68,68,.08); }
  pre { max-height: 280px; overflow: auto; white-space: pre-wrap; background: #080808; padding: 12px; border-radius: 8px; color: #c8c8c8; }
  @media (max-width: 850px) {
    .hero-panel { flex-direction: column; }
    .status-block { align-items: flex-start; }
    .requirements, .info-grid { grid-template-columns: 1fr; }
  }
</style>
