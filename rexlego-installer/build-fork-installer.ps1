param(
    [string]$OutputDir = "$PSScriptRoot\fork-dist",
    [string]$UpstreamRepo = "NeverCookFirst/DimensionsRecomp"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step([string]$Text) {
    Write-Host "== $Text"
}

function Get-UpstreamInstallerAsset {
    param([string]$Repo)

    $headers = @{
        "User-Agent" = "DimensionsRecompiled-ForkBuilder"
        "Accept" = "application/vnd.github+json"
    }
    $releases = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/$Repo/releases?per_page=20"
    $release = $releases |
        Where-Object { -not $_.draft -and $_.assets } |
        Sort-Object { [datetime]$_.published_at } -Descending |
        Where-Object { $_.assets.name -contains "DimensionsRecompiled-Setup.exe" } |
        Select-Object -First 1

    if (-not $release) {
        throw "No published upstream release contains DimensionsRecompiled-Setup.exe"
    }

    $asset = $release.assets | Where-Object { $_.name -eq "DimensionsRecompiled-Setup.exe" } | Select-Object -First 1
    if (-not $asset) { throw "Upstream installer asset was not found" }

    Write-Host "   upstream release: $($release.tag_name)"
    Write-Host "   upstream asset:   $($asset.browser_download_url)"
    return $asset
}

function Copy-AppendedPayload {
    param(
        [string]$SourceInstaller,
        [string]$DestinationInstaller
    )

    $magicBytes = [System.Text.Encoding]::ASCII.GetBytes("RXPAYLD1")
    $source = [System.IO.File]::Open($SourceInstaller, 'Open', 'Read', 'Read')
    try {
        if ($source.Length -lt 16) { throw "Upstream installer is too small to contain a payload footer" }

        $source.Position = $source.Length - 16
        $footer = New-Object byte[] 16
        $read = $source.Read($footer, 0, $footer.Length)
        if ($read -ne 16) { throw "Could not read upstream payload footer" }

        for ($i = 0; $i -lt 8; $i++) {
            if ($footer[$i] -ne $magicBytes[$i]) { throw "Upstream installer has no RXPAYLD1 payload footer" }
        }

        $zipSize = [System.BitConverter]::ToInt64($footer, 8)
        if ($zipSize -le 0 -or $zipSize -gt ($source.Length - 16)) {
            throw "Upstream payload length is invalid: $zipSize"
        }
        $payloadOffset = $source.Length - 16 - $zipSize

        # A fast sanity check before copying a large payload.
        $source.Position = $payloadOffset
        $zipHeader = New-Object byte[] 4
        if ($source.Read($zipHeader, 0, 4) -ne 4 -or
            $zipHeader[0] -ne 0x50 -or $zipHeader[1] -ne 0x4B) {
            throw "Upstream payload does not begin with a ZIP signature"
        }

        $source.Position = $payloadOffset
        $destination = [System.IO.File]::Open($DestinationInstaller, 'Append', 'Write', 'Read')
        try {
            $buffer = New-Object byte[] (1MB)
            [long]$remaining = $zipSize
            while ($remaining -gt 0) {
                $want = [int][Math]::Min($buffer.Length, $remaining)
                $n = $source.Read($buffer, 0, $want)
                if ($n -le 0) { throw "Unexpected end of upstream payload" }
                $destination.Write($buffer, 0, $n)
                $remaining -= $n
            }
            $destination.Write($magicBytes, 0, 8)
            $sizeBytes = [System.BitConverter]::GetBytes([long]$zipSize)
            $destination.Write($sizeBytes, 0, 8)
            $destination.Flush()
        }
        finally { $destination.Dispose() }

        Write-Host ("   reused payload: {0:N1} MB" -f ($zipSize / 1MB))
    }
    finally { $source.Dispose() }
}

Write-Step "Publishing fork installer host"
if (Test-Path -LiteralPath $OutputDir) {
    $resolved = (Resolve-Path -LiteralPath $OutputDir).Path
    $expectedRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
    if (-not $resolved.StartsWith($expectedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear output directory outside rexlego-installer: $resolved"
    }
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Force $OutputDir | Out-Null

$publishDir = Join-Path $OutputDir "host"
dotnet publish "$PSScriptRoot\Setup.csproj" -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $publishDir -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Installer host publish failed" }

$host = Join-Path $publishDir "DimensionsRecompiled-Setup.exe"
if (-not (Test-Path -LiteralPath $host)) { throw "Published installer host is missing" }
$final = Join-Path $OutputDir "DimensionsRecompiled-Setup.exe"
Copy-Item -LiteralPath $host -Destination $final -Force

Write-Step "Downloading latest public upstream installer payload"
$asset = Get-UpstreamInstallerAsset -Repo $UpstreamRepo
$upstream = Join-Path $OutputDir "upstream-installer.exe"
Invoke-WebRequest -Headers @{ "User-Agent" = "DimensionsRecompiled-ForkBuilder" } `
    -Uri $asset.browser_download_url -OutFile $upstream
if ((Get-Item -LiteralPath $upstream).Length -lt 1MB) { throw "Upstream installer download is unexpectedly small" }

Write-Step "Attaching redistributable payload to fork installer"
Copy-AppendedPayload -SourceInstaller $upstream -DestinationInstaller $final

# Verify the exact footer we just wrote and print a useful artifact identity.
$verify = [System.IO.File]::OpenRead($final)
try {
    $verify.Position = $verify.Length - 16
    $footer = New-Object byte[] 16
    if ($verify.Read($footer, 0, 16) -ne 16) { throw "Could not verify final payload footer" }
    $magic = [System.Text.Encoding]::ASCII.GetString($footer, 0, 8)
    if ($magic -ne "RXPAYLD1") { throw "Final installer payload verification failed" }
} finally { $verify.Dispose() }

$sha = (Get-FileHash -LiteralPath $final -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Step "Complete"
Write-Host "   installer: $final"
Write-Host ("   size:      {0:N1} MB" -f ((Get-Item -LiteralPath $final).Length / 1MB))
Write-Host "   sha256:    $sha"

# Remove temporary build/download material so only the distributable remains.
Remove-Item -LiteralPath $publishDir -Recurse -Force
Remove-Item -LiteralPath $upstream -Force
