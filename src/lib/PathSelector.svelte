<script lang="ts">
  import { dialog } from "@tauri-apps/api";
  import { invoke } from "@tauri-apps/api/tauri";
  import { gameExePath, isPathValid } from "./store";
  import { showMessageBox } from "./utils";

  async function selectPath(): Promise<void> {
    try {
      const result = await dialog.open({
        multiple: false,
        directory: false,
        filters: [{ name: "Sons of the Forest", extensions: ["exe"] }],
      });
      if (typeof result !== "string") return;

      const valid = await invoke<boolean>("validate_game_executable", { path: result });
      gameExePath.set(valid ? result : "");
      isPathValid.set(valid);
      if (!valid) {
        await showMessageBox("Falsche Datei", "Bitte wähle die Datei SonsOfTheForest.exe aus.");
      }
    } catch (error) {
      console.error("Spielpfad konnte nicht ausgewählt werden.", error);
    }
  }
</script>

<div id="path-selector">
  <input class:valid={$isPathValid} class="path-input" type="text" value={$gameExePath} placeholder="SonsOfTheForest.exe wurde noch nicht gefunden" readonly />
  <button class="select-btn" aria-label="SonsOfTheForest.exe auswählen" on:click={selectPath}>Durchsuchen</button>
</div>

<style>
  #path-selector { display: flex; width: 100%; align-items: stretch; }
  .path-input { flex: 1; box-sizing: border-box; margin: 0; border-radius: 8px 0 0 8px; color: #a2a2a2; text-align: left; min-width: 0; }
  .path-input.valid { border-color: rgba(34,197,94,.45); color: #c7e8cf; }
  .select-btn { border-radius: 0 8px 8px 0; margin: 0; white-space: nowrap; }
</style>
