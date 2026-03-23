$a = [System.Reflection.Assembly]::LoadFile('C:\Users\david\AppData\Roaming\XIVLauncher\addon\Hooks\dev\FFXIVClientStructs.dll')

# Find DrawDataContainer type
$types = $a.GetTypes() | Where-Object { $_.Name -like '*DrawData*' }
Write-Host "=== DrawData types found ==="
foreach ($t in $types) {
    Write-Host $t.FullName
}

# Get the DrawDataContainer specifically
$ddc = $a.GetTypes() | Where-Object { $_.FullName -like '*DrawDataContainer*' } | Select-Object -First 1
if ($ddc) {
    Write-Host "`n=== DrawDataContainer Fields ==="
    $flags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance
    $fields = $ddc.GetFields($flags)
    foreach ($f in $fields) {
        Write-Host "$($f.Name) : $($f.FieldType.FullName)"
    }
    Write-Host "`n=== DrawDataContainer Properties ==="
    $props = $ddc.GetProperties($flags)
    foreach ($p in $props) {
        Write-Host "$($p.Name) : $($p.PropertyType.FullName)"
    }
    Write-Host "`n=== DrawDataContainer Methods ==="
    $methods = $ddc.GetMethods($flags) | Where-Object { $_.DeclaringType -eq $ddc }
    foreach ($m in $methods) {
        Write-Host "$($m.Name)($($m.GetParameters() | ForEach-Object { $_.ParameterType.Name } | Join-String -Separator ', ')) -> $($m.ReturnType.Name)"
    }
} else {
    Write-Host "DrawDataContainer not found"
}

# Also check Character struct for DrawData field
$charType = $a.GetTypes() | Where-Object { $_.FullName -eq 'FFXIVClientStructs.FFXIV.Client.Game.Character.Character' } | Select-Object -First 1
if ($charType) {
    Write-Host "`n=== Character DrawData field ==="
    $flags2 = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance
    $drawDataField = $charType.GetFields($flags2) | Where-Object { $_.Name -like '*Draw*' -or $_.Name -like '*Equip*' }
    foreach ($f in $drawDataField) {
        Write-Host "$($f.Name) : $($f.FieldType.FullName)"
    }
}
