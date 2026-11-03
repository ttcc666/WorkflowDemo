[CmdletBinding()]
param([switch]$SkipBuild, [switch]$RunBrowser)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'commands.ps1')
$properties = [xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props') -Raw)
$version = [string]$properties.Project.PropertyGroup.ApprovalVersion
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Invalid release version' }
$webRoot = Join-Path $repositoryRoot 'web'
$uiPackage = Get-Content -LiteralPath (Join-Path $webRoot 'packages/approval-ui/package.json') -Raw | ConvertFrom-Json
if ($uiPackage.version -ne $version) { throw 'NuGet and npm release versions differ' }
$packages = Join-Path $repositoryRoot ('output/packages/' + $version)
$frameworkPackages = @('Approval.Core', 'Approval.AspNetCore', 'Approval.Forms', 'Approval.Forms.AspNetCore')
New-Item -ItemType 'Directory' -Path $packages -Force | Out-Null
if (-not $SkipBuild) {
    Invoke-CheckedCommand 'dotnet' @('build', 'Workflow.slnx', '--configuration', 'Release') $repositoryRoot
    Invoke-CheckedCommand 'pnpm' @('build:lib') $webRoot
}
foreach ($package in $frameworkPackages) {
    Invoke-CheckedCommand 'dotnet' @('pack', ('src/' + $package), '--configuration', 'Release', '--no-build', '--output', $packages) $repositoryRoot
}
$tarball = Join-Path $packages ('approval-ui-' + $version + '.tgz')
Invoke-CheckedCommand 'pnpm' @('pack', '--out', $tarball) (Join-Path $webRoot 'packages/approval-ui')

$consumerRoot = Join-Path $repositoryRoot ('output/engineering/consumer-' + [Guid]::NewGuid().ToString('N'))
$apiRoot = Join-Path $consumerRoot 'api'
$sampleRoot = Join-Path $consumerRoot 'samples/Approval.Certificate'
$referenceRoot = Join-Path $consumerRoot 'samples/Approval.ReferenceHost'
$uiRoot = Join-Path $consumerRoot 'web'
$feed = Join-Path $consumerRoot 'feed'
New-Item -ItemType 'Directory' -Path $apiRoot,$uiRoot,$feed -Force | Out-Null

function Copy-HostSources {
    param([string]$Source, [string]$Destination)
    $sourceRoot = [IO.Path]::GetFullPath($Source)
    $destinationRoot = [IO.Path]::GetFullPath($Destination) + [IO.Path]::DirectorySeparatorChar
    $pendingDirectories = [Collections.Generic.Stack[string]]::new()
    $pendingDirectories.Push($sourceRoot)
    $files = [Collections.Generic.List[string]]::new()
    while ($pendingDirectories.Count -gt 0) {
        foreach ($entry in (Get-ChildItem -LiteralPath $pendingDirectories.Pop() -Force)) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
            if ($entry.PSIsContainer) {
                if ($entry.Name -notin @('bin', 'obj', 'node_modules', 'wwwroot', 'App_Data', 'dist')) { $pendingDirectories.Push($entry.FullName) }
            }
            elseif ($entry.Name -notlike '*.db*' -and $entry.Name -notlike '*.test.ts') { $files.Add($entry.FullName) }
        }
    }
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($sourceRoot, $file)
        $target = [IO.Path]::GetFullPath((Join-Path $Destination $relative))
        if (-not $target.StartsWith($destinationRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid consumer path' }
        New-Item -ItemType 'Directory' -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $file -Destination $target
    }
}
Copy-HostSources (Join-Path $repositoryRoot 'samples/Certificate.Api') $apiRoot
Copy-HostSources (Join-Path $repositoryRoot 'samples/Approval.Certificate') $sampleRoot
Copy-HostSources (Join-Path $repositoryRoot 'samples/Approval.ReferenceHost') $referenceRoot
Copy-HostSources (Join-Path $webRoot 'apps/demo') $uiRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'global.json') -Destination $consumerRoot
'<Project />' | Set-Content -LiteralPath (Join-Path $consumerRoot 'Directory.Build.props')
'<Project />' | Set-Content -LiteralPath (Join-Path $consumerRoot 'Directory.Build.targets')
'<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>' | Set-Content -LiteralPath (Join-Path $consumerRoot 'Directory.Packages.props')
$cachedPackages = ((& dotnet nuget locals global-packages --list) -replace '^[^:]+:\s*', '').Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot locate the NuGet dependency cache' }
('<configuration><packageSources><clear/><add key="release" value="feed"/><add key="cached" value="{0}"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="release"><package pattern="Approval.*"/></packageSource><packageSource key="cached"><package pattern="*"/></packageSource><packageSource key="nuget"><package pattern="*"/></packageSource></packageSourceMapping></configuration>' -f [Security.SecurityElement]::Escape($cachedPackages)) | Set-Content -LiteralPath (Join-Path $consumerRoot 'NuGet.Config')
$versions = [xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Packages.props') -Raw)
$efVersion = ($versions.Project.ItemGroup.PackageVersion | Where-Object Include -eq 'Microsoft.EntityFrameworkCore.Sqlite').Version
# The copied business sample consumes framework packages; it is not itself a framework package.
$sampleProjectPath = Join-Path $sampleRoot 'Approval.Certificate.csproj'
$sampleProject = [xml](Get-Content -LiteralPath $sampleProjectPath -Raw)
foreach ($property in ([ordered]@{ TargetFramework = 'net10.0'; ImplicitUsings = 'enable'; Nullable = 'enable'; Version = $version }).GetEnumerator()) {
    $element = $sampleProject.SelectSingleNode('/Project/PropertyGroup/' + $property.Key)
    if ($null -eq $element) {
        $element = $sampleProject.CreateElement($property.Key)
        $sampleProject.Project.PropertyGroup.AppendChild($element) | Out-Null
    }
    $element.InnerText = $property.Value
}
foreach ($reference in $sampleProject.SelectNodes('/Project/ItemGroup/PackageReference')) {
    $dependency = $versions.Project.ItemGroup.PackageVersion | Where-Object Include -eq $reference.Include
    if ($null -eq $dependency) { throw ('Missing sample dependency version: ' + $reference.Include) }
    $reference.SetAttribute('Version', $dependency.Version)
}
foreach ($reference in $sampleProject.SelectNodes('/Project/ItemGroup/ProjectReference')) {
    $name = [IO.Path]::GetFileNameWithoutExtension($reference.Include)
    if ($name -notin $frameworkPackages) { throw ('Unexpected sample project reference: ' + $reference.Include) }
    $packageReference = $sampleProject.CreateElement('PackageReference')
    $packageReference.SetAttribute('Include', $name)
    $packageReference.SetAttribute('Version', $version)
    $reference.ParentNode.ReplaceChild($packageReference, $reference) | Out-Null
}
$sampleProject.Save($sampleProjectPath)
$project = @'
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><IsPackable>false</IsPackable></PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../samples/Approval.Certificate/Approval.Certificate.csproj" />
    <PackageReference Include="Approval.Forms.AspNetCore" Version="{1}" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="{0}" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="{0}" PrivateAssets="all" />
    <Content Update="Flows/*.json" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
'@ -f $efVersion, $version
$project | Set-Content -LiteralPath (Join-Path $apiRoot 'Certificate.Api.csproj')
foreach ($name in $frameworkPackages) {
    Copy-Item -LiteralPath (Join-Path $packages ($name + '.' + $version + '.nupkg')) -Destination $feed
}
Invoke-CheckedCommand 'dotnet' @('build', 'Certificate.Api.csproj', '--configuration', 'Release', ('-p:RestorePackagesPath=' + (Join-Path $consumerRoot 'nuget-packages'))) $apiRoot
$deps = Get-Content -LiteralPath (Join-Path $apiRoot 'bin/Release/net10.0/Certificate.Api.deps.json') -Raw | ConvertFrom-Json
foreach ($name in $frameworkPackages) {
    if ($deps.libraries.($name + '/' + $version).type -ne 'package') { throw ('Consumer did not load a NuGet package: ' + $name) }
    $expectedHash = 'sha512-' + [Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData([IO.File]::ReadAllBytes((Join-Path $feed ($name + '.' + $version + '.nupkg')))))
    if ($deps.libraries.($name + '/' + $version).sha512 -cne $expectedHash) { throw ('Consumer restored stale package bytes: ' + $name) }
}
if ($deps.libraries.('Approval.Certificate/' + $version).type -ne 'project') { throw 'The business sample must be built from the copied sample project.' }
$referenceProjectPath = Join-Path $referenceRoot 'Approval.ReferenceHost.csproj'
$referenceProject = [xml](Get-Content -LiteralPath $referenceProjectPath -Raw)
foreach ($property in ([ordered]@{ TargetFramework = 'net10.0'; ImplicitUsings = 'enable'; Nullable = 'enable' }).GetEnumerator()) {
    $element = $referenceProject.CreateElement($property.Key)
    $element.InnerText = $property.Value
    $referenceProject.Project.PropertyGroup.AppendChild($element) | Out-Null
}
foreach ($reference in $referenceProject.SelectNodes('/Project/ItemGroup/PackageReference')) {
    $dependency = $versions.Project.ItemGroup.PackageVersion | Where-Object Include -eq $reference.Include
    if ($null -eq $dependency) { throw ('Missing reference-host dependency version: ' + $reference.Include) }
    $reference.SetAttribute('Version', $dependency.Version)
}
foreach ($reference in $referenceProject.SelectNodes('/Project/ItemGroup/ProjectReference')) {
    $name = [IO.Path]::GetFileNameWithoutExtension($reference.Include)
    if ($name -notin $frameworkPackages) { throw ('Unexpected reference-host project dependency: ' + $name) }
    $packageReference = $referenceProject.CreateElement('PackageReference')
    $packageReference.SetAttribute('Include', $name)
    $packageReference.SetAttribute('Version', $version)
    $reference.ParentNode.ReplaceChild($packageReference, $reference) | Out-Null
}
$referenceProject.Save($referenceProjectPath)
Invoke-CheckedCommand 'dotnet' @('build', 'Approval.ReferenceHost.csproj', '--configuration', 'Release', ('-p:RestorePackagesPath=' + (Join-Path $consumerRoot 'nuget-packages'))) $referenceRoot
$referenceDeps = Get-Content -LiteralPath (Join-Path $referenceRoot 'bin/Release/net10.0/Approval.ReferenceHost.deps.json') -Raw | ConvertFrom-Json
foreach ($name in $frameworkPackages) {
    $dependency = $referenceDeps.libraries.($name + '/' + $version)
    $expectedHash = 'sha512-' + [Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData([IO.File]::ReadAllBytes((Join-Path $feed ($name + '.' + $version + '.nupkg')))))
    if ($dependency.type -ne 'package' -or $dependency.sha512 -cne $expectedHash) { throw ('Reference host did not consume the validated package: ' + $name) }
}
Invoke-CheckedCommand 'pwsh' @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'verify-reference-host.ps1'), '-SkipBuild', '-SamplePath', $referenceRoot) $repositoryRoot
foreach ($name in @('certificate.create.json', 'certificate.update.json', 'certificate.revoke.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $apiRoot ('bin/Release/net10.0/Flows/' + $name)))) { throw ('Missing sample workflow: ' + $name) }
}

$packagePath = Join-Path $uiRoot 'package.json'
$package = Get-Content -LiteralPath $packagePath -Raw | ConvertFrom-Json
$package.name = 'approval-package-consumer'
$package.dependencies.'@approval/ui' = 'file:./approval-ui.tgz'
foreach ($section in @('dependencies', 'devDependencies')) {
    foreach ($dependency in $package.$section.PSObject.Properties) { $dependency.Value = $dependency.Value -replace '^[~^]', '' }
}
$package | Add-Member -NotePropertyName 'packageManager' -NotePropertyValue 'pnpm@10.20.0' -Force
$package | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $packagePath
Copy-Item -LiteralPath $tarball -Destination (Join-Path $uiRoot 'approval-ui.tgz')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'consumer.vite.config.ts') -Destination (Join-Path $uiRoot 'vite.config.ts')
$main = Join-Path $uiRoot 'src/main.ts'
('import ''@approval/ui/style.css''' + [Environment]::NewLine + (Get-Content -LiteralPath $main -Raw)) | Set-Content -LiteralPath $main
@'
import type { FlowDefinition, FormSchema } from '@approval/ui/contracts'
export const form: FormSchema = { fields: [{ key: 'name', label: 'Name', type: 'Text' }] }
export const flow: FlowDefinition = { schemaVersion: 1, startNodeId: 'start', nodes: {}, form }
'@ | Set-Content -LiteralPath (Join-Path $uiRoot 'src/contractsSmoke.ts')
Invoke-CheckedCommand 'pnpm' @('install', '--ignore-workspace', '--no-frozen-lockfile') $uiRoot
$installed = Get-Content -LiteralPath (Join-Path $uiRoot 'node_modules/@approval/ui/package.json') -Raw | ConvertFrom-Json
if ($installed.version -ne $version -or $installed.exports.'.'.import -ne './dist/index.js') { throw 'Consumer resolved source exports instead of the release package' }
Invoke-CheckedCommand 'pnpm' @('build') $uiRoot

# Core-only real package consumer: existing IdentityDbContext + host SaveChanges override.
$embeddedRoot = Join-Path $consumerRoot 'embedded-host'
Copy-HostSources (Join-Path $repositoryRoot 'eng/consumers/EmbeddedHost') $embeddedRoot
$embeddedProjectPath = Join-Path $embeddedRoot 'EmbeddedHost.csproj'
$embeddedProject = [xml](Get-Content -LiteralPath $embeddedProjectPath -Raw)
foreach ($reference in $embeddedProject.SelectNodes('/Project/ItemGroup/PackageReference')) {
    if ($reference.Include -like 'Approval.*') { $reference.SetAttribute('Version', $version) }
    else {
        $dependency = $versions.Project.ItemGroup.PackageVersion | Where-Object Include -eq $reference.Include
        if ($null -eq $dependency) { throw ('Unknown consumer dependency: ' + $reference.Include) }
        $reference.SetAttribute('Version', $dependency.Version)
    }
}
$embeddedProject.Save($embeddedProjectPath)
Invoke-CheckedCommand 'dotnet' @('build', 'EmbeddedHost.csproj', '--configuration', 'Release', ('-p:RestorePackagesPath=' + (Join-Path $consumerRoot 'nuget-packages'))) $embeddedRoot
$embeddedDeps = Get-Content -LiteralPath (Join-Path $embeddedRoot 'bin/Release/net10.0/EmbeddedHost.deps.json') -Raw | ConvertFrom-Json
if (@($embeddedDeps.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'Approval.Forms/*' -or $_ -like 'Approval.Forms.AspNetCore/*' }).Count -gt 0) { throw 'Core-only consumer implicitly depends on Forms.' }
foreach ($name in @('Approval.Core', 'Approval.AspNetCore')) {
    if ($embeddedDeps.libraries.($name + '/' + $version).type -ne 'package') { throw ('Embedded consumer did not use package: ' + $name) }
}
$savedConsumerDatabase = $env:APPROVAL_CONSUMER_DATABASE
$savedConsumerReport = $env:APPROVAL_CONSUMER_REPORT
try {
    $env:APPROVAL_CONSUMER_DATABASE = Join-Path $embeddedRoot 'host.db'
    $env:APPROVAL_CONSUMER_REPORT = Join-Path $embeddedRoot 'verification.json'
    Invoke-CheckedCommand 'dotnet' @('ef', 'migrations', 'add', 'InitialConsumer', '--project', $embeddedRoot, '--context', 'InvoiceDbContext', '--configuration', 'Release', '--no-build', '--output-dir', 'Migrations') $repositoryRoot
    Invoke-CheckedCommand 'dotnet' @('run', '--project', 'EmbeddedHost.csproj', '--configuration', 'Release', '--no-restore') $embeddedRoot
    $embeddedEvidence = Get-Content -LiteralPath $env:APPROVAL_CONSUMER_REPORT -Raw | ConvertFrom-Json
    if (-not $embeddedEvidence.passed -or $embeddedEvidence.checks.Count -lt 10) { throw 'Core-only package consumer did not execute the required checks.' }
}
finally { $env:APPROVAL_CONSUMER_DATABASE = $savedConsumerDatabase; $env:APPROVAL_CONSUMER_REPORT = $savedConsumerReport }

$artifacts = @($tarball) + @($frameworkPackages | ForEach-Object { Join-Path $packages ($_ + '.' + $version + '.nupkg') })
$hashes = @($artifacts | ForEach-Object { [pscustomobject]@{ File = (Split-Path -Leaf $_); Sha256 = (Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant() } })
$sourceHasher = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
Push-Location -LiteralPath $repositoryRoot
try {
    $commit = & git rev-parse 'HEAD'
    $sourceFiles = & git ls-files --cached --others --exclude-standard
    foreach ($file in ($sourceFiles | Sort-Object -Unique)) {
        if (Test-Path -LiteralPath $file -PathType Leaf) {
            $entry = $file.Replace('\', '/') + ':' + (Get-FileHash -LiteralPath $file).Hash + [char]10
            $sourceHasher.AppendData([Text.Encoding]::UTF8.GetBytes($entry))
        }
    }
    $sourceHash = [Convert]::ToHexString($sourceHasher.GetHashAndReset()).ToLowerInvariant()
}
finally { Pop-Location; $sourceHasher.Dispose() }
$contractHashes = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'contracts') -File | ForEach-Object { [pscustomobject]@{ File = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() } })
$manifest = [ordered]@{ Version = $version; Commit = $commit; SourceSha256 = $sourceHash; ConsumerRoot = $consumerRoot; ApiRoot = $apiRoot; ReferenceApiRoot = $referenceRoot; EmbeddedApiRoot = $embeddedRoot; UiRoot = $uiRoot; Packages = $hashes; Contracts = $contractHashes; ChangelogSha256 = (Get-FileHash -LiteralPath (Join-Path $repositoryRoot 'CHANGELOG.md')).Hash.ToLowerInvariant() }
$manifestPath = Join-Path $consumerRoot 'verification.json'
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $repositoryRoot 'output/engineering/package-verification.json')
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $packages 'release-manifest.json')
Write-Host ('Independent package consumers passed: ' + $consumerRoot)
if ($RunBrowser) { Invoke-CheckedCommand 'node' @('scripts/browser-smoke.mjs', '--manifest', $manifestPath) $webRoot }
