#Requires -Version 5.1
<#
.SYNOPSIS
Prepare and explicitly launch an isolated, test-only RimWorld runtime.
.DESCRIPTION
Default action is Prepare. This script never builds, overwrites an existing run,
deletes a runtime, kills a process, restarts on timeout, or changes live game config.
Launch returns immediately with a process identity; Status and Verify are read-only.
#>
[CmdletBinding()]
param(
    [ValidateSet('Prepare', 'Launch', 'Status', 'Verify')]
    [string]$Action = 'Prepare',
    [string]$RunDirectory,
    [string]$GameRoot = 'C:\Steam\steamapps\common\RimWorld',
    [string]$WorkshopRoot = 'C:\Steam\steamapps\workshop\content\294100',
    [string]$PlayerSaveDataRoot,
    [ValidateSet('bootstrap', 'BG01', 'BG02', 'BG03-P1', 'L04-O1', 'CAP01', 'CAP02', 'CAP03-B', 'CAP03-A', 'CAP03-C', 'L04-O1-DELIVERY', 'L04-B1', 'L04-B1-D2', 'L40-Q1', 'L40-UI-P0')]
    [string]$CaseId = 'bootstrap',
    [ValidateSet('bootstrap', 'baseline-gap', 'satisfied')]
    [string]$ExpectedBehavior = 'bootstrap',
    [ValidateSet('None', 'WorkerUnityError', 'WorkerVerseError', 'LateWorkerError', 'TerminalRace')]
    [string]$NegativeControl = 'None',
    [ValidateSet('Workshop', 'Built')]
    [string]$HdSource = 'Workshop',
    [string]$BuiltModRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$HarnessAssembly = (Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\RuntimeHarness\bin\Release\HaulersDream.RuntimeHarness.dll'),
    [switch]$CommonSense
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskBase = [IO.Path]::GetFullPath((Join-Path $env:TEMP 'haulersdream-runtime-tests')).TrimEnd('\', '/')
$taskUtf8 = New-Object System.Text.UTF8Encoding($false)
$taskCopiedFiles = New-Object 'System.Collections.Generic.List[object]'
. (Join-Path $PSScriptRoot 'runtime-validation-storage.ps1')
. (Join-Path $PSScriptRoot 'runtime-validation-bills.ps1')
. (Join-Path $PSScriptRoot 'runtime-validation-projection.ps1')
. (Join-Path $PSScriptRoot 'runtime-validation-partial-bills.ps1')
. (Join-Path $PSScriptRoot 'runtime-validation-asf-budget.ps1')
. (Join-Path $PSScriptRoot 'runtime-validation-asf-filters.ps1')
. (Join-Path $PSScriptRoot 'runtime-validation-asf-wrappers.ps1')
. (Join-Path $PSScriptRoot 'runtime-validation-recurrence.ps1')
. (Join-Path $PSScriptRoot 'runtime-validation-inflight.ps1')

function Get-CanonicalPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not [IO.Path]::IsPathRooted($Path) -or $Path.Contains('=') -or $Path.Contains('"')) {
        throw "Expected an absolute path without '=' or quotes: $Path"
    }
    return [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

function Assert-ChildPath([string]$Path, [string]$Parent) {
    $taskPath = Get-CanonicalPath $Path
    $taskParent = Get-CanonicalPath $Parent
    if (-not $taskPath.StartsWith($taskParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the expected directory: $taskPath (parent $taskParent)"
    }
}

function Assert-NoReparsePath([string]$Path) {
    $taskCurrent = Get-CanonicalPath $Path
    while ($taskCurrent) {
        if (Test-Path -LiteralPath $taskCurrent) {
            $taskItem = Get-Item -LiteralPath $taskCurrent -Force
            if (($taskItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse points are not permitted: $taskCurrent"
            }
        }
        $taskCurrent = [IO.Path]::GetDirectoryName($taskCurrent)
    }
}

function Get-Sha256([string]$Path) {
    Assert-NoReparsePath $Path
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Write-NewText([string]$Path, [string]$Text) {
    Assert-ChildPath $Path $taskBase
    Assert-NoReparsePath $Path
    $taskBytes = $taskUtf8.GetBytes($Text)
    $taskStream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $taskStream.Write($taskBytes, 0, $taskBytes.Length); $taskStream.Flush($true) }
    finally { $taskStream.Dispose() }
}

function Write-NewJson([string]$Path, $Value) {
    Write-NewText $Path ($Value | ConvertTo-Json -Depth 20)
}

function Write-RunState([string]$Path, $Value) {
    # Atomic replacement is confined to this run's own controller-state file.
    Assert-ChildPath $Path $taskBase
    Assert-NoReparsePath $Path
    $taskTemporary = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    Write-NewJson $taskTemporary $Value
    if (Test-Path -LiteralPath $Path) {
        # PowerShell binds a null string argument to an empty path on some runtimes.
        # Keep a uniquely named prior state as evidence and pass a real backup path.
        $taskPrevious = $Path + '.' + [guid]::NewGuid().ToString('N') + '.previous'
        Assert-ChildPath $taskPrevious $taskBase
        Assert-NoReparsePath $taskPrevious
        [IO.File]::Replace($taskTemporary, $Path, $taskPrevious)
    }
    else { [IO.File]::Move($taskTemporary, $Path) }
}

function Copy-VerifiedFile([string]$Source, [string]$Destination, [string]$Role) {
    Assert-NoReparsePath $Source
    Assert-ChildPath $Destination $script:taskRun
    Assert-NoReparsePath $Destination
    if (Test-Path -LiteralPath $Destination) { throw "Copy destination already exists: $Destination" }
    $taskHash = Get-Sha256 $Source
    [IO.Directory]::CreateDirectory((Split-Path -Parent $Destination)) | Out-Null
    [IO.File]::Copy($Source, $Destination, $false)
    if ((Get-Sha256 $Destination) -ne $taskHash) { throw "Copy verification failed: $Destination" }
    $taskCopiedFiles.Add([pscustomobject]@{ role = $Role; sourcePath = $Source; path = $Destination; sha256 = $taskHash })
}

function Copy-VerifiedTree([string]$Source, [string]$Destination, [string]$Role) {
    Assert-NoReparsePath $Source
    Assert-ChildPath $Destination $script:taskRun
    if (-not (Test-Path -LiteralPath $Source -PathType Container)) { throw "Missing source directory: $Source" }
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    foreach ($taskEntry in Get-ChildItem -LiteralPath $Source -Force) {
        if (($taskEntry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Refusing linked source: $($taskEntry.FullName)" }
        $taskDestination = Join-Path $Destination $taskEntry.Name
        if ($taskEntry.PSIsContainer) { Copy-VerifiedTree $taskEntry.FullName $taskDestination $Role }
        else { Copy-VerifiedFile $taskEntry.FullName $taskDestination $Role }
    }
}

function Copy-HdContent([string]$Source, [string]$Destination) {
    # Exact shipped trees. Never copy source control, credentials, saves or investigation data.
    foreach ($taskTree in @('About', 'Defs', 'Patches', 'Languages', 'Textures')) {
        $taskSourceTree = Join-Path $Source $taskTree
        if (Test-Path -LiteralPath $taskSourceTree) { Copy-VerifiedTree $taskSourceTree (Join-Path $Destination $taskTree) 'hd' }
    }
    Copy-VerifiedFile (Join-Path $Source 'LoadFolders.xml') (Join-Path $Destination 'LoadFolders.xml') 'hd'
    foreach ($taskDll in Get-ChildItem -LiteralPath (Join-Path $Source '1.6\Assemblies') -File -Filter '*.dll') {
        Copy-VerifiedFile $taskDll.FullName (Join-Path $Destination ('1.6\Assemblies\' + $taskDll.Name)) 'hd'
    }
    foreach ($taskRequired in @('About\About.xml', '1.6\Assemblies\HaulersDream.dll', '1.6\Assemblies\HaulersDream.Core.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Destination $taskRequired))) { throw "Incomplete HD content: $taskRequired" }
    }
}

function Get-AssemblyIdentity([string]$Path) {
    $taskName = [Reflection.AssemblyName]::GetAssemblyName($Path)
    return [pscustomobject]@{
        name = $taskName.Name; path = $Path; sha256 = Get-Sha256 $Path
        assemblyVersion = $taskName.Version.ToString()
    }
}

function Get-ProtectedSnapshot([string[]]$Paths) {
    $taskRecords = New-Object 'System.Collections.Generic.List[object]'
    foreach ($taskProtected in $Paths) {
        Assert-NoReparsePath $taskProtected
        if (-not (Test-Path -LiteralPath $taskProtected)) {
            $taskRecords.Add([pscustomobject]@{ path = $taskProtected; exists = $false; sha256 = '' })
            continue
        }
        $taskItem = Get-Item -LiteralPath $taskProtected
        if ($taskItem.PSIsContainer) {
            foreach ($taskChild in Get-ChildItem -LiteralPath $taskProtected -File -Recurse -Force) {
                $taskRecords.Add([pscustomobject]@{ path = $taskChild.FullName; exists = $true; sha256 = Get-Sha256 $taskChild.FullName })
            }
        } else {
            $taskRecords.Add([pscustomobject]@{ path = $taskProtected; exists = $true; sha256 = Get-Sha256 $taskProtected })
        }
    }
    return $taskRecords.ToArray()
}

function Compare-ProtectedSnapshot($Manifest) {
    $taskCurrent = @(Get-ProtectedSnapshot @($Manifest.protectedRoots))
    $taskBefore = @{}
    foreach ($taskRecord in $Manifest.protectedBefore) { $taskBefore[$taskRecord.path] = $taskRecord }
    $taskChanges = New-Object 'System.Collections.Generic.List[string]'
    foreach ($taskRecord in $taskCurrent) {
        if (-not $taskBefore.ContainsKey($taskRecord.path)) { $taskChanges.Add('added: ' + $taskRecord.path); continue }
        $taskOriginal = $taskBefore[$taskRecord.path]
        if ($taskRecord.exists -ne $taskOriginal.exists -or $taskRecord.sha256 -ne $taskOriginal.sha256) {
            $taskChanges.Add('changed: ' + $taskRecord.path)
        }
        $taskBefore.Remove($taskRecord.path)
    }
    foreach ($taskMissing in $taskBefore.Keys) { $taskChanges.Add('missing: ' + $taskMissing) }
    return $taskChanges.ToArray()
}

function Read-PreparedRun {
    if (-not $RunDirectory) { throw 'RunDirectory is required for Launch, Status and Verify.' }
    $script:taskRun = Get-CanonicalPath $RunDirectory
    Assert-ChildPath $script:taskRun $taskBase
    Assert-NoReparsePath $script:taskRun
    $taskManifestPath = Join-Path $script:taskRun 'manifest.json'
    # Write-NewJson and the harness write UTF-8. PS5.1 otherwise treats BOM-less
    # files as the system code page and corrupts non-ASCII protected/copy paths.
    $taskManifest = Get-Content -LiteralPath $taskManifestPath -Encoding UTF8 -Raw | ConvertFrom-Json
    $taskState = Get-Content -LiteralPath (Join-Path $script:taskRun 'controller-state.json') -Encoding UTF8 -Raw | ConvertFrom-Json
    if ($taskManifest.schemaVersion -ne 1 -or $taskManifest.runDirectory -ne $script:taskRun -or $taskManifest.caseId -notin @('bootstrap', 'BG01', 'BG02', 'BG03-P1', 'L04-O1', 'CAP01', 'CAP02', 'CAP03-B', 'CAP03-A', 'CAP03-C', 'L04-O1-DELIVERY', 'L04-B1', 'L04-B1-D2', 'L40-Q1', 'L40-UI-P0')) {
        throw 'Unsupported manifest or mismatched run directory.'
    }
    if ($taskState.manifestSha256 -ne (Get-Sha256 $taskManifestPath)) { throw 'Prepared manifest was modified; refuse to use it.' }
    if ($taskState.runId -ne $taskManifest.runId) { throw 'Controller state belongs to another run.' }
    if (-not $taskManifest.PSObject.Properties['playerSaveDataRoot']) {
        throw 'This legacy preparation did not identify the actual player data root. Preserve it as evidence and prepare a new run explicitly.'
    }
    $taskPlayerRoot = Get-CanonicalPath $taskManifest.playerSaveDataRoot
    foreach ($taskRequiredProtected in @((Join-Path $taskPlayerRoot 'Config'), (Join-Path $taskPlayerRoot 'Saves'))) {
        if ($taskRequiredProtected -notin @($taskManifest.protectedRoots)) { throw 'Manifest lacks the explicitly selected player Config/Saves protection.' }
    }
    foreach ($taskExpected in @(
        @('runtimeDirectory', 'runtime'), @('saveDataDirectory', 'SaveData'), @('evidenceDirectory', 'evidence'), @('logPath', 'evidence\Player.log')
    )) {
        if ((Get-CanonicalPath $taskManifest.($taskExpected[0])) -ne (Join-Path $script:taskRun $taskExpected[1])) {
            throw "Unexpected manifest layout: $($taskExpected[0])"
        }
    }
    return [pscustomobject]@{ manifest = $taskManifest; state = $taskState }
}

function Get-RunProcess($Manifest, $State) {
    if (-not $State.processId) { return [pscustomobject]@{ status = 'not-recorded'; processId = $null; process = $null } }
    $taskProcess = Get-Process -Id ([int]$State.processId) -ErrorAction SilentlyContinue
    if ($null -eq $taskProcess) { return [pscustomobject]@{ status = 'exited'; processId = $State.processId; process = $null } }
    try {
        $taskExpectedExe = Join-Path $Manifest.runtimeDirectory 'RimWorldWin64.exe'
        # PowerShell 7 converts ISO JSON timestamps to DateTime; Windows PowerShell keeps strings.
        $taskRecordedStart = ([DateTime]$State.processStartedUtc).ToUniversalTime().ToString('O')
        if ((Get-CanonicalPath $taskProcess.Path) -ne $taskExpectedExe -or $taskProcess.StartTime.ToUniversalTime().ToString('O') -ne $taskRecordedStart) {
            return [pscustomobject]@{ status = 'identity-mismatch'; processId = $State.processId; process = $null }
        }
        return [pscustomobject]@{ status = 'running'; processId = $State.processId; process = $taskProcess }
    } catch {
        return [pscustomobject]@{ status = 'observation-unavailable'; processId = $State.processId; process = $null; detail = $_.Exception.Message }
    }
}

function Test-EvidenceFields($Value, [hashtable]$Fields, [string]$Context, $Problems) {
    if ($null -eq $Value -or $Value -isnot [pscustomobject]) {
        $Problems.Add($Context + ' must be a JSON object.')
        return $false
    }
    $taskValidShape = $true
    foreach ($taskFieldName in $Fields.Keys) {
        $taskProperty = $Value.PSObject.Properties[$taskFieldName]
        if ($null -eq $taskProperty) {
            $Problems.Add($Context + ' lacks ' + $taskFieldName + '.')
            $taskValidShape = $false
            continue
        }
        $taskValue = $taskProperty.Value
        $taskType = $Fields[$taskFieldName]
        $taskNullable = $taskType.StartsWith('nullable-')
        if ($taskNullable -and $null -eq $taskValue) { continue }
        if ($taskNullable) { $taskType = $taskType.Substring(9) }
        $taskTypeMatches = switch ($taskType) {
            'string' { $taskValue -is [string] }
            'integer' { $taskValue -is [int] -or $taskValue -is [long] }
            'boolean' { $taskValue -is [bool] }
            'array' { $taskValue -is [array] }
            'object' { $taskValue -is [pscustomobject] }
            default { $false }
        }
        if (-not $taskTypeMatches) {
            $Problems.Add($Context + '.' + $taskFieldName + ' has an invalid type; expected ' + $Fields[$taskFieldName] + '.')
            $taskValidShape = $false
        }
    }
    return $taskValidShape
}

function Get-ResultShapeProblems($Value, [string]$Case, [string]$Control) {
    $taskShapeProblems = New-Object 'System.Collections.Generic.List[string]'
    $taskShape = Test-EvidenceFields $Value @{
        schemaVersion='integer'; runId='string'; caseId='string'; processId='integer'; status='string';
        unityErrorsObserved='integer'; assertions='array'; mods='array'; assemblies='array'
    } 'result' $taskShapeProblems
    if (-not $taskShape) { return $taskShapeProblems.ToArray() }
    if ($Value.schemaVersion -ne 1) { $taskShapeProblems.Add('Unsupported result schema version.') }
    foreach ($taskAssertion in $Value.assertions) {
        Test-EvidenceFields $taskAssertion @{id='string'; passed='boolean'; observed='string'} 'result.assertions[]' $taskShapeProblems | Out-Null
    }
    foreach ($taskMod in $Value.mods) {
        Test-EvidenceFields $taskMod @{packageId='string'; rootPath='string'} 'result.mods[]' $taskShapeProblems | Out-Null
    }
    foreach ($taskAssembly in $Value.assemblies) {
        Test-EvidenceFields $taskAssembly @{name='string'; path='string'; sha256='string'; assemblyVersion='string'; moduleVersionId='string'} 'result.assemblies[]' $taskShapeProblems | Out-Null
    }
    if ($Control -ne 'None') {
        if (Test-EvidenceFields $Value @{negativeControl='object'} 'result' $taskShapeProblems) {
            Test-EvidenceFields $Value.negativeControl @{
                name='string'; expectationMatched='boolean'; marker='string'; workerThread='integer'; mainThread='integer';
                callbackThread='integer'; markerCaptureSequence='integer'; finalCaptureSequence='integer';
                matchingCapturedErrors='integer'; expectedCapturedErrors='integer'; workerFinished='boolean';
                callbackEntryOrder='integer'; closeRequestedOrder='integer'; capturedOrder='integer'; captureClosedOrder='integer';
                boundaryOrderingMatched='boolean'; exception='nullable-string'
            } 'result.negativeControl' $taskShapeProblems | Out-Null
        }
    }
    if ($Case -in @('BG01','BG02')) {
        if (Test-EvidenceFields $Value @{scenario='object'} 'result' $taskShapeProblems) {
            Test-EvidenceFields $Value.scenario @{
                fixtureValid='boolean'; requestedBehaviorSatisfied='boolean'; expectationMatched='boolean'; expectedBehavior='string'; status='string';
                timedOut='boolean'; completedNativeRecipe='boolean'; cleaningDuringNativeBill='boolean'; completeInventorySweep='boolean';
                nativeDoBillJobs='integer'; finalRice='integer'; finalPotatoes='integer'; finalMeals='integer';
                nativeCleanupSucceeded='boolean'; noSecondGatherOrRegather='boolean'; noIngredientStorageDetour='boolean'; productCountsStable='boolean';
                productCreationCalls='integer'; productUnitsCreated='integer'; maxSettledMeals='integer'; stablePostProductTicks='integer';
                riceAcquired='integer'; potatoesAcquired='integer'; seededCleaningIncrements='integer'
            } 'result.scenario' $taskShapeProblems | Out-Null
        }
    }
    if ($Case -eq 'L04-O1') {
        if (Test-EvidenceFields $Value @{storageOwnership='object'} 'result' $taskShapeProblems) {
            $taskStorageShape = Test-EvidenceFields $Value.storageOwnership @{
                caseId='string'; expectedBehavior='string'; scope='string'; startedTick='integer'; finishedTick='integer';
                adapterAssembly='string'; adapterModuleId='string'; fixtureValid='boolean'; requestedBehaviorSatisfied='boolean';
                expectationMatched='boolean'; status='string'; error='nullable-string'; assertions='array'; observations='array'
            } 'result.storageOwnership' $taskShapeProblems
            if ($taskStorageShape) {
                foreach ($taskAssertion in $Value.storageOwnership.assertions) {
                    Test-EvidenceFields $taskAssertion @{kind='string'; id='string'; passed='boolean'; observed='string'} 'result.storageOwnership.assertions[]' $taskShapeProblems | Out-Null
                }
                foreach ($taskObservation in $Value.storageOwnership.observations) {
                    Test-EvidenceFields $taskObservation @{
                        id='string'; tick='integer'; custody='string'; actualDelivering='boolean'; actualFreeUnits='integer'; actualStoreCellAllowed='nullable-boolean';
                        correctedDelivering='boolean'; correctedFreeUnits='integer'; baselineDelivering='boolean'; baselineFreeUnits='integer';
                        correctedMatched='boolean'; baselineMatched='boolean'
                    } 'result.storageOwnership.observations[]' $taskShapeProblems | Out-Null
                }
            }
        }
    }
    Test-StorageExtraShape $Value $Case $taskShapeProblems
    if ($Case -eq 'BG02') { Test-BulkRecipeShape $Value $taskShapeProblems }
    if ($Case -eq 'CAP02') { Test-ProjectionShape $Value $taskShapeProblems | Out-Null }
    if ($Case -eq 'CAP03-B') { Test-AsfBudgetShape $Value $taskShapeProblems | Out-Null }
    if ($Case -eq 'CAP03-A') { Test-AsfFilterShape $Value $taskShapeProblems | Out-Null }
    if ($Case -eq 'CAP03-C') { Test-AsfWrapperShape $Value $taskShapeProblems | Out-Null }
    if ($Case -eq 'BG03-P1') { Test-PartialBillShape $Value $taskShapeProblems | Out-Null }
    if ($Case -eq 'L04-B1') { Test-RecurrenceShape $Value $taskShapeProblems | Out-Null }
    if ($Case -eq 'L04-B1-D2') { Test-InFlightShape $Value $taskShapeProblems | Out-Null }
    return $taskShapeProblems.ToArray()
}

function Test-RequiredAssertion($Assertions, [string]$Id, [bool]$Expected) {
    $taskMatches = @($Assertions | Where-Object { $_.id -eq $Id })
    return $taskMatches.Count -eq 1 -and $taskMatches[0].passed -eq $Expected
}

if ($Action -eq 'Prepare') {
    if ($CaseId -in @('BG01','BG02','BG03-P1') -and (-not $CommonSense -or $ExpectedBehavior -notin @('baseline-gap', 'satisfied') -or $NegativeControl -ne 'None')) {
        throw 'Ordinary cooking cases require -CommonSense and explicit -ExpectedBehavior baseline-gap or satisfied; negative controls are bootstrap-only.'
    }
    if ($CaseId -in @('L04-O1', 'CAP01', 'L04-O1-DELIVERY') -and ($ExpectedBehavior -notin @('baseline-gap', 'satisfied') -or $NegativeControl -ne 'None')) {
        throw 'Storage cases require explicit ExpectedBehavior baseline-gap or satisfied; negative controls are bootstrap-only.'
    }
    if ($CaseId -in @('CAP01', 'L04-O1-DELIVERY') -and $CommonSense) {
        throw 'These initial storage fixtures require Harmony/Core/HD only; compatibility variants need separate reviewed cases.'
    }
    if ($CaseId -eq 'CAP02' -and ($ExpectedBehavior -ne 'satisfied' -or $NegativeControl -ne 'None' -or $CommonSense -or $HdSource -ne 'Built')) {
        throw 'CAP02 requires Built HD, satisfied expectation, Harmony/Core/HD only and no bootstrap negative control.'
    }
    if ($CaseId -in @('CAP03-B','CAP03-A','CAP03-C') -and ($ExpectedBehavior -ne 'satisfied' -or $NegativeControl -ne 'None' -or $CommonSense -or $HdSource -ne 'Built')) {
        throw 'CAP03-A/B/C require Built HD, satisfied expectation, their actual ASF/Neat dependency set and no CommonSense or bootstrap negative control.'
    }
    if ($CaseId -eq 'L04-B1' -and ($ExpectedBehavior -notin @('baseline-gap','satisfied') -or $NegativeControl -ne 'None' -or $CommonSense)) {
        throw 'L04-B1 requires explicit baseline-gap or satisfied, Harmony/Core/HD only, and no negative control.'
    }
    if ($CaseId -eq 'L04-B1-D2' -and ($ExpectedBehavior -notin @('baseline-gap','satisfied') -or $NegativeControl -ne 'None' -or $CommonSense)) {
        throw 'L04-B1-D2 requires explicit baseline-gap or satisfied, Harmony/Core/HD only, and no negative control.'
    }
    if ($CaseId -in @('L40-Q1','L40-UI-P0') -and ($ExpectedBehavior -ne 'satisfied' -or $NegativeControl -ne 'None' -or $CommonSense -or $HdSource -ne 'Built')) {
        throw 'Quantity command/UI preflight captures require Built HD, satisfied expectation, Harmony/Core/HD only and no bootstrap negative control.'
    }
    if ($CaseId -eq 'bootstrap' -and $ExpectedBehavior -ne 'bootstrap') { throw 'Bootstrap requires ExpectedBehavior bootstrap.' }
    if ([string]::IsNullOrWhiteSpace($PlayerSaveDataRoot)) {
        throw 'Prepare requires explicit -PlayerSaveDataRoot for the actual player. Never infer it from the sandbox account profile.'
    }
    $PlayerSaveDataRoot = Get-CanonicalPath $PlayerSaveDataRoot
    Assert-NoReparsePath $PlayerSaveDataRoot
    foreach ($taskPlayerFolder in @($PlayerSaveDataRoot, (Join-Path $PlayerSaveDataRoot 'Config'), (Join-Path $PlayerSaveDataRoot 'Saves'))) {
        if (-not (Test-Path -LiteralPath $taskPlayerFolder -PathType Container)) { throw "Explicit player data directory does not exist: $taskPlayerFolder" }
    }
    if ($PlayerSaveDataRoot.StartsWith($taskBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'PlayerSaveDataRoot must identify the real player data, not an isolated test run.'
    }
    $taskRunId = [guid]::NewGuid().ToString('N')
    if (-not $RunDirectory) { $RunDirectory = Join-Path $taskBase $taskRunId }
    $script:taskRun = Get-CanonicalPath $RunDirectory
    Assert-ChildPath $script:taskRun $taskBase
    Assert-NoReparsePath $script:taskRun
    if (Test-Path -LiteralPath $script:taskRun) { throw 'Run directory already exists; choose a new run instead of overwriting it.' }
    $GameRoot = Get-CanonicalPath $GameRoot
    $WorkshopRoot = Get-CanonicalPath $WorkshopRoot
    $HarnessAssembly = Get-CanonicalPath $HarnessAssembly
    $taskHdRoot = if ($HdSource -eq 'Workshop') { Join-Path $WorkshopRoot '3742459652' } else { Get-CanonicalPath $BuiltModRoot }
    foreach ($taskInput in @($GameRoot, $WorkshopRoot, $taskHdRoot, $HarnessAssembly)) { Assert-NoReparsePath $taskInput }
    if (-not (Test-Path -LiteralPath $HarnessAssembly -PathType Leaf)) { throw 'Build the separate RuntimeHarness project before preparing a run. This script never builds.' }
    $taskHarnessAbout = Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\RuntimeHarness\About'
    $taskRuntime = Join-Path $script:taskRun 'runtime'
    $taskSavedata = Join-Path $script:taskRun 'SaveData'
    $taskEvidence = Join-Path $script:taskRun 'evidence'
    $taskLog = Join-Path $taskEvidence 'Player.log'
    $taskConfig = Join-Path $taskSavedata 'Config'
    $taskProtectedRoots = @(
        (Join-Path $PlayerSaveDataRoot 'Config'),
        (Join-Path $PlayerSaveDataRoot 'Saves'),
        (Join-Path $GameRoot 'Mods\HaulersDream\1.6\Assemblies'),
        (Join-Path $WorkshopRoot '3742459652\1.6\Assemblies')
    )
    if ($CaseId -in @('CAP03-B','CAP03-A','CAP03-C')) {
        $taskProtectedRoots += @((Join-Path $WorkshopRoot '3033901359'), (Join-Path $WorkshopRoot '3416243474'))
    }
    $taskProtectedBefore = @(Get-ProtectedSnapshot $taskProtectedRoots)
    [IO.Directory]::CreateDirectory($script:taskRun) | Out-Null
    foreach ($taskFolder in @($taskRuntime, $taskConfig, $taskEvidence, (Join-Path $taskSavedata 'Saves'), (Join-Path $taskRuntime 'Mods'))) {
        [IO.Directory]::CreateDirectory($taskFolder) | Out-Null
    }
    Write-NewJson (Join-Path $script:taskRun 'preparation-start.json') ([ordered]@{
        runId = $taskRunId; createdUtc = [DateTime]::UtcNow.ToString('O'); status = 'preparing'; hdSource = $HdSource
    })
    foreach ($taskTree in @('Data', 'MonoBleedingEdge', 'RimWorldWin64_Data')) {
        Copy-VerifiedTree (Join-Path $GameRoot $taskTree) (Join-Path $taskRuntime $taskTree) 'game'
    }
    foreach ($taskFile in @(
        'RimWorldWin64.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'steam_appid.txt', 'Version.txt', 'ScenarioPreview.jpg',
        'SteamInputDefaultConfiguration.vdf', 'SteamInputDefaultConfiguration_SteamDeck.vdf', 'SteamInputDefaultConfiguration_SteamFrame.vdf',
        'EULA.txt', 'Licenses.txt', 'ModUpdating.txt', 'Readme.txt'
    )) { Copy-VerifiedFile (Join-Path $GameRoot $taskFile) (Join-Path $taskRuntime $taskFile) 'game' }
    $taskHarmonyRoot = Join-Path $taskRuntime 'Mods\Harmony'
    $taskCsRoot = Join-Path $taskRuntime 'Mods\CommonSense'
    $taskAsfRoot = Join-Path $taskRuntime 'Mods\AdaptiveStorageFramework'
    $taskNeatRoot = Join-Path $taskRuntime 'Mods\NeatStorage'
    $taskHdDestination = Join-Path $taskRuntime 'Mods\HaulersDream'
    $taskHarnessRoot = Join-Path $taskRuntime 'Mods\HaulersDreamRuntimeHarness'
    Copy-VerifiedTree (Join-Path $WorkshopRoot '2009463077') $taskHarmonyRoot 'harmony'
    if ($CommonSense) { Copy-VerifiedTree (Join-Path $WorkshopRoot '1561769193') $taskCsRoot 'common-sense' }
    if ($CaseId -in @('CAP03-B','CAP03-A','CAP03-C')) {
        Copy-VerifiedTree (Join-Path $WorkshopRoot '3033901359') $taskAsfRoot 'adaptive-storage-framework'
        Copy-VerifiedTree (Join-Path $WorkshopRoot '3416243474') $taskNeatRoot 'neat-storage'
    }
    Copy-HdContent $taskHdRoot $taskHdDestination
    Copy-VerifiedTree $taskHarnessAbout (Join-Path $taskHarnessRoot 'About') 'harness'
    Copy-VerifiedFile $HarnessAssembly (Join-Path $taskHarnessRoot 'Assemblies\HaulersDream.RuntimeHarness.dll') 'harness'
    if ($CaseId -eq 'CAP03-A') {
        $taskFilterDefs = Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\RuntimeHarness\Fixtures\CAP03-A\Defs\Cap03FilterDefs.xml'
        if ((Get-Sha256 $taskFilterDefs) -ne '470E370CDD776D73564EB66CEDE2C545D084C76AF80D39521CB82E095A866CCF') {
            throw 'CAP03-A test-only definition identity changed; review the exact asset before preparing.'
        }
        Copy-VerifiedFile $taskFilterDefs (Join-Path $taskHarnessRoot 'Defs\Cap03FilterDefs.xml') 'harness-cap03-a-fixture'
    }

    if ($CaseId -eq 'CAP03-C') {
        $taskWrapperDefs = Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\RuntimeHarness\Fixtures\CAP03-C\Defs\Cap03WrapperDefs.xml'
        if ((Get-Sha256 $taskWrapperDefs) -ne 'C6039286A8D9070DB9FC18AD7F7A7CFC4CA5ED9D3E99AB81A9BAB1780D85C4CE') {
            throw 'CAP03-C test-only definition identity changed; review the exact asset before preparing.'
        }
        Copy-VerifiedFile $taskWrapperDefs (Join-Path $taskHarnessRoot 'Defs\Cap03WrapperDefs.xml') 'harness-cap03-c-fixture'
    }

    $taskMods = New-Object 'System.Collections.Generic.List[object]'
    $taskMods.Add([pscustomobject]@{ packageId = 'brrainz.harmony'; rootPath = $taskHarmonyRoot })
    $taskMods.Add([pscustomobject]@{ packageId = 'ludeon.rimworld'; rootPath = Join-Path $taskRuntime 'Data\Core' })
    if ($CommonSense) { $taskMods.Add([pscustomobject]@{ packageId = 'avilmask.commonsense'; rootPath = $taskCsRoot }) }
    if ($CaseId -in @('CAP03-B','CAP03-A','CAP03-C')) {
        $taskMods.Add([pscustomobject]@{ packageId = 'adaptive.storage.framework'; rootPath = $taskAsfRoot })
        $taskMods.Add([pscustomobject]@{ packageId = 'sbz.neatstorage'; rootPath = $taskNeatRoot })
    }
    $taskMods.Add([pscustomobject]@{ packageId = 'giwaffed.haulersdream'; rootPath = $taskHdDestination })
    $taskMods.Add([pscustomobject]@{ packageId = 'giwaffed.haulersdream.runtimeharness'; rootPath = $taskHarnessRoot })
    $taskKnownExpansions = @()
    foreach ($taskDataDir in Get-ChildItem -LiteralPath (Join-Path $taskRuntime 'Data') -Directory) {
        if ($taskDataDir.Name -eq 'Core') { continue }
        [xml]$taskAboutXml = Get-Content -LiteralPath (Join-Path $taskDataDir.FullName 'About\About.xml') -Raw
        $taskKnownExpansions += $taskAboutXml.ModMetaData.packageId.ToLowerInvariant()
    }
    $taskGameVersion = (Get-Content -LiteralPath (Join-Path $taskRuntime 'Version.txt') -Raw).Trim()
    $taskActiveXml = (@($taskMods | ForEach-Object { '    <li>' + $_.packageId + '</li>' }) -join "`n")
    $taskKnownXml = (@($taskKnownExpansions | ForEach-Object { '    <li>' + $_ + '</li>' }) -join "`n")
    $taskModsXml = @"
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData>
  <version>$taskGameVersion</version>
  <activeMods>
$taskActiveXml
  </activeMods>
  <knownExpansions>
$taskKnownXml
  </knownExpansions>
</ModsConfigData>
"@
    Write-NewText (Join-Path $taskConfig 'ModsConfig.xml') $taskModsXml
    Write-NewText (Join-Path $taskConfig 'Prefs.xml') @'
<?xml version="1.0" encoding="utf-8"?>
<PrefsData>
  <devMode>True</devMode>
  <pauseOnLoad>False</pauseOnLoad>
  <runInBackground>True</runInBackground>
  <logVerbose>True</logVerbose>
  <volumeGame>0</volumeGame>
  <volumeMusic>0</volumeMusic>
  <volumeAmbient>0</volumeAmbient>
</PrefsData>
'@
    $taskAssemblyPaths = @(
        (Join-Path $taskRuntime 'RimWorldWin64_Data\Managed\Assembly-CSharp.dll'),
        (Join-Path $taskRuntime 'RimWorldWin64_Data\Managed\UnityEngine.CoreModule.dll'),
        (Join-Path $taskHarmonyRoot 'Current\Assemblies\0Harmony.dll'),
        (Join-Path $taskHarmonyRoot 'Current\Assemblies\HarmonyMod.dll'),
        (Join-Path $taskHdDestination '1.6\Assemblies\HaulersDream.dll'),
        (Join-Path $taskHdDestination '1.6\Assemblies\HaulersDream.Core.dll'),
        (Join-Path $taskHarnessRoot 'Assemblies\HaulersDream.RuntimeHarness.dll')
    )
    if ($CaseId -in @('L40-Q1','L40-UI-P0')) {
        $taskAssemblyPaths += @('UnityEngine.IMGUIModule.dll','UnityEngine.ScreenCaptureModule.dll','UnityEngine.TextRenderingModule.dll') | ForEach-Object {
            Join-Path $taskRuntime ('RimWorldWin64_Data\Managed\' + $_)
        }
    }
    if ($CommonSense) { $taskAssemblyPaths += Join-Path $taskCsRoot '1.6\Assemblies\CommonSense.dll' }
    if ($CaseId -in @('CAP03-B','CAP03-A','CAP03-C')) {
        # The installed framework ships eleven runtime assemblies, including
        # its XML operations and compatibility API; capture every loaded input.
        $taskAssemblyPaths += @(Get-ChildItem -LiteralPath (Join-Path $taskAsfRoot '1.6\Assemblies') -File -Filter '*.dll' |
            Sort-Object Name | ForEach-Object { $_.FullName })
        if (@(Get-ChildItem -LiteralPath $taskNeatRoot -Recurse -File -Filter '*.dll').Count -ne 0) {
            throw 'Neat Storage now contains assemblies; independently review its load folders and runtime identities before capture.'
        }
    }
    $taskConfigFiles = @(Get-ChildItem -LiteralPath $taskConfig -File | ForEach-Object { [pscustomobject]@{ path = $_.FullName; sha256 = Get-Sha256 $_.FullName } })
    $taskManifest = [ordered]@{
        schemaVersion = 1; runId = $taskRunId; caseId = $CaseId; expectedBehavior = $ExpectedBehavior; negativeControl = $NegativeControl
        createdUtc = [DateTime]::UtcNow.ToString('O')
        runDirectory = $script:taskRun; runtimeDirectory = $taskRuntime; saveDataDirectory = $taskSavedata
        evidenceDirectory = $taskEvidence; logPath = $taskLog; gameVersion = $taskGameVersion
        gameSource = $GameRoot; hdSource = $HdSource; hdSourceDirectory = $taskHdRoot
        playerSaveDataRoot = $PlayerSaveDataRoot
        expectedMods = @($taskMods.ToArray()); expectedAssemblies = @($taskAssemblyPaths | ForEach-Object { Get-AssemblyIdentity $_ })
        knownExpansions = $taskKnownExpansions; configFiles = $taskConfigFiles; copiedFiles = @($taskCopiedFiles.ToArray())
        protectedRoots = $taskProtectedRoots; protectedBefore = $taskProtectedBefore
        startupDeadlineSeconds = 300
        semantics = if ($CaseId -eq 'BG03-P1') { 'Partial initially tagged bill ingredients, two floor sources and native cooking/cleaning only; other BG03 controls and original-report convergence remain unfinished.' }
            elseif ($CaseId -in @('BG01','BG02')) { $CaseId + ' ordinary meal: reproduction and requested behavior have separate outcomes.' }
            elseif ($CaseId -eq 'L04-O1') { 'Storage ownership adapter only; no executed hauling or automatic convergence claim.' }
            elseif ($CaseId -eq 'CAP01') { 'Seven physical vanilla shelf scenes; adapter and plan budget outcomes only, no executed hauling.' }
            elseif ($CaseId -eq 'CAP02') { 'Native physical/eligibility projection and zone-index recovery only; no allocation, ASF or executed hauling.' }
            elseif ($CaseId -eq 'CAP03-B') { 'Actual ASF six-slot full-member budget component B only; other CAP03 components, native-call instrumentation and hauling convergence remain unfinished.' }
            elseif ($CaseId -eq 'CAP03-C') { 'Actual incoming/resident native minified wrapper containment and separate inner callback controls only; useful minified support, allocation and hauling require separate evidence.' }
            elseif ($CaseId -eq 'CAP03-A') { 'Actual linked native/ASF fixed-filter dispatch and custom-worker containment only; other CAP03 components, allocation and hauling convergence remain unfinished.' }
            elseif ($CaseId -eq 'L04-B1') { 'Candidate query recurrence and separate healthy delivery only; integrated convergence and cross-scenario evidence remain unfinished.' }
            elseif ($CaseId -eq 'L04-B1-D2') { 'Fresh progressing first-source query window and embedded delivery witness only; final recurrence repair and full B1 require separate evidence.' }
            elseif ($CaseId -eq 'L40-Q1') { 'Native nineteen-scene inventory quantity command capture only; independent full trace/receipt/cleanup review is required. UI, compatibility, network and lifecycle remain unfinished.' }
            elseif ($CaseId -eq 'L40-UI-P0') { 'Native mouse delivery to a test window and capture availability only; independent full trace, PNG decode and visual review are required. Actual product UI remains unfinished.' }
            elseif ($CaseId -eq 'L04-O1-DELIVERY') { 'Actual first automatic bulk delivery and bounded follow-up; original report loops remain separate.' }
            else { 'Bootstrap isolation only; optional intentional-error control is not a clean-runtime pass.' }
    }
    $taskManifestPath = Join-Path $script:taskRun 'manifest.json'
    Write-NewJson $taskManifestPath $taskManifest
    $taskState = [ordered]@{
        schemaVersion = 1; runId = $taskRunId; status = 'prepared'; manifestSha256 = Get-Sha256 $taskManifestPath
        processId = $null; processStartedUtc = $null; launchRequestedUtc = $null
    }
    Write-NewJson (Join-Path $script:taskRun 'controller-state.json') $taskState
    [pscustomobject]@{
        status = 'prepared'; runDirectory = $script:taskRun; manifestPath = $taskManifestPath
        copiedFiles = $taskCopiedFiles.Count; hdSource = $HdSource; commonSense = [bool]$CommonSense; caseId = $CaseId
        expectedBehavior = $ExpectedBehavior; negativeControl = $NegativeControl
        nextAction = 'Review the manifest, then invoke this script with -Action Launch -RunDirectory <this directory>.'
    }
    return
}

$taskPrepared = Read-PreparedRun
$taskManifest = $taskPrepared.manifest
$taskState = $taskPrepared.state
$taskCase = $taskManifest.caseId
$taskExpectedBehavior = if ($taskManifest.PSObject.Properties['expectedBehavior']) { $taskManifest.expectedBehavior } else { 'bootstrap' }
$taskNegativeControl = if ($taskManifest.PSObject.Properties['negativeControl']) { $taskManifest.negativeControl } else { 'None' }
$taskStatePath = Join-Path $script:taskRun 'controller-state.json'
if ($Action -eq 'Launch') {
    if ($taskState.status -ne 'prepared') { throw 'Only a never-launched prepared run can launch. Inspect this run; never restart it on timeout.' }
    $taskOtherGames = @(Get-Process -Name RimWorldWin64 -ErrorAction SilentlyContinue)
    if ($taskOtherGames.Count -gt 0) { throw 'An existing RimWorld process is running. Do not interrupt it or launch another test concurrently.' }
    $taskChanged = @(Compare-ProtectedSnapshot $taskManifest)
    if ($taskChanged.Count -gt 0) { throw ('Protected inputs changed since preparation: ' + ($taskChanged -join '; ')) }
    foreach ($taskFile in @($taskManifest.copiedFiles) + @($taskManifest.configFiles)) {
        Assert-ChildPath $taskFile.path $script:taskRun
        if ((Get-Sha256 $taskFile.path) -ne $taskFile.sha256) { throw "Prepared content changed: $($taskFile.path)" }
    }
    foreach ($taskEvidenceFile in @('Player.log', 'events.jsonl', 'result.json')) {
        if (Test-Path -LiteralPath (Join-Path $taskManifest.evidenceDirectory $taskEvidenceFile)) { throw 'Launch would overwrite previous evidence.' }
    }
    # Empty output directories are not covered by copied-file hashes. Recheck them at the launch boundary.
    foreach ($taskLayoutDirectory in @($script:taskRun, $taskManifest.runtimeDirectory, $taskManifest.saveDataDirectory,
        $taskManifest.evidenceDirectory, (Join-Path $taskManifest.saveDataDirectory 'Config'),
        (Join-Path $taskManifest.saveDataDirectory 'Saves'), (Join-Path $taskManifest.runtimeDirectory 'Mods'))) {
        Assert-NoReparsePath $taskLayoutDirectory
        if (-not (Test-Path -LiteralPath $taskLayoutDirectory -PathType Container)) { throw "Missing prepared layout directory: $taskLayoutDirectory" }
    }
    $taskArguments = @(
        ('"-savedatafolder={0}"' -f $taskManifest.saveDataDirectory),
        '-logFile', ('"{0}"' -f $taskManifest.logPath),
        '-screen-fullscreen', '0', '-screen-width', '1024', '-screen-height', '768',
        '-quicktest', ('-hd-test=' + $taskCase),
        ('"-hd-harnessmanifest={0}"' -f (Join-Path $script:taskRun 'manifest.json'))
    )
    $taskState.status = 'launch-requested'
    $taskState.launchRequestedUtc = [DateTime]::UtcNow.ToString('O')
    Write-RunState $taskStatePath $taskState
    # No blocking wait. Persist identity so another shell can safely poll the same process.
    $taskProcess = Start-Process -FilePath (Join-Path $taskManifest.runtimeDirectory 'RimWorldWin64.exe') `
        -WorkingDirectory $taskManifest.runtimeDirectory -ArgumentList $taskArguments -WindowStyle Hidden -PassThru
    $taskState.processId = $taskProcess.Id
    $taskState.processStartedUtc = $taskProcess.StartTime.ToUniversalTime().ToString('O')
    $taskState.status = 'launched'
    Write-RunState $taskStatePath $taskState
    [pscustomobject]@{
        status = 'launched'; runDirectory = $script:taskRun; processId = $taskProcess.Id
        processStartedUtc = $taskState.processStartedUtc; process = $taskProcess
        statePath = $taskStatePath; evidenceDirectory = $taskManifest.evidenceDirectory
    }
    return
}

$taskProcessState = Get-RunProcess $taskManifest $taskState
$taskResultPath = Join-Path $taskManifest.evidenceDirectory 'result.json'
$taskResult = $null
if (Test-Path -LiteralPath $taskResultPath) {
    try { $taskResult = Get-Content -LiteralPath $taskResultPath -Encoding UTF8 -Raw | ConvertFrom-Json }
    catch { $taskResult = [pscustomobject]@{ status = 'result-incomplete'; detail = $_.Exception.Message } }
}
if ($Action -eq 'Status') {
    [pscustomobject]@{
        controllerStatus = $taskState.status; processStatus = $taskProcessState.status; processId = $taskProcessState.processId
        result = $taskResult; runDirectory = $script:taskRun
        elapsedSeconds = if ($taskState.launchRequestedUtc) { [math]::Round(([DateTime]::UtcNow - ([DateTime]$taskState.launchRequestedUtc).ToUniversalTime()).TotalSeconds, 1) } else { $null }
        timeoutIsTerminal = $false
    }
    return
}

$taskProblems = New-Object 'System.Collections.Generic.List[string]'
if ($taskCase -in @('L40-Q1','L40-UI-P0')) {
    $taskProblems.Add('Quantity capture requires independent complete scene/input/receipt/cleanup evidence review; automatic acceptance is unfinished. UI captures also require full PNG decode and visual review. No feature completion is granted.')
}
if ($null -ne $taskResult) {
    $taskShapeProblems = @(Get-ResultShapeProblems $taskResult $taskCase $taskNegativeControl)
    foreach ($taskShapeProblem in $taskShapeProblems) { $taskProblems.Add('Invalid result evidence: ' + $taskShapeProblem) }
    # Never dereference dependent evidence after its shape was rejected. The original file remains intact.
    if ($taskShapeProblems.Count -gt 0) { $taskResult = $null }
}
$taskControlMarker = '[HD Runtime Harness NegativeControl] run=' + $taskManifest.runId + ' control=' + $taskNegativeControl + ' intentional worker error'
if ($taskProcessState.status -ne 'exited') { $taskProblems.Add('Process is not authoritatively exited: ' + $taskProcessState.status) }
$taskExpectedStatus = if ($taskNegativeControl -ne 'None') { 'failed' } elseif ($taskExpectedBehavior -eq 'baseline-gap') { 'behavior-gap-observed' } else { 'passed' }
if ($null -eq $taskResult -or $taskResult.status -ne $taskExpectedStatus) { $taskProblems.Add('No complete result with the explicitly expected status: ' + $taskExpectedStatus) }
if ($taskResult -and $taskResult.status -ne 'result-incomplete') {
    if ($taskNegativeControl -eq 'None' -and $taskResult.unityErrorsObserved -ne 0) {
        $taskProblems.Add('A non-control run recorded one or more Unity errors.')
    }
    if ($taskResult.runId -ne $taskManifest.runId -or $taskResult.caseId -ne $taskCase -or $taskResult.processId -ne $taskState.processId) {
        $taskProblems.Add('Result identity does not match this run/process.')
    }
    $taskAllowedFailedAssertions = @(if ($taskNegativeControl -ne 'None') { 'no-unity-errors-after-harness-start' }
        elseif ($taskExpectedBehavior -eq 'baseline-gap' -and $taskCase -in @('BG01','BG02')) {
            'behavior-complete-inventory-sweep-before-return'; 'behavior-no-second-gather-or-regather'
        }
        elseif ($taskExpectedBehavior -eq 'baseline-gap' -and $taskCase -eq 'L04-O1') {
            'storage-ownership-behavior-own-inventory'; 'storage-ownership-behavior-own-inventory-with-other-live-claim'
        }
        elseif ($taskExpectedBehavior -eq 'baseline-gap' -and $taskCase -eq 'CAP01') {
            $taskSlotExpected = Get-StorageSlotsExpectations
            foreach($taskId in $taskSlotExpected.Keys) {
                if($taskSlotExpected[$taskId][0] -ne $taskSlotExpected[$taskId][1]) { 'storage-slots-behavior-' + $taskId }
            }
        }
        elseif ($taskExpectedBehavior -eq 'baseline-gap' -and $taskCase -eq 'L04-O1-DELIVERY') {
            'behavior-storage-delivery-first-unload-high'; 'behavior-storage-delivery-settled-without-rehaul'
        })
    if (@($taskResult.assertions | Where-Object { -not $_.passed -and $_.id -notin $taskAllowedFailedAssertions }).Count -gt 0) { $taskProblems.Add('One or more unexpected runtime assertions failed.') }
    foreach ($taskRequired in @('private-runtime-data-path', 'private-save-data-path', 'private-mod-directory', 'private-player-log',
        'harness-compiled-against-running-game', 'exact-active-mod-count', 'real-map-initialized', 'real-game-ticks-advanced', 'no-unity-errors-after-harness-start')) {
        $taskRequiredValue = $taskRequired -notin $taskAllowedFailedAssertions
        if (-not (Test-RequiredAssertion $taskResult.assertions $taskRequired $taskRequiredValue)) { $taskProblems.Add('Missing unique required assertion with expected outcome: ' + $taskRequired) }
    }
    for ($taskIndex = 0; $taskIndex -lt @($taskManifest.expectedMods).Count; $taskIndex++) {
        $taskRequired = 'mod-order-root-' + $taskIndex
        if (-not (Test-RequiredAssertion $taskResult.assertions $taskRequired $true)) { $taskProblems.Add('Missing unique mod assertion: ' + $taskRequired) }
    }
    foreach ($taskExpectedAssembly in $taskManifest.expectedAssemblies) {
        foreach ($taskPrefix in @('single-assembly-', 'assembly-identity-')) {
            $taskRequired = $taskPrefix + $taskExpectedAssembly.name
            if (-not (Test-RequiredAssertion $taskResult.assertions $taskRequired $true)) { $taskProblems.Add('Missing unique assembly assertion: ' + $taskRequired) }
        }
    }
    if ($taskCase -eq 'CAP02') {
        try { Test-ProjectionEvidence $taskResult $taskExpectedBehavior $taskProblems }
        catch { $taskProblems.Add('CAP02 result evidence cannot be verified: '+$_.Exception.Message) }
    }
    if ($taskCase -eq 'CAP03-B') {
        try { Test-AsfBudgetEvidence $taskResult $taskExpectedBehavior $taskProblems }
        catch { $taskProblems.Add('CAP03-B result evidence cannot be verified: '+$_.Exception.Message) }
    }
    if ($taskCase -eq 'BG03-P1') {
        try { Test-PartialBillEvidence $taskResult $taskExpectedBehavior $taskProblems }
        catch { $taskProblems.Add('BG03-P1 result evidence cannot be verified: '+$_.Exception.Message) }
    }
    if ($taskCase -eq 'L04-B1') {
        try { Test-RecurrenceEvidence $taskResult $taskExpectedBehavior $taskProblems }
        catch { $taskProblems.Add('L04-B1 result evidence cannot be verified: '+$_.Exception.Message) }
        $taskProblems.Add('L04-B1 full acceptance requires integrated convergence and cross-scenario evidence; isolated component captures do not establish full B1 acceptance.')
    }
    if ($taskCase -eq 'CAP03-C') {
        try { Test-AsfWrapperEvidence $taskResult $taskExpectedBehavior $taskProblems }
        catch { $taskProblems.Add('CAP03-C result evidence cannot be verified: '+$_.Exception.Message) }
    }
    if ($taskCase -eq 'CAP03-A') {
        try { Test-AsfFilterEvidence $taskResult $taskExpectedBehavior $taskProblems }
        catch { $taskProblems.Add('CAP03-A result evidence cannot be verified: '+$_.Exception.Message) }
    }
    if ($taskCase -eq 'L04-B1-D2') {
        try { Test-InFlightEvidence $taskResult $taskExpectedBehavior $taskProblems }
        catch { $taskProblems.Add('L04-B1-D2 result evidence cannot be verified: '+$_.Exception.Message) }
    }
    if ($taskCase -in @('CAP01','L04-O1-DELIVERY')) {
        try {
            if ($taskCase -eq 'CAP01') { Test-StorageSlotsEvidence $taskResult $taskExpectedBehavior $taskProblems }
            else { Test-StorageDeliveryEvidence $taskResult $taskExpectedBehavior $taskProblems }
        } catch { $taskProblems.Add('Storage result evidence cannot be verified: ' + $_.Exception.Message) }
    }
    if ($taskCase -in @('BG01','BG02')) {
        $taskRecipeCase=$taskCase.ToLowerInvariant()
        $taskIngredientUnits=if($taskCase -eq 'BG02'){20}else{5}
        $taskProductUnits=if($taskCase -eq 'BG02'){4}else{1}
        foreach ($taskRequired in @('fixture-setup-completed', 'execution-native-cooking-completed', 'execution-ordinary-workgiver',
            'fixture-cs-native-driver-prefix', 'fixture-hd-dobill-route-postfix', ('fixture-'+$taskRecipeCase+'-observers-installed'),
            'fixture-cs-patch-settings-assembly', 'fixture-hd-route-mod-assembly',
            'execution-cs-cleaned-during-bill', 'execution-roof-stayed-intact', 'behavior-complete-inventory-sweep-before-return',
            'execution-native-job-cleanup-succeeded', 'execution-exact-native-product-events', 'execution-post-product-counts-stable',
            'execution-ingredients-acquired-once', 'execution-no-ingredient-storage-detour', ('execution-'+$taskRecipeCase+'-observer-healthy'),
            'behavior-no-second-gather-or-regather')) {
            $taskRequiredValue = $taskRequired -notin $taskAllowedFailedAssertions
            if (-not (Test-RequiredAssertion $taskResult.assertions $taskRequired $taskRequiredValue)) { $taskProblems.Add('Missing unique '+$taskCase+' assertion: ' + $taskRequired) }
        }
        if (-not $taskResult.scenario -or -not $taskResult.scenario.fixtureValid -or -not $taskResult.scenario.expectationMatched -or
            $taskResult.scenario.status -ne $taskResult.status -or $taskResult.scenario.status -ne $taskExpectedStatus -or
            $taskResult.scenario.expectedBehavior -ne $taskExpectedBehavior -or
            $taskResult.scenario.requestedBehaviorSatisfied -ne ($taskExpectedBehavior -eq 'satisfied')) {
            $taskProblems.Add($taskCase+' scenario result does not distinguish the expected reproduction from requested behavior.')
        }
        $taskCooking = $taskResult.scenario
        if ($taskCooking.timedOut -or -not $taskCooking.nativeCleanupSucceeded -or -not $taskCooking.noIngredientStorageDetour -or
            -not $taskCooking.productCountsStable -or -not $taskCooking.cleaningDuringNativeBill -or
            -not $taskCooking.completedNativeRecipe -or $taskCooking.nativeDoBillJobs -ne 1 -or
            $taskCooking.productCreationCalls -ne 1 -or $taskCooking.productUnitsCreated -ne $taskProductUnits -or $taskCooking.maxSettledMeals -ne $taskProductUnits -or
            $taskCooking.finalRice -ne 0 -or $taskCooking.finalPotatoes -ne 0 -or $taskCooking.finalMeals -ne $taskProductUnits -or
            $taskCooking.riceAcquired -ne $taskIngredientUnits -or $taskCooking.potatoesAcquired -ne $taskIngredientUnits -or
            $taskCooking.seededCleaningIncrements -ne 3 -or $taskCooking.stablePostProductTicks -lt 300 -or
            $taskCooking.completeInventorySweep -ne ($taskExpectedBehavior -eq 'satisfied') -or
            $taskCooking.noSecondGatherOrRegather -ne ($taskExpectedBehavior -eq 'satisfied')) {
            $taskProblems.Add($taskCase+' recorded quantities, cleaning, cleanup or stable execution do not satisfy the explicit scenario contract.')
        }
        if ($taskCase -eq 'BG02') {
            try { Test-BulkRecipeEvidence $taskResult $taskProblems }
            catch { $taskProblems.Add('BG02 result evidence cannot be verified: '+$_.Exception.Message) }
        }
    }
    if ($taskNegativeControl -ne 'None') {
        $taskControlMarker = '[HD Runtime Harness NegativeControl] run=' + $taskManifest.runId + ' control=' + $taskNegativeControl + ' intentional worker error'
        if (-not $taskResult.negativeControl -or -not $taskResult.negativeControl.expectationMatched -or
            $taskResult.negativeControl.name -ne $taskNegativeControl -or $taskResult.unityErrorsObserved -ne 1 -or
            $taskResult.negativeControl.marker -ne $taskControlMarker -or
            $taskResult.negativeControl.matchingCapturedErrors -ne 1 -or -not $taskResult.negativeControl.workerFinished -or
            $taskResult.negativeControl.callbackThread -ne $taskResult.negativeControl.workerThread -or
            $taskResult.negativeControl.workerThread -le 0 -or
            $taskResult.negativeControl.workerThread -eq $taskResult.negativeControl.mainThread -or
            $taskResult.negativeControl.markerCaptureSequence -le 0 -or
            $taskResult.negativeControl.markerCaptureSequence -gt $taskResult.negativeControl.finalCaptureSequence) {
            $taskProblems.Add('Intentional worker-error control was not observed exactly as configured.')
        }
        if ($taskNegativeControl -eq 'TerminalRace') {
            $taskControl = $taskResult.negativeControl
            if (-not $taskControl.boundaryOrderingMatched -or $taskControl.callbackEntryOrder -le 0 -or
                $taskControl.callbackEntryOrder -ge $taskControl.closeRequestedOrder -or
                $taskControl.closeRequestedOrder -ge $taskControl.capturedOrder -or
                $taskControl.capturedOrder -ge $taskControl.captureClosedOrder) {
                $taskProblems.Add('Terminal race did not witness callback entry, closure request, capture and closure in that order.')
            }
        }
        if (-not (Test-RequiredAssertion $taskResult.assertions 'negative-control-worker-error-observed' $true)) {
            $taskProblems.Add('Missing unique negative-control assertion.')
        }
    }
    if ($taskCase -eq 'L04-O1') {
        $taskStorage = $taskResult.storageOwnership
        if (-not $taskStorage -or -not $taskStorage.fixtureValid -or -not $taskStorage.expectationMatched -or
            $taskStorage.caseId -ne 'L04-O1' -or $taskStorage.expectedBehavior -ne $taskExpectedBehavior -or
            $taskStorage.requestedBehaviorSatisfied -ne ($taskExpectedBehavior -eq 'satisfied') -or
            $taskStorage.startedTick -ne $taskStorage.finishedTick) {
            $taskProblems.Add('Storage adapter result is missing, invalid or does not distinguish the expected baseline gap.')
        }
        $taskStorageIds = @('own-inventory', 'own-hands', 'other-pawn-inventory', 'floor-with-own-in-flight-claim',
            'own-inventory-with-other-live-claim', 'missing-inventory', 'hands-with-missing-inventory', 'null-pawn', 'null-subject', 'both-null')
        if (@($taskStorage.observations).Count -ne $taskStorageIds.Count) { $taskProblems.Add('Unexpected number of storage adapter observations.') }
        foreach ($taskStorageId in $taskStorageIds) {
            $taskRow = @($taskStorage.observations | Where-Object { $_.id -eq $taskStorageId })
            $taskRequired = 'storage-ownership-behavior-' + $taskStorageId
            $taskRequiredValue = $taskRequired -notin $taskAllowedFailedAssertions
            if ($taskRow.Count -ne 1 -or $taskRow[0].correctedMatched -ne $taskRequiredValue -or
                ($taskExpectedBehavior -eq 'baseline-gap' -and -not $taskRow[0].baselineMatched)) {
                $taskProblems.Add('Missing or unexpected storage adapter observation: ' + $taskStorageId)
            }
            if (-not (Test-RequiredAssertion $taskResult.assertions $taskRequired $taskRequiredValue)) {
                $taskProblems.Add('Missing unique storage adapter assertion: ' + $taskRequired)
            }
            if ($taskRow.Count -eq 1) {
                # These answers come from the reviewed fixture contract, not its aggregate success flags.
                $taskCorrectDelivery = $taskStorageId -in @('own-inventory', 'own-hands', 'own-inventory-with-other-live-claim', 'hands-with-missing-inventory')
                $taskCorrectFree = if ($taskStorageId -in @('null-pawn', 'null-subject', 'both-null')) { [int]::MaxValue }
                    elseif ($taskStorageId -in @('other-pawn-inventory', 'floor-with-own-in-flight-claim')) { 0 } else { 10 }
                $taskBaselineGap = $taskStorageId -in @('own-inventory', 'own-inventory-with-other-live-claim')
                $taskBaselineDelivery = $taskCorrectDelivery -and -not $taskBaselineGap
                $taskBaselineFree = if ($taskBaselineGap) { 0 } else { $taskCorrectFree }
                $taskExpectedDelivery = if ($taskExpectedBehavior -eq 'baseline-gap') { $taskBaselineDelivery } else { $taskCorrectDelivery }
                $taskExpectedFree = if ($taskExpectedBehavior -eq 'baseline-gap') { $taskBaselineFree } else { $taskCorrectFree }
                $taskExpectedGate = if ($taskStorageId -in @('null-pawn', 'null-subject', 'both-null')) { $null } else { $taskExpectedFree -gt 0 }
                $taskValue = $taskRow[0]
                if ($taskValue.tick -ne $taskStorage.startedTick -or $taskValue.actualDelivering -ne $taskExpectedDelivery -or
                    $taskValue.actualFreeUnits -ne $taskExpectedFree -or $taskValue.actualStoreCellAllowed -ne $taskExpectedGate -or
                    $taskValue.correctedDelivering -ne $taskCorrectDelivery -or $taskValue.correctedFreeUnits -ne $taskCorrectFree -or
                    $taskValue.baselineDelivering -ne $taskBaselineDelivery -or $taskValue.baselineFreeUnits -ne $taskBaselineFree) {
                    $taskProblems.Add('Storage adapter numeric/custody/gate evidence differs from the reviewed contract: ' + $taskStorageId)
                }
            }
        }
        $taskFixtures = @($taskStorage.assertions | Where-Object { $_.kind -eq 'fixture' })
        if (@($taskStorage.assertions | Where-Object { $_.kind -notin @('fixture','behavior') }).Count -gt 0 -or
            @($taskFixtures | Where-Object { -not $_.passed }).Count -gt 0) {
            $taskProblems.Add('Storage fixture contains unrecognized or failed preconditions.')
        }
        $taskFixtureIds = @('expectation', 'map', 'type-HaulersDream.StorageCommitments', 'method-IsDelivering', 'method-FreeUnitsFor',
            'method-TryCommit', 'method-Commit', 'method-UnitsMovingOf', 'method-ClaimedByOthersFor', 'type-HaulersDream.HaulersDreamMod',
            'settings', 'setting-masterEnabled', 'setting-haulToStack', 'method-GatesVanillaStorage', 'active-storage-gates',
            'cargo-stack-limit', 'scene-bounds', 'scene-has-no-zones', 'own-inventory-holder', 'foreign-claim-live',
            'floor-subject-spawned', 'competing-claim-live', 'null-pawn-no-truncation', 'null-subject-no-truncation', 'both-null-no-truncation',
            'only-fixture-pawns', 'same-tick-adapter-scope')
        foreach ($taskScene in $taskStorageIds[0..6]) {
            foreach ($taskSuffix in @('-single-cell-group', '-physical-capacity', '-unclaimed-capacity', '-complete-measurement')) {
                $taskFixtureIds += $taskScene + $taskSuffix
            }
        }
        foreach ($taskActorIndex in 0..8) { $taskFixtureIds += 'actor-' + $taskActorIndex }
        foreach ($taskFixtureId in $taskFixtureIds) {
            if (-not (Test-RequiredAssertion $taskFixtures $taskFixtureId $true)) {
                $taskProblems.Add('Missing unique required storage fixture assertion: ' + $taskFixtureId)
            }
        }
        $taskFixturePrefixCounts = @{
            'inventory-transfer-'=5; 'inventory-custody-'=5; 'tag-component-'=5; 'hand-transfer-'=2
        }
        foreach ($taskPrefix in $taskFixturePrefixCounts.Keys) {
            if (@($taskFixtures | Where-Object { $_.id.StartsWith($taskPrefix) -and $_.passed }).Count -ne $taskFixturePrefixCounts[$taskPrefix]) {
                $taskProblems.Add('Missing required storage fixture transfer/custody evidence: ' + $taskPrefix)
            }
        }
        if (@($taskFixtures | Where-Object { $_.id -eq 'tag-method' -and $_.passed }).Count -ne 5) { $taskProblems.Add('Missing storage cargo tagging method checks.') }
        foreach ($taskScene in @('own-inventory', 'own-hands', 'other-pawn-inventory', 'floor-with-own-in-flight-claim', 'own-inventory-with-other-live-claim', 'hands-with-missing-inventory')) {
            $taskClaimCount = if ($taskScene -eq 'own-inventory-with-other-live-claim') { 2 } else { 1 }
            foreach ($taskClaimPrefix in @(($taskScene + '-claim-'), ($taskScene + '-cargo-evidence-'))) {
                if (@($taskFixtures | Where-Object { $_.id.StartsWith($taskClaimPrefix) -and $_.passed }).Count -ne $taskClaimCount) {
                    $taskProblems.Add('Missing production claim/evidence precondition: ' + $taskClaimPrefix)
                }
            }
        }
        foreach ($taskFixture in $taskFixtures) {
            $taskCorresponding = @($taskResult.assertions | Where-Object { $_.id -eq ('storage-ownership-fixture-' + $taskFixture.id) -and $_.passed -and $_.observed -eq $taskFixture.observed })
            if ($taskCorresponding.Count -eq 0) { $taskProblems.Add('Storage fixture evidence missing from exported runtime assertions: ' + $taskFixture.id) }
        }
    }
}
foreach ($taskFile in $taskManifest.copiedFiles) {
    try {
        Assert-ChildPath $taskFile.path $script:taskRun
        if ((Get-Sha256 $taskFile.path) -ne $taskFile.sha256) { $taskProblems.Add('Isolated copied content changed: ' + $taskFile.path) }
    } catch { $taskProblems.Add('Cannot verify isolated copied content: ' + $taskFile.path + '; ' + $_.Exception.Message) }
}
$taskProtectedChanges = @(Compare-ProtectedSnapshot $taskManifest)
foreach ($taskChange in $taskProtectedChanges) { $taskProblems.Add('Protected source changed: ' + $taskChange) }
$taskEventsPath = Join-Path $taskManifest.evidenceDirectory 'events.jsonl'
if (-not (Test-Path -LiteralPath $taskEventsPath)) { $taskProblems.Add('Missing events.jsonl.') }
else {
    try {
        $taskEvents = @(Get-Content -LiteralPath $taskEventsPath -Encoding UTF8 | ForEach-Object { $_ | ConvertFrom-Json })
        $taskObservedPhase = if ($taskCase -ne 'bootstrap') { 'scenario-observed' } else { 'bootstrap-observed' }
        $taskRequiredPhases = @('harness-start', 'map-initialized', $taskObservedPhase, 'error-capture-boundary', 'terminal-result')
        if ($taskNegativeControl -ne 'None') { $taskRequiredPhases += 'negative-control-observed' }
        if ($taskNegativeControl -in @('TerminalRace', 'LateWorkerError')) { $taskRequiredPhases += 'assertion-revised' }
        if ($taskNegativeControl -eq 'TerminalRace') { $taskRequiredPhases += 'terminal-race-witness' }
        if ($taskCase -in @('BG01','BG02')) { $taskRequiredPhases += @('fixture-ready', 'native-product-created', 'native-job-cleanup') }
        if ($taskCase -eq 'BG03-P1') { $taskRequiredPhases += @('fixture-ready','bg03-p1-result') }
        if ($taskCase -eq 'L04-B1') { $taskRequiredPhases += @('l04-b1-result') }
        if ($taskCase -eq 'L04-B1-D2') { $taskRequiredPhases += @('l04-b1-d2-result') }
        if ($taskCase -eq 'L04-O1') { $taskRequiredPhases += @('storage-ownership-fixture', 'storage-ownership-bind', 'storage-ownership-result') }
        if ($taskCase -eq 'CAP01') { $taskRequiredPhases += @('storage-slots-isolation', 'storage-slots-bind', 'storage-slots-result') }
        if ($taskCase -eq 'CAP02') { $taskRequiredPhases += @('storage-projection-bind','storage-projection-actor-setup','storage-projection-fixture-setup','storage-projection-result','storage-projection-fixture-mutation','storage-projection-fixture-repair') }
        if ($taskCase -eq 'CAP03-B') { $taskRequiredPhases += @('cap03-b-bindings','cap03-b-physical-setup','cap03-b-result') }
        if ($taskCase -eq 'CAP03-C') { $taskRequiredPhases += @('cap03-c-definition','cap03-c-bindings','cap03-c-catalog','cap03-c-result') }
        if ($taskCase -eq 'CAP03-A') { $taskRequiredPhases += @('cap03-a-bindings','cap03-a-catalog','cap03-a-result') }
        if ($taskCase -eq 'L40-Q1') { $taskRequiredPhases += @('quantity-bindings','quantity-rules-original','quantity-rules-restored','quantity-result') }
        if ($taskCase -eq 'L40-UI-P0') { $taskRequiredPhases += @('quantity-ui-bridge-start','quantity-ui-capture-requested','quantity-ui-bridge-result') }
        if ($taskCase -eq 'L04-O1-DELIVERY') { $taskRequiredPhases += @('storage-delivery-fixture', 'storage-delivery-result') }
        foreach ($taskPhase in $taskRequiredPhases) {
            if (@($taskEvents | Where-Object { $_.phase -eq $taskPhase -and $_.runId -eq $taskManifest.runId }).Count -ne 1) { $taskProblems.Add('Missing unique event: ' + $taskPhase) }
        }
        if ($taskCase -eq 'CAP01') {
            foreach ($taskRequirement in @(@('storage-slots-physical',7),@('storage-slots-measurement',15),@('storage-slots-observation',30),@('storage-slots-live-claim',3))) {
                if (@($taskEvents | Where-Object { $_.phase -eq $taskRequirement[0] -and $_.runId -eq $taskManifest.runId }).Count -ne $taskRequirement[1]) {
                    $taskProblems.Add('CAP01 lacks the required number of actual events: ' + $taskRequirement[0])
                }
            }
        }
        if ($taskCase -eq 'L04-O1-DELIVERY') {
            foreach ($taskPhase in @('storage-delivery-observer-bind','storage-delivery-candidate','storage-delivery-current-job','storage-delivery-free-query',
                'storage-delivery-cell-gate','storage-delivery-bulk-pickup','storage-delivery-physical-deposit','storage-delivery-job-cleanup','storage-delivery-settled-state')) {
                if (@($taskEvents | Where-Object { $_.phase -eq $taskPhase -and $_.runId -eq $taskManifest.runId }).Count -eq 0) {
                    $taskProblems.Add('Storage delivery lacks actual execution event: ' + $taskPhase)
                }
            }
        }
        if ($taskResult -and $taskCase -in @('CAP01','L04-O1-DELIVERY')) {
            try { Test-StorageEventEvidence $taskResult $taskEvents $taskCase $taskExpectedBehavior $taskManifest.runId $taskProblems }
            catch { $taskProblems.Add('Storage event evidence cannot be verified: ' + $_.Exception.Message) }
        }
        if ($taskResult -and $taskCase -eq 'BG02') {
            try { Test-BulkRecipeEvents $taskResult $taskEvents $taskManifest.runId $taskProblems }
            catch { $taskProblems.Add('BG02 event evidence cannot be verified: '+$_.Exception.Message) }
        }
        if ($taskResult -and $taskCase -eq 'CAP02') {
            try { Test-ProjectionEvents $taskResult $taskEvents $taskManifest.runId $taskProblems }
            catch { $taskProblems.Add('CAP02 event evidence cannot be verified: '+$_.Exception.Message) }
        }
        if ($taskResult -and $taskCase -eq 'BG03-P1') {
            try { Test-PartialBillEvents $taskResult $taskEvents $taskManifest.runId $taskProblems }
            catch { $taskProblems.Add('BG03-P1 event evidence cannot be verified: '+$_.Exception.Message) }
        }
        if ($taskResult -and $taskCase -eq 'CAP03-B') {
            try { Test-AsfBudgetEvents $taskResult $taskEvents $taskManifest.runId $taskProblems }
            catch { $taskProblems.Add('CAP03-B event evidence cannot be verified: '+$_.Exception.Message) }
        }
        if ($taskResult -and $taskCase -eq 'CAP03-A') {
            try { Test-AsfFilterEvents $taskResult $taskEvents $taskManifest.runId $taskProblems }
            catch { $taskProblems.Add('CAP03-A event evidence cannot be verified: '+$_.Exception.Message) }
        }
        if ($taskResult -and $taskCase -eq 'CAP03-C') {
            try { Test-AsfWrapperEvents $taskResult $taskEvents $taskManifest.runId $taskProblems }
            catch { $taskProblems.Add('CAP03-C event evidence cannot be verified: '+$_.Exception.Message) }
        }
        if ($taskResult -and $taskCase -eq 'L04-B1') {
            try { Test-RecurrenceEvents $taskResult $taskEvents $taskManifest.runId $taskProblems }
            catch { $taskProblems.Add('L04-B1 event evidence cannot be verified: '+$_.Exception.Message) }
        }
        if ($taskResult -and $taskCase -eq 'L04-B1-D2') {
            try { Test-InFlightEvents $taskResult $taskEvents $taskManifest.runId $taskProblems }
            catch { $taskProblems.Add('L04-B1-D2 event evidence cannot be verified: '+$_.Exception.Message) }
        }
        if ($taskNegativeControl -ne 'None') {
            $taskCapturedErrors = @($taskEvents | Where-Object { $_.phase -eq 'unity-error' -and $_.runId -eq $taskManifest.runId } | ForEach-Object { $_.detail | ConvertFrom-Json })
            $taskMatchingErrors = @($taskCapturedErrors | Where-Object { $_.message -eq $taskControlMarker })
            if ($taskCapturedErrors.Count -ne 1 -or $taskMatchingErrors.Count -ne 1 -or
                $taskMatchingErrors[0].callbackThread -ne $taskResult.negativeControl.callbackThread -or
                $taskMatchingErrors[0].captureSequence -ne $taskResult.negativeControl.markerCaptureSequence) {
                $taskProblems.Add('The unique captured Unity error does not carry the configured sentinel and matching callback/sequence evidence.')
            }
        }
    } catch { $taskProblems.Add('Events are not fully readable JSON lines: ' + $_.Exception.Message) }
}
$taskLogReview = @()
if (-not (Test-Path -LiteralPath $taskManifest.logPath)) { $taskProblems.Add('Missing redirected Player.log.') }
else {
    if ((Get-Item -LiteralPath $taskManifest.logPath).Length -eq 0) { $taskProblems.Add('Redirected Player.log is empty.') }
    $taskRequiredLogMarkers = @(
        ('Save data folder overridden to ' + $taskManifest.saveDataDirectory),
        ('[HD Runtime Harness] run=' + $taskManifest.runId + ' case=' + $taskManifest.caseId + ' startup'),
        ('[HD Runtime Harness] run=' + $taskManifest.runId + ' case=' + $taskManifest.caseId + ' terminal=')
    )
    foreach ($taskMarker in $taskRequiredLogMarkers) {
        if (@(Select-String -LiteralPath $taskManifest.logPath -Encoding UTF8 -SimpleMatch -Pattern $taskMarker).Count -ne 1) {
            $taskProblems.Add('Missing unique run-specific Player.log marker: ' + $taskMarker)
        }
    }
    if ($taskNegativeControl -ne 'None') {
        $taskControlMarker = '[HD Runtime Harness NegativeControl] run=' + $taskManifest.runId + ' control=' + $taskNegativeControl + ' intentional worker error'
        if (@(Select-String -LiteralPath $taskManifest.logPath -Encoding UTF8 -SimpleMatch -Pattern $taskControlMarker).Count -ne 1) {
            $taskProblems.Add('Missing unique intentional-error marker in Player.log.')
        }
    }
    # A review aid, not a claim that every RimWorld error has one standardized prefix.
    $taskLogReview = @(Select-String -LiteralPath $taskManifest.logPath -Encoding UTF8 -Pattern 'Exception|Could not load|Error while|Error in |XML error|patch class .*could not|\[Error' | Select-Object LineNumber, Line)
    if ($taskLogReview.Count -gt 0) { $taskProblems.Add('Player.log contains candidates requiring independent review; bootstrap is not automatically verified.') }
}
[pscustomobject]@{
    status = if ($taskProblems.Count -gt 0) { 'not-verified' } elseif ($taskNegativeControl -ne 'None') { 'negative-control-verified' }
        elseif ($taskExpectedBehavior -eq 'baseline-gap') { 'baseline-gap-verified' } elseif ($taskCase -in @('BG01','BG02','BG03-P1')) { $taskCase+'-behavior-verified' }
        elseif ($taskCase -in @('L04-O1','CAP01')) { 'storage-adapter-verified' }
        elseif ($taskCase -eq 'CAP02') { 'storage-native-projection-verified' }
        elseif ($taskCase -eq 'CAP03-B') { 'storage-asf-budget-component-verified' }
        elseif ($taskCase -eq 'CAP03-A') { 'storage-asf-filter-component-verified' }
        elseif ($taskCase -eq 'L04-B1-D2') { 'inflight-query-component-verified' }
        elseif ($taskCase -eq 'CAP03-C') { 'asf-wrapper-component-verified' }
        elseif ($taskCase -in @('L40-Q1','L40-UI-P0')) { 'not-verified' }
        elseif ($taskCase -eq 'L04-O1-DELIVERY') { 'storage-first-delivery-verified' } else { 'bootstrap-verified' }
    runDirectory = $script:taskRun; processStatus = $taskProcessState.status
    problems = $taskProblems.ToArray(); protectedChanges = $taskProtectedChanges; logReviewCandidates = $taskLogReview
    result = $taskResult
    scope = if ($taskCase -eq 'BG03-P1') { 'Only initially tagged milk plus two remaining floor ingredients, native bill/cleaning and bounded follow-up are measured. Other BG03 controls and original-report convergence remain unverified.' }
        elseif ($taskCase -in @('BG01','BG02')) { 'Only the '+$taskCase+' ordinary-meal scenario is measured. An observed baseline gap is not feedback completion.' }
        elseif ($taskCase -eq 'L04-O1') { 'Only actual storage custody and capacity adapters are measured. No hauling driver or automatic convergence is verified.' }
        elseif ($taskCase -eq 'CAP01') { 'Only seven vanilla shelf adapter/budget scenes are measured. No executed hauling, providers or original reported loops are verified.' }
        elseif ($taskCase -eq 'CAP02') { 'Only native physical/eligibility projection and zone-index recovery are measured. No allocation, ASF or original reported hauling outcome is verified.' }
        elseif ($taskCase -eq 'CAP03-B') { 'Only actual ASF six-slot full-member budget component B is measured. Other CAP03 components, independent native-call counts, allocation and original reported hauling outcomes remain unverified.' }
        elseif ($taskCase -eq 'CAP03-C') { 'Verification covers only incoming/resident native minified wrapper containment and inner callback controls. Useful minified support, allocation, save lifecycle and actual hauling require separate evidence.' }
        elseif ($taskCase -eq 'CAP03-A') { 'Only linked native/ASF fixed-filter dispatch and custom-worker containment are measured. Other CAP03 components, independent native-call counts, allocation and original reported hauling outcomes remain unverified.' }
        elseif ($taskCase -eq 'L04-B1') { 'Only query controls and separate healthy delivery are measured. Integrated convergence and cross-scenario evidence remain unfinished; this partial capture cannot establish full B1 or a product recurrence repair.' }
        elseif ($taskCase -eq 'L04-B1-D2') { 'Verification covers only the fresh progressing first-source query window and embedded delivery witness against the explicit expected behavior. It does not establish unperturbed D1, full B1, recurrence repair or original-report convergence.' }
        elseif ($taskCase -eq 'L40-Q1') { 'Only native inventory quantity command-body capture is attempted. Independent nineteen-scene evidence/cleanup review is unfinished; actual UI, CE/Sidearms, Multiplayer and lifecycle remain separate.' }
        elseif ($taskCase -eq 'L40-UI-P0') { 'Only native queued mouse delivery to a test-owned window and PNG availability are attempted. Independent input-route review, full PNG decode and visual review are unfinished; actual product UI remains separate.' }
        elseif ($taskCase -eq 'L04-O1-DELIVERY') { 'Only first automatic bulk delivery and bounded follow-up are measured. Original reported loops and other storage paths remain unverified.' }
        else { 'Bootstrap and optional error-capture controls only. No feedback issue is considered tested or resolved; post-capture shutdown errors require whole-log review.' }
}
