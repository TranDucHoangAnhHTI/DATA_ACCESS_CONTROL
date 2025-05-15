# Script to uninstall the UsbDlpAgent Windows Service
# IMPORTANT: Run this script as Administrator

param (
    [string]$ServiceName = "UsbDlpAgentService"
)

Write-Host "Attempting to uninstall service: $ServiceName"

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if (-not $service) {
    Write-Warning "Service '$ServiceName' not found. Nothing to uninstall."
    exit 0
}

try {
    # Stop the service if it's running
    if ($service.Status -eq "Running") {
        Write-Host "Stopping service '$ServiceName'..."
        Stop-Service -Name $ServiceName -Force -ErrorAction Stop
        Write-Host "Service '$ServiceName' stopped."
    }

    # Remove the service
    Remove-Service -Name $ServiceName -ErrorAction Stop
    Write-Host "Service '$ServiceName' uninstalled successfully."
}
catch {
    Write-Error "Failed to uninstall service: $($_.Exception.Message)"
    Write-Warning "You might need to manually remove it using 'sc.exe delete $ServiceName' from an Administrator command prompt."
    exit 1
}