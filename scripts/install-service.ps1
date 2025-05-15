# Script to install the UsbDlpAgent as a Windows Service
# IMPORTANT: Run this script as Administrator

param (
    [string]$ServiceName = "UsbDlpAgentService",
    [string]$DisplayName = "USB DLP Agent Service",
    [string]$Description = "Monitors USB drive activity and file operations for DLP purposes.",
    # Assuming the executable is in a 'publish' subfolder relative to this script
    # Adjust if your build output structure is different.
    [string]$ServiceExePath = "$PSScriptRoot\..\artifacts\publish\UsbDlpAgent\UsbDlpAgent.exe"
)

Write-Host "Attempting to install service: $DisplayName"

# Check if service already exists
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    Write-Warning "Service '$ServiceName' already exists."
    # Optional: Offer to uninstall and reinstall or just exit
    # Read-Host "Press Enter to continue or Ctrl+C to abort."
    exit 1
}

# Resolve the full path to the executable
$FullServiceExePath = Resolve-Path -Path $ServiceExePath -ErrorAction Stop
if (-not (Test-Path $FullServiceExePath)) {
    Write-Error "Service executable not found at: $FullServiceExePath"
    exit 1
}

Write-Host "Service Executable: $FullServiceExePath"

try {
    New-Service -Name $ServiceName `
                -BinaryPathName $FullServiceExePath `
                -DisplayName $DisplayName `
                -Description $Description `
                -StartupType Automatic `
                -ErrorAction Stop

    Write-Host "Service '$DisplayName' installed successfully."
    Write-Host "You can start it with: Start-Service -Name $ServiceName"
    Write-Host "Or set it to start automatically on boot and then start:"
    Write-Host "Set-Service -Name $ServiceName -StartupType Automatic"
    Write-Host "Start-Service -Name $ServiceName"

}
catch {
    Write-Error "Failed to install service: $($_.Exception.Message)"
    exit 1
}

# Optional: Configure service recovery options
# sc.exe failure $ServiceName reset= 60 actions= restart/5000/restart/5000/restart/5000
# Write-Host "Configured service recovery options."