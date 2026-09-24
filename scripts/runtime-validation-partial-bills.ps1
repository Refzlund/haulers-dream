# BG03-P1 evidence consumer. Windows PowerShell 5.1 and PowerShell 7.
# Only this file's PB-prefixed implementation is used. No input, file, or game mutation.
function Get-PBSchema([string]$Kind) {
    switch($Kind) {
        'thing' { return @{id='int';def='string';count='int';custody='string';holderType='nullable-string';spawned='bool';destroyed='bool';x='int';z='int';mapId='nullable-int'} }
        'quantity' { return @{def='string';initialHeld='int';initialFloor='int';inventory='int';hands='int';floor='int';uniqueAcquired='int';reenteredHeld='int';consumed='int'} }
        'selection' { return @{ordinal='int';thing='nullable-object';selected='nullable-int';tagged='bool';units='array'} }
        'job' { return @{id='nullable-int';def='nullable-string';bill='nullable-string';recipe='nullable-string';workGiver='nullable-string';workGiverClass='nullable-string';playerForced='bool';queueLengthsMatch='bool';targetA='nullable-int';selection='array'} }
        'state' { return @{sequence='int';tick='int';reason='string';jobId='nullable-int';jobDef='nullable-string';driver='nullable-string';x='int';z='int';benchDistance='float';cleanedTotal='float';meals='int';billRemaining='int';things='array';quantities='array'} }
        'origin' { return @{thingId='int';def='string';count='int';initiallyHeld='bool';units='array'} }
        'candidate' { return @{id='int';tick='int';forcedArgument='bool';workGiverClass='string';nativeSequence='int';routedSequence='int';native='nullable-object';routed='nullable-object'} }
        'start' { return @{sequence='int';tick='int';candidateId='nullable-int';previous='nullable-object';requested='nullable-object';previousDriver='nullable-string';previousToilInit='nullable-string';previousExecutedGatherId='nullable-int';lastJobEndCondition='nullable-string';actualSequence='int';actual='nullable-object';actualDriver='nullable-string';actualDriverAssembly='nullable-string';actualDriverMvid='nullable-string';actualCustody='nullable-object'} }
        'ancestry' { return @{sequence='int';tick='int';operation='string';method='string';sourceBefore='nullable-object';targetBefore='nullable-object';sourceAfter='nullable-object';targetAfter='nullable-object';requested='int';nativeResult='nullable-bool';valid='bool';movedUnits='array'} }
        'transfer' { return @{sequence='int';tick='int';boundary='string';before='object';after='object';newlyAcquiredUnits='array';reenteredUnits='array';leftHeldUnits='array'} }
        'consume' { return @{sequence='int';tick='int';jobId='int';before='nullable-object';after='nullable-object';attributed='bool';units='array'} }
        'clean' { return @{sequence='int';tick='int';operation='string';filthId='int';jobId='int';native='bool';toilTickMethod='nullable-string';destroyed='bool';recordDelta='float';attributed='bool'} }
        'end' { return @{sequence='int';tick='int';jobId='int';jobDef='string';condition='string';released='bool'} }
        'product' { return @{sequence='int';tick='int';jobId='int';thingId='nullable-int';count='int';native='bool';cleaningComplete='bool'} }
        'assertion' { return @{id='string';category='string';passed='bool';detail='string'} }
        'fresh' { return @{origin='string';tick='int';milkId='int';previousYieldTick='int';notifiedYieldTick='int';notificationBinding='string'} }
        'gate' { return @{sequence='int';tick='int';boundary='string';workCall='nullable-int';emergency='nullable-bool';returnedBoolean='nullable-bool';candidateId='nullable-int';workResultValid='nullable-bool';workResultJob='nullable-object';lastYieldTick='int';graceTicks='int';beforeEating='bool';beforeSleep='bool';beforeLeisure='bool';rawUnloadEverything='bool';currentJob='nullable-object';currentDriver='nullable-string';queue='array';queueCount='int';queueOverflow='bool';foodRawUnits='nullable-float';restRawUnits='nullable-float';joyRawUnits='nullable-float';fullTimetable='array';initialMilk='object';initialMilkTagged='bool'} }
        'out' { return @{tick='int';initialThingId='int';transferredThingId='nullable-int';transferredCount='int';sourceRemaining='int';nativeMoved='int';transferValid='bool';nativeDropped='bool';droppedThingId='nullable-int';droppedCount='int';dropValid='bool';cleaned='bool'} }
    }
    return @{}
}
function Test-PBFields($Value,[hashtable]$Schema,[string]$Label,$Problems) {
    if($null-eq$Value -or $Value-isnot[pscustomobject]){$Problems.Add('BG03 '+$Label+' must be an object.');return $false}
    $okay=$true
    foreach($name in $Schema.Keys){
        $property=$Value.PSObject.Properties[$name]
        if($null-eq$property){$Problems.Add('BG03 '+$Label+' lacks '+$name+'.');$okay=$false;continue}
        $item=$property.Value;$type=$Schema[$name]
        if($type.StartsWith('nullable-')){if($null-eq$item){continue};$type=$type.Substring(9)}
        $valid=switch($type){
            'int' {($item-is[int] -or $item-is[long]) -and $item-ge[int]::MinValue -and $item-le[int]::MaxValue}
            'float' {($item-is[int] -or $item-is[long] -or $item-is[single] -or $item-is[double] -or $item-is[decimal]) -and -not[double]::IsNaN([double]$item) -and -not[double]::IsInfinity([double]$item) -and [math]::Abs([double]$item)-le[single]::MaxValue}
            'bool' {$item-is[bool]}
            'string' {$item-is[string]}
            'array' {$item-is[array]}
            'object' {$null-ne$item -and $item-is[pscustomobject]}
            'utc' {$date=[datetimeoffset]::MinValue;($item-is[datetime] -and $item.Kind-eq[DateTimeKind]::Utc) -or ($item-is[datetimeoffset] -and $item.Offset-eq[timespan]::Zero) -or ($item-is[string] -and $item-match'^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$' -and [datetimeoffset]::TryParse($item,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::None,[ref]$date))}
            default {$false}
        }
        if(-not$valid){$Problems.Add('BG03 '+$Label+'.'+$name+' is not '+$Schema[$name]+'.');$okay=$false}
    }
    return $okay
}
function Test-PBRowShape($Value,[string]$Kind,$Problems) {
    if(-not(Test-PBFields $Value (Get-PBSchema $Kind) $Kind $Problems)){return}
    switch($Kind){
        'gate' {Test-PBRowShape $Value.initialMilk thing $Problems;foreach($n in @('currentJob','workResultJob')){if($null-ne$Value.$n){Test-PBRowShape $Value.$n job $Problems}};foreach($j in $Value.queue){if($null-ne$j){Test-PBRowShape $j job $Problems}};foreach($slot in $Value.fullTimetable){if($null-ne$slot -and $slot-isnot[string]){$Problems.Add('BG03 timetable entry is not a nullable def name.')}}}
        'state' {foreach($x in $Value.things){Test-PBRowShape $x thing $Problems};foreach($x in $Value.quantities){Test-PBRowShape $x quantity $Problems}}
        'job' {foreach($x in $Value.selection){Test-PBRowShape $x selection $Problems}}
        'selection' {if($null-ne$Value.thing){Test-PBRowShape $Value.thing thing $Problems}}
        'candidate' {foreach($n in @('native','routed')){if($null-ne$Value.$n){Test-PBRowShape $Value.$n job $Problems}}}
        'start' {foreach($n in @('previous','requested','actual')){if($null-ne$Value.$n){Test-PBRowShape $Value.$n job $Problems}};if($null-ne$Value.actualCustody){Test-PBRowShape $Value.actualCustody state $Problems}}
        'ancestry' {foreach($n in @('sourceBefore','targetBefore','sourceAfter','targetAfter')){if($null-ne$Value.$n){Test-PBRowShape $Value.$n thing $Problems}}}
        'transfer' {Test-PBRowShape $Value.before state $Problems;Test-PBRowShape $Value.after state $Problems}
        'consume' {foreach($n in @('before','after')){if($null-ne$Value.$n){Test-PBRowShape $Value.$n thing $Problems}}}
    }
    foreach($name in @('units','movedUnits','newlyAcquiredUnits','reenteredUnits','leftHeldUnits')){
        if($null-ne$Value.PSObject.Properties[$name]){foreach($id in $Value.$name){if(($id-isnot[int] -and $id-isnot[long]) -or $id-lt0 -or $id-ge40){$Problems.Add('BG03 unit identity must be an integer in [0,39].')}}}
    }
}
function Test-PBShapeCore($Value,$Problems) {
    $before=$Problems.Count
    if(-not(Test-PBFields $Value @{schemaVersion='int';runId='string';caseId='string';processId='int';status='string';detail='string';startedUtc='utc';finishedUtc='utc';unityErrorsObserved='int';assertions='array';mods='array';assemblies='array';installedVersionFile='string';executingGameVersion='string';negativeControl='nullable-object';partialInventoryBill='object'} 'run' $Problems)){return $false}
    foreach($a in $Value.assertions){Test-PBFields $a @{id='string';passed='bool';observed='string'} 'global assertion' $Problems|Out-Null}
    foreach($a in $Value.assemblies){Test-PBFields $a @{name='string';path='string';sha256='string';assemblyVersion='string';moduleVersionId='string'} 'assembly' $Problems|Out-Null}
    foreach($a in $Value.mods){Test-PBFields $a @{packageId='string';rootPath='string'} 'mod' $Problems|Out-Null}
    $p=$Value.partialInventoryBill
    if(-not(Test-PBFields $p @{caseId='string';recipe='string';expectedBehavior='string';status='string';fixtureValid='bool';requestedBehaviorSatisfied='bool';expectationMatched='bool';timedOut='bool';completedNativeRecipe='bool';fullInventoryBeforeReturn='bool';changedProvenanceComplete='bool';baselineProvenanceComplete='bool';startedTick='int';finishedTick='int';maximumTicks='int';departureTick='nullable-int';firstReturnTick='nullable-int';stableSinceTick='nullable-int';stableTickCount='int';stateEvents='int';gatherJobs='int';nativeJobs='int';initial='object';departure='nullable-object';completeInventory='nullable-object';firstReturn='nullable-object';final='object';origins='array';observerOutControl='object';freshCargo='object';initialWorkGateObserved='bool';unloadGates='array';candidates='array';starts='array';ancestry='array';transfers='array';consumption='array';cleaning='array';ends='array';products='array';assertions='array'} 'result' $Problems)){return $false}
    foreach($n in @('initial','departure','completeInventory','firstReturn','final')){if($null-ne$p.$n){Test-PBRowShape $p.$n state $Problems}}
    Test-PBRowShape $p.observerOutControl out $Problems
    Test-PBRowShape $p.freshCargo fresh $Problems
    foreach($g in $p.unloadGates){Test-PBRowShape $g gate $Problems}
    $kinds=@{origins='origin';candidates='candidate';starts='start';ancestry='ancestry';transfers='transfer';consumption='consume';cleaning='clean';ends='end';products='product';assertions='assertion'}
    foreach($n in $kinds.Keys){foreach($row in $p.$n){Test-PBRowShape $row $kinds[$n] $Problems}}
    return $Problems.Count-eq$before
}
function Get-PBCanonical($Value) {
    if($null-eq$Value){return 'null'}
    if($Value-is[array]){return '['+(@(foreach($x in $Value){Get-PBCanonical $x})-join',')+']'}
    if($Value-is[pscustomobject]){return '{'+(@(foreach($n in @($Value.PSObject.Properties.Name|Sort-Object -CaseSensitive)){(ConvertTo-Json -InputObject $n -Compress)+':'+(Get-PBCanonical $Value.$n)})-join',')+'}'}
    return ConvertTo-Json -InputObject $Value -Depth 100 -Compress
}
function Test-PBSame($A,$B){return (Get-PBCanonical $A)-ceq(Get-PBCanonical $B)}
function Get-PBOne($Rows,[string]$Field,$Key,$Problems) {
    $matches=@($Rows|Where-Object {$_.PSObject.Properties[$Field].Value -ceq $Key})
    if($matches.Count-ne1){$Problems.Add('BG03 requires one '+$Field+'='+$Key+'.');return $null};return $matches[0]
}
function Get-PBExpected {return @{Milk=12;RawRice=14;RawPotatoes=14}}
function Test-PBIds($Actual,$Expected){return (@($Actual)-join',')-ceq(@($Expected)-join',')}
function Test-PBSelection($Job,$Origins,$Problems,[switch]$InitialMilk) {
    if($null-eq$Job){$Problems.Add('BG03 required selected job is absent.');return}
    $defs=@{};foreach($o in $Origins){foreach($u in $o.units){$defs[[int]$u]=$o.def}}
    $seen=@{};$ordinal=0
    if(-not$Job.queueLengthsMatch -or $Job.recipe-cne'CookMealSimpleBulk' -or $Job.playerForced -or $Job.id-le0 -or $Job.targetA-le0 -or [string]::IsNullOrWhiteSpace($Job.bill)){$Problems.Add('BG03 selected job context is incomplete/forced.')}
    foreach($row in $Job.selection){
        if($null-eq$row.thing -or $null-eq$row.selected){$Problems.Add('BG03 selection has an absent Thing/count.');continue}
        if($row.ordinal-ne$ordinal -or $row.selected-le0 -or $row.selected-gt$row.thing.count -or $row.units.Count-ne$row.selected -or $row.thing.destroyed){$Problems.Add('BG03 selected row count/ordinal/custody is invalid.')};$ordinal++
        foreach($u in $row.units){if($seen.ContainsKey([int]$u) -or -not$defs.ContainsKey([int]$u) -or $defs[[int]$u]-cne$row.thing.def){$Problems.Add('BG03 selected origin unit is duplicated/unknown/wrong def.')};$seen[[int]$u]=$true}
    }
    if($seen.Count-ne40){$Problems.Add('BG03 selection omits origin units.')}
    if($InitialMilk){$milk=@($Origins|Where-Object def -CEQ Milk);if($milk.Count-eq1){$rows=@($Job.selection|Where-Object {$null-ne$_.thing -and $_.thing.id-eq$milk[0].thingId});if($rows.Count-ne1 -or $rows[0].selected-ne12 -or -not$rows[0].tagged -or $rows[0].thing.custody-cne'inventory' -or -not(Test-PBIds $rows[0].units $milk[0].units)){$Problems.Add('BG03 original inventory milk12 is not the actual tagged selection.')}}}
}
function Test-PBSelectionSame($A,$B,$Problems) {
    if($null-eq$A -or $null-eq$B){$Problems.Add('BG03 selection continuity has an absent job.');return}
    if($A.bill-cne$B.bill -or $A.recipe-cne$B.recipe -or $A.targetA-ne$B.targetA -or $A.selection.Count-ne$B.selection.Count){$Problems.Add('BG03 selection/bill context changed.');return}
    for($i=0;$i-lt$A.selection.Count;$i++){$x=$A.selection[$i];$y=$B.selection[$i];if($null-eq$x.thing -or $null-eq$y.thing -or $x.thing.id-ne$y.thing.id -or $x.selected-ne$y.selected -or -not(Test-PBIds $x.units $y.units)){$Problems.Add('BG03 retained selection identity/count order changed.')}}
}
function Test-PBAllInventory($State) {
    if($null-eq$State -or $State.quantities.Count-ne3){return $false}
    foreach($q in $State.quantities){if($q.hands-ne0 -or $q.floor-ne0 -or $q.inventory-ne($q.initialHeld+$q.initialFloor)){return $false}}
    return $true
}
function Get-PBGlobalCatalog($Value,$Problems) {
    $catalog=@{}
    foreach($id in @('private-runtime-data-path','private-save-data-path','private-mod-directory','private-player-log','case-supported','negative-control-supported','installed-version-file-matches-manifest','harness-compiled-against-running-game','exact-active-mod-count','real-map-initialized','real-game-ticks-advanced','no-unity-errors-after-harness-start','fixture-bg03p1-setup-completed')){$catalog[$id]=1}
    foreach($id in @('bg03p1-real-map','known-expectation','hd-settings','default-cs-patching-mode','cs-patch-settings-assembly','hd-route-mod-assembly','bg03p1-required-prefix-JobDriver_DoBill_MakeNewToils_CommonSensePatch','bg03p1-required-postfix-Patch_WorkGiver_DoBill_InventoryRoute','room-in-bounds','bg03p1-no-existing-room-zones','roof-support-distance','fueled-stove-comp','bench-gather-comp','nutrition','ordinary-recipe','bg03p1-native-nutrition-recipe','bg03p1-bill-batch-disabled','capable-human','bg03p1-initial-milk-owned','bg03p1-tag-component','bg03p1-tag-bindings','bg03p1-initial-tag','bg03p1-three-native-nutrition-defs','bg03p1-native-identity-operations','bg03p1-native-product-shape','only-actor','exact-starting-food','sources-away-from-bench','bg03p1-initial-held-accounting','bg03p1-initial-custody','bg03p1-observers-installed','bg03p1-native-out-observer-control')){$catalog['fixture-'+$id]=1}
    foreach($id in @('passive-field-lastYieldTick','passive-field-unloadGraceTicks','passive-field-unloadBeforeEating','passive-field-unloadBeforeSleep','passive-field-unloadBeforeLeisure','passive-field-unloadEverything','raw-need-binding','fresh-pickup-binding','fresh-pickup-clock','required-postfix-Patch_JobGiver_Work_OpportunisticUnload')){$catalog['fixture-bg03p1-'+$id]=1}
    foreach($name in @('HaulersDream.HaulersDreamMod','CommonSense.Settings')){$catalog['fixture-type-'+$name]=2}
    foreach($name in @('CommonSense.JobDriver_DoBill_MakeNewToils_CommonSensePatch','HaulersDream.Patch_WorkGiver_DoBill_InventoryRoute','HaulersDream.HaulersDreamGameComponent','RimWorld.WorkGiver_DoBill')){$catalog['fixture-type-'+$name]=1}
    foreach($name in @('masterEnabled','inventoryCraftDeliver','shareForCrafting','markForUnload','batchByDefault','adv_cleaning','adv_haul_all_ings','clean_before_work','hauling_over_bills','gatherIngredients')){foreach($prefix in @('setting-exists-','setting-value-')){$catalog['fixture-'+$prefix+$name]=1}}
    for($i=0;$i-lt3;$i++){$catalog['fixture-seed-filth-'+$i]=1}
    $filths=@($Value.partialInventoryBill.cleaning|Select-Object -ExpandProperty filthId -Unique)
    if($filths.Count-ne3){$Problems.Add('BG03 must have three distinct seeded filth IDs.')}
    foreach($id in $filths){$catalog['fixture-one-layer-filth-'+$id]=1}
    for($i=0;$i-lt$Value.mods.Count;$i++){$catalog['mod-order-root-'+$i]=1}
    foreach($a in $Value.assemblies){$catalog['single-assembly-'+$a.name]=1;$catalog['assembly-identity-'+$a.name]=1}
    return $catalog
}
function Test-PBEvidenceCore($Value,[string]$ExpectedBehavior,$Problems) {
    if(-not(Test-PartialBillShape $Value $Problems)){return}
    $p=$Value.partialInventoryBill;$changed=$ExpectedBehavior-ceq'satisfied';$expected=Get-PBExpected
    $status=if($changed){'passed'}else{'behavior-gap-observed'}
    if($ExpectedBehavior-cnotin@('satisfied','baseline-gap') -or $Value.schemaVersion-ne1 -or $Value.runId-cnotmatch'^[a-f0-9]{32}$' -or $Value.caseId-cne'BG03-P1' -or $Value.processId-le0 -or $Value.status-cne$status -or $Value.unityErrorsObserved-ne0 -or $null-ne$Value.negativeControl -or $p.caseId-cne'BG03-P1' -or $p.recipe-cne'CookMealSimpleBulk' -or $p.expectedBehavior-cne$ExpectedBehavior -or $p.status-cne$status -or -not$p.fixtureValid -or -not$p.expectationMatched -or $p.timedOut -or -not$p.completedNativeRecipe -or $p.requestedBehaviorSatisfied-ne$changed -or $p.fullInventoryBeforeReturn-ne$changed -or $p.changedProvenanceComplete-ne$changed -or $p.baselineProvenanceComplete-eq$changed){$Problems.Add('BG03 run/scenario identity, outcome or error state contradicts the selected expectation.')}
    if($p.startedTick-lt0 -or $p.finishedTick-le$p.startedTick -or ([long]$p.finishedTick-$p.startedTick)-ge12000 -or $p.maximumTicks-ne12000 -or $null-eq$p.stableSinceTick -or $p.stableSinceTick-lt$p.startedTick -or ([long]$p.finishedTick-$p.stableSinceTick)-lt300 -or $p.stableTickCount-lt301 -or $p.nativeJobs-ne1 -or $p.gatherJobs-ne$(if($changed){1}else{0}) -or $p.stateEvents-le0){$Problems.Add('BG03 completion/lifetime/stability counters are invalid.')}
    $originIds=@{};$unitIds=@{};$next=0
    if($p.origins.Count-ne3){$Problems.Add('BG03 origin catalog must contain three original Things.')}
    foreach($def in @('RawRice','RawPotatoes','Milk')){
        $o=Get-PBOne $p.origins def $def $Problems;if($null-eq$o){continue}
        if($o.thingId-le0 -or $originIds.ContainsKey($o.thingId) -or $o.count-ne$expected[$def] -or $o.initiallyHeld-ne($def-ceq'Milk') -or -not(Test-PBIds $o.units @($next..($next+$o.count-1)))){$Problems.Add('BG03 origin identity, count, held status or seeded unit order is incorrect.')}
        $originIds[$o.thingId]=$true;foreach($u in $o.units){if($unitIds.ContainsKey($u)){$Problems.Add('BG03 duplicates an initial unit.')};$unitIds[$u]=$def};$next+=$o.count
    }
    if($unitIds.Count-ne40){$Problems.Add('BG03 origin units do not cover forty identities.')}
    if($p.initial.tick-ne$p.startedTick -or $p.initial.reason-cne'initial-before-automatic-work' -or $p.initial.meals-ne0 -or $p.initial.billRemaining-ne1 -or $p.initial.things.Count-ne3 -or $p.initial.quantities.Count-ne3){$Problems.Add('BG03 initial scene does not contain exactly the configured unstarted bill and three ingredient Things.')}
    foreach($origin in $p.origins){$thing=Get-PBOne $p.initial.things id $origin.thingId $Problems;$q=Get-PBOne $p.initial.quantities def $origin.def $Problems;$heldCount=if($origin.initiallyHeld){$origin.count}else{0};if($null-ne$thing -and ($thing.def-cne$origin.def -or $thing.count-ne$origin.count -or $thing.custody-cne$(if($origin.initiallyHeld){'inventory'}else{'floor'}))){$Problems.Add('BG03 actual initial origin custody/count differs.')};if($null-ne$q -and ($q.inventory-ne$heldCount -or $q.hands-ne0 -or $q.floor-ne($origin.count-$heldCount) -or $q.uniqueAcquired-ne0 -or $q.reenteredHeld-ne0 -or $q.consumed-ne0)){$Problems.Add('BG03 initial counters invent a pickup/consumption or place milk outside inventory.')}}
    $o=$p.observerOutControl
    if($o.tick-ne$p.startedTick -or $o.initialThingId-le0 -or $o.transferredThingId-le0 -or $o.initialThingId-eq$o.transferredThingId -or $originIds.ContainsKey($o.initialThingId) -or $originIds.ContainsKey($o.transferredThingId) -or $o.nativeMoved-ne3 -or $o.transferredCount-ne3 -or $o.sourceRemaining-ne4 -or $o.droppedThingId-ne$o.transferredThingId -or $o.droppedCount-ne3 -or -not$o.transferValid -or -not$o.nativeDropped -or -not$o.dropValid -or -not$o.cleaned){$Problems.Add('BG03 separate native out-control is incomplete or contradicts 7 -> 4+3 -> drop3.')}
    $controls=@('native-completed-with-exact-consumption','ordinary-native-work','three-native-cs-cleaning-pairs','initial-held-and-unique-floor-acquisition','settled-custody-and-ancestry-conserved','observers-and-roof-healthy','no-ingredient-storage-detour','fresh-inventory-milk-selected','initial-work-gate-observed')
    $requested=@('complete-inventory-before-first-return','no-reentry-second-excursion-or-extra-gather','changed-complete-selection-provenance')
    if($p.assertions.Count-ne13){$Problems.Add('BG03 nested assertion catalog must have thirteen rows.')}
    foreach($id in @($controls)+@($requested)+@('baseline-initial-milk-selected')){
        $a=Get-PBOne $p.assertions id $id $Problems;if($null-eq$a){continue}
        $category=if($id-cin$controls){'control'}elseif($id-cin$requested){'requested'}else{'baseline'}
        $pass=if($category-ceq'control'){$true}elseif($category-ceq'requested'){$changed}else{-not$changed}
        if($a.category-cne$category -or $a.passed-ne$pass -or [string]::IsNullOrWhiteSpace($a.detail)){$Problems.Add('BG03 nested assertion contradicts its required outcome: '+$id)}
    }
    $catalog=Get-PBGlobalCatalog $Value $Problems
    foreach($id in $catalog.Keys){$rows=@($Value.assertions|Where-Object id -CEQ $id);if($rows.Count-ne$catalog[$id] -or @($rows|Where-Object {-not$_.passed}).Count-gt0){$Problems.Add('BG03 missing/duplicated/failed global assertion '+$id)}}
    foreach($a in $Value.assertions){if(-not$catalog.ContainsKey($a.id)){$Problems.Add('BG03 unexpected global assertion '+$a.id)}}
    foreach($name in @('masterEnabled','inventoryCraftDeliver','shareForCrafting','markForUnload','adv_cleaning','gatherIngredients','batchByDefault','adv_haul_all_ings','clean_before_work','hauling_over_bills')){
        $a=@($Value.assertions|Where-Object id -CEQ ('fixture-setting-value-'+$name));$on=$name-cin@('masterEnabled','inventoryCraftDeliver','shareForCrafting','markForUnload','adv_cleaning','gatherIngredients')
        if($a.Count-eq1 -and $a[0].observed-cne($name+'='+$on.ToString())){$Problems.Add('BG03 actual setting value does not match '+$name)}
    }
    $names=@('Assembly-CSharp','UnityEngine.CoreModule','0Harmony','HarmonyMod','HaulersDream','HaulersDream.Core','CommonSense','HaulersDream.RuntimeHarness')
    if($Value.assemblies.Count-ne$names.Count){$Problems.Add('BG03 loaded assembly catalog is incomplete.')}
    foreach($name in $names){$a=Get-PBOne $Value.assemblies name $name $Problems;if($null-ne$a){$guid=[guid]::Empty;if($a.sha256-notmatch'^[0-9A-Fa-f]{64}$' -or -not[guid]::TryParse($a.moduleVersionId,[ref]$guid) -or $guid-eq[guid]::Empty -or $a.assemblyVersion-notmatch'^\d+\.\d+\.\d+\.\d+$' -or $a.path-notmatch'^[A-Za-z]:\\' -or $a.path.IndexOf(('\'+$Value.runId+'\runtime\'),[StringComparison]::OrdinalIgnoreCase)-lt0){$Problems.Add('BG03 invalid/unisolated loaded identity '+$name)}}}
    if($Value.mods.Count-ne5 -or $Value.executingGameVersion-cne'1.6.4871 rev591' -or $Value.installedVersionFile-cne'1.6.4871 rev590'){$Problems.Add('BG03 unreviewed mod count or installed/executing version.')}
    Test-PBFreshCargo $Value $Problems
    Test-PBProvenance $Value $Problems
    $prod=if($p.products.Count-eq1){$p.products[0]}else{$null}
    if($null-eq$prod){$Problems.Add('BG03 must contain one product event.')}else{
        if($prod.count-ne4 -or $prod.thingId-le0 -or $originIds.ContainsKey($prod.thingId) -or -not$prod.native -or -not$prod.cleaningComplete){$Problems.Add('BG03 product identity/count/native context invalid.')}
        if($p.cleaning.Count-ne6){$Problems.Add('BG03 cleaning must have exactly six seeded operations.')}
        foreach($id in @($p.cleaning|Select-Object -ExpandProperty filthId -Unique)){
            $r=Get-PBOne @($p.cleaning|Where-Object filthId -EQ $id) operation 'filth-removal' $Problems
            $c=Get-PBOne @($p.cleaning|Where-Object filthId -EQ $id) operation 'record-increment' $Problems
            if($null-eq$r -or $null-eq$c){continue}
            if($id-le0 -or $originIds.ContainsKey($id) -or $r.sequence-ge$c.sequence -or $c.sequence-ge$prod.sequence -or $r.tick-ne$c.tick -or $r.jobId-ne$prod.jobId -or $c.jobId-ne$prod.jobId -or -not$r.native -or -not$c.native -or -not$r.attributed -or -not$c.attributed -or -not$r.destroyed -or -not$c.destroyed -or $r.recordDelta-ne0 -or $c.recordDelta-ne1 -or $r.toilTickMethod-cne$c.toilTickMethod){$Problems.Add('BG03 seeded cleaning pair lacks same native job/tick and ordered destruction/increment.')}
            $cs=Get-PBOne $Value.assemblies name CommonSense $Problems
            if($null-ne$cs -and ($c.toilTickMethod-notmatch'^CommonSense\.Utility\+.*\.[<]CleanFilthToil[>]b__\d+;assembly=CommonSense, Version=' -or -not$c.toilTickMethod.EndsWith(';assembly=CommonSense, Version='+$cs.assemblyVersion+', Culture=neutral, PublicKeyToken=null;mvid='+$cs.moduleVersionId))){$Problems.Add('BG03 cleaning toil is not bound to the actual CommonSense module.')}
        }
        $used=@{};foreach($c in $p.consumption){if($null-eq$c.before -or $null-eq$c.after){$Problems.Add('BG03 consumed Thing states absent.');continue};if(-not$c.attributed -or $c.sequence-le$prod.sequence -or $c.tick-ne$prod.tick -or $c.jobId-ne$prod.jobId -or $c.before.id-ne$c.after.id -or $c.before.def-cne$c.after.def -or $c.before.destroyed -or -not$c.after.destroyed -or $c.after.count-ne0 -or $c.after.custody-cne'destroyed' -or $c.before.count-ne$c.units.Count){$Problems.Add('BG03 native consumption boundary invalid.')};foreach($u in $c.units){if($used.ContainsKey($u)){$Problems.Add('BG03 consumes the same origin unit twice.')};$used[$u]=$true}}
        if($used.Count-ne40){$Problems.Add('BG03 native consumption does not cover all forty units.')}
        $end=Get-PBOne @($p.ends|Where-Object jobDef -CEQ DoBill) jobId $prod.jobId $Problems
        if($null-ne$end -and ($end.sequence-le$prod.sequence -or @($p.consumption|Where-Object sequence -GE $end.sequence).Count-gt0 -or $end.condition-cne'Succeeded' -or -not$end.released -or $end.tick-gt$p.stableSinceTick)){$Problems.Add('BG03 native cleanup does not follow complete consumption and precede stability.')}
    }
}
function Test-PBProvenance($Value,$Problems) {
    $p=$Value.partialInventoryBill;$changed=$p.expectedBehavior-ceq'satisfied'
    $native=@($p.starts|Where-Object {$null-ne$_.actual -and $_.actual.def-ceq'DoBill'});$gather=@($p.starts|Where-Object {$null-ne$_.actual -and $_.actual.def-ceq'HaulersDream_GatherBillIngredients'})
    if($native.Count-ne1 -or $gather.Count-ne$(if($changed){1}else{0})){$Problems.Add('BG03 actual native/gather start catalog invalid.');return}
    $n=$native[0];$first=if($changed){$gather[0]}else{$n}
    $candidate=Get-PBOne $p.candidates id $first.candidateId $Problems
    if($null-eq$candidate){return}
    if($candidate.forcedArgument -or $candidate.workGiverClass-cne'RimWorld.WorkGiver_DoBill' -or $null-eq$candidate.native -or $candidate.native.def-cne'DoBill' -or $null-eq$candidate.routed -or $candidate.routed.def-cne$(if($changed){'HaulersDream_GatherBillIngredients'}else{'DoBill'}) -or $candidate.nativeSequence-le0 -or $candidate.nativeSequence-ge$candidate.routedSequence -or $candidate.routedSequence-ge$first.sequence -or $first.sequence-ge$first.actualSequence -or $first.requested.id-ne$candidate.routed.id -or $first.actual.id-ne$first.requested.id){$Problems.Add('BG03 candidate -> request -> actual start provenance is broken.')}
    foreach($j in @($candidate.native,$candidate.routed,$first.requested,$first.actual)){Test-PBSelection $j $p.origins $Problems -InitialMilk}
    Test-PBSelectionSame $candidate.native $candidate.routed $Problems;Test-PBSelectionSame $candidate.routed $first.requested $Problems;Test-PBSelectionSame $first.requested $first.actual $Problems
    foreach($s in @($native)+@($gather)){
        $name=if($s.actual.def-ceq'DoBill'){'Assembly-CSharp'}else{'HaulersDream'};$type=if($name-ceq'Assembly-CSharp'){'Verse.AI.JobDriver_DoBill'}else{'HaulersDream.JobDriver_GatherBillIngredients'};$a=Get-PBOne $Value.assemblies name $name $Problems
        if($null-eq$s.actualCustody -or $s.actualDriver-cne$type -or $s.actual.workGiver-cne'DoBillsCook' -or $s.actual.workGiverClass-cne'RimWorld.WorkGiver_DoBill' -or $s.actual.playerForced -or $s.actualSequence-le$s.sequence -or $s.actualCustody.sequence-le$s.actualSequence -or $s.actualCustody.jobId-ne$s.actual.id -or $s.actualCustody.driver-cne$type -or ($null-ne$a -and ($s.actualDriverMvid-cne$a.moduleVersionId -or -not$s.actualDriverAssembly.StartsWith($name+', Version='+$a.assemblyVersion+',')))){$Problems.Add('BG03 actual driver/job/workgiver/assembly identity is not the required one.')}
    }
    if($changed){
        $g=$gather[0];$end=Get-PBOne $p.ends jobId $g.actual.id $Problems
        if($null-ne$n.candidateId -or $n.previousExecutedGatherId-ne$g.actual.id -or $n.previous.id-ne$g.actual.id -or $n.previousDriver-cne$g.actualDriver -or $n.previousToilInit-cnotlike'HaulersDream.JobDriver_GatherBillIngredients.Handoff;assembly=*' -or -not$n.previousToilInit.EndsWith(';mvid='+$g.actualDriverMvid) -or $n.sequence-le$g.actualSequence -or $n.lastJobEndCondition-cne'Succeeded' -or $null-eq$end -or $end.condition-cne'Succeeded' -or -not$end.released -or $end.sequence-le$n.sequence -or $end.sequence-ge$n.actualSequence -or -not(Test-PBAllInventory $n.actualCustody)){$Problems.Add('BG03 native handoff lacks the actually executing gather/Handoff/cleanup boundary.')}
        foreach($j in @($n.previous,$n.requested,$n.actual)){Test-PBSelection $j $p.origins $Problems};Test-PBSelectionSame $n.requested $n.actual $Problems
    }
}
function Get-PBBindings {
    # Metadata tokens independently read from the installed rev591 native module.
    # Tuple: target, token, native out parameters, exact observer hooks.
    return @(
        @('RimWorld.WorkGiver_DoBill.JobOnThing',100697024,'','CandidateBefore/NativeCandidateAfter/'),@('RimWorld.WorkGiver_DoBill.JobOnThing',100697024,'','/RoutedCandidateAfter/'),
        @('RimWorld.JobGiver_Work.TryIssueJobPackage',100696489,'','WorkBefore/WorkResultBeforeUnload/WorkFinally'),@('RimWorld.JobGiver_Work.TryIssueJobPackage',100696489,'','/WorkResultAfterUnload/'),
        @('Verse.AI.Pawn_JobTracker.StartJob',100688842,'','StartBefore//StartFinally'),@('Verse.AI.JobDriver.Notify_Starting',100687866,'','/ActualStartingAfter/'),
        @('Verse.AI.JobDriver.DriverTick',100687857,'','DriverBefore//DriverFinally'),@('Verse.AI.JobDriver.DriverTickInterval',100687858,'','DriverBefore//DriverFinally'),@('Verse.AI.JobDriver.TryActuallyStartNextToil',100687860,'','DriverBefore//DriverFinally'),
        @('Verse.AI.Pawn_JobTracker.CleanupCurrentJob',100688846,'','CleanupBefore/CleanupAfter/'),@('RimWorld.Filth.ThinFilth',100726069,'','FilthBefore/FilthAfter/'),@('RimWorld.Pawn_RecordsTracker.Increment',100716071,'','RecordBefore/RecordAfter/'),
        @('Verse.GenRecipe.PostProcessProduct',100685218,'','/ProductAfter/'),@('Verse.RecipeWorker.ConsumeIngredient',100667239,'','ConsumptionBefore/ConsumptionAfter/'),
        @('Verse.ThingOwner.TryTransferToContainer',100678240,'','TransferBefore//HolderFinally'),@('Verse.ThingOwner.TryTransferToContainer',100678241,'','TransferBefore//HolderFinally'),@('Verse.ThingOwner.TryTransferToContainer',100678242,'resultingTransferredItem:Verse.Thing&','TransferBefore//HolderFinally'),
        @('Verse.ThingOwner.TryDrop',100678246,'lastResultingThing:Verse.Thing&','HolderBefore//HolderFinally'),@('Verse.ThingOwner.TryDrop',100678247,'resultingThing:Verse.Thing&','HolderBefore//HolderFinally'),@('Verse.ThingOwner.TryDrop',100678248,'lastResultingThing:Verse.Thing&','HolderBefore//HolderFinally'),@('Verse.ThingOwner.TryDrop',100678249,'lastResultingThing:Verse.Thing&','HolderBefore//HolderFinally'),
        @('Verse.ThingOwner`1[[Verse.Thing, Assembly-CSharp, Version=1.6.9676.17735, Culture=neutral, PublicKeyToken=null]].TryAdd',100678164,'','HolderBefore//HolderFinally'),@('Verse.ThingOwner`1[[Verse.Thing, Assembly-CSharp, Version=1.6.9676.17735, Culture=neutral, PublicKeyToken=null]].TryAdd',100678165,'','HolderBefore//HolderFinally'),@('Verse.Pawn_CarryTracker.TryStartCarry',100676343,'','CarryBefore//HolderFinally'),@('Verse.Pawn_CarryTracker.TryStartCarry',100676344,'','CarryBefore//HolderFinally'),
        @('Verse.Thing.SplitOff',100678050,'','SplitBefore/SplitAfter/SplitFinally'),@('Verse.Thing.TryAbsorbStack',100678049,'','MergeBefore/MergeAfter/MergeFinally'),@('Verse.ThingWithComps.SplitOff',100678356,'','SplitBefore/SplitAfter/SplitFinally'),@('Verse.ThingWithComps.TryAbsorbStack',100678355,'','MergeBefore/MergeAfter/MergeFinally')
    )
}
function Test-PBBindingEvents($Value,$Parsed,$Problems) {
    $game=Get-PBOne $Value.assemblies name Assembly-CSharp $Problems;if($null-eq$game){return}
    if($game.moduleVersionId-cne'61e41735-6189-4da4-9d21-0260257b5097' -or $game.sha256-cne'5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A'){$Problems.Add('BG03 observer tokens belong to a different native module.')}
    $bindings=@($Parsed|Where-Object phase -CEQ bg03-observer-binding);$catalog=Get-PBBindings
    if($bindings.Count-ne($catalog.Count+2)){$Problems.Add('BG03 observer binding catalog count is incorrect.')}
    foreach($b in $catalog){$text=$b[0]+'; token='+$b[1]+'; mvid='+$game.moduleVersionId+'; originalOutParameters='+$b[2]+'; hooks='+$b[3]+'; writes=__state only';if(@($bindings|Where-Object detail -CEQ $text).Count-ne1){$Problems.Add('BG03 native observer binding missing/changed: '+$b[0]+'/'+$b[1])}}
    Test-PBFreshBindings $Value $Parsed $Problems
    $sorted=Get-PBOne $Parsed phase bg03-candidate-postfix-order $Problems
    if($null-ne$sorted){$names=@($sorted.detail-split' \| ');$before=[array]::IndexOf($names,'HaulersDream.RuntimeHarness.Bg03Observers.NativeCandidateAfter');$route=[array]::IndexOf($names,'HaulersDream.Patch_WorkGiver_DoBill_InventoryRoute.Postfix');$after=[array]::IndexOf($names,'HaulersDream.RuntimeHarness.Bg03Observers.RoutedCandidateAfter');if($before-lt0 -or $route-le$before -or $after-le$route -or @($names|Where-Object {$_-ceq$names[$before]}).Count-ne1){$Problems.Add('BG03 sorted postfixes do not bracket the HD route.')}}
    $hd=Get-PBOne $Value.assemblies name HaulersDream $Problems
    $legacy=if($null-ne$hd -and $hd.sha256-ceq'D55644ACBE064F4F6A37C82205F48EEAF61264100C92AC568BDCB612FE8DCC2D'){'HaulersDream.Patch_WorkGiver_DoBill_JobOnThing.Postfix | '}else{''}
    $expectedSorted='HaulersDream.RuntimeHarness.Bg03Observers.NativeCandidateAfter | HaulersDream.Patch_WorkGiver_DoBill_BatchRoute.Postfix | HaulersDream.Patch_WorkGiver_DoBill_InventoryRoute.Postfix | '+$legacy+'HaulersDream.Patch_WorkGiver_DoBill_Routing.Postfix | HaulersDream.RuntimeHarness.Bg03Observers.RoutedCandidateAfter'
    if($null-ne$sorted -and $sorted.detail-cne$expectedSorted){$Problems.Add('BG03 sorted postfix catalog contains missing, additional or unreviewed methods.')}
    # RequirePatch exports MethodInfo.ToString(), not declaring-type metadata. Match the whole captured signature.
    foreach($spec in @(@('CommonSense','prefix','JobDriver_DoBill_MakeNewToils_CommonSensePatch','net.avilmask.rimworld.mod.CommonSense','System.Collections.Generic.IEnumerable`1[Verse.AI.Toil] MakeNewToils()'),@('HaulersDream','postfix','Patch_WorkGiver_DoBill_InventoryRoute','giwaffed.HaulersDream','Verse.AI.Job JobOnThing(Verse.Pawn, Verse.Thing, Boolean)'),@('HaulersDream','postfix','Patch_JobGiver_Work_OpportunisticUnload','giwaffed.HaulersDream','Verse.AI.ThinkResult TryIssueJobPackage(Verse.Pawn, Verse.AI.JobIssueParams)'))){
        $a=Get-PBOne $Value.assemblies name $spec[0] $Problems;$assert=Get-PBOne $Value.assertions id ('fixture-bg03p1-required-'+$spec[1]+'-'+$spec[2]) $Problems
        if($null-ne$a -and $null-ne$assert){$suffix='; '+$spec[1]+'; owner='+$spec[3]+'; method='+$spec[0]+'.'+$spec[2]+'.'+$(if($spec[1]-ceq'prefix'){'Prefix'}else{'Postfix'})+'; assembly='+$spec[0]+', Version='+$a.assemblyVersion+', Culture=neutral, PublicKeyToken=null; path='+$a.path+'; mvid='+$a.moduleVersionId;if($assert.observed-cne($spec[4]+$suffix)){$Problems.Add('BG03 required captured target signature/patch owner/method/loaded module correspondence failed.') }}
    }
}
function Test-PBEventCopy($Rows,[string]$Phase,$Expected,$Problems) {
    $rows=@($Rows|Where-Object phase -CEQ $Phase)
    if($rows.Count-ne@($Expected).Count){$Problems.Add('BG03 raw/result row count mismatch: '+$Phase);return}
    for($i=0;$i-lt$rows.Count;$i++){if(-not(Test-PBSame $rows[$i].data $Expected[$i])){$Problems.Add('BG03 raw/result payload mismatch: '+$Phase+'/'+$i)}}
}
function Test-PBPartialCopy($Captured,$Final,[string[]]$Fields,$Problems) {
    foreach($name in $Fields){if(-not(Test-PBSame $Captured.$name $Final.$name)){$Problems.Add('BG03 earlier boundary changed field '+$name)}}
}
function Test-PBEventsCore($Value,$Events,[string]$RunId,$Problems) {
    if(-not(Test-PartialBillShape $Value $Problems)){return}
    $p=$Value.partialInventoryBill;$before=$Problems.Count
    if($Events-isnot[array] -or $Events.Count-eq0){$Problems.Add('BG03 raw event array missing.');return}
    $jsonKinds=@{'bg03-synthetic-fresh-cargo'='fresh';'bg03-unload-gate'='gate';'bg03-initial-unit-origins'='origins';'bg03-native-candidate-before-route'='candidate';'bg03-candidate-after-route'='candidate';'bg03-startjob-request-before-toils'='start';'bg03-actual-start-before-instant-toils'='start';'bg03-executing-job'='start';'bg03-actual-split'='ancestry';'bg03-actual-merge'='ancestry';'bg03-actual-custody-transfer'='transfer';'bg03-settled-state'='state';'bg03-actual-departure'='state';'bg03-complete-inventory-before-return'='state';'bg03-actual-first-return'='state';'bg03-seeded-filth-removal'='clean';'bg03-seeded-cleaning-increment'='clean';'bg03-native-product-created-before-consumption'='product';'bg03-native-ingredient-consumption'='consume';'bg03-actual-job-cleanup'='end';'bg03-assertion'='assertion';'bg03-p1-result'='result';'bg03-observer-native-out-transfer'='out';'bg03-observer-native-out-drop'='out';'bg03-observer-native-out-control-final'='out'}
    $textPhases=@('harness-start','assertion','game-version-provenance','new-game','map-initialized','fixture-isolation','fixture-ready','bg03-initial-tag-setup','bg03-candidate-postfix-order','bg03-work-postfix-order','bg03-observer-binding','scenario-observed','error-capture-boundary','terminal-result')
    $parsed=New-Object 'System.Collections.Generic.List[object]';$index=0;$lastTick=-1;$lastUtc=[datetimeoffset]::MinValue
    foreach($e in $Events){
        $index++;if(-not(Test-PBFields $e @{sequence='int';runId='string';caseId='string';utc='utc';phase='string';detail='string';tick='int'} 'event' $Problems)){continue}
        $utc=[datetimeoffset]$e.utc
        if($e.sequence-ne$index -or $e.runId-cne$RunId -or $e.runId-cne$Value.runId -or $e.caseId-cne'BG03-P1' -or $e.tick-lt$lastTick -or $e.tick-lt-1 -or $utc-lt$lastUtc){$Problems.Add('BG03 outer sequence/tick/time/run identity is discontinuous.')};$lastTick=$e.tick;$lastUtc=$utc
        $data=$null
        if($jsonKinds.ContainsKey($e.phase)){
            try{$data=ConvertFrom-Json -InputObject $e.detail -ErrorAction Stop}catch{$Problems.Add('BG03 malformed JSON payload '+$e.phase);continue}
            $kind=$jsonKinds[$e.phase]
            if($kind-ceq'origins'){if($data-isnot[array]){$Problems.Add('BG03 origins event is not an array.')}else{foreach($x in $data){Test-PBRowShape $x origin $Problems}}}
            elseif($kind-ceq'result'){if(-not(Test-PBSame $data $p)){$Problems.Add('BG03 raw result payload differs from result.json.')}}
            else{Test-PBRowShape $data $kind $Problems;if($null-ne$data -and $null-ne$data.PSObject.Properties['tick'] -and $data.tick-ne$e.tick){$Problems.Add('BG03 payload tick differs from callback tick: '+$e.phase)}}
        }elseif($e.phase-cnotin$textPhases){$Problems.Add('BG03 unexpected/error event phase: '+$e.phase)}
        $parsed.Add([pscustomobject]@{sequence=$e.sequence;tick=$e.tick;utc=$utc;phase=$e.phase;detail=$e.detail;data=$data})
    }
    if($Problems.Count-ne$before){return}
    $rows=$parsed.ToArray()
    Test-PBLifecycle $Value $rows $Problems;Test-PBBindingEvents $Value $rows $Problems
    Test-PBFreshEvents $Value $rows $Problems
    Test-PBEventCopy $rows bg03-initial-unit-origins @(,$p.origins) $Problems
    $families=@{'bg03-actual-split'=@($p.ancestry|Where-Object operation -CEQ SplitOff);'bg03-actual-merge'=@($p.ancestry|Where-Object operation -CEQ TryAbsorbStack);'bg03-actual-custody-transfer'=$p.transfers;'bg03-seeded-filth-removal'=@($p.cleaning|Where-Object operation -CEQ filth-removal);'bg03-seeded-cleaning-increment'=@($p.cleaning|Where-Object operation -CEQ record-increment);'bg03-native-product-created-before-consumption'=$p.products;'bg03-native-ingredient-consumption'=$p.consumption;'bg03-actual-job-cleanup'=$p.ends;'bg03-assertion'=$p.assertions;'bg03-observer-native-out-control-final'=@($p.observerOutControl)}
    foreach($phase in $families.Keys){Test-PBEventCopy $rows $phase $families[$phase] $Problems}
    foreach($pair in @(@('bg03-actual-departure','departure'),@('bg03-complete-inventory-before-return','completeInventory'),@('bg03-actual-first-return','firstReturn'))){$expectedRows=@();if($null-ne$p.($pair[1])){$expectedRows=@($p.($pair[1]))};Test-PBEventCopy $rows $pair[0] $expectedRows $Problems}
    foreach($phase in @('bg03-native-candidate-before-route','bg03-candidate-after-route')){
        $items=@($rows|Where-Object phase -CEQ $phase);if($items.Count-ne$p.candidates.Count){$Problems.Add('BG03 missing candidate boundary '+$phase)}
        foreach($item in $items){$c=Get-PBOne $p.candidates id $item.data.id $Problems;if($null-eq$c){continue};if($phase-ceq'bg03-candidate-after-route'){if(-not(Test-PBSame $item.data $c)){$Problems.Add('BG03 routed candidate mismatch.')}}else{Test-PBPartialCopy $item.data $c @('id','tick','forcedArgument','workGiverClass','nativeSequence','native') $Problems;if($item.data.routedSequence-ne0 -or $null-ne$item.data.routed){$Problems.Add('BG03 pre-route candidate contains future state.')}}}
    }
    $startFields=@('sequence','tick','candidateId','previous','requested','previousDriver','previousToilInit','previousExecutedGatherId','lastJobEndCondition')
    foreach($phase in @('bg03-startjob-request-before-toils','bg03-actual-start-before-instant-toils')){
        $items=@($rows|Where-Object phase -CEQ $phase);$expectedStarts=@(if($phase-ceq'bg03-startjob-request-before-toils'){$p.starts}else{$p.starts|Where-Object actualSequence -GT 0})
        if($items.Count-ne$expectedStarts.Count){$Problems.Add('BG03 missing StartJob boundary '+$phase)}
        foreach($item in $items){$s=Get-PBOne $expectedStarts sequence $item.data.sequence $Problems;if($null-eq$s){continue};if($phase-ceq'bg03-actual-start-before-instant-toils'){if(-not(Test-PBSame $item.data $s)){$Problems.Add('BG03 actual-start payload mismatch.')}}else{Test-PBPartialCopy $item.data $s $startFields $Problems;if($item.data.actualSequence-ne0 -or $null-ne$item.data.actual -or $null-ne$item.data.actualCustody){$Problems.Add('BG03 requested start contains future actual execution.')}}}
    }
    $out=$p.observerOutControl
    foreach($phase in @('bg03-observer-native-out-transfer','bg03-observer-native-out-drop')){$e=Get-PBOne $rows phase $phase $Problems;if($null-eq$e){continue};$fields=@('tick','initialThingId','transferredThingId','transferredCount','sourceRemaining','nativeMoved','transferValid');if($phase-ceq'bg03-observer-native-out-drop'){$fields+=@('nativeDropped','droppedThingId','droppedCount','dropValid')};Test-PBPartialCopy $e.data $out $fields $Problems;if($e.data.cleaned){$Problems.Add('BG03 out-control cleanup was claimed before finally.')};if($phase-ceq'bg03-observer-native-out-transfer' -and ($e.data.nativeDropped -or $e.data.dropValid -or $null-ne$e.data.droppedThingId -or $e.data.droppedCount-ne0)){$Problems.Add('BG03 transfer out-control contains future drop results.')}}
    if($Problems.Count-ne$before){return}
    Test-PBReplay $Value $rows $Problems
}
function Read-PBCell([string]$Text,$Problems) {
    $x=0;$z=0
    if($Text-notmatch'^\((-?\d+), 0, (-?\d+)\)$'){$Problems.Add('BG03 invalid cell.');return $null}
    $sx=$Matches[1];$sz=$Matches[2]
    if(-not[int]::TryParse($sx,[ref]$x) -or -not[int]::TryParse($sz,[ref]$z)){$Problems.Add('BG03 cell coordinate overflow.');return $null}
    return @{x=$x;z=$z}
}
function Get-PBGeometry($Value,$Problems) {
    $a=Get-PBOne $Value.assertions id fixture-sources-away-from-bench $Problems;if($null-eq$a){return $null}
    if($a.observed-notmatch'^bench=(\([^)]*\)); rice=(\([^)]*\)); potato=(\([^)]*\))$'){$Problems.Add('BG03 source/bench coordinates missing.');return $null}
    $b=$Matches[1];$r=$Matches[2];$p=$Matches[3]
    $map=Get-PBOne $Value.assertions id real-map-initialized $Problems;$width=0;$depth=0
    if($null-ne$map -and $map.observed-match'^size=\((\d+), 1, (\d+)\); maps=1$'){$sx=$Matches[1];$sz=$Matches[2];if(-not[int]::TryParse($sx,[ref]$width) -or -not[int]::TryParse($sz,[ref]$depth)){$Problems.Add('BG03 map dimension overflow.')}}else{$Problems.Add('BG03 actual one-map dimensions missing.')}
    return @{bench=(Read-PBCell $b $Problems);rice=(Read-PBCell $r $Problems);potato=(Read-PBCell $p $Problems);width=$width;depth=$depth}
}
function Test-PBLifecycle($Value,$Rows,$Problems) {
    $p=$Value.partialInventoryBill;$single=@{}
    foreach($phase in @('harness-start','game-version-provenance','new-game','map-initialized','fixture-isolation','fixture-ready','bg03-initial-tag-setup','bg03-initial-unit-origins','bg03-p1-result','scenario-observed','error-capture-boundary','terminal-result','bg03-observer-native-out-transfer','bg03-observer-native-out-drop','bg03-observer-native-out-control-final')){$single[$phase]=Get-PBOne $Rows phase $phase $Problems}
    if(@($single.Values|Where-Object {$null-eq$_}).Count-gt0){return}
    $first=$single['harness-start'];$map=$single['map-initialized'];$ready=$single['fixture-ready'];$terminal=$single['terminal-result'];$result=$single['bg03-p1-result'];$observed=$single['scenario-observed'];$boundary=$single['error-capture-boundary']
    if($first.sequence-ne1 -or $first.detail-cne('case=BG03-P1; expectedBehavior='+$p.expectedBehavior) -or $single['new-game'].sequence-ge$map.sequence -or $map.sequence-ge$single['fixture-isolation'].sequence -or $single['fixture-isolation'].sequence-ge$ready.sequence -or $ready.sequence-ge$single['bg03-initial-unit-origins'].sequence -or $single['bg03-initial-tag-setup'].sequence-ge$ready.sequence -or $result.sequence-ge$observed.sequence -or $observed.sequence-ge$boundary.sequence -or $boundary.sequence-ge$terminal.sequence -or $terminal.sequence-ne$Rows.Count){$Problems.Add('BG03 outer setup/result/error/terminal lifecycle order is invalid.')}
    $detail='Partial initially tagged inventory plus two remaining floor sources, native cooking and cleaning, and bounded follow-up only; other inventory cases remain unverified.'
    if($Value.detail-cne$detail -or $terminal.detail-cne($Value.status+': '+$Value.detail) -or $observed.detail-cne('case=BG03-P1; expected='+$p.expectedBehavior+'; requestedBehaviorSatisfied='+$p.requestedBehaviorSatisfied.ToString()+'; expectationMatched='+$p.expectationMatched.ToString()) -or $boundary.detail-cne'Threaded capture closed before terminal result; earlier startup and shutdown require whole Player.log review.' -or $result.tick-ne$p.finishedTick -or $terminal.tick-ne$p.finishedTick -or $ready.tick-ne$p.startedTick -or $Value.unityErrorsObserved-ne0 -or $null-ne$Value.negativeControl){$Problems.Add('BG03 lifecycle detail/tick/error result mismatch.')}
    if(([datetimeoffset]$Value.startedUtc)-gt$first.utc -or ([datetimeoffset]$Value.finishedUtc)-lt$result.utc -or ([datetimeoffset]$Value.finishedUtc)-gt$boundary.utc){$Problems.Add('BG03 run timestamps do not enclose actual execution.')}
    if($map.detail-notmatch'^tick=(\d+); size=\((\d+), 1, (\d+)\)$'){$Problems.Add('BG03 map detail malformed.')}else{
        $tick=0;$width=0;$depth=0;$st=$Matches[1];$sw=$Matches[2];$sd=$Matches[3]
        if(-not[int]::TryParse($st,[ref]$tick) -or -not[int]::TryParse($sw,[ref]$width) -or -not[int]::TryParse($sd,[ref]$depth) -or $tick-ne$map.tick -or $width-le0 -or $depth-le0 -or ([long]$p.startedTick-$tick)-lt5){$Problems.Add('BG03 native map/tick prerequisites invalid.')}
    }
    if($single['game-version-provenance'].detail-cne('Version.txt='+$Value.installedVersionFile+'; executing assembly reports='+$Value.executingGameVersion)){$Problems.Add('BG03 native version event mismatch.')}
    $rawAssertions=@($Rows|Where-Object phase -CEQ assertion);$expectedAssertions=New-Object 'System.Collections.Generic.List[string]'
    foreach($a in $Value.assertions){$observation=$a.observed;if($a.id-ceq'no-unity-errors-after-harness-start'){
        if($observation-cne'observedErrors=0; threaded capture through terminal-result boundary'){$Problems.Add('BG03 final threaded error assertion mismatch.')};$observation='observedErrors=0'
    };$expectedAssertions.Add($a.id+': passed; '+$observation)}
    if($rawAssertions.Count-ne$expectedAssertions.Count){$Problems.Add('BG03 raw/global assertion count mismatch.')}
    foreach($g in @($expectedAssertions|Group-Object -CaseSensitive)){if(@($rawAssertions|Where-Object detail -CEQ $g.Name).Count-ne$g.Count){$Problems.Add('BG03 raw/global assertion payload multiplicity mismatch: '+$g.Name)}}
    $setup=@($rawAssertions|Where-Object {$_.detail.StartsWith('fixture-bg03p1-setup-completed: passed; ')})
    $noerrors=@($rawAssertions|Where-Object {$_.detail-ceq'no-unity-errors-after-harness-start: passed; observedErrors=0'})
    if($setup.Count-ne1 -or $noerrors.Count-ne1){$Problems.Add('BG03 setup/error assertion missing.')}else{
        if($setup[0].sequence-le$single['bg03-observer-native-out-control-final'].sequence -or $setup[0].sequence-ge$result.sequence -or $noerrors[0].sequence-le$result.sequence -or $noerrors[0].sequence-ge$observed.sequence){$Problems.Add('BG03 setup/error assertion ordering invalid.')}
        foreach($row in $Rows){if($row.phase-cin@('bg03-native-candidate-before-route','bg03-candidate-after-route','bg03-startjob-request-before-toils','bg03-actual-start-before-instant-toils','bg03-native-product-created-before-consumption','bg03-native-ingredient-consumption') -and ($row.sequence-le$setup[0].sequence -or $row.sequence-ge$result.sequence)){$Problems.Add('BG03 gameplay callback occurs outside the configured execution window.')}}
    }
    if($single['bg03-observer-native-out-transfer'].sequence-ge$single['bg03-observer-native-out-drop'].sequence -or $single['bg03-observer-native-out-drop'].sequence-ge$single['bg03-observer-native-out-control-final'].sequence){$Problems.Add('BG03 out-control chronology invalid.')}
    $milk=@($p.origins|Where-Object def -CEQ Milk)
    if($milk.Count-eq1 -and $single['bg03-initial-tag-setup'].detail-cne('thing=Milk'+$milk[0].thingId+'; units=12; inventory owner=Verse.Pawn_InventoryTracker; RegisterHauledItem mergedCount argument=0; PeekHashSet contains=true; synthetic initial cargo, not an observed previous haul.')){$Problems.Add('BG03 actual initial tag identity/argument differs.')}
    if($ready.detail-notmatch'^BG03-P1; pawn=([^;]+); recipe=CookMealSimpleBulk; bill=([^;]+); stove=FueledStove(\d+); '){$Problems.Add('BG03 actual fixture cook/bill/stove identity is missing.')}else{$pawnId=$Matches[1];$billId=$Matches[2];$stoveId=0;$stoveText=$Matches[3];if(-not[int]::TryParse($stoveText,[ref]$stoveId) -or $stoveId-le0){$Problems.Add('BG03 fixture stove ID invalid.')};foreach($s in $p.starts){if($null-ne$s.actual -and $s.actual.recipe-ceq'CookMealSimpleBulk' -and ($s.actual.bill-cne$billId -or $s.actual.targetA-ne$stoveId)){$Problems.Add('BG03 actual native/gather uses another fixture bill or stove.')}}}
    $initial=Get-PBOne $Value.assertions id fixture-bg03p1-initial-custody $Problems
    if($null-ne$initial){try{$s=ConvertFrom-Json -InputObject $initial.observed -ErrorAction Stop;if(-not(Test-PBSame $s $p.initial)){$Problems.Add('BG03 initial custody assertion/result mismatch.')}}catch{$Problems.Add('BG03 initial custody assertion JSON malformed.')}}
    foreach($row in $Rows){if($row.phase.StartsWith('bg03-') -and $row.phase-cne'bg03-p1-result' -and $row.sequence-ge$result.sequence){$Problems.Add('BG03 scenario event appears after final scenario result.')}}
}
function Add-PBNode($Nodes,[int]$Sequence,[string]$Kind,$Value,[bool]$Settled,$Problems) {
    if($Sequence-le0){$Problems.Add('BG03 internal capture sequence must be positive.');return}
    $key=[string]$Sequence
    if($Nodes.ContainsKey($key)){
        $old=$Nodes[$key]
        if($old.kind-ceq'state' -and $Kind-ceq'transferAfter' -and (Test-PBSame $old.value $Value.after)){$old.kind=$Kind;$old.value=$Value}
        elseif($old.kind-cne$Kind -or -not(Test-PBSame $old.value $Value)){$Problems.Add('BG03 conflicting records reuse capture sequence '+$Sequence)}
        elseif($Settled){$old.settled=$true}
    }else{$Nodes[$key]=[pscustomobject]@{sequence=$Sequence;kind=$Kind;value=$Value;settled=$Settled}}
}
function Get-PBThingUnits($Thing,$Ledger,$Problems) {
    if($null-eq$Thing){$Problems.Add('BG03 identity replay has null Thing.');return ,@()}
    $key=[string]$Thing.id
    if(-not$Ledger.ContainsKey($key) -or $Ledger[$key].Count-ne$Thing.count){$Problems.Add('BG03 unknown/count-mismatched live Thing '+$key);return ,@()}
    return ,@($Ledger[$key])
}
function Test-PBPhysicalBefore($Thing,$Physical,[string]$Context,$Problems) {
    if($null-eq$Thing){$Problems.Add('BG03 '+$Context+' has no captured physical Thing.');return}
    $key=[string]$Thing.id
    if(-not$Physical.ContainsKey($key) -or -not(Test-PBSame $Physical[$key] $Thing)){
        $Problems.Add('BG03 '+$Context+' contradicts the preceding physical identity/count/custody/map/cell history.')
    }
}
function Test-PBState($S,$Ledger,$UnitDefs,$Acquired,$Reentered,$Consumed,$Held,$Geometry,$InitialClean,[bool]$Settled,$Problems) {
    $seen=@{};$heldNow=@{};$ids=@{};$totals=@{};$expected=Get-PBExpected
    foreach($d in $expected.Keys){$totals[$d]=@{inventory=0;hands=0;floor=0}}
    if($S.meals-lt0 -or $S.meals-gt4 -or $S.billRemaining-lt0 -or $S.billRemaining-gt1 -or $S.cleanedTotal-lt$InitialClean){$Problems.Add('BG03 state product/bill/cleaning counts invalid.')}
    if($null-ne$Geometry -and $null-ne$Geometry.bench){$dx=[double]$S.x-$Geometry.bench.x;$dz=[double]$S.z-$Geometry.bench.z;$distance=[math]::Sqrt($dx*$dx+$dz*$dz);if([math]::Abs($distance-$S.benchDistance)-gt0.0001){$Problems.Add('BG03 bench distance is inconsistent with actual coordinates.')}}
    if($null-ne$Geometry -and ($S.x-lt0 -or $S.z-lt0 -or $S.x-ge$Geometry.width -or $S.z-ge$Geometry.depth)){$Problems.Add('BG03 pawn cell lies outside the actual map.')}
    $hands=0
    foreach($thing in $S.things){
        if($thing.id-le0 -or $ids.ContainsKey($thing.id) -or $thing.count-le0 -or $thing.destroyed -or $thing.custody-cnotin@('floor','inventory','hands') -or -not$expected.ContainsKey($thing.def)){$Problems.Add('BG03 live census repeats/invalidates Thing custody.');continue};$ids[$thing.id]=$true
        if(($thing.custody-ceq'floor')-ne$thing.spawned -or $null-eq$thing.mapId){$Problems.Add('BG03 spawned/map state contradicts custody.')}
        $owner=switch($thing.custody){floor{'Verse.Map'};inventory{'Verse.Pawn_InventoryTracker'};hands{'Verse.Pawn_CarryTracker'}}
        if($thing.holderType-cne$owner){$Problems.Add('BG03 actual holder type contradicts custody.')}
        if($thing.custody-ceq'hands'){$hands++}
        if($thing.custody-ceq'floor' -and $null-ne$Geometry -and ($thing.x-lt0 -or $thing.z-lt0 -or $thing.x-ge$Geometry.width -or $thing.z-ge$Geometry.depth)){$Problems.Add('BG03 floor Thing lies outside the actual map.')}
        $units=Get-PBThingUnits $thing $Ledger $Problems
        foreach($u in $units){$key=[string]$u;if($seen.ContainsKey($key) -or $Consumed.ContainsKey($key) -or $UnitDefs[$key]-cne$thing.def){$Problems.Add('BG03 live census aliases/changes/recovers consumed units.')};$seen[$key]=$true;if($thing.custody-cin@('inventory','hands')){$heldNow[$key]=$true}}
        $totals[$thing.def][$thing.custody]+=$thing.count
    }
    if($hands-gt1){$Problems.Add('BG03 physical hands contain more than one stack.')}
    if($Settled -and ($seen.Count+$Consumed.Count-ne40 -or -not(Test-PBIds @($heldNow.Keys|Sort-Object) @($Held.Keys|Sort-Object)))){$Problems.Add('BG03 settled custody/conservation does not match origin and transfer history.')}
    if($S.quantities.Count-ne3){$Problems.Add('BG03 state quantity catalog incomplete.')}
    foreach($def in $expected.Keys){
        $q=Get-PBOne $S.quantities def $def $Problems;if($null-eq$q){continue}
        $used=@($Consumed.Keys|Where-Object {$UnitDefs[$_]-ceq$def}).Count
        $initial=if($def-ceq'Milk'){12}else{0}
        if($q.initialHeld-ne$initial -or $q.initialFloor-ne($expected[$def]-$initial) -or $q.inventory-ne$totals[$def].inventory -or $q.hands-ne$totals[$def].hands -or $q.floor-ne$totals[$def].floor -or $q.uniqueAcquired-ne$Acquired[$def] -or $q.reenteredHeld-ne$Reentered[$def] -or $q.consumed-ne$used){$Problems.Add('BG03 state counters contradict replayed physical custody/history for '+$def)}
    }
    return ,$heldNow
}
function Test-PBReplay($Value,$Rows,$Problems) {
    $p=$Value.partialInventoryBill;$nodes=@{};$geometry=Get-PBGeometry $Value $Problems
    $ledger=@{};$defs=@{};$ever=@{};$held=@{};$consumed=@{};$acquired=@{Milk=0;RawRice=0;RawPotatoes=0};$reentered=@{Milk=0;RawRice=0;RawPotatoes=0}
    foreach($o in $p.origins){$ledger[[string]$o.thingId]=@($o.units);foreach($u in $o.units){$defs[[string]$u]=$o.def;if($o.initiallyHeld){$ever[[string]$u]=$true;$held[[string]$u]=$true}}}
    $states=@{};$settled=New-Object 'System.Collections.Generic.List[object]';$rawSequence=0;$lastInnerTick=$p.startedTick
    foreach($e in $Rows){
        $d=$e.data;$seq=0;$kind=$null
        switch -CaseSensitive($e.phase){
            'bg03-settled-state' {$seq=$d.sequence;$kind='state';$settled.Add($d)}
            'bg03-executing-job' {$seq=$d.sequence;$kind='executing'}
            'bg03-actual-split' {$seq=$d.sequence;$kind='ancestry'}
            'bg03-actual-merge' {$seq=$d.sequence;$kind='ancestry'}
            'bg03-actual-custody-transfer' {$seq=$d.sequence;$kind='transferEnd'}
            'bg03-native-ingredient-consumption' {$seq=$d.sequence;$kind='consume'}
            'bg03-seeded-filth-removal' {$seq=$d.sequence;$kind='clean'}
            'bg03-seeded-cleaning-increment' {$seq=$d.sequence;$kind='clean'}
            'bg03-unload-gate' {$seq=$d.sequence;$kind='gate'}
            'bg03-native-product-created-before-consumption' {$seq=$d.sequence;$kind='product'}
            'bg03-actual-job-cleanup' {$seq=$d.sequence;$kind='end'}
            'bg03-native-candidate-before-route' {$seq=$d.nativeSequence;$kind='candidateNative'}
            'bg03-candidate-after-route' {$seq=$d.routedSequence;$kind='candidateRouted'}
            'bg03-startjob-request-before-toils' {$seq=$d.sequence;$kind='request'}
            'bg03-actual-start-before-instant-toils' {$seq=$d.actualSequence;$kind='start'}
        }
        if($null-ne$kind){
            # Transfer and actual-start DTOs contain snapshots with later internal sequences;
            # the top-level callback order itself is nevertheless strictly increasing.
            if($seq-le$rawSequence -or $d.tick-lt$lastInnerTick){$Problems.Add('BG03 raw internal callback order regresses/repeats.')};$rawSequence=$seq;$lastInnerTick=$d.tick
            Add-PBNode $nodes $seq $kind $d ($kind-ceq'state') $Problems
        }
    }
    foreach($name in @('initial','departure','completeInventory','firstReturn','final')){if($null-ne$p.$name){Add-PBNode $nodes $p.$name.sequence state $p.$name ($name-cin@('initial','final')) $Problems}}
    foreach($t in $p.transfers){Add-PBNode $nodes $t.before.sequence transferBegin $t $false $Problems;Add-PBNode $nodes $t.after.sequence transferAfter $t $false $Problems}
    foreach($s in $p.starts){if($null-ne$s.actualCustody){Add-PBNode $nodes $s.actualCustody.sequence state $s.actualCustody $false $Problems}}
    if($settled.Count-ne$p.stateEvents){$Problems.Add('BG03 settled-state count differs from raw history.')}
    $beforeHeld=@{};$currentJob=$null;$currentDef=$null;$closed=@{};$executed=@{};$product=$null;$cleanCount=0;$lastClean=$p.initial.cleanedTotal;$lastTick=$p.startedTick
    $floorSplits=@{};$lastSettledSequence=$p.initial.sequence;$physicalThings=@{}
    $departure=$null;$complete=$null;$firstReturn=$null;$second=$false;$mapId=$null
    $receiptAfter=@{};foreach($t in $p.transfers){$receiptAfter[[string]$t.after.sequence]=$t}
    foreach($node in @($nodes.Values|Sort-Object sequence)){
        $d=$node.value;$kind=$node.kind;$s=$null
        if($kind-ceq'state'){$s=$d}
        elseif($kind-ceq'transferBegin'){$s=$d.before}
        elseif($kind-ceq'transferAfter'){
            $s=$d.after;$key=[string]$d.sequence
            if(-not$beforeHeld.ContainsKey($key)){$Problems.Add('BG03 transfer lacks its prior custody boundary.');continue}
            $afterHeld=Test-PBState $s $ledger $defs $acquired $reentered $consumed $held $geometry $p.initial.cleanedTotal $false (New-Object 'System.Collections.Generic.List[string]')
            $previous=$beforeHeld[$key];$fresh=@();$again=@();$left=@()
            foreach($u in @($afterHeld.Keys|Sort-Object {[int]$_})){if(-not$previous.ContainsKey($u)){if($consumed.ContainsKey($u)){$Problems.Add('BG03 consumed unit reenters held custody.')};if($ever.ContainsKey($u)){$again+=[int]$u;$reentered[$defs[$u]]++}else{$fresh+=[int]$u;$ever[$u]=$true;$acquired[$defs[$u]]++}}}
            foreach($u in @($previous.Keys|Sort-Object {[int]$_})){if(-not$afterHeld.ContainsKey($u)){$left+=[int]$u}}
            if(-not(Test-PBIds $fresh $d.newlyAcquiredUnits) -or -not(Test-PBIds $again $d.reenteredUnits) -or -not(Test-PBIds $left $d.leftHeldUnits) -or ($fresh.Count+$again.Count+$left.Count)-eq0){$Problems.Add('BG03 receipt unit sets disagree with physical before/after custody.')}
            foreach($u in $fresh){$witnesses=@();if($floorSplits.ContainsKey([string]$u)){$witnesses=@($floorSplits[[string]$u]|Where-Object {$_-gt$lastSettledSequence -and $_-lt$s.sequence})};if($witnesses.Count-ne1){$Problems.Add('BG03 newly acquired floor unit lacks its unique native split witness in this enclosing operation: '+$u)}}
            if($d.before.tick-ne$d.tick -or $s.tick-ne$d.tick -or $d.before.sequence-ge$s.sequence -or $s.sequence-ge$d.sequence -or $d.boundary-cnotin@('TryAdd','TryDrop','TryTransferToContainer','TryStartCarry','TryAbsorbStack')){$Problems.Add('BG03 receipt operation/capture boundaries invalid.')}
            if($left.Count-gt0 -and $s.benchDistance-gt3){foreach($q in $s.quantities){$old=@($d.before.quantities|Where-Object def -CEQ $q.def);if($old.Count-eq1 -and $q.floor-gt$old[0].floor){$Problems.Add('BG03 ingredient storage detour occurred outside bench region.')}}}
            $held=$afterHeld
            if($p.expectedBehavior-ceq'satisfied' -and $fresh.Count-gt0 -and ($d.before.jobDef-cne'HaulersDream_GatherBillIngredients' -or $s.jobId-ne$d.before.jobId -or $s.driver-cne'HaulersDream.JobDriver_GatherBillIngredients')){$Problems.Add('BG03 changed floor acquisition is not performed by the executing gather.')}
        }
        if($null-ne$s){
            if($s.tick-lt$lastTick -or $s.tick-lt$p.startedTick -or $s.tick-gt$p.finishedTick){$Problems.Add('BG03 snapshot ticks leave the scenario or reverse time.')};$lastTick=$s.tick
            $heldNow=Test-PBState $s $ledger $defs $acquired $reentered $consumed $held $geometry $p.initial.cleanedTotal $node.settled $Problems
            if($kind-ceq'transferBegin'){$beforeHeld[[string]$d.sequence]=$heldNow}
            $currentJob=$s.jobId;$currentDef=$s.jobDef
            foreach($thing in $s.things){$physicalThings[[string]$thing.id]=$thing}
            foreach($thing in $s.things){if($null-eq$mapId){$mapId=$thing.mapId};if($thing.mapId-ne$mapId){$Problems.Add('BG03 ingredient custody changes fixture map.')}}
            if($node.settled){
                $lastSettledSequence=$s.sequence
                if($s.cleanedTotal-lt$lastClean -or $s.cleanedTotal-$p.initial.cleanedTotal-lt$cleanCount){$Problems.Add('BG03 cleaned record is not monotonic or drops attributed increments.')};$lastClean=$s.cleanedTotal
                if($null-ne$product -and ($s.meals-ne4 -or $s.billRemaining-ne0 -or $s.things.Count-ne0 -or $consumed.Count-ne40)){$Problems.Add('BG03 settled state after product is not exactly conserved.')}
            }
            # Geometry calls occur for all emitted snapshots except the initial pre-work
            # snapshot, transfer prefix, and final snapshot. Prefixes never move the pawn.
            if($null-eq$product -and $kind-cne'transferBegin'){
                if($null-eq$departure -and $s.benchDistance-gt3){$departure=$s}
                if($null-ne$departure -and $null-eq$firstReturn -and $s.benchDistance-gt3 -and (Test-PBAllInventory $s) -and $null-eq$complete){$complete=$s}
                if($null-ne$departure -and $null-eq$firstReturn -and $s.benchDistance-le3 -and $s.things.Count-gt0){$firstReturn=$s}
                if($null-ne$firstReturn -and $s.benchDistance-gt3 -and $s.things.Count-gt0){$second=$true}
            }
            continue
        }
        switch -CaseSensitive($kind){
            'gate' {
                $milk=$d.initialMilk;$key=[string]$milk.id
                if(-not$ledger.ContainsKey($key) -or $ledger[$key].Count-ne$milk.count){$Problems.Add('BG03 passive milk snapshot contradicts actual unit ancestry/count.')}
                if(-not$physicalThings.ContainsKey($key) -or -not(Test-PBSame $physicalThings[$key] $milk)){$Problems.Add('BG03 passive original-milk custody differs from preceding physical ancestry/transfer/consumption.')}
                # Native/gather cleanup is completely observed. Other jobs can finish
                # between snapshots without a relevant-job cleanup callback.
                if($currentDef-cin@('DoBill','HaulersDream_GatherBillIngredients') -and $null-ne$currentJob -and -not$closed.ContainsKey([string]$currentJob)){
                    $driver=if($currentDef-ceq'DoBill'){'Verse.AI.JobDriver_DoBill'}else{'HaulersDream.JobDriver_GatherBillIngredients'}
                    if($null-eq$d.currentJob -or $d.currentJob.id-ne$currentJob -or $d.currentJob.def-cne$currentDef -or $d.currentDriver-cne$driver){$Problems.Add('BG03 passive gate omits or changes its proven live native/gather job and driver.')}
                }
                if($null-ne$d.currentJob -and ($d.currentJob.id-ne$currentJob -or $d.currentJob.def-cne$currentDef)){$Problems.Add('BG03 passive gate current job contradicts actual execution history.')}
                if($milk.count-gt0){Get-PBThingUnits $milk $ledger $Problems|Out-Null}else{if(-not$milk.destroyed -or $milk.custody-cne'destroyed'){$Problems.Add('BG03 empty original milk snapshot is not destroyed.')}}
                if($null-ne$d.workResultJob -and $d.workResultJob.recipe-ceq'CookMealSimpleBulk'){Test-PBLiveSelection $d.workResultJob $ledger $Problems}
            }
            'ancestry' {
                if(-not$d.valid -or $null-eq$d.sourceBefore -or $null-eq$d.sourceAfter -or $null-eq$d.targetAfter){$Problems.Add('BG03 invalid ancestry boundary.');continue}
                Test-PBPhysicalBefore $d.sourceBefore $physicalThings 'ancestry sourceBefore' $Problems
                if($null-ne$d.targetBefore){Test-PBPhysicalBefore $d.targetBefore $physicalThings 'ancestry targetBefore' $Problems}
                $source=Get-PBThingUnits $d.sourceBefore $ledger $Problems;$sourceKey=[string]$d.sourceBefore.id
                if($d.sourceBefore.id-ne$d.sourceAfter.id -or $d.sourceBefore.def-cne$d.sourceAfter.def -or $d.sourceBefore.def-cne$d.targetAfter.def){$Problems.Add('BG03 ancestry changes source identity/definition.')}
                if($d.operation-ceq'SplitOff'){
                    if($d.method-cnotin@('Verse.Thing.SplitOff','Verse.ThingWithComps.SplitOff') -or $d.requested-le0 -or $d.requested-gt$d.sourceBefore.count -or $null-ne$d.targetBefore -or $null-ne$d.nativeResult -or $d.targetAfter.destroyed){$Problems.Add('BG03 split binding/arguments invalid.')}
                    if($d.targetAfter.id-eq$d.sourceBefore.id){$moved=$source;if($d.targetAfter.count-ne$d.sourceBefore.count -or $d.sourceAfter.count-ne$d.sourceBefore.count -or $d.requested-ne$d.sourceBefore.count){$Problems.Add('BG03 whole split altered the original count.')}}else{
                        $count=$d.targetAfter.count;$targetKey=[string]$d.targetAfter.id
                        if($count-le0 -or $count-ne$d.requested -or $d.sourceAfter.count+$count-ne$d.sourceBefore.count -or ($ledger.ContainsKey($targetKey) -and $ledger[$targetKey].Count-gt0)){$Problems.Add('BG03 split child/count equation invalid.');continue}
                        $moved=@($source|Select-Object -Last $count);$ledger[$sourceKey]=@($source|Select-Object -First ($source.Count-$count));$ledger[$targetKey]=$moved
                    }
                }elseif($d.operation-ceq'TryAbsorbStack'){
                    if($d.method-cnotin@('Verse.Thing.TryAbsorbStack','Verse.ThingWithComps.TryAbsorbStack') -or $null-eq$d.targetBefore -or $null-eq$d.nativeResult -or $d.requested-ne0){$Problems.Add('BG03 merge binding/arguments invalid.');continue}
                    $target=Get-PBThingUnits $d.targetBefore $ledger $Problems;$targetKey=[string]$d.targetBefore.id;$growth=$d.targetAfter.count-$d.targetBefore.count;$removed=$d.sourceBefore.count-$d.sourceAfter.count
                    if($d.targetAfter.id-ne$d.targetBefore.id -or $d.targetBefore.def-cne$d.sourceBefore.def -or $targetKey-ceq$sourceKey -or $growth-lt0 -or $growth-ne$removed -or $growth-gt$source.Count -or $d.nativeResult-ne$d.sourceAfter.destroyed -or ($d.sourceAfter.destroyed -and $d.sourceAfter.count-ne0)){$Problems.Add('BG03 merge growth/destruction equation invalid.');continue}
                    $moved=@($source|Select-Object -First $growth);$ledger[$sourceKey]=@($source|Select-Object -Skip $growth);$ledger[$targetKey]=@($target)+@($moved)
                }else{$Problems.Add('BG03 unsupported ancestry operation.');continue}
                $physicalThings[[string]$d.sourceAfter.id]=$d.sourceAfter;$physicalThings[[string]$d.targetAfter.id]=$d.targetAfter
                if(-not(Test-PBIds $moved $d.movedUnits)){$Problems.Add('BG03 ancestry moved-unit list contradicts native count equations.')}
                if($d.operation-ceq'SplitOff' -and $d.sourceBefore.custody-ceq'floor' -and $d.sourceBefore.spawned -and $d.sourceBefore.holderType-ceq'Verse.Map' -and $d.sourceBefore.mapId-eq$mapId){foreach($u in $moved){$key=[string]$u;if(-not$floorSplits.ContainsKey($key)){$floorSplits[$key]=@()};$floorSplits[$key]+=$d.sequence}}
            }
            'executing' {
                if($null-eq$d.actual -or $d.actual.id-le0 -or $executed.ContainsKey([string]$d.actual.id)){$Problems.Add('BG03 executed job catalog repeats/omits actual identity.');continue}
                $starts=@($p.starts|Where-Object {$null-ne$_.actual -and $_.actual.id-eq$d.actual.id})
                if($starts.Count-gt0 -or $d.actual.def-cin@('DoBill','HaulersDream_GatherBillIngredients')){
                    if($starts.Count-ne1){$Problems.Add('BG03 native/gather execution has no unique actual start.')}else{
                        $start=$starts[0]
                        if($d.sequence-le$start.actualCustody.sequence -or $d.tick-ne$start.tick -or $d.actualDriver-cne$start.actualDriver -or -not(Test-PBSame $d.actual $start.actual)){
                            $Problems.Add('BG03 native/gather executing row contradicts its actual-start job, workgiver, recipe, selection or driver.')
                        }
                    }
                }
                # ObserveActualJob emits only sequence, tick, actual job and driver.
                # The remaining Start DTO fields must retain their default values.
                if($d.actualSequence-ne0 -or $null-ne$d.candidateId -or $null-ne$d.previous -or $null-ne$d.requested -or $null-ne$d.previousDriver -or $null-ne$d.previousToilInit -or $null-ne$d.previousExecutedGatherId -or $null-ne$d.lastJobEndCondition -or $null-ne$d.actualDriverAssembly -or $null-ne$d.actualDriverMvid -or $null-ne$d.actualCustody){$Problems.Add('BG03 executing-job observation fabricates unrecorded StartJob fields.')}
                $executed[[string]$d.actual.id]=$d.actual;$currentJob=$d.actual.id;$currentDef=$d.actual.def
            }
            'start' {$currentJob=$d.actual.id;$currentDef=$d.actual.def;Test-PBLiveSelection $d.actual $ledger $Problems}
            'request' {if($null-ne$d.requested -and $d.requested.recipe-ceq'CookMealSimpleBulk'){Test-PBLiveSelection $d.requested $ledger $Problems};if($null-ne$d.previous -and $d.previous.def-ceq'HaulersDream_GatherBillIngredients'){Test-PBLiveSelection $d.previous $ledger $Problems}}
            'candidateNative' {if($null-ne$d.native){Test-PBLiveSelection $d.native $ledger $Problems}}
            'candidateRouted' {if($null-ne$d.routed){Test-PBLiveSelection $d.routed $ledger $Problems}}
            'product' {if($null-ne$product -or $d.jobId-ne$currentJob -or $currentDef-cne'DoBill' -or -not$executed.ContainsKey([string]$currentJob) -or $closed.ContainsKey([string]$currentJob) -or $consumed.Count-ne0 -or $cleanCount-ne3){$Problems.Add('BG03 product is outside the live native job/cleaning/consumption boundary.')};$product=$d}
            'clean' {if($null-ne$product -or $d.jobId-ne$currentJob -or $currentDef-cne'DoBill' -or $closed.ContainsKey([string]$currentJob)){$Problems.Add('BG03 seeded cleaning is outside the live pre-product native job.')};if($d.operation-ceq'record-increment'){$cleanCount++}}
            'consume' {
                if($null-eq$d.before -or $null-eq$d.after){$Problems.Add('BG03 native consumption states absent.');continue}
                Test-PBPhysicalBefore $d.before $physicalThings 'consumption.before' $Problems
                $units=Get-PBThingUnits $d.before $ledger $Problems
                if($null-eq$product -or $d.jobId-ne$currentJob -or $currentDef-cne'DoBill' -or $closed.ContainsKey([string]$currentJob) -or -not(Test-PBIds $units $d.units)){$Problems.Add('BG03 consumption is outside native lifetime or has false unit ancestry.')}
                foreach($u in $units){$key=[string]$u;if($consumed.ContainsKey($key)){$Problems.Add('BG03 repeats consumed origin unit.')};$consumed[$key]=$true;$held.Remove($key)};$ledger[[string]$d.before.id]=@();$physicalThings[[string]$d.after.id]=$d.after
            }
            'end' {if($d.jobId-ne$currentJob -or $d.jobDef-cne$currentDef -or $closed.ContainsKey([string]$d.jobId)){$Problems.Add('BG03 cleanup occurs outside its actual job lifetime.')};$closed[[string]$d.jobId]=$d;if($d.jobDef-ceq'DoBill' -and $consumed.Count-ne40){$Problems.Add('BG03 native cleanup predates complete consumption.')};$currentJob=$null;$currentDef=$null}
        }
    }
    foreach($pair in @(@($departure,$p.departure,'departure'),@($complete,$p.completeInventory,'complete inventory'),@($firstReturn,$p.firstReturn,'first return'))){if(-not(Test-PBSame $pair[0] $pair[1])){$Problems.Add('BG03 reported '+$pair[2]+' is not the first reconstructed actual boundary.')}}
    if($null-eq$departure -or $null-eq$firstReturn -or $departure.tick-ne$p.departureTick -or $firstReturn.tick-ne$p.firstReturnTick){$Problems.Add('BG03 required departure/return witness missing or mistimed.')}
    $changed=$p.expectedBehavior-ceq'satisfied'
    if($changed -and ($null-eq$complete -or -not(Test-PBAllInventory $firstReturn) -or $second -or @($reentered.Values|Where-Object {$_-ne0}).Count-gt0)){$Problems.Add('BG03 changed trace lacks the complete single sweep/no-reentry result.')}
    if(-not$changed -and ((Test-PBAllInventory $firstReturn) -or $null-ne$complete)){$Problems.Add('BG03 baseline does not exhibit an incomplete first return.')}
    if($acquired.Milk-ne0 -or $acquired.RawRice-ne14 -or $acquired.RawPotatoes-ne14 -or $consumed.Count-ne40){$Problems.Add('BG03 final unit acquisition/consumption is incorrect.')}
    $ticks=@($settled|Where-Object reason -CEQ game-tick|Select-Object -ExpandProperty tick -Unique|Sort-Object)
    # Setup occurs in Update after its tick's GameComponent callback, so game-tick starts next tick.
    if($ticks.Count-ne([long]$p.finishedTick-$p.startedTick)){$Problems.Add('BG03 raw game-tick coverage is incomplete.')}
    for($i=0;$i-lt$ticks.Count;$i++){if($ticks[$i]-ne($p.startedTick+1+$i)){$Problems.Add('BG03 raw game-tick sequence has a gap.');break}}
    $nativeEnds=@($p.ends|Where-Object jobDef -CEQ DoBill);$cleanupSequence=if($nativeEnds.Count-eq1){$nativeEnds[0].sequence}else{[int]::MaxValue}
    $stable=@($settled|Where-Object {$_.tick-ge$p.stableSinceTick -and $_.sequence-gt$cleanupSequence});$stableTicks=@($stable|Select-Object -ExpandProperty tick -Unique|Sort-Object)
    if($stableTicks.Count-ne$p.stableTickCount -or $stableTicks.Count-ne([long]$p.finishedTick-$p.stableSinceTick+1) -or $stableTicks.Count-lt301){$Problems.Add('BG03 stable tick count/window does not match raw settled history.')}
    for($i=0;$i-lt$stableTicks.Count;$i++){if($stableTicks[$i]-ne($p.stableSinceTick+$i)){$Problems.Add('BG03 stable window has a missing actual tick.');break}}
    foreach($s in $stable){if($s.meals-ne4 -or $s.billRemaining-ne0 -or $s.things.Count-ne0 -or @($s.quantities|Where-Object {$_.inventory-ne0 -or $_.hands-ne0 -or $_.floor-ne0 -or $_.consumed-ne($_.initialHeld+$_.initialFloor)}).Count-gt0){$Problems.Add('BG03 stable boundary is not zero ingredients/four meals/completed bill.');break}}
    if($null-ne$product){$nativeEnd=@($p.ends|Where-Object jobId -EQ $product.jobId);if($nativeEnd.Count-eq1){$firstStable=@($settled|Where-Object {$_.sequence-gt$nativeEnd[0].sequence -and $_.things.Count-eq0 -and $_.meals-eq4 -and $_.billRemaining-eq0}|Select-Object -First 1);if($firstStable.Count-ne1 -or $firstStable[0].tick-ne$p.stableSinceTick){$Problems.Add('BG03 stable start does not match the first settled post-cleanup boundary.')}}}
}
function Test-PBLiveSelection($Job,$Ledger,$Problems) {
    foreach($row in $Job.selection){if($null-eq$row.thing -or $null-eq$row.selected -or $row.selected-lt0){$Problems.Add('BG03 live selection has absent identity/count.');continue};$units=Get-PBThingUnits $row.thing $Ledger $Problems;if(-not(Test-PBIds @($units|Select-Object -First $row.selected) $row.units)){$Problems.Add('BG03 captured selection unit IDs do not match ancestry at that historical boundary.')}}
}
function Test-PartialBillShape($Value,$Problems) {
    $savedPreference=$ErrorActionPreference;$ErrorActionPreference='Stop'
    try{return [bool](Test-PBShapeCore $Value $Problems)}catch{$Problems.Add('BG03 shape rejected malformed evidence: '+$_.Exception.Message);return $false}finally{$ErrorActionPreference=$savedPreference}
}
function Test-PartialBillEvidence($Value,[string]$ExpectedBehavior,$Problems) {
    $savedPreference=$ErrorActionPreference;$ErrorActionPreference='Stop'
    try{Test-PBEvidenceCore $Value $ExpectedBehavior $Problems}catch{$Problems.Add('BG03 result rejected malformed evidence: '+$_.Exception.Message)}finally{$ErrorActionPreference=$savedPreference}
}
function Test-PartialBillEvents($Value,$Events,[string]$RunId,$Problems) {
    $savedPreference=$ErrorActionPreference;$ErrorActionPreference='Stop'
    try{Test-PBEventsCore $Value $Events $RunId $Problems}catch{$Problems.Add('BG03 events rejected malformed evidence: '+$_.Exception.Message)}finally{$ErrorActionPreference=$savedPreference}
}
# Fresh-cargo observations are read as captured; these functions never invoke gameplay gates.
function Get-PBFreshModule($Value,$Problems) {
    $a=Get-PBOne $Value.assemblies name HaulersDream $Problems
    if($null-eq$a){return $null}
    # Read independently with ReflectionOnlyLoadFrom, one isolated process per actual module.
    $catalog=@{
        'D55644ACBE064F4F6A37C82205F48EEAF61264100C92AC568BDCB612FE8DCC2D'=@('05629205-9454-4960-bd33-8ef04c74d4b2',100663413,100664560,100664561)
        'CE0508AF4058C0414DDC9F83142F9708B66EC43A44CD1BD74E1E1553CB37F604'=@('e52f9ce7-fd03-4a99-895a-858063863298',100663419,100664581,100664582)
    }
    if(-not$catalog.ContainsKey($a.sha256) -or $catalog[$a.sha256][0]-cne$a.moduleVersionId){$Problems.Add('BG03 fresh-cargo method tokens require independent review of this exact HD module.');return $null}
    return @{assembly=$a;notify=$catalog[$a.sha256][1];inJob=$catalog[$a.sha256][2];entering=$catalog[$a.sha256][3]}
}
function Test-PBFreshBindings($Value,$Rows,$Problems) {
    $m=Get-PBFreshModule $Value $Problems
    if($null-ne$m){foreach($pair in @(@('IsInDowntimeJob',$m.inJob),@('IsEnteringDowntime',$m.entering))){
        $detail='HaulersDream.OpportunisticUnload.'+$pair[0]+'; token='+$pair[1]+'; mvid='+$m.assembly.moduleVersionId+'; originalOutParameters=; hooks=/DowntimeReturned/; writes=__state only'
        if(@($Rows|Where-Object {$_.phase-ceq'bg03-observer-binding' -and $_.detail-ceq$detail}).Count-ne1){$Problems.Add('BG03 exact HD returned-gate observer binding is absent or changed: '+$pair[0])}
    }}
    $order=Get-PBOne $Rows phase bg03-work-postfix-order $Problems
    if($null-ne$order -and $order.detail-cne'HaulersDream.RuntimeHarness.Bg03Observers.WorkResultBeforeUnload | HaulersDream.Patch_JobGiver_Work_OpportunisticUnload.Postfix | HaulersDream.Patch_OpportunisticLoadDeposit.Postfix | HaulersDream.RuntimeHarness.Bg03Observers.WorkResultAfterUnload'){$Problems.Add('BG03 work-result sorted postfix catalog/order is unreviewed or does not bracket actual HD unloading.')}
}
function Test-PBFreshCargo($Value,$Problems) {
    $p=$Value.partialInventoryBill;$f=$p.freshCargo;$gates=$p.unloadGates
    $milk=Get-PBOne $p.origins def Milk $Problems;$m=Get-PBFreshModule $Value $Problems
    if($null-eq$milk){return}
    if($f.origin-cne'synthetic fixture setup; no observed prior haul' -or $f.milkId-ne$milk.thingId -or $f.tick-ne$p.startedTick -or $f.notifiedYieldTick-ne$f.tick -or $f.previousYieldTick-ne-99999){$Problems.Add('BG03 one-time synthetic pickup clock/original milk provenance is false.')}
    if($null-ne$m -and $f.notificationBinding-cne('HaulersDream.CompHauledToInventory.NotifyYieldPicked; token='+$m.notify+'; mvid='+$m.assembly.moduleVersionId)){$Problems.Add('BG03 pickup notification binding does not match the exact loaded method.')}
    $expected=@{
        'passive-field-lastYieldTick'='HaulersDream.CompHauledToInventory.lastYieldTick; expected=System.Int32; read backing field only'
        'passive-field-unloadGraceTicks'='HaulersDream.HaulersDreamSettings.unloadGraceTicks; expected=System.Int32; read backing field only'
        'passive-field-unloadBeforeEating'='HaulersDream.HaulersDreamSettings.unloadBeforeEating; expected=System.Boolean; read backing field only'
        'passive-field-unloadBeforeSleep'='HaulersDream.HaulersDreamSettings.unloadBeforeSleep; expected=System.Boolean; read backing field only'
        'passive-field-unloadBeforeLeisure'='HaulersDream.HaulersDreamSettings.unloadBeforeLeisure; expected=System.Boolean; read backing field only'
        'passive-field-unloadEverything'='Verse.Pawn_InventoryTracker.unloadEverything; expected=System.Boolean; read backing field only'
        'raw-need-binding'='RimWorld.Need.curLevelInt; raw units, no category/percentage/stat getter.'
        'fresh-pickup-binding'='Actual public NotifyYieldPicked(), called once during synthetic initial setup.'
        'fresh-pickup-clock'=('Actual notification stamps the setup tick; previous='+$f.previousYieldTick+'; now='+$f.notifiedYieldTick)
    }
    foreach($id in $expected.Keys){$a=Get-PBOne $Value.assertions id ('fixture-bg03p1-'+$id) $Problems;if($null-ne$a -and ($a.observed-cne$expected[$id] -or -not$a.passed)){$Problems.Add('BG03 passive/fresh setup assertion does not match its actual field or call: '+$id)}}
    $initial=Get-PBOne $gates boundary initial-before-automatic-work $Problems
    $physical=Get-PBOne $p.initial.things id $milk.thingId $Problems
    if($null-ne$initial -and ($initial.sequence-ne($p.initial.sequence+1) -or $initial.tick-ne$p.startedTick -or -not(Test-PBSame $initial.initialMilk $physical) -or -not$initial.initialMilkTagged -or $initial.lastYieldTick-ne$f.notifiedYieldTick -or $initial.queueCount-ne0 -or $initial.rawUnloadEverything -or $null-ne$initial.currentJob -or $null-ne$initial.currentDriver -or $initial.foodRawUnits-ne1 -or $initial.restRawUnits-ne1 -or $initial.joyRawUnits-ne1)){$Problems.Add('BG03 initial passive snapshot differs from the exact fresh setup state.')}
    $frames=@{};$stack=New-Object 'System.Collections.Generic.List[int]';$nextCall=1;$lastSeq=0;$lastTick=$p.startedTick
    $boundaries=@('initial-before-automatic-work','work-call-entry','work-result-before-hd-unload','work-result-after-hd-unload','IsInDowntimeJob-actual-return','IsEnteringDowntime-actual-return','native-candidate-before-route')
    foreach($g in $gates){
        if($g.sequence-le$lastSeq -or $g.tick-lt$lastTick -or $g.tick-gt$p.finishedTick -or $g.boundary-cnotin$boundaries){$Problems.Add('BG03 passive gate capture order/tick/boundary is invalid.')};$lastSeq=$g.sequence;$lastTick=$g.tick
        if($g.queueCount-lt0 -or $g.queueCount-gt32 -or $g.queue.Count-ne$g.queueCount -or $g.queueOverflow -or $g.fullTimetable.Count-ne24 -or @($g.fullTimetable|Where-Object {$_-cne'Work'}).Count-gt0 -or $g.graceTicks-le0 -or $g.lastYieldTick-lt$f.notifiedYieldTick -or $g.lastYieldTick-gt$g.tick){$Problems.Add('BG03 passive queue/timetable/grace/clock snapshot is incomplete or invalid.')}
        if($null-ne$initial){foreach($n in @('graceTicks','beforeEating','beforeSleep','beforeLeisure')){if($g.$n-ne$initial.$n){$Problems.Add('BG03 passive unload setting changes during the fixture: '+$n)}}}
        if($g.initialMilk.id-ne$milk.thingId -or $g.initialMilk.def-cne'Milk' -or $g.initialMilk.count-lt0 -or $g.initialMilk.count-gt12 -or $g.initialMilk.custody-cnotin@('inventory','hands','floor','destroyed') -or ($g.initialMilk.destroyed-ne($g.initialMilk.custody-ceq'destroyed')) -or ($g.initialMilk.spawned-ne($g.initialMilk.custody-ceq'floor'))){$Problems.Add('BG03 passive original-milk identity/custody is invalid.')}
        if($g.initialMilk.count-gt0 -and $g.initialMilk.mapId-ne$physical.mapId){$Problems.Add('BG03 passive milk leaves the actual fixture map.')}
        if(($null-eq$g.currentJob)-ne($null-eq$g.currentDriver)){$Problems.Add('BG03 passive current job/driver nullability disagrees.')}
        $queueIds=@{};foreach($j in $g.queue){if($null-ne$j){if($null-eq$j.id -or $j.id-le0 -or $queueIds.ContainsKey($j.id)){$Problems.Add('BG03 passive queue repeats/omits a real job identity.')};$queueIds[$j.id]=$true}}
        if($g.boundary-ceq'work-call-entry'){
            if($null-eq$g.workCall -or $g.workCall-ne$nextCall -or $frames.ContainsKey([string]$g.workCall) -or $null-eq$g.emergency){$Problems.Add('BG03 work-call entry IDs are not consecutive or lack emergency provenance.')}
            $nextCall++;$frames[[string]$g.workCall]=@{entry=$g;before=$null;after=$null;returns=(New-Object 'System.Collections.Generic.List[object]');candidates=(New-Object 'System.Collections.Generic.List[object]')};$stack.Add([int]$g.workCall)
        }elseif($g.boundary-cin@('work-result-before-hd-unload','work-result-after-hd-unload')){
            if($null-eq$g.workCall -or -not$frames.ContainsKey([string]$g.workCall) -or $stack.Count-eq0 -or $stack[$stack.Count-1]-ne$g.workCall){$Problems.Add('BG03 work-result bracket is outside its contiguous live work frame.');continue}
            $frame=$frames[[string]$g.workCall]
            if($g.emergency-ne$frame.entry.emergency -or $g.tick-ne$frame.entry.tick -or $null-eq$g.workResultValid -or $g.workResultValid-ne($null-ne$g.workResultJob)){$Problems.Add('BG03 work-result validity/job/emergency/tick correlation failed.')}
            if($null-ne$g.workResultJob -and $g.workResultJob.recipe-ceq'CookMealSimpleBulk'){
                $chosen=@($frame.candidates|Where-Object {$null-ne$_.routed -and $_.routed.id-eq$g.workResultJob.id})
                if($chosen.Count-ne1){$Problems.Add('BG03 returned bill package lacks its routed candidate in this live work frame.')}else{Test-PBSelectionSame $chosen[0].routed $g.workResultJob $Problems}
                if($g.workResultJob.workGiver-cne'DoBillsCook' -or $g.workResultJob.workGiverClass-cne'RimWorld.WorkGiver_DoBill' -or $g.workResultJob.playerForced){$Problems.Add('BG03 actual returned bill package has false ordinary workgiver provenance.')}
            }
            if($g.boundary-ceq'work-result-before-hd-unload'){if($null-ne$frame.before){$Problems.Add('BG03 duplicate before-HD work-result bracket.')};$frame.before=$g}
            else{if($null-eq$frame.before -or $null-ne$frame.after){$Problems.Add('BG03 missing before-HD or duplicated after-HD result.')};$frame.after=$g;$stack.RemoveAt($stack.Count-1)}
        }elseif($g.boundary-ceq'native-candidate-before-route'){
            if($stack.Count-eq0){$Problems.Add('BG03 native candidate has no live ordinary work-call frame.')}else{
                $frame=$frames[[string]$stack[$stack.Count-1]]
                if($null-ne$frame.before -or $frame.entry.emergency -or $frame.entry.tick-ne$g.tick){$Problems.Add('BG03 candidate is not inside the actual ordinary work scan before its returned result.')}
                $c=Get-PBOne $p.candidates id $g.candidateId $Problems;if($null-ne$c){$frame.candidates.Add($c)}
            }
        }elseif($g.boundary-cin@('IsInDowntimeJob-actual-return','IsEnteringDowntime-actual-return')){
            if($null-eq$g.returnedBoolean){$Problems.Add('BG03 actual downtime callback lacks its returned Boolean.')}
            if($null-eq$g.workCall){if($null-ne$g.emergency -or $stack.Count-ne0){$Problems.Add('BG03 unframed returned gate is nested in an active work frame.')}}
            elseif(-not$frames.ContainsKey([string]$g.workCall) -or $stack.Count-eq0 -or $stack[$stack.Count-1]-ne$g.workCall){$Problems.Add('BG03 returned downtime gate references a missing/closed/noncurrent work frame.')}
            else{$frame=$frames[[string]$g.workCall];if($g.emergency-ne$frame.entry.emergency -or $g.tick-ne$frame.entry.tick){$Problems.Add('BG03 returned downtime gate changes frame emergency/tick.')};$frame.returns.Add($g)}
        }
        if($g.boundary-cnotin@('IsInDowntimeJob-actual-return','IsEnteringDowntime-actual-return') -and $null-ne$g.returnedBoolean){$Problems.Add('BG03 non-return snapshot fabricates a gate Boolean.')}
        if($g.boundary-cnotin@('work-result-before-hd-unload','work-result-after-hd-unload') -and ($null-ne$g.workResultValid -or $null-ne$g.workResultJob)){$Problems.Add('BG03 non-result snapshot fabricates a returned work package.')}
        if($g.boundary-cin@('initial-before-automatic-work','native-candidate-before-route') -and ($null-ne$g.workCall -or $null-ne$g.emergency)){$Problems.Add('BG03 unframed passive boundary fabricates work-call metadata.')}
        if(($g.boundary-ceq'native-candidate-before-route')-ne($null-ne$g.candidateId)){$Problems.Add('BG03 passive candidate link has wrong boundary/nullability.')}
    }
    if($stack.Count-ne0){$Problems.Add('BG03 work frame remains open at scenario completion.')}
    foreach($frame in $frames.Values){if($null-eq$frame.before -or $null-eq$frame.after){$Problems.Add('BG03 work-call lifetime lacks one of its returned result brackets.')}}
    $candidateGates=@($gates|Where-Object boundary -CEQ native-candidate-before-route)
    if($candidateGates.Count-ne$p.candidates.Count){$Problems.Add('BG03 passive candidate snapshot catalog is incomplete.')}
    foreach($c in $p.candidates){$g=Get-PBOne $candidateGates candidateId $c.id $Problems;if($null-ne$g){if($g.sequence-ne($c.nativeSequence+1) -or $c.routedSequence-ne($g.sequence+1) -or $g.tick-ne$c.tick){$Problems.Add('BG03 candidate passive snapshot is not between its actual native and routed boundaries.')}}}
    $first=@($p.candidates|Where-Object {$null-ne$_.native}|Sort-Object nativeSequence|Select-Object -First 1)
    $coverage=$false;$protected=$false;$fresh=$false
    if($first.Count-eq1){
        $snapshot=Get-PBOne $candidateGates candidateId $first[0].id $Problems
        foreach($frame in $frames.Values){if($null-eq$frame.before -or $null-eq$frame.after){continue};foreach($g in $frame.returns){
            if($g.boundary-ceq'IsEnteringDowntime-actual-return' -and $frame.before.sequence-lt$g.sequence -and $g.sequence-lt$frame.after.sequence -and $frame.after.sequence-lt$first[0].nativeSequence -and $frame.before.workResultValid-eq$false -and $null-eq$frame.before.workResultJob){
                $coverage=$true
                if($g.returnedBoolean-eq$false -and $g.lastYieldTick-eq$f.notifiedYieldTick -and ([long]$g.tick-$g.lastYieldTick)-ge0 -and ([long]$g.tick-$g.lastYieldTick)-lt$g.graceTicks -and $frame.after.workResultValid-eq$false -and $null-eq$frame.after.workResultJob){$protected=$true}
            }
        }}
        if($null-ne$snapshot){$selection=@($first[0].native.selection|Where-Object {$null-ne$_.thing -and $_.thing.id-eq$milk.thingId});$fresh=$protected -and $snapshot.lastYieldTick-eq$f.notifiedYieldTick -and ([long]$snapshot.tick-$snapshot.lastYieldTick)-ge0 -and ([long]$snapshot.tick-$snapshot.lastYieldTick)-lt$snapshot.graceTicks -and -not$snapshot.rawUnloadEverything -and @($snapshot.queue|Where-Object {$null-ne$_ -and $_.def-ceq'HaulersDream_UnloadInventory'}).Count-eq0 -and $snapshot.initialMilk.count-eq12 -and $snapshot.initialMilk.custody-ceq'inventory' -and $snapshot.initialMilk.holderType-ceq'Verse.Pawn_InventoryTracker' -and $snapshot.initialMilkTagged -and $selection.Count-eq1 -and $selection[0].selected-eq12 -and $selection[0].tagged -and (Test-PBSame $selection[0].thing $snapshot.initialMilk) -and (Test-PBIds $selection[0].units $milk.units)}
    }
    if($p.initialWorkGateObserved-ne$coverage){$Problems.Add('BG03 initial work-gate coverage Boolean contradicts complete actual callback brackets.')}
    $coverageRow=Get-PBOne $p.assertions id initial-work-gate-observed $Problems;$freshRow=Get-PBOne $p.assertions id fresh-inventory-milk-selected $Problems
    if($null-ne$coverageRow -and $coverageRow.passed-ne$coverage){$Problems.Add('BG03 initial gate coverage assertion contradicts the captured callback.')}
    if($null-ne$freshRow -and $freshRow.passed-ne$fresh){$Problems.Add('BG03 fresh-inventory assertion contradicts original milk/clock/selection/protected-miss evidence.')}
    # A true gate/unload is observed behavior; only absent initial callback coverage is inconclusive.
    if(-not$coverage){if($p.status-cne'inconclusive' -or $Value.status-cne'inconclusive'){$Problems.Add('BG03 absent initial gate coverage must remain inconclusive for either build.')};$Problems.Add('BG03 required initial actual gate coverage is missing; acceptance remains inconclusive.')}
    if(-not$fresh){$Problems.Add('BG03 observed behavior lacks the protected fresh original-milk selection required for P1 acceptance.')}
}
function Test-PBFreshEvents($Value,$Rows,$Problems) {
    $p=$Value.partialInventoryBill
    Test-PBEventCopy $Rows bg03-synthetic-fresh-cargo @($p.freshCargo) $Problems
    Test-PBEventCopy $Rows bg03-unload-gate $p.unloadGates $Problems
    $fresh=Get-PBOne $Rows phase bg03-synthetic-fresh-cargo $Problems;$initial=@($Rows|Where-Object {$_.phase-ceq'bg03-unload-gate' -and $_.data.boundary-ceq'initial-before-automatic-work'})
    $tag=Get-PBOne $Rows phase bg03-initial-tag-setup $Problems;$ready=Get-PBOne $Rows phase fixture-ready $Problems
    $setup=@($Rows|Where-Object {$_.phase-ceq'assertion' -and $_.detail.StartsWith('fixture-bg03p1-setup-completed: passed; ')})
    $binding=@($Rows|Where-Object phase -CEQ bg03-observer-binding)
    if($null-ne$fresh -and $null-ne$tag -and $null-ne$ready -and $initial.Count-eq1 -and $setup.Count-eq1){
        if($fresh.tick-ne$p.startedTick -or $fresh.sequence-ge$tag.sequence -or $tag.sequence-ge$ready.sequence -or $ready.sequence-ge$initial[0].sequence -or $initial[0].sequence-ge$setup[0].sequence -or @($binding|Where-Object sequence -GE $initial[0].sequence).Count-gt0){$Problems.Add('BG03 fresh setup/binding/initial passive capture lifecycle is invalid.')}
        foreach($r in @($Rows|Where-Object phase -CEQ bg03-unload-gate)){if($r.data.boundary-cne'initial-before-automatic-work' -and $r.sequence-le$setup[0].sequence){$Problems.Add('BG03 actual work/gate observation predates completed setup.')}}
    }else{$Problems.Add('BG03 fresh setup/initial capture lifecycle is incomplete.')}
    $gates=@($Rows|Where-Object phase -CEQ bg03-unload-gate)
    foreach($row in @($Rows|Where-Object phase -CEQ bg03-native-candidate-before-route)){
        $g=@($gates|Where-Object {$_.data.boundary-ceq'native-candidate-before-route' -and $_.data.candidateId-eq$row.data.id})
        $r=@($Rows|Where-Object {$_.phase-ceq'bg03-candidate-after-route' -and $_.data.id-eq$row.data.id})
        if($g.Count-ne1 -or $r.Count-ne1 -or $g[0].sequence-ne($row.sequence+1) -or $r[0].sequence-ne($g[0].sequence+1)){$Problems.Add('BG03 native candidate/passive gate/routed event callbacks are not contiguous.')}
    }
}
