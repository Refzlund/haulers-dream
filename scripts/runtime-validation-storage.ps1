# Dot-sourced by runtime-test.ps1. These checks consume evidence; they never alter a run.
function Test-StorageStateShape($Value, [string]$Label, $Problems) {
    $taskOk = Test-EvidenceFields $Value @{
        tick='integer'; sequence='integer'; jobId='integer'; source='integer'; high='integer'; elsewhere='integer';
        inventory='integer'; hands='integer'; total='integer'; jobDef='nullable-string'; position='string'; things='array'; claims='array'
    } $Label $Problems
    if (-not $taskOk) { return }
    foreach ($taskThing in $Value.things) { Test-StorageThingShape $taskThing ($Label + '.things[]') $Problems }
    foreach ($taskClaim in $Value.claims) {
        Test-EvidenceFields $taskClaim @{group='string'; recordedUnits='integer'; high='boolean'} ($Label + '.claims[]') $Problems | Out-Null
    }
}

function Test-StorageThingShape($Value, [string]$Label, $Problems) {
    Test-EvidenceFields $Value @{id='integer'; count='integer'; destroyed='boolean'; spawned='boolean'; inventory='boolean';
        hands='boolean'; cell='nullable-string'; holder='nullable-string'} $Label $Problems | Out-Null
}

function Test-StorageExtraShape($Value, [string]$Case, $Problems) {
    if ($Case -eq 'CAP01' -and (Test-EvidenceFields $Value @{storageSlots='object'} 'result' $Problems)) {
        $taskSlots = $Value.storageSlots
        $taskOk = Test-EvidenceFields $taskSlots @{
            caseId='string'; expectedBehavior='string'; adapterContract='string'; scope='string'; startedTick='integer'; finishedTick='integer';
            productionAssembly='string'; productionModuleId='string'; coreAssembly='string'; coreModuleId='string';
            fixtureValid='boolean'; requestedBehaviorSatisfied='boolean'; expectationMatched='boolean'; publishedOccupiedSplitObserved='boolean';
            status='string'; error='nullable-string'; assertions='array'; observations='array'; measurements='array'; shelves='array'
        } 'result.storageSlots' $Problems
        if (-not $taskOk) { return }
        foreach ($taskA in $taskSlots.assertions) {
            Test-EvidenceFields $taskA @{kind='string'; id='string'; passed='boolean'; detail='string'} 'storageSlots.assertions[]' $Problems | Out-Null
        }
        foreach ($taskO in $taskSlots.observations) {
            Test-EvidenceFields $taskO @{id='string'; actual='integer'; baseline='integer'; corrected='integer'; baselineMatched='boolean';
                correctedMatched='boolean'; detail='string'} 'storageSlots.observations[]' $Problems | Out-Null
        }
        foreach ($taskM in $taskSlots.measurements) {
            Test-EvidenceFields $taskM @{scene='string'; subject='string'; def='string'; scalarCellSpace='integer'; emptyCells='integer';
                partialSpace='integer'; perCellCapacity='integer'; unbounded='boolean'; truncated='boolean'; observedFloor='integer'} 'storageSlots.measurements[]' $Problems | Out-Null
        }
        foreach ($taskS in $taskSlots.shelves) {
            if (Test-EvidenceFields $taskS @{scene='string'; shelf='string'; cell='string'; maxSlots='integer'; itemCount='integer'; vacantSlots='integer'; items='array'} 'storageSlots.shelves[]' $Problems) {
                foreach ($taskI in $taskS.items) {
                    Test-EvidenceFields $taskI @{id='string'; def='string'; count='integer'; stackLimit='integer'} 'storageSlots.shelves[].items[]' $Problems | Out-Null
                }
            }
        }
    }
    if ($Case -eq 'L04-O1-DELIVERY' -and (Test-EvidenceFields $Value @{storageDelivery='object'} 'result' $Problems)) {
        $taskD = $Value.storageDelivery
        $taskFields = @{caseId='string'; expectedBehavior='string'; status='string'; originalSourceIds='array'; finalState='object';
            highCell='string'; sourceCells='array'; stableSinceTick='integer';
            jobs='array'; transfers='array'; queries='array'; gates='array'}
        foreach ($taskName in @('fixtureValid','requestedBehaviorSatisfied','expectationMatched','baselineGapObserved','timedOut','ordinaryChain',
            'exactPickup','ownClaimInventoryWitness','physicalUnload','successfulCleanups','conserved','observerHealthy','layoutIntact','followupWindowComplete',
            'highStorageRejected','highStorageAccepted','laterNativeHandHaulConverged','settledHigh','noRehaul')) { $taskFields[$taskName]='boolean' }
        foreach ($taskName in @('startedTick','finishedTick','firstBulkJobId','firstUnloadJobId','firstUnloadEndTick','firstBulkPicked','firstUnloadHigh',
            'firstUnloadSource','firstUnloadElsewhere','sourceZoneNetZeroBulkCycles','laterSourceReacquired','laterNativeHandDelivered','stableDistinctTicks',
            'minTotal','maxTotal','settledBoundaries')) { $taskFields[$taskName]='integer' }
        if (-not (Test-EvidenceFields $taskD $taskFields 'result.storageDelivery' $Problems)) { return }
        Test-StorageStateShape $taskD.finalState 'storageDelivery.finalState' $Problems
        foreach ($taskJ in $taskD.jobs) {
            Test-EvidenceFields $taskJ @{id='integer'; observedTick='integer'; endTick='integer'; def='string'; driver='string'; workgiver='nullable-string';
                workgiverClass='nullable-string'; queue='string'; endCondition='nullable-string'; forced='boolean'; candidateObserved='boolean'; released='boolean'} 'storageDelivery.jobs[]' $Problems | Out-Null
        }
        foreach ($taskT in $taskD.transfers) {
            if (Test-EvidenceFields $taskT @{kind='string'; jobId='integer'; tick='integer'; sequence='integer'; units='integer'; returnedSuccess='boolean';
                original='nullable-object'; resulting='nullable-object'; before='nullable-object'; after='object'} 'storageDelivery.transfers[]' $Problems) {
                if ($null -ne $taskT.original) { Test-StorageThingShape $taskT.original 'transfer.original' $Problems }
                if ($null -ne $taskT.resulting) { Test-StorageThingShape $taskT.resulting 'transfer.resulting' $Problems }
                if ($null -ne $taskT.before) { Test-StorageStateShape $taskT.before 'transfer.before' $Problems }
                Test-StorageStateShape $taskT.after 'transfer.after' $Problems
                if ($taskT.kind -notin @('bulk-pickup','carry-drop','start-carry') -or
                    ($taskT.kind -in @('carry-drop','start-carry') -and $null -eq $taskT.before) -or
                    ($taskT.kind -in @('bulk-pickup','carry-drop') -and $null -eq $taskT.original) -or
                    ($taskT.kind -eq 'start-carry' -and ($null -ne $taskT.original -or $null -ne $taskT.resulting))) {
                    $Problems.Add('Storage delivery transfer has an unknown kind or lacks its required pre-drop state.')
                }
            }
        }
        foreach ($taskQ in $taskD.queries) {
            if (Test-EvidenceFields $taskQ @{tick='integer'; sequence='integer'; jobId='integer'; free='integer'; delivering='integer'; productionLiveUnits='integer';
                taggedInventory='integer'; jobDef='nullable-string'; group='string'; highGroup='boolean'; truncated='boolean'; subject='object'; before='object'} 'storageDelivery.queries[]' $Problems) {
                Test-StorageThingShape $taskQ.subject 'query.subject' $Problems
                Test-StorageStateShape $taskQ.before 'query.before' $Problems
            }
        }
        foreach ($taskG in $taskD.gates) {
            if (Test-EvidenceFields $taskG @{tick='integer'; sequence='integer'; jobId='integer'; jobDef='nullable-string'; allowed='boolean';
                subject='object'; before='object'} 'storageDelivery.gates[]' $Problems) {
                Test-StorageThingShape $taskG.subject 'gate.subject' $Problems
                Test-StorageStateShape $taskG.before 'gate.before' $Problems
            }
        }
    }
}

function Get-StorageSlotsExpectations {
    # CAP01 deliberately uses the identified unmodified vanilla resource definitions.
    # Future stack-limit/provider variants require a separately reviewed scenario contract.
    $taskExpected = [ordered]@{}
    foreach ($taskOrder in @(@('silver-then-cloth',500,75), @('cloth-then-silver',75,500))) {
        $taskPrefix='plan-' + $taskOrder[0]
        $taskExpected[$taskPrefix + '-first-allowance']=@($taskOrder[1],$taskOrder[1])
        $taskExpected[$taskPrefix + '-spent-first-tail']=@(0,0)
        $taskExpected[$taskPrefix + '-second-after-spend']=@($taskOrder[2],0)
    }
    foreach ($taskOrder in @(@('silver-claim-then-cloth',75), @('cloth-claim-then-silver',500))) {
        $taskPrefix='claim-' + $taskOrder[0]
        $taskExpected[$taskPrefix + '-before-claim']=@($taskOrder[1],$taskOrder[1])
        $taskExpected[$taskPrefix + '-after-foreign-claim']=@($taskOrder[1],0)
        $taskExpected[$taskPrefix + '-patched-store-gate']=@(1,0)
    }
    foreach ($taskRow in @(@('plan-before',7),@('plan-after',0),@('other-def-plan',0),@('free-before-claim',7),
        @('silver-gate',1),@('cloth-gate',0),@('free-after-claim',0),@('gate-after-claim',0))) {
        $taskExpected['genuine-silver-top-up-' + $taskRow[0]]=@($taskRow[1],$taskRow[1])
    }
    foreach ($taskDef in @('Silver','Cloth')) {
        foreach ($taskKind in @('plan','free','gate')) { $taskExpected['full-incompatible-' + $taskKind + '-' + $taskDef]=@(0,0) }
    }
    $taskExpected['empty-three-slot-mixed-plan-allowance-0-Silver']=@(1500,1500)
    $taskExpected['empty-three-slot-mixed-plan-allowance-1-Cloth']=@(0,150)
    $taskExpected['empty-three-slot-mixed-plan-allowance-2-WoodLog']=@(0,75)
    $taskExpected['empty-three-slot-mixed-plan-allowance-3-Steel']=@(0,0)
    return $taskExpected
}

function Test-StorageSlotsEvidence($Value, [string]$Expected, $Problems) {
    $taskS=$Value.storageSlots
    $taskCorrected=$Expected -eq 'satisfied'
    if ($taskS.caseId -ne 'CAP01' -or $taskS.expectedBehavior -ne $Expected -or -not $taskS.fixtureValid -or -not $taskS.expectationMatched -or
        $taskS.requestedBehaviorSatisfied -ne $taskCorrected -or $taskS.status -ne $Value.status -or $taskS.error -or
        $taskS.startedTick -ne $taskS.finishedTick -or $taskS.startedTick -lt 0 -or $taskS.adapterContract -ne 'published-scalar-budget-v1') {
        $Problems.Add('CAP01 identity, adapter contract, fixture validity, same-tick scope or outcome is inconsistent.')
    }
    foreach ($taskIdentity in @(@('HaulersDream','productionAssembly','productionModuleId'),@('HaulersDream.Core','coreAssembly','coreModuleId'))) {
        $taskA=@($Value.assemblies | Where-Object name -eq $taskIdentity[0])
        if ($taskA.Count -ne 1 -or $taskS.($taskIdentity[2]) -ne $taskA[0].moduleVersionId -or
            -not $taskS.($taskIdentity[1]).StartsWith($taskIdentity[0] + ', Version=')) { $Problems.Add('CAP01 adapter assembly identity mismatch.') }
    }
    $taskExpectations=Get-StorageSlotsExpectations
    if ($taskS.observations.Count -ne $taskExpectations.Count) { $Problems.Add('CAP01 must retain exactly 30 distinct quantity/control observations.') }
    foreach ($taskId in $taskExpectations.Keys) {
        $taskMatches=@($taskS.observations | Where-Object id -eq $taskId)
        if ($taskMatches.Count -ne 1) { $Problems.Add('Missing unique CAP01 observation: ' + $taskId); continue }
        $taskO=$taskMatches[0]; $taskPair=$taskExpectations[$taskId]
        $taskWanted=if($taskCorrected){$taskPair[1]}else{$taskPair[0]}
        if ($taskO.baseline -ne $taskPair[0] -or $taskO.corrected -ne $taskPair[1] -or $taskO.actual -ne $taskWanted -or
            $taskO.baselineMatched -ne ($taskO.actual -eq $taskPair[0]) -or $taskO.correctedMatched -ne ($taskO.actual -eq $taskPair[1])) {
            $Problems.Add('CAP01 quantity/expected-answer mismatch: ' + $taskId)
        }
        $taskAssertion=@($taskS.assertions | Where-Object { $_.kind -eq 'behavior' -and $_.id -eq $taskId })
        if ($taskAssertion.Count -ne 1 -or $taskAssertion[0].passed -ne $taskO.correctedMatched -or
            -not (Test-RequiredAssertion $Value.assertions ('storage-slots-behavior-' + $taskId) $taskO.correctedMatched)) {
            $Problems.Add('CAP01 behavior assertion does not match its unique observation: ' + $taskId)
        }
    }
    if (@($taskS.assertions | Where-Object { $_.kind -notin @('fixture','behavior') -or ($_.kind -eq 'fixture' -and -not $_.passed) }).Count) {
        $Problems.Add('CAP01 has an unknown assertion kind or failed fixture precondition.')
    }
    foreach ($taskRequired in @('expectation','map','budget-methods','settings','gates-active','scene-bounds','no-existing-zones','same-tick','only-fixture-actors',
        'production-bindings-same-assembly','patch-storage-gate','patch-storage-counter','patch-storage-reservation',
        'plan-silver-then-cloth-spent-budget-survives-reprice','plan-cloth-then-silver-spent-budget-survives-reprice','genuine-silver-top-up-spent-budget-survives-reprice')) {
        if (-not (Test-RequiredAssertion $Value.assertions ('storage-slots-fixture-' + $taskRequired) $true)) { $Problems.Add('Missing CAP01 fixture assertion: ' + $taskRequired) }
    }
    $taskScenes=@('plan-silver-then-cloth','plan-cloth-then-silver','claim-silver-claim-then-cloth','claim-cloth-claim-then-silver',
        'genuine-silver-top-up','full-incompatible','empty-three-slot-mixed-plan')
    if ($taskS.shelves.Count -ne 7 -or @($taskS.shelves.cell | Select-Object -Unique).Count -ne 7 -or
        @($taskS.shelves.shelf | Select-Object -Unique).Count -ne 7) { $Problems.Add('CAP01 requires seven distinct physical shelves/cells.') }
    foreach ($taskId in $taskScenes) {
        $taskRows=@($taskS.shelves | Where-Object scene -eq $taskId)
        if ($taskRows.Count -ne 1) { $Problems.Add('Missing unique CAP01 shelf scene: ' + $taskId); continue }
        $taskShelf=$taskRows[0]
        $taskN=if($taskId -eq 'empty-three-slot-mixed-plan'){0}elseif($taskId -in @('genuine-silver-top-up','full-incompatible')){3}else{2}
        if ($taskShelf.maxSlots -ne 3 -or $taskShelf.itemCount -ne $taskN -or $taskShelf.items.Count -ne $taskN -or $taskShelf.vacantSlots -ne (3-$taskN)) {
            $Problems.Add('CAP01 physical occupancy differs from contract: ' + $taskId)
        }
        if ($taskN -gt 0) {
            $taskExpectedItems=@{Steel=75; WoodLog=75}
            if($taskId -eq 'genuine-silver-top-up'){$taskExpectedItems.Silver=493}
            if($taskId -eq 'full-incompatible'){$taskExpectedItems.Uranium=75}
            foreach ($taskDef in $taskExpectedItems.Keys) {
                $taskItems=@($taskShelf.items | Where-Object def -eq $taskDef)
                $taskLimit=if($taskDef -eq 'Silver'){500}else{75}
                if ($taskItems.Count -ne 1 -or $taskItems[0].count -ne $taskExpectedItems[$taskDef] -or $taskItems[0].stackLimit -ne $taskLimit) {
                    $Problems.Add('CAP01 actual filler/partial stack differs from contract: ' + $taskId + '/' + $taskDef)
                }
            }
        }
        foreach ($taskSuffix in @('vanilla-single-cell','three-slots','occupancy','physical-unchanged')) {
            if (-not (Test-RequiredAssertion $Value.assertions ('storage-slots-fixture-' + $taskId + '-' + $taskSuffix) $true)) {
                $Problems.Add('Missing CAP01 physical fixture witness: ' + $taskId + '/' + $taskSuffix)
            }
        }
    }
    if ($taskS.measurements.Count -ne 15 -or @($taskS.measurements | Where-Object { $_.unbounded -or $_.truncated }).Count -gt 0) {
        $Problems.Add('CAP01 needs all 15 bounded, complete production measurements.')
    }
    if (-not $taskCorrected) {
        $taskSplit=@($taskS.measurements | Where-Object { $_.scene -in $taskScenes[0..3] -and $_.emptyCells -eq 0 -and
            $_.partialSpace -eq $(if($_.def -eq 'Silver'){500}else{75}) })
        if (-not $taskS.publishedOccupiedSplitObserved -or $taskSplit.Count -ne 8) { $Problems.Add('CAP01 baseline lacks eight actual occupied-cell/private-space measurements.') }
    }
    Test-StorageSlotsCompleteness $Value $Expected $Problems
}

function Test-StoragePhysicalTotal($State, [string]$Label, $Problems, $Layout) {
    $taskSums=@{source=[long]0;high=[long]0;elsewhere=[long]0;inventory=[long]0;hands=[long]0}
    $taskIds=@{}; $taskFloorCells=@{}
    foreach($taskThing in $State.things) {
        if($taskThing.id -le 0 -or $taskIds.ContainsKey($taskThing.id) -or $taskThing.count -le 0 -or $taskThing.count -gt 75 -or $taskThing.destroyed) {
            $Problems.Add('Invalid or duplicate physical Thing at ' + $Label)
        }
        $taskIds[$taskThing.id]=$true
        $taskCustody=([int]$taskThing.spawned)+([int]$taskThing.inventory)+([int]$taskThing.hands)
        if($taskCustody -ne 1) { $Problems.Add('Physical Thing must have exactly one custody at ' + $Label); continue }
        if($taskThing.spawned) {
            if([string]::IsNullOrWhiteSpace($taskThing.cell) -or $taskThing.holder -ne 'Verse.Map') { $Problems.Add('Invalid floor holder/cell at ' + $Label) }
            $taskCategory=if($taskThing.cell -eq $Layout.highCell){'high'}elseif($taskThing.cell -in $Layout.sourceCells){'source'}else{'elsewhere'}
            if(-not [string]::IsNullOrWhiteSpace($taskThing.cell)) {
                if($taskFloorCells.ContainsKey($taskThing.cell)) { $Problems.Add('Multiple physical stacks occupy a one-slot fixture cell at '+$Label) }
                $taskFloorCells[$taskThing.cell]=$true
            }
        } else {
            $taskCategory=if($taskThing.inventory){'inventory'}else{'hands'}
            $taskHolder=if($taskThing.inventory){'Verse.Pawn_InventoryTracker'}else{'Verse.Pawn_CarryTracker'}
            if($null -ne $taskThing.cell -or $taskThing.holder -ne $taskHolder) { $Problems.Add('Invalid held Thing holder/cell at ' + $Label) }
        }
        $taskSums[$taskCategory]+=$taskThing.count
    }
    $taskTotal=[long]0
    foreach($taskCategory in $taskSums.Keys) {
        $taskTotal+=$taskSums[$taskCategory]
        if($State.$taskCategory -ne $taskSums[$taskCategory]) { $Problems.Add('Physical ' + $taskCategory + ' subtotal differs from Things at ' + $Label) }
    }
    if($State.total -ne 75 -or $taskTotal -ne 75 -or $taskSums.elsewhere -ne 0 -or $State.things.Count -eq 0) {
        $Problems.Add('Storage delivery physical counts/identities are inconsistent at ' + $Label)
    }
}

function Test-StorageDeliveryEvidence($Value, [string]$Expected, $Problems) {
    $taskD=$Value.storageDelivery; $taskCorrected=$Expected -eq 'satisfied'
    if ($taskD.caseId -ne 'L04-O1-DELIVERY' -or $taskD.expectedBehavior -ne $Expected -or -not $taskD.fixtureValid -or
        -not $taskD.expectationMatched -or $taskD.requestedBehaviorSatisfied -ne $taskCorrected -or $taskD.status -ne $Value.status -or
        $taskD.baselineGapObserved -eq $taskCorrected -or $taskD.timedOut -or $taskD.startedTick -lt 0 -or
        $taskD.finishedTick - $taskD.firstUnloadEndTick -lt 1200 -or $taskD.firstUnloadEndTick -lt $taskD.startedTick -or
        $taskD.finishedTick - $taskD.startedTick -ge 6000) { $Problems.Add('Storage delivery identity/outcome or completed follow-up window is inconsistent.') }
    foreach ($taskFlag in @('ordinaryChain','exactPickup','ownClaimInventoryWitness','physicalUnload','successfulCleanups','conserved',
        'observerHealthy','layoutIntact','followupWindowComplete')) {
        if (-not $taskD.$taskFlag) { $Problems.Add('Required storage delivery execution witness is false: ' + $taskFlag) }
    }
    foreach ($taskRequired in @('ordinary-chain','exact-real-pickups','real-own-claim-inventory','physical-unload','successful-driver-cleanups',
        'conservation','layout-intact','observer-healthy','followup-window','expected-capacity-branch')) {
        if (-not (Test-RequiredAssertion $Value.assertions ('execution-storage-delivery-' + $taskRequired) $true)) {
            $Problems.Add('Missing unique storage delivery execution assertion: ' + $taskRequired)
        }
    }
    foreach ($taskRequired in @('first-unload-high','settled-without-rehaul')) {
        if (-not (Test-RequiredAssertion $Value.assertions ('behavior-storage-delivery-' + $taskRequired) $taskCorrected)) {
            $Problems.Add('Storage delivery behavior assertion differs from configured expectation: ' + $taskRequired)
        }
    }
    foreach ($taskRequired in @('setup','expectation','home-map','steel-limit','settings','no-steel-keep-rule','seam-active','claim-field',
        'patch-bulk-route','patch-storage-gate','patch-storage-counter','patch-storage-reservation','bounds','no-overwritten-zones','roof-support',
        'exact-stockpiles','no-other-higher-storage','capable-human','haul-comp','tag-observation','native-work-only','initial-no-claims','initial-counts','observers',
        'setting-masterEnabled','setting-haulToStack','setting-bulkHaul','setting-markForUnload','setting-pickupDelayOnHauling',
        'setting-carryLimitFraction','setting-carryMassCapKg','setting-autoHaulYields')) {
        if (-not (Test-RequiredAssertion $Value.assertions ('fixture-storage-delivery-' + $taskRequired) $true)) {
            $Problems.Add('Missing unique storage delivery fixture witness: ' + $taskRequired)
        }
    }
    if ($taskD.firstBulkPicked -ne 10 -or $taskD.minTotal -ne 75 -or $taskD.maxTotal -ne 75 -or $taskD.settledBoundaries -lt 1200 -or
        $taskD.originalSourceIds.Count -ne 2 -or @($taskD.originalSourceIds | Select-Object -Unique).Count -ne 2 -or
        @($taskD.originalSourceIds | Where-Object { ($_ -isnot [int] -and $_ -isnot [long]) -or $_ -le 0 }).Count -gt 0) {
        $Problems.Add('Storage delivery pickup, physical bounds or original source identities are invalid.')
    }
    $taskBulk=@($taskD.jobs | Where-Object id -eq $taskD.firstBulkJobId)
    $taskUnload=@($taskD.jobs | Where-Object id -eq $taskD.firstUnloadJobId)
    if ($taskBulk.Count -ne 1 -or $taskUnload.Count -ne 1 -or $taskD.firstBulkJobId -eq $taskD.firstUnloadJobId) {
        $Problems.Add('Storage delivery lacks distinct unique first bulk/unload execution records.')
    } else {
        $taskB=$taskBulk[0]; $taskU=$taskUnload[0]
        if ($taskB.def -ne 'HaulersDream_BulkHaul' -or $taskB.driver -ne 'HaulersDream.JobDriver_BulkHaul' -or
            $taskB.workgiverClass -ne 'RimWorld.WorkGiver_HaulGeneral' -or -not $taskB.candidateObserved -or
            $taskU.def -ne 'HaulersDream_UnloadInventory' -or $taskU.driver -ne 'HaulersDream.JobDriver_UnloadHauledInventory' -or
            $taskB.forced -or $taskU.forced -or $taskB.endCondition -ne 'Succeeded' -or $taskU.endCondition -ne 'Succeeded' -or
            -not $taskB.released -or -not $taskU.released -or $taskU.endTick -ne $taskD.firstUnloadEndTick -or
            $taskB.observedTick -lt $taskD.startedTick -or $taskB.endTick -lt $taskB.observedTick -or
            $taskU.observedTick -lt $taskB.observedTick -or $taskU.endTick -lt $taskU.observedTick) {
            $Problems.Add('Storage delivery native workgiver/driver/start/cleanup provenance is inconsistent.')
        }
        $taskQueue=@($taskB.queue.Split(',') | Sort-Object)
        $taskWantedQueue=@($taskD.originalSourceIds | ForEach-Object { [string]$_ + 'x5' } | Sort-Object)
        if (($taskQueue -join ',') -ne ($taskWantedQueue -join ',')) { $Problems.Add('First actual bulk job did not retain both original five-unit source rows.') }
    }
    if (@($taskD.jobs | ForEach-Object id | Select-Object -Unique).Count -ne $taskD.jobs.Count) { $Problems.Add('Storage delivery contains duplicate executed job IDs.') }
    $taskPickups=@($taskD.transfers | Where-Object { $_.kind -eq 'bulk-pickup' -and $_.jobId -eq $taskD.firstBulkJobId })
    if ($taskPickups.Count -ne 2 -or ($taskPickups.units | Measure-Object -Sum).Sum -ne 10) { $Problems.Add('Storage delivery lacks exactly two observed five-unit pickups.') }
    foreach ($taskId in $taskD.originalSourceIds) {
        $taskRows=@($taskPickups | Where-Object { $_.original.id -eq $taskId })
        if ($taskRows.Count -ne 1 -or $taskRows[0].units -ne 5 -or -not $taskRows[0].returnedSuccess) {
            $Problems.Add('Original source lacks its unique actual five-unit pickup: ' + $taskId)
        }
    }
    foreach ($taskT in $taskD.transfers) {
        Test-StoragePhysicalTotal $taskT.after ('transfer-' + $taskT.sequence) $Problems $taskD
        if ($null -ne $taskT.before) { Test-StoragePhysicalTotal $taskT.before ('before-transfer-' + $taskT.sequence) $Problems $taskD }
    }
    $taskDrops=@($taskD.transfers | Where-Object { $_.kind -eq 'carry-drop' -and $_.jobId -eq $taskD.firstUnloadJobId })
    $taskHighDelta=0; $taskSourceDelta=0; $taskElsewhereDelta=0
    foreach($taskT in $taskDrops) {
        $taskHighDelta += $taskT.after.high - $taskT.before.high
        $taskSourceDelta += $taskT.after.source - $taskT.before.source
        $taskElsewhereDelta += $taskT.after.elsewhere - $taskT.before.elsewhere
    }
    $taskRealDrop=@($taskDrops | Where-Object { $_.returnedSuccess -and $_.units -eq 10 -and $null -ne $_.resulting -and $_.resulting.spawned -and
        ($_.before.inventory + $_.before.hands) -eq 10 -and ($_.after.inventory + $_.after.hands) -eq 0 })
    $taskWantedHigh=if($taskCorrected){10}else{0}; $taskWantedSource=10-$taskWantedHigh
    if ($taskRealDrop.Count -ne 1 -or $taskHighDelta -ne $taskWantedHigh -or $taskSourceDelta -ne $taskWantedSource -or $taskElsewhereDelta -ne 0 -or
        $taskD.firstUnloadHigh -ne $taskHighDelta -or $taskD.firstUnloadSource -ne $taskSourceDelta -or $taskD.firstUnloadElsewhere -ne 0) {
        $Problems.Add('Actual first-unload physical deposit differs from the configured baseline/correction.')
    }
    $taskOwned=@($taskD.queries | Where-Object { $_.jobId -eq $taskD.firstUnloadJobId -and $_.highGroup -and $_.subject.inventory -and -not $_.subject.spawned -and
        $_.before.inventory -eq 10 -and $_.before.hands -eq 0 -and $_.before.source -eq 0 -and $_.before.high -eq 65 -and $_.taggedInventory -eq 10 -and
        @($_.before.claims | Where-Object { $_.high -and $_.recordedUnits -eq 10 }).Count -gt 0 -and -not $_.truncated })
    $taskExpectedQuery=@($taskOwned | Where-Object { if($taskCorrected){$_.delivering -eq 1 -and $_.free -eq 10}else{$_.delivering -eq 0 -and $_.free -eq 0 -and $_.productionLiveUnits -eq 10} })
    $taskExpectedGate=@($taskD.gates | Where-Object { $_.jobId -eq $taskD.firstUnloadJobId -and $_.subject.inventory -and
        $_.before.high -eq 65 -and $_.before.inventory -eq 10 -and $_.allowed -eq $taskCorrected })
    if ($taskExpectedQuery.Count -eq 0 -or $taskExpectedGate.Count -eq 0 -or
        ($taskCorrected -and -not $taskD.highStorageAccepted) -or (-not $taskCorrected -and -not $taskD.highStorageRejected)) {
        $Problems.Add('Storage delivery lacks the actual own-inventory/live-claim capacity branch and matching cell gate.')
    }
    Test-StoragePhysicalTotal $taskD.finalState 'final-state' $Problems $taskD
    if ($taskD.finalState.tick -ne $taskD.finishedTick) { $Problems.Add('Storage delivery final snapshot is not from its finished tick.') }
    if ($taskCorrected) {
        if (-not $taskD.settledHigh -or -not $taskD.noRehaul -or $taskD.stableDistinctTicks -lt 600 -or $taskD.laterSourceReacquired -ne 0 -or
            $taskD.laterNativeHandDelivered -ne 0 -or $taskD.sourceZoneNetZeroBulkCycles -ne 0 -or $taskD.finalState.high -ne 75 -or
            $taskD.finalState.source -ne 0 -or $taskD.finalState.inventory -ne 0 -or $taskD.finalState.hands -ne 0 -or
            @($taskD.jobs | Where-Object def -eq 'HaulersDream_BulkHaul').Count -ne 1 -or
            @($taskD.jobs | Where-Object def -eq 'HaulersDream_UnloadInventory').Count -ne 1 -or
            @($taskD.jobs | Where-Object def -eq 'HaulToCell').Count -gt 0) { $Problems.Add('Corrected storage delivery did not remain settled without rehauling.') }
    } elseif ($taskD.sourceZoneNetZeroBulkCycles -ne 1) { $Problems.Add('Storage baseline lacks the one actual source-zone net-zero first bulk cycle.') }
    Test-StorageDeliveryRecords $taskD $Expected $Problems
}

function Add-StorageBag($Bag, [string]$Key, [int]$Count=1) {
    if($Bag.ContainsKey($Key)) { $Bag[$Key]+=$Count } else { $Bag[$Key]=$Count }
}

function ConvertTo-StorageCanonicalValue($Value) {
    if($null -eq $Value) { return $null }
    # Member-enumeration pipelines can wrap a string in PSObject (and make -is
    # PSCustomObject true). Preserve primitives before inspecting properties.
    if($Value -is [string]) { return [string]$Value }
    if($Value -is [ValueType]) { return $Value.PSObject.BaseObject }
    if($Value -is [pscustomobject]) {
        $taskObject=[ordered]@{}
        foreach($taskProperty in @($Value.PSObject.Properties | Sort-Object Name)) {
            $taskObject[$taskProperty.Name]=ConvertTo-StorageCanonicalValue $taskProperty.Value
        }
        return $taskObject
    }
    if($Value -is [array]) {
        $taskArray=@(foreach($taskElement in $Value) { ,(ConvertTo-StorageCanonicalValue $taskElement) })
        return ,$taskArray
    }
    return $Value
}

function Get-StorageCanonicalJson($Value) {
    ConvertTo-Json -InputObject (ConvertTo-StorageCanonicalValue $Value) -Depth 40 -Compress
}

function Test-StorageBags($Expected, $Actual, [string]$Label, $Problems) {
    $taskWanted=@{}; $taskActual=@{}
    foreach($taskValue in $Expected) { Add-StorageBag $taskWanted (Get-StorageCanonicalJson $taskValue) }
    foreach($taskValue in $Actual) { Add-StorageBag $taskActual (Get-StorageCanonicalJson $taskValue) }
    if($taskWanted.Count -ne $taskActual.Count) { $Problems.Add('Evidence bag differs: ' + $Label); return }
    foreach($taskKey in $taskWanted.Keys) {
        if(-not $taskActual.ContainsKey($taskKey) -or $taskActual[$taskKey] -ne $taskWanted[$taskKey]) {
            $Problems.Add('Evidence bag differs: ' + $Label); return
        }
    }
}

function Get-StorageMeasurementCatalog {
    [ordered]@{
        'plan-silver-then-cloth'=@('Silver','Cloth')
        'plan-cloth-then-silver'=@('Cloth','Silver')
        'claim-silver-claim-then-cloth'=@('Silver','Cloth')
        'claim-cloth-claim-then-silver'=@('Cloth','Silver')
        'genuine-silver-top-up'=@('Silver')
        'full-incompatible'=@('Silver','Cloth')
        'empty-three-slot-mixed-plan'=@('Silver','Cloth','WoodLog','Steel')
    }
}

function Test-StorageSlotsCompleteness($Value, [string]$Expected, $Problems) {
    $taskS=$Value.storageSlots; $taskCatalog=Get-StorageMeasurementCatalog
    $taskFixtures=@($taskS.assertions | Where-Object kind -eq 'fixture')
    if(@($taskS.assertions | Where-Object kind -eq 'behavior').Count -ne 30) { $Problems.Add('CAP01 must retain exactly thirty nested behavior assertions.') }
    $taskRequired=@{}; $taskSubjects=@{}; $taskIds=@{}
    # Every Require call in the published fixture has a source-derived multiplicity.
    # Repeated pricing/field/tag checks legitimately repeat IDs; compare bags, not a global uniqueness rule.
    foreach($taskId in @('expectation','map','budget-methods','production-bindings-same-assembly','patch-storage-gate','patch-storage-counter',
        'patch-storage-reservation','settings','setting-masterEnabled','setting-haulToStack','gates-active','scene-bounds','no-existing-zones','same-tick','only-fixture-actors')) {
        Add-StorageBag $taskRequired $taskId
    }
    foreach($taskName in @('StorageCommitments','BulkHaul','Core.StorageGroupBudget','HaulersDreamMod','Patch_IsGoodStoreCell_HonourCommitments',
        'Patch_HaulToCellStorageJob_ClampToCommitments','Patch_JobDriver_HaulToCell_NoCellReservation')) { Add-StorageBag $taskRequired ('type-HaulersDream.'+$taskName) }
    foreach($taskName in @('MeasureGroup','FreeUnitsFor','ResolveGroupBudget','PriceDefInto','TryCommit','Commit','UnitsMovingOf','ClaimedByOthersFor','GatesVanillaStorage')) {
        Add-StorageBag $taskRequired ('method-'+$taskName)
    }
    foreach($taskDef in @('Silver','Cloth','Steel','WoodLog')) { Add-StorageBag $taskRequired ('stack-limit-'+$taskDef) }
    foreach($taskIndex in 0..9) { Add-StorageBag $taskRequired ('actor-'+$taskIndex) }
    Add-StorageBag $taskRequired 'tag-method' 3
    foreach($taskField in @('EmptyCells','PartialSpace','PerCellCapacity','Unbounded','Truncated','ObservedFloor')) { Add-StorageBag $taskRequired ('field-GroupSpace-'+$taskField) 15 }
    $taskBudgetReads=if($Expected -eq 'satisfied'){21}else{19}
    foreach($taskField in @('partialByDef','perCellByDef','emptyCells')) { Add-StorageBag $taskRequired ('field-StorageGroupBudget-'+$taskField) $taskBudgetReads }
    foreach($taskScene in $taskCatalog.Keys) {
        foreach($taskSuffix in @('vanilla-single-cell','three-slots','occupancy','physical-unchanged')) { Add-StorageBag $taskRequired ($taskScene+'-'+$taskSuffix) }
        foreach($taskDef in $taskCatalog[$taskScene]) {
            $taskRows=@($taskS.measurements | Where-Object { $_.scene -eq $taskScene -and $_.def -eq $taskDef })
            if($taskRows.Count -ne 1) { $Problems.Add('Missing unique CAP01 measurement: '+$taskScene+'/'+$taskDef); continue }
            $taskM=$taskRows[0]; $taskKey=$taskScene+'/'+$taskDef
            $taskSubjects[$taskKey]=$taskM.subject
            if([string]::IsNullOrWhiteSpace($taskM.subject) -or $taskM.subject -notmatch ('^'+$taskDef+'[1-9][0-9]*$') -or $taskIds.ContainsKey($taskM.subject)) {
                $Problems.Add('Invalid/duplicate CAP01 measurement subject: '+$taskKey)
            }
            $taskIds[$taskM.subject]=$true
            $taskLimit=if($taskDef -eq 'Silver'){500}else{75}
            $taskScalar=if($taskScene -eq 'empty-three-slot-mixed-plan'){3*$taskLimit}elseif($taskScene -eq 'genuine-silver-top-up'){7}elseif($taskScene -eq 'full-incompatible'){0}else{$taskLimit}
            if($taskM.scalarCellSpace -ne $taskScalar -or $taskM.emptyCells -lt 0 -or $taskM.partialSpace -lt 0 -or $taskM.perCellCapacity -le 0 -or
                $taskM.observedFloor -ne 0 -or $taskM.truncated -or $taskM.unbounded) { $Problems.Add('CAP01 physical/scalar measurement differs from its scene: '+$taskKey) }
            if($Expected -eq 'baseline-gap') {
                $taskEmpty=if($taskScene -eq 'empty-three-slot-mixed-plan'){1}else{0}
                $taskPartial=if($taskEmpty){0}else{$taskScalar}
                $taskPerCell=if($taskEmpty){3*$taskLimit}else{$taskLimit}
                if($taskM.emptyCells -ne $taskEmpty -or $taskM.partialSpace -ne $taskPartial -or $taskM.perCellCapacity -ne $taskPerCell) {
                    $Problems.Add('CAP01 published decomposition differs from its unique physical scene: '+$taskKey)
                }
            }
            Add-StorageBag $taskRequired ($taskScene+'-complete-measure-'+$taskM.subject)
        }
    }
    # Floor records supply the sole unmeasured Cloth subject in the genuine top-up control.
    $taskExtraFloor=@($taskFixtures | Where-Object { $_.id -match '^genuine-silver-top-up-floor-Cloth[1-9][0-9]*$' })
    if($taskExtraFloor.Count -eq 1) { $taskSubjects['genuine-silver-top-up/Cloth']=$taskExtraFloor[0].id.Substring('genuine-silver-top-up-floor-'.Length) }
    else { $Problems.Add('CAP01 lacks the unique unmeasured top-up Cloth floor subject.') }
    foreach($taskScene in $taskCatalog.Keys) {
        $taskDefs=if($taskScene -eq 'genuine-silver-top-up'){@('Silver','Cloth')}else{$taskCatalog[$taskScene]}
        foreach($taskDef in $taskDefs) {
            $taskKey=$taskScene+'/'+$taskDef
            if(-not $taskSubjects.ContainsKey($taskKey)) { continue }
            $taskSubject=$taskSubjects[$taskKey]
            $taskHeld=$taskScene.StartsWith('claim-') -and $taskDef -eq $taskCatalog[$taskScene][0]
            if($taskHeld) { Add-StorageBag $taskRequired ('held-transfer-'+$taskSubject) }
            else { Add-StorageBag $taskRequired ($taskScene+'-floor-'+$taskSubject) }
            if(-not $taskScene.StartsWith('claim-')) {
                $taskPrices=if(($taskScene.StartsWith('plan-') -and $taskDef -eq $taskCatalog[$taskScene][0]) -or ($taskScene -eq 'genuine-silver-top-up' -and $taskDef -eq 'Silver')){2}else{1}
                foreach($taskPrefix in @('resolved-','idempotent-price-')) { Add-StorageBag $taskRequired ($taskScene+'-'+$taskPrefix+$taskSubject) $taskPrices }
            }
            $taskFree=if($taskScene.StartsWith('claim-') -and -not $taskHeld){2}elseif($taskScene -eq 'genuine-silver-top-up' -and $taskDef -eq 'Silver'){2}elseif($taskScene -eq 'full-incompatible'){1}else{0}
            if($taskFree) { Add-StorageBag $taskRequired ($taskScene+'-free-not-truncated-'+$taskSubject) $taskFree }
        }
        if($taskScene.StartsWith('plan-')) {
            Add-StorageBag $taskRequired ($taskScene+'-same-budget')
            Add-StorageBag $taskRequired ($taskScene+'-spent-budget-survives-reprice')
            Add-StorageBag $taskRequired ($taskScene+'-spend-authorized-'+$taskCatalog[$taskScene][0])
        }
        if($taskScene.StartsWith('claim-') -or $taskScene -eq 'genuine-silver-top-up') {
            Add-StorageBag $taskRequired ($taskScene+'-claim'); Add-StorageBag $taskRequired ($taskScene+'-live-evidence')
            if($taskScene.StartsWith('claim-')) { Add-StorageBag $taskRequired ($taskScene+'-foreign-claim-visible') }
        }
    }
    foreach($taskId in @('genuine-silver-top-up-observed-deficit','genuine-silver-top-up-spent-budget-survives-reprice',
        'genuine-silver-top-up-spend-authorized-Silver','empty-three-slot-mixed-plan-spend-authorized-Silver')) { Add-StorageBag $taskRequired $taskId }
    if($Expected -eq 'satisfied') { foreach($taskDef in @('Cloth','WoodLog')) { Add-StorageBag $taskRequired ('empty-three-slot-mixed-plan-spend-authorized-'+$taskDef) } }
    $taskHeldRows=@($taskFixtures | Where-Object { $_.id.StartsWith('held-transfer-') -and -not $taskRequired.ContainsKey($_.id) })
    if($taskHeldRows.Count -eq 1 -and $taskHeldRows[0].id -match '^held-transfer-Silver[1-9][0-9]*$') { Add-StorageBag $taskRequired $taskHeldRows[0].id }
    else { $Problems.Add('CAP01 lacks exactly one separate seven-unit inventory transfer.') }
    $taskTagRows=@($taskFixtures | Where-Object { $_.id -match '^tag-comp-Human[1-9][0-9]*$' })
    if($taskTagRows.Count -ne 3 -or @($taskTagRows | ForEach-Object id | Select-Object -Unique).Count -ne 3) { $Problems.Add('CAP01 lacks three distinct real cargo-tag components.') }
    foreach($taskTag in $taskTagRows) { Add-StorageBag $taskRequired $taskTag.id }
    $taskActual=@{}
    foreach($taskFixture in $taskFixtures) { Add-StorageBag $taskActual $taskFixture.id }
    foreach($taskId in $taskRequired.Keys) {
        if(-not $taskActual.ContainsKey($taskId) -or $taskActual[$taskId] -ne $taskRequired[$taskId]) { $Problems.Add('Missing/repeated CAP01 fixture assertion: '+$taskId) }
    }
    foreach($taskId in $taskActual.Keys) { if(-not $taskRequired.ContainsKey($taskId)) { $Problems.Add('Unexpected CAP01 fixture assertion: '+$taskId) } }
    $taskExport=@($taskS.assertions | ForEach-Object { [pscustomobject]@{id='storage-slots-'+$_.kind+'-'+$_.id;passed=$_.passed;observed=$_.detail} })
    Test-StorageBags $taskExport @($Value.assertions | Where-Object { $_.id.StartsWith('storage-slots-') }) 'CAP01 nested/top assertion export' $Problems
    $taskPhysicalIds=@{}
    foreach($taskShelf in $taskS.shelves) {
        foreach($taskItem in $taskShelf.items) {
            if($taskItem.id -notmatch ('^'+$taskItem.def+'[1-9][0-9]*$') -or $taskPhysicalIds.ContainsKey($taskItem.id) -or $taskIds.ContainsKey($taskItem.id)) {
                $Problems.Add('CAP01 reuses an invalid physical Thing identity across shelves/subjects.')
            }
            $taskPhysicalIds[$taskItem.id]=$true
        }
    }
}

function Test-StorageStateContext($State, $Delivery, [string]$Label, $Problems) {
    if($State.tick -lt $Delivery.startedTick -or $State.tick -gt $Delivery.finishedTick -or $State.sequence -le 0) {
        $Problems.Add('Storage state is outside its fixture clock: '+$Label)
    }
    if($State.jobId -eq -1) {
        if($null -ne $State.jobDef) { $Problems.Add('Jobless storage state names a job: '+$Label) }
        return
    }
    $taskJobs=@($Delivery.jobs | Where-Object id -eq $State.jobId)
    if($taskJobs.Count -ne 1) { $Problems.Add('Storage state lacks its unique observed current job: '+$Label); return }
    $taskJob=$taskJobs[0]
    if($State.jobDef -ne $taskJob.def -or $State.tick -lt $taskJob.observedTick -or ($taskJob.endTick -ge 0 -and $State.tick -gt $taskJob.endTick)) {
        $Problems.Add('Storage state contradicts the actual job lifetime: '+$Label)
    }
}

function Test-StorageThingMember($Thing, $State, [string]$Label, $Problems) {
    $taskMatches=@($State.things | Where-Object id -eq $Thing.id)
    if($taskMatches.Count -ne 1 -or (Get-StorageCanonicalJson $Thing) -ne (Get-StorageCanonicalJson $taskMatches[0])) {
        $Problems.Add('Storage subject/result does not match its actual physical Thing: '+$Label)
    }
}

function Test-StorageRecordContext($Record, $State, $Delivery, [string]$Label, $Problems) {
    Test-StorageStateContext $State $Delivery $Label $Problems
    if($Record.tick -ne $State.tick -or $Record.sequence -ne $State.sequence -or $Record.jobId -ne $State.jobId) {
        $Problems.Add('Storage record differs from its own snapshot clock/current job: '+$Label)
    }
}

function Test-StorageHighState($State) {
    return $State.high -eq 75 -and $State.source -eq 0 -and $State.inventory -eq 0 -and $State.hands -eq 0 -and $State.elsewhere -eq 0
}

function Test-StorageDeliveryRecords($D, [string]$Expected, $Problems) {
    $taskCorrected=$Expected -eq 'satisfied'
    if($D.highCell -notmatch '^\(-?[0-9]+, 0, -?[0-9]+\)$' -or $D.sourceCells.Count -ne 2 -or
        @($D.sourceCells | Where-Object { $_ -isnot [string] -or $_ -notmatch '^\(-?[0-9]+, 0, -?[0-9]+\)$' }).Count -gt 0 -or
        @($D.sourceCells | Select-Object -Unique).Count -ne 2 -or $D.highCell -in $D.sourceCells) {
        $Problems.Add('Storage delivery lacks its three distinct typed stockpile cells.')
    }
    foreach($taskJob in $D.jobs) {
        if($taskJob.id -lt 0 -or $taskJob.observedTick -lt $D.startedTick -or $taskJob.observedTick -gt $D.finishedTick -or
            $taskJob.endTick -lt -1 -or $taskJob.endTick -gt $D.finishedTick -or ($taskJob.endTick -ge 0 -and $taskJob.endTick -lt $taskJob.observedTick) -or
            ($taskJob.endTick -eq -1 -and ($taskJob.released -or $null -ne $taskJob.endCondition)) -or
            ($taskJob.endTick -ge 0 -and [string]::IsNullOrWhiteSpace($taskJob.endCondition))) { $Problems.Add('Invalid executed storage job lifetime: '+$taskJob.id) }
        if($taskJob.def -in @('HaulersDream_BulkHaul','HaulersDream_UnloadInventory','HaulToCell') -and $taskJob.forced) {
            $Problems.Add('Storage transport was player-forced instead of automatic: '+$taskJob.id)
        }
    }
    $taskBulk=@($D.jobs | Where-Object id -eq $D.firstBulkJobId); $taskUnload=@($D.jobs | Where-Object id -eq $D.firstUnloadJobId)
    if($taskBulk.Count -eq 1 -and $taskUnload.Count -eq 1 -and $taskUnload[0].observedTick -lt $taskBulk[0].endTick) {
        $Problems.Add('First unload starts before first bulk cleanup.')
    }
    $taskSequences=@{}
    foreach($taskRecord in @($D.transfers)+@($D.queries)+@($D.gates)) {
        if($taskRecord.sequence -le 0 -or $taskSequences.ContainsKey($taskRecord.sequence)) { $Problems.Add('Duplicate/nonpositive storage operation sequence.') }
        $taskSequences[$taskRecord.sequence]=$true
    }
    foreach($taskTransfer in $D.transfers) {
        $taskLabel=$taskTransfer.kind+'/'+$taskTransfer.sequence
        Test-StorageRecordContext $taskTransfer $taskTransfer.after $D $taskLabel $Problems
        if($taskTransfer.units -lt 0 -or $taskTransfer.units -gt 10) { $Problems.Add('Storage transfer quantity is outside the scene: '+$taskLabel) }
        if($null -ne $taskTransfer.before) {
            Test-StorageStateContext $taskTransfer.before $D ('before '+$taskLabel) $Problems
            if($taskTransfer.before.tick -ne $taskTransfer.tick -or $taskTransfer.before.jobId -ne $taskTransfer.jobId -or
                $taskTransfer.before.sequence -ge $taskTransfer.after.sequence) { $Problems.Add('Storage operation has inconsistent before/after order: '+$taskLabel) }
        }
        if($taskTransfer.kind -eq 'carry-drop') {
            Test-StorageThingMember $taskTransfer.original $taskTransfer.before ('carried original '+$taskLabel) $Problems
            $taskFloorDelta=($taskTransfer.after.source+$taskTransfer.after.high+$taskTransfer.after.elsewhere)-($taskTransfer.before.source+$taskTransfer.before.high+$taskTransfer.before.elsewhere)
            $taskHeldDelta=($taskTransfer.before.inventory+$taskTransfer.before.hands)-($taskTransfer.after.inventory+$taskTransfer.after.hands)
            if(-not $taskTransfer.original.hands -or $taskTransfer.original.spawned -or $taskTransfer.units -ne $taskFloorDelta -or $taskHeldDelta -ne $taskFloorDelta) {
                $Problems.Add('Actual drop does not transfer its reported units from hands to floor: '+$taskLabel)
            }
            if($taskTransfer.units -gt 0) {
                if(-not $taskTransfer.returnedSuccess -or $null -eq $taskTransfer.resulting) { $Problems.Add('Positive physical drop lacks native success/result: '+$taskLabel) }
                else {
                    Test-StorageThingMember $taskTransfer.resulting $taskTransfer.after ('drop result '+$taskLabel) $Problems
                    if(-not $taskTransfer.resulting.spawned -or $taskTransfer.resulting.destroyed -or $taskTransfer.resulting.count -le 0) { $Problems.Add('Positive drop result is not a live floor Thing: '+$taskLabel) }
                }
            }
        } elseif($taskTransfer.kind -eq 'start-carry') {
            $taskAcquired=[Math]::Max(0,($taskTransfer.after.inventory+$taskTransfer.after.hands)-($taskTransfer.before.inventory+$taskTransfer.before.hands))
            if($taskTransfer.units -ne $taskAcquired -or $taskTransfer.after.high -ne $taskTransfer.before.high -or
                $taskTransfer.before.source-$taskTransfer.after.source -ne $taskAcquired) { $Problems.Add('Carry acquisition does not match actual floor/held deltas: '+$taskLabel) }
        }
    }
    $taskPickups=@($D.transfers | Where-Object { $_.kind -eq 'bulk-pickup' -and $_.jobId -eq $D.firstBulkJobId } | Sort-Object sequence)
    if($taskPickups.Count -eq 2) {
        for($taskIndex=0;$taskIndex -lt 2;$taskIndex++) {
            $taskPickup=$taskPickups[$taskIndex];$taskAfter=$taskPickup.after;$taskHeld=5*($taskIndex+1)
            if($taskPickup.original.id -notin $D.originalSourceIds -or $taskPickup.original.count -ne 5 -or $taskPickup.original.destroyed -or
                $taskAfter.source -ne 10-$taskHeld -or $taskAfter.high -ne 65 -or $taskAfter.inventory -ne $taskHeld -or $taskAfter.hands -ne 0 -or
                @($taskAfter.things | Where-Object { $_.spawned -and $_.id -eq $taskPickup.original.id }).Count -gt 0) {
                $Problems.Add('Actual pickup does not progress from five to ten held units with the original source removed.')
            }
            if($taskIndex -eq 0) {
                $taskInventory=@($taskAfter.things | Where-Object inventory)
                if($taskInventory.Count -ne 1 -or $taskInventory[0].id -ne $taskPickup.original.id -or $taskInventory[0].count -ne 5) {
                    $Problems.Add('First whole-stack pickup does not retain its actual inventory identity.')
                }
            }
        }
    }
    $taskOwned=@();$taskGates=@()
    foreach($taskQuery in $D.queries) {
        Test-StorageRecordContext $taskQuery $taskQuery.before $D ('query/'+$taskQuery.sequence) $Problems
        if($taskQuery.jobDef -ne $taskQuery.before.jobDef -or $taskQuery.delivering -notin @(-1,0,1) -or $taskQuery.productionLiveUnits -lt -1 -or
            $taskQuery.free -lt 0 -or $taskQuery.taggedInventory -lt 0 -or ($taskQuery.highGroup -and $taskQuery.group -ne ('critical@'+$D.highCell))) {
            $Problems.Add('Storage production query has inconsistent identity/domain values.')
        }
        if($taskQuery.jobId -eq $D.firstUnloadJobId -and $taskQuery.highGroup -and $taskQuery.subject.inventory -and -not $taskQuery.subject.spawned -and
            $taskQuery.before.inventory -eq 10 -and $taskQuery.before.hands -eq 0 -and $taskQuery.before.source -eq 0 -and $taskQuery.before.high -eq 65 -and
            $taskQuery.taggedInventory -eq 10 -and @($taskQuery.before.claims | Where-Object { $_.high -and $_.group -eq $taskQuery.group -and $_.recordedUnits -eq 10 }).Count -eq 1 -and -not $taskQuery.truncated) {
            Test-StoragePhysicalTotal $taskQuery.before ('owned query/'+$taskQuery.sequence) $Problems $D
            Test-StorageThingMember $taskQuery.subject $taskQuery.before ('owned query/'+$taskQuery.sequence) $Problems
            $taskOwned+=$taskQuery
        }
    }
    foreach($taskGate in $D.gates) {
        Test-StorageRecordContext $taskGate $taskGate.before $D ('gate/'+$taskGate.sequence) $Problems
        if($taskGate.jobDef -ne $taskGate.before.jobDef) { $Problems.Add('Storage cell gate names a different current job from its snapshot.') }
        if($taskGate.jobId -eq $D.firstUnloadJobId -and $taskGate.subject.inventory -and -not $taskGate.subject.spawned -and
            $taskGate.before.high -eq 65 -and $taskGate.before.inventory -eq 10 -and $taskGate.before.source -eq 0 -and $taskGate.before.hands -eq 0) {
            Test-StoragePhysicalTotal $taskGate.before ('owned gate/'+$taskGate.sequence) $Problems $D
            Test-StorageThingMember $taskGate.subject $taskGate.before ('owned gate/'+$taskGate.sequence) $Problems
            $taskGates+=$taskGate
        }
    }
    $taskAccepted=@($taskOwned | Where-Object { $_.delivering -eq 1 -and $_.free -eq 10 }).Count -gt 0 -and @($taskGates | Where-Object allowed).Count -gt 0
    $taskRejected=@($taskOwned | Where-Object { $_.delivering -eq 0 -and $_.free -eq 0 -and $_.productionLiveUnits -eq 10 }).Count -gt 0 -and @($taskGates | Where-Object { -not $_.allowed }).Count -gt 0
    if($D.highStorageAccepted -ne $taskAccepted -or $D.highStorageRejected -ne $taskRejected) { $Problems.Add('Storage capacity summary differs from actual owned query/gate records.') }
    $taskFirstDrops=@($D.transfers | Where-Object { $_.kind -eq 'carry-drop' -and $_.jobId -eq $D.firstUnloadJobId -and $_.units -gt 0 })
    if($taskFirstDrops.Count -eq 1 -and $taskPickups.Count -eq 2) {
        $taskDrop=$taskFirstDrops[0];$taskLastPickup=$taskPickups[1]
        $taskQueries=@($taskOwned | Where-Object { $_.sequence -gt $taskLastPickup.sequence -and $_.sequence -lt $taskDrop.before.sequence -and
            $(if($taskCorrected){$_.delivering -eq 1 -and $_.free -eq 10}else{$_.delivering -eq 0 -and $_.free -eq 0 -and $_.productionLiveUnits -eq 10}) })
        $taskGateWitness=@($taskGates | Where-Object { $_.sequence -gt $taskLastPickup.sequence -and $_.sequence -lt $taskDrop.before.sequence -and $_.allowed -eq $taskCorrected })
        $taskPairs=@(foreach($taskQ in $taskQueries) { foreach($taskG in $taskGateWitness) { if($taskQ.subject.id -eq $taskG.subject.id -and $taskQ.sequence -lt $taskG.sequence) { $true } } })
        if($taskPairs.Count -eq 0) { $Problems.Add('Storage first delivery lacks ordered pickup, matching owned query/gate, and physical drop witnesses.') }
    }
    Test-StorageStateContext $D.finalState $D 'final-state' $Problems
    $taskLaterAcquired=0;$taskLaterDelivered=0
    foreach($taskTransfer in $D.transfers) {
        $taskJobs=@($D.jobs | Where-Object { $_.id -eq $taskTransfer.jobId -and $_.def -eq 'HaulToCell' -and $_.driver -eq 'Verse.AI.JobDriver_HaulToCell' })
        if($taskJobs.Count -ne 1 -or $taskTransfer.tick -lt $D.firstUnloadEndTick -or $D.firstUnloadSource -ne 10) { continue }
        if($taskTransfer.kind -eq 'start-carry') { $taskLaterAcquired+=$taskTransfer.units }
        if($taskTransfer.kind -eq 'carry-drop') { $taskLaterDelivered+=[Math]::Max(0,$taskTransfer.after.high-$taskTransfer.before.high) }
    }
    $taskNoRehaul=$taskLaterAcquired -eq 0 -and @($D.jobs | Where-Object def -eq 'HaulersDream_BulkHaul').Count -eq 1 -and
        @($D.jobs | Where-Object def -eq 'HaulersDream_UnloadInventory').Count -eq 1 -and @($D.jobs | Where-Object def -eq 'HaulToCell').Count -eq 0
    $taskSettled=$D.stableSinceTick -ge $D.firstUnloadEndTick -and $D.finishedTick-$D.stableSinceTick -ge 600 -and $D.stableDistinctTicks -ge 600 -and (Test-StorageHighState $D.finalState)
    $taskLaterConverged=$D.baselineGapObserved -and $taskLaterAcquired -eq 10 -and $taskLaterDelivered -eq 10 -and $taskSettled -and
        @($D.jobs | Where-Object { $_.def -eq 'HaulToCell' -and $_.driver -eq 'Verse.AI.JobDriver_HaulToCell' -and $_.endCondition -eq 'Succeeded' -and $_.released -and $_.observedTick -ge $D.firstUnloadEndTick }).Count -gt 0
    if($taskLaterConverged) {
        $taskNativeIds=@($D.jobs | Where-Object { $_.def -eq 'HaulToCell' -and $_.driver -eq 'Verse.AI.JobDriver_HaulToCell' -and $_.endCondition -eq 'Succeeded' -and $_.released } | ForEach-Object id)
        $taskNativePickups=@($D.transfers | Where-Object { $_.kind -eq 'start-carry' -and $_.units -eq 10 -and $_.jobId -in $taskNativeIds -and $_.tick -ge $D.firstUnloadEndTick })
        $taskNativeDrops=@($D.transfers | Where-Object { $_.kind -eq 'carry-drop' -and $_.units -eq 10 -and $_.jobId -in $taskNativeIds -and
            $_.after.high-$_.before.high -eq 10 -and $_.tick -ge $D.firstUnloadEndTick })
        $taskLaterConverged=$taskNativePickups.Count -eq 1 -and $taskNativeDrops.Count -eq 1 -and
            $taskNativePickups[0].jobId -eq $taskNativeDrops[0].jobId -and $taskNativePickups[0].sequence -lt $taskNativeDrops[0].before.sequence
    }
    if($D.laterSourceReacquired -ne $taskLaterAcquired -or $D.laterNativeHandDelivered -ne $taskLaterDelivered -or $D.noRehaul -ne $taskNoRehaul -or
        $D.settledHigh -ne $taskSettled -or $D.laterNativeHandHaulConverged -ne $taskLaterConverged -or $D.stableDistinctTicks -lt 0 -or
        $D.stableDistinctTicks -gt $D.finishedTick-$D.startedTick+1 -or $D.stableSinceTick -lt -1) {
        $Problems.Add('Storage later-haul/stability summaries contradict raw transfer/job/final records.')
    }
}

function Read-StoragePlanBudget([string]$Text, [string]$Label, $Problems) {
    # This fixture explicitly reads the published scalar budget fields. Reject
    # incomplete, unbounded or ambiguous snapshots instead of trusting its prose.
    if($Text -notmatch '^emptyCells=([0-3]); (.+)$') { $Problems.Add('CAP01 unreadable/bounded plan budget: '+$Label); return $null }
    $taskEmpty=[int]$Matches[1]; $taskEntries=$Matches[2].Split(';'); $taskDefs=@{}
    foreach($taskEntry in $taskEntries) {
        if($taskEntry.Trim() -notmatch '^(Silver|Cloth|WoodLog|Steel):partial=([0-9]{1,4}),perCell=([0-9]{1,4})$') {
            $Problems.Add('CAP01 unreadable plan budget definition: '+$Label); return $null
        }
        $taskDef=$Matches[1]; $taskPartial=[int]$Matches[2]; $taskPerCell=[int]$Matches[3]
        if($taskDefs.ContainsKey($taskDef) -or $taskPartial -gt 1500 -or $taskPerCell -lt 1 -or $taskPerCell -gt 1500) {
            $Problems.Add('CAP01 duplicate/out-of-range plan budget definition: '+$Label); return $null
        }
        $taskDefs[$taskDef]=[pscustomobject]@{partial=$taskPartial;perCell=$taskPerCell}
    }
    return [pscustomobject]@{empty=$taskEmpty;defs=$taskDefs}
}

function Test-StoragePlanEvents($Slots, $Events, [string]$Expected, $Problems) {
    $taskCorrected=$Expected -eq 'satisfied'
    # Source-derived operation order: OccupiedPlan twice, PartialControl,
    # FullControl, EmptyControl. Claims call Free/Gate, not this plan API.
    $taskTrace=New-Object 'System.Collections.Generic.List[object]'
    foreach($taskPair in @(@('plan-silver-then-cloth','Silver',500,'Cloth',75),@('plan-cloth-then-silver','Cloth',75,'Silver',500))) {
        $taskTrace.Add(@($taskPair[0],'price',$taskPair[1],$taskPair[2],0))
        $taskTrace.Add(@($taskPair[0],'consume',$taskPair[1],0,$taskPair[2]))
        $taskTrace.Add(@($taskPair[0],'price',$taskPair[1],0,0))
        $taskTrace.Add(@($taskPair[0],'price',$taskPair[3],$(if($taskCorrected){0}else{$taskPair[4]}),0))
    }
    foreach($taskRow in @(@('price','Silver',7,0),@('consume','Silver',0,7),@('price','Silver',0,0),@('price','Cloth',0,0))) {
        $taskTrace.Add(@('genuine-silver-top-up')+$taskRow)
    }
    foreach($taskDef in @('Silver','Cloth')) { $taskTrace.Add(@('full-incompatible','price',$taskDef,0,0)) }
    $taskTrace.Add(@('empty-three-slot-mixed-plan','price','Silver',1500,0))
    $taskTrace.Add(@('empty-three-slot-mixed-plan','consume','Silver',1000,500))
    foreach($taskPair in @(@('Cloth',150,75),@('WoodLog',75,0))) {
        $taskTrace.Add(@('empty-three-slot-mixed-plan','price',$taskPair[0],$(if($taskCorrected){$taskPair[1]}else{0}),0))
        $taskTrace.Add(@('empty-three-slot-mixed-plan',$(if($taskCorrected){'consume'}else{'rejected'}),$taskPair[0],$(if($taskCorrected){$taskPair[2]}else{0}),$(if($taskCorrected){75}else{0})))
    }
    $taskTrace.Add(@('empty-three-slot-mixed-plan','price','Steel',0,0))
    $taskTrace.Add(@('empty-three-slot-mixed-plan','rejected','Steel',0,0))
    $taskRows=@($Events | Where-Object { $_.phase.StartsWith('storage-slots-plan-') })
    if($taskRows.Count -ne $taskTrace.Count) { $Problems.Add('CAP01 needs its exact 22 ordered plan price/consume/rejection events.') }
    foreach($taskPhase in @(@('price',15),@('consume',$(if($taskCorrected){6}else{4})),@('rejected',$(if($taskCorrected){1}else{3})))) {
        if(@($taskRows | Where-Object phase -eq ('storage-slots-plan-'+$taskPhase[0])).Count -ne $taskPhase[1]) {
            $Problems.Add('CAP01 plan operation count differs from actual admission contract: '+$taskPhase[0])
        }
    }
    $taskBudgets=@{}
    for($taskIndex=0;$taskIndex -lt [Math]::Min($taskRows.Count,$taskTrace.Count);$taskIndex++) {
        $taskEvent=$taskRows[$taskIndex]; $taskWant=$taskTrace[$taskIndex]
        $taskScene=[string]$taskWant[0]; $taskKind=[string]$taskWant[1]; $taskDef=[string]$taskWant[2]
        $taskLabel=$taskScene+'/'+$taskKind+'/'+$taskDef+' at '+$taskEvent.sequence
        if($taskEvent.phase -ne 'storage-slots-plan-'+$taskKind -or $taskEvent.tick -ne $Slots.startedTick) {
            $Problems.Add('CAP01 plan operation phase/order/tick differs: '+$taskLabel); continue
        }
        $taskShelf=@($Slots.shelves | Where-Object scene -eq $taskScene)
        if($taskShelf.Count -ne 1) { $Problems.Add('CAP01 plan group lacks its unique physical shelf scene: '+$taskLabel); continue }
        $taskBudgetText=$null; $taskSubject=$null; $taskUnits=0
        if($taskKind -eq 'price') {
            if($taskEvent.detail -notmatch '^scene=([^;]+); subject=([A-Za-z]+[1-9][0-9]*); def=([A-Za-z]+); available=([0-9]{1,4}); budget=(.+)$') {
                $Problems.Add('CAP01 unreadable plan price payload: '+$taskLabel); continue
            }
            $taskActualScene=$Matches[1];$taskSubject=$Matches[2];$taskActualDef=$Matches[3];$taskAnswer=[int]$Matches[4];$taskBudgetText=$Matches[5]
        } elseif($taskKind -eq 'consume') {
            if($taskEvent.detail -notmatch '^scene=([^;]+); def=([A-Za-z]+); virtualAllocatedUnits=([0-9]{1,4}); remainingForDef=([0-9]{1,4}); budget=(.+)$') {
                $Problems.Add('CAP01 unreadable plan consume payload: '+$taskLabel); continue
            }
            $taskActualScene=$Matches[1];$taskActualDef=$Matches[2];$taskUnits=[int]$Matches[3];$taskAnswer=[int]$Matches[4];$taskBudgetText=$Matches[5]
        } else {
            if($taskEvent.detail -notmatch '^([^;]+); def=([A-Za-z]+); actualAvailable=([0-9]{1,4}); no Consume call$') {
                $Problems.Add('CAP01 unreadable plan rejection payload: '+$taskLabel); continue
            }
            $taskActualScene=$Matches[1];$taskActualDef=$Matches[2];$taskAnswer=[int]$Matches[3]
        }
        if($taskActualScene -ne $taskScene -or $taskActualDef -ne $taskDef -or $taskAnswer -ne $taskWant[3] -or $taskUnits -ne $taskWant[4]) {
            $Problems.Add('CAP01 plan scene/definition/quantity differs from its ordered control: '+$taskLabel); continue
        }
        $taskPrevious=if($taskBudgets.ContainsKey($taskScene)){$taskBudgets[$taskScene]}else{$null}
        if($taskKind -eq 'rejected') {
            if($null -eq $taskPrevious -or -not $taskPrevious.defs.ContainsKey($taskDef) -or
                $taskPrevious.defs[$taskDef].partial+[long]$taskPrevious.empty*$taskPrevious.defs[$taskDef].perCell -ne $taskAnswer) {
                $Problems.Add('CAP01 rejection lacks its preceding actual zero-allowance budget: '+$taskLabel)
            }
            continue
        }
        $taskBudget=Read-StoragePlanBudget $taskBudgetText $taskLabel $Problems
        if($null -eq $taskBudget) { continue }
        if(-not $taskBudget.defs.ContainsKey($taskDef)) { $Problems.Add('CAP01 plan budget omits its current definition: '+$taskLabel); continue }
        $taskCurrent=$taskBudget.defs[$taskDef]
        if($taskCurrent.partial+[long]$taskBudget.empty*$taskCurrent.perCell -ne $taskAnswer) {
            $Problems.Add('CAP01 reported plan allowance contradicts its actual budget fields: '+$taskLabel)
        }
        if($taskKind -eq 'price') {
            $taskMeasure=@($Slots.measurements | Where-Object { $_.scene -eq $taskScene -and $_.def -eq $taskDef })
            if($taskScene -eq 'genuine-silver-top-up' -and $taskDef -eq 'Cloth') {
                $taskFloor=@($Slots.assertions | Where-Object { $_.kind -eq 'fixture' -and $_.id -eq ($taskScene+'-floor-'+$taskSubject) -and $_.passed })
                if($taskSubject -notmatch '^Cloth[1-9][0-9]*$' -or $taskFloor.Count -ne 1) { $Problems.Add('CAP01 top-up control price has a foreign floor subject: '+$taskLabel) }
            } elseif($taskMeasure.Count -ne 1 -or $taskMeasure[0].subject -ne $taskSubject) {
                $Problems.Add('CAP01 plan price does not identify its actual measured subject/group: '+$taskLabel)
            }
            if($null -eq $taskPrevious) {
                if($taskMeasure.Count -ne 1 -or $taskBudget.defs.Count -ne 1 -or $taskBudget.empty -ne $taskMeasure[0].emptyCells -or
                    $taskCurrent.partial -ne $taskMeasure[0].partialSpace -or $taskCurrent.perCell -ne $taskMeasure[0].perCellCapacity) {
                    $Problems.Add('CAP01 initial plan budget differs from its production measurement: '+$taskLabel)
                }
            } else {
                $taskNewDef=-not $taskPrevious.defs.ContainsKey($taskDef)
                $taskWantedKeys=$taskPrevious.defs.Count+[int]$taskNewDef
                if($taskBudget.empty -ne $taskPrevious.empty -or $taskBudget.defs.Count -ne $taskWantedKeys) {
                    $Problems.Add('CAP01 pricing resets shared capacity or changes unrelated definition keys: '+$taskLabel)
                }
                foreach($taskOldDef in $taskPrevious.defs.Keys) {
                    if(-not $taskBudget.defs.ContainsKey($taskOldDef) -or
                        $taskBudget.defs[$taskOldDef].partial -ne $taskPrevious.defs[$taskOldDef].partial -or
                        $taskBudget.defs[$taskOldDef].perCell -ne $taskPrevious.defs[$taskOldDef].perCell) {
                        $Problems.Add('CAP01 pricing replenishes or changes an already priced/spent definition: '+$taskLabel)
                    }
                }
                if($taskNewDef -and $taskMeasure.Count -eq 1 -and ($taskCurrent.partial -ne $taskMeasure[0].partialSpace -or $taskCurrent.perCell -ne $taskMeasure[0].perCellCapacity)) {
                    $Problems.Add('CAP01 new definition price differs from its actual production measurement: '+$taskLabel)
                }
                if($taskNewDef -and $taskScene -eq 'genuine-silver-top-up' -and $taskDef -eq 'Cloth' -and ($taskCurrent.partial -ne 0 -or $taskCurrent.perCell -ne 75)) {
                    $Problems.Add('CAP01 incompatible cloth price invents room in the full top-up control: '+$taskLabel)
                }
            }
        } else {
            if($null -eq $taskPrevious -or -not $taskPrevious.defs.ContainsKey($taskDef)) {
                $Problems.Add('CAP01 consume lacks its previously priced definition/group: '+$taskLabel)
            } else {
                $taskOld=$taskPrevious.defs[$taskDef]
                $taskBefore=$taskOld.partial+[long]$taskPrevious.empty*$taskOld.perCell
                if($taskBefore-$taskUnits -ne $taskAnswer -or $taskBudget.empty -gt $taskPrevious.empty -or
                    $taskBudget.defs.Count -ne $taskPrevious.defs.Count -or $taskCurrent.perCell -ne $taskOld.perCell) {
                    $Problems.Add('CAP01 consumed units do not spend the preceding actual allowance: '+$taskLabel)
                }
                foreach($taskOldDef in $taskPrevious.defs.Keys) {
                    if(-not $taskBudget.defs.ContainsKey($taskOldDef) -or ($taskOldDef -ne $taskDef -and
                        ($taskBudget.defs[$taskOldDef].partial -ne $taskPrevious.defs[$taskOldDef].partial -or
                         $taskBudget.defs[$taskOldDef].perCell -ne $taskPrevious.defs[$taskOldDef].perCell))) {
                        $Problems.Add('CAP01 consume changes an unrelated priced definition: '+$taskLabel)
                    }
                }
            }
        }
        $taskBudgets[$taskScene]=$taskBudget
    }
}

function Read-StorageEventDetails($Events, [string]$Phase, $Problems) {
    foreach($taskEvent in @($Events | Where-Object phase -eq $Phase)) {
        try {
            $taskData=ConvertFrom-Json -InputObject $taskEvent.detail -ErrorAction Stop
            if($taskData -isnot [pscustomobject]) { $Problems.Add('Storage event detail must be an object: '+$Phase); continue }
            [pscustomobject]@{event=$taskEvent;data=$taskData}
        } catch { $Problems.Add('Unreadable storage event detail: '+$Phase+'; '+$_.Exception.Message) }
    }
}

function Test-StorageEventEvidence($Value, $Events, [string]$Case, [string]$Expected, [string]$RunId, $Problems) {
    $taskStartProblems=$Problems.Count
    $taskPrior=0; $taskPriorTick=[int]::MinValue
    foreach($taskEvent in $Events) {
        if(-not (Test-EvidenceFields $taskEvent @{caseId='string';runId='string';phase='string';detail='string';sequence='integer';tick='integer'} 'storage event' $Problems)) { continue }
        # ConvertFrom-Json preserves ISO text on Windows PowerShell and parses DateTime on PowerShell 7.
        if($null -eq $taskEvent.PSObject.Properties['utc'] -or ($taskEvent.utc -isnot [string] -and $taskEvent.utc -isnot [DateTime])) {
            $Problems.Add('Storage event lacks its UTC timestamp.')
        }
        if($taskEvent.runId -ne $RunId -or $taskEvent.caseId -ne $Case -or $taskEvent.sequence -le $taskPrior -or $taskEvent.tick -lt $taskPriorTick) {
            $Problems.Add('Storage events have foreign identity, non-increasing capture sequence, or a backwards game tick.')
        }
        $taskPrior=$taskEvent.sequence; $taskPriorTick=$taskEvent.tick
    }
    if($Problems.Count -ne $taskStartProblems) { return }
    $taskStorageAssertionPattern=if($Case -eq 'CAP01'){'^storage-slots-'}else{'^(fixture|execution|behavior)-storage-delivery-'}
    $taskAssertionText=@(foreach($taskA in @($Value.assertions | Where-Object { $_.id -match $taskStorageAssertionPattern })) {
        $taskA.id+': '+$(if($taskA.passed){'passed'}else{'failed'})+'; '+$taskA.observed
    })
    # General Unity capture assertions are finalized by Bootstrap and retain their existing controller checks.
    Test-StorageBags $taskAssertionText @($Events | Where-Object { $_.phase -eq 'assertion' -and $_.detail -match $taskStorageAssertionPattern } | ForEach-Object detail) 'storage result/assertion events' $Problems
    if($Case -eq 'CAP01') {
        $taskS=$Value.storageSlots
        foreach($taskPair in @(@('storage-slots-physical','shelves'),@('storage-slots-measurement','measurements'),@('storage-slots-observation','observations'))) {
            $taskRows=@(Read-StorageEventDetails $Events $taskPair[0] $Problems)
            Test-StorageBags $taskS.($taskPair[1]) @($taskRows | ForEach-Object data) ('CAP01 '+$taskPair[0]) $Problems
            foreach($taskRow in $taskRows) { if($taskRow.event.tick -ne $taskS.startedTick) { $Problems.Add('CAP01 data event is outside its single measured tick.') } }
        }
        $taskTerminal=@(Read-StorageEventDetails $Events 'storage-slots-result' $Problems)
        Test-StorageBags @($taskS) @($taskTerminal | ForEach-Object data) 'CAP01 terminal result event' $Problems
        Test-StoragePlanEvents $taskS $Events $Expected $Problems
        foreach($taskShelf in $taskS.shelves) {
            $taskRows=@($taskS.assertions | Where-Object { $_.kind -eq 'fixture' -and $_.id -eq ($taskShelf.scene+'-physical-unchanged') })
            if($taskRows.Count -ne 1) { continue }
            $taskPrefix='Budget/claim calls did not deposit cargo or change shelf items: '
            if(-not $taskRows[0].detail.StartsWith($taskPrefix)) { $Problems.Add('CAP01 physical end witness lacks its actual snapshot.'); continue }
            try { Test-StorageBags @($taskShelf) @((ConvertFrom-Json -InputObject $taskRows[0].detail.Substring($taskPrefix.Length))) 'CAP01 unchanged actual shelf snapshot' $Problems }
            catch { $Problems.Add('CAP01 end physical snapshot is unreadable.') }
        }
        $taskCargo=@()
        foreach($taskEvent in @($Events | Where-Object phase -eq 'storage-slots-setup-cargo')) {
            if($taskEvent.detail -notmatch '^pawn=(Human[1-9][0-9]*); thing=((Silver|Cloth)[1-9][0-9]*); def=(Silver|Cloth); count=([1-9][0-9]*); ParentHolder=Verse.Pawn_InventoryTracker; real tagged inventory; no executed pickup$') {
                $Problems.Add('CAP01 tagged inventory event lacks its exact custody/identity payload.'); continue
            }
            $taskCargo+=[pscustomobject]@{pawn=$Matches[1];thing=$Matches[2];def=$Matches[4];count=[int]$Matches[5]}
        }
        if($taskCargo.Count -ne 3 -or @($taskCargo | ForEach-Object pawn | Select-Object -Unique).Count -ne 3 -or
            @($taskCargo | ForEach-Object thing | Select-Object -Unique).Count -ne 3) { $Problems.Add('CAP01 needs three distinct physical tagged cargo/carrier witnesses.') }
        $taskClaims=@($Events | Where-Object phase -eq 'storage-slots-live-claim')
        foreach($taskExpected in @(@('claim-silver-claim-then-cloth','Silver',500),@('claim-cloth-claim-then-silver','Cloth',75),@('genuine-silver-top-up','Silver',7))) {
            $taskMatches=@($taskClaims | Where-Object { $_.detail.StartsWith('scene='+$taskExpected[0]+'; ') })
            $taskCargoMatches=@($taskCargo | Where-Object { $_.def -eq $taskExpected[1] -and $_.count -eq $taskExpected[2] })
            if($taskMatches.Count -ne 1 -or $taskCargoMatches.Count -ne 1) { $Problems.Add('CAP01 lacks its unique live claim/tagged cargo: '+$taskExpected[0]); continue }
            $taskC=$taskCargoMatches[0]
            $taskClaimText='scene='+$taskExpected[0]+'; carrier='+$taskC.pawn+'; def='+$taskExpected[1]+'; units='+$taskExpected[2]+'; evidence='+$taskExpected[2]
            if($taskMatches[0].detail -ne $taskClaimText -or $taskMatches[0].tick -ne $taskS.startedTick -or
                -not (Test-RequiredAssertion $Value.assertions ('storage-slots-fixture-held-transfer-'+$taskC.thing) $true) -or
                -not (Test-RequiredAssertion $Value.assertions ('storage-slots-fixture-tag-comp-'+$taskC.pawn) $true)) {
                $Problems.Add('CAP01 live claim does not match its actual tagged carrier/quantity.')
            }
            if($taskExpected[0] -ne 'genuine-silver-top-up') {
                $taskMeasured=@($taskS.measurements | Where-Object { $_.scene -eq $taskExpected[0] -and $_.def -eq $taskExpected[1] })
                if($taskMeasured.Count -ne 1 -or $taskMeasured[0].subject -ne $taskC.thing) { $Problems.Add('CAP01 foreign claim is not the measured held subject.') }
            }
        }
        return
    }
    if($Case -ne 'L04-O1-DELIVERY') { return }
    $taskD=$Value.storageDelivery
    $taskTransferEvents=New-Object 'System.Collections.Generic.List[object]'
    foreach($taskPair in @(@('storage-delivery-bulk-pickup','bulk-pickup'),@('storage-delivery-physical-deposit','carry-drop'),@('storage-delivery-carry-transfer','start-carry'))) {
        $taskRows=@(Read-StorageEventDetails $Events $taskPair[0] $Problems)
        Test-StorageBags @($taskD.transfers | Where-Object kind -eq $taskPair[1]) @($taskRows | ForEach-Object data) $taskPair[0] $Problems
        foreach($taskRow in $taskRows) { $taskTransferEvents.Add($taskRow) }
    }
    $taskPriorTransferSequence=0
    foreach($taskTransfer in $taskD.transfers) {
        if($taskTransfer.sequence -le $taskPriorTransferSequence) { $Problems.Add('Storage result transfer order contradicts completed-operation snapshots.') }
        $taskPriorTransferSequence=$taskTransfer.sequence
    }
    foreach($taskPair in @(@('storage-delivery-free-query','queries'),@('storage-delivery-cell-gate','gates'))) {
        $taskRows=@(Read-StorageEventDetails $Events $taskPair[0] $Problems)
        Test-StorageBags $taskD.($taskPair[1]) @($taskRows | ForEach-Object data) $taskPair[0] $Problems
        if($taskPair[1] -eq 'gates') { foreach($taskRow in $taskRows) { $taskTransferEvents.Add($taskRow) } }
    }
    $taskTerminal=@(Read-StorageEventDetails $Events 'storage-delivery-result' $Problems)
    Test-StorageBags @($taskD) @($taskTerminal | ForEach-Object data) 'storage delivery terminal result event' $Problems
    $taskStarts=@(Read-StorageEventDetails $Events 'storage-delivery-current-job' $Problems)
    $taskCleanups=@(Read-StorageEventDetails $Events 'storage-delivery-job-cleanup' $Problems)
    $taskInitialJobs=@(foreach($taskJob in $taskD.jobs) {
        [pscustomobject]@{id=$taskJob.id;observedTick=$taskJob.observedTick;endTick=-1;def=$taskJob.def;driver=$taskJob.driver;workgiver=$taskJob.workgiver;
            workgiverClass=$taskJob.workgiverClass;queue=$taskJob.queue;endCondition=$null;forced=$taskJob.forced;candidateObserved=$taskJob.candidateObserved;released=$false}
    })
    Test-StorageBags $taskInitialJobs @($taskStarts | ForEach-Object data) 'storage actual job-start records' $Problems
    Test-StorageBags @($taskD.jobs | Where-Object { $_.endTick -ge 0 }) @($taskCleanups | ForEach-Object data) 'storage actual cleanup records' $Problems
    # Use reconciled result records as the schema for event details before dereferencing them.
    $taskEventJobsValid=$true
    foreach($taskRow in @($taskStarts)+@($taskCleanups)) {
        if(-not (Test-EvidenceFields $taskRow.data @{id='integer';observedTick='integer';endTick='integer';def='string'} 'storage job event' $Problems)) { $taskEventJobsValid=$false }
    }
    if(-not $taskEventJobsValid) { return }
    foreach($taskJob in $taskD.jobs) {
        $taskStart=@($taskStarts | Where-Object { $_.data.id -eq $taskJob.id })
        $taskEnd=@($taskCleanups | Where-Object { $_.data.id -eq $taskJob.id })
        if($taskStart.Count -eq 1 -and $taskStart[0].event.tick -ne $taskJob.observedTick) { $Problems.Add('Storage current-job event has a different actual tick.') }
        if($taskEnd.Count -eq 1 -and ($taskEnd[0].event.tick -ne $taskJob.endTick -or $taskStart.Count -ne 1 -or
            $taskEnd[0].event.sequence -le $taskStart[0].event.sequence)) { $Problems.Add('Storage cleanup event precedes its actual start or changes tick.') }
    }
    $taskUnloadCleanup=@($taskCleanups | Where-Object { $_.data.id -eq $taskD.firstUnloadJobId })
    $taskBulkCleanup=@($taskCleanups | Where-Object { $_.data.id -eq $taskD.firstBulkJobId })
    $taskUnloadStart=@($taskStarts | Where-Object { $_.data.id -eq $taskD.firstUnloadJobId })
    if($taskBulkCleanup.Count -ne 1 -or $taskUnloadStart.Count -ne 1 -or $taskUnloadCleanup.Count -ne 1) { $Problems.Add('Storage first chain lacks unique actual start/cleanup events.'); return }
    if($taskBulkCleanup[0].event.sequence -ge $taskUnloadStart[0].event.sequence) { $Problems.Add('Storage unload current-job event precedes bulk cleanup.') }
    $taskBulkStart=@($taskStarts | Where-Object { $_.data.id -eq $taskD.firstBulkJobId })
    $taskCandidateWitnesses=0
    foreach($taskCandidate in @($Events | Where-Object phase -eq 'storage-delivery-candidate')) {
        if($taskCandidate.detail -notmatch '^id=([0-9]+); def=HaulersDream_BulkHaul; source=([1-9][0-9]*); forcedArgument=False; queue=(.*)$') { continue }
        $taskCandidateId=[long]$Matches[1];$taskCandidateSource=[long]$Matches[2];$taskQueue=@($Matches[3].Split(',') | Sort-Object)
        $taskWanted=@($taskD.originalSourceIds | ForEach-Object { [string]$_+'x5' } | Sort-Object)
        if($taskCandidateId -eq $taskD.firstBulkJobId -and $taskCandidateSource -in $taskD.originalSourceIds -and
            ($taskQueue -join ',') -eq ($taskWanted -join ',') -and $taskBulkStart.Count -eq 1 -and
            $taskCandidate.sequence -lt $taskBulkStart[0].event.sequence -and $taskCandidate.tick -ge $taskD.startedTick) { $taskCandidateWitnesses++ }
    }
    if($taskCandidateWitnesses -eq 0) { $Problems.Add('First actual bulk job lacks its preceding automatic original-source candidate event.') }
    $taskStates=@(Read-StorageEventDetails $Events 'storage-delivery-settled-state' $Problems)
    if($taskStates.Count -eq 0) { $Problems.Add('Storage delivery lacks actual settled states.'); return }
    $taskInitialProblems=$Problems.Count
    Test-StorageStateShape $taskStates[0].data 'storage initial settled event' $Problems
    if($Problems.Count -eq $taskInitialProblems) {
        $taskInitial=$taskStates[0].data
        $taskOriginal=@($taskInitial.things | Where-Object { $_.id -in $taskD.originalSourceIds })
        if($taskInitial.tick -ne $taskD.startedTick -or $taskInitial.jobId -ne -1 -or $taskInitial.source -ne 10 -or $taskInitial.high -ne 65 -or
            $taskInitial.inventory -ne 0 -or $taskInitial.hands -ne 0 -or $taskInitial.claims.Count -ne 0 -or $taskOriginal.Count -ne 2 -or
            @($taskOriginal | Where-Object { -not $_.spawned -or $_.count -ne 5 -or $_.cell -notin $taskD.sourceCells }).Count -gt 0 -or
            @($taskOriginal | ForEach-Object cell | Select-Object -Unique).Count -ne 2) {
            $Problems.Add('Storage initial raw state does not contain the two original five-unit floor stacks and empty actor/claims.')
        }
    }
    $taskStateSequences=@{}; $taskStableStates=@(); $taskPriorStateSequence=0
    $taskFollowupTicks=New-Object 'System.Collections.Generic.HashSet[int]'
    foreach($taskRow in $taskStates) {
        $taskBeforeProblems=$Problems.Count
        Test-StorageStateShape $taskRow.data 'storage settled event' $Problems
        if($Problems.Count -ne $taskBeforeProblems) { continue }
        $taskState=$taskRow.data
        Test-StoragePhysicalTotal $taskState ('settled event/'+$taskState.sequence) $Problems $taskD
        Test-StorageStateContext $taskState $taskD ('settled event/'+$taskState.sequence) $Problems
        if($taskState.tick -ne $taskRow.event.tick -or $taskState.sequence -le $taskPriorStateSequence -or $taskStateSequences.ContainsKey($taskState.sequence)) {
            $Problems.Add('Storage settled-event clock/sequence is inconsistent.')
        }
        $taskPriorStateSequence=$taskState.sequence; $taskStateSequences[$taskState.sequence]=$true
        if($taskRow.event.sequence -le $taskUnloadCleanup[0].event.sequence) { continue }
        if($taskState.tick -gt $taskD.firstUnloadEndTick) { [void]$taskFollowupTicks.Add($taskState.tick) }
        if(Test-StorageHighState $taskState) { $taskStableStates+=$taskState } else { $taskStableStates=@() }
    }
    # Transfer after-states, gate postfixes and settled observations take their
    # snapshot immediately before logging. They share one real snapshot counter.
    # Free-query snapshots belong to a prefix: a nested query can emit first,
    # so query starts must not be treated as completed snapshots in this ordering.
    $taskPriorImmediateSequence=0
    foreach($taskRow in @(@($taskTransferEvents.ToArray())+@($taskStates) | Sort-Object @{Expression={$_.event.sequence}})) {
        if(-not (Test-EvidenceFields $taskRow.data @{sequence='integer'} 'storage immediate snapshot event' $Problems)) { continue }
        if($taskRow.data.sequence -le $taskPriorImmediateSequence) { $Problems.Add('Storage immediate snapshots contradict actual event capture order.') }
        $taskPriorImmediateSequence=$taskRow.data.sequence
    }
    if($taskD.finalState.sequence -le $taskPriorImmediateSequence) { $Problems.Add('Storage final snapshot does not follow the actual last observed boundary.') }
    if($taskD.settledBoundaries -lt $taskStates.Count) { $Problems.Add('Storage boundary count is smaller than its actual logged observations.') }
    $taskRequiredFollowup=[long]$taskD.finishedTick-$taskD.firstUnloadEndTick
    if($taskRequiredFollowup -lt 1200 -or $taskFollowupTicks.Count -ne $taskRequiredFollowup) {
        $Problems.Add('Storage delivery lacks every actual game tick in its complete 1200-tick-or-longer follow-up interval.')
    }
    # Final snapshot is taken immediately after the last ObserveSettled call. Include its tick
    # without inventing intervening ticks or treating the scalar stability flag as proof.
    if(Test-StorageHighState $taskD.finalState) { $taskStableStates+=$taskD.finalState } else { $taskStableStates=@() }
    $taskStableTicks=@($taskStableStates | ForEach-Object tick | Sort-Object -Unique)
    $taskStableSince=if($taskStableTicks.Count){$taskStableTicks[0]}else{-1}
    if($taskD.stableSinceTick -ne $taskStableSince -or $taskD.stableDistinctTicks -ne $taskStableTicks.Count) {
        $Problems.Add('Storage stability counters lack their exact distinct raw tick witnesses.')
    }
    $taskRawSettled=$taskStableTicks.Count -ge 600 -and $taskD.finishedTick-$taskStableSince -ge 600 -and
        $taskStableTicks.Count -eq $taskD.finishedTick-$taskStableSince+1
    if($taskD.settledHigh -ne $taskRawSettled -or (($Expected -eq 'satisfied' -or $taskD.laterNativeHandHaulConverged) -and -not $taskRawSettled)) {
        $Problems.Add('Storage settled/converged outcome lacks a complete actual stable-tick interval.')
    }
    foreach($taskPhase in @('storage-delivery-bulk-pickup','storage-delivery-physical-deposit','storage-delivery-carry-transfer','storage-delivery-free-query','storage-delivery-cell-gate')) {
        foreach($taskRow in @(Read-StorageEventDetails $Events $taskPhase $Problems)) {
            if(-not (Test-EvidenceFields $taskRow.data @{tick='integer';jobId='integer';sequence='integer'} 'storage operation event' $Problems)) { continue }
            $taskJobStart=@($taskStarts | Where-Object { $_.data.id -eq $taskRow.data.jobId })
            $taskJobEnd=@($taskCleanups | Where-Object { $_.data.id -eq $taskRow.data.jobId })
            if($taskRow.event.tick -ne $taskRow.data.tick -or ($taskJobStart.Count -eq 1 -and $taskRow.event.sequence -le $taskJobStart[0].event.sequence) -or
                ($taskJobEnd.Count -eq 1 -and $taskRow.event.sequence -ge $taskJobEnd[0].event.sequence)) {
                $Problems.Add('Storage operation event lies outside actual current-job/cleanup boundaries: '+$taskPhase)
            }
        }
    }
}
