# Read-only BG02 evidence checks. BG01 keeps its established controller contract.
function Test-BulkRecipeShape($Value, $Problems) {
    if (-not (Test-EvidenceFields $Value @{scenario='object'} 'BG02 result' $Problems)) { return }
    $taskRecipe = $Value.scenario
    if (-not (Test-EvidenceFields $taskRecipe @{
        caseId='string'; recipeDefName='string'; expectedRice='integer'; expectedPotatoes='integer'; expectedProductUnits='integer';
        maximumTicks='integer'; requiredStablePostProductTicks='integer'; elapsedTicks='integer'; executionSnapshots='integer';
        initialRiceThingId='string'; initialPotatoThingId='string'; riceConsumed='integer'; potatoesConsumed='integer';
        nativeIngredientConsumptionExact='boolean'; ingredientAcquisitionConserved='boolean'; ingredientConsumptionEvents='array'
    } 'BG02 scenario' $Problems)) { return }
    foreach ($taskConsumption in $taskRecipe.ingredientConsumptionEvents) {
        Test-EvidenceFields $taskConsumption @{
            thingId='string'; defName='string'; beforeCount='integer'; remainingCount='integer'; unitsConsumed='integer';
            destroyed='boolean'; attributed='boolean'; jobId='integer'; tick='integer'; sequence='integer'
        } 'BG02 consumption record' $Problems | Out-Null
    }
}

function Test-BulkRecipeEvidence($Value, $Problems) {
    $taskRecipe=$Value.scenario
    foreach ($taskId in @('fixture-bg02-native-nutrition-recipe','fixture-bg02-bill-batch-disabled','fixture-bg02-native-product-shape',
        'execution-bg02-native-ingredients-consumed-once','execution-bg02-acquisition-conserved')) {
        if (-not (Test-RequiredAssertion $Value.assertions $taskId $true)) { $Problems.Add('Missing unique BG02 assertion: '+$taskId) }
    }
    if ($taskRecipe.caseId -ne 'BG02' -or $taskRecipe.recipeDefName -ne 'CookMealSimpleBulk' -or
        $taskRecipe.expectedRice -ne 20 -or $taskRecipe.expectedPotatoes -ne 20 -or $taskRecipe.expectedProductUnits -ne 4 -or
        $taskRecipe.maximumTicks -ne 12000 -or $taskRecipe.requiredStablePostProductTicks -ne 300 -or
        $taskRecipe.elapsedTicks -le 300 -or $taskRecipe.elapsedTicks -ge 12000 -or $taskRecipe.executionSnapshots -lt 300 -or
        $taskRecipe.initialRiceThingId -notmatch '^RawRice[0-9]+$' -or $taskRecipe.initialPotatoThingId -notmatch '^RawPotatoes[0-9]+$' -or
        $taskRecipe.riceConsumed -ne 20 -or $taskRecipe.potatoesConsumed -ne 20 -or
        -not $taskRecipe.nativeIngredientConsumptionExact -or -not $taskRecipe.ingredientAcquisitionConserved) {
        $Problems.Add('BG02 recipe, initial identity, timing or consumption metadata differs from the explicit ordinary four-meal contract.')
    }
    $taskIds=@{}; $taskSequences=@{}; $taskTotals=@{RawRice=0L; RawPotatoes=0L}; $taskJobs=@{}
    foreach ($taskConsumption in $taskRecipe.ingredientConsumptionEvents) {
        if ($taskConsumption.defName -notin @('RawRice','RawPotatoes') -or
            $taskConsumption.thingId -notmatch ('^'+[regex]::Escape($taskConsumption.defName)+'[0-9]+$') -or
            $taskConsumption.beforeCount -le 0 -or $taskConsumption.beforeCount -gt 20 -or
            $taskConsumption.unitsConsumed -ne $taskConsumption.beforeCount -or $taskConsumption.remainingCount -ne 0 -or
            -not $taskConsumption.destroyed -or -not $taskConsumption.attributed -or
            $taskConsumption.jobId -lt 0 -or $taskConsumption.tick -lt 0 -or $taskConsumption.sequence -le 0 -or
            $taskIds.ContainsKey($taskConsumption.thingId) -or $taskSequences.ContainsKey($taskConsumption.sequence)) {
            $Problems.Add('BG02 ingredient consumption lacks unique, successfully destroyed native ingredient identities.')
        }
        $taskIds[$taskConsumption.thingId]=$true; $taskSequences[$taskConsumption.sequence]=$true; $taskJobs[$taskConsumption.jobId]=$true
        if ($taskTotals.ContainsKey($taskConsumption.defName)) { $taskTotals[$taskConsumption.defName]+=$taskConsumption.unitsConsumed }
    }
    if ($taskTotals.RawRice -ne 20 -or $taskTotals.RawPotatoes -ne 20 -or $taskJobs.Count -ne 1) {
        $Problems.Add('BG02 unique consumption records do not account for 20 rice and 20 potatoes in one native job.')
    }
}

function Get-BillEventFields([string]$Detail) {
    $taskFields=@{}
    foreach ($taskPart in $Detail.Split(';')) {
        $taskPair=$taskPart.Trim().Split([char[]]@('='),2,[StringSplitOptions]::None)
        if ($taskPair.Count -ne 2 -or [string]::IsNullOrWhiteSpace($taskPair[0]) -or $taskFields.ContainsKey($taskPair[0])) {
            throw 'A required bill event has a missing or repeated field.'
        }
        $taskFields[$taskPair[0]]=$taskPair[1]
    }
    return $taskFields
}

function Test-BillEventFields($Fields, [hashtable]$Schema, [string]$Label, $Problems) {
    $taskValid=$true
    foreach ($taskKey in $Schema.Keys) {
        $taskInteger=0
        $taskOk=$Fields.ContainsKey($taskKey)
        if ($taskOk) {
            $taskText=$Fields[$taskKey]
            $taskOk=switch($Schema[$taskKey]) {
                'positive-integer' { $taskText -match '^[0-9]+$' -and [int]::TryParse($taskText,[ref]$taskInteger) -and $taskInteger -gt 0 }
                'integer' { $taskText -match '^-?[0-9]+$' -and [int]::TryParse($taskText,[ref]$taskInteger) }
                'boolean' { $taskText -ceq 'True' -or $taskText -ceq 'False' }
                'pair' { $taskText -match '^(?:[0-9]|1[0-9]|20),(?:[0-9]|1[0-9]|20)$' }
                'cell' { $null -ne (Read-BillCell $taskText $Problems) }
                'possibly-empty' { $taskText -is [string] }
                default { -not [string]::IsNullOrWhiteSpace($taskText) }
            }
        }
        if (-not $taskOk) { $Problems.Add($Label+' lacks a valid '+$taskKey+' field.'); $taskValid=$false }
    }
    return $taskValid
}

function Read-BillIngredientCensus([string]$Text, $Problems) {
    $taskCensus=@{heldRice=0L;heldPotatoes=0L;floorRice=0L;floorPotatoes=0L;inventoryRice=0L;inventoryPotatoes=0L;handsRice=0L;handsPotatoes=0L;handStacks=0;ids=@{}}
    if([string]::IsNullOrEmpty($Text)) { return $taskCensus }
    foreach($taskItem in $Text.Split('|')) {
        if($taskItem -notmatch '^((RawRice|RawPotatoes)[1-9][0-9]*)/(RawRice|RawPotatoes)/([1-9][0-9]?)@(inventory|hands|\(-?[0-9]+, 0, -?[0-9]+\))$') {
            $Problems.Add('BG02 held receipt has an unreadable physical ingredient identity.'); return $null
        }
        $taskId=$Matches[1];$taskIdDef=$Matches[2];$taskDef=$Matches[3];$taskCount=[int]$Matches[4];$taskWhere=$Matches[5]
        if($taskWhere -notin @('inventory','hands') -and $null -eq (Read-BillCell $taskWhere $Problems)) { return $null }
        if($taskIdDef -ne $taskDef -or $taskCount -gt 20 -or $taskCensus.ids.ContainsKey($taskId)) {
            $Problems.Add('BG02 held receipt repeats an ingredient identity or contradicts its definition/count.'); return $null
        }
        $taskCensus.ids[$taskId]=$true
        $taskKey=$(if($taskWhere -in @('inventory','hands')){'held'}else{'floor'})+$(if($taskDef -eq 'RawRice'){'Rice'}else{'Potatoes'})
        $taskCensus[$taskKey]+=$taskCount
        if($taskWhere -in @('inventory','hands')) {
            $taskCensus[$taskWhere+$(if($taskDef -eq 'RawRice'){'Rice'}else{'Potatoes'})]+=$taskCount
        }
        if($taskWhere -eq 'hands') { $taskCensus.handStacks++ }
    }
    if($taskCensus.handStacks -gt 1) { $Problems.Add('BG02 physical receipt contains more than one actual hand stack.') }
    return $taskCensus
}

function Read-BillCell([string]$Text, $Problems) {
    $taskX=0;$taskZ=0
    if($Text -notmatch '^\((-?[0-9]+), 0, (-?[0-9]+)\)$' -or
        -not [int]::TryParse($Matches[1],[ref]$taskX) -or -not [int]::TryParse($Matches[2],[ref]$taskZ)) {
        $Problems.Add('BG02 spatial evidence lacks an actual integer map cell.'); return $null
    }
    return @{x=$taskX;z=$taskZ}
}

function Test-BulkRecipeAcquisition($Value, $Events, $FixtureEvent, $ProductEvent, $CleanupEvent, $Problems) {
    $taskRecipe=$Value.scenario
    # fixture-ready includes descriptive clauses as well as these keyed values.
    # Parse the known keys without pretending the prose clauses are key/value data.
    $taskFixture=@{}
    foreach($taskPart in $FixtureEvent.detail.Split(';')) {
        $taskPair=$taskPart.Trim().Split([char[]]@('='),2,[StringSplitOptions]::None)
        if($taskPair.Count -ne 2) { continue }
        if($taskFixture.ContainsKey($taskPair[0])) { $Problems.Add('BG02 fixture metadata repeats a field.'); continue }
        $taskFixture[$taskPair[0]]=$taskPair[1]
    }
    if(-not $FixtureEvent.detail.StartsWith('BG02; ') -or
        -not (Test-BillEventFields $taskFixture @{pawn='string';recipe='string';bill='string';stove='string';initialRice='string';initialPotatoes='string';expectedProduct='string';maximumTicks='positive-integer'} 'BG02 fixture metadata' $Problems)) { return }
    if($taskFixture['initialRice'] -ne $taskRecipe.initialRiceThingId+'/20' -or $taskFixture['initialPotatoes'] -ne $taskRecipe.initialPotatoThingId+'/20' -or
        $taskFixture['recipe'] -ne 'CookMealSimpleBulk' -or $taskFixture['expectedProduct'] -ne 'MealSimple/4' -or $taskFixture['maximumTicks'] -ne '12000') {
        $Problems.Add('BG02 initial ingredient identities/quantities or recipe disagree with the actual fixture event.')
    }
    $taskDrivers=@{DoBill='Verse.AI.JobDriver_DoBill';HaulersDream_BillPrepGather='HaulersDream.JobDriver_BillPrepGather';HaulersDream_GatherBillIngredients='HaulersDream.JobDriver_GatherBillIngredients'}
    $taskJobs=@{};$taskCurrentJob=$null;$taskGatherCount=0
    $taskHeld=@(0L,0L);$taskFloor=@(20L,20L);$taskAcquired=@(0L,0L);$taskProducts=0;$taskLastStateTick=-1
    $taskCustody=Read-BillIngredientCensus '' $Problems
    $taskRemoved=0;$taskCleaned=0;$taskPreviousCleaned=0
    foreach($taskEvent in $Events) {
        if($taskEvent.phase -eq 'executed-job') {
            $taskJob=Get-BillEventFields $taskEvent.detail
            if(-not (Test-BillEventFields $taskJob @{id='positive-integer';def='string';driver='string';workgiver='possibly-empty';forced='boolean';recipe='possibly-empty'} 'BG02 executed job' $Problems)) { continue }
            if($taskJobs.ContainsKey($taskJob['id'])) { $Problems.Add('BG02 repeats an executed job identity.'); continue }
            $taskJob['captureSequence']=$taskEvent.sequence;$taskJobs[$taskJob['id']]=$taskJob;$taskCurrentJob=$taskJob
            if($taskDrivers.ContainsKey($taskJob['def'])) {
                if($taskJob['driver'] -ne $taskDrivers[$taskJob['def']] -or $taskJob['recipe'] -ne 'CookMealSimpleBulk' -or
                    $taskJob['workgiver'] -ne 'DoBillsCook' -or $taskJob['forced'] -cne 'False' -or
                    $taskEvent.sequence -le $FixtureEvent.sequence -or $taskEvent.sequence -ge $ProductEvent.sequence) {
                    $Problems.Add('BG02 recipe/gather job lacks its actual automatic workgiver, driver, recipe or execution interval.')
                }
                if($taskJob['def'] -ne 'DoBill') { $taskGatherCount++ }
            } elseif($taskJob['recipe'] -eq 'CookMealSimpleBulk' -or $taskJob['def'] -match 'Batch') {
                $Problems.Add('BG02 ordinary recipe was routed through an unsupported or batch job.')
            }
            continue
        }
        if($taskEvent.phase -in @('native-product-created','native-ingredient-consumed','native-job-cleanup','seeded-filth-removal','seeded-cleaning-increment')) {
            $taskNativeEvent=Get-BillEventFields $taskEvent.detail
            if(-not (Test-BillEventFields $taskNativeEvent @{job='positive-integer'} 'BG02 actual native operation' $Problems) -or
                $null -eq $taskCurrentJob -or $taskCurrentJob['def'] -ne 'DoBill' -or $taskNativeEvent['job'] -ne $taskCurrentJob['id']) {
                $Problems.Add('BG02 native product, consumption, cleaning or cleanup is outside its actual current DoBill job.')
            }
            switch($taskEvent.phase) {
                'native-product-created' { $taskProducts++ }
                'native-job-cleanup' { $taskCurrentJob=$null }
                'seeded-filth-removal' { $taskRemoved++ }
                'seeded-cleaning-increment' { $taskCleaned++ }
            }
            continue
        }
        if($taskEvent.phase -eq 'settled-held-transfer') {
            $taskFields=Get-BillEventFields $taskEvent.detail
            if(-not (Test-BillEventFields $taskFields @{job='positive-integer';before='pair';after='pair';floorBefore='pair';floorAfter='pair';
                cumulativeAcquired='pair';ingredientDrop='boolean';storageDetour='boolean';acquisitionConserved='boolean';identities='possibly-empty'} 'BG02 actual held receipt' $Problems)) { continue }
            if($null -eq $taskCurrentJob -or $taskFields['job'] -ne $taskCurrentJob['id'] -or -not $taskDrivers.ContainsKey($taskCurrentJob['def']) -or
                $taskCurrentJob['captureSequence'] -ge $taskEvent.sequence -or $taskEvent.sequence -ge $CleanupEvent.sequence) {
                $Problems.Add('BG02 held receipt does not belong to its actual current automatic recipe/gather job.')
            }
            $taskCensus=Read-BillIngredientCensus $taskFields['identities'] $Problems
            if($null -ne $taskCensus) { $taskCustody=$taskCensus }
            $taskBefore=$taskFields['before'].Split(',');$taskAfter=$taskFields['after'].Split(',')
            $taskFloorBefore=$taskFields['floorBefore'].Split(',');$taskFloorAfter=$taskFields['floorAfter'].Split(',')
            if($null -ne $taskCensus -and ($taskCensus.heldRice -ne [int]$taskAfter[0] -or $taskCensus.heldPotatoes -ne [int]$taskAfter[1] -or
                $taskCensus.floorRice -ne [int]$taskFloorAfter[0] -or $taskCensus.floorPotatoes -ne [int]$taskFloorAfter[1])) {
                $Problems.Add('BG02 receipt held/floor totals contradict its actual per-Thing physical census.')
            }
            $taskDropped=$false
            for($taskIndex=0;$taskIndex -lt 2;$taskIndex++) {
                $taskDelta=[int]$taskAfter[$taskIndex]-[int]$taskBefore[$taskIndex]
                $taskGain=[Math]::Max(0,$taskDelta);$taskAcquired[$taskIndex]+=$taskGain
                if([int]$taskAfter[$taskIndex] -lt [int]$taskBefore[$taskIndex] -and [int]$taskFloorAfter[$taskIndex] -gt [int]$taskFloorBefore[$taskIndex]) { $taskDropped=$true }
                if($taskEvent.sequence -lt $ProductEvent.sequence) {
                    # SplitOff may already have removed the incoming stack before
                    # the narrow TryAdd prefix. Bound that reduction by the real
                    # held gain, then verify the completed physical destination.
                    if([int]$taskBefore[$taskIndex] -ne $taskHeld[$taskIndex] -or
                        [int]$taskFloorBefore[$taskIndex] -gt $taskFloor[$taskIndex] -or
                        [int]$taskFloorBefore[$taskIndex] -lt [Math]::Max(0,$taskFloor[$taskIndex]-$taskGain) -or
                        [int]$taskFloorAfter[$taskIndex] -ne $taskFloor[$taskIndex]-$taskDelta -or
                        [int]$taskAfter[$taskIndex]+[int]$taskFloorAfter[$taskIndex] -ne 20) {
                        $Problems.Add('BG02 completed held receipt cannot follow the preceding actual ingredient custody.')
                    }
                    $taskHeld[$taskIndex]=[long]$taskAfter[$taskIndex];$taskFloor[$taskIndex]=[long]$taskFloorAfter[$taskIndex]
                } elseif($taskGain -gt 0) { $Problems.Add('BG02 reacquires ingredients after its actual product operation.') }
            }
            if($taskFields['cumulativeAcquired'] -ne ($taskAcquired -join ',') -or $taskFields['ingredientDrop'] -cne [string]$taskDropped -or
                $taskFields['storageDetour'] -cne 'False' -or $taskFields['acquisitionConserved'] -cne 'True') {
                $Problems.Add('BG02 held receipt contradicts its actual acquisition/drop/conservation history.')
            }
            continue
        }
        if($taskEvent.phase -ne 'execution-state') { continue }
        $taskState=Get-BillEventFields $taskEvent.detail
        if(-not (Test-BillEventFields $taskState @{reason='string';job='string';acquired='pair';productCalls='integer'} 'BG02 execution counters' $Problems)) { continue }
        if($taskState['acquired'] -ne ($taskAcquired -join ',') -or [int]$taskState['productCalls'] -ne $taskProducts) {
            $Problems.Add('BG02 actual execution state contradicts prior pickup/product events.')
        }
        $taskExpectedJob=if($null -eq $taskCurrentJob){'/'}else{$taskCurrentJob['id']+'/'+$taskCurrentJob['def']}
        if($taskState['job'] -ne $taskExpectedJob) { $Problems.Add('BG02 execution state contradicts the actual current job identity.') }
        if($taskState['reason'] -notlike '*-settled') { continue }
        $taskLastStateTick=$taskEvent.tick
        if(-not (Test-BillEventFields $taskState @{inv='pair';carry='pair';floor='pair';cleaned='integer';seededFilthRemaining='integer'} 'BG02 settled custody and cleaning' $Problems)) { continue }
        if([int]$taskState['cleaned'] -lt $taskPreviousCleaned -or [int]$taskState['cleaned'] -lt $taskCleaned -or
            [int]$taskState['seededFilthRemaining'] -ne 3-$taskRemoved) {
            $Problems.Add('BG02 settled cleaning counters contradict the actual seeded cleaning/removal history.')
        }
        $taskPreviousCleaned=[int]$taskState['cleaned']
        if($taskEvent.sequence -lt $ProductEvent.sequence) {
            $taskInv=$taskState['inv'].Split(',');$taskHands=$taskState['carry'].Split(',');$taskStateFloor=$taskState['floor'].Split(',')
            if([int]$taskInv[0] -ne $taskCustody.inventoryRice -or [int]$taskInv[1] -ne $taskCustody.inventoryPotatoes -or
                [int]$taskHands[0] -ne $taskCustody.handsRice -or [int]$taskHands[1] -ne $taskCustody.handsPotatoes) {
                $Problems.Add('BG02 settled inventory and hand custody contradict the latest completed physical receipt.')
            }
            for($taskIndex=0;$taskIndex -lt 2;$taskIndex++) {
                if([int]$taskInv[$taskIndex]+[int]$taskHands[$taskIndex] -ne $taskHeld[$taskIndex] -or [int]$taskStateFloor[$taskIndex] -ne $taskFloor[$taskIndex]) {
                    $Problems.Add('BG02 settled ingredient custody contradicts completed physical receipts.')
                }
            }
        }
    }
    if($taskGatherCount -gt 1 -or $taskAcquired[0] -ne 20 -or $taskAcquired[1] -ne 20 -or
        $taskLastStateTick-$FixtureEvent.tick -ne $taskRecipe.elapsedTicks) {
        $Problems.Add('BG02 raw gather count, acquired units or elapsed interval contradict the scenario contract.')
    }
}

function Test-BulkRecipeCleaning($Value, $Events, $ProductEvent, $CleanupEvent, $Problems) {
    $taskProduct=Get-BillEventFields $ProductEvent.detail;$taskRemovals=@{};$taskIncrements=@{};$taskPreviousCapture=0
    $taskNativeStarts=@(foreach($taskEvent in @($Events | Where-Object phase -eq 'executed-job')) {
        $taskJob=Get-BillEventFields $taskEvent.detail
        if($taskJob['id'] -eq $taskProduct['job'] -and $taskJob['def'] -eq 'DoBill') { $taskEvent }
    })
    $taskRows=@($Events | Where-Object { $_.phase -in @('seeded-filth-removal','seeded-cleaning-increment') })
    if($taskRows.Count -ne 6) { $Problems.Add('BG02 needs three actual seeded filth removals and three cleaning increments.') }
    foreach($taskEvent in $taskRows) {
        $taskFields=Get-BillEventFields $taskEvent.detail
        $taskSchema=@{filth='string';job='positive-integer';sequence='positive-integer'}
        if($taskEvent.phase -eq 'seeded-filth-removal') { foreach($taskKey in @('destroyed','native','csToil','beforeProduct')) { $taskSchema[$taskKey]='boolean' } }
        else { $taskSchema['delta']='integer';$taskSchema['attributed']='boolean' }
        if(-not (Test-BillEventFields $taskFields $taskSchema 'BG02 cleaning event' $Problems)) { continue }
        if($taskFields['filth'] -notmatch '^Filth_Dirt([1-9][0-9]*)$') { $Problems.Add('BG02 cleaning event lacks its actual seeded filth identity.'); continue }
        $taskFilthNumber=$Matches[1]
        if(-not (Test-RequiredAssertion $Value.assertions ('fixture-one-layer-filth-'+$taskFilthNumber) $true) -or
            $taskFields['job'] -ne $taskProduct['job'] -or $taskEvent.sequence -ge $ProductEvent.sequence -or
            $taskNativeStarts.Count -ne 1 -or $taskEvent.sequence -le $taskNativeStarts[0].sequence -or
            [int]$taskFields['sequence'] -le $taskPreviousCapture -or [int]$taskFields['sequence'] -ge [int]$taskProduct['sequence']) {
            $Problems.Add('BG02 seeded cleaning is outside its native product job, initial filth set or capture order.')
        }
        $taskPreviousCapture=[int]$taskFields['sequence'];$taskId=$taskFields['filth']
        if($taskEvent.phase -eq 'seeded-filth-removal') {
            if($taskRemovals.ContainsKey($taskId) -or @('destroyed','native','csToil','beforeProduct' | Where-Object { $taskFields[$_] -cne 'True' }).Count -gt 0) {
                $Problems.Add('BG02 seeded removal is repeated or lacks actual native CS attribution.')
            }
            $taskRemovals[$taskId]=$taskEvent
        } else {
            if($taskIncrements.ContainsKey($taskId) -or -not $taskRemovals.ContainsKey($taskId) -or
                $taskRemovals[$taskId].tick -ne $taskEvent.tick -or $taskFields['delta'] -ne '1' -or $taskFields['attributed'] -cne 'True') {
                $Problems.Add('BG02 cleaning increment lacks its unique same-tick actual filth removal.')
            }
            $taskIncrements[$taskId]=$taskEvent
        }
    }
    if($taskRemovals.Count -ne 3 -or $taskIncrements.Count -ne 3 -or
        @($Value.assertions | Where-Object { $_.id -match '^fixture-one-layer-filth-[1-9][0-9]*$' -and $_.passed }).Count -ne 3) {
        $Problems.Add('BG02 raw cleaning does not account for all three initial one-layer filths.')
    }
}

function Test-BulkRecipeEvents($Value, $Events, [string]$RunId, $Problems) {
    $taskRecipe=$Value.scenario
    $taskPreviousSequence=0; $taskPreviousTick=-1
    foreach ($taskEvent in $Events) {
        if (-not (Test-EvidenceFields $taskEvent @{sequence='integer'; runId='string'; caseId='string'; tick='integer'; phase='string'; detail='string'} 'BG02 event' $Problems)) { return }
        if ($taskEvent.runId -ne $RunId -or $taskEvent.caseId -ne 'BG02' -or $taskEvent.sequence -ne $taskPreviousSequence+1 -or
            $taskEvent.tick -lt $taskPreviousTick) { $Problems.Add('BG02 event identity, sequence or time is inconsistent.') }
        $taskPreviousSequence=$taskEvent.sequence; $taskPreviousTick=$taskEvent.tick
    }
    $taskProducts=@($Events | Where-Object phase -eq 'native-product-created')
    $taskCleanups=@($Events | Where-Object phase -eq 'native-job-cleanup')
    $taskConsumption=@($Events | Where-Object phase -eq 'native-ingredient-consumed')
    if ($taskProducts.Count -ne 1 -or $taskCleanups.Count -ne 1 -or $taskConsumption.Count -ne $taskRecipe.ingredientConsumptionEvents.Count -or
        $taskConsumption.Count -lt 2) { $Problems.Add('BG02 lacks the exact product, cleanup and ingredient-consumption events.'); return }
    $taskProductEvent=$taskProducts[0]; $taskCleanupEvent=$taskCleanups[0]
    $taskProduct=Get-BillEventFields $taskProductEvent.detail; $taskCleanup=Get-BillEventFields $taskCleanupEvent.detail
    if (-not (Test-BillEventFields $taskProduct @{thing='string';count='positive-integer';job='positive-integer';calls='positive-integer';
        totalUnits='positive-integer';sequence='positive-integer';seededCleaningComplete='boolean';case='string';recipe='string';expectedStackCount='positive-integer'} 'BG02 product event' $Problems)) { return }
    if (-not (Test-BillEventFields $taskCleanup @{job='positive-integer';condition='string';completedCleanup='boolean';observations='positive-integer'} 'BG02 cleanup event' $Problems)) { return }
    $taskJobId=$taskProduct['job']
    if ($taskJobId -notmatch '^[0-9]+$' -or $taskProduct['thing'] -notmatch '^MealSimple[0-9]+$' -or
        $taskProduct['count'] -ne '4' -or $taskProduct['calls'] -ne '1' -or $taskProduct['totalUnits'] -ne '4' -or
        $taskProduct['seededCleaningComplete'] -ne 'True' -or $taskProduct['case'] -ne 'BG02' -or
        $taskProduct['recipe'] -ne 'CookMealSimpleBulk' -or $taskProduct['expectedStackCount'] -ne '4' -or
        $taskCleanup['job'] -ne $taskJobId -or $taskCleanup['condition'] -ne 'Succeeded' -or
        $taskCleanup['completedCleanup'] -ne 'True' -or $taskCleanup['observations'] -ne '1' -or
        $taskCleanupEvent.sequence -le $taskProductEvent.sequence) { $Problems.Add('BG02 actual product and cleanup are not the same successful ordinary job.') }
    $taskNativeJobs=@()
    foreach ($taskEvent in @($Events | Where-Object phase -eq 'executed-job')) {
        $taskJob=Get-BillEventFields $taskEvent.detail
        if ($taskJob['def'] -eq 'DoBill') { $taskNativeJobs+=@{event=$taskEvent; fields=$taskJob} }
    }
    if ($taskNativeJobs.Count -ne 1) { $Problems.Add('BG02 does not have exactly one executed native DoBill job.') }
    else {
        $taskNative=$taskNativeJobs[0]
        if ($taskNative.fields['id'] -ne $taskJobId -or $taskNative.fields['driver'] -ne 'Verse.AI.JobDriver_DoBill' -or
            $taskNative.fields['forced'] -ne 'False' -or $taskNative.fields['recipe'] -ne 'CookMealSimpleBulk' -or
            $taskNative.event.sequence -ge $taskProductEvent.sequence) { $Problems.Add('BG02 native job execution does not match its product attribution.') }
    }
    $taskTotals=@{RawRice=0L; RawPotatoes=0L}; $taskSeenConsumption=@{}; $taskPreviousCapture=[int]$taskProduct['sequence']
    foreach ($taskEvent in $taskConsumption) {
        $taskFields=Get-BillEventFields $taskEvent.detail
        if (-not (Test-BillEventFields $taskFields @{case='string';recipe='string';thing='string';def='string';before='positive-integer';
            remaining='integer';consumed='positive-integer';destroyed='boolean';job='positive-integer';sequence='positive-integer';attributed='boolean';totalConsumed='pair'} 'BG02 consumption event' $Problems)) { continue }
        $taskRecords=@($taskRecipe.ingredientConsumptionEvents | Where-Object thingId -eq $taskFields['thing'])
        if ($taskRecords.Count -ne 1 -or $taskSeenConsumption.ContainsKey($taskFields['thing'])) { $Problems.Add('BG02 consumption event is absent or repeated in the result.'); continue }
        $taskSeenConsumption[$taskFields['thing']]=$true; $taskRecord=$taskRecords[0]
        $taskExpected=@{case='BG02'; recipe='CookMealSimpleBulk'; thing=$taskRecord.thingId; def=$taskRecord.defName;
            before=[string]$taskRecord.beforeCount; remaining=[string]$taskRecord.remainingCount; consumed=[string]$taskRecord.unitsConsumed;
            destroyed=[string]$taskRecord.destroyed; job=[string]$taskRecord.jobId; sequence=[string]$taskRecord.sequence; attributed=[string]$taskRecord.attributed}
        foreach ($taskKey in $taskExpected.Keys) {
            if ($taskFields[$taskKey] -cne $taskExpected[$taskKey]) { $Problems.Add('BG02 consumption event/result mismatch: '+$taskKey) }
        }
        if ($taskRecord.jobId.ToString() -ne $taskJobId -or $taskEvent.tick -ne $taskRecord.tick -or
            $taskEvent.sequence -le $taskProductEvent.sequence -or $taskEvent.sequence -ge $taskCleanupEvent.sequence -or
            $taskRecord.sequence -le $taskPreviousCapture) { $Problems.Add('BG02 consumption is outside its actual product-to-cleanup boundary or capture order.') }
        $taskPreviousCapture=$taskRecord.sequence
        if ($taskTotals.ContainsKey($taskRecord.defName)) { $taskTotals[$taskRecord.defName]+=$taskRecord.unitsConsumed }
        if ($taskFields['totalConsumed'] -ne ($taskTotals.RawRice.ToString()+','+$taskTotals.RawPotatoes.ToString())) {
            $Problems.Add('BG02 event cumulative consumption differs from its unique actual records.')
        }
    }
    # Material conservation is checked only at enclosing settled boundaries.
    # GenRecipe produces its Thing before native ingredient destruction inside that operation.
    $taskStableTicks=@{}; $taskStableStart=-1; $taskLastStable=-1; $taskStateCount=0
    $taskPriorSettledTick=-1; $taskCountedStableTicks=0
    $taskPreProductTicks=@{}; $taskInitialState=$false; $taskFullInventorySequences=@(); $taskExcursionObserved=$false
    $taskFixtureEvents=@($Events | Where-Object phase -eq 'fixture-ready')
    $taskReturnEvents=@($Events | Where-Object phase -eq 'first-ingredient-return')
    if ($taskFixtureEvents.Count -ne 1 -or $taskReturnEvents.Count -ne 1) { $Problems.Add('BG02 lacks its unique actual fixture/first-return boundaries.'); return }
    $taskFixtureEvent=$taskFixtureEvents[0]; $taskReturnEvent=$taskReturnEvents[0]
    $taskLayoutAssertions=@($Value.assertions | Where-Object id -eq 'fixture-sources-away-from-bench')
    if($taskLayoutAssertions.Count -ne 1 -or -not $taskLayoutAssertions[0].passed) {
        $Problems.Add('BG02 lacks its unique actual bench/source layout.'); return
    }
    $taskLayout=Get-BillEventFields $taskLayoutAssertions[0].observed
    if(-not (Test-BillEventFields $taskLayout @{bench='cell';rice='cell';potato='cell'} 'BG02 fixture layout' $Problems)) { return }
    $taskBench=Read-BillCell $taskLayout['bench'] $Problems
    if($null -eq $taskBench) { return }
    $taskHadAwayCargo=$false;$taskPhysicalSweep=$false;$taskPhysicalReturn=$false;$taskPhysicalExcursion=$false
    $taskReturn=Get-BillEventFields $taskReturnEvent.detail
    if (-not (Test-BillEventFields $taskReturn @{tick='integer';fullInventorySweep='boolean'} 'BG02 first return' $Problems)) { return }
    if ([int]$taskReturn['tick'] -ne $taskReturnEvent.tick -or $taskReturnEvent.sequence -ge $taskProductEvent.sequence -or
        $taskReturnEvent.sequence -le $taskFixtureEvent.sequence -or
        ($taskReturn['fullInventorySweep'] -ceq 'True') -ne ($taskRecipe.expectedBehavior -eq 'satisfied')) {
        $Problems.Add('BG02 first return differs from the configured physical sweep outcome.')
    }
    foreach ($taskEvent in @($Events | Where-Object phase -eq 'execution-state')) {
        $taskFields=Get-BillEventFields $taskEvent.detail; $taskStateCount++
        if ($taskFields['reason'] -notlike '*-settled') { continue }
        if (-not (Test-BillEventFields $taskFields @{reason='string';inv='pair';carry='pair';floor='pair';products='integer';
            stableSince='integer';billRepeatRemaining='integer';fullInventorySweep='boolean';secondExcursion='boolean';acquired='pair';productCalls='integer';position='cell'} 'BG02 settled state' $Problems)) { continue }
        # The fixture's first stable boundary may share a tick with an earlier
        # pre-cleanup boundary. Its counter counts only newly observed ticks.
        $taskNewSettledTick=$taskEvent.tick -ne $taskPriorSettledTick
        $taskPriorSettledTick=$taskEvent.tick
        $taskInventory=$taskFields['inv']; $taskHands=$taskFields['carry']; $taskFloor=$taskFields['floor']
        if ($taskInventory -notmatch '^[0-9]+,[0-9]+$' -or $taskHands -notmatch '^[0-9]+,[0-9]+$' -or $taskFloor -notmatch '^[0-9]+,[0-9]+$') {
            $Problems.Add('BG02 settled state lacks typed ingredient totals.'); continue
        }
        $taskRice=[long]$taskInventory.Split(',')[0]+[long]$taskHands.Split(',')[0]+[long]$taskFloor.Split(',')[0]
        $taskPotatoes=[long]$taskInventory.Split(',')[1]+[long]$taskHands.Split(',')[1]+[long]$taskFloor.Split(',')[1]
        if ($taskEvent.sequence -lt $taskProductEvent.sequence) {
            if ($taskRice -ne 20 -or $taskPotatoes -ne 20 -or $taskFields['products'] -ne '0') { $Problems.Add('BG02 loses or duplicates ingredients before the actual recipe operation.') }
            $taskPosition=Read-BillCell $taskFields['position'] $Problems
            if($null -ne $taskPosition) {
                $taskDx=[double]$taskPosition.x-$taskBench.x;$taskDz=[double]$taskPosition.z-$taskBench.z
                $taskNearBench=$taskDx*$taskDx+$taskDz*$taskDz -le 9.0
                $taskHasCargo=$taskInventory -ne '0,0' -or $taskHands -ne '0,0'
                if($taskHasCargo -and -not $taskNearBench) { $taskHadAwayCargo=$true }
                if(-not $taskPhysicalReturn -and -not $taskNearBench -and $taskInventory -eq '20,20') { $taskPhysicalSweep=$true }
                if(-not $taskPhysicalReturn -and $taskHadAwayCargo -and $taskNearBench -and $taskHasCargo) {
                    $taskPhysicalReturn=$true
                    if($taskEvent.tick -ne $taskReturnEvent.tick -or $taskEvent.sequence -le $taskReturnEvent.sequence) {
                        $Problems.Add('BG02 first-return event differs from the first actual settled arrival with ingredients.')
                    }
                }
                if($taskPhysicalReturn -and -not $taskNearBench) { $taskPhysicalExcursion=$true }
                if(($taskFields['fullInventorySweep'] -ceq 'True') -ne $taskPhysicalSweep -or
                    ($taskFields['secondExcursion'] -ceq 'True') -ne $taskPhysicalExcursion) {
                    $Problems.Add('BG02 claimed sweep/excursion contradicts actual settled position and inventory history.')
                }
            }
            if ($taskEvent.sequence -gt $taskFixtureEvent.sequence) {
                $taskPreProductTicks[$taskEvent.tick]=$true
                if ($taskInventory -eq '0,0' -and $taskHands -eq '0,0' -and $taskFloor -eq '20,20') { $taskInitialState=$true }
                if ($taskInventory -eq '20,20' -and $taskEvent.sequence -lt $taskReturnEvent.sequence) { $taskFullInventorySequences+=$taskEvent.sequence }
                if ($taskFields['secondExcursion'] -ceq 'True') { $taskExcursionObserved=$true }
            }
            continue
        }
        if ($taskRice -ne 0 -or $taskPotatoes -ne 0 -or $taskFields['products'] -ne '4' -or $taskFields['billRepeatRemaining'] -ne '0') {
            $Problems.Add('BG02 settled state does not conserve the completed four-meal recipe.'); continue
        }
        if ($taskEvent.sequence -gt $taskCleanupEvent.sequence -and $taskFields['stableSince'] -match '^[0-9]+$') {
            if ($taskStableStart -lt 0) {
                $taskStableStart=$taskEvent.tick
                if ($taskStableStart -lt $taskCleanupEvent.tick) { $Problems.Add('BG02 stable follow-up begins before actual native cleanup.') }
            }
            if ([int]$taskFields['stableSince'] -ne $taskStableStart -or $taskEvent.tick -lt $taskStableStart -or
                ($taskLastStable -ge 0 -and $taskEvent.tick -gt $taskLastStable+1)) { $Problems.Add('BG02 stable follow-up has a discontinuity or inconsistent start.') }
            $taskStableTicks[$taskEvent.tick]=$true; $taskLastStable=$taskEvent.tick
            if ($taskNewSettledTick) { $taskCountedStableTicks++ }
        }
    }
    if (-not $taskInitialState -or $taskProductEvent.tick -le $taskFixtureEvent.tick) { $Problems.Add('BG02 lacks its initial physical 20+20 ingredient state.') }
    if(-not $taskHadAwayCargo -or -not $taskPhysicalReturn -or
        $taskPhysicalSweep -ne ($taskRecipe.expectedBehavior -eq 'satisfied') -or
        $taskPhysicalExcursion -ne ($taskRecipe.expectedBehavior -eq 'baseline-gap')) {
        $Problems.Add('BG02 lacks the actual departure, inventory sweep, return or subsequent excursion required by its expectation.')
    }
    for ($taskTick=$taskFixtureEvent.tick+1; $taskTick -lt $taskProductEvent.tick; $taskTick++) {
        if (-not $taskPreProductTicks.ContainsKey($taskTick)) { $Problems.Add('BG02 pre-product settled tick is missing: '+$taskTick); break }
    }
    if (($taskFullInventorySequences.Count -gt 0) -ne ($taskRecipe.expectedBehavior -eq 'satisfied') -or
        $taskExcursionObserved -ne ($taskRecipe.expectedBehavior -eq 'baseline-gap')) {
        $Problems.Add('BG02 actual inventory/second-excursion observations disagree with its expected first-return behavior.')
    }
    $taskAcquiredRice=0; $taskAcquiredPotatoes=0; $taskAcquisitionEvents=0
    foreach ($taskEvent in @($Events | Where-Object phase -eq 'settled-held-transfer')) {
        $taskFields=Get-BillEventFields $taskEvent.detail
        if (-not (Test-BillEventFields $taskFields @{before='pair';after='pair';cumulativeAcquired='pair';floorBefore='pair';floorAfter='pair';
            ingredientDrop='boolean';storageDetour='boolean';identities='possibly-empty'} 'BG02 held transfer' $Problems)) { continue }
        $taskBefore=$taskFields['before'].Split(','); $taskAfter=$taskFields['after'].Split(',')
        $taskRiceDelta=[Math]::Max(0,[int]$taskAfter[0]-[int]$taskBefore[0]); $taskPotatoDelta=[Math]::Max(0,[int]$taskAfter[1]-[int]$taskBefore[1])
        $taskAcquiredRice+=$taskRiceDelta; $taskAcquiredPotatoes+=$taskPotatoDelta
        if ($taskRiceDelta+$taskPotatoDelta -gt 0) {
            $taskAcquisitionEvents++
            if ($taskEvent.sequence -ge $taskProductEvent.sequence -or $taskEvent.sequence -le $taskFixtureEvent.sequence) { $Problems.Add('BG02 acquisition is outside its real ingredient-gathering interval.') }
        }
        if ($taskFields['cumulativeAcquired'] -ne ($taskAcquiredRice.ToString()+','+$taskAcquiredPotatoes.ToString()) -or
            $taskFields['storageDetour'] -cne 'False') { $Problems.Add('BG02 held-transfer history contradicts its acquisition/storage-detour totals.') }
    }
    if ($taskAcquisitionEvents -lt 2 -or $taskAcquiredRice -ne 20 -or $taskAcquiredPotatoes -ne 20) {
        $Problems.Add('BG02 lacks actual net pickups of exactly 20 rice and 20 potatoes.')
    }
    if ($taskStateCount -ne $taskRecipe.executionSnapshots -or $taskCountedStableTicks -ne $taskRecipe.stablePostProductTicks -or
        $taskStableTicks.Count -lt 300 -or $taskLastStable-$taskStableStart -lt 300) {
        $Problems.Add('BG02 does not retain its complete declared stable follow-up in actual state events.')
    }
    Test-BulkRecipeAcquisition $Value $Events $taskFixtureEvent $taskProductEvent $taskCleanupEvent $Problems
    Test-BulkRecipeCleaning $Value $Events $taskProductEvent $taskCleanupEvent $Problems
}
