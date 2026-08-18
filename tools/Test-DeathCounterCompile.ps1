[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$ModProject = Join-Path $Root 'bundled-mods\CrazyBatto.SotfDeathCounter\CrazyBatto.SotfDeathCounter.csproj'
$Work = Join-Path $env:TEMP 'CrazyBattoRedManager\DeathCounterCompileTest'
$FakeGame = Join-Path $Work 'Game'
$Net6 = Join-Path $FakeGame '_RedLoader\net6'
$Unity = Join-Path $FakeGame '_RedLoader\unity-libs'
$StubRoot = Join-Path $Work 'Stubs'
$Output = Join-Path $Work 'ModOutput'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK was not found.'
}
if (Test-Path $Work) { Remove-Item $Work -Recurse -Force }
New-Item -ItemType Directory -Path $Net6, $Unity, $StubRoot, $Output -Force | Out-Null
New-Item -ItemType File -Path (Join-Path $FakeGame 'SonsOfTheForest.exe') -Force | Out-Null

function Build-StubAssembly {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$AssemblyName,
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $projectDir = Join-Path $StubRoot $Name
    New-Item -ItemType Directory -Path $projectDir -Force | Out-Null
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <AssemblyName>$AssemblyName</AssemblyName>
    <RootNamespace>$Name</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>10.0</LangVersion>
  </PropertyGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $projectDir "$Name.csproj") -Encoding utf8
    $Source | Set-Content -LiteralPath (Join-Path $projectDir 'Stub.cs') -Encoding utf8

    & dotnet build (Join-Path $projectDir "$Name.csproj") -c Release --nologo --output (Join-Path $projectDir 'out')
    if ($LASTEXITCODE -ne 0) { throw "Stub build failed: $Name" }

    Copy-Item (Join-Path $projectDir "out\$AssemblyName.dll") (Join-Path $Destination "$AssemblyName.dll") -Force
}

Build-StubAssembly -Name 'RedLoaderStub' -AssemblyName 'RedLoader' -Destination $Net6 -Source @'
namespace RedLoader;
public static class Placeholder { }
'@

Build-StubAssembly -Name 'SonsSdkStub' -AssemblyName 'SonsSdk' -Destination $Net6 -Source @'
namespace SonsSdk
{
    public enum ESonsScene
    {
        Title = 0,
        Loading = 1,
        Game = 2
    }

    public abstract class SonsMod
    {
        protected bool HarmonyPatchAll { get; set; }
        protected virtual void OnInitializeMod() { }
        protected virtual void OnSdkInitialized() { }
        protected virtual void OnGameStart() { }
        protected virtual void OnSonsSceneInitialized(ESonsScene sonsScene) { }
        protected void Log(object message) { }
    }
}

namespace SonsSdk.Attributes
{
    public interface IOnInWorldUpdateReceiver
    {
        void OnInWorldUpdate();
    }
}
'@

Build-StubAssembly -Name 'HarmonyStub' -AssemblyName '0Harmony' -Destination $Net6 -Source @'
using System.Reflection;

namespace HarmonyLib
{
    public sealed class Harmony
    {
        public Harmony(string id) { }
        public void Patch(
            MethodBase original,
            HarmonyMethod? prefix = null,
            HarmonyMethod? postfix = null,
            HarmonyMethod? transpiler = null,
            HarmonyMethod? finalizer = null) { }
    }

    public sealed class HarmonyMethod
    {
        public HarmonyMethod(MethodInfo? method) { }
    }

    public static class AccessTools
    {
        public static MethodInfo? Method(Type type, string name) => null;
    }
}
'@

Build-StubAssembly -Name 'UnityCoreStub' -AssemblyName 'UnityEngine.CoreModule' -Destination $Unity -Source @'
namespace UnityEngine
{
    public class Object
    {
        public static T[] FindObjectsOfType<T>() where T : Object => System.Array.Empty<T>();
        public string name { get; set; } = string.Empty;
        public int GetInstanceID() => 0;
    }

    public class GameObject : Object { }

    public class Component : Object
    {
        public Transform transform => null!;
        public GameObject gameObject => null!;
    }

    public class MonoBehaviour : Component { }

    public class Transform : Component
    {
        public Transform root => null!;
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component => System.Array.Empty<T>();
    }
}
'@

Build-StubAssembly -Name 'UnityFacadeStub' -AssemblyName 'UnityEngine' -Destination $Unity -Source @'
namespace UnityEngine;
public static class FacadePlaceholder { }
'@

& dotnet build $ModProject -c Release --nologo --output $Output `
    "-p:GameDir=$FakeGame" `
    "-p:RedLoaderRoot=$(Join-Path $FakeGame '_RedLoader')" `
    "-p:RedLoaderNet6Dir=$Net6" `
    "-p:UnityReferenceDir=$Unity"
if ($LASTEXITCODE -ne 0) { throw "Death-counter compile test failed with exit code $LASTEXITCODE" }

$Dll = Join-Path $Output 'CrazyBatto.SotfDeathCounter.dll'
if (-not (Test-Path -LiteralPath $Dll -PathType Leaf)) {
    throw "Compile test did not create $Dll"
}

Write-Host "Death-counter compile test succeeded: $Dll"
