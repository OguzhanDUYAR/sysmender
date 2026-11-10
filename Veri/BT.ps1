function Ui([string]$tr,[string]$en) { if ($English) { $en } else { $tr } }
function Need([string]$name) {
    if (!(Get-Command $name -ErrorAction SilentlyContinue)) {
        Ui "Bu Windows kurulumunda kullanılamıyor: $name" "Unavailable on this Windows installation: $name"
        exit 3
    }
}
function Show($rows) {
    $items = @($rows | Where-Object { $null -ne $_ })
    if (!$items.Count) { Ui 'Eşleşen kayıt bulunmadı.' 'No matching records found.'; return }
    $items | Format-List | Out-String -Width 240
}
function ReadEvents([string]$name) {
    try {
        $items = @(Get-WinEvent -FilterHashtable @{LogName=$name;Level=1,2;StartTime=(Get-Date).AddDays(-7)} -MaxEvents 100 -ErrorAction Stop)
    } catch {
        if ($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*') { $items=@() } else { throw }
    }
    Show ($items | Select-Object TimeCreated,ProviderName,Id,LevelDisplayName,Message)
    if ($items.Count) { $script:review=$true }
}
$script:review=$false

# TOOL hardware
try {
    Show (Get-CimInstance Win32_ComputerSystem -OperationTimeoutSec 30 | Select-Object Manufacturer,Model,TotalPhysicalMemory,NumberOfLogicalProcessors,Domain,PartOfDomain)
    Show (Get-CimInstance Win32_Processor -OperationTimeoutSec 30 | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed)
    Show (Get-CimInstance Win32_BIOS -OperationTimeoutSec 30 | Select-Object Manufacturer,SMBIOSBIOSVersion,ReleaseDate,SerialNumber)
    Show (Get-CimInstance Win32_PhysicalMemory -OperationTimeoutSec 30 | Select-Object DeviceLocator,Manufacturer,PartNumber,Capacity,Speed,ConfiguredClockSpeed)
} catch {
    (Ui 'CIM envanteri eksik: ' 'CIM inventory incomplete: ') + $_.Exception.Message
    Show (Get-ItemProperty -LiteralPath 'HKLM:\HARDWARE\DESCRIPTION\System\BIOS' | Select-Object SystemManufacturer,SystemProductName,BIOSVendor,BIOSVersion,BIOSReleaseDate)
    Show (Get-ItemProperty -LiteralPath 'HKLM:\HARDWARE\DESCRIPTION\System\CentralProcessor\0' | Select-Object ProcessorNameString,Identifier)
    $script:review=$true
}
# END TOOL

# TOOL gpu
Show (Get-CimInstance Win32_VideoController -OperationTimeoutSec 30 | Select-Object Name,DriverVersion,DriverDate,CurrentHorizontalResolution,CurrentVerticalResolution,Status)
# END TOOL

# TOOL battery
$items=@(Get-CimInstance Win32_Battery -OperationTimeoutSec 30)
if (!$items.Count) { Ui 'Batarya algılanmadı.' 'No battery detected.'; exit 3 }
Show ($items | Select-Object Name,Status,BatteryStatus,EstimatedChargeRemaining,EstimatedRunTime)
# END TOOL

# TOOL drivers
Show (Get-CimInstance Win32_PnPSignedDriver -OperationTimeoutSec 30 | Sort-Object DeviceName | Select-Object -First 600 DeviceName,DeviceID,DriverProviderName,DriverVersion,DriverDate,IsSigned,InfName)
# END TOOL

# TOOL device-errors
$items=@(Get-CimInstance Win32_PnPEntity -OperationTimeoutSec 30 | Where-Object { $_.Present -eq $true -and $_.ConfigManagerErrorCode -ne 0 })
Show ($items | Select-Object Name,PNPDeviceID,ConfigManagerErrorCode,Status)
if ($items.Count) { $script:review=$true }
# END TOOL

# TOOL processes
Show (Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 50 Id,ProcessName,WorkingSet64,PrivateMemorySize64,CPU,Handles)
# END TOOL

# TOOL services-all
Show (Get-CimInstance Win32_Service -OperationTimeoutSec 30 | Sort-Object Name | Select-Object -First 500 Name,DisplayName,State,StartMode,StartName)
# END TOOL

# TOOL task-errors
Need 'Get-ScheduledTask'
$items=@(Get-ScheduledTask | Get-ScheduledTaskInfo | Where-Object { $_.LastRunTime.Year -gt 2000 -and $_.LastTaskResult -ne 0 } | Select-Object -First 100 TaskName,TaskPath,LastRunTime,LastTaskResult,NextRunTime)
Show $items
if ($items.Count) { $script:review=$true }
# END TOOL

# TOOL events-system
ReadEvents 'System'
# END TOOL

# TOOL events-app
ReadEvents 'Application'
# END TOOL

# TOOL reliability
$since=(Get-Date).AddDays(-14).ToUniversalTime().ToString('yyyyMMddHHmmss.000000+000')
Show (Get-CimInstance Win32_ReliabilityRecords -Filter "TimeGenerated >= '$since'" -OperationTimeoutSec 30 | Sort-Object TimeGenerated -Descending | Select-Object -First 100 TimeGenerated,SourceName,EventIdentifier,ProductName,Message)
# END TOOL

# TOOL update-history
$session=New-Object -ComObject Microsoft.Update.Session
try {
    $searcher=$session.CreateUpdateSearcher()
    try {
        $count=[Math]::Min(100,$searcher.GetTotalHistoryCount())
        if ($count -eq 0) { Ui 'Güncelleme geçmişi yok.' 'No update history.' }
        else {
            $history=@($searcher.QueryHistory(0,$count))
            Show ($history | Select-Object Date,Title,Operation,ResultCode,HResult)
            Ui 'ResultCode: 2=başarılı, 3=hatalarla tamamlandı, 4=başarısız, 5=iptal.' 'ResultCode: 2=succeeded, 3=succeeded with errors, 4=failed, 5=aborted.'
            if (@($history | Where-Object { [int]$_.ResultCode -in 3,4,5 }).Count) { $script:review=$true }
        }
    } finally { if ($searcher) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($searcher) } }
} finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($session) }
# END TOOL

# TOOL hotfixes
Show (Get-CimInstance Win32_QuickFixEngineering -OperationTimeoutSec 30 | Sort-Object InstalledOn -Descending | Select-Object -First 150 HotFixID,Description,InstalledOn,InstalledBy)
# END TOOL

# TOOL features
Need 'Get-WindowsOptionalFeature'
Show (Get-WindowsOptionalFeature -Online | Sort-Object FeatureName | Select-Object FeatureName,State)
# END TOOL

# TOOL volumes
try {
    Show (Get-Disk | Select-Object Number,FriendlyName,BusType,PartitionStyle,Size,HealthStatus,OperationalStatus,IsOffline,IsReadOnly)
    Show (Get-Partition | Select-Object DiskNumber,PartitionNumber,DriveLetter,Type,Size)
    Show (Get-Volume | Select-Object DriveLetter,FileSystemLabel,FileSystem,DriveType,HealthStatus,OperationalStatus,SizeRemaining,Size)
} catch {
    (Ui 'Disk veya bölüm ayrıntıları eksik: ' 'Disk or partition details incomplete: ') + $_.Exception.Message
    Show ([IO.DriveInfo]::GetDrives() | Where-Object { $_.DriveType -eq 'Fixed' -and $_.IsReady } |
        Select-Object Name,DriveFormat,AvailableFreeSpace,TotalSize)
    $script:review=$true
}
# END TOOL

# TOOL volume-analysis
Need 'Optimize-Volume'
$letter=[IO.Path]::GetPathRoot($env:SystemRoot).Substring(0,1)
Optimize-Volume -DriveLetter $letter -Analyze -Verbose 4>&1 | Out-String -Width 240
# END TOOL

# TOOL bitlocker
Need 'Get-BitLockerVolume'
Show (Get-BitLockerVolume | Select-Object MountPoint,VolumeType,VolumeStatus,ProtectionStatus,LockStatus,EncryptionMethod,EncryptionPercentage)
# END TOOL

# TOOL defender
Need 'Get-MpComputerStatus'
$status=Get-MpComputerStatus
Show ($status | Select-Object AMRunningMode,AMServiceEnabled,AntivirusEnabled,RealTimeProtectionEnabled,NISEnabled,AntivirusSignatureVersion,AntivirusSignatureLastUpdated,AntivirusSignatureAge,QuickScanStartTime,QuickScanEndTime,RebootRequired)
if (!$status.AMServiceEnabled -or !$status.RealTimeProtectionEnabled -or $status.AntivirusSignatureAge -gt 7) { $script:review=$true }
Ui 'Üçüncü taraf antivirüs veya kurum ilkesi Defender durumunu etkileyebilir.' 'Third-party antivirus or organizational policy may affect Defender status.'
# END TOOL

# TOOL firewall
Need 'Get-NetFirewallProfile'
$items=@(Get-NetFirewallProfile -PolicyStore ActiveStore)
Show ($items | Select-Object Name,Enabled,DefaultInboundAction,DefaultOutboundAction,LogBlocked,LogAllowed)
if (@($items | Where-Object { !$_.Enabled }).Count) { $script:review=$true }
# END TOOL

# TOOL local-users
Show (Get-CimInstance Win32_UserAccount -Filter 'LocalAccount=True' -OperationTimeoutSec 30 | Select-Object Name,Disabled,Lockout,PasswordRequired,PasswordExpires,Status)
# END TOOL

# TOOL connections
Need 'Get-NetTCPConnection'
Show (Get-NetTCPConnection | Sort-Object State,LocalPort | Select-Object -First 300 LocalAddress,LocalPort,RemoteAddress,RemotePort,State,OwningProcess)
# END TOOL

# TOOL proxy
& ([IO.Path]::Combine([Environment]::SystemDirectory,'netsh.exe')) winhttp show proxy
if ($LASTEXITCODE -ne 0) { $script:review=$true }
$proxyKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
if (Test-Path -LiteralPath $proxyKey) {
    Show (Get-ItemProperty -LiteralPath $proxyKey | Select-Object ProxyEnable,ProxyServer,ProxyOverride,AutoConfigURL)
} else { Ui 'Kullanıcı proxy kayıt anahtarı bulunamadı.' 'User proxy registry key was not found.' }
# END TOOL

# TOOL printers
Show (Get-CimInstance Win32_Printer -OperationTimeoutSec 30 | Select-Object Name,Default,Local,Network,DriverName,PortName,PrinterStatus,WorkOffline,DetectedErrorState)
Show (Get-CimInstance Win32_PrintJob -OperationTimeoutSec 30 | Select-Object -First 100 Name,JobId,JobStatus,Status,TotalPages,PagesPrinted)
# END TOOL

# TOOL shares
Show (Get-CimInstance Win32_Share -OperationTimeoutSec 30 | Select-Object Name,Path,Type,Description)
# END TOOL

# TOOL power
& "$env:SystemRoot\System32\powercfg.exe" /getactivescheme
if ($LASTEXITCODE -ne 0) { throw "powercfg exit $LASTEXITCODE" }
& "$env:SystemRoot\System32\powercfg.exe" /a
if ($LASTEXITCODE -ne 0) { throw "powercfg exit $LASTEXITCODE" }
# END TOOL

# TOOL defender-update
Need 'Update-MpSignature'
Update-MpSignature
Show (Get-MpComputerStatus | Select-Object AntivirusSignatureVersion,AntivirusSignatureLastUpdated,AntivirusSignatureAge)
# END TOOL

# TOOL defender-quick
Need 'Start-MpScan'
$before=Get-MpComputerStatus
if (!$before.AMServiceEnabled -or !$before.AntivirusEnabled) { Ui 'Defender etkin değil.' 'Defender is not active.'; exit 3 }
Start-MpScan -ScanType QuickScan
$after=Get-MpComputerStatus
Show ($after | Select-Object QuickScanStartTime,QuickScanEndTime,QuickScanAge)
if ($after.QuickScanEndTime -le $before.QuickScanEndTime) { Ui 'Yeni tamamlanan tarama doğrulanamadı. Windows Güvenliği ekranını inceleyin.' 'A newly completed scan could not be verified. Check Windows Security.'; $script:review=$true }
# END TOOL

# TOOL spooler-restart
$service=Get-Service -Name Spooler
if ($service.StartType -eq 'Disabled') { Ui 'Spooler devre dışı; başlangıç ayarı değiştirilmedi.' 'Spooler is disabled; its startup setting was not changed.'; exit 3 }
if (@($service.DependentServices | Where-Object { $_.Status -ne 'Stopped' }).Count) { Ui 'Etkin bağımlı hizmet var; işlem atlandı.' 'An active dependent service exists; skipped.'; exit 3 }
try {
    if ($service.Status -ne 'Stopped') { Stop-Service -Name Spooler -ErrorAction Stop; $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30)) }
} finally {
    $service.Refresh()
    if ($service.Status -eq 'Stopped') { Start-Service -Name Spooler -ErrorAction Stop }
}
$service.WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
$service.Refresh()
Show ($service | Select-Object Name,Status,StartType)
if ($service.Status -ne 'Running') { throw 'Spooler did not reach Running state.' }
# END TOOL

# TOOL volume-optimize
Need 'Optimize-Volume'
$letter=[IO.Path]::GetPathRoot($env:SystemRoot).Substring(0,1)
Optimize-Volume -DriveLetter $letter -Verbose 4>&1 | Out-String -Width 240
Show (Get-Volume -DriveLetter $letter | Select-Object DriveLetter,FileSystem,HealthStatus,OperationalStatus,SizeRemaining)
# END TOOL

# TOOL readiness
# Registry check adapted from WinUtil Get-WinUtilEnvironmentReport.ps1 (MIT).
# Upstream 2ef5e5dc7852634bc40ad789a3d1115ee3913c41, Copyright (c) 2022 CT Tech Group LLC.
try {
    Show (Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 30 | Select-Object Caption,Version,BuildNumber,OSArchitecture,LastBootUpTime)
} catch {
    (Ui 'CIM sistem bilgisi eksik: ' 'CIM system information incomplete: ') + $_.Exception.Message
    Show (Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' |
        Select-Object EditionID,DisplayVersion,CurrentBuild,UBR)
    $script:review=$true
}
$rebootPaths=[ordered]@{
    CBS='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending'
    WindowsUpdate='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired'
}
$signals=@()
$unknown=$false
foreach ($entry in $rebootPaths.GetEnumerator()) {
    try { if (Test-Path -LiteralPath $entry.Value -ErrorAction Stop) { $signals += $entry.Key } }
    catch { $unknown=$true; "$($entry.Key): $($_.Exception.Message)" }
}
try {
    $sessionManager=Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -ErrorAction Stop
    $pending=@($sessionManager.PendingFileRenameOperations | Where-Object { ![string]::IsNullOrWhiteSpace([string]$_) })
    if ($pending.Count -gt 0) { $signals += 'PendingFileRenameOperations' }
} catch { $unknown=$true; $_.Exception.Message }
if ($signals.Count) { (Ui 'Yeniden başlatma işaretleri: ' 'Pending restart indicators: ') + ($signals -join ', '); $script:review=$true }
if ($unknown) { Ui 'Bazı işaretler doğrulanamadı.' 'Some indicators could not be verified.'; $script:review=$true }
if (!$signals.Count -and !$unknown) { Ui 'Denetlenen işaretler bulunmadı.' 'No checked indicators were found.' }
# END TOOL

# TOOL crashes
$query="*[System[TimeCreated[timediff(@SystemTime) <= 1209600000] and ((EventID=41 and Provider[@Name='Microsoft-Windows-Kernel-Power']) or (EventID=6008 and Provider[@Name='EventLog']) or (EventID=1001 and (Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] or Provider[@Name='BugCheck'])))]]"
try {
    $items=@(Get-WinEvent -LogName System -FilterXPath $query -MaxEvents 100 -ErrorAction Stop)
} catch {
    if ($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*') { $items=@() } else { throw }
}
Show ($items | Select-Object TimeCreated,ProviderName,Id,Message)
if ($items.Count) { $script:review=$true }
# END TOOL

# TOOL tpm-boot
try {
    Show (Get-Tpm -ErrorAction Stop | Select-Object TpmPresent,TpmReady,TpmEnabled,TpmActivated,ManufacturerIdTxt,ManufacturerVersion)
} catch { (Ui 'TPM durumu okunamadı: ' 'TPM state unavailable: ') + $_.Exception.Message; $script:review=$true }
try {
    $boot=Confirm-SecureBootUEFI -ErrorAction Stop
    "SecureBoot=$boot"
    if (!$boot) { $script:review=$true }
} catch { (Ui 'Secure Boot durumu okunamadı: ' 'Secure Boot state unavailable: ') + $_.Exception.Message; $script:review=$true }
# END TOOL

# TOOL profiles
Show (Get-CimInstance Win32_UserProfile -OperationTimeoutSec 30 | Select-Object -First 200 LocalPath,Loaded,Special,Status,LastUseTime)
# END TOOL

# TOOL smb
Need 'Get-SmbConnection'
Show (Get-SmbConnection | Select-Object -First 200 ServerName,ShareName,Dialect,NumOpens,Encrypted,Signed)
# END TOOL

# TOOL vpn
Need 'Get-VpnConnection'
Show (Get-VpnConnection | Select-Object Name,ServerAddress,TunnelType,ConnectionStatus,SplitTunneling)
# END TOOL

# TOOL certificates
$now=Get-Date
$limit=$now.AddDays(30)
foreach ($store in 'Cert:\CurrentUser\My','Cert:\LocalMachine\My') {
    $store
    $items=@(Get-ChildItem -LiteralPath $store | Where-Object NotAfter -le $limit | Sort-Object NotAfter | Select-Object -First 200 Subject,Issuer,NotBefore,NotAfter,Thumbprint)
    Show $items
    if ($items.Count) { $script:review=$true }
}
# END TOOL

# TOOL rdp
Show (Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server' | Select-Object fDenyTSConnections)
$listener='HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp'
if (Test-Path -LiteralPath $listener) {
    Show (Get-ItemProperty -LiteralPath $listener | Select-Object PortNumber,UserAuthentication,SecurityLayer)
}
$service=Get-Service -Name TermService -ErrorAction SilentlyContinue
if ($service) { Show ($service | Select-Object Name,Status,StartType) }
else { Ui 'TermService bilgisi alınamadı.' 'TermService information unavailable.'; $script:review=$true }
Ui 'Kayıt değerleri, etkin güvenlik duvarı ve dış erişim ayrı kontrollerdir.' 'Registry settings, active firewall policy and external reachability are separate checks.'
# END TOOL

# TOOL windows-license
try {
    Show (Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 30 | Select-Object Caption,Version,BuildNumber,InstallDate,LastBootUpTime)
} catch {
    (Ui 'CIM kurulum bilgisi okunamadı: ' 'CIM installation information unavailable: ') + $_.Exception.Message
    $windows=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    Show ($windows | Select-Object EditionID,DisplayVersion,CurrentBuild,UBR)
    if ($windows.InstallDate -gt 0) {
        $date=[DateTimeOffset]::FromUnixTimeSeconds([long]$windows.InstallDate).ToString('o')
        (Ui 'Kayıttaki kurulum zamanı: ' 'Registry installation timestamp: ') + $date
        Ui 'Bu değer bir Windows yükseltmesinde güncellenmiş olabilir.' 'A Windows upgrade may have updated this timestamp.'
    }
    $script:review=$true
}
try {
    $products=@(Get-CimInstance SoftwareLicensingProduct -Filter "ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL" -OperationTimeoutSec 30)
    Show ($products | Select-Object Name,Description,LicenseStatus,GracePeriodRemaining)
    Ui 'LicenseStatus 1 lisanslıdır. Kayıt bulunmaması aktivasyon durumunu kanıtlamaz.' 'LicenseStatus 1 is licensed. An empty result does not establish activation state.'
    if (!$products.Count -or !@($products | Where-Object LicenseStatus -eq 1).Count) { $script:review=$true }
} catch {
    (Ui 'Aktivasyon doğrulanamadı: ' 'Activation could not be verified: ') + $_.Exception.Message
    $script:review=$true
}
# END TOOL

# TOOL uac-audit
$policy=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
Show ($policy | Select-Object EnableLUA,ConsentPromptBehaviorAdmin,ConsentPromptBehaviorUser,PromptOnSecureDesktop,FilterAdministratorToken)
if ($policy.EnableLUA -ne 1 -or $policy.PromptOnSecureDesktop -ne 1 -or $policy.ConsentPromptBehaviorAdmin -eq 0) { $script:review=$true }
# END TOOL

# TOOL local-admins
Need 'Get-LocalGroupMember'
$members=Get-LocalGroupMember -SID ([Security.Principal.SecurityIdentifier]'S-1-5-32-544')
Show ($members | Select-Object Name,ObjectClass,PrincipalSource,SID)
# END TOOL

# TOOL firewall-rules
Need 'Get-NetFirewallRule'
Show (Get-NetFirewallRule -PolicyStore ActiveStore -Enabled True -Direction Inbound |
    Select-Object -First 250 DisplayName,Action,Profile,PolicyStoreSourceType,PrimaryStatus)
# END TOOL



