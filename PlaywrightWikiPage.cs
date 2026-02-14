# Upgrade2026_GUI_Smart.ps1
# Smart version checker - installs ONLY if newer version is available
# Igor, February 2026

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Application]::EnableVisualStyles()

# ================= Settings =================
$ToolsDir       = "C:\Tools\Fuzzers"
$EchidnaExe     = Join-Path $ToolsDir "echidna.exe"
$GoBin          = "$env:USERPROFILE\go\bin"
$UseGoBinForMedusa = $true

if (-not (Test-Path $ToolsDir)) {
    New-Item -ItemType Directory -Path $ToolsDir | Out-Null
}

# ================= Version Comparison =================
function Compare-Versions {
    param(
        [string]$Current,
        [string]$Latest
    )
    
    if ($Current -eq "not installed" -or $Current -eq "error") { return $true }
    if ($Latest -eq "error" -or $Latest -eq "not found") { return $false }
    
    # Clean versions
    $currentClean = $Current -replace '^v|\.0$', ''
    $latestClean = $Latest -replace '^v|\.0$', ''
    
    # Direct comparison for simple cases
    if ($currentClean -eq $latestClean) { return $false }
    
    # Parse versions
    try {
        $currParts = ($currentClean -split '\.' | ForEach-Object { [int]$_ })
        $latestParts = ($latestClean -split '\.' | ForEach-Object { [int]$_ })
        
        for ($i = 0; $i -lt [Math]::Max($currParts.Count, $latestParts.Count); $i++) {
            $curr = if ($i -lt $currParts.Count) { $currParts[$i] } else { 0 }
            $latest = if ($i -lt $latestParts.Count) { $latestParts[$i] } else { 0 }
            
            if ($latest -gt $curr) { return $true }
            if ($curr -gt $latest) { return $false }
        }
    } catch {
        return $false
    }
    
    return $false
}

# ================= Logging =================
$LogFile = Join-Path $env:TEMP "Upgrade2026_Smart.log"
function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "$timestamp [$Level] $Message" | Out-File -FilePath $LogFile -Append -Encoding UTF8
}

# ================= Slither =================
function Get-LocalSlitherVersion {
    try {
        $output = & slither --version 2>$null
        if ($output) {
            $ver = $output -replace '.*(\d+\.\d+\.\d+).*', '$1'
            return $ver.Trim()
        }
    } catch {}
    return "not installed"
}

function Get-LatestSlitherVersion {
    try {
        $response = Invoke-RestMethod -Uri "https://pypi.org/pypi/slither-analyzer/json" -ErrorAction Stop
        return $response.info.version
    } catch {
        Write-Log "Error getting Slither version from PyPI: $_" "ERROR"
        return "error"
    }
}

# ================= Echidna =================
function Get-LocalEchidnaVersion {
    $exe = if (Test-Path $EchidnaExe) { $EchidnaExe } else { "echidna" }
    try {
        $output = & $exe --version 2>$null
        if ($output) {
            $ver = $output -replace '.*Echidna\s+([^\s\(]+).*', '$1'
            return $ver.Trim()
        }
    } catch {}
    return "not installed"
}

function Get-LatestEchidnaVersion {
    try {
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/crytic/echidna/releases/latest" -Headers @{ "User-Agent" = "PowerShell" } -ErrorAction Stop
        return $release.tag_name -replace '^v', ''
    } catch {
        Write-Log "Error getting Echidna version from GitHub: $_" "ERROR"
        return "error"
    }
}

# ================= Medusa =================
function Get-LocalMedusaVersion {
    $exe = if ($UseGoBinForMedusa) { Join-Path $GoBin "medusa.exe" } else { "medusa" }
    try {
        $output = & $exe --version 2>$null
        $ver = $output -replace '.*version\s+(v?[^\s]+).*', '$1'
        return $ver.Trim()
    } catch {}
    return "not installed"
}

function Get-LatestMedusaVersion {
    try {
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/crytic/medusa/releases/latest" -Headers @{ "User-Agent" = "PowerShell" } -ErrorAction Stop
        return $release.tag_name -replace '^v', ''
    } catch {
        Write-Log "Error getting Medusa version from GitHub: $_" "ERROR"
        return "error"
    }
}

# ================= Halmos =================
function Get-LocalHalmosVersion {
    try {
        $output = & halmos --version 2>$null
        if ($output) {
            $ver = $output -replace '.*(\d+\.\d+\.\d+).*', '$1'
            if (-not $ver) { $ver = $output.Trim() }
            return $ver.Trim()
        }
    } catch {}
    return "not installed"
}

function Get-LatestHalmosVersion {
    try {
        $response = Invoke-RestMethod -Uri "https://pypi.org/pypi/halmos/json" -ErrorAction Stop
        return $response.info.version
    } catch {
        Write-Log "Error getting Halmos version from PyPI: $_" "ERROR"
        return "error"
    }
}

# ================= Update Functions =================

function Update-SlitherTool {
    param([bool]$Force = $false)
    
    $current = Get-LocalSlitherVersion
    $latest = Get-LatestSlitherVersion
    
    if (!$Force -and -not (Compare-Versions $current $latest)) {
        Write-Log "Slither: $current is already latest" "INFO"
        return $false
    }
    
    Write-Log "Updating Slither from $current to $latest" "INFO"
    python -m pip install --upgrade slither-analyzer 2>&1 | ForEach-Object { Write-Log $_ }
    Write-Log "Slither update completed" "INFO"
    return $true
}

function Update-EchidnaTool {
    param([bool]$Force = $false)
    
    $current = Get-LocalEchidnaVersion
    $latest = Get-LatestEchidnaVersion
    
    if (!$Force -and -not (Compare-Versions $current $latest)) {
        Write-Log "Echidna: $current is already latest" "INFO"
        return $false
    }
    
    Write-Log "Updating Echidna from $current to $latest" "INFO"
    
    if ($latest -eq "error") { return $false }
    
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/crytic/echidna/releases/latest" -Headers @{ "User-Agent" = "PowerShell" }
    $asset = $release.assets | Where-Object { $_.name -match "win64\.zip|windows.*\.zip" } | Select-Object -First 1
    
    if (-not $asset) {
        Write-Log "Windows build not found for Echidna" "ERROR"
        return $false
    }
    
    $url = $asset.browser_download_url
    $zipPath = Join-Path $env:TEMP "echidna-latest.zip"
    
    Write-Log "Downloading Echidna from $url" "INFO"
    Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing
    
    Write-Log "Extracting to $ToolsDir" "INFO"
    Expand-Archive -Path $zipPath -DestinationPath $ToolsDir -Force
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    
    Write-Log "Echidna update completed" "INFO"
    return $true
}

function Update-MedusaTool {
    param([bool]$Force = $false)
    
    $current = Get-LocalMedusaVersion
    $latest = Get-LatestMedusaVersion
    
    if (!$Force -and -not (Compare-Versions $current $latest)) {
        Write-Log "Medusa: $current is already latest" "INFO"
        return $false
    }
    
    Write-Log "Updating Medusa from $current to $latest" "INFO"
    go install github.com/crytic/medusa@latest 2>&1 | ForEach-Object { Write-Log $_ }
    Write-Log "Medusa update completed" "INFO"
    return $true
}

function Update-HalmosTool {
    param([bool]$Force = $false)
    
    $current = Get-LocalHalmosVersion
    $latest = Get-LatestHalmosVersion
    
    if (!$Force -and -not (Compare-Versions $current $latest)) {
        Write-Log "Halmos: $current is already latest" "INFO"
        return $false
    }
    
    Write-Log "Updating Halmos from $current to $latest" "INFO"
    
    if (Get-Command uv -ErrorAction SilentlyContinue) {
        uv tool upgrade halmos 2>&1 | ForEach-Object { Write-Log $_ }
    } else {
        python -m pip install --upgrade halmos 2>&1 | ForEach-Object { Write-Log $_ }
    }
    
    $currentAfter = Get-LocalHalmosVersion
    if ($currentAfter -eq "not installed") {
        Write-Log "Halmos not found after update - installing" "WARN"
        if (Get-Command uv -ErrorAction SilentlyContinue) {
            uv tool install halmos 2>&1 | ForEach-Object { Write-Log $_ }
        } else {
            python -m pip install halmos 2>&1 | ForEach-Object { Write-Log $_ }
        }
    }
    
    Write-Log "Halmos update completed" "INFO"
    return $true
}

# ================= XAML Interface =================

$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Upgrade Tools 2026 - Smart Version Checker" 
        Height="750" Width="1000"
        Background="#F5F5F5"
        WindowStartupLocation="CenterScreen"
        ResizeMode="CanResize"
        Foreground="#333333">
    <Grid>
        <StackPanel Margin="20">
            <TextBlock Text="Upgrade Tools 2026 - Smart Edition" 
                       FontSize="28" 
                       FontWeight="Bold" 
                       Foreground="#2C3E50"
                       Margin="0,0,0,10"/>
            
            <TextBlock Text="Automatic version comparison - updates ONLY if newer version available" 
                       FontSize="12" 
                       Foreground="#7F8C8D"
                       Margin="0,0,0,20"/>
            
            <TextBlock x:Name="DateTimeBlock" 
                       FontSize="11" 
                       Foreground="#95A5A6"
                       Margin="0,0,0,15"/>
            
            <Separator Margin="0,0,0,15" Background="#BDC3C7"/>
            
            <DataGrid x:Name="ToolsGrid" 
                      Height="320" 
                      Margin="0,0,0,15"
                      AutoGenerateColumns="False"
                      IsReadOnly="False"
                      CanUserAddRows="False"
                      HeadersVisibility="All">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="Tool" Binding="{Binding ToolName}" Width="100"/>
                    <DataGridTextColumn Header="Current" Binding="{Binding CurrentVersion}" Width="120"/>
                    <DataGridTextColumn Header="Latest" Binding="{Binding LatestVersion}" Width="120"/>
                    <DataGridTextColumn Header="Update Available" Binding="{Binding UpdateAvailable}" Width="130"/>
                    <DataGridTextColumn Header="Status" Binding="{Binding Status}" Width="150"/>
                    <DataGridCheckBoxColumn Header="Update?" Binding="{Binding ToUpdate}" Width="70"/>
                </DataGrid.Columns>
            </DataGrid>
            
            <TextBlock Text="Operation Log:" FontSize="12" FontWeight="Bold" Margin="0,0,0,5"/>
            <TextBox x:Name="LogBox" 
                     Height="140" 
                     Margin="0,0,0,15"
                     IsReadOnly="True"
                     VerticalScrollBarVisibility="Auto"
                     Background="#FFFFFF"
                     Foreground="#2C3E50"
                     FontFamily="Courier New"
                     FontSize="9"
                     Padding="10"/>
            
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,0,0,15">
                <Button x:Name="RefreshButton" 
                        Content="Check Versions" 
                        Padding="15,10" 
                        Margin="5"
                        Background="#3498DB"
                        Foreground="White"
                        FontWeight="Bold"
                        FontSize="12"/>
                
                <Button x:Name="UpdateButton" 
                        Content="Update Selected" 
                        Padding="15,10" 
                        Margin="5"
                        Background="#27AE60"
                        Foreground="White"
                        FontWeight="Bold"
                        FontSize="12"/>
                
                <Button x:Name="UpdateAllButton" 
                        Content="Update All" 
                        Padding="15,10" 
                        Margin="5"
                        Background="#E74C3C"
                        Foreground="White"
                        FontWeight="Bold"
                        FontSize="12"/>
                
                <Button x:Name="ClearLogButton" 
                        Content="Clear Log" 
                        Padding="15,10" 
                        Margin="5"
                        Background="#95A5A6"
                        Foreground="White"
                        FontWeight="Bold"
                        FontSize="12"/>
            </StackPanel>
            
            <TextBlock x:Name="StatusBlock" 
                       Text="Ready" 
                       Foreground="#27AE60"
                       FontSize="12"
                       Margin="0,5,0,0"/>
        </StackPanel>
    </Grid>
</Window>
"@

# ================= GUI Initialization =================

$reader = [System.Xml.XmlNodeReader]::new([xml]$xaml)
$window = [System.Windows.Markup.XamlReader]::Load($reader)

$toolsGrid = $window.FindName("ToolsGrid")
$logBox = $window.FindName("LogBox")
$dateTimeBlock = $window.FindName("DateTimeBlock")
$statusBlock = $window.FindName("StatusBlock")
$refreshButton = $window.FindName("RefreshButton")
$updateButton = $window.FindName("UpdateButton")
$updateAllButton = $window.FindName("UpdateAllButton")
$clearLogButton = $window.FindName("ClearLogButton")

$dateTimeBlock.Text = "Date: $(Get-Date -Format 'dddd, dd MMMM yyyy - HH:mm:ss')"

function Update-Log {
    param([string]$Message)
    $timestamp = Get-Date -Format "HH:mm:ss"
    $logBox.AppendText("[$timestamp] $Message`r`n")
    $logBox.ScrollToEnd()
}

function Refresh-ToolsStatus {
    Update-Log "Checking versions for all tools..."
    
    $slitherCurr = Get-LocalSlitherVersion
    $slitherLat = Get-LatestSlitherVersion
    $slitherNeedUpdate = Compare-Versions $slitherCurr $slitherLat
    
    $echidnaCurr = Get-LocalEchidnaVersion
    $echidnaLat = Get-LatestEchidnaVersion
    $echidnaNeedUpdate = Compare-Versions $echidnaCurr $echidnaLat
    
    $medusaCurr = Get-LocalMedusaVersion
    $medusaLat = Get-LatestMedusaVersion
    $medusaNeedUpdate = Compare-Versions $medusaCurr $medusaLat
    
    $halmosCurr = Get-LocalHalmosVersion
    $halmosLat = Get-LatestHalmosVersion
    $halmosNeedUpdate = Compare-Versions $halmosCurr $halmosLat
    
    $tools = @(
        [PSCustomObject]@{
            ToolName = "Slither"
            CurrentVersion = $slitherCurr
            LatestVersion = $slitherLat
            UpdateAvailable = if ($slitherNeedUpdate) { "YES" } else { "No" }
            Status = if ($slitherNeedUpdate) { "Update available" } else { "Up to date" }
            ToUpdate = $slitherNeedUpdate
        },
        [PSCustomObject]@{
            ToolName = "Echidna"
            CurrentVersion = $echidnaCurr
            LatestVersion = $echidnaLat
            UpdateAvailable = if ($echidnaNeedUpdate) { "YES" } else { "No" }
            Status = if ($echidnaNeedUpdate) { "Update available" } else { "Up to date" }
            ToUpdate = $echidnaNeedUpdate
        },
        [PSCustomObject]@{
            ToolName = "Medusa"
            CurrentVersion = $medusaCurr
            LatestVersion = $medusaLat
            UpdateAvailable = if ($medusaNeedUpdate) { "YES" } else { "No" }
            Status = if ($medusaNeedUpdate) { "Update available" } else { "Up to date" }
            ToUpdate = $medusaNeedUpdate
        },
        [PSCustomObject]@{
            ToolName = "Halmos"
            CurrentVersion = $halmosCurr
            LatestVersion = $halmosLat
            UpdateAvailable = if ($halmosNeedUpdate) { "YES" } else { "No" }
            Status = if ($halmosNeedUpdate) { "Update available" } else { "Up to date" }
            ToUpdate = $halmosNeedUpdate
        }
    )
    
    $toolsGrid.ItemsSource = $tools
    Update-Log "Version check completed"
    Update-Status "Ready"
}

$refreshButton.Add_Click({
    Refresh-ToolsStatus
})

$updateAllButton.Add_Click({
    $statusBlock.Foreground = "#E74C3C"
    $statusBlock.Text = "Updating in progress..."
    Update-Log "Starting update of all tools..."
    
    $slither = Update-SlitherTool
    $echidna = Update-EchidnaTool
    $medusa = Update-MedusaTool
    $halmos = Update-HalmosTool
    
    $updatedCount = @($slither, $echidna, $medusa, $halmos) | Where-Object { $_ } | Measure-Object | Select-Object -ExpandProperty Count
    
    Refresh-ToolsStatus
    Update-Log "Update completed. Tools updated: $updatedCount"
    $statusBlock.Foreground = "#27AE60"
    Update-Status "Update completed"
})

$updateButton.Add_Click({
    if ($toolsGrid.SelectedItems.Count -eq 0) {
        Update-Log "No tools selected"
        return
    }
    
    $statusBlock.Foreground = "#E74C3C"
    $statusBlock.Text = "Updating in progress..."
    Update-Log "Starting update of selected tools..."
    
    $updatedCount = 0
    foreach ($item in $toolsGrid.SelectedItems) {
        $result = $false
        switch ($item.ToolName) {
            "Slither" { $result = Update-SlitherTool }
            "Echidna" { $result = Update-EchidnaTool }
            "Medusa" { $result = Update-MedusaTool }
            "Halmos" { $result = Update-HalmosTool }
        }
        if ($result) { $updatedCount++ }
    }
    
    Refresh-ToolsStatus
    Update-Log "Update completed. Tools updated: $updatedCount"
    $statusBlock.Foreground = "#27AE60"
    Update-Status "Update completed"
})

$clearLogButton.Add_Click({
    $logBox.Clear()
    Update-Log "Log cleared"
})

function Update-Status {
    param([string]$Status)
    $statusBlock.Text = $Status
}

Refresh-ToolsStatus
Update-Log "GUI loaded and ready"

$window.ShowDialog() | Out-Null
