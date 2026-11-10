function Ui([string]$tr, [string]$en) {
    if ($English) { $en } else { $tr }
}

switch ($Action) {
    'disk' {
        $disks = @(Get-PhysicalDisk)
        if (!$disks.Count) { Ui 'Windows fiziksel disk bilgisi döndürmedi.' 'Windows returned no physical disk information.'; exit 2 }
        $review = $false
        foreach ($disk in $disks) {
            '{0} | {1} | {2} | {3} | {4:N1} GiB' -f $disk.FriendlyName, $disk.MediaType,
                $disk.HealthStatus, ($disk.OperationalStatus -join ', '), ($disk.Size / 1GB)
            if ($disk.HealthStatus -ne 'Healthy') { $review = $true }
            try {
                $counters = @($disk | Get-StorageReliabilityCounter -ErrorAction Stop)
                $readable = $false
                foreach ($counter in $counters) {
                    foreach ($name in 'Temperature','Wear','PowerOnHours','ReadErrorsUncorrected','WriteErrorsUncorrected') {
                        if ($null -ne $counter.$name) { "  ${name}: $($counter.$name)"; $readable = $true }
                    }
                    if ($counter.ReadErrorsUncorrected -gt 0 -or $counter.WriteErrorsUncorrected -gt 0) { $review = $true }
                }
                if (!$readable) { Ui '  Denetleyici ayrıntılı sayaç sunmadı.' '  The controller did not expose detailed counters.' }
            } catch { (Ui '  Sağlık sayaçları alınamadı: ' '  Health counters are unavailable: ') + $_.Exception.Message }
        }
        if ($review) { exit 2 }
    }
    'wu' {
        foreach ($name in 'wuauserv','BITS','cryptsvc') {
            $service = Get-Service -Name $name -ErrorAction SilentlyContinue
            if ($service) { "$name | $($service.Status) | $($service.StartType)" }
        }
        Ui 'Windows Update aracısından yanıt bekleniyor...' 'Waiting for the Windows Update agent...'
        $session = New-Object -ComObject Microsoft.Update.Session
        $session.ClientApplicationID = 'Windows Bakim Merkezi'
        $searcher = $session.CreateUpdateSearcher()
        $result = $searcher.Search('IsInstalled=0 and IsHidden=0')
        Ui "Sonuç kodu: $([int]$result.ResultCode). Bulunan güncelleme: $($result.Updates.Count)." "Result code: $([int]$result.ResultCode). Updates found: $($result.Updates.Count)."
        for ($i = 0; $i -lt [Math]::Min(120, $result.Updates.Count); $i++) {
            $update = $result.Updates.Item($i)
            $update.Title
        }
        if ($result.Updates.Count -gt 120) { Ui 'İlk 120 sonuç gösterildi.' 'Showing the first 120 results.' }
        if ([int]$result.ResultCode -ne 2) { exit 2 }
    }
    'restore' {
        $tag = 'Windows Bakim Merkezi ' + [Guid]::NewGuid().ToString('N').Substring(0,8)
        try {
            Checkpoint-Computer -Description $tag -RestorePointType MODIFY_SETTINGS -WarningAction Stop
            $point = Get-ComputerRestorePoint | Where-Object Description -eq $tag | Select-Object -First 1
            if ($null -eq $point) {
                Ui 'Yeni geri yükleme noktası Windows listesinde doğrulanamadı. Sistem Koruması ayarını ve son oluşturulma zamanını kontrol edin.' 'The new restore point could not be verified. Check System Protection and the time of the last restore point.'
                exit 2
            }
            Ui "Geri yükleme noktası doğrulandı. Sıra numarası: $($point.SequenceNumber)" "Restore point verified. Sequence number: $($point.SequenceNumber)"
        } catch {
            (Ui 'Geri yükleme noktası doğrulanamadı: ' 'Restore point could not be verified: ') + $_.Exception.Message
            Ui 'Sistem Koruması kapalı olabilir veya Windows oluşturma sıklığını sınırlamış olabilir.' 'System Protection may be disabled, or Windows may be limiting how often restore points can be created.'
            exit 2
        }
    }
    { $_ -in 'store-check','store-repair','xbox-repair' } {
        $store = @('Microsoft.WindowsStore','Microsoft.StorePurchaseApp')
        $xbox = @('Microsoft.GamingApp','Microsoft.GamingServices','Microsoft.XboxIdentityProvider')
        $names = switch ($Action) {
            'store-check'  { $store + $xbox }
            'store-repair' { $store }
            'xbox-repair'  { $xbox }
        }
        $review = $false
        foreach ($name in $names) {
            try {
                $packages = @(Get-AppxPackage -Name $name)
                if (!$packages.Count) {
                    Ui "$name | Bu hesapta kurulu paket bulunamadı." "$name | No installed package was found for this account."
                    $review = $true
                    continue
                }
                foreach ($package in $packages) {
                    "$name | $($package.Version) | $($package.Status)"
                    if ($Action -eq 'store-check') {
                        if ($package.Status -ne 'Ok') { $review = $true }
                        continue
                    }
                    $manifest = Join-Path -Path $package.InstallLocation -ChildPath 'AppxManifest.xml'
                    Add-AppxPackage -DisableDevelopmentMode -Register $manifest
                    $current = Get-AppxPackage -Name $name | Where-Object PackageFullName -eq $package.PackageFullName
                    if (!$current -or $current.Status -ne 'Ok') {
                        Ui "$name | Yeniden kayıt sonrası paket durumu doğrulanamadı." "$name | Package status could not be verified after registration."
                        $review = $true
                    } else { Ui "$name | Paket kaydı doğrulandı." "$name | Package registration verified." }
                }
            } catch {
                "$name | " + $_.Exception.Message
                $review = $true
            }
        }
        if ($Action -eq 'store-check') {
            foreach ($name in 'GamingServices','GamingServicesNet','XblAuthManager','XblGameSave','XboxNetApiSvc') {
                $service = Get-Service -Name $name -ErrorAction SilentlyContinue
                if ($service) { "$name | $($service.Status) | $($service.StartType)" }
                else { Ui "$name | Hizmet bulunamadı." "$name | Service not found." }
            }
            Ui 'İsteğe bağlı Xbox paketleri kurulu olmayabilir. Tetiklemeli hizmetler kullanılmadığında durabilir.' 'Optional Xbox packages may be absent. Trigger-start services can be stopped when idle.'
        }
        if ($review) { exit 2 }
    }
    default { throw (Ui 'Geçersiz yerleşik bakım adımı.' 'Invalid built-in maintenance step.') }
}
