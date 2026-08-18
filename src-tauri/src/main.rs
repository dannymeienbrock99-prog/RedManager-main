// Prevents an additional console window on Windows in release builds.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use regex::Regex;
use serde::Serialize;
use std::collections::HashSet;
use std::error::Error;
use std::fs::{self, File};
use std::io::{BufRead, BufReader};
use std::path::{Path, PathBuf};
use std::process::Command;
use std::ptr::null_mut;
use tauri::Manager;
use windows::{
    core,
    Win32::Storage::FileSystem::{
        GetFileVersionInfoSizeW, GetFileVersionInfoW, VerQueryValueW, VS_FIXEDFILEINFO,
    },
};
use winreg::{enums::*, RegKey};
use zip::read::ZipArchive;

const SOTF_APP_ID: &str = "1326470";
const SOTF_EXECUTABLE: &str = "SonsOfTheForest.exe";
const BUNDLED_ASSEMBLY_NAME: &str = "CrazyBatto.SotfDeathCounter";
const BUNDLED_MOD_VERSION: &str = "0.3.1";
const BUNDLED_MOD_ARCHIVE: &str = "CrazyBatto.SotfDeathCounter-source.zip";
const MAX_ARCHIVE_ENTRIES: usize = 10_000;
const MAX_ARCHIVE_UNCOMPRESSED_BYTES: u64 = 2 * 1024 * 1024 * 1024;
const MAX_ARCHIVE_FILE_BYTES: u64 = 1024 * 1024 * 1024;

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct BundledModStatus {
    installed: bool,
    enabled: bool,
    version: Option<String>,
    assembly_path: String,
    manifest_path: String,
    settings_path: String,
    stats_path: String,
    overlay_url: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct BundledModBuildResult {
    success: bool,
    message: String,
    stdout: String,
    stderr: String,
    build_directory: String,
    installed_path: Option<String>,
}

#[tauri::command]
fn is_dotnet6_installed() -> bool {
    if command_output_contains("dotnet", &["--list-runtimes"], "Microsoft.NETCore.App 6.") {
        return true;
    }

    let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);
    [
        r"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App",
        r"SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App",
    ]
    .iter()
    .any(|path| registry_key_contains_version(&hklm, path, "6."))
}

#[tauri::command]
fn is_dotnet6_sdk_installed() -> bool {
    command_output_contains("dotnet", &["--list-sdks"], "6.")
}

fn command_output_contains(program: &str, args: &[&str], needle: &str) -> bool {
    Command::new(program)
        .args(args)
        .output()
        .ok()
        .filter(|output| output.status.success())
        .map(|output| String::from_utf8_lossy(&output.stdout).contains(needle))
        .unwrap_or(false)
}

fn registry_key_contains_version(hive: &RegKey, path: &str, prefix: &str) -> bool {
    let Ok(key) = hive.open_subkey_with_flags(path, KEY_READ) else {
        return false;
    };

    key.enum_keys()
        .filter_map(Result::ok)
        .any(|name| name.starts_with(prefix))
        || key
            .enum_values()
            .filter_map(Result::ok)
            .any(|(name, _)| name.starts_with(prefix))
}

#[tauri::command]
fn validate_game_executable(path: String) -> bool {
    let executable = PathBuf::from(path);
    executable.is_file()
        && executable
            .file_name()
            .and_then(|name| name.to_str())
            .map(|name| name.eq_ignore_ascii_case(SOTF_EXECUTABLE))
            .unwrap_or(false)
}

#[tauri::command]
fn get_file_version(path: String) -> Result<String, String> {
    get_file_description(path).map_err(|error| error.to_string())
}

fn get_file_description(path: impl AsRef<Path>) -> Result<String, Box<dyn Error>> {
    let size = unsafe { GetFileVersionInfoSizeW(path.as_ref().as_os_str(), null_mut()) };
    if size == 0 {
        return Err(core::Error::from_win32().into());
    }

    let mut buffer = vec![0u8; size as usize];
    unsafe {
        GetFileVersionInfoW(
            path.as_ref().as_os_str(),
            0,
            size,
            buffer.as_mut_ptr() as *mut std::ffi::c_void,
        )
    }
    .ok()?;

    let mut pointer = null_mut();
    let mut length = 0;
    let success = unsafe {
        VerQueryValueW(
            buffer.as_ptr() as *const std::ffi::c_void,
            "\\",
            &mut pointer,
            &mut length,
        )
    }
    .as_bool();

    if !success || pointer.is_null() {
        return Err("Failed to query file version".into());
    }

    let info = pointer as *const VS_FIXEDFILEINFO;
    unsafe {
        if (*info).dwSignature != 0xfeef04bd {
            return Err("Invalid fixed file info signature".into());
        }

        Ok(format!(
            "{}.{}.{}",
            (*info).dwFileVersionMS >> 16,
            (*info).dwFileVersionMS & 0xffff,
            (*info).dwFileVersionLS >> 16,
        ))
    }
}

#[tauri::command]
async fn get_steam_path() -> Option<String> {
    find_sotf_executable().and_then(|path| path.to_str().map(str::to_owned))
}

fn find_sotf_executable() -> Option<PathBuf> {
    let steam_install = find_steam_install_directory()?;
    let library_regex = Regex::new(r#""path"\s+"([^"]+)""#).ok()?;
    let install_regex = Regex::new(r#""installdir"\s+"([^"]+)""#).ok()?;

    let mut steam_apps_directories = vec![steam_install.join("steamapps")];
    let library_file = steam_install.join("steamapps").join("libraryfolders.vdf");

    if let Ok(file) = File::open(library_file) {
        for line in BufReader::new(file).lines().map_while(Result::ok) {
            if let Some(capture) = library_regex.captures(&line) {
                let root = capture[1].replace(r"\\", r"\");
                steam_apps_directories.push(PathBuf::from(root).join("steamapps"));
            }
        }
    }

    let mut seen = HashSet::new();
    for steam_apps in steam_apps_directories {
        let normalized = steam_apps.to_string_lossy().to_lowercase();
        if !seen.insert(normalized) {
            continue;
        }

        let manifest = steam_apps.join(format!("appmanifest_{SOTF_APP_ID}.acf"));
        let Ok(file) = File::open(manifest) else {
            continue;
        };

        for line in BufReader::new(file).lines().map_while(Result::ok) {
            if let Some(capture) = install_regex.captures(&line) {
                let executable = steam_apps
                    .join("common")
                    .join(&capture[1])
                    .join(SOTF_EXECUTABLE);
                if executable.is_file() {
                    return Some(executable);
                }
            }
        }
    }

    None
}

fn find_steam_install_directory() -> Option<PathBuf> {
    let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);
    for key_path in [
        r"SOFTWARE\WOW6432Node\Valve\Steam",
        r"SOFTWARE\Valve\Steam",
    ] {
        if let Ok(key) = hklm.open_subkey_with_flags(key_path, KEY_READ) {
            if let Ok(value) = key.get_value::<String, _>("InstallPath") {
                return Some(PathBuf::from(value));
            }
        }
    }

    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    if let Ok(key) = hkcu.open_subkey_with_flags(r"SOFTWARE\Valve\Steam", KEY_READ) {
        for value_name in ["SteamPath", "InstallPath"] {
            if let Ok(value) = key.get_value::<String, _>(value_name) {
                return Some(PathBuf::from(value.replace('/', r"\")));
            }
        }
    }

    None
}

fn safe_zip_relative_path(name: &str) -> Result<PathBuf, String> {
    if name.is_empty() || name.chars().any(|character| character == '\0') {
        return Err("Archive entry has an invalid or empty path".to_string());
    }

    // ZIP paths use '/', but malformed archives occasionally contain Windows '\\'.
    // Normalize both so validation behaves identically on every build host.
    let normalized = name.replace('\\', "/");
    if normalized.starts_with('/') {
        return Err(format!("Unsafe absolute path in archive: {name}"));
    }

    let mut relative = PathBuf::new();
    for component in normalized.split('/') {
        if component.is_empty() || component == "." {
            continue;
        }
        if component == ".." {
            return Err(format!("Unsafe parent path in archive: {name}"));
        }

        validate_windows_archive_component(component, name)?;
        relative.push(component);
    }

    if relative.as_os_str().is_empty() {
        return Err("Archive entry has an empty path".to_string());
    }

    Ok(relative)
}

fn validate_windows_archive_component(component: &str, entry_name: &str) -> Result<(), String> {
    if component.contains(':') ||
       component.ends_with(' ') ||
       component.ends_with('.') ||
       component.chars().any(|character| {
           character.is_control() || matches!(character, '<' | '>' | '"' | '|' | '?' | '*')
       })
    {
        return Err(format!("Unsafe Windows filename in archive: {entry_name}"));
    }

    let base_name = component
        .split('.')
        .next()
        .unwrap_or(component)
        .to_ascii_uppercase();
    let reserved = matches!(
        base_name.as_str(),
        "CON" | "PRN" | "AUX" | "NUL" | "CONIN$" | "CONOUT$"
    ) ||
        (base_name.len() == 4 &&
         (base_name.starts_with("COM") || base_name.starts_with("LPT")) &&
         base_name.as_bytes()[3].is_ascii_digit() &&
         base_name.as_bytes()[3] != b'0');

    if reserved {
        return Err(format!("Reserved Windows filename in archive: {entry_name}"));
    }

    Ok(())
}

fn unzip_file(source: impl AsRef<Path>, destination: impl AsRef<Path>) -> Result<(), String> {
    let reader = File::open(source.as_ref()).map_err(|error| error.to_string())?;
    let mut archive = ZipArchive::new(reader).map_err(|error| error.to_string())?;
    if archive.len() > MAX_ARCHIVE_ENTRIES {
        return Err(format!(
            "Das Archiv enthält zu viele Einträge ({}; maximal {}).",
            archive.len(), MAX_ARCHIVE_ENTRIES
        ));
    }

    let mut total_uncompressed = 0u64;
    for index in 0..archive.len() {
        let file = archive.by_index(index).map_err(|error| error.to_string())?;
        if file.size() > MAX_ARCHIVE_FILE_BYTES {
            return Err(format!("Archivdatei ist zu groß: {}", file.name()));
        }
        total_uncompressed = total_uncompressed
            .checked_add(file.size())
            .ok_or_else(|| "Archivgröße ist ungültig.".to_string())?;
        if total_uncompressed > MAX_ARCHIVE_UNCOMPRESSED_BYTES {
            return Err("Das entpackte Archiv überschreitet die Sicherheitsgrenze von 2 GiB.".to_string());
        }
    }

    fs::create_dir_all(destination.as_ref()).map_err(|error| error.to_string())?;
    for index in 0..archive.len() {
        let mut file = archive.by_index(index).map_err(|error| error.to_string())?;
        if file
            .unix_mode()
            .map(|mode| mode & 0o170000 == 0o120000)
            .unwrap_or(false)
        {
            return Err(format!("Symbolische Links sind im Archiv nicht erlaubt: {}", file.name()));
        }

        let relative = safe_zip_relative_path(file.name())?;
        let output_path = destination.as_ref().join(relative);

        if file.is_dir() {
            fs::create_dir_all(&output_path).map_err(|error| error.to_string())?;
            continue;
        }

        if let Some(parent) = output_path.parent() {
            fs::create_dir_all(parent).map_err(|error| error.to_string())?;
        }

        let mut output = File::create(&output_path).map_err(|error| error.to_string())?;
        std::io::copy(&mut file, &mut output).map_err(|error| error.to_string())?;
    }

    Ok(())
}

#[tauri::command]
fn unzip_handler(source: String, destination: String) -> Result<(), String> {
    unzip_file(source, destination)
}

fn game_directory_from_executable(game_exe: &str) -> Result<PathBuf, String> {
    if !validate_game_executable(game_exe.to_string()) {
        return Err("Wähle zuerst eine gültige SonsOfTheForest.exe aus.".to_string());
    }

    PathBuf::from(game_exe)
        .parent()
        .map(Path::to_path_buf)
        .ok_or_else(|| "Der Spielordner konnte nicht ermittelt werden.".to_string())
}

fn local_data_directory() -> PathBuf {
    std::env::var_os("LOCALAPPDATA")
        .map(PathBuf::from)
        .unwrap_or_else(std::env::temp_dir)
        .join("Crazy_Batto")
        .join("SOTFDeathCounter")
}

fn bundled_mod_status(game_exe: &str) -> Result<BundledModStatus, String> {
    let game_directory = game_directory_from_executable(game_exe)?;
    let mods_directory = game_directory.join("Mods");
    let enabled_path = mods_directory.join(format!("{BUNDLED_ASSEMBLY_NAME}.dll"));
    let disabled_path = mods_directory.join(format!("{BUNDLED_ASSEMBLY_NAME}.disabled"));
    let manifest_path = mods_directory.join(BUNDLED_ASSEMBLY_NAME).join("manifest.json");

    let version = fs::read_to_string(&manifest_path)
        .ok()
        .and_then(|text| serde_json::from_str::<serde_json::Value>(&text).ok())
        .and_then(|json| json.get("version").and_then(|value| value.as_str()).map(str::to_owned));

    let data_directory = local_data_directory();
    Ok(BundledModStatus {
        installed: enabled_path.is_file() || disabled_path.is_file(),
        enabled: enabled_path.is_file(),
        version,
        assembly_path: if enabled_path.is_file() {
            enabled_path.to_string_lossy().into_owned()
        } else {
            disabled_path.to_string_lossy().into_owned()
        },
        manifest_path: manifest_path.to_string_lossy().into_owned(),
        settings_path: data_directory.join("settings.json").to_string_lossy().into_owned(),
        stats_path: data_directory.join("stats.json").to_string_lossy().into_owned(),
        overlay_url: "http://127.0.0.1:19447/overlay".to_string(),
    })
}

#[tauri::command]
fn get_bundled_mod_status(game_exe: String) -> Result<BundledModStatus, String> {
    bundled_mod_status(&game_exe)
}

fn resolve_bundled_mod_archive(app: &tauri::AppHandle) -> Result<PathBuf, String> {
    for candidate in [
        format!("resources/{BUNDLED_MOD_ARCHIVE}"),
        BUNDLED_MOD_ARCHIVE.to_string(),
    ] {
        if let Some(path) = app.path_resolver().resolve_resource(candidate) {
            if path.is_file() {
                return Ok(path);
            }
        }
    }

    Err("Das eingebettete Quellarchiv des Todeszählers fehlt in den Programmressourcen.".to_string())
}

#[tauri::command]
async fn build_and_install_bundled_mod(
    app: tauri::AppHandle,
    game_exe: String,
) -> Result<BundledModBuildResult, String> {
    tokio::task::spawn_blocking(move || build_and_install_bundled_mod_sync(&app, &game_exe))
        .await
        .map_err(|error| format!("Der Build-Task ist fehlgeschlagen: {error}"))?
}

fn build_and_install_bundled_mod_sync(
    app: &tauri::AppHandle,
    game_exe: &str,
) -> Result<BundledModBuildResult, String> {
    let game_directory = game_directory_from_executable(game_exe)?;
    if !game_directory
        .join("_RedLoader")
        .join("net6")
        .join("SonsSdk.dll")
        .is_file()
    {
        return Ok(BundledModBuildResult {
            success: false,
            message: "Installiere RedLoader und starte das Spiel damit mindestens einmal, bevor du die Mod baust.".to_string(),
            stdout: String::new(),
            stderr: String::new(),
            build_directory: String::new(),
            installed_path: None,
        });
    }

    if !is_dotnet6_sdk_installed() {
        return Ok(BundledModBuildResult {
            success: false,
            message: "Für den lokalen Mod-Build wird das .NET 6 SDK benötigt.".to_string(),
            stdout: String::new(),
            stderr: String::new(),
            build_directory: String::new(),
            installed_path: None,
        });
    }

    let archive = resolve_bundled_mod_archive(app)?;
    let build_directory = std::env::temp_dir()
        .join("CrazyBattoRedManager")
        .join(BUNDLED_ASSEMBLY_NAME);
    if build_directory.exists() {
        fs::remove_dir_all(&build_directory).map_err(|error| error.to_string())?;
    }
    fs::create_dir_all(&build_directory).map_err(|error| error.to_string())?;
    unzip_file(&archive, &build_directory)?;

    let project = build_directory.join("CrazyBatto.SotfDeathCounter.csproj");
    let output_directory = build_directory.join("build-output");
    fs::create_dir_all(&output_directory).map_err(|error| error.to_string())?;

    let output = Command::new("dotnet")
        .arg("build")
        .arg(&project)
        .arg("--configuration")
        .arg("Release")
        .arg("--nologo")
        .arg("--output")
        .arg(&output_directory)
        .arg(format!("-p:GameDir={}", game_directory.to_string_lossy()))
        .output()
        .map_err(|error| format!("dotnet build konnte nicht gestartet werden: {error}"))?;

    let stdout = String::from_utf8_lossy(&output.stdout).into_owned();
    let stderr = String::from_utf8_lossy(&output.stderr).into_owned();
    if !output.status.success() {
        return Ok(BundledModBuildResult {
            success: false,
            message: "Der Build des Todeszählers ist fehlgeschlagen. Details stehen in der Build-Ausgabe.".to_string(),
            stdout,
            stderr,
            build_directory: build_directory.to_string_lossy().into_owned(),
            installed_path: None,
        });
    }

    let built_dll = output_directory.join(format!("{BUNDLED_ASSEMBLY_NAME}.dll"));
    if !built_dll.is_file() {
        return Ok(BundledModBuildResult {
            success: false,
            message: "dotnet meldete Erfolg, aber die erwartete Mod-DLL wurde nicht erzeugt.".to_string(),
            stdout,
            stderr,
            build_directory: build_directory.to_string_lossy().into_owned(),
            installed_path: None,
        });
    }

    let mods_directory = game_directory.join("Mods");
    fs::create_dir_all(&mods_directory).map_err(|error| error.to_string())?;

    let enabled_dll = mods_directory.join(format!("{BUNDLED_ASSEMBLY_NAME}.dll"));
    let disabled_dll = mods_directory.join(format!("{BUNDLED_ASSEMBLY_NAME}.disabled"));
    let staged_dll = mods_directory.join(format!("{BUNDLED_ASSEMBLY_NAME}.dll.new"));
    let backup_dll = mods_directory.join(format!("{BUNDLED_ASSEMBLY_NAME}.dll.backup"));

    let manifest_directory = mods_directory.join(BUNDLED_ASSEMBLY_NAME);
    fs::create_dir_all(&manifest_directory).map_err(|error| error.to_string())?;
    let built_manifest = build_directory.join("manifest.json");
    if !built_manifest.is_file() {
        return Ok(BundledModBuildResult {
            success: false,
            message: "Der Mod-Build enthält kein Manifest und wird deshalb nicht installiert.".to_string(),
            stdout,
            stderr,
            build_directory: build_directory.to_string_lossy().into_owned(),
            installed_path: None,
        });
    }

    let manifest_path = manifest_directory.join("manifest.json");
    let staged_manifest = manifest_directory.join("manifest.json.new");
    let backup_manifest = manifest_directory.join("manifest.json.backup");

    for stale in [&staged_dll, &backup_dll, &staged_manifest, &backup_manifest] {
        if stale.exists() {
            fs::remove_file(stale).map_err(|error| error.to_string())?;
        }
    }

    fs::copy(&built_dll, &staged_dll).map_err(|error| {
        format!("Die neue Mod-DLL konnte nicht vorbereitet werden: {error}")
    })?;
    if let Err(error) = fs::copy(&built_manifest, &staged_manifest) {
        let _ = fs::remove_file(&staged_dll);
        return Err(format!("Das neue Mod-Manifest konnte nicht vorbereitet werden: {error}"));
    }

    // Preserve the user's enabled/disabled state across updates. A first install is enabled.
    let preserve_disabled = !enabled_dll.is_file() && disabled_dll.is_file();
    let previous_dll = if enabled_dll.is_file() {
        Some(enabled_dll.clone())
    } else if disabled_dll.is_file() {
        Some(disabled_dll.clone())
    } else {
        None
    };
    let target_dll = if preserve_disabled {
        disabled_dll.clone()
    } else {
        enabled_dll.clone()
    };
    let alternate_dll = if preserve_disabled {
        enabled_dll.clone()
    } else {
        disabled_dll.clone()
    };

    if let Some(previous) = previous_dll.as_ref() {
        if let Err(error) = fs::rename(previous, &backup_dll) {
            let _ = fs::remove_file(&staged_dll);
            let _ = fs::remove_file(&staged_manifest);
            return Err(format!(
                "Die vorhandene Mod-DLL konnte nicht für das Update gesichert werden. Schließe Sons of the Forest und versuche es erneut: {error}"
            ));
        }
    }

    let had_manifest = manifest_path.is_file();
    if had_manifest {
        if let Err(error) = fs::rename(&manifest_path, &backup_manifest) {
            if let Some(previous) = previous_dll.as_ref() {
                let _ = fs::rename(&backup_dll, previous);
            }
            let _ = fs::remove_file(&staged_dll);
            let _ = fs::remove_file(&staged_manifest);
            return Err(format!("Das vorhandene Mod-Manifest konnte nicht gesichert werden: {error}"));
        }
    }

    if let Err(error) = fs::rename(&staged_dll, &target_dll) {
        if let Some(previous) = previous_dll.as_ref() {
            let _ = fs::rename(&backup_dll, previous);
        }
        if had_manifest && backup_manifest.exists() {
            let _ = fs::rename(&backup_manifest, &manifest_path);
        }
        let _ = fs::remove_file(&staged_dll);
        let _ = fs::remove_file(&staged_manifest);
        return Err(format!(
            "Die Mod-DLL konnte nicht installiert werden. Die vorherige Version wurde nach Möglichkeit wiederhergestellt: {error}"
        ));
    }

    if let Err(error) = fs::rename(&staged_manifest, &manifest_path) {
        let _ = fs::remove_file(&target_dll);
        if let Some(previous) = previous_dll.as_ref() {
            let _ = fs::rename(&backup_dll, previous);
        }
        if had_manifest && backup_manifest.exists() {
            let _ = fs::rename(&backup_manifest, &manifest_path);
        }
        let _ = fs::remove_file(&staged_manifest);
        return Err(format!(
            "Das Mod-Manifest konnte nicht installiert werden. Die vorherige Version wurde nach Möglichkeit wiederhergestellt: {error}"
        ));
    }

    for obsolete in [&backup_dll, &backup_manifest, &alternate_dll] {
        if obsolete.is_file() {
            let _ = fs::remove_file(obsolete);
        }
    }

    let built_pdb = output_directory.join(format!("{BUNDLED_ASSEMBLY_NAME}.pdb"));
    if built_pdb.is_file() {
        let _ = fs::copy(built_pdb, mods_directory.join(format!("{BUNDLED_ASSEMBLY_NAME}.pdb")));
    }

    Ok(BundledModBuildResult {
        success: true,
        message: format!(
            "CrazyBatto SOTF Death Counter {BUNDLED_MOD_VERSION} wurde erfolgreich gebaut und installiert{}.",
            if preserve_disabled { " (deaktivierter Zustand beibehalten)" } else { "" }
        ),
        stdout,
        stderr,
        build_directory: build_directory.to_string_lossy().into_owned(),
        installed_path: Some(target_dll.to_string_lossy().into_owned()),
    })
}

#[tauri::command]
fn uninstall_bundled_mod(game_exe: String) -> Result<BundledModStatus, String> {
    let game_directory = game_directory_from_executable(&game_exe)?;
    let mods_directory = game_directory.join("Mods");

    for file_name in [
        format!("{BUNDLED_ASSEMBLY_NAME}.dll"),
        format!("{BUNDLED_ASSEMBLY_NAME}.disabled"),
        format!("{BUNDLED_ASSEMBLY_NAME}.pdb"),
        format!("{BUNDLED_ASSEMBLY_NAME}.dll.new"),
        format!("{BUNDLED_ASSEMBLY_NAME}.dll.backup"),
    ] {
        let path = mods_directory.join(file_name);
        if path.is_file() {
            fs::remove_file(path).map_err(|error| error.to_string())?;
        }
    }

    let manifest_directory = mods_directory.join(BUNDLED_ASSEMBLY_NAME);
    if manifest_directory.is_dir() {
        fs::remove_dir_all(manifest_directory).map_err(|error| error.to_string())?;
    }

    // User statistics and settings in LocalAppData are deliberately preserved.
    bundled_mod_status(&game_exe)
}

#[cfg(test)]
mod tests {
    use super::safe_zip_relative_path;
    use std::path::PathBuf;

    #[test]
    fn zip_path_validation_accepts_normal_relative_paths() {
        assert_eq!(
            safe_zip_relative_path("Mods/Example/manifest.json").unwrap(),
            PathBuf::from("Mods").join("Example").join("manifest.json")
        );
        assert_eq!(
            safe_zip_relative_path(r"Mods\Example.dll").unwrap(),
            PathBuf::from("Mods").join("Example.dll")
        );
    }

    #[test]
    fn zip_path_validation_rejects_traversal_and_windows_special_names() {
        for unsafe_name in [
            "../outside.dll",
            "Mods/../../outside.dll",
            "/absolute/file.dll",
            r"C:\absolute\file.dll",
            r"\\server\share\file.dll",
            "Mods/CON",
            "Mods/LPT1.txt",
            "Mods/CONOUT$.txt",
            "Mods/file:stream",
            "Mods/file?.dll",
            "Mods/trailing.",
            "Mods/trailing ",
        ] {
            assert!(
                safe_zip_relative_path(unsafe_name).is_err(),
                "unsafe path unexpectedly accepted: {unsafe_name}"
            );
        }
    }
}

fn main() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![
            unzip_handler,
            get_steam_path,
            validate_game_executable,
            is_dotnet6_installed,
            is_dotnet6_sdk_installed,
            get_file_version,
            get_bundled_mod_status,
            build_and_install_bundled_mod,
            uninstall_bundled_mod
        ])
        .plugin(tauri_plugin_upload::init())
        .run(tauri::generate_context!())
        .expect("error while running CrazyBatto RedManager");
}
