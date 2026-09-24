# CAP03-B evidence consumer. No file or game writes. Windows PowerShell 5.1 compatible.
# Only the reviewed six-slot ASF member-budget component; operation charges are not callback counts.
function Test-ABFields($Value,[hashtable]$Schema,[string]$Label,$Problems) {
    if($null-eq$Value -or $Value-is[array] -or $Value-isnot[pscustomobject]){$Problems.Add($Label+' must be an object.');return $false}
    $okay=$true
    foreach($name in $Schema.Keys){
        $property=$Value.PSObject.Properties[$name]
        if($null-eq$property){$Problems.Add($Label+' lacks '+$name+'.');$okay=$false;continue}
        $item=$property.Value;$type=$Schema[$name]
        if($type.StartsWith('nullable-')){if($null-eq$item){continue};$type=$type.Substring(9)}
        $valid=switch($type){
            string {$item-is[string]}
            object {$null-ne$item -and $item-is[pscustomobject] -and $item-isnot[array]}
            array {$item-is[array]}
            bool {$item-is[bool]}
            long {$item-is[int] -or $item-is[long]}
            int {($item-is[int] -or $item-is[long]) -and $item-ge[int]::MinValue -and $item-le[int]::MaxValue}
            number {($item-is[int] -or $item-is[long] -or $item-is[single] -or $item-is[double] -or $item-is[decimal]) -and -not[double]::IsNaN([double]$item) -and -not[double]::IsInfinity([double]$item) -and [math]::Abs([double]$item)-le[single]::MaxValue}
            utc {$date=[datetimeoffset]::MinValue;($item-is[datetime] -and $item.Kind-eq[DateTimeKind]::Utc) -or ($item-is[datetimeoffset] -and $item.Offset-eq[timespan]::Zero) -or ($item-is[string] -and $item-cmatch'^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,7})?(?:Z|\+00:00)$' -and [datetimeoffset]::TryParse($item,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::None,[ref]$date))}
            default {$false}
        }
        if(-not$valid){$Problems.Add($Label+'.'+$name+' has invalid '+$Schema[$name]+' type.');$okay=$false}
    }
    return $okay
}
function Read-ABCell([string]$Text,$Problems){
    $cx=0;$cz=0
    if($Text-cnotmatch'^\((-?[0-9]+), 0, (-?[0-9]+)\)$'){$Problems.Add('CAP03-B malformed native cell.');return $null}
    $sx=$Matches[1];$sz=$Matches[2]
    if(-not[int]::TryParse($sx,[ref]$cx) -or -not[int]::TryParse($sz,[ref]$cz)){$Problems.Add('CAP03-B native cell overflows Int32.');return $null}
    return @{x=$cx;z=$cz}
}
function Get-ABCanonical($Value){
    if($null-eq$Value){return 'null'}
    if($Value-is[array]){return '['+(@(foreach($item in $Value){Get-ABCanonical $item})-join',')+']'}
    if($Value-is[string] -or $Value-is[ValueType]){return ConvertTo-Json -InputObject $Value -Compress -Depth 100}
    if($Value-is[pscustomobject]){return '{'+(@(foreach($name in @($Value.PSObject.Properties.Name|Sort-Object -CaseSensitive)){(ConvertTo-Json -InputObject $name -Compress)+':'+(Get-ABCanonical $Value.PSObject.Properties[$name].Value)})-join',')+'}'}
    return ConvertTo-Json -InputObject $Value -Compress -Depth 100
}
function Test-ABSame($Left,$Right){return (Get-ABCanonical $Left)-ceq(Get-ABCanonical $Right)}
function Test-ABStrings($Rows,[string]$Label,$Problems){foreach($row in $Rows){if($row-isnot[string] -or [string]::IsNullOrWhiteSpace($row)){$Problems.Add('CAP03-B '+$Label+' has nontext/empty entry.')}}}
function Test-ABStatusShape($Value,$Problems){Test-ABFields $Value @{usable='bool';observation='string';capability='string';reason='string'} 'CAP03-B status' $Problems|Out-Null}
function Test-ABComplete($Value){return $Value.usable -and $Value.observation-ceq'Complete' -and $Value.capability-ceq'Supported' -and $Value.reason-ceq'None'}
function Get-ABKinds{return @('Members','Coordinates','GridEntries','NativeGridVisits','NativeCalls','Filters','Compatibility','ProviderVisits','CellLimitCalls','Reservations','Reachability','GuardChecks','Preparation','OutputRecords')}
function Test-ABWorkShape($Rows,$Problems){foreach($row in $Rows){Test-ABFields $row @{kind='string';value='long'} 'CAP03-B work' $Problems|Out-Null}}
function Test-ABThingShape($Value,$Problems){if(Test-ABFields $Value @{thingId='int';def='string';count='int';stackLimit='int';cell='string';spawned='bool';mapId='nullable-int';faction='nullable-string';holderType='nullable-string';heldByFixtureMap='bool';destroyed='bool';stuff='nullable-string';hitPoints='int'} 'CAP03-B Thing' $Problems){Read-ABCell $Value.cell $Problems|Out-Null}}
function Test-ABFilterShape($Value,$Problems){
    if(Test-ABFields $Value @{type='string';onlySpecial='bool';allowedDefs='array';disallowedSpecials='array';hitPointsMin='number';hitPointsMax='number';mentalBreakMin='number';mentalBreakMax='number';qualities='string'} 'CAP03-B filter' $Problems){
        Test-ABStrings $Value.allowedDefs 'allowed definitions' $Problems
        foreach($name in $Value.disallowedSpecials){if($null-ne$name -and $name-isnot[string]){$Problems.Add('CAP03-B invalid special filter entry.')}}
    }
}
function Test-ABPhysicalShape($Value,$Problems){
    if(-not(Test-ABFields $Value @{mapId='int';tick='int';nativeMaximum='int';gridCount='int';cell='string';parentKey='string';groupIdentity='string';registryIdentity='string';settingsIdentity='string';filterIdentity='string';declaredFixedIdentity='string';declaredFilterIdentity='string';interfaceFixedIdentity='string';interfaceFilterIdentity='string';priority='string';storageGroupTag='string';actualCrateClass='string';crateDefSource='string';bookClass='string';bookTitle='string';crateWidth='int';crateDepth='int';defMaximum='int';novelShortHash='int';cargoShortHash='int';registered='bool';unlinked='bool';settingsOwnerIsParent='bool';actorStill='bool';parentEverStorable='bool';novelEffectiveAccepted='bool';novelDeclaredAccepted='bool';novelInterfaceAccepted='bool';cargoEffectiveAccepted='bool';cargoDeclaredAccepted='bool';cargoInterfaceAccepted='bool';parent='object';actor='object';novel='object';cargo='object';items='array';allGrid='array';effectiveFilter='object';declaredFixedFilter='object';interfaceFixedFilter='object';parentCompTypes='array';novelCompTypes='array';cargoCompTypes='array';registry='object'} 'CAP03-B physical scene' $Problems)){return}
    Read-ABCell $Value.cell $Problems|Out-Null
    foreach($thing in @($Value.parent,$Value.actor,$Value.novel,$Value.cargo)+@($Value.items)+@($Value.allGrid)){Test-ABThingShape $thing $Problems}
    foreach($filter in @($Value.effectiveFilter,$Value.declaredFixedFilter,$Value.interfaceFixedFilter)){Test-ABFilterShape $filter $Problems}
    foreach($name in @('parentCompTypes','novelCompTypes','cargoCompTypes')){Test-ABStrings $Value.$name $name $Problems}
    $registry=$Value.registry
    if(-not(Test-ABFields $registry @{collectionType='string';count='int';cellWiseCount='int';cellCount='int';slotLimit='int';anyFree='bool';packed='bool';performanceFish='bool';novelOverridesStack='bool';cargoOverridesStack='bool';acceptsNovelDef='bool';acceptsCargoDef='bool';occupied='array';acceptedDefs='array';acceptedShortHashes='array';members='array'} 'CAP03-B registry' $Problems)){return}
    Test-ABStrings $registry.acceptedDefs 'accepted definitions' $Problems
    foreach($cell in $registry.occupied){if($cell-isnot[string]){$Problems.Add('CAP03-B occupied cell is not text.')}else{Read-ABCell $cell $Problems|Out-Null}}
    foreach($hash in $registry.acceptedShortHashes){if(($hash-isnot[int] -and $hash-isnot[long]) -or $hash-lt1 -or $hash-gt65535){$Problems.Add('CAP03-B invalid definition short hash.')}}
    foreach($member in $registry.members){
        if(Test-ABFields $member @{index='int';thingId='int';shortHash='int';count='int';stackLimit='int';indexOf='int';mapId='nullable-int';storingParentId='nullable-int';def='string';mapCell='string';mapPosition='string';holderType='nullable-string';spawned='bool';contains='bool';memberValid='bool';targetValid='bool';everStorable='bool';compTypes='array'} 'CAP03-B registry member' $Problems){Read-ABCell $member.mapCell $Problems|Out-Null;Read-ABCell $member.mapPosition $Problems|Out-Null;Test-ABStrings $member.compTypes 'member comps' $Problems}
    }
}
function Test-ABCellShape($Value,$Problems){
    if(-not(Test-ABFields $Value @{fixtureScene='string';fixtureStage='string';observationId='long';query='string';session='string';mapId='int';tick='int';generation='long';cell='string';parentKey='nullable-string';groupKey='string';provider='nullable-string';vacantKey='nullable-string';maximumSlots='nullable-int';itemCount='nullable-int';vacantSlots='nullable-long';gridEntries='nullable-int';status='object';stacks='array';work='array'} 'CAP03-B cell' $Problems)){return}
    Read-ABCell $Value.cell $Problems|Out-Null;Test-ABStatusShape $Value.status $Problems;Test-ABWorkShape $Value.work $Problems
    foreach($stack in $Value.stacks){Test-ABFields $stack @{key='string';thingId='int';def='string';count='int';stackLimit='int';deficit='long';providerTargetValid='nullable-bool'} 'CAP03-B stack resource' $Problems|Out-Null}
}
function Test-ABEligibilityShape($Value,$Problems){
    if(-not(Test-ABFields $Value @{fixtureScene='string';fixtureStage='string';observationId='long';parcelId='string';status='object';state='string';vacantEligible='bool';unitsPerNewStack='nullable-int';predicates='array';topUps='array';work='array'} 'CAP03-B eligibility' $Problems)){return}
    Test-ABStatusShape $Value.status $Problems;Test-ABWorkShape $Value.work $Problems
    foreach($predicate in $Value.predicates){Test-ABFields $predicate @{name='string';state='string';reason='string';targetId='nullable-int'} 'CAP03-B predicate' $Problems|Out-Null}
    foreach($edge in $Value.topUps){Test-ABFields $edge @{key='string';targetId='int';units='long'} 'CAP03-B edge' $Problems|Out-Null}
}
function Test-ABShapeCore($Value,$Problems){
    $before=$Problems.Count
    if(-not(Test-ABFields $Value @{storageProjectionBudget='object';caseId='string';runId='string';status='string';detail='string';startedUtc='utc';finishedUtc='utc';executingGameVersion='string';installedVersionFile='string';unityErrorsObserved='int';negativeControl='nullable-object';mods='array';assemblies='array';assertions='array'} 'CAP03-B global result' $Problems)){return $false}
    foreach($row in $Value.mods){Test-ABFields $row @{packageId='string';rootPath='string'} 'CAP03-B loaded mod' $Problems|Out-Null}
    foreach($row in $Value.assemblies){Test-ABFields $row @{name='string';path='string';sha256='string';assemblyVersion='string';moduleVersionId='string'} 'CAP03-B loaded assembly' $Problems|Out-Null}
    foreach($row in $Value.assertions){Test-ABFields $row @{id='string';passed='bool';observed='string'} 'CAP03-B global assertion' $Problems|Out-Null}
    $p=$Value.storageProjectionBudget
    if(-not(Test-ABFields $p @{caseId='string';contract='string';expectedBehavior='string';status='string';scope='string';error='nullable-string';allCap03ComponentsSatisfied='bool';pendingControls='array';fixtureValid='bool';requestedBehaviorSatisfied='bool';expectationMatched='bool';startedTick='int';finishedTick='int';mainThread='int';mapId='int';sessionId='string';nativeIdentity='nullable-string';asfIdentity='nullable-string';gameMapIds='array';assemblies='array';bindings='array';patchInventory='array';inputs='array';catalogStatus='object';preparationStatus='object';preparationAllowance='long';preparationCharged='long';preparationReady='bool';footprintWarmup='bool';indexedZoneCells='int';initialPhysical='object';finalPhysical='object';trials='array';assertions='array';records='array'} 'CAP03-B result' $Problems)){return $false}
    foreach($rows in @('pendingControls','bindings','patchInventory')){Test-ABStrings $p.$rows $rows $Problems}
    foreach($mapId in $p.gameMapIds){if(($mapId-isnot[int] -and $mapId-isnot[long]) -or $mapId-lt0 -or $mapId-gt[int]::MaxValue){$Problems.Add('CAP03-B invalid actual map ID.')}}
    foreach($row in $p.assemblies){Test-ABFields $row @{name='string';path='string';mvid='string';sha256='string'} 'CAP03-B bound assembly' $Problems|Out-Null}
    foreach($row in $p.inputs){Test-ABFields $row @{packageId='string';root='string';relativePath='string';sha256='string'} 'CAP03-B input' $Problems|Out-Null}
    foreach($row in $p.assertions){Test-ABFields $row @{sequence='int';id='string';kind='string';passed='bool';detail='string'} 'CAP03-B nested assertion' $Problems|Out-Null}
    foreach($row in $p.records){Test-ABFields $row @{sequence='int';tick='int';kind='string';trial='nullable-string';data='string'} 'CAP03-B record' $Problems|Out-Null}
    Test-ABStatusShape $p.catalogStatus $Problems;Test-ABStatusShape $p.preparationStatus $Problems
    Test-ABPhysicalShape $p.initialPhysical $Problems;Test-ABPhysicalShape $p.finalPhysical $Problems
    foreach($trial in $p.trials){
        if(-not(Test-ABFields $trial @{id='string';query='string';scopeIdentity='nullable-string';parcelId='string';error='nullable-string';subjectId='int';completed='bool';disposed='bool';defaultAllowance='array';pageAllowance='array';openWork='array';openStatus='object';page='object';eligibility='object';before='object';after='object';used='array'} 'CAP03-B trial' $Problems)){continue}
        foreach($name in @('defaultAllowance','pageAllowance','openWork')){Test-ABWorkShape $trial.$name $Problems}
        Test-ABStatusShape $trial.openStatus $Problems;Test-ABEligibilityShape $trial.eligibility $Problems;Test-ABPhysicalShape $trial.before $Problems;Test-ABPhysicalShape $trial.after $Problems
        foreach($used in $trial.used){if(Test-ABFields $used @{stage='string';cumulative='array'} 'CAP03-B cumulative usage' $Problems){Test-ABWorkShape $used.cumulative $Problems}}
        $page=$trial.page
        if(Test-ABFields $page @{status='object';requestedCellsComplete='bool';wholeGroupComplete='bool';hasCursor='bool';nextMember='nullable-int';nextCell='nullable-int';unresolvedTotal='int';unresolvedSampleStart='int';unresolvedSampleCount='int';operationWork='array';cells='array';unresolved='array'} 'CAP03-B page' $Problems){
            Test-ABStatusShape $page.status $Problems;Test-ABWorkShape $page.operationWork $Problems
            foreach($cell in $page.cells){Test-ABCellShape $cell $Problems}
            foreach($row in $page.unresolved){Test-ABFields $row @{member='nullable-string';cell='string';reason='string';memberOrdinal='nullable-int'} 'CAP03-B unresolved cell' $Problems|Out-Null}
        }
    }
    return $Problems.Count-eq$before
}
function Get-ABOne($Rows,[string]$Field,$Key,$Problems){$found=@($Rows|Where-Object {$_.PSObject.Properties[$Field].Value-ceq$Key});if($found.Count-ne1){$Problems.Add('CAP03-B requires one '+$Field+'='+$Key+'.');return $null};return $found[0]}
function Get-ABMaterialDefs{return @('Steel','WoodLog','Plasteel','Uranium','Jade','Gold')}
function Test-ABWork($Rows,$Expected,[string]$Label,$Problems){
    $kinds=Get-ABKinds
    if($Rows.Count-ne14){$Problems.Add('CAP03-B '+$Label+' must report all 14 work dimensions.');return}
    for($i=0;$i-lt14;$i++){if($Rows[$i].kind-cne$kinds[$i] -or $Rows[$i].value-ne$Expected[$i] -or $Rows[$i].value-lt0){$Problems.Add('CAP03-B '+$Label+' contradicts the independently derived '+$kinds[$i]+' charge.')}}
}
function Get-ABCosts($P,[string]$Operation,[string]$Trial){
    [long[]]$v=@(0L,0L,0L,0L,0L,0L,0L,0L,0L,0L,0L,0L,0L,0L)
    if($Operation-ceq'open'){$v[11]=$P.gameMapIds.Count;return ,$v}
    $scene=$P.initialPhysical;[long]$items=$scene.items.Count;[long]$grid=$scene.gridCount
    if($Operation-ceq'page'){$v[0]=1;$v[1]=2;$v[2]=2*$grid;$v[6]=2*$scene.parentCompTypes.Count+4*$items;$v[7]=2*(1+4*$items);$v[8]=2;$v[11]=6;$v[13]=2*8+130+3*$items+2;return ,$v}
    $low=$Trial-ceq'novel-low'
    $v[2]=$(if($low){1}else{2})*$grid
    $v[5]=4+$scene.effectiveFilter.disallowedSpecials.Count+3*$scene.declaredFixedFilter.disallowedSpecials.Count
    [long]$members=0;[long]$native=0
    foreach($member in $scene.registry.members){$cost=1L+2L*$member.compTypes.Count;$members+=$cost;if($member.everStorable){$native+=$cost}}
    if($scene.parentEverStorable){$native+=1L+2L*$scene.parentCompTypes.Count}
    $v[6]=$(if($low){2*$items}else{5*$items+$members});$v[7]=$(if($low){1+4*$items}else{2*(1+4*$items)+2*$items})
    $v[8]=$(if($low){1}else{2});$v[11]=$(if($low){2}else{3});$v[13]=10+5*$items
    if($Trial-ceq'steel-sufficient'){
        $steel=@($scene.registry.members|Where-Object def -CEQ Steel)[0]
        $v[6]+=3*$grid+$native+3+$steel.compTypes.Count;$v[7]+=2*$grid+1;$v[3]=4*$grid;$v[4]=1;$v[8]+=$grid+1;$v[9]=1;$v[10]=1;$v[5]++
    }
    return ,$v
}
function Test-ABPhysical($Budget,$Problems){
    $p=$Budget.initialPhysical;$r=$p.registry;$cell=Read-ABCell $p.cell $Problems
    if($null-eq$cell){return}
    if($p.mapId-ne$Budget.mapId -or $p.tick-ne$Budget.startedTick -or $p.nativeMaximum-ne6 -or $p.defMaximum-ne6 -or $p.crateWidth-ne1 -or $p.crateDepth-ne1 -or $p.actualCrateClass-cne'AdaptiveStorage.ThingClass' -or $p.crateDefSource-cne'sbz.neatstorage' -or $p.storageGroupTag-cne'Shelf' -or -not$p.unlinked -or -not$p.registered -or -not$p.settingsOwnerIsParent -or $p.priority-cne'Critical' -or $p.parentKey-cne('building:'+$p.parent.thingId)){$Problems.Add('CAP03-B concrete parent/scene identity or six-slot setup differs.')}
    $refs=@($p.groupIdentity,$p.registryIdentity,$p.settingsIdentity,$p.filterIdentity,$p.declaredFixedIdentity,$p.declaredFilterIdentity)
    # Identity() assigns these six distinct physical objects before creating any scope.
    if(-not(Test-ABSame $refs @('ref:1','ref:2','ref:3','ref:4','ref:5','ref:6')) -or @($refs|Select-Object -Unique).Count-ne6 -or @($refs|Where-Object {$_-cnotmatch'^ref:[1-9][0-9]*$'}).Count-ne0 -or $p.interfaceFixedIdentity-cne$p.declaredFixedIdentity -or $p.interfaceFilterIdentity-cne$p.declaredFilterIdentity -or -not(Test-ABSame $p.declaredFixedFilter $p.interfaceFixedFilter)){$Problems.Add('CAP03-B physical reference census/fixed interface routing is contradictory.')}
    if($p.items.Count-ne6 -or $p.allGrid.Count-ne7 -or $p.gridCount-ne7 -or @($p.items.thingId|Select-Object -Unique).Count-ne6 -or @($p.allGrid.thingId|Select-Object -Unique).Count-ne7){$Problems.Add('CAP03-B incomplete or duplicated physical grid census.')}
    $all=@($p.parent,$p.actor,$p.novel,$p.cargo)+@($p.items)
    if(@($all.thingId|Select-Object -Unique).Count-ne10){$Problems.Add('CAP03-B parent/actor/resident/parcel IDs must be distinct.')}
    foreach($thing in $all){if($thing.thingId-le0 -or -not$thing.spawned -or $thing.destroyed -or -not$thing.heldByFixtureMap -or $thing.holderType-cne'Verse.Map' -or $thing.mapId-ne$Budget.mapId -or $thing.count-le0 -or $thing.stackLimit-lt$thing.count){$Problems.Add('CAP03-B live physical quantity/map/custody is invalid.')}}
    foreach($thing in @($p.parent)+@($p.items)){$grid=Get-ABOne $p.allGrid thingId $thing.thingId $Problems;if($thing.cell-cne$p.cell -or $null-eq$grid -or -not(Test-ABSame $thing $grid)){$Problems.Add('CAP03-B real grid object does not reconcile with its physical row.')}}
    $defs=Get-ABMaterialDefs
    foreach($def in $defs){
        $item=Get-ABOne $p.items def $def $Problems;if($null-eq$item){continue}
        $limit=if($def-ceq'Gold'){500}else{75};$wanted=$limit-$(if($def-ceq'Steel'){7}else{0})
        if($item.stackLimit-ne$limit -or $item.count-ne$wanted -or $null-ne$item.faction -or $null-ne$item.stuff){$Problems.Add('CAP03-B actual native '+$def+' must have the single Steel deficit/full filler quantity.')}
    }
    if($p.parent.def-cne'sbz_SmallCrate' -or $p.parent.count-ne1 -or $p.parent.stackLimit-ne1 -or $p.parent.stuff-cne'WoodLog' -or $p.parent.hitPoints-le0 -or $p.parentEverStorable -or $p.actor.def-cne'Human' -or $p.actor.count-ne1 -or $p.actor.stackLimit-ne1 -or -not$p.actorStill -or $p.actor.cell-cne('('+[string]$cell.x+', 0, '+[string]([long]$cell.z-5)+')') -or [string]::IsNullOrWhiteSpace($p.actor.faction) -or $p.parent.faction-cne$p.actor.faction){$Problems.Add('CAP03-B actor/native wooden crate setup is contradictory.')}
    foreach($spec in @(@($p.novel,'Novel',1,1,-2),@($p.cargo,'Steel',7,75,2))){$thing=$spec[0];$expected='('+[string]([long]$cell.x+$spec[4])+', 0, '+[string]([long]$cell.z+3)+')';if($thing.def-cne$spec[1] -or $thing.count-ne$spec[2] -or $thing.stackLimit-ne$spec[3] -or $thing.cell-cne$expected -or $null-ne$thing.faction -or $null-ne$thing.stuff){$Problems.Add('CAP03-B outside subject quantity/position differs from actual fixture setup.')}}
    if($p.bookClass-cne'Verse.Book' -or [string]::IsNullOrWhiteSpace($p.bookTitle) -or $p.novelShortHash-lt1 -or $p.novelShortHash-gt65535 -or $p.cargoShortHash-lt1 -or $p.cargoShortHash-gt65535 -or $p.novelShortHash-eq$p.cargoShortHash){$Problems.Add('CAP03-B native generated Novel metadata is invalid.')}
    foreach($name in @('novelEffectiveAccepted','novelDeclaredAccepted','novelInterfaceAccepted','cargoEffectiveAccepted','cargoDeclaredAccepted','cargoInterfaceAccepted')){if(-not$p.$name){$Problems.Add('CAP03-B preceding native/effective/fixed filter did not accept both subjects.')}}
    $accepted=@('Gold','Jade','Novel','Plasteel','Steel','Uranium','WoodLog')
    if(-not(Test-ABSame $p.effectiveFilter.allowedDefs $accepted)){$Problems.Add('CAP03-B effective filter is not the seven ordinary enabled definitions.')}
    foreach($filter in @($p.effectiveFilter,$p.declaredFixedFilter,$p.interfaceFixedFilter)){
        if($filter.type-cne'Verse.ThingFilter' -or $filter.onlySpecial -or $filter.hitPointsMin-ne0 -or $filter.hitPointsMax-ne1 -or $filter.mentalBreakMin-ne0 -or $filter.mentalBreakMax-ne1 -or $filter.qualities-cne'Awful~Legendary' -or $filter.disallowedSpecials.Count-ne0 -or @($filter.allowedDefs|Select-Object -Unique).Count-ne$filter.allowedDefs.Count -or @($accepted|Where-Object {$filter.allowedDefs-cnotcontains$_}).Count-ne0){$Problems.Add('CAP03-B actual native filter range/definition/special census differs.')}
    }
    if($p.parentCompTypes.Count-ne0 -or -not(Test-ABSame $p.cargoCompTypes @('RimWorld.CompForbiddable')) -or -not(Test-ABSame $p.novelCompTypes @('RimWorld.CompForbiddable','RimWorld.CompQuality','RimWorld.CompBook'))){$Problems.Add('CAP03-B actual parent/subject comp catalog differs from the reviewed native definitions.')}
    if($r.collectionType-cne'AdaptiveStorage.ThingCollection' -or $r.count-ne6 -or $r.members.Count-ne6 -or $r.cellWiseCount-ne6 -or $r.cellCount-ne6 -or $r.slotLimit-ne6 -or $r.anyFree -or $r.packed -or $r.performanceFish -or -not(Test-ABSame $r.occupied @($p.cell)) -or -not$r.novelOverridesStack -or $r.cargoOverridesStack -or $r.acceptsNovelDef -or -not$r.acceptsCargoDef -or -not(Test-ABSame $r.acceptedDefs @('Steel')) -or -not(Test-ABSame $r.acceptedShortHashes @($p.cargoShortHash))){$Problems.Add('CAP03-B normal ASF registry/accepted-def/custom-branch setup differs.')}
    if(@($r.members.shortHash|Select-Object -Unique).Count-ne6){$Problems.Add('CAP03-B registry short hashes are not distinct.')}
    for($i=0;$i-lt$r.members.Count;$i++){
        $member=$r.members[$i];$item=Get-ABOne $p.items thingId $member.thingId $Problems
        $comp=@('RimWorld.CompForbiddable');if($member.def-ceq'WoodLog'){$comp+= 'Verse.CompEquippable'}
        if($i-ge6 -or $member.index-ne$i -or $member.indexOf-ne$i -or $member.def-cne$defs[$i] -or -not$member.contains -or -not$member.memberValid -or -not$member.targetValid -or -not$member.spawned -or -not$member.everStorable -or $member.holderType-cne'Verse.Map' -or $member.mapId-ne$Budget.mapId -or $member.mapCell-cne$p.cell -or $member.mapPosition-cne$p.cell -or $member.storingParentId-ne$p.parent.thingId -or $member.shortHash-lt1 -or $member.shortHash-gt65535 -or -not(Test-ABSame $member.compTypes $comp) -or $null-eq$item -or $member.def-cne$item.def -or $member.count-ne$item.count -or $member.stackLimit-ne$item.stackLimit){$Problems.Add('CAP03-B indexed real member/count/physical custody/comp census is inconsistent.')}
        if($member.def-ceq'Steel' -and $member.shortHash-ne$p.cargoShortHash){$Problems.Add('CAP03-B accepted Steel short hash is not its resident definition.')}
    }
    if(-not(Test-ABSame $p $Budget.finalPhysical)){$Problems.Add('CAP03-B final physical/filter/registry/actor scene changed.')}
}
function Test-ABTrial($Budget,$Trial,[string]$Id,$Problems){
    $p=$Budget.initialPhysical;$t=$Trial;$steel=Get-ABOne $p.items def Steel $Problems;$subject=if($Id-ceq'steel-sufficient'){$p.cargo}else{$p.novel}
    $parcel=$Id+'/'+$subject.def+$subject.thingId
    $expectedScope='ref:'+(7+[array]::IndexOf(@('novel-low','novel-sufficient','steel-sufficient'),$Id))
    if($t.id-cne$Id -or $t.query-cne('CAP03-B/'+$Id) -or $t.parcelId-cne$parcel -or $t.subjectId-ne$subject.thingId -or -not$t.completed -or -not$t.disposed -or $null-ne$t.error -or $t.scopeIdentity-cne$expectedScope -or -not(Test-ABSame $p $t.before) -or -not(Test-ABSame $p $t.after)){$Problems.Add('CAP03-B '+$Id+' is not a completed fresh scope for the same physical scene/subject.')}
    [long[]]$limit=@(16,8,8192,8192,256,4096,8192,8192,16384,256,256,16384,0,4096)
    Test-ABWork $t.pageAllowance $limit ($Id+' page allowance') $Problems
    if($Id-ceq'novel-low'){$limit[7]=36};Test-ABWork $t.defaultAllowance $limit ($Id+' default allowance') $Problems
    $open=Get-ABCosts $Budget open $Id;$page=Get-ABCosts $Budget page $Id;$elig=Get-ABCosts $Budget eligibility $Id
    Test-ABWork $t.openWork $open ($Id+' open') $Problems;Test-ABWork $t.page.operationWork $page ($Id+' page') $Problems;Test-ABWork $t.eligibility.work $elig ($Id+' eligibility') $Problems
    if(-not(Test-ABComplete $t.openStatus) -or -not(Test-ABComplete $t.page.status) -or -not$t.page.requestedCellsComplete -or -not$t.page.wholeGroupComplete -or -not$t.page.hasCursor -or $null-ne$t.page.nextCell -or $null-ne$t.page.nextMember -or $t.page.unresolvedTotal-ne0 -or $t.page.unresolvedSampleStart-ne0 -or $t.page.unresolvedSampleCount-ne0 -or $t.page.unresolved.Count-ne0 -or $t.page.cells.Count-ne1){$Problems.Add('CAP03-B '+$Id+' did not expose its complete real group page.');return}
    $cell=$t.page.cells[0];$xy=Read-ABCell $p.cell $Problems;$prefix=$Budget.sessionId+'/'+$Budget.mapId+'/'+$p.parentKey+'/'+$xy.x+','+$xy.z
    if(-not(Test-ABComplete $cell.status) -or $cell.fixtureScene-cne'CAP03-B' -or $cell.fixtureStage-cne($Id+'/group-page') -or $cell.observationId-ne1 -or $cell.session-cne$Budget.sessionId -or $cell.mapId-ne$Budget.mapId -or $cell.tick-ne$Budget.startedTick -or $cell.generation-ne1 -or $cell.query-cne$t.query -or $cell.cell-cne$p.cell -or $cell.parentKey-cne$p.parentKey -or $cell.groupKey-cne'concrete' -or $cell.provider-cne$Budget.asfIdentity -or $cell.vacantKey-cne($prefix+'/vacant') -or $cell.maximumSlots-ne6 -or $cell.itemCount-ne6 -or $cell.vacantSlots-ne0 -or $cell.gridEntries-ne7 -or $cell.stacks.Count-ne6){$Problems.Add('CAP03-B '+$Id+' cell is not this scope/session/query/concrete parent observation.')}
    Test-ABWork $cell.work $page ($Id+' embedded cell prefix, not another operation') $Problems
    foreach($item in $p.items){$row=Get-ABOne $cell.stacks thingId $item.thingId $Problems;if($null-eq$row -or $row.key-cne($prefix+'/stack:'+$item.thingId) -or $row.def-cne$item.def -or $row.count-ne$item.count -or $row.stackLimit-ne$item.stackLimit -or $row.deficit-ne($item.stackLimit-$item.count) -or $row.providerTargetValid-cne$true){$Problems.Add('CAP03-B '+$Id+' physical resource identity/deficit/validity mismatch.')}}
    $row=$t.eligibility
    if($row.fixtureScene-cne'CAP03-B' -or $row.fixtureStage-cne$Id -or $row.observationId-ne$cell.observationId -or $row.parcelId-cne$t.parcelId -or $row.vacantEligible -or $null-ne$row.unitsPerNewStack){$Problems.Add('CAP03-B '+$Id+' eligibility lost its actual cell/parcel identity or invented vacancy.')}
    $names=@('destination-enabled','destination-faction','selected-priority','effective-thing-filter','concrete-fixed-filter','asf-declared-fixed-filter','asf-actual-member-capacity','native-IsGoodStoreCell','hd-explicit-context-filter')
    $success=$Id-ceq'steel-sufficient';$low=$Id-ceq'novel-low'
    if($success){$names+=@('different-target','target-ever-storable','directional-CanStackWith','asf-individual-target')}
    if($row.predicates.Count-ne$names.Count){$Problems.Add('CAP03-B '+$Id+' predicate catalog is incomplete or duplicated.')}
    for($i=0;$i-lt$names.Count;$i++){
        if($i-ge$row.predicates.Count){break};$pred=$row.predicates[$i];$state='Eligible';$reason='None';$target=$null
        if(-not$success -and $i-ge6){$state='NotEvaluated';if(-not$low -and $i-eq6){$state='Refused';$reason='ProviderRefused'}}
        if($i-ge9){$target=$steel.thingId}
        if($pred.name-cne$names[$i] -or $pred.state-cne$state -or $pred.reason-cne$reason -or $pred.targetId-cne$target){$Problems.Add('CAP03-B '+$Id+' actual '+$names[$i]+' verdict/target/order is contradictory.')}
    }
    if($low){if($row.status.usable -or $row.status.observation-cne'Deferred' -or $row.status.capability-cne'Supported' -or $row.status.reason-cne'ProviderScanRequired' -or $row.state-cne'NotEvaluated' -or $row.topUps.Count-ne0){$Problems.Add('CAP03-B low member precharge was not a typed atomic refusal.')}}
    elseif(-not(Test-ABComplete $row.status) -or $row.state-cne$(if($success){'Eligible'}else{'Refused'})){$Problems.Add('CAP03-B sufficient provider returned an incorrect completion/verdict.')}
    if($success){if($row.topUps.Count-ne1 -or $row.topUps[0].targetId-ne$steel.thingId -or $row.topUps[0].units-ne7 -or $row.topUps[0].key-cne($prefix+'/stack:'+$steel.thingId)){$Problems.Add('CAP03-B ordinary Steel must have exactly the seven-unit real partial-target edge.')}}elseif($row.topUps.Count-ne0){$Problems.Add('CAP03-B Novel must have no phantom edge.')}
    $stages=@('after-open','after-page','after-eligibility','before-dispose','after-dispose')
    if($t.used.Count-ne5){$Problems.Add('CAP03-B '+$Id+' lacks five actual cumulative scope readings.')}
    for($i=0;$i-lt5;$i++){
        if($i-ge$t.used.Count){break};$used=$t.used[$i];[long[]]$expected=$open.Clone()
        for($k=0;$k-lt14;$k++){if($i-ge1){$expected[$k]+=$page[$k]};if($i-ge2){$expected[$k]+=$elig[$k]}}
        if($used.stage-cne$stages[$i]){$Problems.Add('CAP03-B '+$Id+' cumulative read lifecycle is out of order.')}
        Test-ABWork $used.cumulative $expected ($Id+' actual cumulative '+$stages[$i]) $Problems
    }
}

# Exact file identities and metadata resolved read-only from the reviewed frozen modules.
function Get-ABAssemblyCatalog {
    return @(
        'Assembly-CSharp|1.6.9676.17735|61e41735-6189-4da4-9d21-0260257b5097|5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A|RimWorldWin64_Data/Managed/Assembly-CSharp.dll',
        'UnityEngine.CoreModule|0.0.0.0|e422ced3-d0f6-4bb2-9423-e8338d57bc04|C5C58EA254834291780A1D6C388C241443D07167B4A4B890A23C9494F626DDBA|RimWorldWin64_Data/Managed/UnityEngine.CoreModule.dll',
        '0Harmony|2.4.1.0|e5339928-9d9b-419d-83f9-f5b02c5cf609|353DAAFEC180BB8E7BBE4DA78F2A7CDC78067392E3A4E79DC8E7AF295F2371E6|Mods/Harmony/Current/Assemblies/0Harmony.dll',
        'HarmonyMod|2.4.2.0|dc156e18-c1bf-4ec8-acd4-7176558400e1|408C2CD72B45DA8C70C0C538B2B346C8DCBB5BDED066373F6A3EB8B26615A2E7|Mods/Harmony/Current/Assemblies/HarmonyMod.dll',
        'HaulersDream|1.24.0.0|e52f9ce7-fd03-4a99-895a-858063863298|CE0508AF4058C0414DDC9F83142F9708B66EC43A44CD1BD74E1E1553CB37F604|Mods/HaulersDream/1.6/Assemblies/HaulersDream.dll',
        'HaulersDream.Core|1.24.0.0|277c4189-755b-4be8-8214-a5f2c177717a|84C6FABA608F18E0DEBA1638CF7C89452659358766C966EFEBBB0D3EEE040125|Mods/HaulersDream/1.6/Assemblies/HaulersDream.Core.dll',
        'HaulersDream.RuntimeHarness|0.1.0.0|bddfb6f9-bdfd-4c96-9633-474cfbc4370c|96636193A0BBD7AAC6A13F35A868240941C1A18E0CA2643EC72422C2BDD76A1B|Mods/HaulersDreamRuntimeHarness/Assemblies/HaulersDream.RuntimeHarness.dll',
        '0MultiplayerAPI|0.5.0.0|9a8d208a-60bf-452b-8ca1-0a48128812b0|4689E799C50DB79411B00AD8855D7DF86721BF3CFE1E390B68038B6ABCD852EC|Mods/AdaptiveStorageFramework/1.6/Assemblies/0MultiplayerAPI.dll',
        '1ITransformable|1.0.0.0|4ee66561-03e8-4f31-a084-965806452d5f|598E25338D988C0E404A1F7AFD405542956D3E97B4ADCEA0C8BA45B102B8573E|Mods/AdaptiveStorageFramework/1.6/Assemblies/1ITransformable.dll',
        'AdaptiveStorageFramework|1.2.4.0|d7c605b3-e59a-4b26-af97-594bc5417053|28DAA37ADE4144CAD2B7669EDFDA2201F9C0E8E99D4639853131E066026935B9|Mods/AdaptiveStorageFramework/1.6/Assemblies/AdaptiveStorageFramework.dll',
        'CopyOperation|1.0.0.0|1be97508-f31f-4be3-852c-9c88d036085b|D69C3D67EEDA7B3E49092491201CEFDEF5B6F649350171F2553C4AB38A98A620|Mods/AdaptiveStorageFramework/1.6/Assemblies/CopyOperation.dll',
        'DefNameLink|1.0.0.0|a098b497-78b2-4ecd-ba75-efcb3318533b|50F8FCB40B76A390CEA7DCC0E5B207AF50029034DFFE7458B3E650C90609A6DA|Mods/AdaptiveStorageFramework/1.6/Assemblies/DefNameLink.dll',
        'GeneratorOperation|1.0.0.0|8b7b673c-8658-4bb1-a357-812a56cbf168|206B7907298291B06C12EC685E9C925FF25D13C3E986AB2A6F12EFC51DE1C379|Mods/AdaptiveStorageFramework/1.6/Assemblies/GeneratorOperation.dll',
        'GeneratorOperationV2|1.0.0.0|dabe2593-42db-40f3-b908-4136ee9b121e|E35787D0EBD3B2C11289CDD23160A9A9212D6BEEEF5771293485FAB704E33AFA|Mods/AdaptiveStorageFramework/1.6/Assemblies/GeneratorOperationV2.dll',
        'PatchOperationSet|1.0.0.0|4181dc00-ef4f-40a9-a3ea-bf41c9baf2a3|5D49BE458370C728C8B522306812971289AE7DCE643DC4DA54D6811862DE2401|Mods/AdaptiveStorageFramework/1.6/Assemblies/PatchOperationSet.dll',
        'PatchOperationTryAdd|1.0.0.0|f34dcf66-5a44-4f30-891a-1a19fcbbbf1a|FFDA75CE50C939586F6853279CE4E54B359A3778558BBD26DAE5CDED443FA79C|Mods/AdaptiveStorageFramework/1.6/Assemblies/PatchOperationTryAdd.dll',
        'PostInheritanceOperation|1.0.0.0|3f7769b7-c500-4289-a256-8220b406a4b7|6BAFE852EF4987FBC8899D525DC64FCA98D2B385EB215DA816E31D310B9A04CD|Mods/AdaptiveStorageFramework/1.6/Assemblies/PostInheritanceOperation.dll',
        'SaveGameCompatibility|1.0.0.0|450d517a-15c0-42e3-a593-56db3d5dcbe6|EB9DCDA01D8A17D5AED3A389BB50B069783F2F6EBE916799BFD28A245C813A98|Mods/AdaptiveStorageFramework/1.6/Assemblies/SaveGameCompatibility.dll'
    )
}
function Get-ABBindingCatalog {
    return @(
        'HaulersDream.StorageProjectionEnvironment.ctor; token=100665163; mvid=e52f9ce7-fd03-4a99-895a-858063863298',
        'HaulersDream.StorageProjectionRequest.ctor; token=100665185; mvid=e52f9ce7-fd03-4a99-895a-858063863298',
        'HaulersDream.StorageParcelProbe.ctor; token=100665191; mvid=e52f9ce7-fd03-4a99-895a-858063863298',
        'HaulersDream.StorageProviderCatalog.Create; token=100665318; mvid=e52f9ce7-fd03-4a99-895a-858063863298',
        'HaulersDream.StorageProviderCatalog.PrepareMember; token=100665324; mvid=e52f9ce7-fd03-4a99-895a-858063863298',
        'HaulersDream.StorageResourceProjector.Open; token=100665335; mvid=e52f9ce7-fd03-4a99-895a-858063863298',
        'HaulersDream.StorageProjectionScope.ObserveCell; token=100665350; mvid=e52f9ce7-fd03-4a99-895a-858063863298',
        'HaulersDream.StorageProjectionScope.ObserveEligibility; token=100665355; mvid=e52f9ce7-fd03-4a99-895a-858063863298',
        'HaulersDream.StorageProjectionScope.Recheck; token=100665356; mvid=e52f9ce7-fd03-4a99-895a-858063863298',
        'HaulersDream.StorageProjectionScope.Dispose; token=100665366; mvid=e52f9ce7-fd03-4a99-895a-858063863298',
        'HaulersDream.Core.ProjectionWork.ctor; token=100663540; mvid=277c4189-755b-4be8-8214-a5f2c177717a',
        'HaulersDream.StorageProjectionLimits.ctor; token=100665172; mvid=e52f9ce7-fd03-4a99-895a-858063863298',
        'HaulersDream.StorageProjectionScope.ObserveGroupPage; token=100665361; mvid=e52f9ce7-fd03-4a99-895a-858063863298',
        'HaulersDream.StorageProjectionScope.get_Used; token=100665340; mvid=e52f9ce7-fd03-4a99-895a-858063863298',
        'AdaptiveStorage.ThingClass.get_StoredThings; token=100663810; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ThingCollection.get_Count; token=100663915; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ThingCollection.get_CellWiseCount; token=100663916; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ThingClass.get_CurrentSlotLimit; token=100663813; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ThingClass.get_AnyFreeSlots; token=100663815; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ThingClass.get_ContentsPacked; token=100663818; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ThingClass.get_OccupiedRect; token=100663824; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ModCompatibility.PerformanceFish.get_Active; token=100664404; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ThingClass.HasCapacityForThing; token=100663868; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ThingClass.FixedFilterAllows; token=100663866; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ThingClass.GetParentStoreSettings; token=100663861; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ThingCollection.Contains; token=100663974; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ThingCollection.IndexOf; token=100663995; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ThingCollection.get_Item; token=100663933; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ThingCollection.MapPositionOf; token=100663929; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ThingCollection.ItemCountAtMapCell; token=100663959; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ThingClass.ContainsAndAllows; token=100663831; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ThingCollection.AcceptsForStacking; token=100663978; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ThingCollection.AcceptsForStacking; token=100663977; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.Utility.ThingExtensions.OverridesCanStackWith; token=100664228; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.Utility.ThingExtensions.StoringAdaptiveStorage; token=100664214; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.HarmonyPatches.StoreUtilityPatches+FixMissingValidStackDestinationCheck.IsValidStackDestination; token=100666384; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'AdaptiveStorage.ThingCollection._defsAcceptedForStacking; field-token=67109163; mvid=d7c605b3-e59a-4b26-af97-594bc5417053',
        'RimWorld.Building_Storage.RimWorld.IStorageGroupMember.get_ParentStoreSettings; token=100723580; mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'Verse.Book.GenerateBook; token=100666333; mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'Verse.Book.CanStackWith; token=100666322; mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'Verse.ThingFilter.disallowedSpecialFilters; field-token=67125235; mvid=61e41735-6189-4da4-9d21-0260257b5097'
    )
}
function Get-ABPatchCatalog {
    return @(
        'Verse.GridsUtility.GetMaxItemsAllowedInCell <- adaptive.storage.framework:AdaptiveStorage.HarmonyPatches.StorageLimit.Transpiler;kind=Transpiler;mvid=d7c605b3-e59a-4b26-af97-594bc5417053;token=100664475',
        'RimWorld.StoreUtility.NoStorageBlockersIn <- adaptive.storage.framework:AdaptiveStorage.HarmonyPatches.StoreUtilityPatches+FixMissingValidStackDestinationCheck.Transpiler;kind=Transpiler;mvid=d7c605b3-e59a-4b26-af97-594bc5417053;token=100666382',
        'RimWorld.StoreUtility.IsGoodStoreCell <- giwaffed.HaulersDream:HaulersDream.Patch_IsGoodStoreCell_HonourCommitments.Postfix;kind=Postfix;mvid=e52f9ce7-fd03-4a99-895a-858063863298;token=100665072',
        'RimWorld.StoreUtility.IsGoodStoreCell <- giwaffed.HaulersDream:HaulersDream.HDLog.UniversalExceptionFinalizer;kind=Finalizer;mvid=e52f9ce7-fd03-4a99-895a-858063863298;token=100664009',
        'RimWorld.StoreUtility.TryFindBestBetterStoreCellForWorker <- adaptive.storage.framework:AdaptiveStorage.HarmonyPatches.StoreUtilityPatches+PreventStorageLookupFaster.Prefix;kind=Prefix;mvid=d7c605b3-e59a-4b26-af97-594bc5417053;token=100666381'
    )
}
function Get-ABGlobalAssertions($Value,$Problems){
    $native=Get-ABOne $Value.assemblies name Assembly-CSharp $Problems;if($null-eq$native){return @()}
    $path=$native.path.Replace('\','/');$suffix='/RimWorldWin64_Data/Managed/Assembly-CSharp.dll'
    if(-not$path.EndsWith($suffix) -or $path.Contains('/../') -or $path.Contains('/./')){$Problems.Add('CAP03-B native path does not describe the private runtime.');return @()}
    $runtime=$path.Substring(0,$path.Length-$suffix.Length);$base=$runtime.Substring(0,$runtime.Length-8)
    if($runtime-cnotmatch('/haulersdream-runtime-tests/'+[regex]::Escape($Value.runId)+'/runtime$')){$Problems.Add('CAP03-B assembly paths are not attached to this private run.')}
    $result=New-Object 'System.Collections.Generic.List[object]'
    foreach($spec in @(@('private-runtime-data-path',($runtime+'/RimWorldWin64_Data')),@('private-save-data-path',($base+'/SaveData').Replace('/','\')),@('private-mod-directory',($runtime+'/Mods').Replace('/','\')),@('private-player-log',($base+'/evidence/Player.log')),@('case-supported','CAP03-B'),@('negative-control-supported','None'),@('installed-version-file-matches-manifest','1.6.4871 rev590'),@('harness-compiled-against-running-game','5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A'))){$result.Add([pscustomobject]@{id=$spec[0];observed=$spec[1]})}
    $map=Get-ABOne $Value.assertions id real-map-initialized $Problems
    if($null-ne$map){if($map.observed-cnotmatch'^size=\(([1-9][0-9]*), 1, ([1-9][0-9]*)\); maps=([1-9][0-9]*)$'){$Problems.Add('CAP03-B actual initialized map dimensions/count malformed.')}else{$sx=0;$sz=0;$maps=0;$ax=$Matches[1];$az=$Matches[2];$am=$Matches[3];if(-not[int]::TryParse($ax,[ref]$sx) -or -not[int]::TryParse($az,[ref]$sz) -or -not[int]::TryParse($am,[ref]$maps) -or $maps-ne$Value.storageProjectionBudget.gameMapIds.Count){$Problems.Add('CAP03-B map census/dimension exceeds native range or differs.')}else{$xy=Read-ABCell $Value.storageProjectionBudget.initialPhysical.cell $Problems;if($null-ne$xy -and ($xy.x-ne[math]::Floor($sx/2) -or $xy.z-ne[math]::Floor($sz/2) -or $xy.x-lt10 -or $xy.z-lt8 -or $xy.x+10-ge$sx -or $xy.z+8-ge$sz)){$Problems.Add('CAP03-B scene is not the bounded native map-center fixture.')}}};$result.Add([pscustomobject]@{id='real-map-initialized';observed=$map.observed})}
    $result.Add([pscustomobject]@{id='exact-active-mod-count';observed='actual=6; expected=6'})
    $mods=@(@('brrainz.harmony','Mods/Harmony'),@('ludeon.rimworld','Data/Core'),@('adaptive.storage.framework','Mods/AdaptiveStorageFramework'),@('sbz.neatstorage','Mods/NeatStorage'),@('giwaffed.haulersdream','Mods/HaulersDream'),@('giwaffed.haulersdream.runtimeharness','Mods/HaulersDreamRuntimeHarness'))
    if($Value.mods.Count-ne6){$Problems.Add('CAP03-B requires exactly six active mods.')}
    for($i=0;$i-lt6;$i++){$mod=$mods[$i];$root=($runtime+'/'+$mod[1]).Replace('/','\');if($i-ge$Value.mods.Count -or $Value.mods[$i].packageId-cne$mod[0] -or $Value.mods[$i].rootPath-cne$root){$Problems.Add('CAP03-B actual package/root order differs.')};$result.Add([pscustomobject]@{id='mod-order-root-'+$i;observed=$mod[0]+' @ '+$root})}
    $catalog=Get-ABAssemblyCatalog
    if($Value.assemblies.Count-ne$catalog.Count){$Problems.Add('CAP03-B loaded assembly catalog differs from the eighteen reviewed files.')}
    for($i=0;$i-lt$catalog.Count;$i++){
        $spec=$catalog[$i].Split('|');$row=Get-ABOne $Value.assemblies name $spec[0] $Problems;$expected=($runtime+'/'+$spec[4]).Replace('/','\')
        if($null-eq$row -or $row.path-cne$expected -or $row.assemblyVersion-cne$spec[1] -or $row.moduleVersionId-cne$spec[2] -or $row.sha256-cne$spec[3] -or $Value.assemblies[$i].name-cne$spec[0]){$Problems.Add('CAP03-B exact loaded file/module/order differs: '+$spec[0])}
        $result.Add([pscustomobject]@{id='single-assembly-'+$spec[0];observed='count=1'})
        $result.Add([pscustomobject]@{id='assembly-identity-'+$spec[0];observed=$expected+'; version='+$spec[1]+'; sha256='+$spec[3]})
    }
    $ticks=Get-ABOne $Value.assertions id real-game-ticks-advanced $Problems
    if($null-ne$ticks){$elapsed=0;if($ticks.observed-cnotmatch'^elapsedTicks=([0-9]+)$' -or -not[int]::TryParse($Matches[1],[ref]$elapsed) -or $elapsed-lt5 -or $elapsed-ge$Value.storageProjectionBudget.startedTick){$Problems.Add('CAP03-B actual ready tick delay is invalid.')};$result.Add([pscustomobject]@{id='real-game-ticks-advanced';observed=$ticks.observed})}
    $result.Add([pscustomobject]@{id='no-unity-errors-after-harness-start';observed='observedErrors=0; threaded capture through terminal-result boundary'})
    return $result.ToArray()
}
function Test-ABIdentity($Value,$Problems){
    $p=$Value.storageProjectionBudget;$expected=Get-ABGlobalAssertions $Value $Problems
    if($Value.assertions.Count-ne$expected.Count){$Problems.Add('CAP03-B global assertion catalog contains missing/duplicate/extra entries.')}
    for($i=0;$i-lt$expected.Count;$i++){$want=$expected[$i];$a=Get-ABOne $Value.assertions id $want.id $Problems;if($null-eq$a -or -not$a.passed -or $a.observed-cne$want.observed -or $Value.assertions[$i].id-cne$want.id){$Problems.Add('CAP03-B global assertion outcome/value/order differs: '+$want.id)}}
    $bound=@('HaulersDream','HaulersDream.Core','Assembly-CSharp','0Harmony','HaulersDream.RuntimeHarness','AdaptiveStorageFramework')
    if($p.assemblies.Count-ne6){$Problems.Add('CAP03-B binding assembly census must have six exact modules.')}
    for($i=0;$i-lt6;$i++){
        $a=Get-ABOne $Value.assemblies name $bound[$i] $Problems;if($null-eq$a -or $i-ge$p.assemblies.Count){continue};$row=$p.assemblies[$i]
        if($row.name-cne($a.name+', Version='+$a.assemblyVersion+', Culture=neutral, PublicKeyToken=null') -or $row.mvid-cne$a.moduleVersionId -or $row.path-cne$a.path -or $row.sha256-cne$a.sha256){$Problems.Add('CAP03-B bound module does not match its actual loaded file: '+$bound[$i])}
    }
    if(-not(Test-ABSame $p.bindings (Get-ABBindingCatalog)) -or -not(Test-ABSame $p.patchInventory (Get-ABPatchCatalog))){$Problems.Add('CAP03-B exact native/ASF/HD metadata binding or five-patch owner/kind/module/token catalog changed.')}
    if($p.nativeIdentity-cne'1.6.4871 rev591;Assembly-CSharp, Version=1.6.9676.17735, Culture=neutral, PublicKeyToken=null;mvid=61e41735-6189-4da4-9d21-0260257b5097' -or $p.asfIdentity-cne'AdaptiveStorageFramework, Version=1.2.4.0, Culture=neutral, PublicKeyToken=null;mvid=d7c605b3-e59a-4b26-af97-594bc5417053'){$Problems.Add('CAP03-B native/provider identity provenance differs.')}
    $inputSpecs=@(@('sbz.neatstorage','1.6/Defs/ThingDefs_Buildings/Buildings_CrateAndPallet.xml','9B9F757E13A16327250B5F62BA15DC457F40F4B45DD7500CA9DEA0F56BC4D582'),@('adaptive.storage.framework','Defs/ThingDefBase.xml','DECE4A55D724F4D1EE23B6F21C531BB4F5EF627EC93E4A4D02C7565FE73A242B'),@('ludeon.rimworld','Defs/Books/BookDefs.xml','86F29BB5FDB9991E52865D78686BD7F98CB5FB1744BAB1B8719720CF6D20829D'))
    if($p.inputs.Count-ne3){$Problems.Add('CAP03-B requires all three exact native/ASF/Neat definition inputs.')}
    for($i=0;$i-lt3;$i++){
        if($i-ge$p.inputs.Count){break};$spec=$inputSpecs[$i];$row=$p.inputs[$i];$mod=Get-ABOne $Value.mods packageId $spec[0] $Problems
        if($row.packageId-cne$spec[0] -or $row.relativePath-cne$spec[1] -or $row.sha256-cne$spec[2] -or $null-eq$mod -or $row.root-cne$mod.rootPath){$Problems.Add('CAP03-B input identity/order does not match the loaded private package.')}
    }
}
function ConvertTo-ABWorkRows($Costs){$kinds=Get-ABKinds;return @(for($i=0;$i-lt14;$i++){[pscustomobject]@{kind=$kinds[$i];value=$Costs[$i]}})}
function Get-ABRecordPlan($P,$Problems){
    $plan=New-Object 'System.Collections.Generic.List[object]';$state=@{assertion=0}
    $emit={param($kind,$trial,$data) $plan.Add([pscustomobject]@{kind=$kind;trial=$trial;data=$data})}
    $assert={param($id,$kind)
        if($state.assertion-ge$P.assertions.Count){$Problems.Add('CAP03-B missing nested assertion '+$id+'.');return}
        $row=$P.assertions[$state.assertion];$state.assertion++
        if($row.sequence-ne$state.assertion -or $row.id-cne$id -or $row.kind-cne$kind -or -not$row.passed -or [string]::IsNullOrWhiteSpace($row.detail)){$Problems.Add('CAP03-B nested assertion catalog/order/outcome differs: '+$id)}
        & $emit assertion $null $row
    }
    foreach($id in @('expectation','map','native-filter-field','reviewed-asf-assembly')){& $assert $id fixture}
    & $emit bindings $null ([pscustomobject]@{assemblies=$P.assemblies;bindings=$P.bindings})
    foreach($row in $P.inputs){& $emit input $null $row;& $assert ('reviewed-input-'+$row.packageId) fixture}
    foreach($id in @('fixture-bounds','no-zones-overwritten','normal-actor','only-fixture-actor','actual-neat-def')){& $assert $id fixture}
    foreach($def in (Get-ABMaterialDefs)){$item=Get-ABOne $P.initialPhysical.items def $def $Problems;if($null-ne$item){& $assert ('stack-limit-'+$def) fixture;& $assert ('spawn-'+$def+$item.thingId) fixture}}
    foreach($thing in @($P.initialPhysical.cargo,$P.initialPhysical.novel)){& $assert ('spawn-'+$thing.def+$thing.thingId) fixture}
    & $assert actual-book fixture;& $assert book-generated fixture
    & $emit physical-setup $null $P.initialPhysical
    & $assert six-real-asf-slots fixture;& $assert complete-physical-census fixture
    foreach($def in (Get-ABMaterialDefs)){$item=Get-ABOne $P.initialPhysical.items def $def $Problems;if($null-ne$item){& $assert ('resident-quantity-'+$def+$item.thingId) fixture}}
    foreach($id in @('real-outside-parcels','actual-filter-acceptance','registry-full-six','registry-identities-valid','accepted-def-set-real','actual-custom-classification','actor-still','directional-commodity-control')){& $assert $id fixture}
    & $emit catalog $null $P.catalogStatus;& $assert catalog-created behavior;& $assert native-semantics-reviewed behavior
    & $emit patch-inventory $null $P.patchInventory
    & $emit preparation $null ([pscustomobject]@{status=$P.preparationStatus;allowance=$P.preparationAllowance;charged=$P.preparationCharged;ready=$P.preparationReady;indexed=$P.indexedZoneCells;warmup=$P.footprintWarmup})
    & $assert member-prepared behavior
    foreach($id in @('novel-low','novel-sufficient','steel-sufficient')){
        $t=Get-ABOne $P.trials id $id $Problems;if($null-eq$t){continue}
        & $emit trial-before $id $t.before;& $assert ($id+'-equivalent-scene') fixture
        & $emit default-allowance $id $t.defaultAllowance;& $emit page-allowance $id $t.pageAllowance
        & $emit open $id ([pscustomobject]@{status=$t.openStatus;scopeIdentity=$t.scopeIdentity;work=$t.openWork;query=$t.query})
        & $assert ($id+'-open') behavior
        & $emit scope-used $id (Get-ABOne $t.used stage after-open $Problems);& $assert ($id+'-open-used') behavior
        & $emit source-derived-open-charges $id (ConvertTo-ABWorkRows (Get-ABCosts $P open $id));& $assert ($id+'-open-all-charges') behavior
        & $emit page $id $t.page;& $emit scope-used $id (Get-ABOne $t.used stage after-page $Problems)
        foreach($suffix in @('page-used-delta','complete-real-page','cell-identity','cell-physical-resources')){& $assert ($id+'-'+$suffix) behavior}
        & $emit source-derived-page-charges $id (ConvertTo-ABWorkRows (Get-ABCosts $P page $id))
        foreach($suffix in @('page-all-charges','page-provider-fifty','cell-prefix-accounting')){& $assert ($id+'-'+$suffix) behavior}
        & $emit eligibility $id $t.eligibility;& $emit scope-used $id (Get-ABOne $t.used stage after-eligibility $Problems)
        foreach($suffix in @('eligibility-used-delta','retained-observation-identity')){& $assert ($id+'-'+$suffix) behavior}
        & $emit source-derived-eligibility-charges $id (ConvertTo-ABWorkRows (Get-ABCosts $P eligibility $id))
        foreach($suffix in @('eligibility-all-charges','exact-predicate-catalog','filter-path-reached','no-phantom-vacancies')){& $assert ($id+'-'+$suffix) behavior}
        $special=switch($id){novel-low {@('typed-scan-refusal','atomic-provider-precharge','later-predicates-not-evaluated')};novel-sufficient {@('actual-provider-refusal','provider-sixty-two','native-not-reached')};steel-sufficient {@('ordinary-success','ordinary-provider-seventy-seven','different-target','target-ever-storable','directional-CanStackWith','asf-individual-target')}}
        foreach($suffix in $special){& $assert ($id+'-'+$suffix) behavior}
        & $emit scope-used $id (Get-ABOne $t.used stage before-dispose $Problems);& $emit scope-used $id (Get-ABOne $t.used stage after-dispose $Problems)
        & $assert ($id+'-dispose-accounting') behavior;& $emit trial-after $id $t.after;& $assert ($id+'-physical-unchanged') fixture
        & $emit trial-result $id $t
    }
    & $emit physical-final $null $P.finalPhysical
    & $assert physical-final-unchanged fixture;& $assert same-tick fixture;& $assert all-scopes-closed behavior;& $assert three-fresh-trials-complete behavior
    if($state.assertion-ne120 -or $P.assertions.Count-ne$state.assertion -or @($P.assertions.id|Select-Object -Unique).Count-ne120){$Problems.Add('CAP03-B exact source assertion catalog must contain 120 unique entries.')}
    return $plan.ToArray()
}
function Test-ABECore($Value,[string]$ExpectedBehavior,$Problems){
    if(-not(Test-ABShapeCore $Value $Problems)){return}
    $p=$Value.storageProjectionBudget
    if($ExpectedBehavior-cne'satisfied' -or $Value.caseId-cne'CAP03-B' -or $p.caseId-cne'CAP03-B' -or $p.expectedBehavior-cne'satisfied' -or $p.contract-cne'asf-six-slot-member-budget-v1' -or $p.status-cne'passed' -or $Value.status-cne'passed' -or $null-ne$p.error -or -not$p.fixtureValid -or -not$p.requestedBehaviorSatisfied -or -not$p.expectationMatched -or $p.allCap03ComponentsSatisfied -or $Value.unityErrorsObserved-ne0 -or $null-ne$Value.negativeControl){$Problems.Add('CAP03-B is not a clean explicitly satisfied component-B result.')}
    if($p.scope-cne'Only actual Neat Storage full-member budget component B; A/C, allocator, original reports and independent native call instrumentation remain pending.' -or -not(Test-ABSame $p.pendingControls @('CAP03-A','CAP03-C','independent-native-call-instrumentation','large-groups-and-lifecycle','allocation-and-original-report-convergence')) -or $Value.detail-cne'Actual ASF full-member budget component B only; other CAP03 components, native-call instrumentation and hauling convergence remain unfinished.'){$Problems.Add('CAP03-B scope/pending-controls limitations were lost or broadened.')}
    if($Value.runId-cnotmatch'^[a-f0-9]{32}$' -or $p.sessionId-cnotmatch'^[a-f0-9]{32}$' -or $p.mainThread-ne1 -or $p.startedTick-lt5 -or $p.finishedTick-ne$p.startedTick -or $p.mapId-lt0 -or $p.gameMapIds.Count-lt1 -or @($p.gameMapIds|Select-Object -Unique).Count-ne$p.gameMapIds.Count -or $p.gameMapIds-cnotcontains$p.mapId -or $Value.executingGameVersion-cne'1.6.4871 rev591' -or $Value.installedVersionFile-cne'1.6.4871 rev590' -or [datetimeoffset]$Value.finishedUtc-lt[datetimeoffset]$Value.startedUtc){$Problems.Add('CAP03-B run/session/main-thread/map/tick/version provenance is invalid.')}
    if(-not(Test-ABComplete $p.catalogStatus) -or -not(Test-ABComplete $p.preparationStatus) -or -not$p.preparationReady -or -not$p.footprintWarmup -or $p.indexedZoneCells-ne0 -or $p.preparationAllowance-le0 -or $p.preparationCharged-le0 -or $p.preparationCharged-gt$p.preparationAllowance){$Problems.Add('CAP03-B actual provider/catalog preparation did not complete within its own allowance.')}
    Test-ABIdentity $Value $Problems;Test-ABPhysical $p $Problems
    $ids=@('novel-low','novel-sufficient','steel-sufficient')
    if($p.trials.Count-ne3 -or -not(Test-ABSame @($p.trials.id) $ids) -or @($p.trials.scopeIdentity|Select-Object -Unique).Count-ne3){$Problems.Add('CAP03-B requires three distinct fresh scope trials in source order.')}
    foreach($t in $p.trials){if($ids-ccontains$t.id){Test-ABTrial $p $t $t.id $Problems}}
    $physicalRefs=@($p.initialPhysical.groupIdentity,$p.initialPhysical.registryIdentity,$p.initialPhysical.settingsIdentity,$p.initialPhysical.filterIdentity,$p.initialPhysical.declaredFixedIdentity,$p.initialPhysical.declaredFilterIdentity)
    foreach($t in $p.trials){if($physicalRefs-ccontains$t.scopeIdentity){$Problems.Add('CAP03-B scope identity aliases a physical object.')}}
    $plan=@(Get-ABRecordPlan $p $Problems)
    if($plan.Count-ne177 -or $p.records.Count-ne$plan.Count){$Problems.Add('CAP03-B complete ordered source record catalog is missing or duplicated.')}
    for($i=0;$i-lt[math]::Min($plan.Count,$p.records.Count);$i++){
        $row=$p.records[$i];$spec=$plan[$i];$data=ConvertFrom-Json -InputObject $row.data
        if($row.sequence-ne($i+1) -or $row.tick-ne$p.startedTick -or $row.kind-cne$spec.kind -or $row.trial-cne$spec.trial -or -not(Test-ABSame $data $spec.data)){$Problems.Add('CAP03-B retained record/data/order contradicts the complete source catalog at '+($i+1)+'.')}
    }
}
function Test-ABEventsCore($Value,$Events,[string]$RunId,$Problems){
    if(-not(Test-ABShapeCore $Value $Problems)){return}
    if($Events-isnot[array] -or $Events.Count-eq0){$Problems.Add('CAP03-B raw event array is absent.');return}
    $p=$Value.storageProjectionBudget;$before=$Problems.Count;$lastTick=-1;$lastUtc=[datetimeoffset]::MinValue;$i=0
    foreach($row in $Events){
        $i++;if(-not(Test-ABFields $row @{sequence='int';runId='string';caseId='string';utc='utc';phase='string';detail='string';tick='int'} 'CAP03-B raw event' $Problems)){continue}
        $utc=[datetimeoffset]$row.utc
        if($row.sequence-ne$i -or $row.caseId-cne'CAP03-B' -or $row.runId-cne$RunId -or $row.runId-cne$Value.runId -or $row.tick-lt$lastTick -or $row.tick-lt-1 -or $utc-lt$lastUtc -or $utc-lt[datetimeoffset]$Value.startedUtc){$Problems.Add('CAP03-B raw event sequence/tick/UTC/run identity is not continuous.')}
        $lastTick=$row.tick;$lastUtc=$utc
    }
    if($Problems.Count-ne$before){return}
    $plan=@(Get-ABRecordPlan $p $Problems);$component=@($Events|Where-Object {$_.phase.StartsWith('cap03-b-') -and $_.phase-cne'cap03-b-result'})
    if($component.Count-ne$plan.Count -or $component.Count-ne$p.records.Count){$Problems.Add('CAP03-B raw/retained component catalogs have missing, repeated or extra rows.')}
    for($i=0;$i-lt[math]::Min($component.Count,$plan.Count);$i++){
        $event=$component[$i];$record=ConvertFrom-Json -InputObject $event.detail;$expected=$plan[$i]
        if(-not(Test-ABFields $record @{sequence='int';tick='int';kind='string';trial='nullable-string';data='string'} 'CAP03-B raw record' $Problems)){continue}
        if($record.sequence-ne($i+1) -or $record.tick-ne$p.startedTick -or $event.tick-ne$record.tick -or $event.phase-cne('cap03-b-'+$expected.kind) -or $record.kind-cne$expected.kind -or $record.trial-cne$expected.trial -or $i-ge$p.records.Count -or -not(Test-ABSame $record $p.records[$i])){$Problems.Add('CAP03-B raw component record does not reconcile with its retained identity/catalog.')}
        $payload=ConvertFrom-Json -InputObject $record.data
        if(-not(Test-ABSame $payload $expected.data)){$Problems.Add('CAP03-B raw '+$record.kind+' payload differs from its typed result/source-derived expectation.')}
        if($i-gt0 -and $event.sequence-ne($component[$i-1].sequence+1)){$Problems.Add('CAP03-B synchronous component observations are not contiguous.')}
    }
    $terminal=Get-ABOne $Events phase cap03-b-result $Problems
    if($null-ne$terminal){$data=ConvertFrom-Json -InputObject $terminal.detail;if(-not(Test-ABSame $data $p) -or $terminal.tick-ne$p.finishedTick -or $component.Count-eq0 -or $terminal.sequence-ne($component[-1].sequence+1)){$Problems.Add('CAP03-B terminal component payload/boundary differs from the actual nested result.') }}
    $start=Get-ABOne $Events phase harness-start $Problems;$version=Get-ABOne $Events phase game-version-provenance $Problems;$new=Get-ABOne $Events phase new-game $Problems;$map=Get-ABOne $Events phase map-initialized $Problems
    $scenario=Get-ABOne $Events phase scenario-observed $Problems;$capture=Get-ABOne $Events phase error-capture-boundary $Problems;$end=Get-ABOne $Events phase terminal-result $Problems
    $globals=@($Events|Where-Object phase -CEQ assertion);$expectedGlobal=@(Get-ABGlobalAssertions $Value $Problems)
    if($globals.Count-ne$expectedGlobal.Count){$Problems.Add('CAP03-B complete global assertion event catalog differs.')}
    for($i=0;$i-lt[math]::Min($globals.Count,$expectedGlobal.Count);$i++){
        $expected=$expectedGlobal[$i];$observed=$expected.observed
        # Finish rewrites the retained clean assertion with the final closed-capture count without emitting another assertion.
        if($expected.id-ceq'no-unity-errors-after-harness-start'){$observed='observedErrors=0'}
        if($globals[$i].detail-cne($expected.id+': passed; '+$observed)){$Problems.Add('CAP03-B raw global assertion outcome/detail/order differs: '+$expected.id)}
    }
    if($null-eq$start -or $null-eq$version -or $null-eq$new -or $null-eq$map -or $null-eq$terminal -or $null-eq$scenario -or $null-eq$capture -or $null-eq$end -or $component.Count-eq0 -or $globals.Count-ne$expectedGlobal.Count){return}
    $ticks=Get-ABOne $Value.assertions id real-game-ticks-advanced $Problems;$elapsed=0
    if($null-ne$ticks -and $ticks.observed-cmatch'^elapsedTicks=([0-9]+)$'){[int]::TryParse($Matches[1],[ref]$elapsed)|Out-Null}
    $mapAssertion=Get-ABOne $Value.assertions id real-map-initialized $Problems;$size=''
    if($null-ne$mapAssertion -and $mapAssertion.observed-cmatch'^size=(.*); maps=[0-9]+$'){$size=$Matches[1]}
    if($start.detail-cne'case=CAP03-B; expectedBehavior=satisfied' -or $start.sequence-ne1 -or $start.tick-ne-1 -or $version.tick-ne-1 -or $version.detail-cne('Version.txt='+$Value.installedVersionFile+'; executing assembly reports='+$Value.executingGameVersion) -or $new.tick-ne0 -or $new.detail-cne'GameComponent lifecycle callback received.' -or $map.tick-lt1 -or $map.detail-cne('tick='+$map.tick+'; size='+$size) -or $p.startedTick-$map.tick-ne$elapsed){$Problems.Add('CAP03-B startup/native-version/new-map/ready-tick provenance differs.')}
    # Bootstrap emits the first six assertions, version provenance, then the two version checks, new-game,
    # map/mod/assembly checks, map initialized, ready ticks, the synchronous component, and the final clean assertion.
    $outer=New-Object 'System.Collections.Generic.List[object]';$outer.Add($start)
    foreach($row in $globals[0..5]){$outer.Add($row)};$outer.Add($version)
    foreach($row in $globals[6..7]){$outer.Add($row)};$outer.Add($new)
    for($i=8;$i-lt$globals.Count-2;$i++){$outer.Add($globals[$i])}
    $outer.Add($map);$outer.Add($globals[-2]);foreach($row in $component){$outer.Add($row)};$outer.Add($terminal)
    $outer.Add($globals[-1]);$outer.Add($scenario);$outer.Add($capture);$outer.Add($end)
    if($outer.Count-ne$Events.Count){$Problems.Add('CAP03-B outer lifecycle has unexpected/error/extra phases.')}
    for($i=0;$i-lt$outer.Count;$i++){if($outer[$i].sequence-ne$i+1){$Problems.Add('CAP03-B outer lifecycle/global setup/component/closure chronology is out of order.');break}}
    if($globals[-2].tick-ne$p.startedTick -or $globals[-1].tick-ne$p.finishedTick -or $scenario.tick-ne$p.finishedTick -or $capture.tick-ne$p.finishedTick -or $end.tick-ne$p.finishedTick -or $scenario.detail-cne'case=CAP03-B; expected=satisfied; requestedBehaviorSatisfied=True; expectationMatched=True' -or $capture.detail-cne'Threaded capture closed before terminal result; earlier startup and shutdown require whole Player.log review.' -or $end.detail-cne($Value.status+': '+$Value.detail) -or $end.sequence-ne$Events.Count -or [datetimeoffset]$Value.finishedUtc-lt[datetimeoffset]$scenario.utc -or [datetimeoffset]$Value.finishedUtc-gt[datetimeoffset]$capture.utc){$Problems.Add('CAP03-B result/scenario/error-closure/last-terminal ordering or error reconciliation is contradictory.')}
}
function Test-AsfBudgetShape($Value,$Problems){
    $old=$ErrorActionPreference;$ErrorActionPreference='Stop';$before=$Problems.Count
    try{return Test-ABShapeCore $Value $Problems}catch{$Problems.Add('CAP03-B malformed evidence shape: '+$_.Exception.Message);return $false}finally{$ErrorActionPreference=$old}
}
function Test-AsfBudgetEvidence($Value,[string]$ExpectedBehavior,$Problems){
    $old=$ErrorActionPreference;$ErrorActionPreference='Stop'
    try{Test-ABECore $Value $ExpectedBehavior $Problems|Out-Null}catch{$Problems.Add('CAP03-B malformed/inconsistent evidence: '+$_.Exception.Message)}finally{$ErrorActionPreference=$old}
}
function Test-AsfBudgetEvents($Value,$Events,[string]$RunId,$Problems){
    $old=$ErrorActionPreference;$ErrorActionPreference='Stop'
    try{Test-ABEventsCore $Value $Events $RunId $Problems|Out-Null}catch{$Problems.Add('CAP03-B malformed/inconsistent raw evidence: '+$_.Exception.Message)}finally{$ErrorActionPreference=$old}
}
