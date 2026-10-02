# CAP02 evidence consumer. No filesystem or game mutation; compatible with Windows PowerShell 5.1.
# Public entry points receive the global run result, whose CAP02 DTO is storageProjection.
function Test-ProjectionFields($Value, [hashtable]$Schema, [string]$Label, $Problems) {
    if ($null -eq $Value -or $Value -isnot [pscustomobject]) { $Problems.Add($Label+' must be a JSON object.'); return $false }
    $taskOkay=$true
    foreach($taskName in $Schema.Keys) {
        $taskProperty=$Value.PSObject.Properties[$taskName]
        if($null -eq $taskProperty) { $Problems.Add($Label+' lacks '+$taskName+'.'); $taskOkay=$false; continue }
        $taskItem=$taskProperty.Value; $taskType=$Schema[$taskName]
        if($taskType.StartsWith('nullable-')) { if($null -eq $taskItem){continue}; $taskType=$taskType.Substring(9) }
        $taskValid=switch($taskType) {
            'string' { $taskItem -is [string] }
            'object' { $null -ne $taskItem -and $taskItem -is [pscustomobject] }
            'array' { $taskItem -is [array] }
            'boolean' { $taskItem -is [bool] }
            'long' { $taskItem -is [int] -or $taskItem -is [long] }
            'int' { ($taskItem -is [int] -or $taskItem -is [long]) -and $taskItem -ge [int]::MinValue -and $taskItem -le [int]::MaxValue }
            'number' { ($taskItem -is [int] -or $taskItem -is [long] -or $taskItem -is [single] -or $taskItem -is [double] -or $taskItem -is [decimal]) -and -not [double]::IsNaN([double]$taskItem) -and -not [double]::IsInfinity([double]$taskItem) }
            'timestamp' { $taskDate=[datetimeoffset]::MinValue; ($taskItem -is [datetime] -and $taskItem.Kind -eq [DateTimeKind]::Utc) -or ($taskItem -is [datetimeoffset] -and $taskItem.Offset -eq [timespan]::Zero) -or ($taskItem -is [string] -and $taskItem -match '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$' -and [datetimeoffset]::TryParse($taskItem,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::None,[ref]$taskDate)) }
            default { $false }
        }
        if(-not $taskValid) { $Problems.Add($Label+'.'+$taskName+' has an invalid '+$Schema[$taskName]+' value.'); $taskOkay=$false }
    }
    return $taskOkay
}
function Read-ProjectionCell([string]$Text, $Problems) {
    $taskX=0; $taskZ=0
    if($Text -notmatch '^\((-?[0-9]+), 0, (-?[0-9]+)\)$' -or -not [int]::TryParse($Matches[1],[ref]$taskX) -or -not [int]::TryParse($Matches[2],[ref]$taskZ)) {
        $Problems.Add('CAP02 has an invalid native cell.'); return $null
    }
    return @{x=$taskX;z=$taskZ}
}
function Test-ProjectionStatusShape($Value,$Problems) {
    Test-ProjectionFields $Value @{usable='boolean';observation='string';capability='string';reason='string'} 'CAP02 status' $Problems | Out-Null
}
function Test-ProjectionThingShape($Value,$Problems) {
    if(Test-ProjectionFields $Value @{thingId='int';def='string';count='int';stackLimit='int';cell='string';spawned='boolean';mapId='nullable-int';faction='nullable-string';holderType='nullable-string';heldByFixtureMap='boolean';destroyed='boolean';stuff='nullable-string';hitPoints='int'} 'CAP02 physical Thing' $Problems) { Read-ProjectionCell $Value.cell $Problems | Out-Null }
}
function Test-ProjectionFilterShape($Value,$Problems) {
    if(-not (Test-ProjectionFields $Value @{type='string';onlySpecial='boolean';allowedDefs='array';disallowedSpecials='array';hitPointsMin='number';hitPointsMax='number';mentalBreakMin='number';mentalBreakMax='number';qualities='string'} 'CAP02 filter' $Problems)){return}
    foreach($taskName in $Value.allowedDefs) { if($taskName -isnot [string]){$Problems.Add('CAP02 allowed filter definition is not a string.')} }
    foreach($taskName in $Value.disallowedSpecials) { if($null -ne $taskName -and $taskName -isnot [string]){$Problems.Add('CAP02 special filter definition is not nullable text.')} }
}
function Test-ProjectionPhysicalShape($Value,$Problems) {
    if(-not (Test-ProjectionFields $Value @{scene='string';session='string';mapId='int';tick='int';cell='string';parentKey='string';parentId='int';parentType='string';parentThing='nullable-object';maximumSlots='int';items='array';silver='object';cloth='object';registered='boolean';zoneRegistered='boolean';zoneCells='nullable-array';priority='string';settingsOwnerIsParent='boolean';settingsIdentityUnchanged='boolean';fixedIdentityUnchanged='boolean';filter='object';fixedFilter='object'} 'CAP02 physical scene' $Problems)){return}
    Read-ProjectionCell $Value.cell $Problems | Out-Null
    foreach($taskThing in @($Value.items)+@($Value.silver,$Value.cloth)) { Test-ProjectionThingShape $taskThing $Problems }
    if($null -ne $Value.parentThing){Test-ProjectionThingShape $Value.parentThing $Problems}
    if($null -ne $Value.zoneCells){foreach($taskCell in $Value.zoneCells){if($taskCell -isnot [string]){$Problems.Add('CAP02 zone cell is not text.')}else{Read-ProjectionCell $taskCell $Problems | Out-Null}}}
    Test-ProjectionFilterShape $Value.filter $Problems; Test-ProjectionFilterShape $Value.fixedFilter $Problems
}
function Test-ProjectionWorkShape($Rows,$Problems) {
    foreach($taskRow in $Rows){Test-ProjectionFields $taskRow @{kind='string';value='long'} 'CAP02 reported work' $Problems | Out-Null}
}
function Test-ProjectionCellShape($Value,$Problems) {
    if(-not (Test-ProjectionFields $Value @{fixtureScene='string';fixtureStage='string';observationId='long';query='string';session='string';mapId='int';tick='int';generation='long';cell='string';parentKey='nullable-string';groupKey='string';provider='nullable-string';vacantKey='nullable-string';maximumSlots='nullable-int';itemCount='nullable-int';vacantSlots='nullable-long';gridEntries='nullable-int';status='object';stacks='array';work='array'} 'CAP02 cell projection' $Problems)){return}
    Read-ProjectionCell $Value.cell $Problems | Out-Null; Test-ProjectionStatusShape $Value.status $Problems; Test-ProjectionWorkShape $Value.work $Problems
    foreach($taskStack in $Value.stacks){Test-ProjectionFields $taskStack @{key='string';thingId='int';def='string';count='int';stackLimit='int';deficit='long';providerTargetValid='nullable-boolean'} 'CAP02 stack resource' $Problems | Out-Null}
}
function Test-ProjectionEligibilityShape($Value,$Problems) {
    if(-not (Test-ProjectionFields $Value @{fixtureScene='string';fixtureStage='string';observationId='long';parcelId='string';status='object';state='string';vacantEligible='boolean';unitsPerNewStack='nullable-int';predicates='array';topUps='array';work='array'} 'CAP02 eligibility' $Problems)){return}
    Test-ProjectionStatusShape $Value.status $Problems; Test-ProjectionWorkShape $Value.work $Problems
    foreach($taskRow in $Value.predicates){Test-ProjectionFields $taskRow @{name='string';state='string';reason='string';targetId='nullable-int'} 'CAP02 predicate' $Problems | Out-Null}
    foreach($taskRow in $Value.topUps){Test-ProjectionFields $taskRow @{key='string';targetId='int';units='long'} 'CAP02 top-up edge' $Problems | Out-Null}
}
function Test-ProjectionShape($Value,$Problems) {
    $taskBefore=$Problems.Count
    if(-not (Test-ProjectionFields $Value @{storageProjection='object';assemblies='array';executingGameVersion='string';runId='string';caseId='string';status='string';detail='string';unityErrorsObserved='int';assertions='array';negativeControl='nullable-object'} 'CAP02 run result' $Problems)){return $false}
    foreach($taskA in $Value.assemblies){Test-ProjectionFields $taskA @{name='string';path='string';sha256='string';assemblyVersion='string';moduleVersionId='string'} 'CAP02 loaded assembly' $Problems | Out-Null}
    foreach($taskA in $Value.assertions){Test-ProjectionFields $taskA @{id='string';passed='boolean';observed='string'} 'CAP02 global assertion' $Problems | Out-Null}
    $taskP=$Value.storageProjection
    if(-not (Test-ProjectionFields $taskP @{caseId='string';expectedBehavior='string';contract='string';scope='string';status='string';sessionId='string';nativeIdentity='nullable-string';error='nullable-string';fixtureValid='boolean';requestedBehaviorSatisfied='boolean';expectationMatched='boolean';startedTick='int';finishedTick='int';mainThread='int';assemblies='array';bindings='array';patchInventory='array';assertions='array';operations='array';cells='array';eligibility='array';initialActor='object';finalActor='object';initialPhysical='array';finalPhysical='array'} 'CAP02 scenario' $Problems)){return $false}
    foreach($taskA in $taskP.assemblies){Test-ProjectionFields $taskA @{name='string';path='string';mvid='string';sha256='string'} 'CAP02 assembly' $Problems | Out-Null}
    foreach($taskText in @($taskP.bindings)+@($taskP.patchInventory)){if($taskText -isnot [string]){$Problems.Add('CAP02 binding/patch inventory is not text.')}}
    foreach($taskA in $taskP.assertions){Test-ProjectionFields $taskA @{sequence='int';id='string';kind='string';passed='boolean';detail='string'} 'CAP02 assertion' $Problems | Out-Null}
    foreach($taskO in $taskP.operations){if(Test-ProjectionFields $taskO @{scene='string';stage='string';status='object';detail='nullable-string'} 'CAP02 operation' $Problems){Test-ProjectionStatusShape $taskO.status $Problems}}
    Test-ProjectionThingShape $taskP.initialActor $Problems; Test-ProjectionThingShape $taskP.finalActor $Problems
    foreach($taskPhysical in @($taskP.initialPhysical)+@($taskP.finalPhysical)){Test-ProjectionPhysicalShape $taskPhysical $Problems}
    foreach($taskCell in $taskP.cells){Test-ProjectionCellShape $taskCell $Problems}
    foreach($taskE in $taskP.eligibility){Test-ProjectionEligibilityShape $taskE $Problems}
    return $Problems.Count -eq $taskBefore
}
function Get-ProjectionCanonical($Value) {
    if($null -eq $Value){return 'null'}
    if($Value -is [pscustomobject]) {
        $taskParts=@(foreach($taskName in @($Value.PSObject.Properties.Name | Sort-Object -CaseSensitive)){(ConvertTo-Json -InputObject $taskName -Compress)+':'+(Get-ProjectionCanonical $Value.PSObject.Properties[$taskName].Value)})
        return '{'+($taskParts -join ',')+'}'
    }
    if($Value -is [array]){return '['+(@(foreach($taskItem in $Value){Get-ProjectionCanonical $taskItem}) -join ',')+']'}
    return ConvertTo-Json -InputObject $Value -Compress -Depth 100
}
function Test-ProjectionSame($Left,$Right){return (Get-ProjectionCanonical $Left) -ceq (Get-ProjectionCanonical $Right)}
function Test-ProjectionComplete($Status){return $Status.usable -and $Status.observation -ceq 'Complete' -and $Status.capability -ceq 'Supported' -and $Status.reason -ceq 'None'}
function Test-ProjectionChanged($Status){return -not $Status.usable -and $Status.observation -ceq 'Invalidated' -and $Status.capability -ceq 'Supported' -and $Status.reason -ceq 'GroupChanged'}
function Get-ProjectionScenes { return @('occupied-two-full-one-vacant','empty-three-vacant','full-seven-unit-silver-top-up','full-incompatible','one-cell-stockpile') }
function Test-ProjectionReportedWork($Rows,$Problems) {
    $taskKinds=@('Members','Coordinates','GridEntries','NativeGridVisits','NativeCalls','Filters','Compatibility','ProviderVisits','CellLimitCalls','Reservations','Reachability','GuardChecks','Preparation','OutputRecords')
    # StorageProjectionBridge opens every scope with StorageProjectionLimits.Page.
    $taskCeilings=@(32L,200L,4096L,4096L,200L,800L,4096L,4096L,8192L,200L,200L,8192L,0L,4096L)
    if($Rows.Count -ne $taskKinds.Count){$Problems.Add('CAP02 reported work catalog is incomplete.')}
    for($taskI=0;$taskI-lt$taskKinds.Count;$taskI++){
        $taskKind=$taskKinds[$taskI];$taskMatches=@($Rows|Where-Object kind -CEQ $taskKind)
        if($taskMatches.Count-ne1 -or $taskMatches[0].value-lt0 -or $taskMatches[0].value-gt$taskCeilings[$taskI]){$Problems.Add('CAP02 reported work repeats, omits, negates or exceeds its Page limit for '+$taskKind+'.')}
    }
}
function Get-ProjectionOne($Rows,[string]$Field,[string]$Value,$Problems) {
    $taskRows=@($Rows|Where-Object { $_.PSObject.Properties[$Field].Value -ceq $Value })
    if($taskRows.Count-ne1){$Problems.Add('CAP02 requires exactly one '+$Field+'='+$Value+'.');return $null}
    return $taskRows[0]
}
function Get-ProjectionAssertionCatalog($P,$Problems) {
    # Ordered Require/Check calls from StorageProjectionScenario, native v1.
    # Repeated checks are intentional. Spawn/floor IDs come from the typed
    # independent physical setup, never from the assertion rows being checked.
    $taskCatalog=New-Object 'System.Collections.Generic.List[string]'
    $taskAdd={param([string]$Id,[string]$Kind='behavior');$taskCatalog.Add($Kind+':'+$Id)}
    $taskCell={param($Scene)
        & $taskAdd ($Scene.scene+'-physical-resource-counts')
        & $taskAdd ($Scene.scene+'-physical-resource-identities')
        for($taskResource=0;$taskResource-le$Scene.items.Count;$taskResource++){& $taskAdd ($Scene.scene+'-cross-scene-resource-identity')}
        & $taskAdd ($Scene.scene+'-physical-provenance')
    }
    $taskEligibility={param($Scene,[string]$Def)
        foreach($taskSuffix in @('eligibility-identity-','eligibility-','top-up-','actual-native-predicate-')){& $taskAdd ($Scene.scene+'-'+$taskSuffix+$Def)}
        if($Scene.scene-ceq'full-seven-unit-silver-top-up' -and $Def-ceq'Silver'){& $taskAdd ($Scene.scene+'-actual-directional-predicate')}
    }
    $taskPrepare={param($Scene,[string]$Stage);& $taskAdd ($Scene.scene+'-'+$Stage);& $taskAdd ($Scene.scene+'-'+$Stage+'-work')}
    $taskProbe={param($Scene,[string]$Stage)
        & $taskAdd ($Scene.scene+'-'+$Stage+'-open');& $taskAdd ($Scene.scene+'-'+$Stage+'-physical-complete');& $taskCell $Scene
        foreach($taskDef in $(if($Stage-ceq'cloth-then-silver'){@('Cloth','Silver')}else{@('Silver','Cloth')})){
            & $taskEligibility $Scene $taskDef
            & $taskAdd ($Scene.scene+'-'+$Stage+'-recheck-'+$taskDef)
            & $taskEligibility $Scene $taskDef
            & $taskAdd ($Scene.scene+'-'+$Stage+'-fresh-equal-'+$taskDef)
        }
        & $taskCell $Scene;& $taskAdd ($Scene.scene+'-'+$Stage+'-resource-not-consumed')
    }
    foreach($taskId in @('expectation','map','native-provider-only','native-filter-state-binding','fixture-bounds','no-zones-overwritten','normal-actor','only-fixture-actor')){& $taskAdd $taskId 'fixture'}
    foreach($taskDef in @('Steel','WoodLog','Silver','Cloth','Uranium')){& $taskAdd ('stack-limit-'+$taskDef) 'fixture'}
    $taskScenes=New-Object 'System.Collections.Generic.List[object]'
    foreach($taskName in Get-ProjectionScenes){
        $taskScene=Get-ProjectionOne $P.initialPhysical scene $taskName $Problems;if($null-eq$taskScene){return}
        $taskScenes.Add($taskScene)
        if($taskName-cne'one-cell-stockpile'){& $taskAdd ($taskName+'-actual-native-shelf') 'fixture'}
        & $taskAdd ($taskName+'-registered') 'fixture';& $taskAdd ($taskName+'-actual-slot-limit') 'fixture'
        $taskDefOrder=@(switch($taskName){'occupied-two-full-one-vacant'{'Steel';'WoodLog'};'full-seven-unit-silver-top-up'{'Steel';'WoodLog';'Silver'};'full-incompatible'{'Steel';'WoodLog';'Uranium'}})
        foreach($taskDef in $taskDefOrder){
            $taskThing=Get-ProjectionOne $taskScene.items def $taskDef $Problems;if($null-eq$taskThing){return}
            & $taskAdd ('spawn-'+$taskThing.def+$taskThing.thingId) 'fixture'
        }
        foreach($taskThing in @($taskScene.silver,$taskScene.cloth)){& $taskAdd ('spawn-'+$taskThing.def+$taskThing.thingId) 'fixture'}
        foreach($taskThing in @($taskScene.silver,$taskScene.cloth)){& $taskAdd ($taskName+'-floor-'+$taskThing.def+$taskThing.thingId) 'fixture'}
        & $taskAdd ($taskName+'-physical-item-count') 'fixture'
        if($taskName-ceq'full-seven-unit-silver-top-up'){& $taskAdd ($taskName+'-actual-directional-controls') 'fixture'}
    }
    & $taskAdd 'catalog-created';& $taskAdd 'native-semantics-reviewed'
    foreach($taskScene in $taskScenes){
        if($taskScene.scene-cne'one-cell-stockpile'){
            & $taskPrepare $taskScene 'prepare'; & $taskProbe $taskScene 'silver-then-cloth'; & $taskProbe $taskScene 'cloth-then-silver'
            & $taskAdd ($taskScene.scene+'-reverse-order-invariant');continue
        }
        & $taskPrepare $taskScene 'prepare-valid'; & $taskAdd ($taskScene.scene+'-valid-before-mutation-open')
        & $taskAdd ($taskScene.scene+'-initial-valid'); & $taskCell $taskScene; & $taskEligibility $taskScene 'Silver'
        & $taskAdd ($taskScene.scene+'-initial-recheck'); & $taskEligibility $taskScene 'Silver'
        & $taskAdd ($taskScene.scene+'-stale-control-established') 'fixture'
        & $taskAdd ($taskScene.scene+'-retained-index-invalidated'); & $taskAdd ($taskScene.scene+'-prior-observation-invalidated')
        & $taskPrepare $taskScene 'prepare-stale-empty-list'; & $taskAdd ($taskScene.scene+'-fresh-stale-index-open')
        & $taskAdd ($taskScene.scene+'-grid-is-not-membership-proof')
        & $taskAdd ($taskScene.scene+'-repair-precondition') 'fixture'; & $taskAdd ($taskScene.scene+'-repaired-native-membership') 'fixture'
        & $taskPrepare $taskScene 'prepare-repaired'; & $taskProbe $taskScene 'silver-then-cloth'; & $taskAdd ($taskScene.scene+'-recovery-complete')
    }
    foreach($taskScene in $taskScenes){& $taskAdd ($taskScene.scene+'-physical-unchanged') 'fixture'}
    & $taskAdd 'same-tick' 'fixture'; & $taskAdd 'actor-unchanged' 'fixture'; & $taskAdd 'all-scopes-disposed'
    return $taskCatalog.ToArray()
}
function Test-ProjectionAssertionCatalog($P,$Problems) {
    $taskBefore=$Problems.Count;$taskExpected=@(Get-ProjectionAssertionCatalog $P $Problems)
    if($Problems.Count-ne$taskBefore){return}
    if($taskExpected.Count-ne427 -or @($taskExpected|Select-Object -Unique).Count-ne224){$Problems.Add('CAP02 source-derived assertion catalog cannot be established from this physical setup.');return}
    if($P.assertions.Count-ne$taskExpected.Count){$Problems.Add('CAP02 must retain all 427 ordered fixture/behavior assertions, including repeated IDs.');return}
    for($taskI=0;$taskI-lt$taskExpected.Count;$taskI++){
        $taskA=$P.assertions[$taskI]
        if(($taskA.kind+':'+$taskA.id)-cne$taskExpected[$taskI]){$Problems.Add('CAP02 assertion ID/kind/order differs at '+($taskI+1)+': expected '+$taskExpected[$taskI]+'.')}
    }
}
function Test-ProjectionPatchInventory($P,$Problems) {
    # Reviewed immutable CE0508 module: tokens were independently resolved from
    # its actual MethodInfo and tested with 24 isolated KnownPatch controls.
    # A new module needs reviewed token tuples, even if its token numbers match.
    # This reads exported identities only; it does not load/execute a product DLL.
    $taskHd=@($P.assemblies|Where-Object{$_.name.StartsWith('HaulersDream,')})
    if($taskHd.Count-ne1){$Problems.Add('CAP02 patch inventory lacks a unique HD module.');return}
    $taskMvid='e52f9ce7-fd03-4a99-895a-858063863298'
    if($taskHd[0].mvid-cne$taskMvid -or $taskHd[0].sha256-ine'CE0508AF4058C0414DDC9F83142F9708B66EC43A44CD1BD74E1E1553CB37F604'){
        $Problems.Add('CAP02 HD patch tokens have not been independently reviewed for this product module.');return
    }
    $taskExpected=@(
        ('RimWorld.StoreUtility.IsGoodStoreCell <- giwaffed.HaulersDream:HaulersDream.Patch_IsGoodStoreCell_HonourCommitments.Postfix;kind=Postfix;mvid='+$taskMvid+';token=100665072'),
        ('RimWorld.StoreUtility.IsGoodStoreCell <- giwaffed.HaulersDream:HaulersDream.HDLog.UniversalExceptionFinalizer;kind=Finalizer;mvid='+$taskMvid+';token=100664009')
    )
    if($P.patchInventory.Count-ne2){$Problems.Add('CAP02 requires exactly the two reviewed native HD patch registrations.')}
    foreach($taskPatch in $taskExpected){if(@($P.patchInventory|Where-Object{$_-ceq$taskPatch}).Count-ne1){$Problems.Add('CAP02 lacks a unique exact reviewed patch owner/target/method/kind/module/token: '+$taskPatch)}}
}
function Test-ProjectionOperationWork($Row,$Scene,[bool]$Eligibility,$Problems) {
    # Work belongs to THIS operation's returned DTO, not cumulative scope.Used.
    # These are deterministic precharges, not measured native iterations/time.
    # Scope.Used and exact live comp counts are not exported by CAP02.
    $taskBefore=$Problems.Count;Test-ProjectionReportedWork $Row.work $Problems;if($Problems.Count-ne$taskBefore){return}
    $taskActual=@{};foreach($taskW in $Row.work){$taskActual[$taskW.kind]=[long]$taskW.value}
    $taskExpected=@{};foreach($taskKind in $taskActual.Keys){$taskExpected[$taskKind]=0L}
    $taskMinCompatibility=0L;$taskZone=$Scene.scene-ceq'one-cell-stockpile';$taskItems=[long]$Scene.items.Count
    if(-not$Eligibility){
        $taskExpected.Members=1L;$taskExpected.Coordinates=1L;$taskExpected.GuardChecks=1L
        if($Row.fixtureStage-cnotin@('retained-index-stale','fresh-stale-index')){
            if($null-eq$Row.gridEntries -or $Row.gridEntries-lt0){$Problems.Add('CAP02 cell work lacks a nonnegative actual grid census.');return}
            $taskGrid=[long]$Row.gridEntries
            $taskExpected.GridEntries=2L*$taskGrid;$taskExpected.CellLimitCalls=2L;$taskExpected.GuardChecks=2L;$taskExpected.OutputRecords=3L*$taskItems+2L
            # ResolveMember charges the actual building AllComps.Count. The DTO
            # omits that count; only its page ceiling and repeated-row agreement
            # can be checked here. Zones have no comp charge.
            if(-not$taskZone){$taskExpected.Remove('Compatibility')}
        }
    }else{
        # The synchronous cleared scene consists of the recorded item Things and
        # its one shelf parent (zones are not grid Things). SceneRows also binds
        # each usable cell census to this physical membership.
        $taskGrid=$taskItems+$(if($taskZone){0L}else{1L})
        $taskExpected.GridEntries=2L*$taskGrid;$taskExpected.NativeGridVisits=4L*$taskGrid;$taskExpected.NativeCalls=1L
        $taskExpected.CellLimitCalls=$taskGrid+3L;$taskExpected.Reservations=1L;$taskExpected.Reachability=1L
        $taskExpected.GuardChecks=3L;$taskExpected.OutputRecords=10L+5L*$taskItems
        # Unlinked native eligibility checks effective and twice the same fixed
        # filter, including duplicate special-list entries, before native call.
        $taskContext=@($Row.predicates|Where-Object{$_.name-ceq'hd-explicit-context-filter'-and$_.state-ceq'Eligible'}).Count
        $taskExpected.Filters=3L+$Scene.filter.disallowedSpecials.Count+2L*$Scene.fixedFilter.disallowedSpecials.Count+$taskContext
        # Native precharge N plus preflight N plus >=1 per storable item,
        # then >=3 per executed directional edge. Additional comp visits remain
        # bounded by Page.Compatibility, without inventing an observed comp list.
        $taskDirectional=@($Row.predicates|Where-Object name -CEQ 'directional-CanStackWith').Count
        $taskMinCompatibility=2L*$taskGrid+$taskItems+3L*$taskDirectional
        if($taskGrid-gt0){$taskExpected.Remove('Compatibility')}
    }
    foreach($taskKind in $taskExpected.Keys){if($taskActual[$taskKind]-ne$taskExpected[$taskKind]){$Problems.Add('CAP02 '+$Row.fixtureScene+'/'+$Row.fixtureStage+' '+$taskKind+' charge contradicts its executed operation; expected '+$taskExpected[$taskKind]+'.')}}
    if($taskActual.Compatibility-lt$taskMinCompatibility -or $taskActual.Compatibility-gt4096 -or $taskActual.GridEntries+$taskActual.NativeGridVisits-gt4096){$Problems.Add('CAP02 operation undercharges required compatibility work or exceeds its actual Page work ceiling.')}
}
function Test-ProjectionPreparationWork($Value,$Problems) {
    # Setup allowance = 8*D*C*S+1000000; PrepareMember precharges 4*D*C*S
    # +3*zoneCells +3*warmedFootprint +actual parent comps +game.Maps.Count.
    # The def counts and comp list are not separately exported. Reconcile their
    # declared common base and the observed ResolveMember comp charge; do not
    # claim independent enumeration or cumulative scope-budget instrumentation.
    $taskP=$Value.storageProjection;$taskMapRows=@($Value.assertions|Where-Object id -CEQ 'real-map-initialized');$taskMaps=0
    if($taskMapRows.Count-ne1 -or $taskMapRows[0].observed-notmatch'^size=\([0-9]+, 1, [0-9]+\); maps=([1-9][0-9]*)$' -or -not[int]::TryParse($Matches[1],[ref]$taskMaps)){$Problems.Add('CAP02 preparation work lacks the actual initialized map count.');return}
    $taskAllowanceSeen=$null
    foreach($taskO in @($taskP.operations|Where-Object{$_.stage-like'prepare*'})){
        if($null-eq$taskO.detail -or $taskO.detail-notmatch'^allowance=([0-9]+); charged=([0-9]+); ready=True; indexed=([0-9]+); explicitFootprintWarmup=(True|False)$'){continue}
        $taskRawAllowance=$Matches[1];$taskRawCharged=$Matches[2];$taskRawIndexed=$Matches[3];$taskWarm=$Matches[4]
        $taskAllowance=0L;$taskCharged=0L;$taskIndexed=0
        if(-not[long]::TryParse($taskRawAllowance,[ref]$taskAllowance) -or -not[long]::TryParse($taskRawCharged,[ref]$taskCharged) -or -not[int]::TryParse($taskRawIndexed,[ref]$taskIndexed)){continue}
        if($taskAllowance-le1000000 -or ($taskAllowance-1000000)%8-ne0){$Problems.Add('CAP02 preparation allowance cannot represent the declared complete filter cross-product.');continue}
        if($null-ne$taskAllowanceSeen -and $taskAllowance-ne$taskAllowanceSeen){$Problems.Add('CAP02 preparation changed the synchronous native definition-count allowance.')};$taskAllowanceSeen=$taskAllowance
        $taskComp=0L
        if($taskO.scene-cne'one-cell-stockpile'){
            $taskCells=@($taskP.cells|Where-Object{$_.fixtureScene-ceq$taskO.scene-and$_.status.usable})
            if($taskCells.Count-ne4){$Problems.Add('CAP02 shelf preparation lacks all complete member observations.');continue}
            $taskCompRows=@($taskCells[0].work|Where-Object kind -CEQ 'Compatibility')
            if($taskCompRows.Count-ne1){continue};$taskComp=[long]$taskCompRows[0].value
        }
        $taskExpected=([decimal]$taskAllowance-1000000)/2+3L*$taskIndexed+$taskComp+$taskMaps
        if($taskWarm-ceq'True'){$taskExpected+=3L} # independently checked 1x1 ShelfSmall
        if([decimal]$taskCharged-ne$taskExpected){$Problems.Add('CAP02 '+$taskO.scene+'/'+$taskO.stage+' preparation charge disagrees with its allowance, membership, footprint, comp charge and map count.')}
    }
}
function Test-ProjectionEvidence($Value,[string]$ExpectedBehavior,$Problems) {
    if(-not (Test-ProjectionShape $Value $Problems)){return}
    $taskP=$Value.storageProjection; $taskScenes=Get-ProjectionScenes
    if($ExpectedBehavior-cne'satisfied' -or $taskP.expectedBehavior-cne'satisfied' -or $taskP.caseId-cne'CAP02' -or $taskP.contract-cne'native-physical-projector-v1' -or $taskP.status-cne'passed' -or -not $taskP.fixtureValid -or -not $taskP.requestedBehaviorSatisfied -or -not $taskP.expectationMatched -or $null-ne$taskP.error -or $taskP.startedTick-lt0 -or $taskP.finishedTick-ne$taskP.startedTick -or $taskP.mainThread-le0 -or $taskP.sessionId-notmatch'^[0-9a-f]{32}$' -or $taskP.nativeIdentity-notmatch'^1\.6\.4871 rev591;'){$Problems.Add('CAP02 result does not describe a completed satisfied same-tick native fixture.')}
    if($taskP.initialPhysical.Count-ne5 -or $taskP.finalPhysical.Count-ne5 -or $taskP.cells.Count-ne21 -or $taskP.eligibility.Count-ne38 -or $taskP.operations.Count-ne38){$Problems.Add('CAP02 physical/control catalog has missing or extra rows.')}
    $taskSequence=0
    foreach($taskA in $taskP.assertions){$taskSequence++;if($taskA.sequence-ne$taskSequence -or -not $taskA.passed -or $taskA.kind-notin@('fixture','behavior')){$Problems.Add('CAP02 assertion sequence or outcome contradicts success.')}}
    Test-ProjectionAssertionCatalog $taskP $Problems
    foreach($taskId in @('expectation','map','native-provider-only','native-filter-state-binding','only-fixture-actor','catalog-created','native-semantics-reviewed','same-tick','actor-unchanged','all-scopes-disposed')){
        $taskA=@($taskP.assertions|Where-Object id -CEQ $taskId);if($taskA.Count-ne1 -or -not $taskA[0].passed){$Problems.Add('CAP02 lacks its unique required assertion '+$taskId+'.')}
    }
    if(-not(Test-ProjectionSame $taskP.initialActor $taskP.finalActor) -or -not $taskP.initialActor.spawned -or $taskP.initialActor.destroyed -or -not $taskP.initialActor.heldByFixtureMap -or $taskP.initialActor.holderType-cne'Verse.Map' -or $taskP.initialActor.thingId-le0 -or [string]::IsNullOrWhiteSpace($taskP.initialActor.faction)){$Problems.Add('CAP02 actor identity/custody changed or is not an actual spawned player actor.')}
    $taskAllIds=@{}; $taskParents=@{}; $taskLocations=@{}
    foreach($taskScene in $taskScenes){
        $taskS=Get-ProjectionOne $taskP.initialPhysical scene $taskScene $Problems; $taskF=Get-ProjectionOne $taskP.finalPhysical scene $taskScene $Problems
        if($null-eq$taskS -or $null-eq$taskF){continue}
        $taskZone=$taskScene-ceq'one-cell-stockpile'; $taskCount=switch($taskScene){'occupied-two-full-one-vacant'{2};'empty-three-vacant'{0};'one-cell-stockpile'{0};default{3}}
        $taskSlots=if($taskZone){1}else{3}; $taskParentPrefix=if($taskZone){'zone:'}else{'building:'}
        if(-not(Test-ProjectionSame $taskS $taskF) -or $taskS.session-cne$taskP.sessionId -or $taskS.tick-ne$taskP.startedTick -or $taskS.mapId-ne$taskP.initialActor.mapId -or $taskS.maximumSlots-ne$taskSlots -or $taskS.items.Count-ne$taskCount -or $taskS.parentId-lt0 -or $taskS.parentKey-cne($taskParentPrefix+$taskS.parentId) -or $taskS.parentType-cne$(if($taskZone){'RimWorld.Zone_Stockpile'}else{'RimWorld.Building_Storage'}) -or -not $taskS.registered -or -not $taskS.zoneRegistered -or $taskS.priority-cne'Critical' -or -not $taskS.settingsOwnerIsParent -or -not $taskS.settingsIdentityUnchanged -or -not $taskS.fixedIdentityUnchanged){$Problems.Add('CAP02 physical setup/final state contradicts scene '+$taskScene+'.')}
        if($taskParents.ContainsKey($taskS.parentKey) -or $taskLocations.ContainsKey($taskS.cell)){$Problems.Add('CAP02 aliases distinct scene parent/cell identities.')};$taskParents[$taskS.parentKey]=$true;$taskLocations[$taskS.cell]=$true
        if($taskZone){if($null-ne$taskS.parentThing -or $null-eq$taskS.zoneCells -or $taskS.zoneCells.Count-ne1 -or $taskS.zoneCells[0]-cne$taskS.cell){$Problems.Add('CAP02 stockpile is not the exact one-cell native parent.')}}
        else{if($null-eq$taskS.parentThing -or $null-ne$taskS.zoneCells){$Problems.Add('CAP02 shelf parent snapshot is missing or has zone custody.')}else{if($taskS.parentThing.thingId-ne$taskS.parentId -or $taskS.parentThing.def-cne'ShelfSmall' -or $taskS.parentThing.stuff-cne'WoodLog' -or $taskS.parentThing.cell-cne$taskS.cell -or $taskS.parentThing.faction-cne$taskP.initialActor.faction){$Problems.Add('CAP02 shelf identity/stuff/player faction differs from the actual parent.')}}}
        foreach($taskFilter in @($taskS.filter,$taskS.fixedFilter)){
            if($taskFilter.type-cne'Verse.ThingFilter' -or $taskFilter.onlySpecial -or $taskFilter.hitPointsMin-lt0 -or $taskFilter.hitPointsMax-gt1 -or $taskFilter.hitPointsMin-gt$taskFilter.hitPointsMax -or $taskFilter.mentalBreakMin-lt0 -or $taskFilter.mentalBreakMax-gt1 -or $taskFilter.mentalBreakMin-gt$taskFilter.mentalBreakMax){$Problems.Add('CAP02 filter values are invalid for the native fixture.')}
            foreach($taskDef in @('Steel','WoodLog','Silver','Cloth','Uranium')){if($taskFilter.allowedDefs-cnotcontains$taskDef){$Problems.Add('CAP02 actual fixture filter does not include '+$taskDef+'.')}}
        }
        $taskExpectedDefs=if($taskCount-eq0){@()}elseif($taskCount-eq2){@('Steel','WoodLog')}elseif($taskScene-ceq'full-seven-unit-silver-top-up'){@('Steel','WoodLog','Silver')}else{@('Steel','WoodLog','Uranium')}
        foreach($taskDef in $taskExpectedDefs){$taskItem=Get-ProjectionOne $taskS.items def $taskDef $Problems;if($null-ne$taskItem){$taskExpected=$taskItem.stackLimit-$(if($taskDef-ceq'Silver'){7}else{0});if($taskItem.count-ne$taskExpected -or $taskItem.stackLimit-le7 -or $taskItem.cell-cne$taskS.cell){$Problems.Add('CAP02 physical occupant count or position differs from the scene.')}}}
        foreach($taskPair in @(@('Silver',$taskS.silver),@('Cloth',$taskS.cloth))){if($taskPair[1].def-cne$taskPair[0] -or $taskPair[1].count-ne7 -or $taskPair[1].stackLimit-le7 -or $taskPair[1].cell-ceq$taskS.cell){$Problems.Add('CAP02 incoming parcel is not an independent seven-unit floor stack.')}}
        foreach($taskThing in @($taskS.items)+@($taskS.silver,$taskS.cloth)+$(if($null-ne$taskS.parentThing){@($taskS.parentThing)}else{@()})){
            if($taskThing.thingId-le0 -or $taskAllIds.ContainsKey($taskThing.thingId) -or -not $taskThing.spawned -or $taskThing.destroyed -or $taskThing.mapId-ne$taskS.mapId -or -not $taskThing.heldByFixtureMap -or $taskThing.holderType-cne'Verse.Map'){$Problems.Add('CAP02 reuses or loses a physical Thing identity/map/custody.')};$taskAllIds[$taskThing.thingId]=$true
        }
        Test-ProjectionSceneRows $taskP $taskS $Problems
    }
    if($taskAllIds.ContainsKey($taskP.initialActor.thingId)){$Problems.Add('CAP02 actor aliases a resource Thing.')}
    Test-ProjectionOperations $taskP $Problems
    Test-ProjectionPatchInventory $taskP $Problems
    Test-ProjectionPreparationWork $Value $Problems
    if($taskP.assemblies.Count-ne5){$Problems.Add('CAP02 must bind exactly five actual assemblies.')}
    foreach($taskName in @('HaulersDream','HaulersDream.Core','Assembly-CSharp','0Harmony','HaulersDream.RuntimeHarness')){
        $taskMatches=@($taskP.assemblies|Where-Object{($_.name.Split(',')[0])-ceq$taskName})
        if($taskMatches.Count-ne1){$Problems.Add('CAP02 assembly identity is missing or repeated: '+$taskName);continue}
        $taskA=$taskMatches[0];$taskGuid=[guid]::Empty
        if($taskA.sha256-notmatch'^[A-Fa-f0-9]{64}$' -or -not[guid]::TryParse($taskA.mvid,[ref]$taskGuid) -or [string]::IsNullOrWhiteSpace($taskA.path)){$Problems.Add('CAP02 assembly lacks typed hash/module/file identity.')}
        $taskLoaded=@($Value.assemblies|Where-Object name -CEQ $taskName)
        if($taskLoaded.Count-ne1){$Problems.Add('CAP02 bound assembly lacks one corresponding loaded assembly.')}else{
            $taskL=$taskLoaded[0]
            if($taskA.path-ine$taskL.path -or $taskA.sha256-ine$taskL.sha256 -or $taskA.mvid-ine$taskL.moduleVersionId -or -not $taskA.name.StartsWith($taskName+', Version='+$taskL.assemblyVersion+',')){$Problems.Add('CAP02 bound assembly does not match the controller loaded-file identity.')}
        }
        if($taskName-ceq'Assembly-CSharp' -and $taskP.nativeIdentity-cne($Value.executingGameVersion+';'+$taskA.name+';mvid='+$taskA.mvid)){$Problems.Add('CAP02 catalog native identity differs from the actual executing game assembly.')}
    }
    if($taskP.bindings.Count-ne10 -or @($taskP.bindings|Select-Object -Unique).Count-ne10){$Problems.Add('CAP02 exact constructor/method binding catalog is incomplete or repeated.')}
    $taskHd=@($taskP.assemblies|Where-Object{$_.name.StartsWith('HaulersDream,')})
    foreach($taskMethod in @('StorageProjectionEnvironment.ctor','StorageProjectionRequest.ctor','StorageParcelProbe.ctor','StorageProviderCatalog.Create','StorageProviderCatalog.PrepareMember','StorageResourceProjector.Open','StorageProjectionScope.ObserveCell','StorageProjectionScope.ObserveEligibility','StorageProjectionScope.Recheck','StorageProjectionScope.Dispose')){
        $taskB=@($taskP.bindings|Where-Object{$_.StartsWith('HaulersDream.'+$taskMethod+';')});$taskToken=0
        if($taskB.Count-ne1 -or $taskB[0]-notmatch'; token=([0-9]+); mvid=([0-9a-f-]{36})$'){$Problems.Add('CAP02 lacks its exact bound method '+$taskMethod+'.');continue}
        $taskModule=$Matches[2];if(-not[int]::TryParse($Matches[1],[ref]$taskToken) -or $taskToken-le0 -or $taskHd.Count-ne1 -or $taskModule-cne$taskHd[0].mvid){$Problems.Add('CAP02 bound method token/module does not identify the selected HD module.')}
    }
}
function Test-ProjectionSceneRows($P,$Scene,$Problems) {
    $taskName=$Scene.scene; $taskZone=$taskName-ceq'one-cell-stockpile'; $taskAt=Read-ProjectionCell $Scene.cell $Problems
    if($null-eq$taskAt){return}
    $taskPrefix=$P.sessionId+'/'+$Scene.mapId+'/'+$Scene.parentKey+'/'+$taskAt.x+','+$taskAt.z
    $taskCellStages=if($taskZone){@('valid-before-mutation','retained-index-stale','fresh-stale-index','silver-then-cloth','silver-then-cloth-after-probes')}else{@('silver-then-cloth','silver-then-cloth-after-probes','cloth-then-silver','cloth-then-silver-after-probes')}
    $taskCells=@($P.cells|Where-Object fixtureScene -CEQ $taskName)
    if($taskCells.Count-ne$taskCellStages.Count){$Problems.Add('CAP02 scene cell catalog differs: '+$taskName)}
    foreach($taskStage in $taskCellStages){
        $taskC=Get-ProjectionOne $taskCells fixtureStage $taskStage $Problems; if($null-eq$taskC){continue}
        Test-ProjectionOperationWork $taskC $Scene $false $Problems
        $taskBase=$taskStage -replace '-after-probes$','';if($taskStage-ceq'retained-index-stale'){$taskBase='valid-before-mutation'}
        if($taskC.session-cne$P.sessionId -or $taskC.mapId-ne$Scene.mapId -or $taskC.tick-ne$P.startedTick -or $taskC.generation-ne1 -or $taskC.cell-cne$Scene.cell -or $taskC.groupKey-cne'concrete' -or $taskC.query-cne('CAP02/'+$taskName+'/'+$taskBase)){$Problems.Add('CAP02 cell query/session/map/parent provenance disagrees with its actual request.')}
        if($taskStage-in@('retained-index-stale','fresh-stale-index')){
            if(-not(Test-ProjectionChanged $taskC.status) -or $taskC.observationId-ne0 -or $taskC.stacks.Count-ne0 -or $null-ne$taskC.maximumSlots -or $null-ne$taskC.itemCount -or $null-ne$taskC.vacantSlots -or $null-ne$taskC.vacantKey){$Problems.Add('CAP02 stale membership exposes a usable resource or lacks GroupChanged.')};continue
        }
        $taskExpectedObservation=if($taskStage-like'*-after-probes'){2}else{1}
        if(-not(Test-ProjectionComplete $taskC.status) -or $taskC.observationId-ne$taskExpectedObservation -or $taskC.parentKey-cne$Scene.parentKey -or $taskC.provider-cne'RimWorld physical slots' -or $taskC.vacantKey-cne($taskPrefix+'/vacant') -or $taskC.maximumSlots-ne$Scene.maximumSlots -or $taskC.itemCount-ne$Scene.items.Count -or $taskC.vacantSlots-ne($Scene.maximumSlots-$Scene.items.Count) -or $taskC.stacks.Count-ne$Scene.items.Count -or $taskC.gridEntries-ne($Scene.items.Count+$(if($taskZone){0}else{1}))){$Problems.Add('CAP02 usable cell resources differ from the independently captured physical scene.')}
        foreach($taskItem in $Scene.items){$taskStack=@($taskC.stacks|Where-Object thingId -EQ $taskItem.thingId);if($taskStack.Count-ne1){$Problems.Add('CAP02 stack resource omits/repeats a physical identity.');continue};$taskStack=$taskStack[0]
            if($taskStack.key-cne($taskPrefix+'/stack:'+$taskItem.thingId) -or $taskStack.def-cne$taskItem.def -or $taskStack.count-ne$taskItem.count -or $taskStack.stackLimit-ne$taskItem.stackLimit -or $taskStack.deficit-ne[Math]::Max([long]$taskItem.stackLimit-$taskItem.count,0) -or $null-ne$taskStack.providerTargetValid){$Problems.Add('CAP02 stack key/count/deficit or native provider data is inconsistent.')}
        }
    }
    $taskCompleteCells=@($taskCells|Where-Object{$_.fixtureStage-cnotin@('retained-index-stale','fresh-stale-index')})
    if($taskCompleteCells.Count-gt1){foreach($taskCellRow in $taskCompleteCells){if(-not(Test-ProjectionSame $taskCellRow.work $taskCompleteCells[0].work)){$Problems.Add('CAP02 unchanged member observation charges differ across independent scopes/rechecks.')}}}
    $taskStages=if($taskZone){@('valid','valid-fresh','silver-then-cloth','silver-then-cloth-fresh')}else{@('silver-then-cloth','silver-then-cloth-fresh','cloth-then-silver','cloth-then-silver-fresh')}
    $taskRows=@($P.eligibility|Where-Object fixtureScene -CEQ $taskName)
    if($taskRows.Count-ne$(if($taskZone){6}else{8})){$Problems.Add('CAP02 scene eligibility catalog differs: '+$taskName)}
    foreach($taskStage in $taskStages){
        $taskSubjects=@(if($taskStage-in@('valid','valid-fresh')){$Scene.silver}else{$Scene.silver;$Scene.cloth})
        $taskStageRows=@($taskRows|Where-Object fixtureStage -CEQ $taskStage)
        if($taskStageRows.Count-ne$taskSubjects.Count){$Problems.Add('CAP02 eligibility stage has extra/missing parcel evaluations.')}
        foreach($taskSubject in $taskSubjects){
            $taskParcel=$taskName+'/'+$taskSubject.def+$taskSubject.thingId
            $taskE=Get-ProjectionOne $taskStageRows parcelId $taskParcel $Problems;if($null-eq$taskE){continue}
            Test-ProjectionOperationWork $taskE $Scene $true $Problems
            $taskVacant=$Scene.items.Count-lt$Scene.maximumSlots; $taskTopUp=$taskName-ceq'full-seven-unit-silver-top-up' -and $taskSubject.def-ceq'Silver'; $taskEligible=$taskVacant-or$taskTopUp
            if($taskE.observationId-ne1 -or -not(Test-ProjectionComplete $taskE.status) -or $taskE.state-cne$(if($taskEligible){'Eligible'}else{'Refused'}) -or $taskE.vacantEligible-ne$taskVacant -or $taskE.unitsPerNewStack-ne$(if($taskVacant){$taskSubject.stackLimit}else{$null}) -or $taskE.topUps.Count-ne$(if($taskTopUp){1}else{0})){$Problems.Add('CAP02 initial/fresh eligibility quantity, state or observation identity is inconsistent.')}
            $taskTarget=$null
            if($taskTopUp){$taskTarget=Get-ProjectionOne $Scene.items def 'Silver' $Problems;if($null-ne$taskTarget -and $taskE.topUps.Count-eq1){$taskEdge=$taskE.topUps[0];if($taskEdge.targetId-ne$taskTarget.thingId -or $taskEdge.units-ne7 -or $taskEdge.key-cne($taskPrefix+'/stack:'+$taskTarget.thingId)){$Problems.Add('CAP02 top-up edge is not bound to the real seven-unit Silver deficit.')}}}
            $taskNames=@('destination-enabled','destination-faction','selected-priority','effective-thing-filter','concrete-fixed-filter','asf-declared-fixed-filter','asf-actual-member-capacity','native-IsGoodStoreCell','hd-explicit-context-filter')
            if($taskTopUp){$taskNames+=@('different-target','target-ever-storable','directional-CanStackWith')}
            if($taskE.predicates.Count-ne$taskNames.Count){$Problems.Add('CAP02 predicate catalog omits or adds an operation.')}
            foreach($taskPredicate in $taskNames){
                $taskR=Get-ProjectionOne $taskE.predicates name $taskPredicate $Problems;if($null-eq$taskR){continue}
                $taskState='Eligible';$taskReason='None';$taskId=$null
                if($taskPredicate-like'asf-*' -or ($taskZone-and$taskPredicate-ceq'destination-faction') -or (-not$taskEligible-and$taskPredicate-ceq'hd-explicit-context-filter')){$taskState='NotEvaluated'}
                elseif(-not$taskEligible-and$taskPredicate-ceq'native-IsGoodStoreCell'){$taskState='Refused';$taskReason='NativeCellRefused'}
                if($taskPredicate-in@('different-target','target-ever-storable','directional-CanStackWith') -and $null-ne$taskTarget){$taskId=$taskTarget.thingId}
                if($taskR.state-cne$taskState -or $taskR.reason-cne$taskReason -or $taskR.targetId-ne$taskId){$Problems.Add('CAP02 native/fixed/context/directional predicate outcome or target identity is inconsistent.')}
            }
        }
    }
}
function Get-ProjectionOperationCatalog {
    $taskRows=@([pscustomobject]@{scene='catalog';stage='create';changed=$false})
    foreach($taskScene in Get-ProjectionScenes){
        if($taskScene-ceq'one-cell-stockpile'){$taskStages=@('prepare-valid','valid-before-mutation-open','stale-recheck','prepare-stale-empty-list','fresh-stale-index-open','prepare-repaired','silver-then-cloth-open','silver-then-cloth-recheck-Silver','silver-then-cloth-recheck-Cloth')}
        else{$taskStages=@('prepare','silver-then-cloth-open','silver-then-cloth-recheck-Silver','silver-then-cloth-recheck-Cloth','cloth-then-silver-open','cloth-then-silver-recheck-Cloth','cloth-then-silver-recheck-Silver')}
        foreach($taskStage in $taskStages){$taskRows+= [pscustomobject]@{scene=$taskScene;stage=$taskStage;changed=($taskStage-ceq'stale-recheck')}}
    }
    return $taskRows
}
function Test-ProjectionOperations($P,$Problems) {
    $taskCatalog=@(Get-ProjectionOperationCatalog)
    if($P.operations.Count-ne$taskCatalog.Count){$Problems.Add('CAP02 operation catalog is incomplete.');return}
    for($taskI=0;$taskI-lt$taskCatalog.Count;$taskI++){
        $taskActual=$P.operations[$taskI];$taskExpected=$taskCatalog[$taskI]
        if($taskActual.scene-cne$taskExpected.scene -or $taskActual.stage-cne$taskExpected.stage -or -not$(if($taskExpected.changed){Test-ProjectionChanged $taskActual.status}else{Test-ProjectionComplete $taskActual.status})){$Problems.Add('CAP02 operation order/status differs at '+$taskI+'.')}
        if($taskExpected.stage-like'prepare*'){
            if($taskActual.detail-notmatch'^allowance=([0-9]+); charged=([0-9]+); ready=True; indexed=([0-9]+); explicitFootprintWarmup=(True|False)$'){$Problems.Add('CAP02 preparation lacks exact finite ready/index/warmup evidence.');continue}
            $taskAllowance=0L;$taskCharged=0L;$taskIndexed=0;$taskWarm=$Matches[4];$taskRawAllowance=$Matches[1];$taskRawCharged=$Matches[2];$taskRawIndexed=$Matches[3]
            if(-not[long]::TryParse($taskRawAllowance,[ref]$taskAllowance) -or -not[long]::TryParse($taskRawCharged,[ref]$taskCharged) -or -not[int]::TryParse($taskRawIndexed,[ref]$taskIndexed) -or $taskCharged-le0 -or $taskCharged-gt$taskAllowance -or $taskIndexed-ne$(if($taskExpected.stage-in@('prepare-valid','prepare-repaired')){1}else{0}) -or $taskWarm-cne$(if($taskExpected.scene-ceq'one-cell-stockpile'){'False'}else{'True'})){$Problems.Add('CAP02 preparation work/index/warmup values contradict the requested native member.')}
        }
    }
}
function Read-ProjectionEventJson($Event,$Problems) {
    try{return ConvertFrom-Json -InputObject $Event.detail -ErrorAction Stop}catch{$Problems.Add('CAP02 event contains malformed JSON: '+$Event.phase);return $null}
}
function Test-ProjectionEvents($Value,$Events,[string]$RunId,$Problems) {
    if(-not(Test-ProjectionShape $Value $Problems)){return}
    if($Events-isnot[array]){$Problems.Add('CAP02 events must be an array.');return}
    $taskP=$Value.storageProjection;$taskPrevious=0;$taskTick=-1;$taskRelevant=@();$taskRows=@{}
    foreach($taskEvent in $Events){
        if(-not(Test-ProjectionFields $taskEvent @{sequence='int';runId='string';caseId='string';utc='timestamp';phase='string';detail='string';tick='int'} 'CAP02 event' $Problems)){return}
        if($taskEvent.sequence-ne$taskPrevious+1 -or $taskEvent.runId-cne$RunId -or $taskEvent.caseId-cne'CAP02' -or $taskEvent.tick-lt$taskTick){$Problems.Add('CAP02 outer event identity/sequence/tick order is inconsistent.')}
        $taskPrevious=$taskEvent.sequence;$taskTick=$taskEvent.tick
        if($taskEvent.phase-like'storage-projection-*'){
            if($taskEvent.tick-ne$taskP.startedTick){$Problems.Add('CAP02 projection event escaped the synchronous fixture tick.')}
            $taskRelevant+= $taskEvent
        }
    }
    $taskMap=@{'storage-projection-assertion'='assertions';'storage-projection-operation'='operations';'storage-projection-physical-setup'='initialPhysical';'storage-projection-physical-final'='finalPhysical'}
    foreach($taskPhase in $taskMap.Keys){
        $taskCaptured=@($taskRelevant|Where-Object phase -CEQ $taskPhase);$taskExpected=$taskP.PSObject.Properties[$taskMap[$taskPhase]].Value
        if($taskCaptured.Count-ne$taskExpected.Count){$Problems.Add('CAP02 event/result multiplicity differs: '+$taskPhase);continue}
        for($taskI=0;$taskI-lt$taskCaptured.Count;$taskI++){$taskJson=Read-ProjectionEventJson $taskCaptured[$taskI] $Problems;if(-not(Test-ProjectionSame $taskJson $taskExpected[$taskI])){$Problems.Add('CAP02 event/result payload differs: '+$taskPhase)}}
    }
    foreach($taskPair in @(@('storage-projection-cell','cells'),@('storage-projection-eligibility','eligibility'))){
        $taskCaptured=@($taskRelevant|Where-Object phase -CEQ $taskPair[0]);$taskExpected=$taskP.PSObject.Properties[$taskPair[1]].Value
        if($taskCaptured.Count-ne$taskExpected.Count){$Problems.Add('CAP02 row event/result multiplicity differs: '+$taskPair[0]);continue}
        for($taskI=0;$taskI-lt$taskCaptured.Count;$taskI++){
            $taskJson=Read-ProjectionEventJson $taskCaptured[$taskI] $Problems
            if(-not(Test-ProjectionFields $taskJson @{scene='string';stage='string';value='object'} 'CAP02 row wrapper' $Problems)){continue}
            if($taskJson.scene-cne$taskExpected[$taskI].fixtureScene -or $taskJson.stage-cne$taskExpected[$taskI].fixtureStage -or -not(Test-ProjectionSame $taskJson.value $taskExpected[$taskI])){$Problems.Add('CAP02 wrapped row does not name/copy its actual result row.')}
            $taskKey=$taskPair[1]+'/'+$taskJson.scene+'/'+$taskJson.stage
            if($taskPair[1]-ceq'eligibility'){$taskKey+='/'+$taskExpected[$taskI].parcelId}
            if($taskRows.ContainsKey($taskKey)){$Problems.Add('CAP02 repeats a row capture identity.')}else{$taskRows[$taskKey]=$taskCaptured[$taskI].sequence}
        }
    }
    foreach($taskPair in @(@('storage-projection-result',$taskP),@('storage-projection-actor-setup',$taskP.initialActor))){
        $taskCaptured=@($taskRelevant|Where-Object phase -CEQ $taskPair[0]);if($taskCaptured.Count-ne1){$Problems.Add('CAP02 lacks a unique '+$taskPair[0]+'.');continue}
        $taskJson=Read-ProjectionEventJson $taskCaptured[0] $Problems;if(-not(Test-ProjectionSame $taskJson $taskPair[1])){$Problems.Add('CAP02 terminal/actor event differs from its result.')}
    }
    $taskBind=@($taskRelevant|Where-Object phase -CEQ 'storage-projection-bind')
    if($taskBind.Count-ne1){$Problems.Add('CAP02 lacks the unique actual binding event.')}else{
        $taskJson=Read-ProjectionEventJson $taskBind[0] $Problems
        if(Test-ProjectionFields $taskJson @{assemblies='array';bindings='array'} 'CAP02 binding event' $Problems){if(-not(Test-ProjectionSame $taskJson.assemblies $taskP.assemblies) -or -not(Test-ProjectionSame $taskJson.bindings $taskP.bindings)){$Problems.Add('CAP02 binding event differs from the actual selected assembly records.')}}
    }
    $taskKnown=@($taskMap.Keys)+@('storage-projection-cell','storage-projection-eligibility','storage-projection-result','storage-projection-actor-setup','storage-projection-bind','storage-projection-fixture-setup','storage-projection-fixture-mutation','storage-projection-fixture-repair')
    if(@($taskRelevant|Where-Object{$_.phase-cnotin$taskKnown}).Count-gt0){$Problems.Add('CAP02 has unknown projection event phases.')}
    Test-ProjectionChronology $taskP $taskRelevant $taskRows $Problems
    Test-ProjectionLifecycle $Value $Events $RunId $Problems
}
function Test-ProjectionLifecycle($Value,$Events,[string]$RunId,$Problems) {
    # Called after full DTO/event shape validation. This reconciles both success
    # and failure records; Evidence independently requires satisfied completion.
    $taskP=$Value.storageProjection; $taskPhases=@{}
    $taskOuterKinds=@('harness-start','new-game','game-version-provenance','assertion','map-initialized','scenario-observed','error-capture-boundary','terminal-result','unity-error','assertion-revised','negative-control-start','negative-control-observed','terminal-race-witness')
    if(@($Events|Where-Object{$_.phase-cnotlike'storage-projection-*'-and$_.phase-cnotin$taskOuterKinds}).Count-gt0){$Problems.Add('CAP02 has an unrecognized outer lifecycle event phase.')}
    foreach($taskName in @('harness-start','new-game','map-initialized','storage-projection-bind','storage-projection-actor-setup','storage-projection-fixture-setup','storage-projection-result','scenario-observed','error-capture-boundary','terminal-result')){
        $taskRows=@($Events|Where-Object phase -CEQ $taskName)
        if($taskRows.Count-ne1){$Problems.Add('CAP02 outer lifecycle lacks a unique '+$taskName+'.')}else{$taskPhases[$taskName]=$taskRows[0]}
    }
    if($taskPhases.Count-ne10){return}
    $taskBegin=$taskPhases['harness-start'];$taskNew=$taskPhases['new-game'];$taskMap=$taskPhases['map-initialized'];$taskResult=$taskPhases['storage-projection-result'];$taskObserved=$taskPhases['scenario-observed'];$taskBoundary=$taskPhases['error-capture-boundary'];$taskTerminal=$taskPhases['terminal-result']
    if($Value.runId-cne$RunId -or $Value.caseId-cne'CAP02' -or $taskBegin.sequence-ne1 -or $taskBegin.detail-cne('case=CAP02; expectedBehavior='+$taskP.expectedBehavior) -or $taskNew.detail-cne'GameComponent lifecycle callback received.' -or $taskTerminal.sequence-ne$Events[-1].sequence){$Problems.Add('CAP02 run/start/terminal identity or final event position is inconsistent.')}
    $taskSequence=@($taskBegin.sequence,$taskNew.sequence,$taskMap.sequence,$taskPhases['storage-projection-bind'].sequence,$taskPhases['storage-projection-actor-setup'].sequence,$taskPhases['storage-projection-fixture-setup'].sequence,$taskResult.sequence,$taskObserved.sequence,$taskBoundary.sequence,$taskTerminal.sequence)
    for($taskI=1;$taskI-lt$taskSequence.Count;$taskI++){if($taskSequence[$taskI]-le$taskSequence[$taskI-1]){$Problems.Add('CAP02 outer lifecycle is not ordered from map/setup through projection, observation, error closure and terminal result.');break}}
    $taskProjectionEvents=@($Events|Where-Object{$_.phase-like'storage-projection-*'})
    if(@($taskProjectionEvents|Where-Object{$_.sequence-le$taskMap.sequence-or$_.sequence-gt$taskResult.sequence}).Count-gt0){$Problems.Add('CAP02 projection event lies before map initialization or after its final projection result.')}
    $taskPhysical=@($Events|Where-Object phase -CEQ 'storage-projection-physical-setup')
    if($taskPhysical.Count-ne5 -or $taskPhysical[0].sequence-le$taskPhases['storage-projection-actor-setup'].sequence -or $taskPhysical[-1].sequence-ge$taskPhases['storage-projection-fixture-setup'].sequence){$Problems.Add('CAP02 physical setup does not follow actor setup and precede completed fixture setup.')}
    $taskExpectedObserved='case=CAP02; expected='+$taskP.expectedBehavior+'; requestedBehaviorSatisfied='+$taskP.requestedBehaviorSatisfied.ToString()+'; expectationMatched='+$taskP.expectationMatched.ToString()
    if($taskObserved.detail-cne$taskExpectedObserved -or $taskTerminal.detail-cne($Value.status+': '+$Value.detail) -or $taskBoundary.detail-cne'Threaded capture closed before terminal result; earlier startup and shutdown require whole Player.log review.' -or $taskObserved.tick-ne$taskP.finishedTick -or $taskBoundary.tick-ne$taskP.finishedTick -or $taskTerminal.tick-ne$taskP.finishedTick){$Problems.Add('CAP02 observed/terminal/error-boundary payload or tick contradicts the actual result.')}
    $taskMapTick=0;$taskWidth=0;$taskHeight=0;$taskDepth=0
    if($taskMap.detail-notmatch'^tick=([0-9]+); size=\(([0-9]+), ([0-9]+), ([0-9]+)\)$'){$Problems.Add('CAP02 map initialization lacks its native tick/size.');return}
    $taskRawTick=$Matches[1];$taskRawWidth=$Matches[2];$taskRawHeight=$Matches[3];$taskRawDepth=$Matches[4]
    if(-not[int]::TryParse($taskRawTick,[ref]$taskMapTick) -or -not[int]::TryParse($taskRawWidth,[ref]$taskWidth) -or -not[int]::TryParse($taskRawHeight,[ref]$taskHeight) -or -not[int]::TryParse($taskRawDepth,[ref]$taskDepth) -or $taskWidth-le0 -or $taskHeight-ne1 -or $taskDepth-le0 -or $taskMapTick-ne$taskMap.tick -or [long]$taskP.startedTick-$taskMapTick-lt5){$Problems.Add('CAP02 map size/tick does not support initialized-map execution after five real ticks.');return}
    $taskMapSize='('+$taskWidth+', '+$taskHeight+', '+$taskDepth+')';$taskElapsed=[long]$taskP.startedTick-$taskMapTick
    foreach($taskId in @('real-map-initialized','real-game-ticks-advanced','no-unity-errors-after-harness-start')){
        $taskAssertions=@($Value.assertions|Where-Object id -CEQ $taskId)
        $taskAssertionEvents=@($Events|Where-Object{$_.phase-ceq'assertion'-and$_.detail.StartsWith($taskId+': ')})
        if($taskAssertions.Count-ne1 -or $taskAssertionEvents.Count-ne1){$Problems.Add('CAP02 clean lifecycle lacks a unique global assertion and event for '+$taskId+'.');continue}
        $taskA=$taskAssertions[0];$taskEvent=$taskAssertionEvents[0]
        if(-not$taskA.passed){$Problems.Add('CAP02 required map/tick/error assertion is not passed.')}
        if($taskId-ceq'no-unity-errors-after-harness-start'){
            if($taskA.observed-cne'observedErrors=0; threaded capture through terminal-result boundary' -or $taskEvent.detail-cne'no-unity-errors-after-harness-start: passed; observedErrors=0' -or $taskEvent.sequence-le$taskResult.sequence -or $taskEvent.sequence-ge$taskObserved.sequence){$Problems.Add('CAP02 zero-error assertion does not reconcile its initial and terminal capture observations.')}
        }else{
            if($taskEvent.detail-cne($taskId+': passed; '+$taskA.observed)){$Problems.Add('CAP02 map/tick assertion event differs from its exported global assertion.')}
            if($taskId-ceq'real-map-initialized'){
                $taskMapPattern='^size='+[regex]::Escape($taskMapSize)+'; maps=([1-9][0-9]*)$';$taskMapCount=0
                if($taskA.observed-notmatch$taskMapPattern -or -not[int]::TryParse($Matches[1],[ref]$taskMapCount) -or $taskEvent.sequence-le$taskNew.sequence -or $taskEvent.sequence-ge$taskMap.sequence){$Problems.Add('CAP02 real-map assertion does not precede its matching map initialization.')}
            }elseif($taskA.observed-cne('elapsedTicks='+$taskElapsed) -or $taskEvent.sequence-le$taskMap.sequence -or $taskEvent.sequence-ge$taskProjectionEvents[0].sequence){$Problems.Add('CAP02 real-tick assertion is not supported before projection setup.')}
        }
    }
    $taskErrors=@($Events|Where-Object phase -CEQ 'unity-error')
    if($Value.unityErrorsObserved-ne$taskErrors.Count -or $Value.unityErrorsObserved-ne0 -or $taskErrors.Count-ne0 -or $null-ne$Value.negativeControl -or @($Events|Where-Object{$_.phase-in@('assertion-revised','negative-control-start','negative-control-observed','terminal-race-witness')}).Count-gt0){$Problems.Add('CAP02 clean run contradicts captured Unity errors, a revised error assertion or negative-control evidence.')}
    if($Value.status-cne$taskP.status){$Problems.Add('CAP02 zero-error outer status differs from its actual projection result.')}
}
function Test-ProjectionChronology($P,$Events,$Rows,$Problems) {
    # The narrative mutation/repair suffixes embed the same typed physical DTO,
    # allowing the deliberately broken list and exact restoration to be checked.
    $taskZone=Get-ProjectionOne $P.initialPhysical scene 'one-cell-stockpile' $Problems;if($null-eq$taskZone){return}
    $taskMutation=@($Events|Where-Object phase -CEQ 'storage-projection-fixture-mutation');$taskRepair=@($Events|Where-Object phase -CEQ 'storage-projection-fixture-repair')
    if($taskMutation.Count-ne1 -or $taskRepair.Count-ne1){$Problems.Add('CAP02 lacks its unique deliberate zone mutation and repair.');return}
    foreach($taskEntry in @(@($taskMutation[0],$true),@($taskRepair[0],$false))){
        $taskText=$taskEntry[0].detail;$taskPrefix=if($taskEntry[1]){'Removed exactly one zone.cells entry without notifications; '}else{'Restored original sole zone.cells entry; registries were untouched; '}
        if(-not$taskText.StartsWith($taskPrefix)){$Problems.Add('CAP02 zone control narrative is not the recorded fixture operation.');continue}
        try{$taskState=ConvertFrom-Json -InputObject $taskText.Substring($taskPrefix.Length) -ErrorAction Stop}catch{$Problems.Add('CAP02 zone control snapshot is malformed JSON.');continue}
        $taskBefore=$Problems.Count;Test-ProjectionPhysicalShape $taskState $Problems;if($Problems.Count-ne$taskBefore){continue}
        # Copy solely into a new comparison object; never edit evidence objects.
        $taskExpected=ConvertFrom-Json -InputObject (ConvertTo-Json -InputObject $taskZone -Depth 100 -Compress)
        if($taskEntry[1]){$taskExpected.zoneCells=@()}
        if(-not(Test-ProjectionSame $taskState $taskExpected)){$Problems.Add('CAP02 zone mutation/repair changes more than the controlled sole list entry.')}
    }
    $taskTerminal=@($Events|Where-Object phase -CEQ 'storage-projection-result');$taskSetup=@($Events|Where-Object phase -CEQ 'storage-projection-fixture-setup');$taskPhysical=@($Events|Where-Object phase -CEQ 'storage-projection-physical-setup');$taskFinal=@($Events|Where-Object phase -CEQ 'storage-projection-physical-final')
    if($taskTerminal.Count-ne1 -or $taskSetup.Count-ne1 -or $taskPhysical.Count-ne5 -or $taskFinal.Count-ne5){$Problems.Add('CAP02 lacks full setup/final event boundaries.');return}
    $taskOperations=@($Events|Where-Object phase -CEQ 'storage-projection-operation');$taskOperationIndex=@{}
    foreach($taskEvent in $taskOperations){$taskJson=Read-ProjectionEventJson $taskEvent $Problems;if(Test-ProjectionFields $taskJson @{scene='string';stage='string'} 'CAP02 operation chronology' $Problems){$taskKey=$taskJson.scene+'/'+$taskJson.stage;if($taskOperationIndex.ContainsKey($taskKey)){$Problems.Add('CAP02 repeats an operation capture identity.')}else{$taskOperationIndex[$taskKey]=$taskEvent.sequence}}}
    $taskOrder=@($taskPhysical[-1].sequence,$taskSetup[0].sequence)
    if($taskOperationIndex.ContainsKey('catalog/create')){$taskOrder+=$taskOperationIndex['catalog/create']}else{$Problems.Add('CAP02 chronology lacks catalog creation after setup.')}
    foreach($taskScene in Get-ProjectionScenes){
        if($taskScene-ceq'one-cell-stockpile'){
            $taskNames=@('op:prepare-valid','op:valid-before-mutation-open','cell:valid-before-mutation','elig:valid:Silver','elig:valid-fresh:Silver','mutation','cell:retained-index-stale','op:stale-recheck','op:prepare-stale-empty-list','op:fresh-stale-index-open','cell:fresh-stale-index','repair','op:prepare-repaired')
        }else{$taskNames=@('op:prepare')}
        foreach($taskStage in $(if($taskScene-ceq'one-cell-stockpile'){@('silver-then-cloth')}else{@('silver-then-cloth','cloth-then-silver')})){
            $taskNames+=@(('op:'+$taskStage+'-open'),('cell:'+$taskStage))
            foreach($taskDef in $(if($taskStage-ceq'cloth-then-silver'){@('Cloth','Silver')}else{@('Silver','Cloth')})){$taskNames+=@(('elig:'+$taskStage+':'+$taskDef),('op:'+$taskStage+'-recheck-'+$taskDef),('elig:'+$taskStage+'-fresh:'+$taskDef))}
            $taskNames+= 'cell:'+$taskStage+'-after-probes'
        }
        $taskPhysicalScene=Get-ProjectionOne $P.initialPhysical scene $taskScene $Problems;if($null-eq$taskPhysicalScene){continue}
        foreach($taskName in $taskNames){
            if($taskName-ceq'mutation'){$taskOrder+=$taskMutation[0].sequence;continue};if($taskName-ceq'repair'){$taskOrder+=$taskRepair[0].sequence;continue}
            $taskParts=$taskName.Split(':');$taskKey=$taskScene+'/'+$taskParts[1];$taskLookup=$taskOperationIndex
            if($taskParts[0]-ceq'cell'){$taskKey='cells/'+$taskKey;$taskLookup=$Rows}
            if($taskParts[0]-ceq'elig'){$taskSubject=if($taskParts[2]-ceq'Silver'){$taskPhysicalScene.silver}else{$taskPhysicalScene.cloth};$taskKey='eligibility/'+$taskKey+'/'+$taskScene+'/'+$taskSubject.def+$taskSubject.thingId;$taskLookup=$Rows}
            if(-not$taskLookup.ContainsKey($taskKey)){$Problems.Add('CAP02 chronology lacks '+$taskName+' for '+$taskScene+'.')}else{$taskOrder+=$taskLookup[$taskKey]}
        }
    }
    $taskOrder+=@($taskFinal[0].sequence,$taskFinal[-1].sequence,$taskTerminal[0].sequence)
    for($taskI=1;$taskI-lt$taskOrder.Count;$taskI++){if($taskOrder[$taskI]-le$taskOrder[$taskI-1]){$Problems.Add('CAP02 actual setup/probe/recheck/mutation/repair/final capture order is inconsistent.');break}}
}
