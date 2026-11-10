$all = @(Get-AppxPackage -PackageTypeFilter Main,Bundle)
foreach ($pair in $Allowed) {
    $name, $publisher = $pair.Split('|', 2)
    $matching = @($all | Where-Object {
        $_.Name -ceq $name -and $_.PublisherId -ceq $publisher -and
        $_.NonRemovable -is [bool] -and !$_.NonRemovable -and !$_.IsFramework -and !$_.IsResourcePackage
    })
    $bundle = @($matching | Where-Object IsBundle)
    if ($bundle.Count) { $matching = $bundle }
    if (!$Remove) {
        if ($matching.Count -eq 1) { "BAKIM-APP|$name|$($matching[0].Version)" }
        continue
    }
    if ($Remove -cne $name -or $matching.Count -ne 1 -or "$($matching[0].Version)" -cne $ExpectedVersion) { exit 2 }
    $full = $matching[0].PackageFullName
    Remove-AppxPackage -Package $full -Confirm:$false
    if (@(Get-AppxPackage -Name $name -PackageTypeFilter Main,Bundle | Where-Object PublisherId -ceq $publisher).Count) {
        exit 2
    }
}
