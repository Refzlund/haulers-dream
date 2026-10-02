# CAP03-A consumer. Uses immutable primitive/cost/metadata helpers from the accepted B leaf;
# it never adapts an A result into another case DTO or modifies any input evidence.
. (Join-Path $PSScriptRoot 'runtime-validation-asf-budget.ps1')

function Assert-AF($Condition,[string]$Label,$Problems){if(-not$Condition){$Problems.Add('CAP03-A '+$Label)}}
function Test-AFSettingsShape($Value,$Problems){
    if(Test-ABFields $Value @{settingsIdentity='string';filterIdentity='string';ownerIdentity='nullable-string';ownerType='nullable-string';priority='string';contents='object'} 'CAP03-A settings' $Problems){Test-ABFilterShape $Value.contents $Problems}
}
function Test-AFCounterShape($Value,$Problems){Test-ABFields $Value @{constructed='long';matches='long';alwaysMatches='long';canEverMatch='long';workerExists='bool';actualWorkerType='nullable-string';workerIdentity='nullable-string'} 'CAP03-A counter' $Problems|Out-Null}
function Test-AFSnapshotShape($Value,$Problems){
    if(-not(Test-ABFields $Value @{tick='int';mapId='int';groupLoadId='int';groupIdentity='nullable-string';memberOrder='array';groupEffective='nullable-object';groupFixed='nullable-object';members='array';parcels='array';actor='object';actorIdle='bool';groupRegistered='bool'} 'CAP03-A physical snapshot' $Problems)){return}
    foreach($id in $Value.memberOrder){Assert-AF (($id-is[int] -or $id-is[long]) -and $id-gt0 -and $id-le[int]::MaxValue) 'invalid member ID.' $Problems}
    foreach($name in @('groupEffective','groupFixed')){if($null-ne$Value.$name){Test-AFSettingsShape $Value.$name $Problems}}
    Test-ABThingShape $Value.actor $Problems;foreach($row in $Value.parcels){Test-ABThingShape $row $Problems}
    foreach($m in $Value.members){
        if(-not(Test-ABFields $m @{thing='object';runtimeType='string';defPackage='string';groupTag='string';slotIdentity='string';groupIdentity='nullable-string';ordinal='int';definitionMaximum='int';nativeMaximum='int';registered='bool';contentsPacked='bool';registryCount='nullable-int';cells='array';compTypes='array';grid='array';local='object';effective='object';declaredFixed='object';interfaceFixed='object'} 'CAP03-A member' $Problems)){continue}
        Test-ABThingShape $m.thing $Problems;Test-ABStrings $m.compTypes 'member comps' $Problems
        foreach($c in $m.cells){if($c-isnot[string]){$Problems.Add('CAP03-A nontext member cell.')}else{Read-ABCell $c $Problems|Out-Null}}
        foreach($g in $m.grid){if(Test-ABFields $g @{cell='string';things='array'} 'CAP03-A grid' $Problems){Read-ABCell $g.cell $Problems|Out-Null;foreach($thing in $g.things){Test-ABThingShape $thing $Problems}}}
        foreach($name in @('local','effective','declaredFixed','interfaceFixed')){Test-AFSettingsShape $m.$name $Problems}
    }
}
function Test-AFShapeCore($Value,$Problems){
    $before=$Problems.Count
    if(-not(Test-ABFields $Value @{storageProjectionFilters='object';caseId='string';runId='string';status='string';detail='string';startedUtc='utc';finishedUtc='utc';executingGameVersion='string';installedVersionFile='string';unityErrorsObserved='int';negativeControl='nullable-object';mods='array';assemblies='array';assertions='array'} 'CAP03-A global result' $Problems)){return $false}
    foreach($r in $Value.mods){Test-ABFields $r @{packageId='string';rootPath='string'} 'CAP03-A mod' $Problems|Out-Null}
    foreach($r in $Value.assemblies){Test-ABFields $r @{name='string';path='string';sha256='string';assemblyVersion='string';moduleVersionId='string'} 'CAP03-A assembly' $Problems|Out-Null}
    foreach($r in $Value.assertions){Test-ABFields $r @{id='string';passed='bool';observed='string'} 'CAP03-A global assertion' $Problems|Out-Null}
    $p=$Value.storageProjectionFilters
    if(-not(Test-ABFields $p @{caseId='string';contract='string';expectedBehavior='string';status='string';scope='string';error='nullable-string';allCap03ComponentsSatisfied='bool';pendingControls='array';fixtureValid='bool';requestedBehaviorSatisfied='bool';expectationMatched='bool';startedTick='int';finishedTick='int';mainThread='int';mapId='int';session='string';nativeIdentity='nullable-string';asfIdentity='nullable-string';thingDefs='int';categoryDefs='int';specialDefs='int';gameMaps='int';assemblies='array';bindings='array';patchInventory='array';inputs='array';catalogStatus='object';initialCounter='object';finalCounter='object';scenes='array';assertions='array';records='array'} 'CAP03-A component' $Problems)){return $false}
    foreach($name in @('bindings','patchInventory','pendingControls')){Test-ABStrings $p.$name $name $Problems}
    foreach($r in $p.assemblies){Test-ABFields $r @{name='string';path='string';mvid='string';sha256='string'} 'CAP03-A bound assembly' $Problems|Out-Null}
    foreach($r in $p.inputs){Test-ABFields $r @{packageId='string';root='string';relativePath='string';sha256='string'} 'CAP03-A input' $Problems|Out-Null}
    foreach($r in $p.assertions){Test-ABFields $r @{sequence='int';id='string';kind='string';passed='bool';detail='string'} 'CAP03-A assertion' $Problems|Out-Null}
    foreach($r in $p.records){Test-ABFields $r @{sequence='int';tick='int';kind='string';trial='nullable-string';data='string'} 'CAP03-A record' $Problems|Out-Null}
    Test-ABStatusShape $p.catalogStatus $Problems;Test-AFCounterShape $p.initialCounter $Problems;Test-AFCounterShape $p.finalCounter $Problems
    foreach($s in $p.scenes){
        if(-not(Test-ABFields $s @{id='string';error='nullable-string';unknownWorker='bool';asfFirst='bool';completed='bool';restored='bool';startedTick='int';finishedTick='int';storageGroupsBefore='int';storageGroupsAfter='int';riceRottableType='nullable-string';riceRotStage='nullable-string';freshWorkerType='nullable-string';unlinked='object';linked='object';afterProtected='object';afterNativeControl='object';beforeProtected='object';afterProjection='object';afterControl='object';preparations='array';trials='array';nativeControls='array';retiredThingIds='array'} 'CAP03-A scene' $Problems)){continue}
        foreach($name in @('unlinked','linked','afterProtected','afterNativeControl')){Test-AFSnapshotShape $s.$name $Problems}
        foreach($name in @('beforeProtected','afterProjection','afterControl')){Test-AFCounterShape $s.$name $Problems}
        foreach($id in $s.retiredThingIds){Assert-AF (($id-is[int] -or $id-is[long]) -and $id-gt0 -and $id-le[int]::MaxValue) 'invalid retirement ID.' $Problems}
        foreach($r in $s.preparations){if(Test-ABFields $r @{stage='string';parentId='int';allowance='long';sourceRequired='long';declaredCost='long';charged='long';ready='bool';warmup='bool';indexed='int';status='object';before='object';after='object'} 'CAP03-A preparation' $Problems){Test-ABStatusShape $r.status $Problems;Test-AFCounterShape $r.before $Problems;Test-AFCounterShape $r.after $Problems}}
        foreach($t in $s.trials){
            if(-not(Test-ABFields $t @{id='string';stage='string';query='string';parcelId='string';scopeIdentity='nullable-string';error='nullable-string';parentId='int';subjectId='int';completed='bool';disposed='bool';openStatus='object';cell='object';eligibility='object';allowance='array';openWork='array';afterOpen='array';afterCell='array';afterEligibility='array';afterDispose='array';expectedFilterCharge='long';before='object';after='object'} 'CAP03-A trial' $Problems)){continue}
            foreach($name in @('allowance','openWork','afterOpen','afterCell','afterEligibility','afterDispose')){Test-ABWorkShape $t.$name $Problems}
            Test-ABStatusShape $t.openStatus $Problems;Test-ABCellShape $t.cell $Problems;Test-ABEligibilityShape $t.eligibility $Problems;Test-AFCounterShape $t.before $Problems;Test-AFCounterShape $t.after $Problems
        }
        foreach($n in $s.nativeControls){if(Test-ABFields $n @{route='string';parentId='int';subjectId='int';actual='bool';expected='bool';before='object';after='object'} 'CAP03-A native control' $Problems){Test-AFCounterShape $n.before $Problems;Test-AFCounterShape $n.after $Problems}}
    }
    return $Problems.Count-eq$before
}
function Get-AFCounter([long]$Matches){return [pscustomobject]@{constructed=$(if($Matches-gt0){1L}else{0L});matches=$Matches;alwaysMatches=0L;canEverMatch=0L;workerExists=($Matches-gt0);actualWorkerType=$(if($Matches-gt0){'HaulersDream.RuntimeHarness.Cap03FilterUnknownWorker'}else{$null});workerIdentity=$(if($Matches-gt0){'ref:70'}else{$null})}}
function Test-AFCounter($Value,[long]$Matches,$Problems){Assert-AF (Test-ABSame $Value (Get-AFCounter $Matches)) 'custom construction/Matches/other virtual counter boundary differs.' $Problems}
function Test-AFFilter($Value,$Defs,$Specials,$Problems){
    Assert-AF ($Value.type-ceq'Verse.ThingFilter' -and -not$Value.onlySpecial -and $Value.hitPointsMin-eq0 -and $Value.hitPointsMax-eq1 -and $Value.mentalBreakMin-eq0 -and $Value.mentalBreakMax-eq1 -and $Value.qualities-ceq'Awful~Legendary' -and (Test-ABSame $Value.disallowedSpecials $Specials)) 'fixed/effective filter kind/ranges/special restrictions differ.' $Problems
    if($null-ne$Defs){Assert-AF (Test-ABSame $Value.allowedDefs $Defs) 'complete allowed-definition census differs.' $Problems}
    else{
        # Exact inspected 367-definition native Shelf filter for the frozen base-game profile.
        $sha=[Security.Cryptography.SHA256]::Create()
        try{$hash=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes((Get-ABCanonical $Value)))).Replace('-','')}finally{$sha.Dispose()}
        Assert-AF ($Value.allowedDefs.Count-eq367 -and $hash-ceq'A7F9626A49276985DC214AA16CAA7727F655316D2C8257E9E550374803A03D6B') 'native Shelf full fixed-filter contents differ from the inspected profile.' $Problems
    }
}
function Test-AFSettings($Value,[int]$Settings,[int]$Filter,$Owner,$Type,[string]$Priority,$Defs,$Specials,$Problems){
    Assert-AF ($Value.settingsIdentity-ceq('ref:'+$Settings) -and $Value.filterIdentity-ceq('ref:'+$Filter) -and $Value.ownerIdentity-ceq$Owner -and $Value.ownerType-ceq$Type -and $Value.priority-ceq$Priority) 'settings/filter/owner identity or priority differs.' $Problems
    Test-AFFilter $Value.contents $Defs $Specials $Problems
}
function Test-AFThing($Thing,[string]$Def,[string]$Cell,[int]$MapId,[int]$Limit,$Faction,$Stuff,[int]$HP,$Problems){
    Assert-AF ($Thing.thingId-gt0 -and $Thing.def-ceq$Def -and $Thing.cell-ceq$Cell -and $Thing.mapId-eq$MapId -and $Thing.count-eq1 -and $Thing.stackLimit-eq$Limit -and $Thing.spawned -and -not$Thing.destroyed -and $Thing.heldByFixtureMap -and $Thing.holderType-ceq'Verse.Map' -and $Thing.faction-ceq$Faction -and $Thing.stuff-ceq$Stuff -and $Thing.hitPoints-eq$HP) ('physical thing identity/count/map/custody differs: '+$Def) $Problems
}
function Test-AFScenePhysical($P,$S,[int]$Index,[int]$CenterX,[int]$CenterZ,$Problems){
    $l=$S.linked;$u=$S.unlinked;$a=$l.members[0];$b=$l.members[1];$defs=@('RawRice','Steel','WoodLog')
    $ax=$CenterX+$(if(($Index%2)-eq0){-14}else{10});$az=$CenterZ+$(if($Index-lt2){-8}else{8});$anchor='('+$ax+', 0, '+$az+')'
    $shelf='('+($ax+5)+', 0, '+$az+')';$shelf2='('+($ax+6)+', 0, '+$az+')'
    $asfDef=$(if($Index-lt2){'HDHarness_CAP03A_Fresh'}else{'HDHarness_CAP03A_Unreviewed'});$special=$(if($Index-lt2){'AllowFresh'}else{'HDHarness_CAP03A_Unknown'})
    $refs=@(@(1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18),@(26,27,28,29,30,31,7,8,32,33,34,35,13,14,36,37,38,39),@(47,48,49,50,51,52,53,54,55,56,57,58,13,14,59,60,61,62),@(71,72,73,74,75,76,53,54,77,78,79,80,13,14,81,82,83,84))[$Index]
    $faction=$l.actor.faction;Assert-AF ($faction-cmatch'^Faction_[0-9]+$') 'actor/player faction absent.' $Problems
    Test-AFThing $l.actor Human ('('+$CenterX+', 0, '+($CenterZ-20)+')') $P.mapId 1 $faction $null -1 $Problems
    Test-AFThing $a.thing $asfDef $anchor $P.mapId 1 $faction WoodLog 65 $Problems
    Test-AFThing $b.thing Shelf $shelf $P.mapId 1 $faction WoodLog 65 $Problems
    for($i=0;$i-lt3;$i++){$parcel=$l.parcels[$i];Test-AFThing $parcel @('RawRice','WoodLog','Steel')[$i] ('('+($ax-3+2*$i)+', 0, '+($az+4)+')') $P.mapId 75 $null $null @(60,150,-1)[$i] $Problems}
    Assert-AF ($S.riceRottableType-ceq'RimWorld.CompRottable' -and $S.riceRotStage-ceq'Fresh' -and $S.freshWorkerType-ceq'RimWorld.SpecialThingFilterWorker_Fresh') 'live freshness discriminator differs.' $Problems
    Assert-AF ($S.completed -and $S.restored -and $null-eq$S.error -and $S.startedTick-eq$P.startedTick -and $S.finishedTick-eq$P.finishedTick -and $S.storageGroupsBefore-eq0 -and $S.storageGroupsAfter-eq0) 'scene did not complete and restore its native group.' $Problems
    $retired=@($b.thing.thingId,$a.thing.thingId,$l.parcels[2].thingId,$l.parcels[1].thingId,$l.parcels[0].thingId)
    Assert-AF (Test-ABSame $S.retiredThingIds $retired) 'native retirement order/ID census differs.' $Problems
    Assert-AF ((Test-ABSame $l $S.afterProtected) -and (Test-ABSame $l $S.afterNativeControl)) 'physical/filter evidence changed during projection or explicit controls.' $Problems
    foreach($snap in @($u,$l)){
        $linked=$snap-eq$l
        Assert-AF ($snap.tick-eq$P.startedTick -and $snap.mapId-eq$P.mapId -and $snap.actorIdle -and (Test-ABSame $snap.actor $l.actor) -and (Test-ABSame $snap.parcels $l.parcels)) 'snapshot actor/parcel/tick/map continuity differs.' $Problems
        $group=$(if($linked){'ref:'+$refs[14]}else{$null})
        Assert-AF ($snap.groupIdentity-ceq$group -and $snap.groupRegistered-eq$linked -and $snap.groupLoadId-eq$(if($linked){$Index}else{-1})) 'native group registration/load/reference differs.' $Problems
        $order=@();if($linked){$order=$(if($S.asfFirst){@($a.thing.thingId,$b.thing.thingId)}else{@($b.thing.thingId,$a.thing.thingId)})}
        Assert-AF (Test-ABSame $snap.memberOrder @($order)) 'native group member order differs.' $Problems
        if($linked){Test-AFSettings $snap.groupEffective $refs[15] $refs[16] $group 'RimWorld.StorageGroup' Critical $defs @() $Problems;Assert-AF (Test-ABSame $snap.groupFixed $snap.members[$(if($S.asfFirst){0}else{1})].interfaceFixed) 'group fixed settings is not first interface object.' $Problems}
        else{Assert-AF ($null-eq$snap.groupEffective -and $null-eq$snap.groupFixed) 'unlinked snapshot invents group settings.' $Problems}
        for($mi=0;$mi-lt2;$mi++){
            $m=$snap.members[$mi];$r=$(if($mi-eq0){$refs[0..7]}else{$refs[8..13]});$type=$(if($mi-eq0){'AdaptiveStorage.ThingClass'}else{'RimWorld.Building_Storage'})
            $cells=@(if($mi-eq0){$anchor}else{$shelf;$shelf2})
            Assert-AF ((Test-ABSame $m.thing $l.members[$mi].thing) -and $m.runtimeType-ceq$type -and $m.defPackage-ceq$(if($mi-eq0){'giwaffed.haulersdream.runtimeharness'}else{'ludeon.rimworld'}) -and $m.groupTag-ceq'Shelf' -and $m.slotIdentity-ceq('ref:'+$r[0]) -and $m.groupIdentity-ceq$group -and $m.ordinal-eq$(if(-not$linked){-1}elseif($S.asfFirst){$mi}else{1-$mi}) -and $m.registered -and -not$m.contentsPacked -and $m.definitionMaximum-eq$(if($mi-eq0){6}else{3}) -and $m.nativeMaximum-eq$m.definitionMaximum -and $m.registryCount-eq$(if($mi-eq0){0}else{$null}) -and (Test-ABSame $m.cells @($cells)) -and (Test-ABSame $m.compTypes @(if($mi-eq1){'RimWorld.CompStyleable'}))) 'actual member type/slot/comp/registry/cell census differs.' $Problems
            Assert-AF ($m.grid.Count-eq$cells.Count) 'grid cell coverage count differs.' $Problems
            for($j=0;$j-lt[math]::Min($m.grid.Count,$cells.Count);$j++){Assert-AF ($m.grid[$j].cell-ceq$cells[$j] -and (Test-ABSame $m.grid[$j].things @($m.thing))) 'native grid includes missing/extra/wrong physical entries.' $Problems}
            Test-AFSettings $m.local $r[1] $r[2] ('ref:'+$r[3]) $type Critical $defs @() $Problems
            Assert-AF (Test-ABSame $m.effective $(if($linked){$snap.groupEffective}else{$m.local})) 'effective settings reference/content does not follow real linking.' $Problems
            if($mi-eq0){Test-AFSettings $m.declaredFixed $r[4] $r[5] $null $null Normal @('WoodLog') @() $Problems;Test-AFSettings $m.interfaceFixed $r[6] $r[7] $null $null Normal $defs @($special) $Problems}
            else{Test-AFSettings $m.declaredFixed $r[4] $r[5] $null $null Normal $null @('AllowLargeCorpses') $Problems;Assert-AF (Test-ABSame $m.declaredFixed $m.interfaceFixed) 'native Shelf interface/declared dispatch differs.' $Problems}
        }
    }
}
function Get-AFCosts($P,$Scene,$Member,[bool]$Deferred,[bool]$Eligible,[long]$FilterCharge,[string]$Operation){
    [long[]]$v=@(0L,0L,0L,0L,0L,0L,0L,0L,0L,0L,0L,0L,0L,0L);$asf=$Member.runtimeType-ceq'AdaptiveStorage.ThingClass'
    if($Operation-ceq'open'){$v[11]=$P.gameMaps;return ,$v}
    # Source: one member resolution, two complete empty-grid/capacity censuses, and linked guard.
    if($Operation-ceq'cell'){$v[0]=1;$v[1]=1;$v[2]=2;$v[6]=$Member.compTypes.Count;$v[7]=$(if($asf){2}else{0});$v[8]=2;$v[11]=4;$v[13]=2;return ,$v}
    # Deferred readiness returns after the initial ValidateCell. Other outcomes also run Finish's
    # second validation. Native success prepays NativeCellPredicate(1,true) and ASF global patch work.
    $v[2]=$(if($Deferred){1}else{2});$v[7]=$(if($asf){$v[2]}else{0});$v[8]=$v[2];$v[11]=$(if($Deferred){2}else{3});$v[13]=10;$v[5]=$FilterCharge
    if($Eligible){$v[3]=4;$v[4]=1;$v[6]=3;$v[7]+=3;$v[8]+=2;$v[9]=1;$v[10]=1}
    return ,$v
}
function Test-AFPreparation($P,$S,$Row,$Member,[string]$Stage,$Problems){
    $full=$Stage-ceq'complete-allowance';$unsupported=$S.unknownWorker -and $Member.runtimeType-ceq'AdaptiveStorage.ThingClass';$ready=$full -and -not$unsupported
    [long]$cost=4L*([long]$P.thingDefs+1)*([long]$P.categoryDefs+1)*([long]$P.specialDefs+1)+3L*$Member.cells.Count+$Member.compTypes.Count+$P.gameMaps
    $status=[pscustomobject]@{usable=$ready;observation=$(if($ready){'Complete'}else{'Deferred'});capability=$(if($full -and $unsupported){'Unsupported'}else{'Supported'});reason=$(if($ready){'None'}elseif($full -and $unsupported){'UnreviewedPredicate'}else{'ProviderInitializing'})}
    Assert-AF ($Row.stage-ceq$Stage -and $Row.parentId-eq$Member.thing.thingId -and $Row.sourceRequired-eq$cost -and $Row.allowance-eq$(if($full){$cost}else{0}) -and $Row.declaredCost-eq$Row.allowance -and $Row.charged-eq$Row.allowance -and $Row.ready-eq$ready -and $Row.warmup -and $Row.indexed-eq0 -and (Test-ABSame $Row.status $status)) 'preparation identity/status/complete source cost differs.' $Problems
    Assert-AF ((Test-ABSame $Row.before $S.beforeProtected) -and (Test-ABSame $Row.after $Row.before)) 'preparation crosses the protected worker boundary.' $Problems
}
function Get-AFPredicates{return @('destination-enabled','destination-faction','selected-priority','effective-thing-filter','concrete-fixed-filter','asf-declared-fixed-filter','asf-actual-member-capacity','native-IsGoodStoreCell','hd-explicit-context-filter')}
function Test-AFTrial($P,$S,$T,$Member,$Parcel,[string]$Stage,[int]$ScopeRef,$Problems){
    $asf=$Member.runtimeType-ceq'AdaptiveStorage.ThingClass';$rice=$Parcel.def-ceq'RawRice';$wood=$Parcel.def-ceq'WoodLog'
    $deferred=$Stage-ceq'unprepared' -or ($S.unknownWorker -and ($asf -or $S.asfFirst));$effectiveRefusal=-not$deferred -and $S.asfFirst -and $rice
    $memberRefusal=-not$deferred -and -not$effectiveRefusal -and $asf -and -not$wood;$eligible=-not$deferred -and -not$effectiveRefusal -and -not$memberRefusal
    $id=$S.id+'/'+$Stage+'/'+$Member.thing.def+$Member.thing.thingId+'/'+$Parcel.def+$Parcel.thingId
    Assert-AF ($T.id-ceq$id -and $T.stage-ceq$Stage -and $T.parentId-eq$Member.thing.thingId -and $T.subjectId-eq$Parcel.thingId -and $T.query-ceq('CAP03-A/'+$id) -and $T.parcelId-ceq($id+'/parcel') -and $T.scopeIdentity-ceq('ref:'+$ScopeRef) -and $null-eq$T.error -and $T.completed -and $T.disposed -and (Test-ABComplete $T.openStatus)) 'fresh scope lifetime/query/parent/subject binding differs.' $Problems
    Assert-AF ((Test-ABSame $T.before $S.beforeProtected) -and (Test-ABSame $T.after $T.before)) 'actual scope changes custom-worker counters during protected interval.' $Problems
    $c=$T.cell;$e=$T.eligibility;$xy=Read-ABCell $c.cell $Problems
    Assert-AF ((Test-ABComplete $c.status) -and $c.fixtureScene-ceq$S.id -and $c.fixtureStage-ceq$Stage -and $c.observationId-eq1 -and $c.generation-eq1 -and $c.session-ceq$P.session -and $c.mapId-eq$P.mapId -and $c.tick-eq$P.startedTick -and $c.query-ceq$T.query -and $c.cell-ceq$Member.cells[0] -and $c.parentKey-ceq('building:'+$T.parentId) -and $c.groupKey-ceq('linked:'+$S.linked.groupLoadId) -and $c.provider-ceq$(if($asf){$P.asfIdentity}else{'RimWorld physical slots'}) -and $c.vacantKey-ceq($P.session+'/'+$P.mapId+'/building:'+$T.parentId+'/'+$xy.x+','+$xy.z+'/vacant') -and $c.stacks.Count-eq0 -and $c.gridEntries-eq1 -and $c.itemCount-eq0 -and $c.maximumSlots-eq$Member.nativeMaximum -and $c.vacantSlots-eq$Member.nativeMaximum) 'native cell/resource/complete empty physical census differs.' $Problems
    $status=[pscustomobject]@{usable=(-not$deferred);observation=$(if($deferred){'Deferred'}else{'Complete'});capability='Supported';reason=$(if($deferred){'ProviderInitializing'}else{'None'})}
    Assert-AF ($e.fixtureScene-ceq$S.id -and $e.fixtureStage-ceq$Stage -and $e.observationId-eq$c.observationId -and $e.parcelId-ceq$T.parcelId -and (Test-ABSame $e.status $status) -and $e.state-ceq$(if($deferred){'NotEvaluated'}elseif($eligible){'Eligible'}else{'Refused'}) -and $e.topUps.Count-eq0 -and $e.vacantEligible-eq$eligible -and $e.unitsPerNewStack-eq$(if($eligible){$Parcel.stackLimit}else{$null})) 'eligibility readiness/refusal/quantity or exact parcel correspondence differs.' $Problems
    [long]$preflight=3L+$S.linked.groupEffective.contents.disallowedSpecials.Count+$S.linked.groupFixed.contents.disallowedSpecials.Count+$Member.declaredFixed.contents.disallowedSpecials.Count
    [long]$filters=$(if($deferred){0}else{$preflight+$(if($eligible){1L+$(if($asf){1L+$Member.declaredFixed.contents.disallowedSpecials.Count}else{0})}else{0})})
    Assert-AF ($T.expectedFilterCharge-eq$filters) 'source-selected fixed-filter precharge differs.' $Problems
    $names=Get-AFPredicates;Assert-AF ($e.predicates.Count-eq9) 'nine predicate catalog incomplete/duplicated.' $Problems
    for($i=0;$i-lt[math]::Min(9,$e.predicates.Count);$i++){
        $state='NotEvaluated';$reason='None'
        if($i-lt3){$state='Eligible'}elseif(-not$deferred){
            if($i-eq3){$state=$(if($effectiveRefusal){'Refused'}else{'Eligible'});if($effectiveRefusal){$reason='ThingFilterRefused'}}
            elseif(-not$effectiveRefusal -and $i-eq4){$state=$(if($memberRefusal){'Refused'}else{'Eligible'});if($memberRefusal){$reason='MemberFixedFilterRefused'}}
            elseif($eligible -and ($i-ge7 -or $asf)){$state='Eligible'}
        }
        $r=$e.predicates[$i];Assert-AF ($r.name-ceq$names[$i] -and $r.state-ceq$state -and $r.reason-ceq$reason -and $null-eq$r.targetId) ('predicate boundary differs: '+$names[$i]) $Problems
    }
    Test-ABWork $T.allowance @(16L,8L,8192L,8192L,256L,4096L,8192L,8192L,16384L,256L,256L,16384L,0L,4096L) 'A scope allowance' $Problems
    $open=Get-AFCosts $P $S $Member $deferred $eligible $filters open;$cell=Get-AFCosts $P $S $Member $deferred $eligible $filters cell;$elig=Get-AFCosts $P $S $Member $deferred $eligible $filters eligibility
    Test-ABWork $T.openWork $open 'A open operation' $Problems;Test-ABWork $T.afterOpen $open 'A actual after-open' $Problems
    Test-ABWork $c.work $cell 'A cell operation' $Problems;Test-ABWork $e.work $elig 'A eligibility operation' $Problems
    $afterCell=@(for($i=0;$i-lt14;$i++){$open[$i]+$cell[$i]});$afterEligibility=@(for($i=0;$i-lt14;$i++){$afterCell[$i]+$elig[$i]})
    Test-ABWork $T.afterCell $afterCell 'A actual after-cell' $Problems;Test-ABWork $T.afterEligibility $afterEligibility 'A actual after-eligibility' $Problems;Test-ABWork $T.afterDispose $afterEligibility 'A actual after-dispose' $Problems
}
function Get-AFRecordPlan($P,$Problems){
    $plan=New-Object 'System.Collections.Generic.List[object]';$state=@{assertion=0}
    $emit={param($kind,$trial,$data) $plan.Add([pscustomobject]@{kind=$kind;trial=$trial;data=$data})}
    $assert={param($id,$kind)
        if($state.assertion-ge$P.assertions.Count){$Problems.Add('CAP03-A missing required assertion '+$id);return}
        $r=$P.assertions[$state.assertion];$state.assertion++
        Assert-AF ($r.sequence-eq$state.assertion -and $r.id-ceq$id -and $r.kind-ceq$kind -and $r.passed -and -not[string]::IsNullOrWhiteSpace($r.detail)) ('assertion ID/kind/order/outcome differs: '+$id) $Problems
        & $emit assertion $null $r
    }
    $prepare={param($scene,$row)
        & $emit preparation $scene.id $row
        # Physical parent keys are resolved from the source catalog, never from assertion text.
        $m=@($scene.linked.members|Where-Object {$_.thing.thingId-eq$row.parentId})[0];$thing=$m.thing.def+$m.thing.thingId
        & $assert ($scene.id+'-prepare-'+$thing+'-'+$row.stage) behavior
        & $assert ($scene.id+'-prepare-no-Matches-'+$thing+'-'+$row.stage) behavior
    }
    $trial={param($scene,$t)
        $begin=[pscustomobject]@{id=$t.id;stage=$t.stage;query=$t.query;parcelId=$t.parcelId;scopeIdentity=$null;error=$null;parentId=$t.parentId;subjectId=$t.subjectId;completed=$false;disposed=$false;openStatus=$null;cell=$null;eligibility=$null;allowance=$null;openWork=$null;afterOpen=$null;afterCell=$null;afterEligibility=$null;afterDispose=$null;expectedFilterCharge=0L;before=$t.before;after=$null}
        & $emit trial-begin $scene.id $begin
        & $emit open $t.id ([pscustomobject]@{status=$t.openStatus;query=$t.query;scopeIdentity=$t.scopeIdentity;work=$t.openWork})
        & $assert ($t.id+'-open') behavior;& $assert ($t.id+'-open-accounting') behavior
        & $emit cell $t.id $t.cell
        & $assert ($t.id+'-cell-accounting') behavior;& $assert ($t.id+'-real-cell') behavior
        & $emit eligibility $t.id $t.eligibility
        foreach($suffix in @('eligibility-accounting','cell-parcel-binding','filter-charge','typed-outcome','quantity','predicate-catalog')){& $assert ($t.id+'-'+$suffix) behavior}
        foreach($name in (Get-AFPredicates)){& $assert ($t.id+'-predicate-'+$name) behavior}
        & $assert ($t.id+'-disposed') behavior;& $assert ($t.id+'-no-custom-Matches') behavior
        & $emit trial-result $scene.id $t
    }
    & $assert expectation fixture;& $assert actual-main-thread-map fixture
    & $emit rice-definition $null @('RawRice','Verse.ThingWithComps','ludeon.rimworld','75')
    & $assert test-worker-definition fixture;& $assert native-fresh-definition fixture
    & $emit counter-initial $null $P.initialCounter;& $emit bindings $null ([pscustomobject]@{assemblies=$P.assemblies;bindings=$P.bindings})
    & $assert reviewed-asf fixture
    for($i=0;$i-lt$P.inputs.Count;$i++){& $emit input $null $P.inputs[$i];& $assert ('input-'+($i+1)) fixture}
    foreach($id in @('private-scene-bounds','normal-actor','one-idle-actor')){& $assert $id fixture}
    & $emit catalog $null $P.catalogStatus;& $assert catalog-created fixture
    & $emit patch-inventory $null $P.patchInventory;& $assert catalog-no-custom-predicate behavior
    foreach($s in $P.scenes){
        foreach($parcelItem in $s.linked.parcels){& $assert ('spawn-'+$parcelItem.def+$parcelItem.thingId) fixture}
        & $assert ($s.id+'-fresh-rice') fixture;& $emit freshness $s.id @($s.riceRottableType,$s.riceRotStage,$s.freshWorkerType)
        & $assert ($s.id+'-xml-class') fixture;& $assert ($s.id+'-xml-stuff-lock') fixture
        foreach($m in $s.linked.members){& $assert ('spawn-'+$m.thing.def+$m.thing.thingId) fixture}
        & $assert ($s.id+'-native-shelf') fixture
        foreach($m in $s.linked.members){& $assert ($s.id+'-initial-clean-'+$m.thing.def+$m.thing.thingId) fixture}
        & $emit unlinked $s.id $s.unlinked;& $assert ($s.id+'-initial-equivalent') fixture;& $emit linked $s.id $s.linked
        foreach($suffix in @('real-group-order','distinct-fixed-dispatch','first-member-interface-owner','effective-uncontaminated','physical-empty-members')){& $assert ($s.id+'-'+$suffix) fixture}
        & $emit protected-begin $s.id $s.beforeProtected
        for($i=0;$i-lt2;$i++){& $prepare $s $s.preparations[$i];& $trial $s $s.trials[$i]}
        & $prepare $s $s.preparations[2];& $prepare $s $s.preparations[3]
        for($i=2;$i-lt8;$i++){& $trial $s $s.trials[$i]}
        & $emit protected-end $s.id $s.afterProjection;& $emit physical-after-protected $s.id $s.afterProtected
        & $assert ($s.id+'-physical-unchanged') fixture;& $assert ($s.id+'-no-custom-Matches') behavior
        foreach($n in $s.nativeControls){
            & $emit native-control $s.id $n
            $m=@($s.linked.members|Where-Object {$_.thing.thingId-eq$n.parentId})[0];$parcelItem=@($s.linked.parcels|Where-Object thingId -EQ $n.subjectId)[0]
            & $assert ($s.id+'-native-'+$n.route+'-'+$m.thing.def+$m.thing.thingId+'-'+$parcelItem.def+$parcelItem.thingId) behavior
        }
        & $emit physical-after-native-control $s.id $s.afterNativeControl;& $emit native-control-end $s.id $s.afterControl
        & $assert ($s.id+'-native-controls-physical-unchanged') fixture
        if($s.unknownWorker){& $assert ($s.id+'-counter-reachable-after-boundary') behavior}
        & $assert ($s.id+'-retired') behavior;& $emit scene-result $s.id $s
    }
    foreach($id in @('four-complete-scenes','scenes-disjoint','all-scopes-closed')){& $assert $id behavior}
    & $assert same-tick fixture;& $emit counter-final $null $P.finalCounter
    Assert-AF ($state.assertion-eq881 -and $P.assertions.Count-eq881 -and @($P.assertions.id|Select-Object -Unique).Count-eq881 -and @($P.assertions|Where-Object kind -CEQ fixture).Count-eq91 -and @($P.assertions|Where-Object kind -CEQ behavior).Count-eq790) 'complete source assertion catalog must be 91 fixture +790 behavior, no duplicates or omissions.' $Problems
    return $plan.ToArray()
}
function Test-AFECore($Value,[string]$ExpectedBehavior,$Problems){
    if(-not(Test-AFShapeCore $Value $Problems)){return};$p=$Value.storageProjectionFilters;$initialProblems=$Problems.Count
    Assert-AF ($ExpectedBehavior-ceq'satisfied' -and $Value.caseId-ceq'CAP03-A' -and $p.caseId-ceq'CAP03-A' -and $p.expectedBehavior-ceq'satisfied' -and $p.contract-ceq'linked-fixed-filter-dispatch-v1' -and $p.status-ceq'passed' -and $Value.status-ceq'passed' -and $null-eq$p.error -and $p.fixtureValid -and $p.requestedBehaviorSatisfied -and $p.expectationMatched -and -not$p.allCap03ComponentsSatisfied -and $Value.unityErrorsObserved-eq0 -and $null-eq$Value.negativeControl) 'not a clean satisfied component-A result.' $Problems
    Assert-AF ($p.scope-ceq'Only native/ASF linked fixed-filter dispatch and custom-worker containment. No full CAP03, allocation, native-call instrumentation or hauling compatibility claim.' -and (Test-ABSame $p.pendingControls @('CAP03-C','independent-native-call-instrumentation','large-groups-and-lifecycle','allocation-and-original-report-convergence')) -and $Value.detail-ceq'Actual linked fixed-filter dispatch and custom-worker containment only; remaining CAP03 and hauling convergence are unfinished.') 'component-only scope/pending controls lost or broadened.' $Problems
    Assert-AF ($Value.runId-cmatch'^[a-f0-9]{32}$' -and $p.session-cmatch'^[a-f0-9]{32}$' -and $p.mainThread-eq1 -and $p.startedTick-ge5 -and $p.finishedTick-eq$p.startedTick -and $p.mapId-ge0 -and $p.gameMaps-eq1 -and $p.thingDefs-eq1561 -and $p.categoryDefs-eq75 -and $p.specialDefs-eq39 -and $Value.executingGameVersion-ceq'1.6.4871 rev591' -and $Value.installedVersionFile-ceq'1.6.4871 rev590' -and [datetimeoffset]$Value.finishedUtc-ge[datetimeoffset]$Value.startedUtc -and (Test-ABComplete $p.catalogStatus)) 'frozen native/definition census, session, map, thread or same-tick provenance differs.' $Problems
    Test-AFIdentity $Value $Problems
    if($Problems.Count-ne$initialProblems){return}
    $ids=@('native-asf-first','native-shelf-first','unknown-asf-first','unknown-shelf-first')
    Assert-AF ($p.scenes.Count-eq4 -and (Test-ABSame @($p.scenes.id) $ids)) 'requires all four independent scene orders.' $Problems
    if($p.scenes.Count-ne4){return}
    Test-AFCounter $p.initialCounter 0 $Problems;Test-AFCounter $p.finalCounter 12 $Problems
    $map=Get-ABOne $Value.assertions id real-map-initialized $Problems
    if($null-eq$map -or $map.observed-cnotmatch'^size=\(([0-9]+), 1, ([0-9]+)\); maps=1$'){$Problems.Add('CAP03-A map size missing.');return}
    $sx=0;$sz=0;$tx=$Matches[1];$tz=$Matches[2]
    if(-not[int]::TryParse($tx,[ref]$sx) -or -not[int]::TryParse($tz,[ref]$sz) -or $sx-lt50 -or $sz-lt50){$Problems.Add('CAP03-A map dimensions invalid.');return}
    $cx=[int][math]::Floor($sx/2);$cz=[int][math]::Floor($sz/2);$physical=New-Object 'System.Collections.Generic.List[int]'
    for($si=0;$si-lt4;$si++){
        $s=$p.scenes[$si]
        Assert-AF ($s.unknownWorker-eq($si-ge2) -and $s.asfFirst-eq(($si%2)-eq0)) 'scene worker/order flags contradict source catalog.' $Problems
        if($s.linked.members.Count-ne2 -or $s.unlinked.members.Count-ne2 -or $s.linked.parcels.Count-ne3 -or $s.unlinked.parcels.Count-ne3 -or $s.trials.Count-ne8 -or $s.preparations.Count-ne4 -or $s.nativeControls.Count-ne18){$Problems.Add('CAP03-A scene missing physical/operation rows.');return}
        Test-AFScenePhysical $p $s $si $cx $cz $Problems
        if($Problems.Count-ne$initialProblems){return}
        Assert-AF (Test-ABSame $s.linked.actor $p.scenes[0].linked.actor) 'actor changed between complete scenes.' $Problems
        foreach($m in $s.linked.members){$physical.Add($m.thing.thingId)};foreach($q in $s.linked.parcels){$physical.Add($q.thingId)}
        [long]$count=$(if($si-eq3){9}else{0});Test-AFCounter $s.beforeProtected $count $Problems;Test-AFCounter $s.afterProjection $count $Problems
        $order=@(if($s.asfFirst){$s.linked.members[0];$s.linked.members[1]}else{$s.linked.members[1];$s.linked.members[0]})
        for($i=0;$i-lt4;$i++){Test-AFPreparation $p $s $s.preparations[$i] $order[$i%2] $(if($i-lt2){'zero-allowance'}else{'complete-allowance'}) $Problems}
        for($i=0;$i-lt8;$i++){$mi=$(if($i-lt2){$i}else{[int][math]::Floor(($i-2)/3)});$pi=$(if($i-lt2){0}else{($i-2)%3});Test-AFTrial $p $s $s.trials[$i] $order[$mi] $s.linked.parcels[$pi] $(if($i-lt2){'unprepared'}else{'prepared'}) (@(18,39,62,84)[$si]+$i) $Problems}
        $ni=0
        foreach($m in $s.linked.members){foreach($parcel in $s.linked.parcels){foreach($route in @('effective-recursive','interface-fixed','declared-fixed')){
            $n=$s.nativeControls[$ni++];$asf=$m.runtimeType-ceq'AdaptiveStorage.ThingClass';$rice=$parcel.def-ceq'RawRice';$wood=$parcel.def-ceq'WoodLog'
            $expected=$(if($route-ceq'effective-recursive'){-not($s.asfFirst -and $rice)}elseif($route-ceq'interface-fixed'){-not$asf -or -not$rice}else{-not$asf -or $wood})
            $invokes=$s.unknownWorker -and (($route-ceq'effective-recursive' -and $s.asfFirst) -or ($route-ceq'interface-fixed' -and $asf))
            Assert-AF ($n.parentId-eq$m.thing.thingId -and $n.subjectId-eq$parcel.thingId -and $n.route-ceq$route -and $n.actual-eq$expected -and $n.expected-eq$expected) 'ordered actual native predicate control differs.' $Problems
            Test-AFCounter $n.before $count $Problems;if($invokes){$count++};Test-AFCounter $n.after $count $Problems
        }}}
        Test-AFCounter $s.afterControl $count $Problems
        if($Problems.Count-ne$initialProblems){return}
    }
    Assert-AF (@($physical|Select-Object -Unique).Count-eq20 -and $physical-cnotcontains$p.scenes[0].linked.actor.thingId) 'physical scenes reuse identities or alias the actor.' $Problems
    $plan=@(Get-AFRecordPlan $p $Problems)
    Assert-AF ($plan.Count-eq1176 -and $p.records.Count-eq$plan.Count) 'complete source record catalog must have 1176 ordered rows.' $Problems
    if($Problems.Count-ne$initialProblems){return}
    for($i=0;$i-lt[math]::Min($plan.Count,$p.records.Count);$i++){$row=$p.records[$i];$spec=$plan[$i];$data=ConvertFrom-Json -InputObject $row.data;Assert-AF ($row.sequence-eq($i+1) -and $row.tick-eq$p.startedTick -and $row.kind-ceq$spec.kind -and $row.trial-ceq$spec.trial -and (Test-ABSame $data $spec.data)) ('retained raw payload/order differs at '+($i+1)) $Problems;if($Problems.Count-ne$initialProblems){return}}
}

# Frozen native/ASF/HD/harness metadata profile, resolved against actual private DLLs.
function Get-AFAssemblyCatalog {
    return @(foreach($spec in (Get-ABAssemblyCatalog)){
        if($spec.StartsWith('HaulersDream.RuntimeHarness|')){
            'HaulersDream.RuntimeHarness|0.1.0.0|cdd329d2-1313-4b73-be6d-145979a79fdc|C64B6DCBEC84E5CB7758703A397DAB94863A47DAC54D77CF46BAB903B5324E11|Mods/HaulersDreamRuntimeHarness/Assemblies/HaulersDream.RuntimeHarness.dll'
        }else{$spec}
    })
}
function Get-AFBindingCatalog {
    # First forty bridge bindings are unchanged. Setup intentionally rebinds Count/ContentsPacked.
    return @((Get-ABBindingCatalog)[0..39])+@(
        'Verse.ThingFilter.disallowedSpecialFilters;token=67125235;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'Verse.SpecialThingFilterDef.workerInt;token=67125226;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'AdaptiveStorage.ThingCollection.get_Count;token=100663915;mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ThingClass.get_ContentsPacked;token=100663818;mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'Verse.StorageGroupManager.NewGroup;token=100672136;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'Verse.StorageGroupManager.HasStorageGroup;token=100672140;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'Verse.StorageGroupManager.get_StorageGroupsForReading;token=100672134;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'RimWorld.StorageGroup.InitFrom;token=100723763;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'RimWorld.StorageGroup.GetParentStoreSettings;token=100723766;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'RimWorld.StorageGroupUtility.SetStorageGroup;token=100723786;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'RimWorld.Building_Storage.GetStoreSettings;token=100723591;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'RimWorld.Building_Storage.GetParentStoreSettings;token=100723592;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'RimWorld.StorageSettings.AllowedToAccept;token=100723831;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'Verse.ThingFilter.Allows;token=100685753;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'RimWorld.SpecialThingFilterWorker_Fresh.Matches;token=100743315;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'HaulersDream.RuntimeHarness.Cap03FilterUnknownWorker..ctor;token=100664350;mvid=cdd329d2-1313-4b73-be6d-145979a79fdc',
        'HaulersDream.RuntimeHarness.Cap03FilterUnknownWorker.Matches;token=100664351;mvid=cdd329d2-1313-4b73-be6d-145979a79fdc',
        'HaulersDream.RuntimeHarness.Cap03FilterUnknownWorker.AlwaysMatches;token=100664352;mvid=cdd329d2-1313-4b73-be6d-145979a79fdc',
        'HaulersDream.RuntimeHarness.Cap03FilterUnknownWorker.CanEverMatch;token=100664353;mvid=cdd329d2-1313-4b73-be6d-145979a79fdc'
    )
}
function Get-AFPatchCatalog {return @(Get-ABPatchCatalog)}
function Get-AFGlobalAssertions($Value,$Problems){
    $native=Get-ABOne $Value.assemblies name Assembly-CSharp $Problems;if($null-eq$native){return @()}
    $path=$native.path.Replace('\','/');$suffix='/RimWorldWin64_Data/Managed/Assembly-CSharp.dll'
    if(-not$path.EndsWith($suffix) -or $path.Contains('/../') -or $path.Contains('/./')){$Problems.Add('CAP03-A native path does not describe the private runtime.');return @()}
    $runtime=$path.Substring(0,$path.Length-$suffix.Length);$base=$runtime.Substring(0,$runtime.Length-8)
    if($runtime-cnotmatch('/haulersdream-runtime-tests/'+[regex]::Escape($Value.runId)+'/runtime$')){$Problems.Add('CAP03-A assembly paths are not attached to this private run.')}
    $result=New-Object 'System.Collections.Generic.List[object]'
    foreach($spec in @(@('private-runtime-data-path',($runtime+'/RimWorldWin64_Data')),@('private-save-data-path',($base+'/SaveData').Replace('/','\')),@('private-mod-directory',($runtime+'/Mods').Replace('/','\')),@('private-player-log',($base+'/evidence/Player.log')),@('case-supported','CAP03-A'),@('negative-control-supported','None'),@('installed-version-file-matches-manifest','1.6.4871 rev590'),@('harness-compiled-against-running-game','5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A'))){$result.Add([pscustomobject]@{id=$spec[0];observed=$spec[1]})}
    $map=Get-ABOne $Value.assertions id real-map-initialized $Problems
    if($null-ne$map){if($map.observed-cnotmatch'^size=\(([1-9][0-9]*), 1, ([1-9][0-9]*)\); maps=([1-9][0-9]*)$'){$Problems.Add('CAP03-A actual initialized map dimensions/count malformed.')}else{$sx=0;$sz=0;$maps=0;$ax=$Matches[1];$az=$Matches[2];$am=$Matches[3];if(-not[int]::TryParse($ax,[ref]$sx) -or -not[int]::TryParse($az,[ref]$sz) -or -not[int]::TryParse($am,[ref]$maps) -or $maps-ne$Value.storageProjectionFilters.gameMaps){$Problems.Add('CAP03-A map census/dimension exceeds native range or differs.')}};$result.Add([pscustomobject]@{id='real-map-initialized';observed=$map.observed})}
    $result.Add([pscustomobject]@{id='exact-active-mod-count';observed='actual=6; expected=6'})
    $mods=@(@('brrainz.harmony','Mods/Harmony'),@('ludeon.rimworld','Data/Core'),@('adaptive.storage.framework','Mods/AdaptiveStorageFramework'),@('sbz.neatstorage','Mods/NeatStorage'),@('giwaffed.haulersdream','Mods/HaulersDream'),@('giwaffed.haulersdream.runtimeharness','Mods/HaulersDreamRuntimeHarness'))
    if($Value.mods.Count-ne6){$Problems.Add('CAP03-A requires exactly six active mods.')}
    for($i=0;$i-lt6;$i++){$mod=$mods[$i];$root=($runtime+'/'+$mod[1]).Replace('/','\');if($i-ge$Value.mods.Count -or $Value.mods[$i].packageId-cne$mod[0] -or $Value.mods[$i].rootPath-cne$root){$Problems.Add('CAP03-A actual package/root order differs.')};$result.Add([pscustomobject]@{id='mod-order-root-'+$i;observed=$mod[0]+' @ '+$root})}
    $catalog=Get-AFAssemblyCatalog
    if($Value.assemblies.Count-ne$catalog.Count){$Problems.Add('CAP03-A loaded assembly catalog differs from the eighteen reviewed files.')}
    for($i=0;$i-lt$catalog.Count;$i++){
        $spec=$catalog[$i].Split('|');$row=Get-ABOne $Value.assemblies name $spec[0] $Problems;$expected=($runtime+'/'+$spec[4]).Replace('/','\')
        if($null-eq$row -or $row.path-cne$expected -or $row.assemblyVersion-cne$spec[1] -or $row.moduleVersionId-cne$spec[2] -or $row.sha256-cne$spec[3] -or $Value.assemblies[$i].name-cne$spec[0]){$Problems.Add('CAP03-A exact loaded file/module/order differs: '+$spec[0])}
        $result.Add([pscustomobject]@{id='single-assembly-'+$spec[0];observed='count=1'})
        $result.Add([pscustomobject]@{id='assembly-identity-'+$spec[0];observed=$expected+'; version='+$spec[1]+'; sha256='+$spec[3]})
    }
    $ticks=Get-ABOne $Value.assertions id real-game-ticks-advanced $Problems
    if($null-ne$ticks){$elapsed=0;if($ticks.observed-cnotmatch'^elapsedTicks=([0-9]+)$' -or -not[int]::TryParse($Matches[1],[ref]$elapsed) -or $elapsed-lt5 -or $elapsed-ge$Value.storageProjectionFilters.startedTick){$Problems.Add('CAP03-A actual ready tick delay is invalid.')};$result.Add([pscustomobject]@{id='real-game-ticks-advanced';observed=$ticks.observed})}
    $result.Add([pscustomobject]@{id='no-unity-errors-after-harness-start';observed='observedErrors=0; threaded capture through terminal-result boundary'})
    return $result.ToArray()
}
function Test-AFIdentity($Value,$Problems){
    $p=$Value.storageProjectionFilters;$expected=Get-AFGlobalAssertions $Value $Problems
    if($Value.assertions.Count-ne$expected.Count){$Problems.Add('CAP03-A global assertion catalog contains missing/duplicate/extra entries.')}
    for($i=0;$i-lt$expected.Count;$i++){$want=$expected[$i];$a=Get-ABOne $Value.assertions id $want.id $Problems;if($null-eq$a -or -not$a.passed -or $a.observed-cne$want.observed -or $Value.assertions[$i].id-cne$want.id){$Problems.Add('CAP03-A global assertion outcome/value/order differs: '+$want.id)}}
    $bound=@('HaulersDream','HaulersDream.Core','Assembly-CSharp','0Harmony','HaulersDream.RuntimeHarness','AdaptiveStorageFramework')
    if($p.assemblies.Count-ne6){$Problems.Add('CAP03-A binding assembly census must have six exact modules.')}
    for($i=0;$i-lt6;$i++){
        $a=Get-ABOne $Value.assemblies name $bound[$i] $Problems;if($null-eq$a -or $i-ge$p.assemblies.Count){continue};$row=$p.assemblies[$i]
        if($row.name-cne($a.name+', Version='+$a.assemblyVersion+', Culture=neutral, PublicKeyToken=null') -or $row.mvid-cne$a.moduleVersionId -or $row.path-cne$a.path -or $row.sha256-cne$a.sha256){$Problems.Add('CAP03-A bound module does not match its actual loaded file: '+$bound[$i])}
    }
    if(-not(Test-ABSame $p.bindings (Get-AFBindingCatalog)) -or -not(Test-ABSame $p.patchInventory (Get-AFPatchCatalog))){$Problems.Add('CAP03-A exact native/ASF/HD metadata binding or five-patch owner/kind/module/token catalog changed.')}
    if($p.nativeIdentity-cne'1.6.4871 rev591;Assembly-CSharp, Version=1.6.9676.17735, Culture=neutral, PublicKeyToken=null;mvid=61e41735-6189-4da4-9d21-0260257b5097' -or $p.asfIdentity-cne'AdaptiveStorageFramework, Version=1.2.4.0, Culture=neutral, PublicKeyToken=null;mvid=d7c605b3-e59a-4b26-af97-594bc5417053'){$Problems.Add('CAP03-A native/provider identity provenance differs.')}
    $inputSpecs=@(@('giwaffed.haulersdream.runtimeharness','Defs/Cap03FilterDefs.xml','470E370CDD776D73564EB66CEDE2C545D084C76AF80D39521CB82E095A866CCF'),@('adaptive.storage.framework','Defs/ThingDefBase.xml','DECE4A55D724F4D1EE23B6F21C531BB4F5EF627EC93E4A4D02C7565FE73A242B'),@('sbz.neatstorage','1.6/Defs/ThingDefs_Buildings/Buildings_CrateAndPallet.xml','9B9F757E13A16327250B5F62BA15DC457F40F4B45DD7500CA9DEA0F56BC4D582'),@('ludeon.rimworld','Defs/Misc/SpecialThingFilterDefs/SpecialThingFilters.xml','A3D81DA38D9DE4145D46CFB46AB8891F3AC7648CB73B481763BE88B48A79806F'),@('ludeon.rimworld','Defs/ThingDefs_Buildings/Buildings_Furniture.xml','CEA362CA9451F0762F8A104B2344BD540B5F6E8663DD9A4E75F3C39B46607C55'))
    if($p.inputs.Count-ne5){$Problems.Add('CAP03-A requires all five exact harness/native/ASF/Neat definition inputs.')}
    for($i=0;$i-lt5;$i++){
        if($i-ge$p.inputs.Count){break};$spec=$inputSpecs[$i];$row=$p.inputs[$i];$mod=Get-ABOne $Value.mods packageId $spec[0] $Problems
        if($row.packageId-cne$spec[0] -or $row.relativePath-cne$spec[1] -or $row.sha256-cne$spec[2] -or $null-eq$mod -or $row.root-cne$mod.rootPath){$Problems.Add('CAP03-A input identity/order does not match the loaded private package.')}
    }
}
function Test-AFEventsCore($Value,$Events,[string]$RunId,$Problems){
    if(-not(Test-AFShapeCore $Value $Problems)){return}
    if($Events-isnot[array] -or $Events.Count-eq0){$Problems.Add('CAP03-A raw event array is absent.');return}
    $p=$Value.storageProjectionFilters;$before=$Problems.Count;$lastTick=-1;$lastUtc=[datetimeoffset]::MinValue;$i=0
    foreach($row in $Events){
        $i++;if(-not(Test-ABFields $row @{sequence='int';runId='string';caseId='string';utc='utc';phase='string';detail='string';tick='int'} 'CAP03-A raw event' $Problems)){continue}
        $utc=[datetimeoffset]$row.utc
        if($row.sequence-ne$i -or $row.caseId-cne'CAP03-A' -or $row.runId-cne$RunId -or $row.runId-cne$Value.runId -or $row.tick-lt$lastTick -or $row.tick-lt-1 -or $utc-lt$lastUtc -or $utc-lt[datetimeoffset]$Value.startedUtc){$Problems.Add('CAP03-A raw event sequence/tick/UTC/run identity is not continuous.')}
        $lastTick=$row.tick;$lastUtc=$utc
    }
    if($Problems.Count-ne$before){return}
    $plan=@(Get-AFRecordPlan $p $Problems);$component=@($Events|Where-Object {$_.phase.StartsWith('cap03-a-') -and $_.phase-cne'cap03-a-result'})
    if($component.Count-ne$plan.Count -or $component.Count-ne$p.records.Count){$Problems.Add('CAP03-A raw/retained component catalogs have missing, repeated or extra rows.')}
    for($i=0;$i-lt[math]::Min($component.Count,$plan.Count);$i++){
        $event=$component[$i];$record=ConvertFrom-Json -InputObject $event.detail;$expected=$plan[$i]
        if(-not(Test-ABFields $record @{sequence='int';tick='int';kind='string';trial='nullable-string';data='string'} 'CAP03-A raw record' $Problems)){continue}
        if($record.sequence-ne($i+1) -or $record.tick-ne$p.startedTick -or $event.tick-ne$record.tick -or $event.phase-cne('cap03-a-'+$expected.kind) -or $record.kind-cne$expected.kind -or $record.trial-cne$expected.trial -or $i-ge$p.records.Count -or -not(Test-ABSame $record $p.records[$i])){$Problems.Add('CAP03-A raw component record does not reconcile with its retained identity/catalog.')}
        $payload=ConvertFrom-Json -InputObject $record.data
        if(-not(Test-ABSame $payload $expected.data)){$Problems.Add('CAP03-A raw '+$record.kind+' payload differs from its typed result/source-derived expectation.')}
        if($i-gt0 -and $event.sequence-ne($component[$i-1].sequence+1)){$Problems.Add('CAP03-A synchronous component observations are not contiguous.')}
    }
    $terminal=Get-ABOne $Events phase cap03-a-result $Problems
    if($null-ne$terminal){$data=ConvertFrom-Json -InputObject $terminal.detail;if(-not(Test-ABSame $data $p) -or $terminal.tick-ne$p.finishedTick -or $component.Count-eq0 -or $terminal.sequence-ne($component[-1].sequence+1)){$Problems.Add('CAP03-A terminal component payload/boundary differs from the actual nested result.') }}
    $start=Get-ABOne $Events phase harness-start $Problems;$version=Get-ABOne $Events phase game-version-provenance $Problems;$new=Get-ABOne $Events phase new-game $Problems;$map=Get-ABOne $Events phase map-initialized $Problems
    $scenario=Get-ABOne $Events phase scenario-observed $Problems;$capture=Get-ABOne $Events phase error-capture-boundary $Problems;$end=Get-ABOne $Events phase terminal-result $Problems
    $globals=@($Events|Where-Object phase -CEQ assertion);$expectedGlobal=@(Get-AFGlobalAssertions $Value $Problems)
    if($globals.Count-ne$expectedGlobal.Count){$Problems.Add('CAP03-A complete global assertion event catalog differs.')}
    for($i=0;$i-lt[math]::Min($globals.Count,$expectedGlobal.Count);$i++){
        $expected=$expectedGlobal[$i];$observed=$expected.observed
        # Finish rewrites the retained clean assertion with the final closed-capture count without emitting another assertion.
        if($expected.id-ceq'no-unity-errors-after-harness-start'){$observed='observedErrors=0'}
        if($globals[$i].detail-cne($expected.id+': passed; '+$observed)){$Problems.Add('CAP03-A raw global assertion outcome/detail/order differs: '+$expected.id)}
    }
    if($null-eq$start -or $null-eq$version -or $null-eq$new -or $null-eq$map -or $null-eq$terminal -or $null-eq$scenario -or $null-eq$capture -or $null-eq$end -or $component.Count-eq0 -or $globals.Count-ne$expectedGlobal.Count){return}
    $ticks=Get-ABOne $Value.assertions id real-game-ticks-advanced $Problems;$elapsed=0
    if($null-ne$ticks -and $ticks.observed-cmatch'^elapsedTicks=([0-9]+)$'){[int]::TryParse($Matches[1],[ref]$elapsed)|Out-Null}
    $mapAssertion=Get-ABOne $Value.assertions id real-map-initialized $Problems;$size=''
    if($null-ne$mapAssertion -and $mapAssertion.observed-cmatch'^size=(.*); maps=[0-9]+$'){$size=$Matches[1]}
    if($start.detail-cne'case=CAP03-A; expectedBehavior=satisfied' -or $start.sequence-ne1 -or $start.tick-ne-1 -or $version.tick-ne-1 -or $version.detail-cne('Version.txt='+$Value.installedVersionFile+'; executing assembly reports='+$Value.executingGameVersion) -or $new.tick-ne0 -or $new.detail-cne'GameComponent lifecycle callback received.' -or $map.tick-lt1 -or $map.detail-cne('tick='+$map.tick+'; size='+$size) -or $p.startedTick-$map.tick-ne$elapsed){$Problems.Add('CAP03-A startup/native-version/new-map/ready-tick provenance differs.')}
    # Bootstrap emits the first six assertions, version provenance, then the two version checks, new-game,
    # map/mod/assembly checks, map initialized, ready ticks, the synchronous component, and the final clean assertion.
    $outer=New-Object 'System.Collections.Generic.List[object]';$outer.Add($start)
    foreach($row in $globals[0..5]){$outer.Add($row)};$outer.Add($version)
    foreach($row in $globals[6..7]){$outer.Add($row)};$outer.Add($new)
    for($i=8;$i-lt$globals.Count-2;$i++){$outer.Add($globals[$i])}
    $outer.Add($map);$outer.Add($globals[-2]);foreach($row in $component){$outer.Add($row)};$outer.Add($terminal)
    $outer.Add($globals[-1]);$outer.Add($scenario);$outer.Add($capture);$outer.Add($end)
    if($outer.Count-ne$Events.Count){$Problems.Add('CAP03-A outer lifecycle has unexpected/error/extra phases.')}
    for($i=0;$i-lt$outer.Count;$i++){if($outer[$i].sequence-ne$i+1){$Problems.Add('CAP03-A outer lifecycle/global setup/component/closure chronology is out of order.');break}}
    if($globals[-2].tick-ne$p.startedTick -or $globals[-1].tick-ne$p.finishedTick -or $scenario.tick-ne$p.finishedTick -or $capture.tick-ne$p.finishedTick -or $end.tick-ne$p.finishedTick -or $scenario.detail-cne'case=CAP03-A; expected=satisfied; requestedBehaviorSatisfied=True; expectationMatched=True' -or $capture.detail-cne'Threaded capture closed before terminal result; earlier startup and shutdown require whole Player.log review.' -or $end.detail-cne($Value.status+': '+$Value.detail) -or $end.sequence-ne$Events.Count -or [datetimeoffset]$Value.finishedUtc-lt[datetimeoffset]$scenario.utc -or [datetimeoffset]$Value.finishedUtc-gt[datetimeoffset]$capture.utc){$Problems.Add('CAP03-A result/scenario/error-closure/last-terminal ordering or error reconciliation is contradictory.')}
}

function Test-AsfFilterShape($Value,$Problems){
    $old=$ErrorActionPreference;$ErrorActionPreference='Stop'
    try{return Test-AFShapeCore $Value $Problems}catch{$Problems.Add('CAP03-A malformed evidence shape: '+$_.Exception.Message);return $false}finally{$ErrorActionPreference=$old}
}
function Test-AsfFilterEvidence($Value,[string]$ExpectedBehavior,$Problems){
    $old=$ErrorActionPreference;$ErrorActionPreference='Stop'
    try{Test-AFECore $Value $ExpectedBehavior $Problems|Out-Null}catch{$Problems.Add('CAP03-A malformed/inconsistent evidence: '+$_.Exception.Message)}finally{$ErrorActionPreference=$old}
}
function Test-AsfFilterEvents($Value,$Events,[string]$RunId,$Problems){
    $old=$ErrorActionPreference;$ErrorActionPreference='Stop'
    try{Test-AFEventsCore $Value $Events $RunId $Problems|Out-Null}catch{$Problems.Add('CAP03-A malformed/inconsistent raw evidence: '+$_.Exception.Message)}finally{$ErrorActionPreference=$old}
}
