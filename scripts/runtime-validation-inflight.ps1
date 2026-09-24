# L04-B1-D2: perturbed in-flight query and delivery witness only.
# Pure input validation; no game/DLL loading, evidence mutation or inferred D1 capture.
# The sibling's reviewed leaf primitives are reused without changing its source.
. (Join-Path $PSScriptRoot 'runtime-validation-recurrence.ps1')

function Add-InFlightProblem($Problems,[string]$Text) { [void]$Problems.Add('L04-B1-D2: '+$Text) }
function Assert-InFlight([bool]$Okay,[string]$Text,$Problems) { if(-not$Okay){Add-InFlightProblem $Problems $Text} }
function Get-InFlightSchemas {
    $s=Get-RecurrenceSchemas
    $s.d2reservation=@{pawnId='int';jobToken='int';jobId='int';thingId='int';count='int';maxPawns='int?';kind='str';cell='str';layer='str?'}
    $s.d2progress=@{fromObservationSequence='int';toObservationSequence='int';fromTick='int';toTick='int';cellChanged='bool';pathCostDecreased='bool'}
    $s.d2path=@{moving='bool';pathToken='int';destinationThing='int';nextCell='str';destinationCell='str';costLeft='num';costTotal='num'}
    $s.d2physical=@{tick='int';mapId='int';actorId='int';driverToken='int';currentTargetBThing='int';currentTargetBCell='str?';spawned='bool';healthy='bool';drafted='bool';driverOwnsCurrent='bool';current='job?';queued='job[]';anchor='hthing?';cargo='hstate';path='d2path?';reservations='d2reservation[]'}
    $s.d2observation=@{sequence='int';tick='int';purpose='str';physical='d2physical';counter='counter?';cache='cache?'}
    $s.d2query=@{ordinal='int';tick='int';beforeSequence='int';afterSequence='int';actualBuilds='int';successfulBuilds='int';actualNotes='int';kind='str';returned='bool';candidateIsCurrent='bool';stateUnchanged='bool';completeCall='bool';cacheCoherent='bool';selfReservationGateObserved='bool';candidate='job?';before='d2observation';after='d2observation'}
    $s.d2gate=@{actorId='int';sourceId='int';parentCallId='int';maxPawns='int';stackCount='int';queryOrdinal='int?';caller='str';layer='str?';ignoreOtherReservations='bool';returned='bool'}
    $s.d2event=@{sequence='int';tick='int';kind='str';detail='str?';call='event?';observation='d2observation?';query='d2query?';reservationGate='d2gate?';progress='d2progress?';assertion='assertion?'}
    $s.d2result=@{caseId='str';expectedBehavior='str';status='str';counterContract='str';error='str?';deliveryRole='str';unexercisedReason='str?';burstStopReason='str?';startedTick='int';finishedTick='int';firstBulkToken='int';firstBulkJobId='int';firstBulkDriverToken='int';anchorId='int';approachEntrySequence='int';firstCellAdvanceSequence='int';burstEntrySequence='int';maximumProbes='int';initialCount='int';initialEffectiveCount='int';thresholdBuildsNeeded='int';successfulCandidateQueries='int';actualBuilds='int';successfulBuilds='int';actualNotes='int';initialRowResetReason='str?';fixtureValid='bool';nativeDispatchProven='bool';approachWindowExercised='bool';realApproachProgress='bool';withinBurstProgress='bool';burstProgressFromSequence='int';burstProgressToSequence='int';probeStateUnchanged='bool';counterPathExercised='bool';postThresholdRejected='bool';deliverySatisfied='bool';counterTransitionsValid='bool';warningsAttributed='bool';requestedBehaviorSatisfied='bool';expectationMatched='bool';baselineGapObserved='bool';timedOut='bool';initialCounter='counter?';initialBulkJob='job?';bindings='str[]';events='d2event[]';queries='d2query[]';assertions='assertion[]';deliveryWitness='healthy?'}
    $s.assembly=@{name='str';path='str';sha256='str';assemblyVersion='str';moduleVersionId='str'}
    $s.mod=@{packageId='str';rootPath='str'}
    $s.globalAssertion=@{id='str';passed='bool';observed='str'}
    return $s
}
function Test-InFlightUtc($Value) {
    if($Value-is[datetime]){return $Value.Kind-eq[DateTimeKind]::Utc}
    if($Value-is[datetimeoffset]){return $Value.Offset-eq[TimeSpan]::Zero}
    $stamp=[datetimeoffset]::MinValue
    return ($Value-is[string]-and$Value-cmatch'^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,7})?(?:Z|\+00:00)$'-and[datetimeoffset]::TryParse($Value,[ref]$stamp))
}
function Get-InFlightPassiveMembers {
    # Independently resolved with reflection-only metadata from the immutable native
    # 5CF1 module. Resolution never executes the getter or loads a game in this helper.
    @{
        'Verse.AI.Pawn_PathFollower.get_Moving'=@{token=100688733;kind='method';type='System.Boolean'}
        'Verse.AI.Pawn_PathFollower.get_Destination'=@{token=100688732;kind='method';type='Verse.LocalTargetInfo'}
        'Verse.AI.Pawn_PathFollower.nextCell'=@{token=67127068;kind='field';type='Verse.IntVec3'}
        'Verse.AI.Pawn_PathFollower.nextCellCostLeft'=@{token=67127071;kind='field';type='System.Single'}
        'Verse.AI.Pawn_PathFollower.nextCellCostTotal'=@{token=67127072;kind='field';type='System.Single'}
        'Verse.AI.Pawn_PathFollower.curPath'=@{token=67127081;kind='field';type='Verse.AI.PawnPath'}
        'Verse.AI.ReservationManager.get_ReservationsReadOnly'=@{token=100688975;kind='method';type='System.Collections.Generic.List`1[[Verse.AI.ReservationManager+Reservation, Assembly-CSharp, Version=1.6.9676.17735, Culture=neutral, PublicKeyToken=null]]'}
        'Verse.AI.ReservationManager+Reservation.get_Claimant'=@{token=100689005;kind='method';type='Verse.Pawn'}
        'Verse.AI.ReservationManager+Reservation.get_Job'=@{token=100689006;kind='method';type='Verse.AI.Job'}
        'Verse.AI.ReservationManager+Reservation.get_Target'=@{token=100689007;kind='method';type='Verse.LocalTargetInfo'}
        'Verse.AI.ReservationManager+Reservation.get_StackCount'=@{token=100689010;kind='method';type='System.Int32'}
        'Verse.AI.ReservationManager+Reservation.get_MaxPawns'=@{token=100689009;kind='method';type='System.Int32'}
        'Verse.AI.ReservationManager+Reservation.get_Layer'=@{token=100689008;kind='method';type='Verse.ReservationLayerDef'}
    }
}
function Test-InFlightExactMember($Assemblies,[string]$Name,[long]$Token,[string]$Kind,$Problems) {
    $members=Get-InFlightPassiveMembers
    if($Name-ceq'Verse.AI.ReservationManager.CanReserve'){$members[$Name]=@{token=100688981;kind='method'}}
    if(-not$members.ContainsKey($Name)){Test-RecurrenceExactMember $Assemblies $Name $Token $Kind $Problems;return}
    $a=@($Assemblies|Where-Object{$_.name-ceq'Assembly-CSharp'})
    Assert-InFlight ($a.Count-eq1-and$a[0].sha256-ceq'5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A'-and$a[0].moduleVersionId-ceq'61e41735-6189-4da4-9d21-0260257b5097'-and$members[$Name].token-eq$Token-and$members[$Name].kind-ceq$Kind) ('Unreviewed exact passive/reservation member '+$Name) $Problems
}
function Test-InFlightShape($Value,$Problems) {
    try {
        if($null-eq$Value-or$Value-isnot[pscustomobject]){Add-InFlightProblem $Problems 'Global result must be an object.';return $false}
        $s=Get-InFlightSchemas;$okay=$true
        $fields=@{schemaVersion='int';runId='str';caseId='str';processId='int';status='str';detail='str';unityErrorsObserved='int';installedVersionFile='str';executingGameVersion='str';assertions='globalAssertion[]';assemblies='assembly[]';mods='mod[]';inFlightRecurrence='d2result'}
        $nulls=@('scenario','partialInventoryBill','candidateRecurrence','storageOwnership','storageSlots','storageProjection','storageProjectionBudget','storageDelivery','negativeControl')
        foreach($name in $fields.Keys){$p=$Value.PSObject.Properties[$name];if($null-eq$p){Add-InFlightProblem $Problems ('Missing global '+$name);$okay=$false}elseif(-not(Test-RecurrenceNode $p.Value $fields[$name] ('global.'+$name) $s $Problems)){$okay=$false}}
        foreach($name in $nulls){$p=$Value.PSObject.Properties[$name];if($null-eq$p-or$null-ne$p.Value){Add-InFlightProblem $Problems ('Other case payload must be present and null: '+$name);$okay=$false}}
        foreach($name in @('startedUtc','finishedUtc')){$p=$Value.PSObject.Properties[$name];if($null-eq$p-or-not(Test-InFlightUtc $p.Value)){Add-InFlightProblem $Problems ('Invalid UTC '+$name);$okay=$false}}
        foreach($p in $Value.PSObject.Properties){if(-not$fields.ContainsKey($p.Name)-and$p.Name-cnotin$nulls-and$p.Name-cnotin@('startedUtc','finishedUtc')){Add-InFlightProblem $Problems ('Unknown global '+$p.Name);$okay=$false}}
        return $okay
    }catch{Add-InFlightProblem $Problems ('Shape cannot be read: '+$_.Exception.Message);return $false}
}
function Get-InFlightCalls($R) { @($R.events|Where-Object{$null-ne$_.call}|ForEach-Object{$_.call}) }
function Get-InFlightEffective($Counter,[int]$Tick,$Problems,[int]$SourceStackCount=5) {
    if($null-eq$Counter-or-not$Counter.anchorPresent-or$Counter.count-le0){return @{count=0;reason='absent-or-nonpositive'}}
    if($Counter.anchorTick-gt$Tick){Add-InFlightProblem $Problems 'Initial counter is from a future tick.'}
    if([long]$Tick-$Counter.anchorTick-gt180){return @{count=0;reason='gap-exceeds180'}}
    if($Counter.stackCount-gt$SourceStackCount){return @{count=0;reason='source-count-shrank'}}
    return @{count=$Counter.count;reason='live-unshrunk-row'}
}
function Test-InFlightBulk($Job,$R) {
    return ($null-ne$Job-and$Job.token-gt0-and$Job.loadId-gt0-and$Job.def-ceq'HaulersDream_BulkHaul'-and-not$Job.forced-and$Job.targetA-eq$R.anchorId-and$Job.queueIds.Count-eq2-and$Job.counts.Count-eq2-and$Job.counts[0]-eq5-and$Job.counts[1]-eq5-and$Job.queueIds[0]-eq$R.anchorId-and@($Job.queueIds|Select-Object -Unique).Count-eq2-and@($Job.queueIds|Where-Object{$_-notin$R.deliveryWitness.originalSourceIds}).Count-eq0)
}
function Test-InFlightProgress($A,$B) {
    if($null-eq$A-or$null-eq$B-or$A.tick-ge$B.tick-or$A.sequence-ge$B.sequence){return $false}
    $aP=$A.physical;$bP=$B.physical
    if($null-eq$aP.current-or$null-eq$bP.current-or$null-eq$aP.path-or$null-eq$bP.path-or$aP.current.token-ne$bP.current.token-or$aP.current.loadId-ne$bP.current.loadId-or$aP.driverToken-ne$bP.driverToken){return $false}
    return ($aP.cargo.position-cne$bP.cargo.position-or($aP.path.pathToken-eq$bP.path.pathToken-and$aP.path.nextCell-ceq$bP.path.nextCell-and$aP.path.costTotal-eq$bP.path.costTotal-and$bP.path.costLeft-lt$aP.path.costLeft))
}
function Test-InFlightPhysical($P,$R,$Problems,[bool]$Approach=$false) {
    $h=$R.deliveryWitness
    Assert-InFlight ($P.tick-ge$R.startedTick-and$P.tick-le$R.finishedTick-and$P.actorId-gt0-and$P.mapId-ge0-and$P.cargo.tick-eq$P.tick-and$P.queued.Count-le64-and$P.reservations.Count-le64) 'Physical snapshot identity/bounds mismatch.' $Problems
    Test-RecurrenceHealthyState $P.cargo $h $Problems
    Assert-InFlight (($null-eq$P.current-and$P.cargo.jobId-eq-1)-or($null-ne$P.current-and$P.current.loadId-eq$P.cargo.jobId-and$P.current.def-ceq$P.cargo.jobDef)) 'Passive job differs from independently observed current cargo job.' $Problems
    if($null-ne$P.current){
        $jobs=@($h.jobs|Where-Object{$_.id-eq$P.current.loadId})
        Assert-InFlight ($jobs.Count-eq1-and$P.driverToken-gt0-and$P.driverOwnsCurrent-and$P.current.driver-ceq$jobs[0].driver-and$P.current.forced-eq$jobs[0].forced-and(Test-RecurrenceEqual $P.current.workgiver $jobs[0].workgiver)-and(Test-RecurrenceEqual $P.current.workgiverClass $jobs[0].workgiverClass)) 'Passive executing driver/workgiver/forced state contradicts actual current-job evidence.' $Problems
    }else{Assert-InFlight ($P.driverToken-eq0-and-not$P.driverOwnsCurrent) 'No-current snapshot invents an active driver.' $Problems}
    if($null-ne$P.anchor){
        Assert-InFlight ($P.anchor.id-eq$R.anchorId) 'Separate anchor descriptor changed its pinned identity.' $Problems
        $copies=@($P.cargo.things|Where-Object{$_.id-eq$R.anchorId})
        if(-not$P.anchor.destroyed){Assert-InFlight ($copies.Count-eq1-and(Test-RecurrenceEqual $P.anchor $copies[0])) 'Live anchor differs from its own complete physical custody census.' $Problems}
        else{Assert-InFlight ($copies.Count-eq0-and$P.anchor.count-eq0-and-not$P.anchor.spawned-and-not$P.anchor.inventory-and-not$P.anchor.hands-and$null-eq$P.anchor.cell-and$null-eq$P.anchor.holder) 'Destroyed anchor retains invented physical custody.' $Problems}
    }
    foreach($rsv in $P.reservations){
        Assert-InFlight ($rsv.kind-cin@('normal','physical-interaction')-and($rsv.pawnId-eq$P.actorId-or$rsv.thingId-in$h.originalSourceIds)-and$rsv.jobToken-ge0-and$rsv.count-ge-1-and(($rsv.jobToken-eq0)-eq($rsv.jobId-eq-1))) 'Reservation has unsupported kind/identity/filter.' $Problems
        if($rsv.kind-ceq'normal'){Assert-InFlight ($null-ne$rsv.maxPawns-and$rsv.maxPawns-ge1) 'Normal reservation omitted its actual MaxPawns.' $Problems}
        else{Assert-InFlight ($null-eq$rsv.maxPawns-and$null-eq$rsv.layer-and$rsv.count-eq-1) 'Physical-interaction reservation invents normal reservation fields.' $Problems}
        Get-RecurrenceCell $rsv.cell $Problems|Out-Null
    }
    if(-not$Approach){return}
    Assert-InFlight ((Test-InFlightBulk $P.current $R)-and$P.current.token-eq$R.firstBulkToken-and$P.current.loadId-eq$R.firstBulkJobId-and$P.current.driver-ceq'HaulersDream.JobDriver_BulkHaul'-and$P.driverToken-eq$R.firstBulkDriverToken-and$P.driverOwnsCurrent-and$P.spawned-and$P.healthy-and-not$P.drafted-and$P.queued.Count-eq0) 'Approach does not retain the pinned actual bulk job/driver.' $Problems
    $anchor=@($P.cargo.things|Where-Object{$_.id-eq$R.anchorId})
    Assert-InFlight ($null-ne$P.anchor-and$anchor.Count-eq1-and(Test-RecurrenceEqual $P.anchor $anchor[0])-and$P.anchor.spawned-and$P.anchor.holder-ceq'Verse.Map'-and$P.anchor.count-eq5-and-not$P.anchor.destroyed-and$P.anchor.cell-cin$h.sourceCells-and$P.cargo.source-eq10-and$P.cargo.high-eq0-and$P.cargo.inventory-eq0-and$P.cargo.hands-eq0-and$P.cargo.elsewhere-eq0-and$P.cargo.things.Count-eq2) 'Approach lacks both untouched original floor stacks.' $Problems
    Assert-InFlight ($P.currentTargetBThing-eq$R.anchorId-and$P.currentTargetBCell-ceq$P.anchor.cell-and$null-ne$P.path-and$P.path.moving-and$P.path.pathToken-gt0-and$P.path.destinationThing-eq$R.anchorId-and$P.path.destinationCell-ceq$P.anchor.cell-and$P.path.costTotal-gt0) 'Approach path/targetB is not the pinned first source.' $Problems
    Assert-InFlight (@($P.reservations|Where-Object{$_.kind-ceq'normal'-and$null-eq$_.layer-and$_.pawnId-eq$P.actorId-and$_.jobToken-eq$R.firstBulkToken-and$_.jobId-eq$R.firstBulkJobId-and$_.thingId-eq$R.anchorId-and$_.cell-ceq$P.anchor.cell-and($_.count-eq-1-or$_.count-ge5)}).Count-ge1) 'Approach lacks the current exact job self-reservation.' $Problems
}
function Test-InFlightRecordedEvents($R,$Problems) {
    $payloads=@{call='call';observation='observation';query='query';'reservation-gate'='reservationGate';'burst-progress'='progress';assertion='assertion'}
    $details=@('fixture-ready','first-bulk-current','burst-open','burst-close')
    Assert-InFlight ($R.events.Count-gt0-and$R.events.Count-le20009-and$R.queries.Count-ge2-and$R.queries.Count-le8) 'Event/probe bounded coverage mismatch.' $Problems
    $seq=0;$tick=$R.startedTick;$actor=0;$map=-1;$lastCargo=0;$anchorPinned=$false
    foreach($e in $R.events){
        Assert-InFlight ($e.sequence-eq++$seq-and$e.tick-ge$tick-and$e.tick-le$R.finishedTick) 'D2 event sequence/tick discontinuity.' $Problems;$tick=$e.tick
        Assert-InFlight ($payloads.ContainsKey($e.kind)-or$e.kind-cin$details) 'Unknown/fault D2 event.' $Problems
        foreach($name in $payloads.Values){Assert-InFlight (($null-ne$e.$name)-eq($payloads.ContainsKey($e.kind)-and$payloads[$e.kind]-ceq$name)) 'D2 event payload kind mismatch.' $Problems}
        Assert-InFlight (($null-ne$e.detail)-eq($e.kind-cin$details)) 'D2 event detail/payload ambiguity.' $Problems
        if($e.kind-ceq'first-bulk-current'){$anchorPinned=$true}
        if($null-ne$e.call){Assert-InFlight ($e.call.sequence-eq$e.sequence-and$e.call.tick-eq$e.tick-and$e.call.scene-ceq'D2'-and$e.call.caller-cin@('native-or-external','fixture-in-flight-query')-and(($null-ne$e.call.queryOrdinal)-eq($e.call.caller-ceq'fixture-in-flight-query'))) 'Native call envelope/context mismatch.' $Problems
            foreach($name in @('physical','beforePhysical','zoneRetirement','poolOperation')){Assert-InFlight ($null-eq$e.call.$name) 'D2 call contains an unrelated legacy scene payload.' $Problems}
        }
        if($null-ne$e.observation){$o=$e.observation;$p=$o.physical
            # StartEvent pins the original source once; Observe then emits its
            # descriptor, counter and cache together for every later observation.
            # The initial observation before native dispatch has none of them.
            Assert-InFlight (($null-ne$p.anchor)-eq$anchorPinned-and($null-ne$o.counter)-eq$anchorPinned-and($null-ne$o.cache)-eq$anchorPinned) 'Pinned anchor/counter/cache presence contradicts the actual first-bulk boundary.' $Problems
            Assert-InFlight ($o.sequence-eq$e.sequence-and$o.tick-eq$e.tick-and$p.tick-eq$e.tick-and$o.purpose-cin@('game-tick','query-before','query-after')-and$p.cargo.sequence-gt$lastCargo) 'Observation envelope/cargo sequence mismatch.' $Problems;$lastCargo=$p.cargo.sequence
            if($actor-eq0){$actor=$p.actorId;$map=$p.mapId};Assert-InFlight ($p.actorId-eq$actor-and$p.mapId-eq$map) 'Observation changed actor/map identity.' $Problems
            Test-InFlightPhysical $p $R $Problems
            if($null-ne$o.counter){Test-RecurrenceCounter $o.counter $R.anchorId $o.tick $Problems}
            if($null-ne$o.cache){Assert-InFlight ($o.cache.tick-eq$o.tick-and$o.cache.pawnId-eq$actor-and$o.cache.sourceId-eq$R.anchorId-and$o.cache.key-eq([long]$actor*4294967296L+[long]$R.anchorId)) 'Observation cache key/context mismatch.' $Problems}
        }
    }
    $calls=@(Get-InFlightCalls $R);Test-RecurrenceCalls $calls $Problems
    $entries=@($calls|Where-Object{$_.kind.EndsWith('-enter')-or$_.kind-ceq'start-attempt'})
    for($i=0;$i-lt$entries.Count;$i++){Assert-InFlight ($entries[$i].callId-eq$i+1) 'Actual native/Note/Start invocation identities have an omitted or invented creation.' $Problems}
    $allowed=@('observer-bind','has-enter','has-return','candidate-enter','candidate-return','try-build-enter','try-build-return','build-enter','build-return','note-enter','note-return','native-work-enter','native-work-return','start-attempt','start-return','make-job','warning')
    foreach($c in $calls){Assert-InFlight ($c.kind-cin$allowed) 'Unknown native D2 call kind.' $Problems
        if($c.kind-cnotin@('observer-bind','note-enter','note-return','make-job','warning')){Assert-InFlight ($c.actorId-eq$actor) 'Native call used a different actor.' $Problems}
        if($c.kind-ceq'warning'){Assert-InFlight ($c.caller-ceq'fixture-in-flight-query') 'Unexpected warning outside the measured query burst.' $Problems}
        if($c.kind-cin@('has-enter','has-return','candidate-enter','candidate-return','try-build-enter','try-build-return','build-enter','build-return')){Assert-InFlight ($c.sourceId-in$R.deliveryWitness.originalSourceIds-and$null-ne$c.counter-and$null-ne$c.cache-and$c.cache.pawnId-eq$actor-and$c.cache.sourceId-eq$c.sourceId-and$c.cache.key-eq([long]$actor*4294967296L+[long]$c.sourceId)) 'Actual source-bound native call omitted or changed its counter/cache context.' $Problems}
        if($c.kind-cin@('note-enter','note-return')){Assert-InFlight ($c.actorId-eq0-and$c.sourceId-in$R.deliveryWitness.originalSourceIds-and$null-ne$c.counter-and$null-eq$c.cache-and$null-eq$c.forced-and$null-eq$c.forceSweep-and$null-eq$c.inputJob-and$null-eq$c.job-and$null-eq$c.returned-and$null-eq$c.actualCurrent-and($c.kind-cne'note-return'-or$c.parentCallId-eq0)) 'Actual static Note input/return fields contradict the installed observer.' $Problems}
    }
    $nativeStack=New-Object 'System.Collections.Generic.List[object]'
    foreach($c in $calls){
        if($c.kind.EndsWith('-enter')-or$c.kind-ceq'start-attempt'){
            $parent=if($nativeStack.Count){$nativeStack[-1].callId}else{0}
            Assert-InFlight ($c.parentCallId-eq$parent) 'Actual native input skips its immediate active parent invocation.' $Problems;[void]$nativeStack.Add($c)
        }elseif($c.kind.EndsWith('-return')){
            Assert-InFlight ($nativeStack.Count-gt0-and$nativeStack[-1].callId-eq$c.callId) 'Actual native returns are not a complete nested call stack.' $Problems
            if($nativeStack.Count){$nativeStack.RemoveAt($nativeStack.Count-1)}
        }elseif($c.kind-ceq'make-job'){
            $parent=if($nativeStack.Count){$nativeStack[-1].callId}else{0}
            Assert-InFlight ($c.parentCallId-eq$parent-and$c.actorId-eq0-and$c.sourceId-eq0-and$null-ne$c.job-and$null-eq$c.counter-and$null-eq$c.cache-and$null-eq$c.forced-and$null-eq$c.forceSweep) 'Actual MakeJob return contradicts its enclosing native call or static arguments.' $Problems
        }
    };Assert-InFlight ($nativeStack.Count-eq0) 'Actual native invocation stack did not close.' $Problems
    foreach($e in @($R.events|Where-Object{$null-ne$_.call-or$null-ne$_.reservationGate})){
        $record=if($null-ne$e.call){$e.call}else{$e.reservationGate};$inside=@($R.queries|Where-Object{$e.sequence-gt$_.beforeSequence-and$e.sequence-lt$_.afterSequence})
        Assert-InFlight ($inside.Count-le1-and($record.caller-ceq'fixture-in-flight-query')-eq($inside.Count-eq1)-and(($null-ne$record.queryOrdinal)-eq($inside.Count-eq1))) 'Native call/gate was added outside its declared measured query or relabeled within one.' $Problems
        if($inside.Count-eq1){Assert-InFlight ($record.queryOrdinal-eq$inside[0].ordinal-and$e.tick-eq$inside[0].tick) 'Native call/gate disagrees with enclosing query identity.' $Problems}
        if($null-ne$e.reservationGate){Assert-InFlight ($record.actorId-eq$actor-and$record.sourceId-in$R.deliveryWitness.originalSourceIds-and$record.caller-cin@('native-or-external','fixture-in-flight-query')) 'Reservation gate contradicts actual observer actor/source selection.' $Problems
            if($record.parentCallId-gt0){$parent=@($calls|Where-Object{$_.callId-eq$record.parentCallId-and$_.kind.EndsWith('-enter')});$end=@($calls|Where-Object{$_.callId-eq$record.parentCallId-and$_.kind.EndsWith('-return')});Assert-InFlight ($parent.Count-eq1-and$end.Count-eq1-and$parent[0].sequence-lt$e.sequence-and$end[0].sequence-gt$e.sequence-and$parent[0].tick-eq$e.tick-and$parent[0].caller-ceq$record.caller) 'Reservation gate is outside its actual parent invocation.' $Problems}
        }
    }
    $ticks=@($R.events|Where-Object{$null-ne$_.observation-and$_.observation.purpose-ceq'game-tick'}|ForEach-Object{$_.tick})
    Assert-InFlight ($ticks.Count-gt0-and$ticks[0]-in@($R.startedTick,($R.startedTick+1))-and$ticks[-1]-eq$R.finishedTick-and$ticks.Count-eq($ticks[-1]-$ticks[0]+1)) 'Missing/duplicated actual game-tick integration.' $Problems
    for($i=1;$i-lt$ticks.Count;$i++){Assert-InFlight ($ticks[$i]-eq$ticks[$i-1]+1) 'Nonconsecutive GameComponentTick snapshots.' $Problems}
    Assert-InFlight ((Test-RecurrenceEqual @($R.events|Where-Object{$null-ne$_.query}|ForEach-Object{$_.query}) $R.queries)-and(Test-RecurrenceEqual @($R.events|Where-Object{$null-ne$_.assertion}|ForEach-Object{$_.assertion}) $R.assertions)) 'Query/assertion catalogs differ from actual events.' $Problems
    foreach($q in $R.queries){foreach($side in @('before','after')){$o=$q.$side;$found=@($R.events|Where-Object{$_.sequence-eq$o.sequence-and$null-ne$_.observation});Assert-InFlight ($found.Count-eq1-and(Test-RecurrenceEqual $o $found[0].observation)) 'Probe snapshot is disconnected from the complete observation stream.' $Problems}}
}
function Test-InFlightQuery($Q,$R,$Calls,$Problems) {
    $before=$Q.before;$after=$Q.after;$actor=$before.physical.actorId
    Assert-InFlight ($Q.ordinal-ge1-and$Q.ordinal-le8-and$Q.tick-eq$before.tick-and$Q.tick-eq$after.tick-and$Q.beforeSequence-eq$before.sequence-and$Q.afterSequence-eq$after.sequence-and$before.sequence-lt$after.sequence-and$before.purpose-ceq'query-before'-and$after.purpose-ceq'query-after') 'Probe snapshot identity/chronology mismatch.' $Problems
    Test-InFlightPhysical $before.physical $R $Problems $true;Test-InFlightPhysical $after.physical $R $Problems $true
    $game=@($R.events|Where-Object{$null-ne$_.observation-and$_.observation.purpose-ceq'game-tick'-and$_.tick-eq$Q.tick})
    Assert-InFlight ($game.Count-eq1-and$game[0].sequence-lt$Q.beforeSequence-and(Test-RecurrenceEqual $game[0].observation.physical $before.physical @('cargo.sequence'))-and(Test-RecurrenceEqual $game[0].observation.counter $before.counter)-and(Test-RecurrenceEqual $game[0].observation.cache $before.cache)) 'Synchronous query-before differs from its actual same-tick game observation.' $Problems
    Assert-InFlight ($Q.stateUnchanged-and(Test-RecurrenceEqual $before.physical $after.physical @('cargo.sequence'))-and$Q.completeCall-and$Q.cacheCoherent) 'Synchronous native probe changed physical state or lacked complete coverage.' $Problems
    $inside=@($Calls|Where-Object{$_.sequence-gt$before.sequence-and$_.sequence-lt$after.sequence});$stack=New-Object 'System.Collections.Generic.List[object]';$counts=@{has=0;candidate=0;'try-build'=0;build=0;note=0}
    foreach($e in $inside){
        Assert-InFlight ($e.caller-ceq'fixture-in-flight-query'-and$e.queryOrdinal-eq$Q.ordinal-and$e.tick-eq$Q.tick-and$e.scene-ceq'D2') 'Probe nested call changed caller/ordinal/tick.' $Problems
        if($e.kind-ceq'warning'){continue}
        if($e.kind-ceq'make-job'){Assert-InFlight ($stack.Count-gt0-and$e.parentCallId-eq$stack[-1].callId-and$e.actorId-eq0-and$e.sourceId-eq0-and$null-ne$e.job-and$null-eq$e.counter-and$null-eq$e.cache-and$null-eq$e.forced-and$null-eq$e.forceSweep) 'MakeJob lacks actual enclosing query provenance.' $Problems;continue}
        if($e.kind.EndsWith('-enter')){
            $family=$e.kind.Substring(0,$e.kind.Length-6);$parent=if($stack.Count){$stack[-1].kind}else{$null};$parentId=if($stack.Count){$stack[-1].callId}else{0}
            Assert-InFlight ($counts.ContainsKey($family)-and$e.parentCallId-eq$parentId-and$e.sourceId-eq$R.anchorId-and$e.callId-gt0) 'Unexpected probe call family/parent/source.' $Problems
            if(-not$counts.ContainsKey($family)){continue};$counts[$family]++;Assert-InFlight ($counts[$family]-le1) 'Duplicate probe call family.' $Problems
            $parents=@{has=$null;candidate='has-enter';'try-build'='candidate-enter';build='try-build-enter';note='build-enter'}
            Assert-InFlight (Test-RecurrenceEqual $parent $parents[$family]) 'Probe does not follow actual Has/candidate/build/Note nesting.' $Problems
            if($family-ceq'note'){Assert-InFlight ($e.actorId-eq0-and$null-eq$e.forced-and$null-eq$e.forceSweep-and$null-eq$e.cache-and$null-eq$e.job-and$null-eq$e.inputJob-and$null-eq$e.returned) 'Static Note input was invented.' $Problems}
            else{Assert-InFlight ($e.actorId-eq$actor-and$e.forced-eq$false-and$e.forceSweep-eq$false-and$null-eq$e.job-and$null-eq$e.returned-and$e.actualCurrent-eq$false) 'Probe pawn-bound input state mismatch.' $Problems}
            Assert-InFlight (Test-RecurrenceEqual $e.counter $before.counter) 'Nested input counter differs from the actual query-before state.' $Problems
            if($family-cin@('has','candidate','try-build')){Assert-InFlight (Test-RecurrenceEqual $e.cache $before.cache) 'Nested pre-build cache differs from query input.' $Problems}
            [void]$stack.Add($e);continue
        }
        if(-not$e.kind.EndsWith('-return')-or$stack.Count-eq0){Add-InFlightProblem $Problems 'Unpaired/unexpected probe return or dispatch.';continue}
        $start=$stack[-1];$stack.RemoveAt($stack.Count-1);$family=$start.kind.Substring(0,$start.kind.Length-6)
        Assert-InFlight ($e.kind-ceq($family+'-return')-and$e.callId-eq$start.callId-and$e.sourceId-eq$start.sourceId-and$e.actorId-eq$start.actorId-and(Test-RecurrenceEqual $e.forced $start.forced)-and(Test-RecurrenceEqual $e.forceSweep $start.forceSweep)-and(Test-RecurrenceEqual $e.inputJob $start.inputJob)-and$e.parentCallId-eq$(if($family-ceq'note'){0}else{$start.parentCallId})) 'Probe paired return identity/input mismatch.' $Problems
        Assert-InFlight (Test-RecurrenceEqual $e.counter $after.counter) 'Actual nested return counter differs from query-after.' $Problems
        if($family-cin@('has','candidate','try-build')){Assert-InFlight (Test-RecurrenceEqual $e.cache $after.cache) 'Returned cache differs from query result.' $Problems}
        if($family-ceq'has'){Assert-InFlight ($e.returned-eq$Q.returned-and$null-eq$e.job) 'Actual Has return differs from probe result.' $Problems}
        elseif($family-ceq'candidate'){Assert-InFlight ($e.returned-eq$Q.returned-and(Test-RecurrenceEqual $e.job $Q.candidate)) 'Actual candidate return differs from probe result.' $Problems}
        elseif($family-ceq'note'){Assert-InFlight ($null-eq$e.returned-and$null-eq$e.cache-and$null-eq$e.job) 'Static Note return invented result/cache.' $Problems}
        else{Assert-InFlight ($e.returned-eq($null-ne$e.job)) 'Actual build result boolean mismatch.' $Problems}
    }
    Assert-InFlight ($stack.Count-eq0-and$counts.has-eq1-and$counts.candidate-eq1-and$Q.actualBuilds-eq$counts.build-and$Q.actualNotes-eq$counts.note) 'Incomplete probe call census.' $Problems
    $built=@($inside|Where-Object{$_.kind-ceq'build-return'});$tried=@($inside|Where-Object{$_.kind-ceq'try-build-return'})
    if($built.Count){Assert-InFlight ($tried.Count-eq1-and(Test-RecurrenceEqual $built[0].job $tried[0].job)) 'Actual build/try return identity diverges.' $Problems}
    if($tried.Count-eq1-and$null-ne$tried[0].job){Assert-InFlight (Test-RecurrenceEqual $tried[0].job $Q.candidate) 'Candidate is not the actual TryBuild return.' $Problems}
    Assert-InFlight ($Q.returned-eq($null-ne$Q.candidate)-and$Q.candidateIsCurrent-eq($null-ne$Q.candidate-and$Q.candidate.token-eq$R.firstBulkToken)) 'Candidate/current result flags are unsupported.' $Problems
    if($null-ne$Q.candidate){Assert-InFlight (Test-InFlightBulk $Q.candidate $R) 'Returned candidate lacks the original automatic 5+5 plan.' $Problems}
    foreach($c in @($before.cache,$after.cache)){Assert-InFlight ($null-ne$c-and$c.tick-eq$Q.tick-and$c.pawnId-eq$actor-and$c.sourceId-eq$R.anchorId-and$c.key-eq([long]$actor*4294967296L+[long]$R.anchorId)) 'Actual probe cache context missing.' $Problems}
    if($tried.Count-eq0){Assert-InFlight (Test-RecurrenceEqual $before.cache $after.cache) 'Cache changed without an actual TryBuild.' $Problems}
    elseif($null-eq$tried[0].job){$c=$after.cache;Assert-InFlight ((Test-RecurrenceEqual $before.cache $after.cache)-or($c.generation-eq$Q.tick-and$c.entryPresent-and$null-eq$c.actualJob-and$c.pinnedLoadId-eq-1-and$c.jobState-eq0)) 'Rejected TryBuild cache is neither unchanged nor a native negative entry.' $Problems}
    else{foreach($c in @($after.cache)+$(if($counts.build-eq0){@($before.cache)}else{@()})){Assert-InFlight ($c.dictionaryPresent-and$c.entryPresent-and$c.generation-eq$Q.tick-and$c.jobState-eq0-and$c.pinnedLoadId-eq$Q.candidate.loadId-and(Test-RecurrenceEqual $c.actualJob $Q.candidate)) 'Cache return is not its actual pinned candidate identity.' $Problems}}
    $gates=@($R.events|Where-Object{$null-ne$_.reservationGate-and$_.sequence-gt$before.sequence-and$_.sequence-lt$after.sequence});$goodGate=$false
    foreach($ge in $gates){$g=$ge.reservationGate;Assert-InFlight ($g.actorId-eq$actor-and$g.sourceId-in$R.deliveryWitness.originalSourceIds-and$g.queryOrdinal-eq$Q.ordinal-and$g.caller-ceq'fixture-in-flight-query'-and$ge.tick-eq$Q.tick) 'CanReserve callback lost its query/actor/source context.' $Problems
        $par=@($inside|Where-Object{$_.callId-eq$g.parentCallId-and$_.kind.EndsWith('-enter')});$end=@($inside|Where-Object{$_.callId-eq$g.parentCallId-and$_.kind.EndsWith('-return')});Assert-InFlight ($par.Count-eq1-and$end.Count-eq1-and$par[0].sequence-lt$ge.sequence-and$ge.sequence-lt$end[0].sequence) 'CanReserve callback lies outside its actual native parent.' $Problems
        if($g.sourceId-eq$R.anchorId-and$g.returned-and-not$g.ignoreOtherReservations-and$g.maxPawns-eq1-and$null-eq$g.layer-and($g.stackCount-eq-1-or($g.stackCount-gt0-and$g.stackCount-le5))){$goodGate=$true}
    }
    Assert-InFlight ($Q.selfReservationGateObserved-eq$goodGate) 'Self-reservation gate aggregate lacks its actual returned callback.' $Problems
    $success=if($Q.returned-and$goodGate){@($built|Where-Object{Test-RecurrenceEqual $_.job $Q.candidate}).Count}else{0}
    Assert-InFlight ($Q.successfulBuilds-eq$success) 'Successful-build count differs from actual complete returned candidates.' $Problems
    $previous=$before.counter
    foreach($n in @($inside|Where-Object{$_.kind-ceq'note-enter'})){
        $end=@($inside|Where-Object{$_.kind-ceq'note-return'-and$_.callId-eq$n.callId})
        Assert-InFlight ((Test-RecurrenceEqual $previous $n.counter)-and$end.Count-eq1) 'Note does not begin from the observed prior counter.' $Problems
        if($end.Count-ne1){continue};$afterNote=$end[0].counter;$effective=Get-InFlightEffective $n.counter $Q.tick $Problems;$next=$effective.count+1
        Test-RecurrenceCounter $afterNote $R.anchorId $Q.tick $Problems
        if($next-ge6){Assert-InFlight (-not$afterNote.anchorPresent-and$afterNote.backoffPresent-and$afterNote.until-eq$Q.tick+2500-and$afterNote.backedOff-and$afterNote.warned) 'Actual sixth Note did not produce the reviewed backoff transition.' $Problems}
        else{Assert-InFlight ($afterNote.anchorPresent-and$afterNote.count-eq$next-and$afterNote.anchorTick-eq$Q.tick-and$afterNote.stackCount-eq5-and-not$afterNote.backoffPresent-and-not$afterNote.backedOff-and-not$afterNote.warned) 'Actual Note count/stamp transition is inconsistent.' $Problems}
        Assert-InFlight (-not$afterNote.failPresent) 'In-flight Note acquired unrelated failure state.' $Problems;$previous=$afterNote
    }
    Assert-InFlight (Test-RecurrenceEqual $previous $after.counter) 'Final query counter lacks its actual Note transition.' $Problems
}
function Test-InFlightWindow($R,$Problems) {
    $calls=@(Get-InFlightCalls $R);$obs=@($R.events|Where-Object{$null-ne$_.observation}|ForEach-Object{$_.observation})
    $initial=Get-RecurrenceOne $obs sequence $R.burstEntrySequence $Problems 'D2 burst entry'
    $entry=Get-RecurrenceOne $obs sequence $R.approachEntrySequence $Problems 'D2 approach entry'
    $advance=Get-RecurrenceOne $obs sequence $R.firstCellAdvanceSequence $Problems 'D2 initial cell advance'
    $first=Get-RecurrenceOne $R.events kind 'first-bulk-current' $Problems 'D2 first current'
    $opened=Get-RecurrenceOne $R.events kind 'burst-open' $Problems 'D2 burst open'
    $closed=Get-RecurrenceOne $R.events kind 'burst-close' $Problems 'D2 burst close'
    $ready=Get-RecurrenceOne $R.events kind 'fixture-ready' $Problems 'D2 fixture ready'
    $progress=Get-RecurrenceOne $R.events kind 'burst-progress' $Problems 'D2 burst motion'
    if($null-eq$initial-or$null-eq$entry-or$null-eq$advance-or$null-eq$first-or$null-eq$opened-or$null-eq$closed-or$null-eq$ready-or$null-eq$progress){return}
    foreach($o in @($initial,$entry,$advance)){Test-InFlightPhysical $o.physical $R $Problems $true;Assert-InFlight ($o.purpose-ceq'game-tick') 'Initial motion borrowed a query observation.' $Problems}
    Assert-InFlight ($ready.tick-eq$R.startedTick-and$ready.detail-ceq($R.deliveryRole+' Native first-source cell advance and exact current-job self-reservation required; at most8 one-per-tick automatic probes.')-and$ready.sequence-lt$first.sequence-and$first.sequence-lt$entry.sequence-and$entry.sequence-le$advance.sequence-and$advance.sequence-eq$initial.sequence-and$initial.sequence-lt$opened.sequence-and$opened.sequence-lt$R.queries[0].beforeSequence-and$closed.sequence-gt$R.queries[-1].afterSequence-and$closed.detail-ceq$R.burstStopReason) 'Burst/current/first-cell lifecycle is inconsistent.' $Problems
    Assert-InFlight ($first.detail-cmatch'^Actual native StartJob/current-driver witness; cell=(\(-?[0-9]+, 0, -?[0-9]+\))$') 'Initial actual-current position was omitted.' $Problems
    if($first.detail-cmatch'^Actual native StartJob/current-driver witness; cell=(\(-?[0-9]+, 0, -?[0-9]+\))$'){
        $startCell=$Matches[1];Assert-InFlight ($advance.physical.cargo.position-cne$startCell) 'Initial first-source cell advance never occurred.' $Problems
        foreach($o in @($obs|Where-Object{$_.sequence-ge$entry.sequence-and$_.sequence-lt$advance.sequence-and$_.purpose-ceq'game-tick'})){Assert-InFlight ($o.physical.cargo.position-ceq$startCell) 'First cell advance reference skips earlier movement.' $Problems}
    }
    $prior=@($obs|Where-Object{$_.purpose-ceq'game-tick'-and$_.sequence-ge$entry.sequence-and$_.sequence-le$R.queries[-1].beforeSequence});$real=$false
    for($i=1;$i-lt$prior.Count;$i++){if(Test-InFlightProgress $prior[$i-1] $prior[$i]){$real=$true}}
    Assert-InFlight ($real-and$R.realApproachProgress) 'No real approach motion connects game-tick observations.' $Problems
    $firstStart=@($calls|Where-Object{$_.kind-ceq'start-return'-and$_.actualCurrent-eq$true-and$_.job.token-eq$R.firstBulkToken-and$_.job.loadId-eq$R.firstBulkJobId})
    Assert-InFlight ($firstStart.Count-eq1-and$firstStart[0].sequence-eq$first.sequence-1-and(Test-RecurrenceEqual $firstStart[0].job $R.initialBulkJob)-and$R.initialBulkJob.driver-ceq'HaulersDream.JobDriver_BulkHaul'-and$R.initialBulkJob.workgiver-ceq'HaulGeneral'-and$R.initialBulkJob.workgiverClass-ceq'RimWorld.WorkGiver_HaulGeneral'-and$R.firstBulkDriverToken-gt0-and(Test-InFlightBulk $R.initialBulkJob $R)) 'Initial pinned descriptor is disconnected from actual StartJob/current.' $Problems
    $dispatch=$false
    foreach($start in $firstStart){foreach($work in @($calls|Where-Object{$_.kind-ceq'native-work-return'-and$_.returned-eq$true-and$_.tick-eq$start.tick-and$_.sequence-lt$start.sequence-and$_.job.token-eq$start.job.token-and$_.job.loadId-eq$start.job.loadId})){
        $workEnter=@($calls|Where-Object{$_.kind-ceq'native-work-enter'-and$_.callId-eq$work.callId});$attempt=@($calls|Where-Object{$_.kind-ceq'start-attempt'-and$_.callId-eq$start.callId})
        foreach($selected in @($calls|Where-Object{$_.kind-ceq'candidate-return'-and$_.parentCallId-eq$work.callId-and$_.sourceId-eq$R.anchorId-and$_.returned-eq$true-and$_.forced-eq$false-and$_.forceSweep-eq$false-and$_.sequence-lt$work.sequence-and(Test-RecurrenceEqual $_.job $work.job @('workgiver','workgiverClass'))})){
            $selection=@($calls|Where-Object{$_.kind-ceq'candidate-enter'-and$_.callId-eq$selected.callId});if($selection.Count-ne1){continue}
            $has=@($calls|Where-Object{$_.kind-ceq'has-return'-and$_.parentCallId-eq$work.callId-and$_.sourceId-eq$R.anchorId-and$_.returned-eq$true-and$_.sequence-lt$selection[0].sequence})
            $chain=@($workEnter)+@($work)+@($attempt)+@($start)+@($selection)+@($selected)+@($has)
            if($workEnter.Count-eq1-and$attempt.Count-eq1-and$has.Count-ge1-and$workEnter[0].sequence-lt$has[0].sequence-and$selected.sequence-lt$work.sequence-and$work.sequence-lt$attempt[0].sequence-and$attempt[0].sequence-lt$start.sequence-and@($chain|Where-Object{$_.caller-cne'native-or-external'-or$null-ne$_.queryOrdinal-or$_.actorId-ne$initial.physical.actorId-or$_.tick-ne$start.tick}).Count-eq0-and(Test-RecurrenceEqual $work.job $attempt[0].inputJob)-and(Test-RecurrenceEqual $attempt[0].inputJob $start.job @('driver','startTick'))){$dispatch=$true}
        }
    }}
    Assert-InFlight ($dispatch-and$R.nativeDispatchProven) 'Pinned first job lacks its exact native work/first-source selection/start chain.' $Problems
    Assert-InFlight ($null-ne$R.initialCounter-and(Test-RecurrenceEqual $R.initialCounter $initial.counter)-and-not$initial.counter.backoffPresent-and-not$initial.counter.warned-and-not$initial.counter.failPresent-and-not$initial.counter.backedOff) 'Burst initial counter is not the recorded fresh pre-query row.' $Problems
    $effective=Get-InFlightEffective $initial.counter $initial.tick $Problems;$raw=if($null-eq$initial.counter.count){0}else{$initial.counter.count}
    Assert-InFlight ($R.initialCount-eq$raw-and$R.initialEffectiveCount-eq$effective.count-and$R.initialRowResetReason-ceq$effective.reason-and$R.thresholdBuildsNeeded-eq6-$effective.count-and$R.thresholdBuildsNeeded-ge1-and$R.thresholdBuildsNeeded-le6-and$R.maximumProbes-eq8) 'Initial effective threshold does not follow actual gap/count-shrink semantics.' $Problems
    Assert-InFlight ($opened.detail-ceq('rawCount='+$raw+'; effectiveCount='+$effective.count+'; reset='+$effective.reason+'; neededBuilds='+$R.thresholdBuildsNeeded+'; maximumProbes=8')) 'Burst-open payload differs from actual initial row.' $Problems
    $builds=0;$successful=0;$notes=0;$candidates=0;$previous=$initial;$backoff=$false
    for($i=0;$i-lt$R.queries.Count;$i++){
        $q=$R.queries[$i];Assert-InFlight ($q.ordinal-eq$i+1-and($i-eq0-or$q.tick-eq$R.queries[$i-1].tick+1)-and$q.kind-ceq$(if($backoff){'post-threshold-check'}else{'candidate-probe'})) 'Probe ordinal/consecutive tick/threshold phase mismatch.' $Problems
        Test-InFlightQuery $q $R $calls $Problems
        Assert-InFlight ((Test-RecurrenceEqual $previous.counter $q.before.counter @('tick'))-and@($calls|Where-Object{$_.kind-ceq'note-enter'-and$_.sourceId-eq$R.anchorId-and$_.sequence-gt$previous.sequence-and$_.sequence-lt$q.beforeSequence}).Count-eq0) 'An intervening actual Note/row change breaks burst attribution.' $Problems
        Assert-InFlight ($q.tick-ge$initial.tick-and$q.tick-lt$R.deliveryWitness.transfers[0].tick) 'Probe is outside the pre-pickup approach window.' $Problems
        $builds+=$q.actualBuilds;$successful+=$q.successfulBuilds;$notes+=$q.actualNotes;if($q.returned-and$q.selfReservationGateObserved){$candidates++}
        if($backoff){Assert-InFlight ($i-eq$R.queries.Count-1-and-not$q.returned-and$null-eq$q.candidate-and$q.after.counter.backedOff) 'Post-threshold observation did not measure rejection.' $Problems}
        else{Assert-InFlight $q.returned 'Candidate was rejected before threshold coverage.' $Problems}
        $backoff=$q.after.counter.backedOff;$previous=$q.after
    }
    Assert-InFlight ($R.actualBuilds-eq$builds-and$R.successfulBuilds-eq$successful-and$R.actualNotes-eq$notes-and$R.successfulCandidateQueries-eq$candidates-and$successful-ge$R.thresholdBuildsNeeded-and$candidates-ge$R.thresholdBuildsNeeded) 'Aggregate candidate/build/Note counts do not match actual calls.' $Problems
    $pg=$progress.progress;$from=Get-RecurrenceOne $obs sequence $R.burstProgressFromSequence $Problems 'D2 motion before';$to=Get-RecurrenceOne $obs sequence $R.burstProgressToSequence $Problems 'D2 motion after'
    $pair=$false;for($i=1;$i-lt$R.queries.Count;$i++){if($R.queries[$i-1].afterSequence-eq$R.burstProgressFromSequence-and$R.queries[$i].beforeSequence-eq$R.burstProgressToSequence){$pair=$true}}
    Assert-InFlight ($pair-and$null-ne$from-and$null-ne$to-and(Test-InFlightProgress $from $to)-and$pg.fromObservationSequence-eq$from.sequence-and$pg.toObservationSequence-eq$to.sequence-and$pg.fromTick-eq$from.tick-and$pg.toTick-eq$to.tick-and$progress.tick-eq$to.tick-and$progress.sequence-gt$to.sequence-and$progress.sequence-lt$R.queries[$to.tick-$R.queries[0].tick].afterSequence) 'Motion proof does not connect two real consecutive probe boundaries.' $Problems
    if($null-ne$from-and$null-ne$to){$a=$from.physical;$b=$to.physical;$cell=$a.cargo.position-cne$b.cargo.position;$cost=$a.path.pathToken-eq$b.path.pathToken-and$a.path.nextCell-ceq$b.path.nextCell-and$a.path.costTotal-eq$b.path.costTotal-and$b.path.costLeft-lt$a.path.costLeft
        Assert-InFlight ($pg.cellChanged-eq$cell-and$pg.pathCostDecreased-eq$cost-and($cell-or$cost)) 'Typed movement flags contradict actual cells/path costs.' $Problems}
    $warnings=@($calls|Where-Object{$_.kind-ceq'warning'})
    $suffix=' was bulk-hauled 6 times in quick succession without moving (net-zero). Another mod is very likely returning it to where HD keeps re-hauling it (a logistics or loadout mod, e.g. RimIOT). HD is backing it off its automatic haul scan so pawns stop looping; a forced player order still hauls it. Please report the mod combination (issue #214) if this is unexpected.'
    foreach($w in $warnings){$ns=@($calls|Where-Object{$_.kind-ceq'note-enter'-and$_.sourceId-eq$R.anchorId-and$_.parentCallId-eq$w.parentCallId-and$_.sequence-lt$w.sequence-and$_.queryOrdinal-eq$w.queryOrdinal-and$_.tick-eq$w.tick});$ends=@($calls|Where-Object{$_.kind-ceq'note-return'-and$_.callId-in@($ns.callId)-and$_.sequence-gt$w.sequence-and$_.counter.warned-and$_.counter.backedOff-and$_.counter.until-eq$w.tick+2500})
        Assert-InFlight ($w.caller-ceq'fixture-in-flight-query'-and$w.sourceId-eq$R.anchorId-and$w.actorId-eq$initial.physical.actorId-and$w.warningAttribution-ceq'legacy-note'-and$null-ne$w.detail-and$w.detail.Length-gt$suffix.Length-and$w.detail.EndsWith($suffix,[StringComparison]::Ordinal)-and$ns.Count-eq1-and$ends.Count-eq1) 'Query warning is unknown or lacks its actual enclosing sixth Note.' $Problems}
    if($R.expectedBehavior-ceq'baseline-gap'){Assert-InFlight ($notes-eq$R.thresholdBuildsNeeded-and$warnings.Count-eq1-and$R.postThresholdRejected-and$backoff-and$closed.detail-ceq'Measured post-threshold automatic gate.') 'Published gap lacks threshold warning and next-tick actual rejection.' $Problems}
    else{Assert-InFlight ($notes-eq0-and$warnings.Count-eq0-and-not$R.postThresholdRejected-and-not$backoff-and$closed.detail-ceq'Required equivalent actual-build coverage completed.') 'Requested outcome still counts/rejects progressing queries.' $Problems}
}
function Test-InFlightEvidence($Value,[string]$ExpectedBehavior,$Problems) {
    try {
        if(-not(Test-InFlightShape $Value $Problems)){return};$r=$Value.inFlightRecurrence
        Assert-InFlight ($ExpectedBehavior-cin@('baseline-gap','satisfied')-and$r.expectedBehavior-ceq$ExpectedBehavior-and$r.caseId-ceq'L04-B1-D2'-and$Value.caseId-ceq$r.caseId-and$Value.schemaVersion-eq1-and$Value.processId-gt0-and$Value.runId-cmatch'^[a-f0-9]{32}$') 'Global/case/expectation identity mismatch.' $Problems
        Assert-InFlight ($r.counterContract-ceq'legacy-anchor-tuple-v1;threshold6;gap180;backoff2500;exact-fields-required'-and$r.deliveryRole-ceq'In-flight-query execution witness; not the unperturbed D1 control.'-and$r.startedTick-ge0-and$r.finishedTick-gt$r.startedTick-and$r.finishedTick-$r.startedTick-lt6000-and$null-eq$r.error-and$null-eq$r.unexercisedReason-and-not$r.timedOut-and$Value.unityErrorsObserved-eq0-and$null-ne$r.deliveryWitness) 'Incomplete/unreviewed/error D2 capture.' $Problems
        if($null-eq$r.deliveryWitness-or$r.queries.Count-lt2-or$r.queries.Count-gt8-or$r.finishedTick-$r.startedTick-ge6000){return}
        $baseline=$ExpectedBehavior-ceq'baseline-gap';$status=if($baseline){'behavior-gap-observed'}else{'passed'}
        Assert-InFlight ($r.status-ceq$status-and$Value.status-ceq$status-and$r.expectationMatched-and$r.baselineGapObserved-eq$baseline-and$r.requestedBehaviorSatisfied-eq(-not$baseline)) 'Expected baseline gap and requested behavior were conflated.' $Problems
        foreach($name in @('fixtureValid','nativeDispatchProven','approachWindowExercised','realApproachProgress','withinBurstProgress','probeStateUnchanged','counterPathExercised','deliverySatisfied','counterTransitionsValid','warningsAttributed')){Assert-InFlight $r.$name ('Missing bounded coverage '+$name) $Problems}
        Test-InFlightRecordedEvents $r $Problems;Test-InFlightWindow $r $Problems;Test-InFlightBindings $Value $Problems;Test-InFlightDelivery $r $Problems
        Assert-InFlight ($r.firstBulkJobId-eq$r.deliveryWitness.firstBulkJobId-and$r.anchorId-in$r.deliveryWitness.originalSourceIds) 'Delivery witness is disconnected from the current in-flight job/source.' $Problems
        $ids=@('D2-native-dispatch','D2-pinned-first-source-window','D2-within-burst-progress','D2-query-isolation','D2-counter-path-coverage','D2-query-warning-attribution','D2-healthy-followthrough','D2-no-false-query-recurrence')
        Assert-InFlight ($r.assertions.Count-eq8-and($r.assertions.id-join'|')-ceq($ids-join'|')) 'D2 assertion catalog omitted or duplicated a source-defined contract.' $Problems
        for($i=0;$i-lt$r.assertions.Count;$i++){Assert-InFlight ($r.assertions[$i].kind-ceq$(if($i-eq7){'behavior'}else{'execution'})-and$r.assertions[$i].passed-eq($i-ne7-or-not$baseline)-and-not[string]::IsNullOrWhiteSpace($r.assertions[$i].detail)) 'D2 assertion kind/outcome mismatch.' $Problems}
    }catch{Add-InFlightProblem $Problems ('Evidence cannot be verified: '+$_.Exception.Message)}
}
function Test-InFlightEvents($Value,$Events,[string]$RunId,$Problems) {
    try {
        if(-not(Test-InFlightShape $Value $Problems)){return};$r=$Value.inFlightRecurrence;$s=Get-InFlightSchemas
        if($null-eq$r.deliveryWitness-or$r.finishedTick-$r.startedTick-ge6000-or$Events-isnot[array]-or$Events.Count-eq0-or$Events.Count-gt100000){Add-InFlightProblem $Problems 'Incomplete or unbounded raw capture.';return}
        $outer=@('harness-start','new-game','game-version-provenance','assertion','map-initialized','scenario-observed','error-capture-boundary','terminal-result','l04-b1-d2-event','l04-b1-d2-result')
        $healthy=@('fixture','candidate','current-job','free-query','cell-gate','bulk-pickup','physical-deposit','carry-transfer','job-cleanup','settled-state','result','observer-bind','split','absorb')
        $raw=New-Object 'System.Collections.Generic.List[object]';$seq=0;$tick=-1;$lastUtc=[datetimeoffset]::MinValue
        foreach($e in $Events){
            if($e-isnot[pscustomobject]){Add-InFlightProblem $Problems 'Raw event is not an object.';return}
            $fields=@('sequence','runId','caseId','utc','phase','detail','tick');Assert-InFlight ((@($e.PSObject.Properties.Name|Sort-Object)-join'|')-ceq(($fields|Sort-Object)-join'|')) 'Raw event has missing/unknown fields.' $Problems
            if(-not(Test-RecurrenceNode $e.sequence int raw.sequence $s $Problems)-or-not(Test-RecurrenceNode $e.tick int raw.tick $s $Problems)-or$e.phase-isnot[string]-or$e.detail-isnot[string]-or-not(Test-InFlightUtc $e.utc)){Add-InFlightProblem $Problems 'Malformed raw event types/UTC.';return}
            $utc=[datetimeoffset]$e.utc
            Assert-InFlight ($e.sequence-eq++$seq-and$e.tick-ge$tick-and$e.runId-ceq$RunId-and$e.caseId-ceq'L04-B1-D2'-and$utc-ge$lastUtc) 'Raw event run/sequence/tick/UTC chronology mismatch.' $Problems;$tick=$e.tick;$lastUtc=$utc
            if($e.phase-clike'l04-b1-healthy-*'){Assert-InFlight ($e.phase.Substring(15)-cin$healthy) 'Unknown/error healthy observer phase.' $Problems}
            else{Assert-InFlight ($e.phase-cin$outer) 'Unknown/error/control outer phase.' $Problems}
            if($e.phase-ceq'l04-b1-d2-event'){$dto=Read-RecurrenceJson $e.detail $Problems 'D2 raw event';if(Test-RecurrenceNode $dto d2event raw.d2 $s $Problems){Assert-InFlight ($dto.tick-eq$e.tick) 'D2 raw/payload tick mismatch.' $Problems;[void]$raw.Add($dto)}}
        }
        Assert-InFlight (Test-RecurrenceEqual ($raw.ToArray()) $r.events) 'Complete raw D2 stream differs from result.events.' $Problems
        $phases=@{};$names=@('harness-start','new-game','game-version-provenance','map-initialized','l04-b1-healthy-fixture','l04-b1-healthy-result','l04-b1-d2-result','scenario-observed','error-capture-boundary','terminal-result')
        foreach($name in $names){$e=Get-RecurrenceOne $Events phase $name $Problems 'D2 lifecycle';if($null-ne$e){$phases[$name]=$e}}
        if($phases.Count-ne$names.Count){return};$prior=0
        # Version provenance is emitted during startup, before the native new-game callback.
        foreach($name in @('harness-start','game-version-provenance','new-game','map-initialized','l04-b1-healthy-fixture','l04-b1-healthy-result','l04-b1-d2-result','scenario-observed','error-capture-boundary','terminal-result')){Assert-InFlight ($phases[$name].sequence-gt$prior) 'D2 lifecycle out of order.' $Problems;$prior=$phases[$name].sequence}
        Assert-InFlight ($Value.runId-ceq$RunId-and$Value.caseId-ceq'L04-B1-D2'-and$phases['harness-start'].sequence-eq1-and$phases['harness-start'].detail-ceq('case=L04-B1-D2; expectedBehavior='+$r.expectedBehavior)-and$phases['new-game'].detail-ceq'GameComponent lifecycle callback received.'-and$phases['terminal-result'].sequence-eq$Events[-1].sequence) 'Run/start/native lifecycle/terminal identity mismatch.' $Problems
        Assert-InFlight ($phases['game-version-provenance'].detail-ceq('Version.txt='+$Value.installedVersionFile+'; executing assembly reports='+$Value.executingGameVersion)) 'Executing version provenance payload mismatch.' $Problems
        $rawResult=Read-RecurrenceJson $phases['l04-b1-d2-result'].detail $Problems 'D2 raw result';$rawHealthy=Read-RecurrenceJson $phases['l04-b1-healthy-result'].detail $Problems 'D2 delivery witness'
        Assert-InFlight ((Test-RecurrenceEqual $rawResult $r)-and(Test-RecurrenceEqual $rawHealthy $r.deliveryWitness)) 'Raw terminal DTO differs from global result.' $Problems
        Assert-InFlight ($phases['l04-b1-d2-result'].tick-eq$r.finishedTick-and$phases['l04-b1-healthy-result'].tick-eq$r.finishedTick-and$phases['scenario-observed'].tick-ge$r.finishedTick-and$phases['terminal-result'].tick-eq$phases['scenario-observed'].tick-and$phases['error-capture-boundary'].tick-eq$phases['scenario-observed'].tick) 'Terminal export precedes final actual game tick.' $Problems
        Assert-InFlight ($phases['scenario-observed'].detail-ceq('case=L04-B1-D2; expected='+$r.expectedBehavior+'; requestedBehaviorSatisfied='+$r.requestedBehaviorSatisfied.ToString()+'; expectationMatched='+$r.expectationMatched.ToString())-and$phases['terminal-result'].detail-ceq($Value.status+': '+$Value.detail)-and$Value.detail-ceq'Actual progressing first-source query window and its delivery witness only; full recurrence repair and original reports remain unverified.'-and$phases['error-capture-boundary'].detail-ceq'Threaded capture closed before terminal result; earlier startup and shutdown require whole Player.log review.') 'Scenario/scope/error-boundary/terminal payload mismatch.' $Problems
        Assert-InFlight ($Value.unityErrorsObserved-eq0-and$null-eq$Value.negativeControl-and$Value.status-cin@('behavior-gap-observed','passed')) 'Global runtime error or unsupported terminal outcome.' $Problems
        $ids=@();$rawAssertions=@($Events|Where-Object{$_.phase-ceq'assertion'})
        foreach($a in $Value.assertions){$ids+=@($a.id);$matches=@($rawAssertions|Where-Object{$_.detail.StartsWith($a.id+': ',[StringComparison]::Ordinal)})
            Assert-InFlight ($a.passed-and$matches.Count-eq1) 'Global assertion failed/missing/duplicated.' $Problems
            if($a.id-ceq'no-unity-errors-after-harness-start'){Assert-InFlight ($a.observed-ceq'observedErrors=0; threaded capture through terminal-result boundary'-and$matches.Count-eq1-and$matches[0].detail-ceq'no-unity-errors-after-harness-start: passed; observedErrors=0'-and$matches[0].sequence-gt$phases['l04-b1-d2-result'].sequence-and$matches[0].sequence-lt$phases['scenario-observed'].sequence) 'Actual no-errors boundary assertion was weakened or moved.' $Problems}
            elseif($matches.Count-eq1){Assert-InFlight ($matches[0].detail-ceq($a.id+': passed; '+$a.observed)) 'Global assertion differs from its actual raw payload.' $Problems}
        }
        $required=@('private-runtime-data-path','private-save-data-path','private-mod-directory','private-player-log','case-supported','negative-control-supported','installed-version-file-matches-manifest','harness-compiled-against-running-game','real-map-initialized','exact-active-mod-count','real-game-ticks-advanced','no-unity-errors-after-harness-start')
        for($i=0;$i-lt4;$i++){$required+=('mod-order-root-'+$i)}
        foreach($a in $Value.assemblies){$required+=('single-assembly-'+$a.name);$required+=('assembly-identity-'+$a.name)}
        foreach($suffix in @('expectation','main-map','scanner','fresh-actor','setup-completed')){$required+=('fixture-l04-b1-d2-'+$suffix)}
        $required+=@(Get-RecurrenceHealthySetupIds)
        foreach($suffix in @('ordinary-chain','exact-original-pickups','physical-unload','successful-cleanups','stable-conservation','execution-health')){$required+=('execution-l04-b1-healthy-'+$suffix)}
        Assert-InFlight ($ids.Count-eq$required.Count-and@($ids|Select-Object -Unique).Count-eq$ids.Count-and@($ids|Where-Object{$_-cnotin$required}).Count-eq0-and$rawAssertions.Count-eq$ids.Count) 'Complete source-defined global assertion catalog mismatch.' $Problems
        foreach($a in $Value.assemblies){
            $one=Get-RecurrenceOne $Value.assertions id ('single-assembly-'+$a.name) $Problems 'Loaded assembly uniqueness';$identity=Get-RecurrenceOne $Value.assertions id ('assembly-identity-'+$a.name) $Problems 'Loaded assembly identity'
            Assert-InFlight ($null-ne$one-and$one.observed-ceq'count=1'-and$null-ne$identity-and$identity.observed-ceq($a.path+'; version='+$a.assemblyVersion+'; sha256='+$a.sha256)) 'Loaded assembly assertions contradict the actual identity census.' $Problems
        }
        for($i=0;$i-lt$Value.mods.Count;$i++){$a=Get-RecurrenceOne $Value.assertions id ('mod-order-root-'+$i) $Problems 'Loaded mod order';Assert-InFlight ($null-ne$a-and$a.observed-ceq($Value.mods[$i].packageId+' @ '+$Value.mods[$i].rootPath)) 'Loaded mod root/order assertion contradicts its census.' $Problems}
        foreach($pair in @(@('case-supported','L04-B1-D2'),@('negative-control-supported','None'),@('exact-active-mod-count','actual=4; expected=4'),@('installed-version-file-matches-manifest',$Value.installedVersionFile),@('harness-compiled-against-running-game',(@($Value.assemblies|Where-Object{$_.name-ceq'Assembly-CSharp'})[0].sha256)),@('fixture-l04-b1-d2-expectation',$r.expectedBehavior))){$a=Get-RecurrenceOne $Value.assertions id $pair[0] $Problems 'Startup contract';Assert-InFlight ($null-ne$a-and$a.observed-ceq$pair[1]) 'Startup assertion payload contradicts actual selected case/version.' $Problems}
        $map=$phases['map-initialized'];Assert-InFlight ($map.detail-cmatch'^tick=([0-9]+); size=\(([0-9]+), 1, ([0-9]+)\)$') 'Native map tick/dimensions absent.' $Problems
        if($map.detail-cmatch'^tick=([0-9]+); size=\(([0-9]+), 1, ([0-9]+)\)$'){
            $mapTick=[int]$Matches[1];$width=[int]$Matches[2];$height=[int]$Matches[3];$x=[int][math]::Floor($width/2);$z=[int][math]::Floor($height/2)
            $sources=@(('('+($x-14)+', 0, '+($z-1)+')'),('('+($x-14)+', 0, '+($z+1)+')'));$high='('+($x+14)+', 0, '+$z+')'
            Assert-InFlight ($mapTick-eq$map.tick-and$width-ge41-and$height-ge13-and$r.startedTick-$mapTick-ge5-and(Test-RecurrenceEqual $sources $r.deliveryWitness.sourceCells)-and$r.deliveryWitness.highCell-ceq$high) 'Actual map-relative fresh geometry mismatch.' $Problems
            $a=Get-RecurrenceOne $Value.assertions id 'real-game-ticks-advanced' $Problems 'Native tick proof';Assert-InFlight ($null-ne$a-and$a.observed-ceq('elapsedTicks='+($r.startedTick-$mapTick))) 'Initial tick assertion contradicts actual map/case times.' $Problems
        }
        $setup=Get-RecurrenceOne $rawAssertions detail 'fixture-l04-b1-d2-setup-completed: passed; Fresh automatic delivery scene; only GameComponentTick advances the in-flight query coordinator.' $Problems 'D2 setup completion'
        $d2raw=@($Events|Where-Object{$_.phase-ceq'l04-b1-d2-event'});$firstObservation=@($d2raw|Where-Object{($_.detail|ConvertFrom-Json).kind-ceq'observation'}|Select-Object -First 1)
        Assert-InFlight ($null-ne$setup-and$setup.tick-eq$r.startedTick-and$setup.sequence-gt$phases['l04-b1-healthy-fixture'].sequence-and$firstObservation.Count-eq1-and$setup.sequence-lt$firstObservation[0].sequence) 'D2 setup completion is outside actual prepared/first-tick lifetime.' $Problems
        $healthyBinds=@($Events|Where-Object{$_.phase-ceq'l04-b1-healthy-observer-bind'});$settled=@($Events|Where-Object{$_.phase-ceq'l04-b1-healthy-settled-state'})
        foreach($id in @(Get-RecurrenceHealthySetupIds)){$a=@($rawAssertions|Where-Object{$_.detail.StartsWith($id+': ',[StringComparison]::Ordinal)});if($a.Count-ne1){continue}
            $lower=if($id-ceq'fixture-l04-b1-healthy-observers'-and$healthyBinds.Count){$healthyBinds[-1].sequence}else{$map.sequence};$upper=if($id-ceq'fixture-l04-b1-healthy-observers'-and$settled.Count){$settled[0].sequence}else{$phases['l04-b1-healthy-fixture'].sequence}
            Assert-InFlight ($a[0].tick-eq$r.startedTick-and$a[0].sequence-gt$lower-and$a[0].sequence-lt$upper) 'Healthy setup assertion is outside its actual preparation/binding phase.' $Problems}
        foreach($a in @($rawAssertions|Where-Object{$_.detail.StartsWith('execution-l04-b1-healthy-')})){Assert-InFlight ($a.tick-eq$r.finishedTick-and$settled.Count-gt0-and$a.sequence-gt$settled[-1].sequence-and$a.sequence-lt$phases['l04-b1-healthy-result'].sequence) 'Delivery witness graded before its final settled observation.' $Problems}
        foreach($e in @($Events|Where-Object{$_.phase-clike'l04-b1-*'})){Assert-InFlight ($e.sequence-gt$map.sequence-and$e.sequence-le$phases['l04-b1-d2-result'].sequence) 'Fixture record outside actual map/result lifetime.' $Problems}
        $assertEvents=@($r.events|Where-Object{$_.kind-ceq'assertion'});Assert-InFlight ($assertEvents.Count-eq8-and$assertEvents[0].sequence-eq$r.events.Count-7-and$assertEvents[-1].sequence-eq$r.events.Count-and@($assertEvents|Where-Object{$_.tick-ne$r.finishedTick}).Count-eq0) 'D2 terminal assertion grading order mismatch.' $Problems
        Test-InFlightDeliveryRaw $r $Events $Problems $Value.assemblies
        Test-InFlightRawContinuity $r $Events $Problems
        # The new passive observation stream is checked against actual healthy current
        # lifetime boundaries too; it cannot omit or revive an executing job.
        $life=Get-RecurrenceHealthyLifetimes $Events $Problems
        foreach($e in $d2raw){$dto=Read-RecurrenceJson $e.detail $Problems 'D2 lifecycle correlation';if($null-ne$dto.observation){Test-RecurrenceHealthyLifetime $dto.observation.physical.cargo $life $e.sequence $Problems}}
    }catch{Add-InFlightProblem $Problems ('Events cannot be verified: '+$_.Exception.Message)}
}
function Test-InFlightRawContinuity($R,$Events,$Problems) {
    # These are original raw streams, joined in capture order. No relabeling or
    # invented D1 layout is used. Native pool reuse permits one token with later
    # load IDs; the actual current lifetime is keyed by the normalized load ID.
    $h=$R.deliveryWitness;$s=Get-InFlightSchemas;$calls=@(Get-InFlightCalls $R)
    $outerByNested=@{};$currents=@{};$settled=New-Object 'System.Collections.Generic.List[object]';$d2Rows=New-Object 'System.Collections.Generic.List[object]'
    $splits=New-Object 'System.Collections.Generic.List[object]';$absorbs=New-Object 'System.Collections.Generic.List[object]';$index=0;$priorOuter=$null
    foreach($e in $Events){
        if($e.phase-ceq'l04-b1-healthy-settled-state'){$state=Read-RecurrenceJson $e.detail $Problems 'D2 physical cross-stream';if(Test-RecurrenceNode $state hstate 'raw shared physical' $s $Problems){[void]$settled.Add(@{outer=$e.sequence;state=$state})}}
        elseif($e.phase-ceq'l04-b1-healthy-current-job'){$job=Read-RecurrenceJson $e.detail $Problems 'D2 native creation';if(Test-RecurrenceNode $job hjob 'raw current' $s $Problems){if($currents.ContainsKey([int]$job.id)){Add-InFlightProblem $Problems 'Duplicate independent actual-current creation.'}else{$currents[[int]$job.id]=@{outer=$e.sequence;job=$job}}}}
        elseif($e.phase-ceq'l04-b1-healthy-split'){
            if($e.detail-cmatch'^job=([0-9]+); requested=([0-9]+); originalBefore=(\{.*\}); originalAfter=(\{.*\}); result=(null|\{.*\})$'){
                $job=[int]$Matches[1];$requested=[int]$Matches[2];$texts=@($Matches[3],$Matches[4],$Matches[5]);$parts=@($texts|ForEach-Object{Read-RecurrenceJson $_ $Problems 'D2 required split'})
                if($parts.Count-eq3-and$null-ne$parts[0]-and$null-ne$parts[2]){[void]$splits.Add(@{outer=$e.sequence;tick=$e.tick;job=$job;requested=$requested;before=$parts[0];after=$parts[1];result=$parts[2]})}
            }
        }elseif($e.phase-ceq'l04-b1-healthy-absorb'){
            if($e.detail-cmatch'^job=([0-9]+); returned=(True|False); targetBefore=(\{.*\}); sourceBefore=(\{.*\}); targetAfter=(\{.*\}); sourceAfter=(\{.*\})$'){
                $job=[int]$Matches[1];$texts=@($Matches[3],$Matches[4],$Matches[5],$Matches[6]);$parts=@($texts|ForEach-Object{Read-RecurrenceJson $_ $Problems 'D2 anchor ancestry'})
                if($parts.Count-eq4){[void]$absorbs.Add(@{outer=$e.sequence;tick=$e.tick;job=$job;source=$parts[1];after=$parts[3]})}
            }
        }elseif($e.phase-ceq'l04-b1-d2-event'){
            if($index-ge$R.events.Count){Add-InFlightProblem $Problems 'Unexpected extra raw D2 record.';continue};$dto=$R.events[$index];$index++;$outerByNested[[int]$dto.sequence]=$e.sequence
            if($null-ne$dto.observation){
                $o=$dto.observation;[void]$d2Rows.Add(@{outer=$e.sequence;observation=$o})
                if($o.purpose-ceq'game-tick'){
                    Assert-InFlight ($null-ne$priorOuter-and$priorOuter.phase-ceq'l04-b1-healthy-settled-state'-and$settled.Count-gt0-and$settled[-1].outer-eq$e.sequence-1-and$settled[-1].state.tick-eq$o.tick-and(Test-RecurrenceEqual $settled[-1].state $o.physical.cargo @('sequence'))-and$o.physical.cargo.sequence-eq$settled[-1].state.sequence+1) 'D2 game-tick cargo differs from the immediately preceding independently emitted healthy snapshot.' $Problems
                }
            }
        }
        $priorOuter=$e
    }
    $starts=@($calls|Where-Object{$_.kind-ceq'start-return'-and$_.actualCurrent-eq$true});$byLoad=@{}
    foreach($job in $h.jobs){
        $matching=@($starts|Where-Object{$null-ne$_.job-and$_.job.loadId-eq$job.id})
        Assert-InFlight ($matching.Count-eq1-and$currents.ContainsKey([int]$job.id)) 'Independent current-job creation lacks exactly one actual successful native StartJob.' $Problems
        if($matching.Count-ne1-or-not$currents.ContainsKey([int]$job.id)){continue};$start=$matching[0];$attempt=@($calls|Where-Object{$_.kind-ceq'start-attempt'-and$_.callId-eq$start.callId})
        if($attempt.Count-ne1){Add-InFlightProblem $Problems 'Native start lacks its actual input attempt.';continue};$a=$attempt[0];$current=$currents[[int]$job.id]
        Assert-InFlight ($outerByNested.ContainsKey([int]$a.sequence)-and$outerByNested.ContainsKey([int]$start.sequence)-and$outerByNested[[int]$a.sequence]-lt$current.outer-and$current.outer-lt$outerByNested[[int]$start.sequence]-and$a.tick-eq$start.tick-and$start.tick-eq$job.observedTick-and$a.caller-ceq'native-or-external'-and$start.caller-ceq'native-or-external'-and$null-eq$a.queryOrdinal-and$null-eq$start.queryOrdinal-and$a.actualCurrent-eq$false) 'Actual current creation is outside its native StartJob boundaries.' $Problems
        # StartJob sets startTick and creates a driver. Later native targets,
        # queues and expiry are deliberately not frozen to their initial values.
        Assert-InFlight ((Test-RecurrenceEqual $a.inputJob $start.inputJob @('startTick'))-and(Test-RecurrenceEqual $start.inputJob $start.job @('driver'))-and$start.job.startTick-eq$start.tick-and$start.job.driver-ceq$job.driver-and$start.job.def-ceq$job.def-and$start.job.forced-eq$job.forced-and(Test-RecurrenceEqual $start.job.workgiver $job.workgiver)-and(Test-RecurrenceEqual $start.job.workgiverClass $job.workgiverClass)) 'Native start/current stable descriptor fields disagree.' $Problems
        $byLoad[[int]$job.id]=$start
    }
    Assert-InFlight ($starts.Count-eq$h.jobs.Count-and$currents.Count-eq$h.jobs.Count) 'Native start/independent current creation census is incomplete.' $Problems
    foreach($q in $R.queries){if($null-ne$q.candidate-and-not$q.candidateIsCurrent){Assert-InFlight (@($starts|Where-Object{$null-ne$_.job-and$_.job.token-eq$q.candidate.token-and$_.job.loadId-eq$q.candidate.loadId}).Count-eq0) 'A query-only candidate was actually dispatched.' $Problems}}
    $driverByLoad=@{};$mapEvent=@($Events|Where-Object{$_.phase-ceq'map-initialized'});$width=0;$height=0
    if($mapEvent.Count-eq1-and$mapEvent[0].detail-cmatch'^tick=[0-9]+; size=\(([0-9]+), 1, ([0-9]+)\)$'){$width=[int]$Matches[1];$height=[int]$Matches[2]}
    foreach($row in $d2Rows){$p=$row.observation.physical;$position=Get-RecurrenceCell $p.cargo.position $Problems
        Assert-InFlight ($null-ne$position-and$position.x-ge0-and$position.x-lt$width-and$position.z-ge0-and$position.z-lt$height) 'Passive actor position lies outside the actual initialized map.' $Problems
        if($null-ne$p.current){$id=[int]$p.current.loadId
            if(-not$byLoad.ContainsKey($id)){Add-InFlightProblem $Problems 'Passive current lacks an actual native start descriptor.';continue};$start=$byLoad[$id]
            foreach($name in @('token','loadId','def','driver','startTick','forced','workgiver','workgiverClass')){Assert-InFlight (Test-RecurrenceEqual $p.current.$name $start.job.$name) ('Passive executing '+$name+' contradicts the actual native start.') $Problems}
            if($driverByLoad.ContainsKey($id)){Assert-InFlight ($driverByLoad[$id]-eq$p.driverToken) 'Current native job changed driver identity without another actual start.' $Problems}else{$driverByLoad[$id]=$p.driverToken}
        }
        # The installed bulk driver reserves both original sources with native
        # defaults; these rows persist until its normal cleanup. Native
        # LocalTargetInfo.Cell reads Thing.PositionHeld, so a carried source's
        # reservation follows the current pawn position. Its custody DTO still
        # has no floor cell. The pawn position is joined to the healthy census.
        $isBulk=$null-ne$p.current-and$p.current.loadId-eq$h.firstBulkJobId
        Assert-InFlight ($p.reservations.Count-eq$(if($isBulk){2}else{0})) 'Native reservation census does not follow the actual bulk lifetime and cleanup.' $Problems
        if($isBulk){foreach($sourceId in $h.originalSourceIds){
            $rows=@($p.reservations|Where-Object{$_.thingId-eq$sourceId});$sourceIndex=0;while($sourceIndex-lt$h.originalSourceIds.Count-and$h.originalSourceIds[$sourceIndex]-ne$sourceId){$sourceIndex++};$expectedCell=$h.sourceCells[$sourceIndex]
            $picked=@($h.transfers|Where-Object{$_.kind-ceq'bulk-pickup'-and$_.original.id-eq$sourceId-and$_.tick-le$p.tick})
            if($picked.Count-eq1){$expectedCell=$p.cargo.position}
            Assert-InFlight ($rows.Count-eq1-and$rows[0].kind-ceq'normal'-and$rows[0].pawnId-eq$p.actorId-and$rows[0].jobToken-eq$p.current.token-and$rows[0].jobId-eq$p.current.loadId-and$rows[0].count-eq-1-and$rows[0].maxPawns-eq1-and$null-eq$rows[0].layer-and$rows[0].cell-ceq$expectedCell) 'Native reservation identity/defaults/retained target cell contradict its live source and pickup history.' $Problems
        }}
        if($null-ne$p.anchor-and$p.anchor.destroyed){Assert-InFlight (@($absorbs|Where-Object{$_.outer-lt$row.outer-and$_.source.id-eq$R.anchorId-and(Test-RecurrenceEqual $_.after $p.anchor)}).Count-ge1) 'Destroyed pinned anchor lacks its actual observed absorption history.' $Problems}
    }
    # Require the physical boundaries that the installed drivers necessarily
    # execute, in addition to checking every present ancestry record above.
    foreach($pickup in @($h.transfers|Where-Object{$_.kind-ceq'bulk-pickup'})){
        $receipts=@($Events|Where-Object{$_.phase-ceq'l04-b1-healthy-bulk-pickup'-and$_.tick-eq$pickup.tick})
        $receipt=@($receipts|Where-Object{Test-RecurrenceEqual (Read-RecurrenceJson $_.detail $Problems 'D2 pickup boundary') $pickup})
        $matching=@($splits|Where-Object{$_.job-eq$pickup.jobId-and$_.tick-eq$pickup.tick-and$_.requested-eq$pickup.units-and(Test-RecurrenceEqual $_.result $pickup.original)-and$receipt.Count-eq1-and$_.outer-lt$receipt[0].sequence})
        Assert-InFlight ($receipt.Count-eq1-and$matching.Count-eq1) 'An actual pickup receipt lacks its required preceding native floor SplitOff.' $Problems
    }
    $previous=$null;$handTransitions=0
    foreach($row in $settled){$state=$row.state
        if($null-ne$previous-and$previous.state.inventory-eq10-and$previous.state.hands-eq0-and$state.inventory-eq0-and$state.hands-eq10){
            $from=@($previous.state.things|Where-Object{$_.inventory});$to=@($state.things|Where-Object{$_.hands});$handTransitions++
            $matching=@($splits|Where-Object{$_.outer-gt$previous.outer-and$_.outer-lt$row.outer-and$_.job-eq$state.jobId-and$_.tick-eq$state.tick-and$_.requested-eq10-and$from.Count-eq1-and$to.Count-eq1-and(Test-RecurrenceEqual $_.before $from[0])-and(Test-RecurrenceEqual $_.result $to[0] @('hands','holder'))})
            Assert-InFlight ($state.jobId-eq$h.firstUnloadJobId-and$matching.Count-eq1-and$from.Count-eq1-and$to.Count-eq1-and$from[0].id-eq$to[0].id) 'Actual inventory-to-hands transition lacks its required native whole-stack split.' $Problems
        };$previous=$row
    }
    Assert-InFlight ($handTransitions-eq1) 'The delivery witness lacks its one actual ten-unit inventory-to-hands transition.' $Problems
    Test-InFlightPhysicalReplay $R $Events $Problems
    Test-InFlightNativeStateReplay $R $Events $Problems
}
function New-InFlightCustodyThing($Thing,[string]$Custody,[string]$Cell='') {
    # A new owned DTO, never a mutation of a captured Thing.
    return [pscustomobject]@{id=$Thing.id;count=$Thing.count;destroyed=$false;spawned=($Custody-ceq'map');inventory=($Custody-ceq'inventory');hands=($Custody-ceq'hands');cell=$(if($Custody-ceq'map'){$Cell}else{$null});holder=$(if($Custody-ceq'map'){'Verse.Map'}elseif($Custody-ceq'inventory'){'Verse.Pawn_InventoryTracker'}elseif($Custody-ceq'hands'){'Verse.Pawn_CarryTracker'}else{$null})}
}
function Test-InFlightCensus($Things,$Ledger,[string]$Boundary,$Problems) {
    $expected=@($Ledger.Values|Sort-Object id);$actual=@($Things|Sort-Object id)
    Assert-InFlight (Test-RecurrenceEqual $actual $expected) ('Complete physical Thing census contradicts its retained native receipts at '+$Boundary) $Problems
}
function Test-InFlightPhysicalReplay($R,$Events,$Problems) {
    # Start from the fixture's two real, dynamically assigned source identities.
    # Only retained native physical receipts can change any Thing. In particular,
    # an unrelated second stack cannot change identity between joined snapshots.
    $h=$R.deliveryWitness;$ledger=@{};$pending=@{};$lastAbsorb=$null;$awaitingAbsorbTwin=$false;$settled=0
    for($i=0;$i-lt$h.originalSourceIds.Count;$i++){$thing=[pscustomobject]@{id=$h.originalSourceIds[$i];count=5};$ledger[[int]$thing.id]=New-InFlightCustodyThing $thing map $h.sourceCells[$i]}
    foreach($e in $Events){
        if($awaitingAbsorbTwin-and$e.phase-cne'l04-b1-healthy-absorb'){
            # This fixture's Steel is ThingWithComps. Its successful override
            # calls base; both installed DeclaredOnly postfixes must emit, even
            # though their identical tokens describe one physical consumption.
            Add-InFlightProblem $Problems 'Successful native Steel absorption omitted its adjacent second observer envelope.'
            $awaitingAbsorbTwin=$false
        }
        if($e.phase-ceq'l04-b1-healthy-split'){
            if($e.detail-cnotmatch'^job=([0-9]+); requested=([0-9]+); originalBefore=(\{.*\}); originalAfter=(\{.*\}); result=(null|\{.*\})$'){Add-InFlightProblem $Problems 'Physical replay cannot read a split receipt.';return}
            $job=[int]$Matches[1];$requested=[int]$Matches[2];$texts=@($Matches[3],$Matches[4],$Matches[5]);$parts=@($texts|ForEach-Object{Read-RecurrenceJson $_ $Problems 'Physical replay split'})
            if($parts.Count-ne3-or$null-eq$parts[0]-or$null-eq$parts[1]-or$null-eq$parts[2]){Add-InFlightProblem $Problems 'Physical replay has an incomplete split.';return}
            $id=[int]$parts[0].id;$expected=New-InFlightCustodyThing $parts[0] detached
            Assert-InFlight ($ledger.ContainsKey($id)-and(Test-RecurrenceEqual $ledger[$id] $parts[0])-and$requested-eq$parts[0].count-and(Test-RecurrenceEqual $parts[1] $expected)-and(Test-RecurrenceEqual $parts[2] $expected)-and-not$pending.ContainsKey($id)) 'Native split does not consume the exact retained physical Thing once.' $Problems
            $kind=if($job-eq$h.firstBulkJobId-and$parts[0].spawned-and$id-in$h.originalSourceIds){'pickup'}elseif($job-eq$h.firstUnloadJobId-and$parts[0].inventory){'hands'}else{'unsupported'}
            Assert-InFlight ($kind-cne'unsupported') 'Unreviewed physical split custody/job path.' $Problems
            $ledger[$id]=$expected;$pending[$id]=@{kind=$kind;job=$job;tick=$e.tick;outer=$e.sequence;result=$expected;absorbed=$false}
        }elseif($e.phase-ceq'l04-b1-healthy-absorb'){
            if($e.detail-cnotmatch'^job=([0-9]+); returned=(True|False); targetBefore=(\{.*\}); sourceBefore=(\{.*\}); targetAfter=(\{.*\}); sourceAfter=(\{.*\})$'){Add-InFlightProblem $Problems 'Physical replay cannot read an absorb receipt.';return}
            $job=[int]$Matches[1];$returned=$Matches[2];$texts=@($Matches[3],$Matches[4],$Matches[5],$Matches[6]);$parts=@($texts|ForEach-Object{Read-RecurrenceJson $_ $Problems 'Physical replay absorb'})
            if($parts.Count-ne4-or@($parts|Where-Object{$null-eq$_}).Count){Add-InFlightProblem $Problems 'Physical replay has an incomplete absorb.';return}
            if($null-ne$lastAbsorb-and$lastAbsorb.outer-eq$e.sequence-1-and$lastAbsorb.tick-eq$e.tick-and$lastAbsorb.job-eq$job-and$lastAbsorb.returned-ceq$returned-and(Test-RecurrenceEqual $lastAbsorb.parts $parts)){
                # Exactly the two installed Thing/ThingWithComps hooks can describe
                # the same nested operation. Do not consume its source twice.
                Assert-InFlight ($lastAbsorb.duplicates-eq0) 'More than two identical native absorb envelopes were invented.' $Problems
                $lastAbsorb.duplicates++;$lastAbsorb.outer=$e.sequence;$awaitingAbsorbTwin=$false;continue
            }
            Assert-InFlight (-not$awaitingAbsorbTwin) 'Native Steel absorb observer twins disagree before the next physical receipt.' $Problems
            $target=[int]$parts[0].id;$source=[int]$parts[1].id
            Assert-InFlight ($ledger.ContainsKey($target)-and$ledger.ContainsKey($source)-and$target-ne$source-and(Test-RecurrenceEqual $ledger[$target] $parts[0])-and(Test-RecurrenceEqual $ledger[$source] $parts[1])) 'Native absorb inputs contradict the complete retained physical census.' $Problems
            Assert-InFlight ($pending.ContainsKey($source)-and$pending[$source].kind-ceq'pickup'-and$pending[$source].job-eq$job-and$pending[$source].tick-eq$e.tick-and-not$pending[$source].absorbed-and$job-eq$h.firstBulkJobId-and$returned-ceq'True') 'Native absorb is disconnected from its one pending floor split.' $Problems
            Assert-InFlight ($parts[0].inventory-and(Test-RecurrenceEqual $parts[2] $parts[0] @('count'))-and$parts[2].count-eq$parts[0].count+$parts[1].count-and$parts[3].id-eq$source-and$parts[3].count-eq0-and$parts[3].destroyed-and-not$parts[3].spawned-and-not$parts[3].inventory-and-not$parts[3].hands-and$null-eq$parts[3].cell-and$null-eq$parts[3].holder) 'Native inventory absorb did not preserve target custody and account for the whole consumed source.' $Problems
            $ledger[$target]=$parts[2];[void]$ledger.Remove($source)
            if($pending.ContainsKey($source)){$pending[$source].absorbed=$true}
            $lastAbsorb=@{outer=$e.sequence;tick=$e.tick;job=$job;returned=$returned;parts=$parts;duplicates=0};$awaitingAbsorbTwin=$true
        }elseif($e.phase-ceq'l04-b1-healthy-bulk-pickup'){
            $t=Read-RecurrenceJson $e.detail $Problems 'Physical replay pickup';$id=[int]$t.original.id
            Assert-InFlight ($pending.ContainsKey($id)-and$pending[$id].kind-ceq'pickup'-and$pending[$id].job-eq$t.jobId-and$pending[$id].tick-eq$e.tick-and$pending[$id].outer-lt$e.sequence-and(Test-RecurrenceEqual $pending[$id].result $t.original)-and$t.returnedSuccess-and$t.units-eq$t.original.count) 'Actual inventory handover lacks its exact pending native source split.' $Problems
            if($pending.ContainsKey($id)){
                if(-not$pending[$id].absorbed){Assert-InFlight ($ledger.ContainsKey($id)-and(Test-RecurrenceEqual $ledger[$id] $t.original)) 'Pickup source changed before its native inventory handover.' $Problems;$ledger[$id]=New-InFlightCustodyThing $t.original inventory}
                else{Assert-InFlight (-not$ledger.ContainsKey($id)) 'An absorbed pickup source was resurrected at handover.' $Problems}
                [void]$pending.Remove($id)
            }
            Test-InFlightCensus $t.after.things $ledger ('pickup outer '+$e.sequence) $Problems
        }elseif($e.phase-ceq'l04-b1-healthy-physical-deposit'){
            $t=Read-RecurrenceJson $e.detail $Problems 'Physical replay deposit';$id=[int]$t.original.id
            Test-InFlightCensus $t.before.things $ledger ('deposit input outer '+$e.sequence) $Problems
            Assert-InFlight ($ledger.ContainsKey($id)-and(Test-RecurrenceEqual $ledger[$id] $t.original)-and$t.original.hands-and$t.jobId-eq$h.firstUnloadJobId-and$t.returnedSuccess-and$t.units-eq$t.original.count-and$t.resulting.id-eq$id) 'Native whole-stack deposit does not consume its retained hand custody.' $Problems
            $expected=New-InFlightCustodyThing $t.original map $h.highCell
            Assert-InFlight (Test-RecurrenceEqual $t.resulting $expected) 'Native deposit result differs from the conserved original identity and actual destination.' $Problems
            $ledger[$id]=$expected;Test-InFlightCensus $t.after.things $ledger ('deposit output outer '+$e.sequence) $Problems
        }elseif($e.phase-ceq'l04-b1-healthy-carry-transfer'){
            Add-InFlightProblem $Problems 'This native inventory-transfer fixture unexpectedly used a separate TryStartCarry receipt.'
        }elseif($e.phase-cin@('l04-b1-healthy-free-query','l04-b1-healthy-cell-gate')){
            # BeginFree and CellGate capture the same complete Snapshot plus a
            # separate Describe(subject). Neither is a physical handover receipt.
            $row=Read-RecurrenceJson $e.detail $Problems 'Physical replay query/gate';$id=[int]$row.subject.id
            Test-InFlightCensus $row.before.things $ledger ('query/gate input outer '+$e.sequence) $Problems
            Assert-InFlight ($ledger.ContainsKey($id)-and(Test-RecurrenceEqual $row.subject $ledger[$id])) 'A native query/gate subject differs from its retained physical Thing.' $Problems
        }elseif($e.phase-ceq'l04-b1-healthy-settled-state'){
            $state=Read-RecurrenceJson $e.detail $Problems 'Physical replay settled census';$settled++
            foreach($id in @($pending.Keys)){
                $p=$pending[$id]
                if($p.kind-ceq'hands'){
                    # Native TryTransferToContainer emits SplitOff, then hands are
                    # visible at the next settled boundary; no TryStartCarry call
                    # occurs on this reviewed published unload path.
                    Assert-InFlight ($p.tick-eq$e.tick-and$p.job-eq$state.jobId-and$p.outer-lt$e.sequence-and$ledger.ContainsKey($id)-and(Test-RecurrenceEqual $ledger[$id] $p.result)) 'Inventory split lacks its immediate native hand-transfer boundary.' $Problems
                    $ledger[$id]=New-InFlightCustodyThing $p.result hands;[void]$pending.Remove($id)
                }else{Add-InFlightProblem $Problems 'A floor split reached a settled boundary before its inventory handover.'}
            }
            Test-InFlightCensus $state.things $ledger ('settled outer '+$e.sequence) $Problems
        }
    }
    Assert-InFlight ($settled-gt0-and$pending.Count-eq0-and-not$awaitingAbsorbTwin) 'Physical replay ends without a complete settled census and closed transfers/observer twins.' $Problems
    Test-InFlightCensus $h.finalState.things $ledger 'final result' $Problems
}
function Test-InFlightNativeStateReplay($R,$Events,$Problems) {
    $last=@{};$noteInputs=@{};$latestCache=@{};$knownJobs=@{};$lastCensus=$null;$index=0;$s=Get-InFlightSchemas
    foreach($outer in $Events){
        if($outer.phase-ceq'l04-b1-healthy-settled-state'){$lastCensus=Read-RecurrenceJson $outer.detail $Problems 'D2 native counter source census';continue}
        if($outer.phase-cne'l04-b1-d2-event'){continue};if($index-ge$R.events.Count){continue};$e=$R.events[$index];$index++
        $counter=$null;$cache=$null;$kind=$null;$source=0
        if($null-ne$e.call){$counter=$e.call.counter;$cache=$e.call.cache;$kind=$e.call.kind;$source=$e.call.sourceId}
        elseif($null-ne$e.observation){$counter=$e.observation.counter;$cache=$e.observation.cache;$kind='observation';$source=$R.anchorId}
        if($null-ne$e.call-and$e.call.kind-cin@('native-work-return','start-return')-and$null-ne$e.call.job){$j=$e.call.job;$knownJobs[([int]$j.token).ToString()+':'+([int]$j.loadId).ToString()]=$j}
        if($null-ne$counter){
            Assert-InFlight ($source-in$R.deliveryWitness.originalSourceIds) 'Native counter snapshot names an unobserved source.' $Problems;Test-RecurrenceCounter $counter $source $e.tick $Problems
            $key=[int]$source
            if($kind-ceq'note-return'){
                $id=[int]$e.call.callId
                if(-not$noteInputs.ContainsKey($id)){Add-InFlightProblem $Problems 'Native Note return lacks its physical input counter.'}
                else{$input=$noteInputs[$id];$effective=Get-InFlightEffective $input.counter $e.tick $Problems $input.stack;$next=$effective.count+1
                    if($next-ge6){Assert-InFlight (-not$counter.anchorPresent-and$counter.backoffPresent-and$counter.until-eq$e.tick+2500-and$counter.warned-and$counter.backedOff) 'Native Note threshold transition contradicts its actual prior row.' $Problems}
                    else{Assert-InFlight ($counter.anchorPresent-and$counter.count-eq$next-and$counter.anchorTick-eq$e.tick-and$counter.stackCount-eq$input.stack-and(Test-RecurrenceEqual $counter.backoffPresent $input.counter.backoffPresent)-and(Test-RecurrenceEqual $counter.until $input.counter.until)-and$counter.warned-eq$input.counter.warned) 'Native Note return does not follow the actual gap/shrink/count transition.' $Problems}
                    Assert-InFlight (-not$counter.failPresent) 'Native Note acquired unsupported failure state.' $Problems
                }
            }elseif($last.ContainsKey($key)){
                # IsBackedOff itself does not delete expired rows. Tick-relative
                # suppression can change; retained tuple/stamp data cannot change
                # without an observed mutation in this two-source fixture.
                Assert-InFlight (Test-RecurrenceEqual $counter $last[$key] @('tick','backedOff')) 'Same-source native/passive counter snapshots changed without an observed Note transition.' $Problems
            }else{Assert-InFlight (-not$counter.anchorPresent-and-not$counter.backoffPresent-and-not$counter.warned-and-not$counter.failPresent) 'Fresh source initial counter predates the observed native creation chain.' $Problems}
            if($kind-ceq'note-enter'){
                $things=@(if($null-ne$lastCensus){$lastCensus.things|Where-Object{$_.id-eq$source-and-not$_.destroyed}})
                Assert-InFlight ($things.Count-eq1-and$things[0].count-gt0) 'Native Note input lacks its actual source stack census.' $Problems
                if($things.Count-eq1){$noteInputs[[int]$e.call.callId]=@{counter=$counter;stack=[int]$things[0].count}}
            }
            $last[$key]=$counter
        }
        if($null-ne$cache){
            Assert-InFlight ($cache.tick-eq$e.tick-and$cache.sourceId-eq$source-and$cache.generation-le$e.tick-and$cache.entryPresent-eq($null-ne$cache.pinnedLoadId)-and$cache.entryPresent-eq($null-ne$cache.jobState)-and($cache.dictionaryPresent-or-not$cache.entryPresent)) 'Native/passive cache presence/context is inconsistent.' $Problems
            if($cache.entryPresent){Assert-InFlight ($cache.jobState-eq0-and(($null-eq$cache.actualJob-and$cache.pinnedLoadId-eq-1)-or($null-ne$cache.actualJob-and$cache.actualJob.loadId-eq$cache.pinnedLoadId))) 'Retained cache pin differs from its actual job.' $Problems}
            if($null-ne$cache.actualJob){Assert-InFlight ($null-eq$cache.actualJob.driver) 'A cache descriptor invents a driver that the source reader never supplies.' $Problems}
            $key=[int]$source
            if($kind-cnotin@('build-enter','try-build-return')){
                if($latestCache.ContainsKey($key)){
                    $prior=$latestCache[$key];$expectedJob=$prior.actualJob
                    if($null-ne$expectedJob){$jobKey=([int]$expectedJob.token).ToString()+':'+([int]$expectedJob.loadId).ToString();if($knownJobs.ContainsKey($jobKey)){$expectedJob=$knownJobs[$jobKey]}}
                    # A cache stores a Job reference. Native selection/start can
                    # set workgiver/startTick on that same object without writing
                    # the cache entry. Require the independently captured native
                    # descriptor, not an unconstrained ignored field list.
                    Assert-InFlight ((Test-RecurrenceEqual $cache $prior @('tick','actualJob'))-and(Test-RecurrenceEqual $cache.actualJob $expectedJob @('driver'))) 'Retained cache differs from its entry or actual native job descriptor.' $Problems
                }else{Assert-InFlight ($kind-cne'observation'-and-not$cache.dictionaryPresent-and-not$cache.entryPresent-and$cache.generation-eq0-and$null-eq$cache.actualJob) 'First source cache predates its observed native initialization.' $Problems}
            }
            if($kind-cne'observation'){
                if($kind-ceq'build-enter'){Assert-InFlight ($cache.dictionaryPresent-and$cache.generation-eq$e.tick-and-not$cache.entryPresent-and$null-eq$cache.actualJob) 'Actual build did not follow its native cache generation preparation.' $Problems}
                if($kind-ceq'try-build-return'-and$null-ne$e.call.job){Assert-InFlight ($cache.entryPresent-and$cache.generation-eq$e.tick-and(Test-RecurrenceEqual $cache.actualJob $e.call.job)-and$cache.pinnedLoadId-eq$e.call.job.loadId) 'Native TryBuild return lacks its actual cache write/hit.' $Problems}
            }
            $latestCache[$key]=$cache
        }
    }
}

# Dedicated wrappers retain original D2 calls and the complete healthy raw census.
function Test-InFlightDelivery($R,$Problems) {
    $taskH=$R.deliveryWitness
    foreach($taskFlag in @('fixtureValid','requestedBehaviorSatisfied','expectationMatched','ordinaryChain','exactPickup','physicalUnload','successfulCleanups','conserved','observerHealthy','layoutIntact','followupWindowComplete','settledHigh','noRehaul')){Assert-Recurrence $taskH.$taskFlag ('D2 delivery witness lacks '+$taskFlag) $Problems}
    Assert-Recurrence ($taskH.caseId-ceq'L04-B1-HEALTHY'-and$taskH.expectedBehavior-ceq'satisfied'-and$taskH.status-ceq'passed'-and-not$taskH.baselineGapObserved-and-not$taskH.timedOut-and$taskH.startedTick-ge$R.startedTick-and$taskH.finishedTick-eq$R.finishedTick-and$taskH.finishedTick-$taskH.startedTick-lt6000-and$taskH.finishedTick-$taskH.firstUnloadEndTick-ge600-and$taskH.finishedTick-$taskH.stableSinceTick-ge600-and$taskH.stableSinceTick-ge$taskH.firstUnloadEndTick-and$taskH.stableDistinctTicks-ge600) 'D2 delivery witness result identity/lifetime/stable window mismatch.' $Problems
    Assert-Recurrence ($taskH.originalSourceIds.Count-eq2-and@($taskH.originalSourceIds|Select-Object -Unique).Count-eq2-and$taskH.sourceCells.Count-eq2-and@($taskH.sourceCells|Select-Object -Unique).Count-eq2-and$taskH.highCell-cnotin$taskH.sourceCells) 'D2 delivery witness source/destination catalog mismatch.' $Problems
    foreach($taskCell in @($taskH.highCell)+@($taskH.sourceCells)){Get-RecurrenceCell $taskCell $Problems|Out-Null}
    foreach($taskName in @('firstBulkPicked','firstUnloadHigh','minTotal','maxTotal')){Assert-Recurrence ($taskH.$taskName-eq10) ('D2 delivery witness '+$taskName+' mismatch.') $Problems}
    foreach($taskName in @('firstUnloadSource','firstUnloadElsewhere','sourceZoneNetZeroBulkCycles','laterSourceReacquired','laterNativeHandDelivered')){Assert-Recurrence ($taskH.$taskName-eq0) ('D2 delivery witness '+$taskName+' is nonzero.') $Problems}
    Assert-Recurrence (@($taskH.jobs.id|Select-Object -Unique).Count-eq$taskH.jobs.Count) 'D2 delivery witness duplicate job identity.' $Problems
    $taskBulk=@($taskH.jobs|Where-Object{$_.def-ceq'HaulersDream_BulkHaul'});$taskUnload=@($taskH.jobs|Where-Object{$_.def-ceq'HaulersDream_UnloadInventory'})
    if($taskBulk.Count-ne1-or$taskUnload.Count-ne1){Add-RecurrenceProblem $Problems 'D2 delivery witness needs one actual bulk and unload job.';return};$taskBulk=$taskBulk[0];$taskUnload=$taskUnload[0]
    Assert-Recurrence ($taskBulk.id-eq$taskH.firstBulkJobId-and$taskUnload.id-eq$taskH.firstUnloadJobId-and$taskBulk.id-ne$taskUnload.id-and$taskBulk.driver-ceq'HaulersDream.JobDriver_BulkHaul'-and$taskUnload.driver-ceq'HaulersDream.JobDriver_UnloadHauledInventory'-and$taskBulk.workgiver-ceq'HaulGeneral'-and$taskBulk.workgiverClass-ceq'RimWorld.WorkGiver_HaulGeneral'-and$taskBulk.candidateObserved-and-not$taskBulk.forced-and-not$taskUnload.forced-and$taskBulk.endCondition-ceq'Succeeded'-and$taskUnload.endCondition-ceq'Succeeded'-and$taskBulk.released-and$taskUnload.released-and$taskBulk.observedTick-le$taskBulk.endTick-and$taskBulk.endTick-le$taskUnload.observedTick-and$taskUnload.observedTick-le$taskUnload.endTick-and$taskUnload.endTick-eq$taskH.firstUnloadEndTick) 'D2 delivery witness native job/cleanup provenance mismatch.' $Problems
    Assert-Recurrence (@($taskH.jobs|Where-Object{$_.def-cin@('HaulToCell','HaulToContainer')}).Count-eq0) 'D2 delivery witness used native hand-haul rescue.' $Problems
    $taskQueue=@($taskBulk.queue-split',');$taskIds=@();foreach($taskPart in $taskQueue){if($taskPart-cmatch'^([0-9]+)x5$'){$taskIds+=([int]$Matches[1])}else{Add-RecurrenceProblem $Problems 'D2 delivery witness malformed initial 5+5 queue.'}}
    Assert-Recurrence ($taskIds.Count-eq2-and@($taskIds|Select-Object -Unique).Count-eq2-and@($taskIds|Where-Object{$_-notin$taskH.originalSourceIds}).Count-eq0) 'D2 delivery witness initial bulk queue does not name both original sources.' $Problems
    $taskPickups=@($taskH.transfers|Where-Object{$_.kind-ceq'bulk-pickup'});Assert-Recurrence ($taskPickups.Count-eq2-and@($taskPickups.original.id|Select-Object -Unique).Count-eq2) 'D2 delivery witness pickup receipt count/identity mismatch.' $Problems
    for($taskI=0;$taskI-lt$taskPickups.Count;$taskI++){$taskT=$taskPickups[$taskI];Assert-Recurrence ($taskT.jobId-eq$taskBulk.id-and$taskT.returnedSuccess-and$taskT.units-eq5-and$null-ne$taskT.original-and$taskT.original.id-in$taskH.originalSourceIds-and$taskT.original.count-eq5-and-not$taskT.original.destroyed-and$null-eq$taskT.before-and$null-eq$taskT.resulting-and$taskT.after.inventory-eq(5*($taskI+1))-and$taskT.after.source-eq(5*(1-$taskI))-and$taskT.after.high-eq0-and$taskT.after.hands-eq0-and$taskT.after.jobId-eq$taskBulk.id-and$taskT.after.jobDef-ceq$taskBulk.def) 'D2 delivery witness physical pickup receipt mismatch.' $Problems;Test-RecurrenceHealthyState $taskT.after $taskH $Problems}
    $taskDrops=@($taskH.transfers|Where-Object{$_.kind-ceq'carry-drop'});Assert-Recurrence ($taskDrops.Count-eq1) 'D2 delivery witness needs exactly one physical deposit.' $Problems
    foreach($taskT in $taskDrops){Assert-Recurrence ($taskT.jobId-eq$taskUnload.id-and$taskT.returnedSuccess-and$taskT.units-eq10-and$null-ne$taskT.original-and$null-ne$taskT.resulting-and$null-ne$taskT.before) 'D2 delivery witness deposit lacks actual original/resulting custody.' $Problems;if($null-eq$taskT.before-or$null-eq$taskT.resulting){continue}
        Assert-Recurrence ($taskT.before.inventory-eq0-and$taskT.before.hands-eq10-and$taskT.before.source-eq0-and$taskT.before.high-eq0-and$taskT.before.jobId-eq$taskUnload.id-and$taskT.after.jobId-eq$taskUnload.id-and$taskT.before.jobDef-ceq$taskUnload.def-and$taskT.after.jobDef-ceq$taskUnload.def-and$taskT.original.hands-and$taskT.original.count-eq10-and@($taskT.before.things|Where-Object{(Test-RecurrenceEqual $_ $taskT.original)}).Count-eq1-and$taskT.resulting.spawned-and-not$taskT.resulting.destroyed-and$taskT.resulting.count-eq10-and$taskT.resulting.cell-ceq$taskH.highCell-and$taskT.after.high-eq10-and$taskT.after.inventory-eq0-and$taskT.after.hands-eq0-and@($taskT.after.things|Where-Object{(Test-RecurrenceEqual $_ $taskT.resulting)}).Count-eq1) 'D2 delivery witness drop did not move its actually observed ten-unit hand stack into the high destination.' $Problems
        Test-RecurrenceHealthyState $taskT.before $taskH $Problems;Test-RecurrenceHealthyState $taskT.after $taskH $Problems}
    if($taskPickups.Count-eq2-and$taskDrops.Count-eq1){
        # This fixture has exactly ten units and an empty destination. Its one
        # ten-unit whole-stack drop cannot acquire a new Thing identity on the way.
        $taskDrop=$taskDrops[0]
        Assert-Recurrence ($taskDrop.tick-ge$taskPickups[-1].tick-and$taskDrop.original.id-eq$taskDrop.resulting.id-and@($taskPickups[-1].after.things|Where-Object{$_.inventory-and$_.id-eq$taskDrop.original.id}).Count-eq1) 'D2 delivery witness deposited identity is disconnected from the preceding real inventory and whole-stack drop.' $Problems
    }
    $taskLastSequence=0
    foreach($taskT in $taskH.transfers){Assert-Recurrence ($taskT.kind-cin@('bulk-pickup','carry-drop','start-carry')-and$taskT.sequence-gt$taskLastSequence-and$taskT.sequence-eq$taskT.after.sequence-and$taskT.tick-eq$taskT.after.tick-and$taskT.tick-ge$taskH.startedTick-and$taskT.tick-le$taskH.finishedTick) 'D2 delivery witness transfer chronology/type mismatch.' $Problems;$taskLastSequence=$taskT.sequence
        if($null-ne$taskT.before){Assert-Recurrence ($taskT.before.sequence-lt$taskT.sequence-and$taskT.before.tick-eq$taskT.tick-and$taskT.before.jobId-eq$taskT.jobId) 'D2 delivery witness transfer input is disconnected from its actual synchronous invocation.' $Problems;Test-RecurrenceHealthyState $taskT.before $taskH $Problems}
        Test-RecurrenceHealthyState $taskT.after $taskH $Problems
        if($taskT.kind-ceq'start-carry'){Assert-Recurrence ($null-ne$taskT.before-and$null-eq$taskT.original-and$null-eq$taskT.resulting-and$taskT.units-eq[math]::Max(0,$taskT.after.inventory+$taskT.after.hands-$taskT.before.inventory-$taskT.before.hands)) 'D2 delivery witness start-carry conservation mismatch.' $Problems}}
    Assert-Recurrence ($taskH.queries.Count-gt0-and$taskH.gates.Count-gt0) 'D2 delivery witness lacks actual production storage query/cell-gate observations.' $Problems
    $taskOwnClaimWitness=$false
    foreach($taskQ in $taskH.queries){Assert-Recurrence ($taskQ.sequence-eq$taskQ.before.sequence-and$taskQ.tick-eq$taskQ.before.tick-and$taskQ.tick-ge$taskH.startedTick-and$taskQ.tick-le$taskH.finishedTick-and$taskQ.jobId-eq$taskQ.before.jobId-and$taskQ.jobDef-ceq$taskQ.before.jobDef-and$taskQ.free-ge0-and$taskQ.delivering-in@(-1,0,1)-and$taskQ.productionLiveUnits-ge-1-and$taskQ.taggedInventory-ge0-and($taskQ.group-ceq('critical@'+$taskH.highCell))-eq$taskQ.highGroup) 'D2 delivery witness actual free-query record mismatch.' $Problems;Test-RecurrenceHealthyState $taskQ.before $taskH $Problems
        if($taskQ.highGroup-and$taskQ.subject.inventory-and$taskQ.before.inventory-eq10-and$taskQ.taggedInventory-eq10-and$taskQ.before.source-eq0-and$taskQ.before.high-eq0-and@($taskQ.before.claims|Where-Object{$_.high-and$_.recordedUnits-eq10}).Count-gt0){$taskOwnClaimWitness=$true}}
    Assert-Recurrence ($taskH.ownClaimInventoryWitness-eq$taskOwnClaimWitness) 'D2 delivery witness own-claim inventory aggregate differs from its actual diagnostic queries.' $Problems
    foreach($taskG in $taskH.gates){Assert-Recurrence ($taskG.sequence-eq$taskG.before.sequence-and$taskG.tick-eq$taskG.before.tick-and$taskG.tick-ge$taskH.startedTick-and$taskG.tick-le$taskH.finishedTick-and$taskG.jobId-eq$taskG.before.jobId-and$taskG.jobDef-ceq$taskG.before.jobDef) 'D2 delivery witness actual cell-gate context mismatch.' $Problems;Test-RecurrenceHealthyState $taskG.before $taskH $Problems}
    Test-RecurrenceHealthyState $taskH.finalState $taskH $Problems $true
    Assert-Recurrence ($taskH.finalState.tick-eq$taskH.finishedTick-and$taskH.finalState.claims.Count-eq0) 'D2 delivery witness final state tick or empty recorded-claim ledger mismatch.' $Problems
    $taskD=@(Get-InFlightCalls $R);$taskStarts=@($taskD|Where-Object{$_.kind-ceq'start-return'-and$_.actualCurrent-eq$true-and$_.job.loadId-eq$taskBulk.id})
    foreach($taskActual in @($taskD|Where-Object{$_.kind-ceq'start-return'-and$_.actualCurrent-eq$true})){
        $taskJob=@($taskH.jobs|Where-Object{$_.id-eq$taskActual.job.loadId})
        Assert-Recurrence ($taskJob.Count-eq1-and$taskActual.job.def-ceq$taskJob[0].def-and$taskActual.job.driver-ceq$taskJob[0].driver-and$taskActual.job.forced-eq$taskJob[0].forced-and$taskActual.job.workgiver-ceq$taskJob[0].workgiver-and$taskActual.job.workgiverClass-ceq$taskJob[0].workgiverClass-and$taskActual.tick-eq$taskJob[0].observedTick-and$taskActual.job.startTick-eq$taskActual.tick-and(Test-RecurrenceEqual $taskActual.inputJob $taskActual.job @('driver'))) 'D2 delivery witness actual StartJob descriptor differs from its independent actual-current record.' $Problems
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
    };Assert-Recurrence $taskChain 'D2 delivery witness lacks the actual native query/selection/start chain for its executed bulk job.' $Problems
}

function Test-InFlightDeliveryRaw($R,$Events,$Problems,$Assemblies) {
    $taskH=$R.deliveryWitness;$taskSchemas=Get-RecurrenceSchemas;$taskCalls=@(Get-InFlightCalls $R)
    $taskLifetimes=Get-RecurrenceHealthyLifetimes $Events $Problems
    $taskFixture=Get-RecurrenceOne $Events phase 'l04-b1-healthy-fixture' $Problems 'D2 delivery witness fixture'
    if($null-ne$taskFixture){
        $taskPattern='^case=L04-B1-HEALTHY; actor=Human([0-9]+); source='+[regex]::Escape(($taskH.sourceCells-join','))+'; high='+[regex]::Escape($taskH.highCell)+'; ordinary workgiver only; no runtime job/claim injection; first unload then at least600 stable ticks; healthy on both selections\.$'
        Assert-Recurrence ($taskFixture.detail-cmatch$taskPattern-and$taskFixture.tick-eq$taskH.startedTick) 'D2 delivery witness fixture identity/layout payload mismatch.' $Problems
        if($taskFixture.detail-cmatch$taskPattern){$taskActor=[int]$Matches[1];Assert-Recurrence ($taskActor-eq$R.queries[0].before.physical.actorId-and@($taskCalls|Where-Object{$_.actorId-gt0-and$_.actorId-ne$taskActor}).Count-eq0) 'D2 delivery witness dispatch uses a different/reused actor.' $Problems}
    }
    $taskExpectedBindings=@{'RimWorld.WorkGiver_HaulGeneral.JobOnThing'=1;'HaulersDream.StorageCommitments.FreeUnitsFor'=1;'HaulersDream.StorageCommitments.IsDelivering'=1;'HaulersDream.StorageCommitments.UnitsMoving'=1;'RimWorld.StoreUtility.IsGoodStoreCell'=1;'HaulersDream.JobDriver_BulkHaul.DepositSwept'=1;'Verse.AI.Pawn_JobTracker.CleanupCurrentJob'=1;'Verse.AI.JobDriver.DriverTick'=1;'Verse.AI.JobDriver.DriverTickInterval'=1;'Verse.AI.JobDriver.TryActuallyStartNextToil'=1;'Verse.Pawn_CarryTracker.TryStartCarry'=2;'Verse.Pawn_CarryTracker.TryDropCarriedThing'=2;'Verse.Thing.SplitOff'=1;'Verse.Thing.TryAbsorbStack'=1;'Verse.ThingWithComps.TryAbsorbStack'=1}
        $taskSeenBindings=@{};$taskTokens=@{};$taskBinds=@($Events|Where-Object{$_.phase-ceq'l04-b1-healthy-observer-bind'})
    foreach($taskBind in $taskBinds){
        if($taskBind.detail-cnotmatch'^([^;]+); token=([0-9]+); assembly=(.*); MVID=([a-fA-F0-9-]+)$'){Add-RecurrenceProblem $Problems 'Malformed D2 delivery witness observer binding.';continue}
        $taskMethod=$Matches[1];$taskToken=[long]$Matches[2];$taskFull=$Matches[3];$taskMvid=$Matches[4];$taskName=if($taskMethod.StartsWith('HaulersDream.')){'HaulersDream'}else{'Assembly-CSharp'}
        $taskAssembly=Get-RecurrenceOne $Assemblies name $taskName $Problems 'D2 delivery witness loaded binding'
        Assert-Recurrence ($taskExpectedBindings.ContainsKey($taskMethod)-and($taskToken-shr24)-eq6-and-not$taskTokens.ContainsKey($taskMvid+'/'+$taskToken)-and$null-ne$taskAssembly-and$taskMvid-eq$taskAssembly.moduleVersionId-and$taskFull-ceq($taskName+', Version='+$taskAssembly.assemblyVersion+', Culture=neutral, PublicKeyToken=null')) 'D2 delivery witness observer method/token/loaded module mismatch.' $Problems
        Test-RecurrenceExactMember $Assemblies $taskMethod $taskToken method $Problems
        $taskTokens[$taskMvid+'/'+$taskToken]=$true;if(-not$taskSeenBindings.ContainsKey($taskMethod)){$taskSeenBindings[$taskMethod]=0};$taskSeenBindings[$taskMethod]++
    }
    Assert-Recurrence ($taskBinds.Count-eq17) 'D2 delivery witness observer binding count mismatch.' $Problems
    foreach($taskMethod in $taskExpectedBindings.Keys){Assert-Recurrence ($taskSeenBindings.ContainsKey($taskMethod)-and$taskSeenBindings[$taskMethod]-eq$taskExpectedBindings[$taskMethod]) ('D2 delivery witness missing observer overload '+$taskMethod) $Problems}
    $taskBulkCandidates=0
    foreach($taskRow in @($Events|Where-Object{$_.phase-ceq'l04-b1-healthy-candidate'})){
        if($taskRow.detail-cnotmatch'^id=([0-9]+); def=([^;]+); source=([0-9]+); forcedArgument=(True|False); queue=(.*)$'){Add-RecurrenceProblem $Problems 'Malformed D2 delivery witness native candidate record.';continue}
        $taskId=[int]$Matches[1];$taskDef=$Matches[2];$taskSource=[int]$Matches[3];$taskForced=$Matches[4];$taskQueue=$Matches[5]
        $taskReturns=@($taskCalls|Where-Object{$_.kind-ceq'candidate-return'-and$_.tick-eq$taskRow.tick-and$null-ne$_.job-and$_.job.loadId-eq$taskId-and$_.sourceId-eq$taskSource})
        Assert-Recurrence ($taskReturns.Count-ge1-and$taskSource-in$taskH.originalSourceIds-and$taskForced-ceq'False') 'D2 delivery witness candidate lacks matching native query return/source.' $Problems
        $taskMatching=@($taskReturns|Where-Object{
            $job=$_.job;$parts=@();for($i=0;$i-lt$job.queueIds.Count;$i++){$parts+=([string]$job.queueIds[$i]+'x'+$job.counts[$i])}
            $job.def-ceq$taskDef-and($parts-join',')-ceq$taskQueue
        })
        Assert-InFlight ($taskMatching.Count-ge1) 'Independent healthy candidate descriptor differs from the retained actual D2 query return.' $Problems
        if($taskId-eq$taskH.firstBulkJobId){$taskJob=Get-RecurrenceOne $taskH.jobs id $taskId $Problems 'D2 executed candidate';Assert-InFlight ($taskDef-ceq'HaulersDream_BulkHaul'-and$null-ne$taskJob-and$taskQueue-ceq$taskJob.queue) 'D2 first candidate descriptor differs from its executed job.' $Problems;if($taskRow.tick-le$taskJob.observedTick){$taskBulkCandidates++}}
    }
    Assert-Recurrence ($taskBulkCandidates-ge1) 'D2 delivery witness lacks the independent healthy observer candidate witness.' $Problems
    foreach($taskPair in @(@('bulk-pickup','bulk-pickup'),@('carry-drop','physical-deposit'),@('start-carry','carry-transfer'))){
        $taskRows=@($Events|Where-Object{$_.phase-ceq('l04-b1-healthy-'+$taskPair[1])});$taskExpected=@($taskH.transfers|Where-Object{$_.kind-ceq$taskPair[0]})
        Assert-Recurrence ($taskRows.Count-eq$taskExpected.Count) 'Raw D2 delivery witness transfer cardinality mismatch.' $Problems
        for($taskI=0;$taskI-lt$taskRows.Count;$taskI++){$taskDto=Read-RecurrenceJson $taskRows[$taskI].detail $Problems 'D2 delivery witness transfer';if(-not(Test-RecurrenceNode $taskDto transfer 'D2 delivery witness raw transfer' $taskSchemas $Problems)){continue};Assert-Recurrence ($taskI-lt$taskExpected.Count-and(Test-RecurrenceEqual $taskDto $taskExpected[$taskI])-and$taskRows[$taskI].tick-eq$taskDto.tick) 'Raw D2 delivery witness transfer differs from result receipt.' $Problems;Test-RecurrenceHealthyLifetime $taskDto.before $taskLifetimes $taskRows[$taskI].sequence $Problems;Test-RecurrenceHealthyLifetime $taskDto.after $taskLifetimes $taskRows[$taskI].sequence $Problems}
    }
    foreach($taskPair in @(@('queries','free-query','query'),@('gates','cell-gate','gate'))){$taskRows=@($Events|Where-Object{$_.phase-ceq('l04-b1-healthy-'+$taskPair[1])});$taskExpected=$taskH.($taskPair[0]);Assert-Recurrence ($taskRows.Count-eq$taskExpected.Count) 'D2 delivery witness query/gate raw cardinality mismatch.' $Problems
        for($taskI=0;$taskI-lt$taskRows.Count;$taskI++){$taskDto=Read-RecurrenceJson $taskRows[$taskI].detail $Problems 'D2 delivery witness query/gate';if(Test-RecurrenceNode $taskDto $taskPair[2] 'D2 delivery witness raw query/gate' $taskSchemas $Problems){Assert-Recurrence ($taskI-lt$taskExpected.Count-and(Test-RecurrenceEqual $taskDto $taskExpected[$taskI])-and$taskRows[$taskI].tick-eq$taskDto.tick) 'D2 delivery witness query/gate raw payload mismatch.' $Problems;Test-RecurrenceHealthyLifetime $taskDto.before $taskLifetimes $taskRows[$taskI].sequence $Problems}}}
    $taskCurrents=@($Events|Where-Object{$_.phase-ceq'l04-b1-healthy-current-job'});Assert-Recurrence ($taskCurrents.Count-eq$taskH.jobs.Count) 'D2 delivery witness actual-current job catalog mismatch.' $Problems
    foreach($taskRow in $taskCurrents){$taskDto=Read-RecurrenceJson $taskRow.detail $Problems 'D2 delivery witness current job';if(-not(Test-RecurrenceNode $taskDto hjob 'D2 delivery witness actual current job' $taskSchemas $Problems)){continue};$taskJob=Get-RecurrenceOne $taskH.jobs id $taskDto.id $Problems 'D2 delivery witness job'
        Assert-Recurrence ($null-ne$taskJob-and(Test-RecurrenceEqual $taskDto $taskJob @('endTick','endCondition','released'))-and$taskDto.endTick-eq-1-and$null-eq$taskDto.endCondition-and-not$taskDto.released-and$taskRow.tick-eq$taskDto.observedTick) 'D2 delivery witness actual-current job payload/lifecycle mismatch.' $Problems}
    $taskCleanups=@($Events|Where-Object{$_.phase-ceq'l04-b1-healthy-job-cleanup'});Assert-Recurrence ($taskCleanups.Count-eq@($taskH.jobs|Where-Object{$_.endTick-ge0}).Count) 'D2 delivery witness cleanup multiplicity mismatch.' $Problems
    $taskCleanupIds=@();foreach($taskRow in $taskCleanups){$taskDto=Read-RecurrenceJson $taskRow.detail $Problems 'D2 delivery witness cleanup';if(-not(Test-RecurrenceNode $taskDto hjob 'D2 delivery witness cleanup' $taskSchemas $Problems)){continue};$taskJob=Get-RecurrenceOne $taskH.jobs id $taskDto.id $Problems 'D2 delivery witness completed job';$taskCleanupIds+=@($taskDto.id)
        Assert-Recurrence ($null-ne$taskJob-and(Test-RecurrenceEqual $taskDto $taskJob)-and$taskRow.tick-eq$taskDto.endTick) 'D2 delivery witness cleanup differs from final job record.' $Problems};Assert-Recurrence (@($taskCleanupIds|Select-Object -Unique).Count-eq$taskCleanupIds.Count) 'Duplicate D2 delivery witness cleanup.' $Problems
    $taskStates=@($Events|Where-Object{$_.phase-ceq'l04-b1-healthy-settled-state'});$taskStable=@{};$taskLastSequence=0;$taskLastTick=$taskH.startedTick;$taskDistinct=@{};$taskCensuses=New-Object 'System.Collections.Generic.List[object]'
    foreach($taskRow in $taskStates){$taskDto=Read-RecurrenceJson $taskRow.detail $Problems 'D2 delivery witness settled state';if(-not(Test-RecurrenceNode $taskDto hstate 'D2 delivery witness settled state' $taskSchemas $Problems)){continue}
        Assert-Recurrence ($taskDto.sequence-gt$taskLastSequence-and$taskDto.tick-ge$taskLastTick-and$taskDto.tick-eq$taskRow.tick-and$taskDto.tick-le$taskH.finishedTick) 'D2 delivery witness settled observation ordering mismatch.' $Problems;$taskLastSequence=$taskDto.sequence;$taskLastTick=$taskDto.tick;$taskDistinct[$taskDto.tick]=$true
        Test-RecurrenceHealthyState $taskDto $taskH $Problems ($taskDto.tick-ge$taskH.stableSinceTick)
        Test-RecurrenceHealthyLifetime $taskDto $taskLifetimes $taskRow.sequence $Problems
        [void]$taskCensuses.Add(@{sequence=$taskRow.sequence;state=$taskDto})
        if($taskDto.tick-ge$taskH.stableSinceTick){$taskStable[$taskDto.tick]=$true}
    }
    Assert-Recurrence ($taskStates.Count-gt0-and$taskH.settledBoundaries-ge$taskStates.Count-and$taskDistinct.ContainsKey($taskH.startedTick)) 'D2 delivery witness settled-state capture is missing.' $Problems
    for($taskTick=$taskH.startedTick;$taskTick-le$taskH.finishedTick;$taskTick++){Assert-Recurrence ($taskDistinct.ContainsKey($taskTick)) ('Missing actual D2 delivery witness tick '+$taskTick) $Problems}
    for($taskTick=$taskH.stableSinceTick;$taskTick-le$taskH.finishedTick;$taskTick++){Assert-Recurrence ($taskStable.ContainsKey($taskTick)) ('Missing D2 delivery witness stable tick '+$taskTick) $Problems}
    Assert-Recurrence ($taskH.stableDistinctTicks-eq($taskH.finishedTick-$taskH.stableSinceTick+1)) 'D2 delivery witness stable distinct-tick aggregate mismatch.' $Problems
    $taskAbsorbs=New-Object 'System.Collections.Generic.List[object]';$taskSplits=New-Object 'System.Collections.Generic.List[object]'
    foreach($taskRow in @($Events|Where-Object{$_.phase-cin@('l04-b1-healthy-split','l04-b1-healthy-absorb')})){
        $taskPrior=@($taskCensuses|Where-Object{$_.sequence-lt$taskRow.sequence}|Select-Object -Last 1)
        Assert-Recurrence ($taskPrior.Count-eq1) 'Actual ancestry lacks a preceding full physical census.' $Problems
        if($taskRow.phase-ceq'l04-b1-healthy-split'){
            if($taskRow.detail-cnotmatch'^job=([0-9]+); requested=([0-9]+); originalBefore=(\{.*\}); originalAfter=(\{.*\}); result=(null|\{.*\})$'){Add-RecurrenceProblem $Problems 'Malformed actual split ancestry record.';continue}
            $taskJob=[int]$Matches[1];$taskRequested=[int]$Matches[2];$taskTexts=@($Matches[3],$Matches[4],$Matches[5]);$taskParts=@($taskTexts|ForEach-Object{Read-RecurrenceJson $_ $Problems 'Split ancestry'})
            if($taskLifetimes.ContainsKey($taskJob)){Test-RecurrenceHealthyLifetime ([pscustomobject]@{jobId=$taskJob;jobDef=$taskLifetimes[$taskJob].job.def}) $taskLifetimes $taskRow.sequence $Problems}else{Add-RecurrenceProblem $Problems 'D2 delivery witness split lacks an actually observed job lifetime.'}
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
            if($taskLifetimes.ContainsKey($taskJob)){Test-RecurrenceHealthyLifetime ([pscustomobject]@{jobId=$taskJob;jobDef=$taskLifetimes[$taskJob].job.def}) $taskLifetimes $taskRow.sequence $Problems}else{Add-RecurrenceProblem $Problems 'D2 delivery witness absorb lacks an actually observed job lifetime.'}
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
            Assert-Recurrence ($taskMerge.Count-ge1) 'D2 delivery witness original pickup identity disappeared without an observed conserved merge into the resulting inventory.' $Problems
        }
    }
}

function Test-InFlightBindings($Value,$Problems) {
    $taskR=$Value.inFlightRecurrence;$taskAssemblies=@{}
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
    $taskSeen=@{};$taskAssemblySeen=@{};$taskPassiveSeen=@{};$taskPassive=Get-InFlightPassiveMembers
    foreach($taskBinding in $taskR.bindings){
        if($taskBinding-cmatch'^passive-(field|getter)=([^;]+);type=(.*);token=([0-9]+);mvid=([a-fA-F0-9-]+)$'){
            $kind=if($Matches[1]-ceq'field'){'field'}else{'method'};$name=$Matches[2];$type=$Matches[3];$token=[long]$Matches[4];$mvid=$Matches[5]
            Assert-InFlight ($taskPassive.ContainsKey($name)-and-not$taskPassiveSeen.ContainsKey($name)) 'Unknown/duplicate passive member.' $Problems
            if($taskPassive.ContainsKey($name)){Assert-InFlight ($type-ceq$taskPassive[$name].type-and$kind-ceq$taskPassive[$name].kind-and$mvid-eq$taskAssemblies['Assembly-CSharp'].moduleVersionId) 'Passive reader declared type/kind/module mismatch.' $Problems}
            Test-InFlightExactMember $Value.assemblies $name $token $kind $Problems;$taskPassiveSeen[$name]=$true
        }elseif($taskBinding-match'^(.*);path=(.*);mvid=([a-fA-F0-9-]+)$'){
            $taskFull=$Matches[1];$taskPath=$Matches[2];$taskMvid=$Matches[3];$taskName=($taskFull-split',')[0]
            Assert-Recurrence ($taskName-cin@('HaulersDream','HaulersDream.Core','Assembly-CSharp','HaulersDream.RuntimeHarness')-and$taskAssemblies.ContainsKey($taskName)-and-not$taskAssemblySeen.ContainsKey($taskName)) 'Unknown/duplicate production bridge assembly binding.' $Problems
            if($taskAssemblies.ContainsKey($taskName)){$taskA=$taskAssemblies[$taskName];Assert-Recurrence ($taskPath-ceq$taskA.path-and$taskMvid-eq$taskA.moduleVersionId-and$taskFull-ceq($taskName+', Version='+$taskA.assemblyVersion+', Culture=neutral, PublicKeyToken=null')) 'Bridge binding does not match the actual loaded assembly.' $Problems};$taskAssemblySeen[$taskName]=$true
        }elseif($taskBinding-match'^([^;]+);token=([0-9]+)(?:;mvid=([a-fA-F0-9-]+))?$'){
            $taskName=$Matches[1];$taskToken=[long]$Matches[2];$taskMvid=$Matches[3]
            Assert-Recurrence ($taskNeeded.ContainsKey($taskName)-and-not$taskSeen.ContainsKey($taskName)) 'Unexpected/duplicate exact field/method binding.' $Problems
            if($taskNeeded.ContainsKey($taskName)){$taskTable=if($taskNeeded[$taskName]-ceq'field'){4}else{6};Assert-Recurrence (($taskToken-shr24)-eq$taskTable-and($taskToken-band16777215)-gt0-and($taskTable-eq4-or$taskMvid-eq$taskHd.moduleVersionId)) 'Binding token table/module mismatch.' $Problems;Test-InFlightExactMember $Value.assemblies ($taskName-replace'^installed=','') $taskToken $taskNeeded[$taskName] $Problems};$taskSeen[$taskName]=$true
        }else{Add-RecurrenceProblem $Problems 'Unrecognized production binding text.'}
    };Assert-Recurrence ($taskSeen.Count-eq12-and$taskAssemblySeen.Count-eq4-and$taskR.bindings.Count-eq29-and$taskPassiveSeen.Count-eq13) 'Production binding catalog is incomplete.' $Problems
    $taskMethods=@{'RimWorld.WorkGiver_Scanner.HasJobOnThing'='Assembly-CSharp';'RimWorld.WorkGiver_HaulGeneral.JobOnThing'='Assembly-CSharp';'HaulersDream.BulkHaul.TryBuildBulkJob'='HaulersDream';'HaulersDream.BulkHaul.BuildBulkJob'='HaulersDream';'HaulersDream.HaulChurnGuard.NoteBulkAnchor'='HaulersDream';'HaulersDream.HDLog.Warn'='HaulersDream';'Verse.AI.Pawn_JobTracker.StartJob'='Assembly-CSharp';'RimWorld.JobGiver_Work.TryIssueJobPackage'='Assembly-CSharp';'Verse.JobMaker.MakeJob'='Assembly-CSharp';'Verse.AI.ReservationManager.CanReserve'='Assembly-CSharp'}
    $taskBinds=@((Get-InFlightCalls $taskR)|Where-Object{$_.kind-ceq'observer-bind'});Assert-Recurrence ($taskBinds.Count-eq10-and@($taskBinds.method|Select-Object -Unique).Count-eq10) 'B1 observer target catalog mismatch.' $Problems
    foreach($taskBind in $taskBinds){Assert-Recurrence ($taskMethods.ContainsKey($taskBind.method)) 'Unknown B1 observer method.' $Problems;if(-not$taskMethods.ContainsKey($taskBind.method)){continue};$taskA=$taskAssemblies[$taskMethods[$taskBind.method]]
        Assert-Recurrence ($taskBind.detail-match'^(.*);mvid=([a-fA-F0-9-]+);token=([0-9]+)$') 'Malformed observer module/token binding.' $Problems
        if($taskBind.detail-match'^(.*);mvid=([a-fA-F0-9-]+);token=([0-9]+)$'){$taskToken=[long]$Matches[3];Assert-Recurrence ($Matches[1]-ceq($taskA.name+', Version='+$taskA.assemblyVersion+', Culture=neutral, PublicKeyToken=null')-and$Matches[2]-eq$taskA.moduleVersionId-and($taskToken-shr24)-eq6) 'Observer binding differs from loaded assembly.' $Problems;Test-InFlightExactMember $Value.assemblies $taskBind.method $taskToken method $Problems}}
    Assert-Recurrence ($Value.mods-is[array]-and$Value.mods.Count-eq4-and(($Value.mods.packageId|ForEach-Object{$_.ToLowerInvariant()})-join',')-ceq'brrainz.harmony,ludeon.rimworld,giwaffed.haulersdream,giwaffed.haulersdream.runtimeharness') 'Unexpected active B1 mod set/order.' $Problems
}
