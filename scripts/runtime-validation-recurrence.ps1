# L04-B1 implemented-scope evidence consumer; Windows PowerShell 5.1 and PowerShell 7.
# This never approves all B1: D2 must remain explicitly incomplete and requestedBehaviorSatisfied false.
# Public functions receive the global result. No evidence, filesystem, process, or game mutation.
function Get-RecurrenceSchemas {
    @{
        job=@{token='int';loadId='int';startTick='int';expiry='int';targetA='int';def='str?';driver='str?';workgiver='str?';workgiverClass='str?';forced='bool';queueIds='int[]';counts='int[]'}
        counter=@{contract='str';subjectId='int';tick='int';anchorPresent='bool';backoffPresent='bool';warned='bool';failPresent='bool';backedOff='bool';anchorTick='int?';count='int?';stackCount='int?';until='int?';failTick='int?';failCount='int?'}
        cache=@{tick='int';generation='int';pawnId='int';sourceId='int';key='long';dictionaryPresent='bool';entryPresent='bool';pinnedLoadId='int?';jobState='int?';actualJob='job?'}
        thing=@{id='int';count='int';limit='int';cell='str?';holder='str?';ownerPawn='int?';spawned='bool';destroyed='bool';onMap='bool';mapHolder='bool';inventory='bool';hands='bool'}
        actor=@{id='int';inventory='int';hands='int';handSpace='int';carryMass='num';gearInventoryMass='num';cell='str';faction='str?';driver='str?';spawned='bool';healthy='bool';drafted='bool';haulingCapable='bool';haulingActive='bool';current='job?';queued='job[]'}
        reservation=@{pawnId='int';jobToken='int';jobId='int';thingId='int';count='int';cell='str';layer='str?'}
        claim=@{pawnId='int';units='int';def='str?';group='str'}
        physical=@{tick='int';mapId='int';source='int';high='int';elsewhere='int';inventory='int';hands='int';total='int';things='thing[]';actors='actor[]';reservations='reservation[]';claims='claim[]'}
        retirement=@{zoneId='str';cell='str';beforeGridZone='str?';afterGridZone='str?';beforeCells='str[]';afterCells='str[]';beforeRegistered='bool';beforeSlotMatches='bool';afterRegistered='bool';afterSlotAbsent='bool';afterGroupRemoved='bool'}
        pool=@{cycle='int';beforeCount='int';afterCount='int';targetToken='int';priorLoadId='int';action='str';targetReused='bool'}
        event=@{sequence='int';tick='int';actorId='int';sourceId='int';callId='int';parentCallId='int';kind='str';scene='str';caller='str';method='str?';detail='str?';warningAttribution='str?';queryOrdinal='int?';returned='bool?';forced='bool?';forceSweep='bool?';actualCurrent='bool?';job='job?';inputJob='job?';counter='counter?';cache='cache?';physical='physical?';beforePhysical='physical?';zoneRetirement='retirement?';poolOperation='pool?'}
        assertion=@{id='str';kind='str';detail='str';passed='bool'}
        layout=@{scene='str';rectangle='str';highCell='str';sourceCells='str[]';sources='int[]';actors='int[]';initial='physical'}
        hthing=@{id='int';count='int';destroyed='bool';spawned='bool';inventory='bool';hands='bool';cell='str?';holder='str?'}
        hclaim=@{group='str';recordedUnits='int';high='bool'}
        hstate=@{tick='int';sequence='int';jobId='int';source='int';high='int';elsewhere='int';inventory='int';hands='int';total='int';jobDef='str?';position='str';things='hthing[]';claims='hclaim[]'}
        hjob=@{id='int';observedTick='int';endTick='int';def='str';driver='str';workgiver='str?';workgiverClass='str?';queue='str';endCondition='str?';forced='bool';candidateObserved='bool';released='bool'}
        transfer=@{kind='str';jobId='int';tick='int';sequence='int';units='int';returnedSuccess='bool';original='hthing?';resulting='hthing?';before='hstate?';after='hstate'}
        query=@{tick='int';sequence='int';jobId='int';free='int';delivering='int';productionLiveUnits='int';taggedInventory='int';jobDef='str?';group='str';highGroup='bool';truncated='bool';subject='hthing';before='hstate'}
        gate=@{tick='int';sequence='int';jobId='int';jobDef='str?';allowed='bool';subject='hthing';before='hstate'}
        healthy=@{caseId='str';expectedBehavior='str';status='str';highCell='str';fixtureValid='bool';requestedBehaviorSatisfied='bool';expectationMatched='bool';baselineGapObserved='bool';timedOut='bool';ordinaryChain='bool';exactPickup='bool';ownClaimInventoryWitness='bool';physicalUnload='bool';successfulCleanups='bool';conserved='bool';observerHealthy='bool';layoutIntact='bool';followupWindowComplete='bool';settledHigh='bool';noRehaul='bool';startedTick='int';finishedTick='int';firstBulkJobId='int';firstUnloadJobId='int';firstUnloadEndTick='int';firstBulkPicked='int';firstUnloadHigh='int';firstUnloadSource='int';firstUnloadElsewhere='int';sourceZoneNetZeroBulkCycles='int';laterSourceReacquired='int';laterNativeHandDelivered='int';stableDistinctTicks='int';stableSinceTick='int';minTotal='int';maxTotal='int';settledBoundaries='int';sourceCells='str[]';originalSourceIds='int[]';finalState='hstate';jobs='hjob[]';transfers='transfer[]';queries='query[]';gates='gate[]'}
        result=@{caseId='str';expectedBehavior='str';status='str';counterContract='str';error='str?';startedTick='int';finishedTick='int';fixtureValid='bool';requestedBehaviorSatisfied='bool';implementedBehaviorSatisfied='bool';baselineGapObserved='bool';expectationMatched='bool';queryControlsComplete='bool';healthyDeliveryComplete='bool';incompleteControls='str[]';bindings='str[]';layouts='layout[]';events='event[]';assertions='assertion[]';healthyDelivery='healthy'}
    }
}
function Add-RecurrenceProblem($Problems,[string]$Text) { [void]$Problems.Add('L04-B1: '+$Text) }
function Assert-Recurrence([bool]$Okay,[string]$Text,$Problems) { if(-not $Okay){Add-RecurrenceProblem $Problems $Text} }
function Test-RecurrenceNode($Value,[string]$Type,[string]$Path,$Schemas,$Problems,[int]$Depth=0) {
    if($Depth -gt 32){Add-RecurrenceProblem $Problems ($Path+' exceeds shape depth.');return $false}
    if($Type.EndsWith('?')){if($null-eq$Value){return $true};$Type=$Type.Substring(0,$Type.Length-1)}
    if($Type.EndsWith('[]')){
        if($Value-isnot[array] -or $Value.Count-gt100000){Add-RecurrenceProblem $Problems ($Path+' must be a bounded JSON array.');return $false}
        $taskOkay=$true;$taskType=$Type.Substring(0,$Type.Length-2)
        for($taskIndex=0;$taskIndex-lt$Value.Count;$taskIndex++){if(-not(Test-RecurrenceNode $Value[$taskIndex] $taskType ($Path+'['+$taskIndex+']') $Schemas $Problems ($Depth+1))){$taskOkay=$false}}
        return $taskOkay
    }
    if($Schemas.ContainsKey($Type)){
        if($null-eq$Value -or $Value-isnot[pscustomobject]){Add-RecurrenceProblem $Problems ($Path+' must be an object.');return $false}
        $taskOkay=$true;$taskSchema=$Schemas[$Type]
        foreach($taskName in $taskSchema.Keys){$taskProperty=$Value.PSObject.Properties[$taskName];if($null-eq$taskProperty){Add-RecurrenceProblem $Problems ($Path+' lacks '+$taskName);$taskOkay=$false}else{if(-not(Test-RecurrenceNode $taskProperty.Value $taskSchema[$taskName] ($Path+'.'+$taskName) $Schemas $Problems ($Depth+1))){$taskOkay=$false}}}
        foreach($taskProperty in $Value.PSObject.Properties){if(-not$taskSchema.ContainsKey($taskProperty.Name)){Add-RecurrenceProblem $Problems ($Path+' has unknown field '+$taskProperty.Name);$taskOkay=$false}}
        return $taskOkay
    }
    $taskOkay=switch($Type){
        str {$Value-is[string]};bool {$Value-is[bool]}
        int {($Value-is[int]-or$Value-is[long])-and$Value-ge[int]::MinValue-and$Value-le[int]::MaxValue}
        long {$Value-is[int]-or$Value-is[long]}
        num {($Value-is[int]-or$Value-is[long]-or$Value-is[double]-or$Value-is[single]-or$Value-is[decimal])-and-not[double]::IsNaN([double]$Value)-and-not[double]::IsInfinity([double]$Value)}
        default {$false}
    }
    if(-not$taskOkay){Add-RecurrenceProblem $Problems ($Path+' has invalid '+$Type+' value.')};return [bool]$taskOkay
}
function Test-RecurrenceShape($Value,$Problems) {
    try {
        if($null-eq$Value -or $Value-isnot[pscustomobject] -or $null-eq$Value.PSObject.Properties['candidateRecurrence']){Add-RecurrenceProblem $Problems 'Global result lacks candidateRecurrence.';return $false}
        return Test-RecurrenceNode $Value.candidateRecurrence result 'result.candidateRecurrence' (Get-RecurrenceSchemas) $Problems
    }catch{Add-RecurrenceProblem $Problems ('Shape cannot be read: '+$_.Exception.Message);return $false}
}
function Test-RecurrenceEqual($Left,$Right,[string[]]$Ignore=@(),[string]$Path='') {
    if($Path-cin$Ignore){return $true}
    if($null-eq$Left-or$null-eq$Right){return ($null-eq$Left-and$null-eq$Right)}
    if($Left-is[array]-or$Right-is[array]){
        if($Left-isnot[array]-or$Right-isnot[array]-or$Left.Count-ne$Right.Count){return $false}
        for($taskIndex=0;$taskIndex-lt$Left.Count;$taskIndex++){if(-not(Test-RecurrenceEqual $Left[$taskIndex] $Right[$taskIndex] $Ignore ($Path+'['+$taskIndex+']'))){return $false}};return $true
    }
    if($Left-is[pscustomobject]-or$Right-is[pscustomobject]){
        if($Left-isnot[pscustomobject]-or$Right-isnot[pscustomobject]){return $false}
        $taskNames=@($Left.PSObject.Properties.Name|Sort-Object);$taskOther=@($Right.PSObject.Properties.Name|Sort-Object)
        if(($taskNames-join'|')-cne($taskOther-join'|')){return $false}
        foreach($taskName in $taskNames){$taskPath=if($Path){$Path+'.'+$taskName}else{$taskName};if(-not(Test-RecurrenceEqual $Left.$taskName $Right.$taskName $Ignore $taskPath)){return $false}};return $true
    }
    if(($Left-is[bool])-ne($Right-is[bool])-or($Left-is[string])-ne($Right-is[string])){return $false};return ($Left-ceq$Right)
}
function Get-RecurrenceOne($Values,[string]$Field,$Key,$Problems,[string]$Label) {
    $taskFound=@($Values|Where-Object{$_.$Field-ceq$Key});if($taskFound.Count-ne1){Add-RecurrenceProblem $Problems ($Label+' needs exactly one '+$Field+'='+$Key);return $null};return $taskFound[0]
}
function Get-RecurrenceCell([string]$Text,$Problems) {
    $taskX=0;$taskZ=0
    if($Text-cnotmatch'^\((-?[0-9]+), 0, (-?[0-9]+)\)$'-or-not[int]::TryParse($Matches[1],[ref]$taskX)-or-not[int]::TryParse($Matches[2],[ref]$taskZ)){Add-RecurrenceProblem $Problems ('Invalid cell '+$Text);return $null};return @{x=$taskX;z=$taskZ}
}
function Test-RecurrenceCounter($C,[int]$Source,[int]$Tick,$Problems) {
    Assert-Recurrence ($C.contract-ceq'legacy-anchor-tuple-v1;threshold6;gap180;backoff2500;exact-fields-required') 'Unreviewed counter contract.' $Problems
    Assert-Recurrence ($C.subjectId-eq$Source-and$C.tick-eq$Tick) 'Counter source/tick mismatch.' $Problems
    Assert-Recurrence ($C.anchorPresent-eq($null-ne$C.anchorTick)-and$C.anchorPresent-eq($null-ne$C.count)-and$C.anchorPresent-eq($null-ne$C.stackCount)-and$C.backoffPresent-eq($null-ne$C.until)-and$C.failPresent-eq($null-ne$C.failTick)-and$C.failPresent-eq($null-ne$C.failCount)) 'Counter presence/value mismatch.' $Problems
    Assert-Recurrence ($C.backedOff-eq($C.backoffPresent-and$Tick-lt$C.until)) 'Counter backoff/stamp mismatch.' $Problems
}
function Test-RecurrenceQueryPhysical($P,$Layout,$Problems) {
    Assert-Recurrence ($P.source-eq10-and$P.high-eq0-and$P.elsewhere-eq0-and$P.inventory-eq0-and$P.hands-eq0-and$P.total-eq10-and$P.claims.Count-eq0-and$P.reservations.Count-eq0) 'Query physical counts/claims/reservations changed.' $Problems
    Assert-Recurrence ($P.things.Count-eq2-and@($P.things.id|Select-Object -Unique).Count-eq2-and$P.actors.Count-eq$Layout.actors.Count-and@($P.actors.id|Select-Object -Unique).Count-eq$P.actors.Count) 'Query physical identity census mismatch.' $Problems
    foreach($taskThing in $P.things){Assert-Recurrence ($taskThing.id-in$Layout.sources-and$taskThing.count-eq5-and$taskThing.limit-eq75-and$taskThing.spawned-and-not$taskThing.destroyed-and$taskThing.onMap-and$taskThing.mapHolder-and$taskThing.holder-ceq'Verse.Map'-and-not$taskThing.inventory-and-not$taskThing.hands-and$null-eq$taskThing.ownerPawn) 'Query item is not an original five-unit spawned map stack.' $Problems
        $taskAllowed=if($taskThing.id-eq$Layout.sources[0]-and$Layout.scene-ceq'M0'){@($Layout.sourceCells[0],$Layout.sourceCells[2])}elseif($taskThing.id-eq$Layout.sources[0]){@($Layout.sourceCells[0])}else{@($Layout.sourceCells[1])};Assert-Recurrence ($taskThing.cell-cin$taskAllowed) 'Unexpected query cargo cell.' $Problems}
    foreach($taskActor in $P.actors){Assert-Recurrence ($taskActor.id-in$Layout.actors-and$taskActor.spawned-and$taskActor.healthy-and-not$taskActor.drafted-and$taskActor.haulingCapable-and-not$taskActor.haulingActive-and$taskActor.inventory-eq0-and$taskActor.hands-eq0-and$taskActor.handSpace-ge10-and$taskActor.carryMass-gt0) 'Query actor capability/custody mismatch.' $Problems
        foreach($taskJob in @($taskActor.current)+@($taskActor.queued)){if($null-ne$taskJob){Assert-Recurrence ($taskJob.def-cnotin@('HaulersDream_BulkHaul','HaulersDream_UnloadInventory','HaulToCell','HaulToContainer')) 'Transport current/queued in a query control.' $Problems}}}
}
function Test-RecurrenceQueue($Job,$Layout,$Problems) {
    Assert-Recurrence ($null-ne$Job-and$Job.token-gt0-and$Job.loadId-gt0-and$Job.def-ceq'HaulersDream_BulkHaul'-and-not$Job.forced-and$Job.targetA-eq$Layout.sources[0]-and$Job.queueIds.Count-eq2-and@($Job.queueIds|Select-Object -Unique).Count-eq2-and$Job.counts.Count-eq2-and$Job.counts[0]-eq5-and$Job.counts[1]-eq5-and@($Job.queueIds|Where-Object{$_-notin$Layout.sources}).Count-eq0) 'Candidate is not the exact original automatic 5+5 bulk selection.' $Problems
}
function Get-RecurrenceAssertionCatalog($R) {
    $taskCatalog=@{}
    foreach($taskId in @('expectation','main-map','actual-workgiver','no-steel-rule','storage-seam','steel-limit','D1-healthy-delivery','D1-native-selection-dispatch')){$taskCatalog[$taskId]='execution'}
    foreach($taskLayout in $R.layouts){
        $taskScene=$taskLayout.scene
        foreach($taskSuffix in @('bounds','no-zones','actual-destination-capacity','initial-cargo','fresh-counter','owned-zone-before-retirement','owned-zone-retired')){$taskCatalog[$taskScene+'-'+$taskSuffix]='execution'}
        foreach($taskId in $taskLayout.sources){$taskCatalog['spawn-Steel'+$taskId]='execution'}
        for($taskI=0;$taskI-lt$taskLayout.actors.Count;$taskI++){foreach($taskSuffix in @('actor-','comp-')){$taskCatalog[$taskScene+'-'+$taskSuffix+$taskI]='execution'}}
        $taskN=if($taskScene-ceq'Q1'){6}else{7}
        for($taskI=1;$taskI-le$taskN;$taskI++){foreach($taskSuffix in @('query-physical-','query-actors-')){$taskCatalog[$taskScene+'-'+$taskSuffix+$taskI]='execution'}}
        foreach($taskSuffix in @('actual-entry-chain','six-real-candidates','zero-starts','no-failure-state','build-count','warning-attribution')){$taskCatalog[$taskScene+'-'+$taskSuffix]='execution'}
        $taskCatalog[$taskScene+'-no-query-recurrence']='behavior';$taskCatalog[$taskScene+'-expected-transition']='expectation'
        if($taskScene-cin@('Q1','Q2','Q4')){$taskCatalog[$taskScene+'-single-tick']='execution'}
        if($taskScene-ceq'Q1'){$taskCatalog['Q1-live-cache-reuse']='execution'}
        if($taskScene-ceq'Q2'){$taskCatalog['Q2-distinct-pawns']='execution'}
        if($taskScene-ceq'Q4'){$taskCatalog['Q4-five-real-recycles']='execution';$taskCatalog['Q4-recycled-cache-misses']='execution';for($taskI=0;$taskI-lt5;$taskI++){$taskCatalog['Q4-real-pool-clear-'+$taskI]='execution';$taskCatalog['Q4-real-pool-reuse-'+$taskI]='execution'}}
        if($taskScene-cin@('Q3','M0')){
            foreach($taskSuffix in @('ready-cargo-unchanged','adjacent-ticks','full-boundary-chain','declared-relocations-only')){$taskCatalog[$taskScene+'-'+$taskSuffix]='execution'}
            for($taskI=0;$taskI-lt6;$taskI++){$taskCatalog[$taskScene+'-same-native-idle-'+$taskI]='execution';$taskCatalog[$taskScene+'-tick-entry-continuity-'+($taskI+1)]='execution'}
            for($taskI=1;$taskI-le7;$taskI++){$taskCatalog[$taskScene+'-query-entry-continuity-'+$taskI]='execution'}
            if($taskScene-ceq'M0'){for($taskI=0;$taskI-lt6;$taskI++){$taskCatalog['M0-clear-relocation-'+$taskI]='execution';$taskCatalog['M0-real-relocation-'+$taskI]='execution'}}
        }
    };return $taskCatalog
}
function Get-RecurrenceHealthySetupIds {
    foreach($taskSuffix in @('expectation','home-map','steel-limit','settings','no-steel-keep-rule','seam-active','claim-field','patch-bulk-route','patch-storage-gate','patch-storage-counter','patch-storage-reservation','bounds','no-overwritten-zones','roof-support','exact-stockpiles','no-other-higher-storage','capable-human','haul-comp','tag-observation','native-work-only','initial-no-claims','initial-counts','observers','setting-masterEnabled','setting-haulToStack','setting-bulkHaul','setting-markForUnload','setting-pickupDelayOnHauling','setting-carryLimitFraction','setting-carryMassCapKg','setting-autoHaulYields')){'fixture-l04-b1-healthy-'+$taskSuffix}
}
function Test-RecurrenceLayoutGeometry($R,[int]$Width,[int]$Height,$Problems) {
    $taskCenterX=[int][math]::Floor($Width/2);$taskCenterZ=[int][math]::Floor($Height/2)
    $taskOffsets=@{Q1=@(-22,-16);Q2=@(22,-16);Q3=@(-22,0);Q4=@(22,0);M0=@(-22,16)}
    foreach($taskL in $R.layouts){
        if(-not$taskOffsets.ContainsKey($taskL.scene)){continue};$taskOffset=$taskOffsets[$taskL.scene];$taskX=$taskCenterX+$taskOffset[0];$taskZ=$taskCenterZ+$taskOffset[1]
        $taskRect='('+($taskX-20)+','+($taskZ-6)+','+($taskX+20)+','+($taskZ+6)+')'
        $taskSources=@(('('+($taskX-14)+', 0, '+($taskZ-1)+')'),('('+($taskX-14)+', 0, '+($taskZ+1)+')'),('('+($taskX-13)+', 0, '+($taskZ-1)+')'))
        Assert-Recurrence ($taskL.rectangle-ceq$taskRect-and$taskL.highCell-ceq('('+($taskX+14)+', 0, '+$taskZ+')')-and(Test-RecurrenceEqual $taskL.sourceCells $taskSources)-and$taskX-20-ge0-and$taskX+20-lt$Width-and$taskZ-6-ge0-and$taskZ+6-lt$Height) 'Query layout differs from its native map-relative room and exact source/high cells.' $Problems
    }
    $taskH=$R.healthyDelivery;$taskSources=@(('('+($taskCenterX-14)+', 0, '+($taskCenterZ-1)+')'),('('+($taskCenterX-14)+', 0, '+($taskCenterZ+1)+')'))
    Assert-Recurrence ((Test-RecurrenceEqual $taskH.sourceCells $taskSources)-and$taskH.highCell-ceq('('+($taskCenterX+14)+', 0, '+$taskCenterZ+')')) 'D1 layout is not the independent native map-center rectangle.' $Problems
}
function Test-RecurrenceCalls($Rows,$Problems) {
    $taskKinds=@('has','candidate','try-build','build','native-work','note')
    $taskEntries=@($Rows|Where-Object{$_.kind-cin@('has-enter','candidate-enter','try-build-enter','build-enter','native-work-enter','note-enter','start-attempt')})
    Assert-Recurrence (@($taskEntries.callId|Select-Object -Unique).Count-eq$taskEntries.Count-and@($taskEntries|Where-Object{$_.callId-le0}).Count-eq0) 'Observer call IDs are not unique positive identities.' $Problems
    foreach($taskEntry in $taskEntries){
        $taskKind=$taskEntry.kind-replace'-(enter|attempt)$','';$taskReturns=@($Rows|Where-Object{$_.callId-eq$taskEntry.callId-and$_.kind-ceq($taskKind+'-return')})
        if($taskReturns.Count-ne1){Add-RecurrenceProblem $Problems ('Missing/duplicate return for call '+$taskEntry.callId);continue};$taskReturn=$taskReturns[0]
        Assert-Recurrence ($taskReturn.sequence-gt$taskEntry.sequence-and$taskReturn.tick-eq$taskEntry.tick-and$taskReturn.scene-ceq$taskEntry.scene) 'Call return chronology/context mismatch.' $Problems
        foreach($taskField in @('actorId','sourceId','queryOrdinal','caller','forced','forceSweep')){Assert-Recurrence (Test-RecurrenceEqual $taskEntry.$taskField $taskReturn.$taskField) ('Call changed '+$taskField) $Problems}
        if($taskKind-cne'note'){Assert-Recurrence ($taskReturn.parentCallId-eq$taskEntry.parentCallId) 'Call parent changed at return.' $Problems}
        if($taskEntry.parentCallId-gt0){$taskParent=@($taskEntries|Where-Object{$_.callId-eq$taskEntry.parentCallId});if($taskParent.Count-ne1){Add-RecurrenceProblem $Problems 'Missing enclosing call.'}else{
            $taskEnd=@($Rows|Where-Object{$_.callId-eq$taskParent[0].callId-and$_.kind-ceq($taskParent[0].kind-replace'-enter$','-return')})
            Assert-Recurrence ($taskParent[0].sequence-lt$taskEntry.sequence-and$taskEnd.Count-eq1-and$taskEnd[0].sequence-gt$taskReturn.sequence) 'Observer call is outside its enclosing invocation.' $Problems
            Assert-Recurrence ($taskEntry.scene-ceq$taskParent[0].scene-and$taskEntry.tick-eq$taskParent[0].tick-and$taskEntry.caller-ceq$taskParent[0].caller-and(Test-RecurrenceEqual $taskEntry.queryOrdinal $taskParent[0].queryOrdinal)) 'Nested call changed its enclosing scene/tick/caller/query identity.' $Problems
            if($taskKind-cne'note'){Assert-Recurrence ($taskEntry.actorId-eq$taskParent[0].actorId) 'Nested native call used a different pawn.' $Problems}
            if($taskParent[0].sourceId-gt0){Assert-Recurrence ($taskEntry.sourceId-eq$taskParent[0].sourceId) 'Nested call used a different source.' $Problems}
        }}
    }
    foreach($taskReturn in @($Rows|Where-Object{$_.kind-cin@('has-return','candidate-return','try-build-return','build-return','native-work-return','note-return','start-return')})){
        Assert-Recurrence (@($taskEntries|Where-Object{$_.callId-eq$taskReturn.callId}).Count-eq1) 'Orphan observer return.' $Problems
    }
}
function Test-RecurrenceQueryObservations($Inside,$Before,$After,$Problems) {
    # Emit inherits caller/ordinal even for static Note and JobMaker callbacks. Their
    # default actor/flags are deliberately distinct from a pawn-bound scanner call.
    $taskCalls=@('has-enter','has-return','candidate-enter','candidate-return','try-build-enter','try-build-return','build-enter','build-return')
    foreach($taskE in $Inside){
        Assert-Recurrence ($taskE.caller-ceq'fixture-query'-and$taskE.queryOrdinal-eq$Before.queryOrdinal-and$taskE.tick-eq$Before.tick-and$taskE.scene-ceq$Before.scene) 'Nested query event contradicts its actual fixture invocation.' $Problems
        if($taskE.kind-cin$taskCalls){
            Assert-Recurrence ($taskE.actorId-eq$Before.actorId-and$taskE.sourceId-eq$Before.sourceId-and$taskE.forced-eq$false-and$taskE.forceSweep-eq$false-and$null-ne$taskE.counter-and$null-ne$taskE.cache) 'Nested query call changed pawn/source/inputs or omitted its captured state.' $Problems
            $taskExpected=if($taskE.kind.EndsWith('-enter')){$Before.counter}else{$After.counter}
            Assert-Recurrence (Test-RecurrenceEqual $taskE.counter $taskExpected) 'Nested query counter contradicts the enclosing before/after transition.' $Problems
            if($taskE.kind-cin@('has-enter','candidate-enter','try-build-enter')){Assert-Recurrence (Test-RecurrenceEqual $taskE.cache $Before.cache) 'Pre-build cache differs from the enclosing query input.' $Problems}
            if($taskE.kind-cin@('has-return','candidate-return','try-build-return')){Assert-Recurrence (Test-RecurrenceEqual $taskE.cache $After.cache) 'Returned cache differs from the enclosing query result.' $Problems}
        }elseif($taskE.kind-cin@('note-enter','note-return')){
            Assert-Recurrence ($taskE.actorId-eq0-and$taskE.sourceId-eq$Before.sourceId-and$null-eq$taskE.forced-and$null-eq$taskE.forceSweep-and$null-eq$taskE.cache) 'Static recurrence callback has invented pawn/input/cache state.' $Problems
            $taskExpected=if($taskE.kind-ceq'note-enter'){$Before.counter}else{$After.counter}
            Assert-Recurrence (Test-RecurrenceEqual $taskE.counter $taskExpected) 'Actual Note callback counter contradicts its enclosing query transition.' $Problems
        }elseif($taskE.kind-ceq'make-job'){
            Assert-Recurrence ($taskE.actorId-eq0-and$taskE.sourceId-eq0-and$null-eq$taskE.forced-and$null-eq$taskE.forceSweep-and$null-eq$taskE.counter-and$null-eq$taskE.cache) 'Static MakeJob callback has invented pawn/input/state.' $Problems
            $taskParent=@($Inside|Where-Object{$_.callId-eq$taskE.parentCallId-and$_.kind-cin@('candidate-enter','build-enter')})
            if($taskParent.Count-eq1){$taskEnd=@($Inside|Where-Object{$_.callId-eq$taskE.parentCallId-and$_.kind-ceq($taskParent[0].kind-replace'-enter$','-return')});Assert-Recurrence ($taskParent[0].sequence-lt$taskE.sequence-and$taskEnd.Count-eq1-and$taskEnd[0].sequence-gt$taskE.sequence) 'Query MakeJob is outside its native candidate/build parent.' $Problems}else{Add-RecurrenceProblem $Problems 'Query MakeJob lacks its actual native candidate/build parent.'}
        }
    }
    $taskBuildIn=@($Inside|Where-Object{$_.kind-ceq'build-enter'});$taskBuildOut=@($Inside|Where-Object{$_.kind-ceq'build-return'})
    if($taskBuildIn.Count-eq1-and$taskBuildOut.Count-eq1){
        $taskCache=$taskBuildIn[0].cache
        Assert-Recurrence ((Test-RecurrenceEqual $taskCache $taskBuildOut[0].cache)-and$taskCache.dictionaryPresent-and$taskCache.generation-eq$Before.tick) 'Build mutated the cache before its enclosing TryBuild publishes the result.' $Problems
        if($Before.cache.generation-eq$Before.tick){Assert-Recurrence (Test-RecurrenceEqual $taskCache $Before.cache @('dictionaryPresent')) 'Same-tick Build input does not retain the actual old cache entry.' $Problems}
        else{Assert-Recurrence ((Test-RecurrenceEqual $taskCache $Before.cache @('dictionaryPresent','generation','entryPresent','pinnedLoadId','jobState','actualJob'))-and-not$taskCache.entryPresent-and$null-eq$taskCache.pinnedLoadId-and$null-eq$taskCache.jobState-and$null-eq$taskCache.actualJob) 'New-tick Build input did not observe native cache clearing.' $Problems}
    }
}
function Test-RecurrenceContinuity($Rows,$Layout,$Problems) {
    $taskReady=Get-RecurrenceOne $Rows kind 'natural-idle-ready' $Problems $Layout.scene;if($null-eq$taskReady){return}
    $taskPrevious=$taskReady.physical;$taskPreviousSequence=$taskReady.sequence
    Assert-Recurrence ($null-ne$taskPrevious-and$taskPrevious.actors.Count-eq1-and$taskPrevious.actors[0].driver-ceq'Verse.AI.JobDriver_Wait'-and$null-ne$taskReady.job) 'Missing natural native Wait witness.' $Problems
    if($null-eq$taskPrevious){return}
    Assert-Recurrence (Test-RecurrenceEqual $taskReady.job $taskPrevious.actors[0].current) 'Natural Wait record differs from physical actor job.' $Problems
    Assert-Recurrence ($taskReady.job.expiry-gt0-and([long]$taskReady.job.startTick+$taskReady.job.expiry)-ge([long]$taskReady.tick+12)) 'Natural idle lifetime does not cover query interval.' $Problems
    $taskBoundaries=@($Rows|Where-Object{$_.kind-ceq'boundary-continuity'});$taskMoves=@($Rows|Where-Object{$_.kind-ceq'fixture-relocation'})
    Assert-Recurrence ($taskBoundaries.Count-eq13-and$taskMoves.Count-eq$(if($Layout.scene-ceq'M0'){6}else{0})) 'Continuity/relocation catalog mismatch.' $Problems
    for($taskI=1;$taskI-le7;$taskI++){
        if($taskI-le6){
            $taskEntry=@($taskBoundaries|Where-Object{$_.method-ceq'tick-entry'-and$_.callId-eq$taskI});if($taskEntry.Count-ne1){Add-RecurrenceProblem $Problems 'Missing tick-entry boundary.';return};$taskEntry=$taskEntry[0]
            Assert-Recurrence ($taskEntry.returned-eq$true-and$taskEntry.sequence-gt$taskPreviousSequence-and(Test-RecurrenceEqual $taskEntry.beforePhysical $taskPrevious)) 'Tick boundary is disconnected from its actual predecessor.' $Problems
            $taskIgnore=if($taskI-gt1){@('tick','actors[0].current.expiry')}else{@()}
            Assert-Recurrence ((Test-RecurrenceEqual $taskEntry.beforePhysical $taskEntry.physical $taskIgnore)-and$taskEntry.physical.tick-eq($taskPrevious.tick+$(if($taskI-gt1){1}else{0}))) 'Unexplained cross-tick physical/cohort change.' $Problems
            $taskPrevious=$taskEntry.physical;$taskPreviousSequence=$taskEntry.sequence
            if($Layout.scene-ceq'M0'){
                $taskMove=@($taskMoves|Where-Object{$_.callId-eq$taskI});if($taskMove.Count-ne1){Add-RecurrenceProblem $Problems 'Missing M0 relocation.';return};$taskMove=$taskMove[0]
                Assert-Recurrence ($taskMove.returned-eq$true-and$taskMove.sourceId-eq$Layout.sources[0]-and$taskMove.sequence-gt$taskPreviousSequence-and(Test-RecurrenceEqual $taskMove.beforePhysical $taskPrevious)) 'M0 relocation predecessor/source mismatch.' $Problems
                $taskAnchorIndex=-1;for($taskJ=0;$taskJ-lt$taskPrevious.things.Count;$taskJ++){if($taskPrevious.things[$taskJ].id-eq$Layout.sources[0]){$taskAnchorIndex=$taskJ}}
                if($taskAnchorIndex-lt0){Add-RecurrenceProblem $Problems 'M0 anchor absent.';return}
                $taskFrom=$taskPrevious.things[$taskAnchorIndex].cell;$taskTo=if($taskFrom-ceq$Layout.sourceCells[0]){$Layout.sourceCells[2]}else{$Layout.sourceCells[0]}
                Assert-Recurrence ((Test-RecurrenceEqual $taskPrevious $taskMove.physical @('things['+$taskAnchorIndex+'].cell'))-and$taskMove.physical.things[$taskAnchorIndex].cell-ceq$taskTo-and$taskFrom-cne$taskTo) 'M0 changed more than the declared anchor cell.' $Problems
                $taskPrevious=$taskMove.physical;$taskPreviousSequence=$taskMove.sequence
            }
        }
        $taskEntry=@($taskBoundaries|Where-Object{$_.method-ceq'query-entry'-and$_.callId-eq$taskI});if($taskEntry.Count-ne1){Add-RecurrenceProblem $Problems 'Missing query-entry continuity.';return};$taskEntry=$taskEntry[0]
        $taskBefore=@($Rows|Where-Object{$_.kind-ceq'query-before'-and$_.callId-eq$taskI});$taskAfter=@($Rows|Where-Object{$_.kind-ceq'query-after'-and$_.callId-eq$taskI})
        if($taskBefore.Count-ne1-or$taskAfter.Count-ne1){Add-RecurrenceProblem $Problems 'Continuity has no unique query pair.';return}
        Assert-Recurrence ($taskEntry.returned-eq$true-and$taskEntry.sequence-gt$taskPreviousSequence-and$taskEntry.sequence-lt$taskBefore[0].sequence-and(Test-RecurrenceEqual $taskPrevious $taskEntry.beforePhysical)-and(Test-RecurrenceEqual $taskPrevious $taskEntry.physical)-and(Test-RecurrenceEqual $taskPrevious $taskBefore[0].physical)) 'Immediate query boundary is disconnected or mutates state.' $Problems
        $taskPrevious=$taskAfter[0].physical;$taskPreviousSequence=$taskAfter[0].sequence
    }
}
function Test-RecurrenceQueries($R,$Expected,$Problems) {
    $taskBaseline=$Expected-ceq'baseline-gap'
    foreach($taskLayout in $R.layouts){
        $taskName=$taskLayout.scene;$taskRows=@($R.events|Where-Object{$_.scene-ceq$taskName})
        $taskBefore=@($taskRows|Where-Object{$_.kind-ceq'query-before'});$taskAfter=@($taskRows|Where-Object{$_.kind-ceq'query-after'})
        $taskN=if($taskName-ceq'Q1'){6}else{7};if($taskBefore.Count-ne$taskN-or$taskAfter.Count-ne$taskN){Add-RecurrenceProblem $Problems ($taskName+' query count mismatch.');continue}
        $taskMeasured=@($taskRows|Where-Object{$_.sequence-ge$taskBefore[0].sequence-and$_.sequence-le$taskAfter[-1].sequence})
        Assert-Recurrence (@($taskMeasured|Where-Object{$_.kind-ceq'start-attempt'}).Count-eq0) 'A query cohort started a job during its measured interval.' $Problems
        $taskBuilds=@($taskMeasured|Where-Object{$_.kind-ceq'build-return'});Assert-Recurrence ($taskBuilds.Count-eq$(if($taskName-ceq'Q1'){1}else{6})) ($taskName+' actual build count mismatch.') $Problems
        for($taskI=0;$taskI-lt$taskN;$taskI++){
            $taskB=$taskBefore[$taskI];$taskA=$taskAfter[$taskI];$taskOrdinal=$taskI+1
            Assert-Recurrence ($taskB.callId-eq$taskOrdinal-and$taskA.callId-eq$taskOrdinal-and$taskB.queryOrdinal-eq$taskOrdinal-and$taskA.queryOrdinal-eq$taskOrdinal-and$taskB.sequence-lt$taskA.sequence-and$taskB.tick-eq$taskA.tick-and$taskB.actorId-eq$taskA.actorId-and$taskB.sourceId-eq$taskLayout.sources[0]-and$taskA.sourceId-eq$taskLayout.sources[0]-and$taskB.caller-ceq'fixture-query'-and$taskA.caller-ceq'fixture-query'-and$taskB.forced-eq$false-and$taskA.forced-eq$false-and$taskB.forceSweep-eq$false-and$taskA.forceSweep-eq$false) 'Query pair identity/input/chronology mismatch.' $Problems
            $taskActor=if($taskName-ceq'Q2'-and$taskI-lt6){$taskLayout.actors[$taskI]}else{$taskLayout.actors[0]};Assert-Recurrence ($taskA.actorId-eq$taskActor) 'Query used the wrong actor.' $Problems
            $taskTick=$taskAfter[0].tick+$(if($taskName-cin@('Q3','M0')){[math]::Min($taskI,5)}else{0});Assert-Recurrence ($taskA.tick-eq$taskTick) 'Query did not use the required same/adjacent actual tick.' $Problems
            foreach($taskRow in @($taskB,$taskA)){
                if($null-eq$taskRow.physical-or$null-eq$taskRow.counter-or$null-eq$taskRow.cache){Add-RecurrenceProblem $Problems 'Query lacks required physical/counter/cache evidence.';continue}
                Test-RecurrenceQueryPhysical $taskRow.physical $taskLayout $Problems
                Assert-Recurrence ($taskRow.physical.tick-eq$taskRow.tick-and$taskRow.physical.mapId-eq$taskLayout.initial.mapId) 'Query physical map/tick mismatch.' $Problems
                Test-RecurrenceCounter $taskRow.counter $taskRow.sourceId $taskRow.tick $Problems
                $taskCache=$taskRow.cache;$taskKey=([long]$taskRow.actorId*[long]4294967296)+[long]$taskRow.sourceId
                Assert-Recurrence ($taskCache.key-eq$taskKey-and$taskCache.pawnId-eq$taskRow.actorId-and$taskCache.sourceId-eq$taskRow.sourceId-and$taskCache.tick-eq$taskRow.tick-and(-not$taskCache.entryPresent-or$taskCache.dictionaryPresent)) 'Cache identity/key mismatch.' $Problems
                if(-not$taskCache.entryPresent){Assert-Recurrence ($null-eq$taskCache.pinnedLoadId-and$null-eq$taskCache.jobState-and$null-eq$taskCache.actualJob) 'Absent cache has invented payload.' $Problems}
                Assert-Recurrence (-not$taskRow.counter.failPresent) 'Query generated failed-job state.' $Problems
            }
            Assert-Recurrence (Test-RecurrenceEqual $taskB.physical $taskA.physical) 'A scanner call changed physical/actor state.' $Problems
            if($taskI-eq0){Assert-Recurrence (-not$taskB.counter.anchorPresent-and-not$taskB.counter.backoffPresent-and-not$taskB.counter.warned-and-not$taskB.counter.failPresent) 'Query anchor did not start fresh.' $Problems}
            else{Assert-Recurrence (Test-RecurrenceEqual $taskAfter[$taskI-1].counter $taskB.counter @('tick')) 'Counter changed between queries without a build.' $Problems}
            $taskInside=@($taskMeasured|Where-Object{$_.sequence-gt$taskB.sequence-and$_.sequence-lt$taskA.sequence})
            Test-RecurrenceQueryObservations $taskInside $taskB $taskA $Problems
            foreach($taskKind in @('has','candidate')){
                $taskEnter=@($taskInside|Where-Object{$_.kind-ceq($taskKind+'-enter')});$taskReturn=@($taskInside|Where-Object{$_.kind-ceq($taskKind+'-return')})
                Assert-Recurrence ($taskEnter.Count-eq1-and$taskReturn.Count-eq1) 'Query lacks its unique actual native scanner/candidate call.' $Problems
                if($taskEnter.Count-eq1-and$taskReturn.Count-eq1){Assert-Recurrence ($taskEnter[0].actorId-eq$taskActor-and$taskEnter[0].sourceId-eq$taskA.sourceId-and$taskEnter[0].queryOrdinal-eq$taskOrdinal-and$taskReturn[0].returned-eq$taskA.returned) 'Native scanner return/context differs from query.' $Problems;if($taskKind-ceq'candidate'){Assert-Recurrence (Test-RecurrenceEqual $taskReturn[0].job $taskA.job) 'Captured candidate differs from actual candidate return.' $Problems}}
            }
            $taskRejected=$taskBaseline-and$taskI-eq6
            Assert-Recurrence ($taskA.returned-eq(-not$taskRejected)-and(-not$taskRejected-or$null-eq$taskA.job)) 'Unexpected automatic candidate availability.' $Problems
            if(-not$taskRejected){Test-RecurrenceQueue $taskA.job $taskLayout $Problems;Assert-Recurrence ($taskA.cache.entryPresent-and$taskA.cache.jobState-eq0-and$taskA.cache.generation-eq$taskA.tick-and$taskA.cache.pinnedLoadId-eq$taskA.job.loadId-and(Test-RecurrenceEqual $taskA.cache.actualJob $taskA.job)) 'Returned candidate is not the captured live automatic cache entry.' $Problems}
            $taskBuildExpected=($taskI-lt6-and($taskName-cne'Q1'-or$taskI-eq0));$taskThisBuild=@($taskInside|Where-Object{$_.kind-ceq'build-return'})
            Assert-Recurrence ($taskThisBuild.Count-eq$(if($taskBuildExpected){1}else{0})) 'Query build versus cache reuse mismatch.' $Problems
            if($taskThisBuild.Count-eq1){Assert-Recurrence (Test-RecurrenceEqual $taskThisBuild[0].job $taskA.job) 'Built Job differs from returned candidate.' $Problems}
            $taskNotes=@($taskInside|Where-Object{$_.kind-ceq'note-enter'});Assert-Recurrence ($taskNotes.Count-eq$(if($taskBaseline-and$taskBuildExpected){1}else{0})) 'Unexpected actual recurrence accounting invocation.' $Problems
            $taskHas=Get-RecurrenceOne $taskInside kind 'has-enter' $Problems 'Query chain';$taskCandidate=Get-RecurrenceOne $taskInside kind 'candidate-enter' $Problems 'Query chain'
            if($null-ne$taskHas-and$null-ne$taskCandidate){Assert-Recurrence ($taskHas.parentCallId-eq0-and$taskCandidate.parentCallId-eq$taskHas.callId) 'Actual candidate call is not nested in the native Has invocation.' $Problems}
            foreach($taskTry in @($taskInside|Where-Object{$_.kind-ceq'try-build-enter'})){Assert-Recurrence ($null-ne$taskCandidate-and$taskTry.parentCallId-eq$taskCandidate.callId) 'Bulk entry is outside actual candidate selection.' $Problems}
            foreach($taskBuild in @($taskInside|Where-Object{$_.kind-ceq'build-enter'})){$taskTry=@($taskInside|Where-Object{$_.kind-ceq'try-build-enter'-and$_.callId-eq$taskBuild.parentCallId});Assert-Recurrence ($taskTry.Count-eq1) 'Actual build lacks its native TryBuild parent.' $Problems}
            foreach($taskNote in $taskNotes){$taskBuild=@($taskInside|Where-Object{$_.kind-ceq'build-enter'-and$_.callId-eq$taskNote.parentCallId});Assert-Recurrence ($taskBuild.Count-eq1-and$taskNote.sourceId-eq$taskA.sourceId) 'Recurrence accounting is not nested in the actual source build.' $Problems}
            if($taskBaseline){
                if($taskName-ceq'Q1'-or$taskI-lt5){$taskCount=if($taskName-ceq'Q1'){1}else{$taskI+1};$taskStamp=if($taskName-ceq'Q1'){$taskAfter[0].tick}else{$taskA.tick};Assert-Recurrence ($taskA.counter.anchorPresent-and$taskA.counter.count-eq$taskCount-and$taskA.counter.anchorTick-eq$taskStamp-and$taskA.counter.stackCount-eq5-and-not$taskA.counter.backoffPresent-and-not$taskA.counter.warned) 'Published anchor tally transition mismatch.' $Problems}
                else{Assert-Recurrence (-not$taskA.counter.anchorPresent-and$taskA.counter.backoffPresent-and$taskA.counter.until-eq([long]$taskAfter[5].tick+2500)-and$taskA.counter.warned-and$taskA.counter.backedOff) 'Published sixth-build backoff transition mismatch.' $Problems}
            }else{Assert-Recurrence (-not$taskA.counter.anchorPresent-and-not$taskA.counter.backoffPresent-and-not$taskA.counter.warned-and-not$taskA.counter.backedOff) 'Corrected query still produces recurrence state.' $Problems}
        }
        if($taskName-ceq'Q1'){Assert-Recurrence (@($taskAfter.job.token|Select-Object -Unique).Count-eq1-and@($taskAfter.job.loadId|Select-Object -Unique).Count-eq1) 'Q1 did not reuse one live Job.' $Problems}
        if($taskName-cin@('Q3','M0')){Test-RecurrenceContinuity $taskRows $taskLayout $Problems}
        if($taskName-ceq'Q4'){Test-RecurrencePool $taskRows $taskBefore $taskAfter $Problems}
        $taskWarnings=@($taskMeasured|Where-Object{$_.kind-ceq'warning'});Assert-Recurrence ($taskWarnings.Count-eq$(if($taskBaseline-and$taskName-cne'Q1'){1}else{0})) 'Unexpected query-scoped warning count.' $Problems
        foreach($taskWarning in $taskWarnings){
            Assert-Recurrence ($taskWarning.caller-ceq'fixture-query'-and$taskWarning.queryOrdinal-eq6-and$taskWarning.sourceId-eq$taskLayout.sources[0]-and$taskWarning.actorId-eq$taskAfter[5].actorId-and$taskWarning.warningAttribution-ceq'legacy-note'-and$taskWarning.detail-cmatch' was bulk-hauled 6 times in quick succession without moving \(net-zero\)\. Another mod is very likely returning it to where HD keeps re-hauling it \(a logistics or loadout mod, e\.g\. RimIOT\)\. HD is backing it off its automatic haul scan so pawns stop looping; a forced player order still hauls it\. Please report the mod combination \(issue #214\) if this is unexpected\.$') 'Warning is not the exact attributed published sixth-build diagnostic.' $Problems
            $taskParent=$taskWarning.parentCallId;$taskSeen=@{};$taskFound=$false
            while($taskParent-gt0-and-not$taskSeen.ContainsKey($taskParent)){$taskSeen[$taskParent]=$true;$taskEntry=@($taskMeasured|Where-Object{$_.callId-eq$taskParent-and$_.kind-cin@('has-enter','candidate-enter','try-build-enter','build-enter')});if($taskEntry.Count-ne1){break};$taskEntry=$taskEntry[0];$taskExit=@($taskMeasured|Where-Object{$_.callId-eq$taskParent-and$_.kind-ceq($taskEntry.kind-replace'-enter$','-return')});if($taskExit.Count-ne1){break}
                Assert-Recurrence ($taskEntry.sequence-lt$taskWarning.sequence-and$taskExit[0].sequence-gt$taskWarning.sequence-and$taskEntry.actorId-eq$taskWarning.actorId-and$taskEntry.sourceId-eq$taskWarning.sourceId-and$taskEntry.queryOrdinal-eq6-and$taskEntry.forced-eq$false-and$taskEntry.forceSweep-eq$false) 'Warning not enclosed by its actual source call.' $Problems
                if($taskEntry.kind-ceq'has-enter'){$taskFound=$true;break};$taskParent=$taskEntry.parentCallId}
            Assert-Recurrence $taskFound 'Warning has no complete live Has call ancestry.' $Problems
        }
    }
}
function Test-RecurrencePool($Rows,$Before,$After,$Problems) {
    $taskReturns=@($Rows|Where-Object{$_.kind-ceq'fixture-pool-return'});$taskBorrowed=@($Rows|Where-Object{$_.kind-ceq'fixture-pool-borrow'});$taskReleases=@($Rows|Where-Object{$_.kind-ceq'fixture-pool-release'})
    Assert-Recurrence ($taskReturns.Count-eq5-and$taskBorrowed.Count-ge5-and$taskBorrowed.Count-le5000-and$taskReleases.Count-eq$taskBorrowed.Count-and@($After[0..5].job.loadId|Select-Object -Unique).Count-eq6) 'Q4 bounded FIFO return/borrow/release catalog mismatch.' $Problems
    $taskHeld=@{};$taskHeldIds=@{}
    for($taskCycle=1;$taskCycle-le5;$taskCycle++){
        $taskOriginal=$After[$taskCycle-1].job;$taskBetween=@($Rows|Where-Object{$_.sequence-gt$After[$taskCycle-1].sequence-and$_.sequence-lt$Before[$taskCycle].sequence})
        $taskReturn=Get-RecurrenceOne $taskBetween kind 'fixture-pool-return' $Problems 'Q4 native return';$taskPool=Get-RecurrenceOne $taskBetween kind 'pool-return' $Problems 'Q4 observed return';$taskEntry=Get-RecurrenceOne $taskBetween kind 'pool-enter' $Problems 'Q4 observed return'
        if($null-eq$taskReturn-or$null-eq$taskPool-or$null-eq$taskEntry-or$null-eq$taskReturn.poolOperation){continue};$taskOp=$taskReturn.poolOperation
        Assert-Recurrence ($taskEntry.sequence-lt$taskPool.sequence-and$taskPool.sequence-lt$taskReturn.sequence-and(Test-RecurrenceEqual $taskEntry.job $taskOriginal)-and(Test-RecurrenceEqual $taskPool.inputJob $taskOriginal)-and(Test-RecurrenceEqual $taskReturn.inputJob $taskOriginal)-and(Test-RecurrenceEqual $taskPool.job $taskReturn.job)-and$taskPool.job.token-eq$taskOriginal.token-and$taskPool.job.loadId-eq-1-and$null-eq$taskPool.job.def) 'Q4 actual Clear/return witnesses disagree.' $Problems
        Assert-Recurrence ($taskReturn.caller-ceq'fixture-pool-recycle'-and$taskOp.action-ceq'return-candidate'-and$taskOp.cycle-eq$taskCycle-and$taskOp.targetToken-eq$taskOriginal.token-and$taskOp.priorLoadId-eq$taskOriginal.loadId-and-not$taskOp.targetReused-and$taskOp.beforeCount-ge0-and$taskOp.beforeCount-lt1000-and$taskOp.afterCount-eq($taskOp.beforeCount+1)) 'Q4 return did not enter the actual bounded native FIFO.' $Problems
        $taskBorrows=@($taskBetween|Where-Object{$_.kind-ceq'fixture-pool-borrow'});$taskCount=$taskOp.afterCount;$taskPrevious=$taskReturn.sequence;$taskReused=$null
        Assert-Recurrence ($taskBorrows.Count-ge1-and$taskBorrows.Count-le$taskOp.afterCount) 'Q4 borrowing exceeded the actual post-return pool count.' $Problems
        foreach($taskBorrow in $taskBorrows){
            $taskP=$taskBorrow.poolOperation;$taskJob=$taskBorrow.job;if($null-eq$taskP-or$null-eq$taskJob){Add-RecurrenceProblem $Problems 'Q4 borrowing lacks typed native state.';continue}
            $taskMakes=@($taskBetween|Where-Object{$_.kind-ceq'make-job'-and$_.sequence-gt$taskPrevious-and$_.sequence-lt$taskBorrow.sequence})
            Assert-Recurrence ($taskMakes.Count-eq1-and$taskMakes[0].parentCallId-eq0-and$taskMakes[0].caller-ceq'fixture-pool-recycle'-and$null-eq$taskMakes[0].queryOrdinal-and(Test-RecurrenceEqual $taskMakes[0].job $taskJob)) 'Q4 borrowing is not an actual out-of-query MakeJob return.' $Problems
            Assert-Recurrence ($taskBorrow.caller-ceq'fixture-pool-recycle'-and$null-eq$taskBorrow.queryOrdinal-and$taskP.action-ceq'borrow-unassigned'-and$taskP.cycle-eq$taskCycle-and$taskP.beforeCount-eq$taskCount-and$taskCount-gt0-and$taskP.afterCount-eq($taskCount-1)-and$taskP.targetToken-eq$taskOriginal.token-and$taskP.priorLoadId-eq$taskOriginal.loadId-and$taskJob.token-gt0-and$taskJob.loadId-gt0-and$null-eq$taskJob.def-and-not$taskJob.forced-and$taskJob.queueIds.Count-eq0-and$taskJob.counts.Count-eq0-and-not$taskHeld.ContainsKey([int]$taskJob.token)-and-not$taskHeldIds.ContainsKey([int]$taskJob.loadId)-and$taskP.targetReused-eq($taskJob.token-eq$taskOriginal.token)) 'Q4 borrowing changed pool counts/identity or reused an already held object.' $Problems
            # The JSON shape permits either CLR integer width. Normalize identity
            # keys so equivalent Int32/Int64 values cannot create distinct entries.
            $taskHeld[[int]$taskJob.token]=$taskJob;$taskHeldIds[[int]$taskJob.loadId]=$true;$taskCount=$taskP.afterCount;$taskPrevious=$taskBorrow.sequence
            if($taskP.targetReused){Assert-Recurrence ($null-eq$taskReused-and$taskBorrow.sequence-eq$taskBorrows[-1].sequence-and$taskJob.loadId-ne$taskOriginal.loadId) 'Q4 target reuse is duplicated, stale or followed by unnecessary borrowing.' $Problems;$taskReused=$taskBorrow}
        }
        Assert-Recurrence ($null-ne$taskReused-and$taskCount-eq0) 'Q4 did not reach its actual FIFO-tail candidate before the next query.' $Problems
        if($null-ne$taskReused){$taskCache=$Before[$taskCycle].cache;Assert-Recurrence ($taskCache.entryPresent-and$taskCache.pinnedLoadId-eq$taskOriginal.loadId-and$taskCache.jobState-eq0-and(Test-RecurrenceEqual $taskCache.actualJob $taskReused.job)-and-not$taskHeld.ContainsKey([int]$After[$taskCycle].job.token)-and-not$taskHeldIds.ContainsKey([int]$After[$taskCycle].job.loadId)) 'Q4 lacks the real stale-cache identity or next candidate collides with a held object/native load ID.' $Problems}
        Assert-Recurrence (@($taskBetween|Where-Object{$_.kind-cin@('has-enter','candidate-enter','try-build-enter','build-enter','note-enter','start-attempt','warning')}).Count-eq0) 'Q4 preparation was counted as query/accounting/executed work.' $Problems
    }
    $taskPrevious=$After[-1].sequence
    foreach($taskMake in @($Rows|Where-Object{$_.kind-ceq'make-job'-and$_.caller-ceq'fixture-query'})){Assert-Recurrence ($null-ne$taskMake.job-and@($taskBorrowed|Where-Object{$_.sequence-lt$taskMake.sequence-and($_.job.token-eq$taskMake.job.token-or$_.job.loadId-eq$taskMake.job.loadId)}).Count-eq0) 'Q4 native query allocated an object or native load ID already held outside the pool.' $Problems}
    for($taskI=0;$taskI-lt$taskReleases.Count;$taskI++){
        $taskRelease=$taskReleases[$taskI];$taskP=$taskRelease.poolOperation;if($null-eq$taskP-or$null-eq$taskRelease.inputJob-or$null-eq$taskRelease.job){Add-RecurrenceProblem $Problems 'Q4 cleanup lacks exact owned-job state.';continue}
        Assert-Recurrence ($taskI-lt$taskBorrowed.Count-and(Test-RecurrenceEqual $taskRelease.inputJob $taskBorrowed[$taskI].job)-and$taskRelease.sequence-gt$taskPrevious-and$taskRelease.tick-eq$After[-1].tick-and$taskRelease.caller-ceq'fixture-pool-cleanup'-and$taskP.action-ceq'release-unassigned'-and$taskP.cycle-eq0-and$taskP.targetToken-eq0-and$taskP.priorLoadId-eq0-and-not$taskP.targetReused-and$taskP.beforeCount-ge0-and$taskP.beforeCount-le1000) 'Q4 cleanup did not return exactly the held objects after final query evidence.' $Problems
        if($taskI-gt0){Assert-Recurrence ($taskP.beforeCount-eq$taskReleases[$taskI-1].poolOperation.afterCount) 'Consecutive synchronous Q4 releases have disconnected native pool counts.' $Problems}
        if($taskP.beforeCount-lt1000){Assert-Recurrence ($taskP.afterCount-eq($taskP.beforeCount+1)-and$taskRelease.job.token-eq$taskRelease.inputJob.token-and$taskRelease.job.loadId-eq-1-and$null-eq$taskRelease.job.def) 'Q4 cleanup native enqueue/Clear mismatch.' $Problems}
        else{Assert-Recurrence ($taskP.afterCount-eq1000-and(Test-RecurrenceEqual $taskRelease.job $taskRelease.inputJob)) 'Q4 full pool did not preserve native refusal behavior.' $Problems}
        $taskPrevious=$taskRelease.sequence
    }
    Assert-Recurrence (@($Rows|Where-Object{$_.kind-cnotin@('fixture-pool-return','fixture-pool-borrow','fixture-pool-release')-and$null-ne$_.poolOperation}).Count-eq0) 'Q4 pool payload appears on an unrelated event.' $Problems
}
function Test-RecurrenceHealthyState($S,$H,$Problems,[bool]$Stable=$false) {
    if($S.jobId-gt0){$taskJob=@($H.jobs|Where-Object{$_.id-eq$S.jobId});Assert-Recurrence ($taskJob.Count-eq1-and$taskJob[0].def-ceq$S.jobDef-and$S.tick-ge$taskJob[0].observedTick-and($taskJob[0].endTick-lt0-or$S.tick-le$taskJob[0].endTick)) 'D1 physical state names a job outside its recorded native lifetime.' $Problems}
    else{Assert-Recurrence ($S.jobId-eq-1-and$null-eq$S.jobDef) 'D1 no-current-job state has an invented identity.' $Problems}
    Assert-Recurrence ($S.total-eq10-and$S.elsewhere-eq0-and$S.source-ge0-and$S.high-ge0-and$S.inventory-ge0-and$S.hands-ge0-and$S.total-eq($S.source+$S.high+$S.inventory+$S.hands)-and@($S.things.id|Select-Object -Unique).Count-eq$S.things.Count-and@($S.things|Where-Object{$_.hands}).Count-le1) 'D1 settled totals/identities or native one-stack carry custody are inconsistent.' $Problems
    $taskInventory=0;$taskHands=0;$taskSource=0;$taskHigh=0;$taskElsewhere=0
    foreach($taskThing in $S.things){
        Assert-Recurrence ($taskThing.id-gt0-and$taskThing.count-gt0-and$taskThing.count-le75-and-not$taskThing.destroyed-and([int]$taskThing.spawned+[int]$taskThing.inventory+[int]$taskThing.hands)-eq1) 'D1 live Thing has invalid custody/count.' $Problems
        if($taskThing.inventory){$taskInventory+=$taskThing.count;Assert-Recurrence ($taskThing.holder-ceq'Verse.Pawn_InventoryTracker'-and$null-eq$taskThing.cell) 'D1 inventory holder mismatch.' $Problems}
        elseif($taskThing.hands){$taskHands+=$taskThing.count;Assert-Recurrence ($taskThing.holder-ceq'Verse.Pawn_CarryTracker'-and$null-eq$taskThing.cell) 'D1 carry holder mismatch.' $Problems}
        else{Assert-Recurrence ($taskThing.holder-ceq'Verse.Map') 'D1 spawned holder mismatch.' $Problems;if($taskThing.cell-ceq$H.highCell){$taskHigh+=$taskThing.count}elseif($taskThing.cell-cin$H.sourceCells){$taskSource+=$taskThing.count}else{$taskElsewhere+=$taskThing.count}}
    }
    Assert-Recurrence ($taskInventory-eq$S.inventory-and$taskHands-eq$S.hands-and$taskSource-eq$S.source-and$taskHigh-eq$S.high-and$taskElsewhere-eq$S.elsewhere) 'D1 aggregate counts do not match its actual Thing census.' $Problems
    foreach($taskClaim in $S.claims){Assert-Recurrence ($taskClaim.recordedUnits-gt0-and$taskClaim.recordedUnits-le10-and($taskClaim.group-ceq('critical@'+$H.highCell))-eq$taskClaim.high) 'D1 claim units/group mismatch.' $Problems}
    if($Stable){
        Assert-Recurrence ($S.high-eq10-and$S.source-eq0-and$S.inventory-eq0-and$S.hands-eq0) 'D1 stable state still has cargo away from the high destination.' $Problems
        # Exact installed legacy janitor contract: GameComponentTick runs RunJanitor at tick%120==0.
        # These are immutable recorded rows, not effective cargo-clamped claim units. Observers
        # on the boundary tick can run before or after the janitor; the following tick must be empty.
        $taskReconcileTick=([long][math]::Floor($H.firstUnloadEndTick/120)+1)*120
        if($S.tick-gt$taskReconcileTick){Assert-Recurrence ($S.claims.Count-eq0) 'D1 recorded claims survived the first subsequent native reconciliation boundary.' $Problems}
    }
}
function Test-RecurrenceHealthy($R,$Problems) {
    $taskH=$R.healthyDelivery
    foreach($taskFlag in @('fixtureValid','requestedBehaviorSatisfied','expectationMatched','ordinaryChain','exactPickup','physicalUnload','successfulCleanups','conserved','observerHealthy','layoutIntact','followupWindowComplete','settledHigh','noRehaul')){Assert-Recurrence $taskH.$taskFlag ('D1 lacks '+$taskFlag) $Problems}
    Assert-Recurrence ($taskH.caseId-ceq'L04-B1-HEALTHY'-and$taskH.expectedBehavior-ceq'satisfied'-and$taskH.status-ceq'passed'-and-not$taskH.baselineGapObserved-and-not$taskH.timedOut-and$taskH.startedTick-ge$R.startedTick-and$taskH.finishedTick-eq$R.finishedTick-and$taskH.finishedTick-$taskH.startedTick-lt6000-and$taskH.finishedTick-$taskH.firstUnloadEndTick-ge600-and$taskH.finishedTick-$taskH.stableSinceTick-ge600-and$taskH.stableSinceTick-ge$taskH.firstUnloadEndTick-and$taskH.stableDistinctTicks-ge600) 'D1 result identity/lifetime/stable window mismatch.' $Problems
    Assert-Recurrence ($taskH.originalSourceIds.Count-eq2-and@($taskH.originalSourceIds|Select-Object -Unique).Count-eq2-and$taskH.sourceCells.Count-eq2-and@($taskH.sourceCells|Select-Object -Unique).Count-eq2-and$taskH.highCell-cnotin$taskH.sourceCells) 'D1 source/destination catalog mismatch.' $Problems
    foreach($taskCell in @($taskH.highCell)+@($taskH.sourceCells)){Get-RecurrenceCell $taskCell $Problems|Out-Null}
    Assert-Recurrence (@($taskH.originalSourceIds|Where-Object{$_-in@($R.layouts.sources)}).Count-eq0) 'D1 reused query-scene source identities.' $Problems
    foreach($taskName in @('firstBulkPicked','firstUnloadHigh','minTotal','maxTotal')){Assert-Recurrence ($taskH.$taskName-eq10) ('D1 '+$taskName+' mismatch.') $Problems}
    foreach($taskName in @('firstUnloadSource','firstUnloadElsewhere','sourceZoneNetZeroBulkCycles','laterSourceReacquired','laterNativeHandDelivered')){Assert-Recurrence ($taskH.$taskName-eq0) ('D1 '+$taskName+' is nonzero.') $Problems}
    Assert-Recurrence (@($taskH.jobs.id|Select-Object -Unique).Count-eq$taskH.jobs.Count) 'D1 duplicate job identity.' $Problems
    $taskBulk=@($taskH.jobs|Where-Object{$_.def-ceq'HaulersDream_BulkHaul'});$taskUnload=@($taskH.jobs|Where-Object{$_.def-ceq'HaulersDream_UnloadInventory'})
    if($taskBulk.Count-ne1-or$taskUnload.Count-ne1){Add-RecurrenceProblem $Problems 'D1 needs one actual bulk and unload job.';return};$taskBulk=$taskBulk[0];$taskUnload=$taskUnload[0]
    Assert-Recurrence ($taskBulk.id-eq$taskH.firstBulkJobId-and$taskUnload.id-eq$taskH.firstUnloadJobId-and$taskBulk.id-ne$taskUnload.id-and$taskBulk.driver-ceq'HaulersDream.JobDriver_BulkHaul'-and$taskUnload.driver-ceq'HaulersDream.JobDriver_UnloadHauledInventory'-and$taskBulk.workgiver-ceq'HaulGeneral'-and$taskBulk.workgiverClass-ceq'RimWorld.WorkGiver_HaulGeneral'-and$taskBulk.candidateObserved-and-not$taskBulk.forced-and-not$taskUnload.forced-and$taskBulk.endCondition-ceq'Succeeded'-and$taskUnload.endCondition-ceq'Succeeded'-and$taskBulk.released-and$taskUnload.released-and$taskBulk.observedTick-le$taskBulk.endTick-and$taskBulk.endTick-le$taskUnload.observedTick-and$taskUnload.observedTick-le$taskUnload.endTick-and$taskUnload.endTick-eq$taskH.firstUnloadEndTick) 'D1 native job/cleanup provenance mismatch.' $Problems
    Assert-Recurrence (@($taskH.jobs|Where-Object{$_.def-cin@('HaulToCell','HaulToContainer')}).Count-eq0) 'D1 used native hand-haul rescue.' $Problems
    $taskQueue=@($taskBulk.queue-split',');$taskIds=@();foreach($taskPart in $taskQueue){if($taskPart-cmatch'^([0-9]+)x5$'){$taskIds+=([int]$Matches[1])}else{Add-RecurrenceProblem $Problems 'D1 malformed initial 5+5 queue.'}}
    Assert-Recurrence ($taskIds.Count-eq2-and@($taskIds|Select-Object -Unique).Count-eq2-and@($taskIds|Where-Object{$_-notin$taskH.originalSourceIds}).Count-eq0) 'D1 initial bulk queue does not name both original sources.' $Problems
    $taskPickups=@($taskH.transfers|Where-Object{$_.kind-ceq'bulk-pickup'});Assert-Recurrence ($taskPickups.Count-eq2-and@($taskPickups.original.id|Select-Object -Unique).Count-eq2) 'D1 pickup receipt count/identity mismatch.' $Problems
    for($taskI=0;$taskI-lt$taskPickups.Count;$taskI++){$taskT=$taskPickups[$taskI];Assert-Recurrence ($taskT.jobId-eq$taskBulk.id-and$taskT.returnedSuccess-and$taskT.units-eq5-and$null-ne$taskT.original-and$taskT.original.id-in$taskH.originalSourceIds-and$taskT.original.count-eq5-and-not$taskT.original.destroyed-and$null-eq$taskT.before-and$null-eq$taskT.resulting-and$taskT.after.inventory-eq(5*($taskI+1))-and$taskT.after.source-eq(5*(1-$taskI))-and$taskT.after.high-eq0-and$taskT.after.hands-eq0-and$taskT.after.jobId-eq$taskBulk.id-and$taskT.after.jobDef-ceq$taskBulk.def) 'D1 physical pickup receipt mismatch.' $Problems;Test-RecurrenceHealthyState $taskT.after $taskH $Problems}
    $taskDrops=@($taskH.transfers|Where-Object{$_.kind-ceq'carry-drop'});Assert-Recurrence ($taskDrops.Count-eq1) 'D1 needs exactly one physical deposit.' $Problems
    foreach($taskT in $taskDrops){Assert-Recurrence ($taskT.jobId-eq$taskUnload.id-and$taskT.returnedSuccess-and$taskT.units-eq10-and$null-ne$taskT.original-and$null-ne$taskT.resulting-and$null-ne$taskT.before) 'D1 deposit lacks actual original/resulting custody.' $Problems;if($null-eq$taskT.before-or$null-eq$taskT.resulting){continue}
        Assert-Recurrence ($taskT.before.inventory-eq0-and$taskT.before.hands-eq10-and$taskT.before.source-eq0-and$taskT.before.high-eq0-and$taskT.before.jobId-eq$taskUnload.id-and$taskT.after.jobId-eq$taskUnload.id-and$taskT.before.jobDef-ceq$taskUnload.def-and$taskT.after.jobDef-ceq$taskUnload.def-and$taskT.original.hands-and$taskT.original.count-eq10-and@($taskT.before.things|Where-Object{(Test-RecurrenceEqual $_ $taskT.original)}).Count-eq1-and$taskT.resulting.spawned-and-not$taskT.resulting.destroyed-and$taskT.resulting.count-eq10-and$taskT.resulting.cell-ceq$taskH.highCell-and$taskT.after.high-eq10-and$taskT.after.inventory-eq0-and$taskT.after.hands-eq0-and@($taskT.after.things|Where-Object{(Test-RecurrenceEqual $_ $taskT.resulting)}).Count-eq1) 'D1 drop did not move its actually observed ten-unit hand stack into the high destination.' $Problems
        Test-RecurrenceHealthyState $taskT.before $taskH $Problems;Test-RecurrenceHealthyState $taskT.after $taskH $Problems}
    if($taskPickups.Count-eq2-and$taskDrops.Count-eq1){
        # This fixture has exactly ten units and an empty destination. Its one
        # ten-unit whole-stack drop cannot acquire a new Thing identity on the way.
        $taskDrop=$taskDrops[0]
        Assert-Recurrence ($taskDrop.tick-ge$taskPickups[-1].tick-and$taskDrop.original.id-eq$taskDrop.resulting.id-and@($taskPickups[-1].after.things|Where-Object{$_.inventory-and$_.id-eq$taskDrop.original.id}).Count-eq1) 'D1 deposited identity is disconnected from the preceding real inventory and whole-stack drop.' $Problems
    }
    $taskLastSequence=0
    foreach($taskT in $taskH.transfers){Assert-Recurrence ($taskT.kind-cin@('bulk-pickup','carry-drop','start-carry')-and$taskT.sequence-gt$taskLastSequence-and$taskT.sequence-eq$taskT.after.sequence-and$taskT.tick-eq$taskT.after.tick-and$taskT.tick-ge$taskH.startedTick-and$taskT.tick-le$taskH.finishedTick) 'D1 transfer chronology/type mismatch.' $Problems;$taskLastSequence=$taskT.sequence
        if($null-ne$taskT.before){Assert-Recurrence ($taskT.before.sequence-lt$taskT.sequence-and$taskT.before.tick-eq$taskT.tick-and$taskT.before.jobId-eq$taskT.jobId) 'D1 transfer input is disconnected from its actual synchronous invocation.' $Problems;Test-RecurrenceHealthyState $taskT.before $taskH $Problems}
        Test-RecurrenceHealthyState $taskT.after $taskH $Problems
        if($taskT.kind-ceq'start-carry'){Assert-Recurrence ($null-ne$taskT.before-and$null-eq$taskT.original-and$null-eq$taskT.resulting-and$taskT.units-eq[math]::Max(0,$taskT.after.inventory+$taskT.after.hands-$taskT.before.inventory-$taskT.before.hands)) 'D1 start-carry conservation mismatch.' $Problems}}
    Assert-Recurrence ($taskH.queries.Count-gt0-and$taskH.gates.Count-gt0) 'D1 lacks actual production storage query/cell-gate observations.' $Problems
    $taskOwnClaimWitness=$false
    foreach($taskQ in $taskH.queries){Assert-Recurrence ($taskQ.sequence-eq$taskQ.before.sequence-and$taskQ.tick-eq$taskQ.before.tick-and$taskQ.tick-ge$taskH.startedTick-and$taskQ.tick-le$taskH.finishedTick-and$taskQ.jobId-eq$taskQ.before.jobId-and$taskQ.jobDef-ceq$taskQ.before.jobDef-and$taskQ.free-ge0-and$taskQ.delivering-in@(-1,0,1)-and$taskQ.productionLiveUnits-ge-1-and$taskQ.taggedInventory-ge0-and($taskQ.group-ceq('critical@'+$taskH.highCell))-eq$taskQ.highGroup) 'D1 actual free-query record mismatch.' $Problems;Test-RecurrenceHealthyState $taskQ.before $taskH $Problems
        if($taskQ.highGroup-and$taskQ.subject.inventory-and$taskQ.before.inventory-eq10-and$taskQ.taggedInventory-eq10-and$taskQ.before.source-eq0-and$taskQ.before.high-eq0-and@($taskQ.before.claims|Where-Object{$_.high-and$_.recordedUnits-eq10}).Count-gt0){$taskOwnClaimWitness=$true}}
    Assert-Recurrence ($taskH.ownClaimInventoryWitness-eq$taskOwnClaimWitness) 'D1 own-claim inventory aggregate differs from its actual diagnostic queries.' $Problems
    foreach($taskG in $taskH.gates){Assert-Recurrence ($taskG.sequence-eq$taskG.before.sequence-and$taskG.tick-eq$taskG.before.tick-and$taskG.tick-ge$taskH.startedTick-and$taskG.tick-le$taskH.finishedTick-and$taskG.jobId-eq$taskG.before.jobId-and$taskG.jobDef-ceq$taskG.before.jobDef) 'D1 actual cell-gate context mismatch.' $Problems;Test-RecurrenceHealthyState $taskG.before $taskH $Problems}
    Test-RecurrenceHealthyState $taskH.finalState $taskH $Problems $true
    Assert-Recurrence ($taskH.finalState.tick-eq$taskH.finishedTick-and$taskH.finalState.claims.Count-eq0) 'D1 final state tick or empty recorded-claim ledger mismatch.' $Problems
    $taskD=@($R.events|Where-Object{$_.scene-ceq'D1'});$taskStarts=@($taskD|Where-Object{$_.kind-ceq'start-return'-and$_.actualCurrent-eq$true-and$_.job.loadId-eq$taskBulk.id})
    foreach($taskActual in @($taskD|Where-Object{$_.kind-ceq'start-return'-and$_.actualCurrent-eq$true})){
        $taskJob=@($taskH.jobs|Where-Object{$_.id-eq$taskActual.job.loadId})
        Assert-Recurrence ($taskJob.Count-eq1-and$taskActual.job.def-ceq$taskJob[0].def-and$taskActual.job.driver-ceq$taskJob[0].driver-and$taskActual.job.forced-eq$taskJob[0].forced-and$taskActual.job.workgiver-ceq$taskJob[0].workgiver-and$taskActual.job.workgiverClass-ceq$taskJob[0].workgiverClass-and$taskActual.tick-eq$taskJob[0].observedTick-and$taskActual.job.startTick-eq$taskActual.tick-and(Test-RecurrenceEqual $taskActual.inputJob $taskActual.job @('driver'))) 'D1 actual StartJob descriptor differs from its independent actual-current record.' $Problems
    }
    $taskChain=$false
    foreach($taskStart in $taskStarts){$taskWorks=@($taskD|Where-Object{$_.kind-ceq'native-work-return'-and$_.sequence-lt$taskStart.sequence-and$_.actorId-eq$taskStart.actorId-and$_.returned-eq$true-and$_.job.token-eq$taskStart.job.token-and$_.job.loadId-eq$taskBulk.id})
        foreach($taskWork in $taskWorks){$taskEnter=@($taskD|Where-Object{$_.kind-ceq'native-work-enter'-and$_.callId-eq$taskWork.callId});$taskSelected=@($taskD|Where-Object{$_.kind-ceq'candidate-return'-and$_.parentCallId-eq$taskWork.callId-and$_.actorId-eq$taskStart.actorId-and$_.job.token-eq$taskStart.job.token-and$_.job.loadId-eq$taskBulk.id-and$_.forced-eq$false-and$_.sequence-lt$taskWork.sequence});$taskHas=@($taskD|Where-Object{$_.kind-ceq'has-return'-and$_.parentCallId-eq$taskWork.callId-and$_.actorId-eq$taskStart.actorId-and$_.returned-eq$true-and$_.sequence-lt$taskWork.sequence})
            $taskAttempt=@($taskD|Where-Object{$_.kind-ceq'start-attempt'-and$_.callId-eq$taskStart.callId})
            # Native StartJob initializes startTick and creates the driver; its other
            # recorded descriptor fields must still identify the selected bulk plan.
            $taskDescriptor=$taskAttempt.Count-eq1-and(Test-RecurrenceEqual $taskWork.job $taskAttempt[0].inputJob)-and(Test-RecurrenceEqual $taskAttempt[0].inputJob $taskStart.inputJob @('startTick'))-and(Test-RecurrenceEqual $taskStart.inputJob $taskStart.job @('driver'))
            $taskQueueExact=($taskStart.job.queueIds-join',')-ceq($taskIds-join',')-and($taskStart.job.counts-join',')-ceq'5,5'
            $taskSelectedExact=@($taskSelected|Where-Object{Test-RecurrenceEqual $_.job $taskWork.job @('workgiver','workgiverClass')}).Count-ge1
            if($taskEnter.Count-eq1-and$taskSelectedExact-and$taskHas.Count-ge1-and$taskDescriptor-and$taskQueueExact-and$taskStart.job.def-ceq$taskBulk.def-and$taskStart.job.driver-ceq$taskBulk.driver-and$taskStart.job.workgiver-ceq$taskBulk.workgiver-and$taskStart.job.workgiverClass-ceq$taskBulk.workgiverClass-and$taskStart.job.startTick-eq$taskStart.tick-and$taskStart.inputJob.startTick-eq$taskStart.tick-and$taskStart.tick-eq$taskBulk.observedTick-and$taskAttempt[0].sequence-gt$taskWork.sequence-and$taskWork.tick-eq$taskStart.tick-and-not$taskStart.job.forced-and$taskStart.tick-le$taskPickups[0].tick){$taskChain=$true}}
    };Assert-Recurrence $taskChain 'D1 lacks the actual native query/selection/start chain for its executed bulk job.' $Problems
}
function Test-RecurrenceEvidence($Value,[string]$ExpectedBehavior,$Problems) {
    try {
        if(-not(Test-RecurrenceShape $Value $Problems)){return};$taskR=$Value.candidateRecurrence
        Assert-Recurrence ($ExpectedBehavior-cin@('baseline-gap','satisfied')-and$taskR.expectedBehavior-ceq$ExpectedBehavior-and$taskR.caseId-ceq'L04-B1'-and$taskR.status-ceq'partial'-and$Value.status-ceq'partial'-and$taskR.fixtureValid-and$taskR.expectationMatched-and$taskR.queryControlsComplete-and$taskR.healthyDeliveryComplete-and-not$taskR.requestedBehaviorSatisfied-and$null-eq$taskR.error-and$taskR.startedTick-ge0-and$taskR.finishedTick-gt$taskR.startedTick) 'Result is not a complete implemented partial-scope capture.' $Problems
        Assert-Recurrence ($taskR.implementedBehaviorSatisfied-eq($ExpectedBehavior-ceq'satisfied')-and$taskR.baselineGapObserved-eq($ExpectedBehavior-ceq'baseline-gap')) 'Implemented/baseline outcome contradicts selected mode.' $Problems
        Assert-Recurrence ($taskR.incompleteControls.Count-eq1-and$taskR.incompleteControls[0]-ceq'D2 in-flight query burst is not implemented in this first staged fixture; it remains unexercised, not passed.') 'D2 incomplete scope was lost or changed.' $Problems
        Assert-Recurrence ($taskR.counterContract-ceq'legacy-anchor-tuple-v1;threshold6;gap180;backoff2500;exact-fields-required') 'Unknown legacy counter reader contract.' $Problems
        Assert-Recurrence ($taskR.layouts.Count-eq5-and(($taskR.layouts.scene-join',')-ceq'Q1,Q2,Q3,Q4,M0')) 'Query layout catalog/order mismatch.' $Problems
        $taskAllIds=@();$taskAllActors=@()
        foreach($taskL in $taskR.layouts){Assert-Recurrence ($taskL.sources.Count-eq2-and$taskL.actors.Count-eq$(if($taskL.scene-ceq'Q2'){6}else{1})-and$taskL.sourceCells.Count-eq3-and@($taskL.sourceCells|Select-Object -Unique).Count-eq3) 'Layout item/actor/cell count mismatch.' $Problems;$taskAllIds+=@($taskL.sources);$taskAllActors+=@($taskL.actors)
            foreach($taskCell in @($taskL.highCell)+@($taskL.sourceCells)){Get-RecurrenceCell $taskCell $Problems|Out-Null};Test-RecurrenceQueryPhysical $taskL.initial $taskL $Problems
            $taskReady=@($taskR.events|Where-Object{$_.kind-ceq'scene-ready'-and$_.scene-ceq$taskL.scene});Assert-Recurrence ($taskReady.Count-eq1-and(Test-RecurrenceEqual $taskReady[0].physical $taskL.initial)) 'Layout initial state differs from actual scene-ready capture.' $Problems}
        Assert-Recurrence (@($taskAllIds|Select-Object -Unique).Count-eq10-and@($taskAllActors|Select-Object -Unique).Count-eq10) 'Scenes share item/actor identities.' $Problems
        $taskCatalog=Get-RecurrenceAssertionCatalog $taskR
        Assert-Recurrence ($taskR.assertions.Count-eq$taskCatalog.Count-and@($taskR.assertions.id|Select-Object -Unique).Count-eq$taskR.assertions.Count) 'Source-derived assertion catalog cardinality mismatch.' $Problems
        foreach($taskAssertion in $taskR.assertions){$taskExpected=$ExpectedBehavior-cne'baseline-gap'-or$taskAssertion.id-cnotin@('Q1-no-query-recurrence','Q2-no-query-recurrence','Q3-no-query-recurrence','Q4-no-query-recurrence','M0-no-query-recurrence');Assert-Recurrence ($taskCatalog.ContainsKey($taskAssertion.id)-and$taskAssertion.kind-ceq$taskCatalog[$taskAssertion.id]-and$taskAssertion.passed-eq$taskExpected) ('Unexpected/missing assertion outcome '+$taskAssertion.id) $Problems}
        $taskSequence=0;$taskTick=$taskR.startedTick
        $taskKinds=@('observer-bind','scene-ready','await-natural-idle','natural-idle-ready','query-before','query-after','boundary-continuity','fixture-relocation','fixture-phase-reset','query-scene-final','fixture-zone-retirement','fixture-pool-return','fixture-pool-borrow','fixture-pool-release','warning','pool-enter','pool-return','make-job','has-enter','has-return','candidate-enter','candidate-return','try-build-enter','try-build-return','build-enter','build-return','note-enter','note-return','native-work-enter','native-work-return','start-attempt','start-return')
        foreach($taskE in $taskR.events){Assert-Recurrence ($taskE.sequence-eq($taskSequence+1)-and$taskE.tick-ge$taskTick-and$taskE.tick-le$taskR.finishedTick-and$taskE.kind-cin$taskKinds-and$taskE.scene-cin@('setup','Q1','Q2','Q3','Q4','M0','D1')) 'Unexpected event or event sequence/tick gap.' $Problems;$taskSequence=$taskE.sequence;$taskTick=$taskE.tick}
        Assert-Recurrence (@($taskR.events|Where-Object{$_.kind-ceq'warning'}).Count-eq$(if($ExpectedBehavior-ceq'baseline-gap'){4}else{0})) 'Unclassified or missing warnings outside the exact query catalog.' $Problems
        Assert-Recurrence (@($taskR.events|Where-Object{$_.scene-cne'Q4'-and($null-ne$_.poolOperation-or$_.kind-clike'fixture-pool-*')}).Count-eq0) 'Fixture pool work occurred outside Q4.' $Problems
        foreach($taskE in $taskR.events){if($null-ne$taskE.counter){$taskSource=$taskE.sourceId;if($taskE.kind-ceq'scene-ready'){$taskL=Get-RecurrenceOne $taskR.layouts scene $taskE.scene $Problems 'Scene counter';if($null-ne$taskL){$taskSource=$taskL.sources[0]}};Test-RecurrenceCounter $taskE.counter $taskSource $taskE.tick $Problems}}
        Test-RecurrenceRetirements $taskR $Problems
        Test-RecurrenceCalls $taskR.events $Problems;Test-RecurrenceQueries $taskR $ExpectedBehavior $Problems;Test-RecurrenceHealthy $taskR $Problems
        Test-RecurrenceBindings $Value $Problems
    }catch{Add-RecurrenceProblem $Problems ('Evidence cannot be verified: '+$_.Exception.Message)}
}
function Test-RecurrenceRetirements($R,$Problems) {
    $taskReset=Get-RecurrenceOne $R.events kind 'fixture-phase-reset' $Problems 'B1 retirement';if($null-eq$taskReset){return}
    $taskEnds=@($R.events|Where-Object{$_.kind-ceq'query-scene-final'});$taskRows=@($R.events|Where-Object{$_.kind-ceq'fixture-zone-retirement'})
    Assert-Recurrence ($taskEnds.Count-eq5-and$taskRows.Count-eq5-and@($taskRows.zoneRetirement.zoneId|Select-Object -Unique).Count-eq5) 'Missing/duplicate owned zone retirement.' $Problems
    $taskPrevious=$taskReset.sequence
    foreach($taskL in $R.layouts){$taskEnd=@($taskEnds|Where-Object{$_.scene-ceq$taskL.scene});$taskRowsForScene=@($taskRows|Where-Object{$_.scene-ceq$taskL.scene})
        if($taskEnd.Count-ne1-or$taskRowsForScene.Count-ne1){Add-RecurrenceProblem $Problems 'Scene lacks its unique final/retirement pair.';continue};$taskEnd=$taskEnd[0];$taskE=$taskRowsForScene[0];$taskZ=$taskE.zoneRetirement
        if($null-eq$taskZ){Add-RecurrenceProblem $Problems 'Retirement lacks typed state.';continue}
        Assert-Recurrence ($taskEnd.sequence-gt$taskPrevious-and$taskE.sequence-gt$taskEnd.sequence-and$taskE.tick-eq$taskEnd.tick-and$taskE.tick-le$R.healthyDelivery.startedTick-and$taskE.detail-ceq'Native Delete(false) after final query evidence; fixture retirement, not haul progress.') 'Owned retirement chronology/operation mismatch.' $Problems;$taskPrevious=$taskE.sequence
        Test-RecurrenceQueryPhysical $taskEnd.physical $taskL $Problems
        Assert-Recurrence ($taskZ.zoneId-cmatch'^Zone_[0-9]+$'-and$taskZ.beforeGridZone-ceq$taskZ.zoneId-and$taskZ.cell-ceq$taskL.highCell-and$taskZ.beforeCells.Count-eq1-and$taskZ.beforeCells[0]-ceq$taskL.highCell-and$taskZ.beforeRegistered-and$taskZ.beforeSlotMatches-and$taskZ.afterCells.Count-eq0-and$null-eq$taskZ.afterGridZone-and-not$taskZ.afterRegistered-and$taskZ.afterSlotAbsent-and$taskZ.afterGroupRemoved) 'Retirement did not remove the original one-cell zone through all native registries.' $Problems
    }
    Assert-Recurrence (@($R.events|Where-Object{$_.kind-ceq'query-after'-and$_.sequence-ge$taskReset.sequence}).Count-eq0) 'Query execution continued after scene retirement began.' $Problems
    Assert-Recurrence (@($R.events|Where-Object{$_.kind-cne'fixture-zone-retirement'-and$null-ne$_.zoneRetirement}).Count-eq0) 'Retirement payload attached to an unrelated event.' $Problems
}
function Test-RecurrenceExactMember($Assemblies,[string]$Name,[long]$Token,[string]$Kind,$Problems) {
    # Independently resolved from the immutable 0077 copied modules, including all
    # 12 bridge, 10 query and 17 D1 target records. Unknown modules require a fresh
    # metadata review and explicit catalog update. No recorded token/name is trusted
    # as its own proof, and this pure consumer never loads or executes a DLL.
    $taskNative=$Name.StartsWith('RimWorld.')-or$Name.StartsWith('Verse.')
    $taskModule=if($taskNative){'Assembly-CSharp'}else{'HaulersDream'}
    $taskExpectedHash=if($taskNative){'5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A'}else{'D55644ACBE064F4F6A37C82205F48EEAF61264100C92AC568BDCB612FE8DCC2D'}
    $taskExpectedMvid=if($taskNative){'61e41735-6189-4da4-9d21-0260257b5097'}else{'05629205-9454-4960-bd33-8ef04c74d4b2'}
    $taskMembers=@{
        'HaulersDream.HaulChurnGuard.sync'=@(67109201);'HaulersDream.HaulChurnGuard.bulkAnchors'=@(67109205)
        'HaulersDream.HaulChurnGuard.backoffUntil'=@(67109202);'HaulersDream.HaulChurnGuard.loopWarned'=@(67109206)
        'HaulersDream.HaulChurnGuard.thingFails'=@(67109203);'HaulersDream.BulkHaul.cacheTick'=@(67108878)
        'HaulersDream.BulkHaul.TryBuildBulkJob'=@(100663330);'HaulersDream.BulkHaul.BuildBulkJob'=@(100663339)
        'HaulersDream.HaulChurnGuard.NoteBulkAnchor'=@(100663894);'HaulersDream.HaulChurnGuard.IsBackedOff'=@(100663897)
        'HaulersDream.Patch_WorkGiver_HaulGeneral_BulkHaul.Postfix'=@(100663325)
        'HaulersDream.Patch_WorkGiver_HaulGeneral_ChurnBackoff.Postfix'=@(100663900)
        'HaulersDream.HDLog.Warn'=@(100663996)
        'HaulersDream.StorageCommitments.FreeUnitsFor'=@(100665063);'HaulersDream.StorageCommitments.IsDelivering'=@(100665064)
        'HaulersDream.StorageCommitments.UnitsMoving'=@(100665073);'HaulersDream.JobDriver_BulkHaul.DepositSwept'=@(100664322)
        'RimWorld.WorkGiver_Scanner.HasJobOnThing'=@(100696537);'RimWorld.WorkGiver_HaulGeneral.JobOnThing'=@(100697227)
        'RimWorld.JobGiver_Work.TryIssueJobPackage'=@(100696489);'RimWorld.StoreUtility.IsGoodStoreCell'=@(100696521)
        'Verse.AI.Pawn_JobTracker.StartJob'=@(100688842);'Verse.AI.Pawn_JobTracker.CleanupCurrentJob'=@(100688846)
        'Verse.JobMaker.MakeJob'=@(100666474);'Verse.JobMaker.ReturnToPool'=@(100666481)
        'Verse.AI.JobDriver.DriverTick'=@(100687857);'Verse.AI.JobDriver.DriverTickInterval'=@(100687858)
        'Verse.AI.JobDriver.TryActuallyStartNextToil'=@(100687860)
        'Verse.Pawn_CarryTracker.TryStartCarry'=@(100676343,100676344);'Verse.Pawn_CarryTracker.TryDropCarriedThing'=@(100676346,100676347)
        'Verse.Thing.SplitOff'=@(100678050);'Verse.Thing.TryAbsorbStack'=@(100678049);'Verse.ThingWithComps.TryAbsorbStack'=@(100678355)
    }
    $taskAssembly=@($Assemblies|Where-Object{$_.name-ceq$taskModule})
    Assert-Recurrence ($taskAssembly.Count-eq1-and$taskAssembly[0].sha256-eq$taskExpectedHash-and$taskAssembly[0].moduleVersionId-eq$taskExpectedMvid) ('Unreviewed '+$taskModule+' module for exact B1 member bindings.') $Problems
    $taskTable=if($Kind-ceq'field'){4}elseif($Kind-ceq'method'){6}else{-1}
    Assert-Recurrence ($taskMembers.ContainsKey($Name)-and$Token-in$taskMembers[$Name]-and($Token-shr24)-eq$taskTable) ('Token does not identify the independently reviewed '+$Kind+' '+$Name) $Problems
}
function Test-RecurrenceBindings($Value,$Problems) {
    $taskR=$Value.candidateRecurrence;$taskAssemblies=@{}
    foreach($taskA in $Value.assemblies){
        Assert-Recurrence ($taskA.name-is[string]-and$taskA.path-is[string]-and$taskA.sha256-is[string]-and$taskA.sha256-cmatch'^[A-Fa-f0-9]{64}$'-and$taskA.assemblyVersion-is[string]-and$taskA.assemblyVersion-match'^\d+\.\d+\.\d+\.\d+$'-and$taskA.moduleVersionId-is[string]-and$taskA.moduleVersionId-match'^[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}$') 'Malformed loaded assembly identity.' $Problems
        if($taskAssemblies.ContainsKey($taskA.name)){Add-RecurrenceProblem $Problems 'Duplicate loaded assembly.'}else{$taskAssemblies[$taskA.name]=$taskA}}
    $taskNames=@('Assembly-CSharp','UnityEngine.CoreModule','0Harmony','HarmonyMod','HaulersDream','HaulersDream.Core','HaulersDream.RuntimeHarness')
    Assert-Recurrence ($taskAssemblies.Count-eq7-and@($taskNames|Where-Object{-not$taskAssemblies.ContainsKey($_)}).Count-eq0) 'Native B1 loaded assembly catalog mismatch.' $Problems
    if(@($taskNames|Where-Object{-not$taskAssemblies.ContainsKey($_)}).Count-gt0){return}
    $taskHd=$taskAssemblies['HaulersDream'];$taskNeeded=@{}
    foreach($taskField in @('sync','bulkAnchors','backoffUntil','loopWarned','thingFails')){$taskNeeded['HaulersDream.HaulChurnGuard.'+$taskField]='field'};$taskNeeded['HaulersDream.BulkHaul.cacheTick']='field'
    foreach($taskMethod in @('HaulersDream.BulkHaul.TryBuildBulkJob','HaulersDream.BulkHaul.BuildBulkJob','HaulersDream.HaulChurnGuard.NoteBulkAnchor','HaulersDream.HaulChurnGuard.IsBackedOff')){$taskNeeded[$taskMethod]='method'}
    foreach($taskType in @('HaulersDream.Patch_WorkGiver_HaulGeneral_BulkHaul','HaulersDream.Patch_WorkGiver_HaulGeneral_ChurnBackoff')){$taskNeeded['installed='+$taskType+'.Postfix']='method'}
    $taskSeen=@{};$taskAssemblySeen=@{}
    foreach($taskBinding in $taskR.bindings){
        if($taskBinding-match'^(.*);path=(.*);mvid=([a-fA-F0-9-]+)$'){
            $taskFull=$Matches[1];$taskPath=$Matches[2];$taskMvid=$Matches[3];$taskName=($taskFull-split',')[0]
            Assert-Recurrence ($taskName-cin@('HaulersDream','HaulersDream.Core','Assembly-CSharp','HaulersDream.RuntimeHarness')-and$taskAssemblies.ContainsKey($taskName)-and-not$taskAssemblySeen.ContainsKey($taskName)) 'Unknown/duplicate production bridge assembly binding.' $Problems
            if($taskAssemblies.ContainsKey($taskName)){$taskA=$taskAssemblies[$taskName];Assert-Recurrence ($taskPath-ceq$taskA.path-and$taskMvid-eq$taskA.moduleVersionId-and$taskFull-ceq($taskName+', Version='+$taskA.assemblyVersion+', Culture=neutral, PublicKeyToken=null')) 'Bridge binding does not match the actual loaded assembly.' $Problems};$taskAssemblySeen[$taskName]=$true
        }elseif($taskBinding-match'^([^;]+);token=([0-9]+)(?:;mvid=([a-fA-F0-9-]+))?$'){
            $taskName=$Matches[1];$taskToken=[long]$Matches[2];$taskMvid=$Matches[3]
            Assert-Recurrence ($taskNeeded.ContainsKey($taskName)-and-not$taskSeen.ContainsKey($taskName)) 'Unexpected/duplicate exact field/method binding.' $Problems
            if($taskNeeded.ContainsKey($taskName)){$taskTable=if($taskNeeded[$taskName]-ceq'field'){4}else{6};Assert-Recurrence (($taskToken-shr24)-eq$taskTable-and($taskToken-band16777215)-gt0-and($taskTable-eq4-or$taskMvid-eq$taskHd.moduleVersionId)) 'Binding token table/module mismatch.' $Problems;Test-RecurrenceExactMember $Value.assemblies ($taskName-replace'^installed=','') $taskToken $taskNeeded[$taskName] $Problems};$taskSeen[$taskName]=$true
        }else{Add-RecurrenceProblem $Problems 'Unrecognized production binding text.'}
    };Assert-Recurrence ($taskSeen.Count-eq12-and$taskAssemblySeen.Count-eq4-and$taskR.bindings.Count-eq16) 'Production binding catalog is incomplete.' $Problems
    $taskMethods=@{'RimWorld.WorkGiver_Scanner.HasJobOnThing'='Assembly-CSharp';'RimWorld.WorkGiver_HaulGeneral.JobOnThing'='Assembly-CSharp';'HaulersDream.BulkHaul.TryBuildBulkJob'='HaulersDream';'HaulersDream.BulkHaul.BuildBulkJob'='HaulersDream';'HaulersDream.HaulChurnGuard.NoteBulkAnchor'='HaulersDream';'HaulersDream.HDLog.Warn'='HaulersDream';'Verse.AI.Pawn_JobTracker.StartJob'='Assembly-CSharp';'RimWorld.JobGiver_Work.TryIssueJobPackage'='Assembly-CSharp';'Verse.JobMaker.MakeJob'='Assembly-CSharp';'Verse.JobMaker.ReturnToPool'='Assembly-CSharp'}
    $taskBinds=@($taskR.events|Where-Object{$_.kind-ceq'observer-bind'});Assert-Recurrence ($taskBinds.Count-eq10-and@($taskBinds.method|Select-Object -Unique).Count-eq10) 'B1 observer target catalog mismatch.' $Problems
    foreach($taskBind in $taskBinds){Assert-Recurrence ($taskMethods.ContainsKey($taskBind.method)) 'Unknown B1 observer method.' $Problems;if(-not$taskMethods.ContainsKey($taskBind.method)){continue};$taskA=$taskAssemblies[$taskMethods[$taskBind.method]]
        Assert-Recurrence ($taskBind.detail-match'^(.*);mvid=([a-fA-F0-9-]+);token=([0-9]+)$') 'Malformed observer module/token binding.' $Problems
        if($taskBind.detail-match'^(.*);mvid=([a-fA-F0-9-]+);token=([0-9]+)$'){$taskToken=[long]$Matches[3];Assert-Recurrence ($Matches[1]-ceq($taskA.name+', Version='+$taskA.assemblyVersion+', Culture=neutral, PublicKeyToken=null')-and$Matches[2]-eq$taskA.moduleVersionId-and($taskToken-shr24)-eq6) 'Observer binding differs from loaded assembly.' $Problems;Test-RecurrenceExactMember $Value.assemblies $taskBind.method $taskToken method $Problems}}
    Assert-Recurrence ($Value.mods-is[array]-and$Value.mods.Count-eq4-and(($Value.mods.packageId|ForEach-Object{$_.ToLowerInvariant()})-join',')-ceq'brrainz.harmony,ludeon.rimworld,giwaffed.haulersdream,giwaffed.haulersdream.runtimeharness') 'Unexpected active B1 mod set/order.' $Problems
}
function Read-RecurrenceJson([string]$Text,$Problems,[string]$Label) {
    try{return ConvertFrom-Json -InputObject $Text -ErrorAction Stop}catch{Add-RecurrenceProblem $Problems ($Label+' contains malformed JSON.');return $null}
}
function Get-RecurrenceHealthyLifetimes($Events,$Problems) {
    $taskLife=@{};$taskOpen=$null
    foreach($taskE in @($Events|Where-Object{$_.phase-cin@('l04-b1-healthy-current-job','l04-b1-healthy-job-cleanup')})){
        $taskJob=Read-RecurrenceJson $taskE.detail $Problems 'D1 lifetime'
        if(-not(Test-RecurrenceNode $taskJob hjob 'D1 lifetime job' (Get-RecurrenceSchemas) $Problems)){continue}
        if($taskE.phase-ceq'l04-b1-healthy-current-job'){
            Assert-Recurrence (-not$taskLife.ContainsKey([int]$taskJob.id)) 'D1 job has multiple actual-current creation events.' $Problems
            Assert-Recurrence ($null-eq$taskOpen) 'D1 observes a new current job before its preceding current job is cleaned up.' $Problems;$taskOpen=[int]$taskJob.id
            $taskLife[[int]$taskJob.id]=@{start=$taskE.sequence;end=$null;job=$taskJob}
        }else{
            if(-not$taskLife.ContainsKey([int]$taskJob.id)){Add-RecurrenceProblem $Problems 'D1 cleanup precedes its actual-current observation.';continue}
            Assert-Recurrence ($null-ne$taskOpen-and$taskOpen-eq$taskJob.id) 'D1 cleanup does not close the actually current job.' $Problems;$taskOpen=$null
            $taskL=$taskLife[[int]$taskJob.id];Assert-Recurrence ($null-eq$taskL.end-and$taskE.sequence-gt$taskL.start) 'D1 cleanup is duplicated or precedes its actual start.' $Problems;$taskL.end=$taskE.sequence
        }
    }
    return $taskLife
}
function Test-RecurrenceHealthyLifetime($State,$Life,[int]$Sequence,$Problems) {
    if($null-eq$State){return}
    $taskActive=@($Life.Values|Where-Object{$Sequence-gt$_.start-and($null-eq$_.end-or$Sequence-lt$_.end)})
    if($State.jobId-gt0){
        if(-not$Life.ContainsKey([int]$State.jobId)){Add-RecurrenceProblem $Problems 'D1 state lacks its actual-current job event.';return}
        $taskL=$Life[[int]$State.jobId]
        Assert-Recurrence ($taskActive.Count-eq1-and$taskActive[0].job.id-eq$State.jobId-and$State.jobDef-ceq$taskL.job.def-and$Sequence-gt$taskL.start-and($null-eq$taskL.end-or$Sequence-lt$taskL.end)) 'D1 state/transfer contradicts the actual current-job lifetime.' $Problems
    }else{Assert-Recurrence ($State.jobId-eq-1-and$null-eq$State.jobDef-and$taskActive.Count-eq0) 'D1 state omits a job that is still actually current.' $Problems}
}
function Test-RecurrenceAncestryThing($Thing,$Problems) {
    if($null-eq$Thing){Add-RecurrenceProblem $Problems 'Ancestry lacks its physical Thing descriptor.';return}
    $taskCustody=[int]$Thing.spawned+[int]$Thing.inventory+[int]$Thing.hands
    Assert-Recurrence ($Thing.id-gt0-and$Thing.count-ge0-and$Thing.count-le75-and$taskCustody-le1-and($Thing.destroyed-or$Thing.count-gt0)) 'Ancestry has an invalid Thing identity/count/custody combination.' $Problems
    if($Thing.spawned){Assert-Recurrence (-not$Thing.destroyed-and$Thing.holder-ceq'Verse.Map'-and$null-ne$Thing.cell) 'Spawned ancestry lacks its map/cell custody.' $Problems;Get-RecurrenceCell $Thing.cell $Problems|Out-Null}
    elseif($Thing.inventory){Assert-Recurrence (-not$Thing.destroyed-and$Thing.holder-ceq'Verse.Pawn_InventoryTracker'-and$null-eq$Thing.cell) 'Inventory ancestry contradicts its physical holder/cell.' $Problems}
    elseif($Thing.hands){Assert-Recurrence (-not$Thing.destroyed-and$Thing.holder-ceq'Verse.Pawn_CarryTracker'-and$null-eq$Thing.cell) 'Carried ancestry contradicts its physical holder/cell.' $Problems}
    else{Assert-Recurrence ($null-eq$Thing.holder-and$null-eq$Thing.cell) 'Detached/destroyed ancestry invents a holder or map cell.' $Problems}
}
function Test-RecurrenceHealthyRaw($R,$Events,$Problems,$Assemblies) {
    $taskH=$R.healthyDelivery;$taskSchemas=Get-RecurrenceSchemas
    $taskLifetimes=Get-RecurrenceHealthyLifetimes $Events $Problems
    $taskFixture=Get-RecurrenceOne $Events phase 'l04-b1-healthy-fixture' $Problems 'D1 fixture'
    if($null-ne$taskFixture){
        $taskPattern='^case=L04-B1-HEALTHY; actor=Human([0-9]+); source='+[regex]::Escape(($taskH.sourceCells-join','))+'; high='+[regex]::Escape($taskH.highCell)+'; ordinary workgiver only; no runtime job/claim injection; first unload then at least600 stable ticks; healthy on both selections\.$'
        Assert-Recurrence ($taskFixture.detail-cmatch$taskPattern-and$taskFixture.tick-eq$taskH.startedTick) 'D1 fixture identity/layout payload mismatch.' $Problems
        if($taskFixture.detail-cmatch$taskPattern){$taskActor=[int]$Matches[1];Assert-Recurrence ($taskActor-notin@($R.layouts.actors)-and@($R.events|Where-Object{$_.scene-ceq'D1'-and$_.actorId-gt0-and$_.actorId-ne$taskActor}).Count-eq0) 'D1 dispatch uses a different/reused actor.' $Problems}
    }
    $taskExpectedBindings=@{'RimWorld.WorkGiver_HaulGeneral.JobOnThing'=1;'HaulersDream.StorageCommitments.FreeUnitsFor'=1;'HaulersDream.StorageCommitments.IsDelivering'=1;'HaulersDream.StorageCommitments.UnitsMoving'=1;'RimWorld.StoreUtility.IsGoodStoreCell'=1;'HaulersDream.JobDriver_BulkHaul.DepositSwept'=1;'Verse.AI.Pawn_JobTracker.CleanupCurrentJob'=1;'Verse.AI.JobDriver.DriverTick'=1;'Verse.AI.JobDriver.DriverTickInterval'=1;'Verse.AI.JobDriver.TryActuallyStartNextToil'=1;'Verse.Pawn_CarryTracker.TryStartCarry'=2;'Verse.Pawn_CarryTracker.TryDropCarriedThing'=2;'Verse.Thing.SplitOff'=1;'Verse.Thing.TryAbsorbStack'=1;'Verse.ThingWithComps.TryAbsorbStack'=1}
        $taskSeenBindings=@{};$taskTokens=@{};$taskBinds=@($Events|Where-Object{$_.phase-ceq'l04-b1-healthy-observer-bind'})
    foreach($taskBind in $taskBinds){
        if($taskBind.detail-cnotmatch'^([^;]+); token=([0-9]+); assembly=(.*); MVID=([a-fA-F0-9-]+)$'){Add-RecurrenceProblem $Problems 'Malformed D1 observer binding.';continue}
        $taskMethod=$Matches[1];$taskToken=[long]$Matches[2];$taskFull=$Matches[3];$taskMvid=$Matches[4];$taskName=if($taskMethod.StartsWith('HaulersDream.')){'HaulersDream'}else{'Assembly-CSharp'}
        $taskAssembly=Get-RecurrenceOne $Assemblies name $taskName $Problems 'D1 loaded binding'
        Assert-Recurrence ($taskExpectedBindings.ContainsKey($taskMethod)-and($taskToken-shr24)-eq6-and-not$taskTokens.ContainsKey($taskMvid+'/'+$taskToken)-and$null-ne$taskAssembly-and$taskMvid-eq$taskAssembly.moduleVersionId-and$taskFull-ceq($taskName+', Version='+$taskAssembly.assemblyVersion+', Culture=neutral, PublicKeyToken=null')) 'D1 observer method/token/loaded module mismatch.' $Problems
        Test-RecurrenceExactMember $Assemblies $taskMethod $taskToken method $Problems
        $taskTokens[$taskMvid+'/'+$taskToken]=$true;if(-not$taskSeenBindings.ContainsKey($taskMethod)){$taskSeenBindings[$taskMethod]=0};$taskSeenBindings[$taskMethod]++
    }
    Assert-Recurrence ($taskBinds.Count-eq17) 'D1 observer binding count mismatch.' $Problems
    foreach($taskMethod in $taskExpectedBindings.Keys){Assert-Recurrence ($taskSeenBindings.ContainsKey($taskMethod)-and$taskSeenBindings[$taskMethod]-eq$taskExpectedBindings[$taskMethod]) ('D1 missing observer overload '+$taskMethod) $Problems}
    $taskBulkCandidates=0
    foreach($taskRow in @($Events|Where-Object{$_.phase-ceq'l04-b1-healthy-candidate'})){
        if($taskRow.detail-cnotmatch'^id=([0-9]+); def=([^;]+); source=([0-9]+); forcedArgument=(True|False); queue=(.*)$'){Add-RecurrenceProblem $Problems 'Malformed D1 native candidate record.';continue}
        $taskId=[int]$Matches[1];$taskDef=$Matches[2];$taskSource=[int]$Matches[3];$taskForced=$Matches[4];$taskQueue=$Matches[5]
        $taskReturns=@($R.events|Where-Object{$_.scene-ceq'D1'-and$_.kind-ceq'candidate-return'-and$_.tick-eq$taskRow.tick-and$null-ne$_.job-and$_.job.loadId-eq$taskId-and$_.sourceId-eq$taskSource})
        Assert-Recurrence ($taskReturns.Count-ge1-and$taskSource-in$taskH.originalSourceIds-and$taskForced-ceq'False') 'D1 candidate lacks matching native query return/source.' $Problems
        if($taskId-eq$taskH.firstBulkJobId){$taskBulkCandidates++;$taskJob=Get-RecurrenceOne $taskH.jobs id $taskId $Problems 'D1 actual candidate';Assert-Recurrence ($taskDef-ceq'HaulersDream_BulkHaul'-and$null-ne$taskJob-and$taskQueue-ceq$taskJob.queue-and$taskRow.tick-le$taskJob.observedTick) 'D1 candidate identity/queue differs from the actually executed first job.' $Problems}
    }
    Assert-Recurrence ($taskBulkCandidates-ge1) 'D1 lacks the independent healthy observer candidate witness.' $Problems
    foreach($taskPair in @(@('bulk-pickup','bulk-pickup'),@('carry-drop','physical-deposit'),@('start-carry','carry-transfer'))){
        $taskRows=@($Events|Where-Object{$_.phase-ceq('l04-b1-healthy-'+$taskPair[1])});$taskExpected=@($taskH.transfers|Where-Object{$_.kind-ceq$taskPair[0]})
        Assert-Recurrence ($taskRows.Count-eq$taskExpected.Count) 'Raw D1 transfer cardinality mismatch.' $Problems
        for($taskI=0;$taskI-lt$taskRows.Count;$taskI++){$taskDto=Read-RecurrenceJson $taskRows[$taskI].detail $Problems 'D1 transfer';if(-not(Test-RecurrenceNode $taskDto transfer 'D1 raw transfer' $taskSchemas $Problems)){continue};Assert-Recurrence ($taskI-lt$taskExpected.Count-and(Test-RecurrenceEqual $taskDto $taskExpected[$taskI])-and$taskRows[$taskI].tick-eq$taskDto.tick) 'Raw D1 transfer differs from result receipt.' $Problems;Test-RecurrenceHealthyLifetime $taskDto.before $taskLifetimes $taskRows[$taskI].sequence $Problems;Test-RecurrenceHealthyLifetime $taskDto.after $taskLifetimes $taskRows[$taskI].sequence $Problems}
    }
    foreach($taskPair in @(@('queries','free-query','query'),@('gates','cell-gate','gate'))){$taskRows=@($Events|Where-Object{$_.phase-ceq('l04-b1-healthy-'+$taskPair[1])});$taskExpected=$taskH.($taskPair[0]);Assert-Recurrence ($taskRows.Count-eq$taskExpected.Count) 'D1 query/gate raw cardinality mismatch.' $Problems
        for($taskI=0;$taskI-lt$taskRows.Count;$taskI++){$taskDto=Read-RecurrenceJson $taskRows[$taskI].detail $Problems 'D1 query/gate';if(Test-RecurrenceNode $taskDto $taskPair[2] 'D1 raw query/gate' $taskSchemas $Problems){Assert-Recurrence ($taskI-lt$taskExpected.Count-and(Test-RecurrenceEqual $taskDto $taskExpected[$taskI])-and$taskRows[$taskI].tick-eq$taskDto.tick) 'D1 query/gate raw payload mismatch.' $Problems;Test-RecurrenceHealthyLifetime $taskDto.before $taskLifetimes $taskRows[$taskI].sequence $Problems}}}
    $taskCurrents=@($Events|Where-Object{$_.phase-ceq'l04-b1-healthy-current-job'});Assert-Recurrence ($taskCurrents.Count-eq$taskH.jobs.Count) 'D1 actual-current job catalog mismatch.' $Problems
    foreach($taskRow in $taskCurrents){$taskDto=Read-RecurrenceJson $taskRow.detail $Problems 'D1 current job';if(-not(Test-RecurrenceNode $taskDto hjob 'D1 actual current job' $taskSchemas $Problems)){continue};$taskJob=Get-RecurrenceOne $taskH.jobs id $taskDto.id $Problems 'D1 job'
        Assert-Recurrence ($null-ne$taskJob-and(Test-RecurrenceEqual $taskDto $taskJob @('endTick','endCondition','released'))-and$taskDto.endTick-eq-1-and$null-eq$taskDto.endCondition-and-not$taskDto.released-and$taskRow.tick-eq$taskDto.observedTick) 'D1 actual-current job payload/lifecycle mismatch.' $Problems}
    $taskCleanups=@($Events|Where-Object{$_.phase-ceq'l04-b1-healthy-job-cleanup'});Assert-Recurrence ($taskCleanups.Count-eq@($taskH.jobs|Where-Object{$_.endTick-ge0}).Count) 'D1 cleanup multiplicity mismatch.' $Problems
    $taskCleanupIds=@();foreach($taskRow in $taskCleanups){$taskDto=Read-RecurrenceJson $taskRow.detail $Problems 'D1 cleanup';if(-not(Test-RecurrenceNode $taskDto hjob 'D1 cleanup' $taskSchemas $Problems)){continue};$taskJob=Get-RecurrenceOne $taskH.jobs id $taskDto.id $Problems 'D1 completed job';$taskCleanupIds+=@($taskDto.id)
        Assert-Recurrence ($null-ne$taskJob-and(Test-RecurrenceEqual $taskDto $taskJob)-and$taskRow.tick-eq$taskDto.endTick) 'D1 cleanup differs from final job record.' $Problems};Assert-Recurrence (@($taskCleanupIds|Select-Object -Unique).Count-eq$taskCleanupIds.Count) 'Duplicate D1 cleanup.' $Problems
    $taskStates=@($Events|Where-Object{$_.phase-ceq'l04-b1-healthy-settled-state'});$taskStable=@{};$taskLastSequence=0;$taskLastTick=$taskH.startedTick;$taskDistinct=@{};$taskCensuses=New-Object 'System.Collections.Generic.List[object]'
    foreach($taskRow in $taskStates){$taskDto=Read-RecurrenceJson $taskRow.detail $Problems 'D1 settled state';if(-not(Test-RecurrenceNode $taskDto hstate 'D1 settled state' $taskSchemas $Problems)){continue}
        Assert-Recurrence ($taskDto.sequence-gt$taskLastSequence-and$taskDto.tick-ge$taskLastTick-and$taskDto.tick-eq$taskRow.tick-and$taskDto.tick-le$taskH.finishedTick) 'D1 settled observation ordering mismatch.' $Problems;$taskLastSequence=$taskDto.sequence;$taskLastTick=$taskDto.tick;$taskDistinct[$taskDto.tick]=$true
        Test-RecurrenceHealthyState $taskDto $taskH $Problems ($taskDto.tick-ge$taskH.stableSinceTick)
        Test-RecurrenceHealthyLifetime $taskDto $taskLifetimes $taskRow.sequence $Problems
        [void]$taskCensuses.Add(@{sequence=$taskRow.sequence;state=$taskDto})
        if($taskDto.tick-ge$taskH.stableSinceTick){$taskStable[$taskDto.tick]=$true}
    }
    Assert-Recurrence ($taskStates.Count-gt0-and$taskH.settledBoundaries-ge$taskStates.Count-and$taskDistinct.ContainsKey($taskH.startedTick)) 'D1 settled-state capture is missing.' $Problems
    for($taskTick=$taskH.startedTick;$taskTick-le$taskH.finishedTick;$taskTick++){Assert-Recurrence ($taskDistinct.ContainsKey($taskTick)) ('Missing actual D1 tick '+$taskTick) $Problems}
    for($taskTick=$taskH.stableSinceTick;$taskTick-le$taskH.finishedTick;$taskTick++){Assert-Recurrence ($taskStable.ContainsKey($taskTick)) ('Missing D1 stable tick '+$taskTick) $Problems}
    Assert-Recurrence ($taskH.stableDistinctTicks-eq($taskH.finishedTick-$taskH.stableSinceTick+1)) 'D1 stable distinct-tick aggregate mismatch.' $Problems
    $taskAbsorbs=New-Object 'System.Collections.Generic.List[object]';$taskSplits=New-Object 'System.Collections.Generic.List[object]'
    foreach($taskRow in @($Events|Where-Object{$_.phase-cin@('l04-b1-healthy-split','l04-b1-healthy-absorb')})){
        $taskPrior=@($taskCensuses|Where-Object{$_.sequence-lt$taskRow.sequence}|Select-Object -Last 1)
        Assert-Recurrence ($taskPrior.Count-eq1) 'Actual ancestry lacks a preceding full physical census.' $Problems
        if($taskRow.phase-ceq'l04-b1-healthy-split'){
            if($taskRow.detail-cnotmatch'^job=([0-9]+); requested=([0-9]+); originalBefore=(\{.*\}); originalAfter=(\{.*\}); result=(null|\{.*\})$'){Add-RecurrenceProblem $Problems 'Malformed actual split ancestry record.';continue}
            $taskJob=[int]$Matches[1];$taskRequested=[int]$Matches[2];$taskTexts=@($Matches[3],$Matches[4],$Matches[5]);$taskParts=@($taskTexts|ForEach-Object{Read-RecurrenceJson $_ $Problems 'Split ancestry'})
            if($taskLifetimes.ContainsKey($taskJob)){Test-RecurrenceHealthyLifetime ([pscustomobject]@{jobId=$taskJob;jobDef=$taskLifetimes[$taskJob].job.def}) $taskLifetimes $taskRow.sequence $Problems}else{Add-RecurrenceProblem $Problems 'D1 split lacks an actually observed job lifetime.'}
            if($taskParts.Count-ne3){Add-RecurrenceProblem $Problems 'Incomplete split ancestry.';continue};foreach($taskPart in $taskParts){Test-RecurrenceNode $taskPart hthing 'Split Thing' $taskSchemas $Problems|Out-Null;Test-RecurrenceAncestryThing $taskPart $Problems}
            if($null-ne$taskParts[0]-and$null-ne$taskParts[1]-and$null-ne$taskParts[2]){$taskWhole=$taskParts[0].id-eq$taskParts[2].id;Assert-Recurrence ($taskJob-in@($taskH.firstBulkJobId,$taskH.firstUnloadJobId)-and$taskRequested-gt0-and$taskParts[0].id-eq$taskParts[1].id-and$(if($taskWhole){$taskParts[2].count-eq$taskParts[0].count}else{$taskParts[1].count+$taskParts[2].count-eq$taskParts[0].count})) 'Actual split ancestry count/identity mismatch.' $Problems}
            Assert-Recurrence ($taskPrior.Count-eq1-and@($taskPrior[0].state.things|Where-Object{Test-RecurrenceEqual $_ $taskParts[0]}).Count-eq1) 'Split originalBefore contradicts the preceding actual physical census.' $Problems
            # The two five-unit sources and subsequent ten-unit inventory transfer
            # are whole stacks in this fixture. Keep detached intermediate custody.
            Assert-Recurrence ($taskWhole-and$taskRequested-eq$taskParts[0].count-and(Test-RecurrenceEqual $taskParts[1] $taskParts[2])-and-not$taskParts[2].spawned-and-not$taskParts[2].inventory-and-not$taskParts[2].hands-and-not$taskParts[2].destroyed) 'Fixture split did not preserve its actual whole-stack detached identity.' $Problems
            if($taskJob-eq$taskH.firstBulkJobId){$taskReceipts=@($taskH.transfers|Where-Object{$_.kind-ceq'bulk-pickup'-and$_.jobId-eq$taskJob-and$_.tick-eq$taskRow.tick-and(Test-RecurrenceEqual $_.original $taskParts[2])});Assert-Recurrence ($taskReceipts.Count-eq1-and$taskRequested-eq5) 'Floor split does not connect to its actual unique original pickup receipt.' $Problems}
            else{$taskNext=@($taskCensuses|Where-Object{$_.sequence-gt$taskRow.sequence}|Select-Object -First 1);Assert-Recurrence ($taskRequested-eq10-and$taskParts[0].inventory-and$taskNext.Count-eq1-and$taskNext[0].state.tick-eq$taskRow.tick-and@($taskNext[0].state.things|Where-Object{$_.hands-and(Test-RecurrenceEqual $_ $taskParts[2] @('hands','holder'))}).Count-eq1) 'Inventory split does not connect to the next actual ten-unit hand custody.' $Problems}
            [void]$taskSplits.Add(@{job=$taskJob;tick=$taskRow.tick;sequence=$taskRow.sequence;result=$taskParts[2]})
        }else{
            if($taskRow.detail-cnotmatch'^job=([0-9]+); returned=(True|False); targetBefore=(\{.*\}); sourceBefore=(\{.*\}); targetAfter=(\{.*\}); sourceAfter=(\{.*\})$'){Add-RecurrenceProblem $Problems 'Malformed actual absorb ancestry record.';continue}
            $taskJob=[int]$Matches[1];$taskTexts=@($Matches[3],$Matches[4],$Matches[5],$Matches[6]);$taskParts=@($taskTexts|ForEach-Object{Read-RecurrenceJson $_ $Problems 'Absorb ancestry'});if($taskParts.Count-ne4){Add-RecurrenceProblem $Problems 'Incomplete absorb ancestry.';continue};foreach($taskPart in $taskParts){Test-RecurrenceNode $taskPart hthing 'Absorb Thing' $taskSchemas $Problems|Out-Null;Test-RecurrenceAncestryThing $taskPart $Problems}
            if($taskLifetimes.ContainsKey($taskJob)){Test-RecurrenceHealthyLifetime ([pscustomobject]@{jobId=$taskJob;jobDef=$taskLifetimes[$taskJob].job.def}) $taskLifetimes $taskRow.sequence $Problems}else{Add-RecurrenceProblem $Problems 'D1 absorb lacks an actually observed job lifetime.'}
            $taskGrowth=$(if($taskParts[2].destroyed){0}else{$taskParts[2].count})-$taskParts[0].count;$taskRemoved=$taskParts[1].count-$(if($taskParts[3].destroyed){0}else{$taskParts[3].count})
            Assert-Recurrence ($taskJob-in@($taskH.firstBulkJobId,$taskH.firstUnloadJobId)-and$taskParts[0].id-eq$taskParts[2].id-and$taskParts[1].id-eq$taskParts[3].id-and$taskGrowth-ge0-and$taskGrowth-eq$taskRemoved) 'Actual absorb ancestry count/identity mismatch.' $Problems
            Assert-Recurrence ($taskPrior.Count-eq1-and@($taskPrior[0].state.things|Where-Object{Test-RecurrenceEqual $_ $taskParts[0]}).Count-eq1) 'Absorb targetBefore contradicts the preceding inventory census.' $Problems
            $taskSourceSplits=@($taskSplits|Where-Object{$_.sequence-lt$taskRow.sequence-and$_.job-eq$taskJob-and$_.tick-eq$taskRow.tick-and(Test-RecurrenceEqual $_.result $taskParts[1])})
            Assert-Recurrence ($taskSourceSplits.Count-eq1-and$taskParts[0].id-ne$taskParts[1].id) 'Absorb sourceBefore is disconnected from its actual preceding detached split.' $Problems
            $taskReceipts=@($taskH.transfers|Where-Object{$_.kind-ceq'bulk-pickup'-and$_.jobId-eq$taskJob-and$_.tick-eq$taskRow.tick-and$_.original.id-eq$taskParts[1].id-and$_.units-eq$taskGrowth-and@($_.after.things|Where-Object{Test-RecurrenceEqual $_ $taskParts[2]}).Count-eq1-and@($_.after.things|Where-Object{$_.id-eq$taskParts[3].id}).Count-eq0})
            Assert-Recurrence ($taskReceipts.Count-eq1-and$taskParts[3].destroyed-and$taskParts[3].count-eq0) 'Absorb outputs do not connect to the actual conserved pickup receipt.' $Problems
            # Thing and ThingWithComps can emit identical nested envelopes. Each
            # proves the same receipt above; only pickup receipts contribute units.
            [void]$taskAbsorbs.Add(@{job=$taskJob;tick=$taskRow.tick;source=$taskParts[1].id;target=$taskParts[2].id;removed=$taskRemoved;after=$taskParts[2]})
        }
    }
    foreach($taskPickup in @($taskH.transfers|Where-Object{$_.kind-ceq'bulk-pickup'})){
        if(@($taskPickup.after.things|Where-Object{$_.id-eq$taskPickup.original.id-and-not$_.destroyed-and$_.inventory}).Count-eq0){
            $taskMerge=@($taskAbsorbs|Where-Object{$_.job-eq$taskPickup.jobId-and$_.tick-eq$taskPickup.tick-and$_.source-eq$taskPickup.original.id-and$_.removed-eq$taskPickup.units-and$_.target-in@($taskPickup.after.things.id)})
            Assert-Recurrence ($taskMerge.Count-ge1) 'D1 original pickup identity disappeared without an observed conserved merge into the resulting inventory.' $Problems
        }
    }
}
function Test-RecurrenceEvents($Value,$Events,[string]$RunId,$Problems) {
    try {
        if(-not(Test-RecurrenceShape $Value $Problems)){return};$taskR=$Value.candidateRecurrence;$taskSchemas=Get-RecurrenceSchemas
        if($Events-isnot[array]-or$Events.Count-eq0-or$Events.Count-gt200000){Add-RecurrenceProblem $Problems 'Raw events must be a nonempty bounded array.';return}
        $taskOuterSequence=0;$taskOuterTick=-1;$taskRows=New-Object 'System.Collections.Generic.List[object]'
        $taskOuterKinds=@('harness-start','new-game','game-version-provenance','assertion','map-initialized','scenario-observed','error-capture-boundary','terminal-result')
        $taskHealthyKinds=@('fixture','candidate','current-job','free-query','cell-gate','bulk-pickup','physical-deposit','carry-transfer','job-cleanup','settled-state','result','observer-bind','split','absorb')
        foreach($taskE in $Events){
            if($taskE-isnot[pscustomobject]-or$taskE.phase-isnot[string]-or$taskE.detail-isnot[string]-or$taskE.sequence-isnot[long]-and$taskE.sequence-isnot[int]-or$taskE.tick-isnot[long]-and$taskE.tick-isnot[int]){Add-RecurrenceProblem $Problems 'Malformed outer event.';return}
            Assert-Recurrence ($taskE.sequence-eq($taskOuterSequence+1)-and$taskE.runId-ceq$RunId-and$taskE.caseId-ceq'L04-B1'-and$taskE.tick-ge$taskOuterTick) 'Outer event identity/order mismatch.' $Problems;$taskOuterSequence=$taskE.sequence;$taskOuterTick=$taskE.tick
            # PS7 ConvertFrom-Json may materialize ISO UTC text as DateTime; PS5.1 retains text.
            $taskUtc=[datetimeoffset]::MinValue;$taskUtcOkay=$false
            if($taskE.utc-is[datetime]){$taskUtcOkay=$taskE.utc.Kind-eq[DateTimeKind]::Utc;$taskUtc=[datetimeoffset]$taskE.utc}
            elseif($taskE.utc-is[datetimeoffset]){$taskUtc=$taskE.utc;$taskUtcOkay=$taskUtc.Offset-eq[TimeSpan]::Zero}
            elseif($taskE.utc-is[string]){$taskUtcOkay=$taskE.utc-cmatch'^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,7})?(?:Z|\+00:00)$'-and[datetimeoffset]::TryParse($taskE.utc,[ref]$taskUtc)}
            Assert-Recurrence $taskUtcOkay 'Outer event lacks an explicit UTC capture time.' $Problems
            if($taskE.phase-cin$taskOuterKinds){continue}
            if($taskE.phase-clike'l04-b1-healthy-*'){Assert-Recurrence ($taskE.phase.Substring(15)-cin$taskHealthyKinds) 'Unknown D1 outer event.' $Problems;continue}
            if($taskE.phase-cin@('l04-b1-assertion','l04-b1-result')){continue}
            if($taskE.phase-cnotlike'l04-b1-*'){Add-RecurrenceProblem $Problems 'Unknown or error/control outer event.';continue}
            $taskDto=Read-RecurrenceJson $taskE.detail $Problems 'B1 event';if(-not(Test-RecurrenceNode $taskDto event 'B1 raw event' $taskSchemas $Problems)){continue}
            Assert-Recurrence ($taskE.phase-ceq('l04-b1-'+$taskDto.kind)-and$taskE.tick-eq$taskDto.tick) 'Raw B1 event phase/tick mismatch.' $Problems;[void]$taskRows.Add($taskDto)
        }
        Assert-Recurrence (Test-RecurrenceEqual ($taskRows.ToArray()) $taskR.events) 'Complete raw B1 event stream differs from result.events.' $Problems
        $taskAssertions=@($Events|Where-Object{$_.phase-ceq'l04-b1-assertion'});Assert-Recurrence ($taskAssertions.Count-eq$taskR.assertions.Count) 'Raw B1 assertion catalog differs from result.' $Problems
        for($taskI=0;$taskI-lt$taskAssertions.Count;$taskI++){$taskDto=Read-RecurrenceJson $taskAssertions[$taskI].detail $Problems 'B1 assertion';Assert-Recurrence ($taskI-lt$taskR.assertions.Count-and(Test-RecurrenceEqual $taskDto $taskR.assertions[$taskI])) 'Raw B1 assertion payload mismatch.' $Problems}
        $taskPhases=@{};foreach($taskName in @('harness-start','new-game','map-initialized','l04-b1-healthy-fixture','l04-b1-healthy-result','l04-b1-result','scenario-observed','error-capture-boundary','terminal-result')){$taskEvent=Get-RecurrenceOne $Events phase $taskName $Problems 'B1 lifecycle';if($null-ne$taskEvent){$taskPhases[$taskName]=$taskEvent}}
        if($taskPhases.Count-ne9){return};$taskPrior=0
        foreach($taskName in @('harness-start','new-game','map-initialized','l04-b1-healthy-fixture','l04-b1-healthy-result','l04-b1-result','scenario-observed','error-capture-boundary','terminal-result')){Assert-Recurrence ($taskPhases[$taskName].sequence-gt$taskPrior) 'B1 lifecycle is out of order.' $Problems;$taskPrior=$taskPhases[$taskName].sequence}
        Assert-Recurrence ($Value.runId-ceq$RunId-and$Value.caseId-ceq'L04-B1'-and$taskPhases['harness-start'].sequence-eq1-and$taskPhases['harness-start'].detail-ceq('case=L04-B1; expectedBehavior='+$taskR.expectedBehavior)-and$taskPhases['new-game'].detail-ceq'GameComponent lifecycle callback received.'-and$taskPhases['terminal-result'].sequence-eq$Events[-1].sequence) 'B1 outer run/start/terminal identity mismatch.' $Problems
        $taskRawResult=Read-RecurrenceJson $taskPhases['l04-b1-result'].detail $Problems 'B1 result';$taskRawHealthy=Read-RecurrenceJson $taskPhases['l04-b1-healthy-result'].detail $Problems 'D1 result'
        Assert-Recurrence ((Test-RecurrenceEqual $taskRawResult $taskR)-and(Test-RecurrenceEqual $taskRawHealthy $taskR.healthyDelivery)) 'Raw final DTO differs from global result.' $Problems
        Assert-Recurrence ($taskPhases['l04-b1-result'].tick-eq$taskR.finishedTick-and$taskPhases['l04-b1-healthy-result'].tick-eq$taskR.finishedTick-and$taskPhases['scenario-observed'].tick-ge$taskR.finishedTick-and$taskPhases['terminal-result'].tick-eq$taskPhases['scenario-observed'].tick-and$taskPhases['error-capture-boundary'].tick-eq$taskPhases['scenario-observed'].tick) 'B1 terminal export is earlier than its held tick result.' $Problems
        Assert-Recurrence ($taskPhases['scenario-observed'].detail-ceq('case=L04-B1; expected='+$taskR.expectedBehavior+'; requestedBehaviorSatisfied=False; expectationMatched=True')-and$taskPhases['terminal-result'].detail-ceq($Value.status+': '+$Value.detail)-and$taskPhases['error-capture-boundary'].detail-ceq'Threaded capture closed before terminal result; earlier startup and shutdown require whole Player.log review.') 'B1 scenario/terminal payload mismatch.' $Problems
        Assert-Recurrence ($Value.unityErrorsObserved-eq0-and$null-eq$Value.negativeControl-and$Value.status-ceq'partial'-and@($Events|Where-Object{$_.phase-cin@('unity-error','assertion-revised','negative-control-start','negative-control-observed')}).Count-eq0) 'B1 clean capture contains runtime errors/control/revision evidence.' $Problems
        $taskGlobalIds=@();foreach($taskA in $Value.assertions){$taskGlobalIds+=@($taskA.id);$taskRaw=@($Events|Where-Object{$_.phase-ceq'assertion'-and$_.detail.StartsWith($taskA.id+': ')})
            Assert-Recurrence ($taskA.passed-is[bool]-and$taskA.passed-and$taskA.id-is[string]-and$taskA.observed-is[string]-and$taskRaw.Count-eq1) 'Global assertion failed, missing or duplicated.' $Problems
            if($taskA.id-ceq'no-unity-errors-after-harness-start'){Assert-Recurrence ($taskA.observed-ceq'observedErrors=0; threaded capture through terminal-result boundary'-and$taskRaw[0].detail-ceq'no-unity-errors-after-harness-start: passed; observedErrors=0'-and$taskRaw[0].sequence-gt$taskPhases['l04-b1-result'].sequence-and$taskRaw[0].sequence-lt$taskPhases['scenario-observed'].sequence) 'Global zero-error assertion boundary mismatch.' $Problems}
            elseif($taskRaw.Count-eq1){Assert-Recurrence ($taskRaw[0].detail-ceq($taskA.id+': passed; '+$taskA.observed)) 'Global assertion/raw payload mismatch.' $Problems}}
        Assert-Recurrence (@($taskGlobalIds|Select-Object -Unique).Count-eq$taskGlobalIds.Count-and@($Events|Where-Object{$_.phase-ceq'assertion'}).Count-eq$taskGlobalIds.Count) 'Global assertion catalog is inconsistent.' $Problems
        foreach($taskSuffix in @('ordinary-chain','exact-original-pickups','physical-unload','successful-cleanups','stable-conservation','execution-health')){Assert-Recurrence ('execution-l04-b1-healthy-'+$taskSuffix-cin$taskGlobalIds) 'Missing D1 execution assertion.' $Problems}
        $taskHealthySetup=@(Get-RecurrenceHealthySetupIds)
        Assert-Recurrence (@($taskGlobalIds|Where-Object{$_-clike'fixture-l04-b1-healthy-*'}).Count-eq$taskHealthySetup.Count) 'D1 setup assertion catalog cardinality mismatch.' $Problems
        foreach($taskId in $taskHealthySetup){Assert-Recurrence ($taskId-cin$taskGlobalIds) ('Missing D1 setup assertion '+$taskId) $Problems}
        $taskRetired=@($Events|Where-Object{$_.phase-ceq'l04-b1-fixture-zone-retirement'});$taskSettled=@($Events|Where-Object{$_.phase-ceq'l04-b1-healthy-settled-state'});$taskBinds=@($Events|Where-Object{$_.phase-ceq'l04-b1-healthy-observer-bind'})
        if($taskRetired.Count-eq5-and$taskSettled.Count-gt0-and$taskBinds.Count-eq17){
            foreach($taskId in $taskHealthySetup){$taskRaw=@($Events|Where-Object{$_.phase-ceq'assertion'-and$_.detail.StartsWith($taskId+': ')});if($taskRaw.Count-ne1){continue}
                $taskLower=if($taskId-ceq'fixture-l04-b1-healthy-observers'){$taskBinds[-1].sequence}else{$taskRetired[-1].sequence};$taskUpper=if($taskId-ceq'fixture-l04-b1-healthy-observers'){$taskSettled[0].sequence}else{$taskPhases['l04-b1-healthy-fixture'].sequence}
                Assert-Recurrence ($taskRaw[0].tick-eq$taskR.healthyDelivery.startedTick-and$taskRaw[0].sequence-gt$taskLower-and$taskRaw[0].sequence-lt$taskUpper) 'D1 setup assertion lies outside its actual preparation/binding phase.' $Problems}
            foreach($taskRaw in @($Events|Where-Object{$_.phase-ceq'assertion'-and$_.detail.StartsWith('execution-l04-b1-healthy-')})){Assert-Recurrence ($taskRaw.tick-eq$taskR.finishedTick-and$taskRaw.sequence-gt$taskSettled[-1].sequence-and$taskRaw.sequence-lt$taskPhases['l04-b1-healthy-result'].sequence) 'D1 execution was graded before its final settled observation.' $Problems}
        }
        foreach($taskId in @('real-map-initialized','real-game-ticks-advanced','no-unity-errors-after-harness-start','fixture-l04-b1-setup-completed','fixture-l04-b1-healthy-observers')){Assert-Recurrence ($taskId-cin$taskGlobalIds) ('Missing global '+$taskId) $Problems}
        Assert-Recurrence ($taskPhases['map-initialized'].detail-cmatch'^tick=([0-9]+); size=\(([0-9]+), 1, ([0-9]+)\)$') 'Map lacks exact native tick/size.' $Problems
        if($taskPhases['map-initialized'].detail-cmatch'^tick=([0-9]+); size=\(([0-9]+), 1, ([0-9]+)\)$'){$taskMapTick=[long]$Matches[1];$taskWidth=[int]$Matches[2];$taskHeight=[int]$Matches[3];Assert-Recurrence ($taskMapTick-eq$taskPhases['map-initialized'].tick-and$taskWidth-ge85-and$taskHeight-ge45-and[long]$taskR.startedTick-$taskMapTick-ge5) 'Actual map/tick dimensions do not support the fixture.' $Problems;Test-RecurrenceLayoutGeometry $taskR $taskWidth $taskHeight $Problems
            $taskTickAssertion=Get-RecurrenceOne $Value.assertions id 'real-game-ticks-advanced' $Problems 'Actual tick assertion';Assert-Recurrence ($null-ne$taskTickAssertion-and$taskTickAssertion.observed-ceq('elapsedTicks='+($taskR.startedTick-$taskMapTick))) 'Initial elapsed-tick assertion differs from the actual map/scenario ticks.' $Problems}
        foreach($taskE in @($Events|Where-Object{$_.phase-clike'l04-b1-*'})){Assert-Recurrence ($taskE.sequence-gt$taskPhases['map-initialized'].sequence-and$taskE.sequence-le$taskPhases['l04-b1-result'].sequence) 'Fixture record lies outside actual map/result lifetime.' $Problems}
        Test-RecurrenceHealthyRaw $taskR $Events $Problems $Value.assemblies
    }catch{Add-RecurrenceProblem $Problems ('Events cannot be verified: '+$_.Exception.Message)}
}
