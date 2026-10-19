# Compares an MSI's components with those of the last release's MSIs, for CI and the release, and runs none of them.
# The new product goes in before the old one is removed, which takes away the files of every old component the new
# one does not share, and either machine's MSI replaces the other's. So a key path keeps its component's GUID from one
# release to the next, and a GUID its key path.
param(
    [Parameter(Mandatory)] [string] $Msi,
    # The MSIs of the newest earlier release that shipped one, for each machine.
    [Parameter(Mandatory)] [string[]] $Previous
)
$ErrorActionPreference = 'Stop'

function Invoke-Com($Target, [string] $Name, [string] $Kind, [object[]] $Arguments) {
    $Target.GetType().InvokeMember($Name, $Kind, $null, $Target, $Arguments)
}

# A Filename or DefaultDir column's long name: short|long, then :source for a directory.
function Get-LongName([string] $Column) {
    (($Column -split ':')[0] -split '\|')[-1]
}

function Get-Rows($Database, [string] $Query) {
    $view = Invoke-Com $Database 'OpenView' 'InvokeMethod' @($Query)
    try {
        Invoke-Com $view 'Execute' 'InvokeMethod' $null | Out-Null
        while (($record = Invoke-Com $view 'Fetch' 'InvokeMethod' $null)) {
            $count = Invoke-Com $record 'FieldCount' 'GetProperty' $null
            # COM takes no PSObject, which a pipeline's $_ is.
            , @(1..$count | ForEach-Object { Invoke-Com $record 'StringData' 'GetProperty' @([int] $_) })
        }
    } finally {
        Invoke-Com $view 'Close' 'InvokeMethod' $null | Out-Null
    }
}

# A folder's path by its long names, from the standard folder it lies in, such as ProgramFiles64Folder, which goes by
# its own name.
function Get-FolderPath($Folders, [string] $Folder) {
    $parts = [Collections.Generic.List[string]]::new()
    $at = $Folder
    while ($true) {
        if (-not $Folders.ContainsKey($at)) { throw "the Directory table has no $at" }
        $parent, $default = $Folders[$at]
        if (-not $parent -or $parent -ceq $at -or $parent -ceq 'TARGETDIR') {
            $parts.Insert(0, $at)
            break
        }
        $name = Get-LongName $default
        if ($name -ne '.') { $parts.Insert(0, $name) }
        $at = $parent
    }
    $parts -join '\'
}

# Each component's GUID by its key path: a file by its folder and long name, a registry value by its root, key and
# name, and a component with no key path by its folder.
function Get-Components([string] $File) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = Invoke-Com $installer 'OpenDatabase' 'InvokeMethod' @((Resolve-Path -LiteralPath $File).Path, 0)
    $folders = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($row in @(Get-Rows $database 'SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`')) {
        $folders[$row[0]] = @($row[1], $row[2])
    }
    $names = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($row in @(Get-Rows $database 'SELECT `File`, `FileName` FROM `File`')) {
        $names[$row[0]] = Get-LongName $row[1]
    }
    $values = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    if (@(Get-Rows $database "SELECT ``Name`` FROM ``_Tables`` WHERE ``Name`` = 'Registry'").Count) {
        foreach ($row in @(Get-Rows $database 'SELECT `Registry`, `Root`, `Key`, `Name` FROM `Registry`')) {
            $values[$row[0]] = "registry value $($row[1]) $($row[2]) '$($row[3])'"
        }
    }
    # Windows reads paths and registry keys without regard to case.
    $paths = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($row in @(Get-Rows $database 'SELECT `Component`, `ComponentId`, `Directory_`, `Attributes`, `KeyPath` FROM `Component`')) {
        $component, $guid, $folder, $attributes, $key = $row
        $where = Get-FolderPath $folders $folder
        # msidbComponentAttributesRegistryKeyPath and msidbComponentAttributesODBCDataSource
        $path = if (-not $key) {
            "folder $where"
        } elseif ([int] $attributes -band 0x4) {
            if (-not $values.ContainsKey($key)) { throw "the component $component names the registry value $key, which no row holds" }
            $values[$key]
        } elseif ([int] $attributes -band 0x20) {
            "ODBC data source $key"
        } else {
            if (-not $names.ContainsKey($key)) { throw "the component $component names the file $key, which no row holds" }
            "file $where\$($names[$key])"
        }
        if ($paths.ContainsKey($path)) { throw "$(Split-Path -Leaf $File) has two components with the key path $path" }
        $paths[$path] = $guid
    }
    [void] [Runtime.InteropServices.Marshal]::ReleaseComObject($database)
    , $paths
}

$leaf = Split-Path -Leaf $Msi
$now = Get-Components $Msi
$drift = [Collections.Generic.List[string]]::new()
foreach ($last in $Previous) {
    $was = Get-Components $last
    $name = Split-Path -Leaf $last
    $pathOf = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
    $shared = 0
    foreach ($path in $was.Keys) {
        if ($was[$path]) { $pathOf[$was[$path]] = $path }
        if (-not $now.ContainsKey($path)) { continue }
        $shared++
        if ($now[$path] -ne $was[$path]) { $drift.Add("$path has the component $($now[$path]), and had $($was[$path]) in $name") }
    }
    foreach ($path in $now.Keys) {
        $guid = $now[$path]
        if ($guid -and $pathOf.ContainsKey($guid) -and $pathOf[$guid] -ne $path) {
            $drift.Add("the component $guid has the key path $path, and had $($pathOf[$guid]) in $name")
        }
    }
    Write-Output "$leaf shares $shared of its $($now.Count) key paths with $name."
}
if ($drift.Count) {
    throw "$leaf gives a component another GUID or key path than the last release did, which an upgrade or a switch of machine would take away:`n$($drift -join "`n")"
}
Write-Output "Each of them keeps its GUID."
exit 0
