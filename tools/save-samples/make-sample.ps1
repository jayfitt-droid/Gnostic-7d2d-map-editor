# Writes a sample project save with a released version's own code, for the save-compatibility tests.
# Run it once for each new release, after its tag exists:
#   powershell -File tools/save-samples/make-sample.ps1 -Tag v2.0.0
# It writes tests/MapEditor.Core.Tests/SaveSamples/<tag>.7dmap and <tag>-preset.json. Commit both, then add the tag
# to SaveCompatibilityTests.Samples. Never change or remake a sample that is already committed: it stands for the
# files users already have.
param(
    [Parameter(Mandatory)] [string] $Tag,
    # FORMAT1 for v1.2.0 and older; NO_GENERATOR for releases before the Generate panel (v1.2.x).
    [string] $Defines = ""
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..\..").Path
$work = Join-Path ([IO.Path]::GetTempPath()) "mapeditor-sample-$Tag"
$samples = Join-Path $repo "tests\MapEditor.Core.Tests\SaveSamples"
$out = Join-Path $samples "$Tag.7dmap"
if (Test-Path $out) { throw "$out exists already. Samples are never remade." }

if (Test-Path $work) { git -C $repo worktree remove --force $work }
git -C $repo worktree add --detach $work $Tag
if ($LASTEXITCODE -ne 0) { throw "Could not check out $Tag." }
try {
    $gen = Join-Path $work "save-samples"
    New-Item -ItemType Directory -Force $gen | Out-Null
    Copy-Item "$PSScriptRoot\Program.cs" $gen
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <DefineConstants>`$(DefineConstants);$Defines</DefineConstants>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\src\MapEditor.Core\MapEditor.Core.csproj" />
  </ItemGroup>
</Project>
"@ | Set-Content -Encoding utf8 (Join-Path $gen "save-samples.csproj")
    $outputs = @($out)
    if ($Defines -notmatch 'NO_GENERATOR') { $outputs += Join-Path $samples "$Tag-preset.json" }
    dotnet run --project (Join-Path $gen "save-samples.csproj") -c Release -- $outputs
    if ($LASTEXITCODE -ne 0) { throw "The sample program failed." }
}
finally {
    git -C $repo worktree remove --force $work
}
