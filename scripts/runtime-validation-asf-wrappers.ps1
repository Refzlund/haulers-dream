# CAP03-C native wrapper evidence consumer. Read-only; Windows PowerShell 5.1 compatible.
# Fixture source freeze 8500F693. This is containment/counter evidence, not minified hauling support.
. (Join-Path $PSScriptRoot 'runtime-validation-asf-budget.ps1')

function Assert-AW($Condition,[string]$Label,$Problems){if(-not$Condition){[void]$Problems.Add('CAP03-C '+$Label)}}
function Get-AWSchemas {
    # All serialized fields, including null case payloads and large fixed-filter/registry lists.
    $declarations=@'
Global|schemaVersion:int processId:int caseId:string runId:string status:string detail:string startedUtc:utc finishedUtc:utc executingGameVersion:string installedVersionFile:string unityErrorsObserved:int scenario:nil negativeControl:nil candidateRecurrence:nil inFlightRecurrence:nil partialInventoryBill:nil storageDelivery:nil storageOwnership:nil storageProjection:nil storageProjectionBudget:nil storageProjectionFilters:nil storageProjectionWrappers:Result storageSlots:nil mods:Mod[] assemblies:Assembly[] assertions:GlobalAssertion[]
Mod|packageId:string rootPath:string
Assembly|name:string path:string sha256:string assemblyVersion:string moduleVersionId:string
GlobalAssertion|id:string passed:bool observed:string
Result|caseId:string contract:string expectedBehavior:string error:?string status:string scope:string fixtureValid:bool requestedBehaviorSatisfied:bool expectationMatched:bool allCap03ComponentsSatisfied:bool pendingControls:string[] startedTick:int finishedTick:int mainThread:int mapId:int thingDefs:int categoryDefs:int specialDefs:int gameMaps:int session:string nativeIdentity:string asfIdentity:string definition:Definition catalogStatus:Status assemblies:BoundAssembly[] bindings:string[] patchInventory:string[] inputs:Input[] scenes:Scene[] assertions:Assertion[] records:Record[]
BoundAssembly|name:string path:string mvid:string sha256:string
Input|packageId:string root:string relativePath:string sha256:string
Assertion|sequence:int id:string kind:string passed:bool detail:string
Record|sequence:int tick:int kind:string trial:?string data:string
Event|sequence:int runId:string caseId:string utc:utc phase:string detail:string tick:int
Definition|def:string runtimeType:string package:string category:string minifiedDef:string outerType:string minifiable:bool everStorableWhenMinified:bool everStorableAsBuilding:bool useHitPoints:bool withinBuildings:bool withinNeat:bool width:int depth:int outerStackLimit:int categories:string[]
Scene|id:string error:?string parentKey:string positiveParentKey:string cell:string positiveCell:string resident:bool asf:bool completed:bool restored:bool startedTick:int finishedTick:int zonesBefore:int zonesAfter:int groupsBefore:int groupsAfter:int beforeProtected:Snapshot afterProtected:Snapshot afterNative:Snapshot protectedBegin:Counter[] protectedEnd:Counter[] nativeEnd:Counter[] retirementEnd:Counter[] minifications:Minify[] preparations:Preparation[] trials:Trial[] nativeControls:Native[] retiredThingIds:int[] retiredDestinations:Retirement[]
Retirement|parentKey:string cells:string[] slotsCleared:bool zoneRegistered:bool asfCount:?int
Counter|innerId:int identity:string stackCalls:long hitPointReads:long
Thing|thingId:int def:string count:int stackLimit:int cell:string spawned:bool destroyed:bool mapId:?int heldByFixtureMap:bool holderType:?string faction:?string stuff:?string hitPoints:int
Wrapper|outer:Thing inner:Thing outerIdentity:string innerIdentity:string ownerIdentity:string innerHoldingOwnerIdentity:string innerParentIdentity:string innerParentType:string outerType:string innerType:string innerDefPackage:string directCount:int setupMaximumHitPoints:int innerMapHeldId:?int sameInner:bool directContainsInner:bool innerOwnerMatches:bool innerParentIsOuter:bool innerSpawnedOrParentSpawned:bool directIds:int[] outerComps:string[] innerComps:string[]
Minify|before:Thing after:Wrapper countersBefore:Counter[] countersAfter:Counter[]
Destination|key:string identity:string runtimeType:string slotIdentity:string groupIdentity:?string registryIdentity:?string defPackage:?string thing:?Thing registered:bool zoneRegistered:bool cells:string[] compTypes:string[] effective:Settings declaredFixed:Settings interfaceFixed:Settings grids:Grid[] registry:?Registry
Settings|settingsIdentity:string filterIdentity:string ownerIdentity:?string ownerType:?string priority:string contents:Filter
Filter|type:string onlySpecial:bool allowedDefs:string[] disallowedSpecials:?string[] hitPointsMin:number hitPointsMax:number mentalBreakMin:number mentalBreakMax:number qualities:string
Grid|cell:string nativeMaximum:int things:Thing[]
Registry|collectionType:string count:int cellWiseCount:int cellCount:int slotLimit:int anyFree:bool packed:bool performanceFish:bool novelOverridesStack:bool cargoOverridesStack:bool acceptsNovelDef:bool acceptsCargoDef:bool occupied:string[] acceptedDefs:string[] acceptedShortHashes:int[] members:Member[]
Member|index:int thingId:int shortHash:int count:int stackLimit:int indexOf:int mapId:?int storingParentId:?int def:string mapCell:string mapPosition:string holderType:?string spawned:bool contains:bool memberValid:bool targetValid:bool everStorable:bool compTypes:string[]
Snapshot|tick:int mapId:int actor:Thing steel:Thing actorIdle:bool wrappers:Wrapper[] destinations:Destination[]
Preparation|parentKey:string stage:string allowance:long sourceRequired:long declaredCost:long charged:long ready:bool warmup:bool indexed:int status:Status before:Counter[] after:Counter[]
Trial|id:string kind:string query:string parcelId:string scopeIdentity:string parentKey:string cellAddress:string error:?string subjectId:int completed:bool disposed:bool openStatus:Status cell:Cell eligibility:Eligibility allowance:Work[] openWork:Work[] afterOpen:Work[] afterCell:Work[] afterEligibility:Work[] afterDispose:Work[] expectedFilterCharge:long before:Counter[] after:Counter[]
Native|route:string filterIdentity:?string cell:string receiverId:int argumentId:int actual:bool expected:bool before:Counter[] after:Counter[]
Status|usable:bool observation:string capability:string reason:string
Work|kind:string value:long
Cell|fixtureScene:string fixtureStage:string observationId:long query:string session:string mapId:int tick:int generation:long cell:string parentKey:?string groupKey:string provider:?string vacantKey:?string maximumSlots:?int itemCount:?int vacantSlots:?long gridEntries:?int status:Status stacks:Stack[] work:Work[]
Stack|key:string thingId:int def:string count:int stackLimit:int deficit:long providerTargetValid:?bool
Eligibility|fixtureScene:string fixtureStage:string observationId:long parcelId:string status:Status state:string vacantEligible:bool unitsPerNewStack:?int predicates:Predicate[] topUps:Edge[] work:Work[]
Predicate|name:string state:string reason:string targetId:?int
Edge|key:string targetId:int units:long
'@
    $schemas=@{}
    foreach($line in ($declarations -split '\r?\n')){$parts=$line.Split('|');$fields=@{};foreach($field in $parts[1].Split(' ')){$pair=$field.Split(':');$fields[$pair[0]]=$pair[1]};$schemas[$parts[0]]=$fields}
    return $schemas
}
function Test-AWNode($Value,[string]$Type,[string]$Label,$Schemas,$Problems,[int]$Depth=0){
    if($Depth-gt32){Assert-AW $false ($Label+' exceeds the source DTO depth bound.') $Problems;return}
    if($Type.StartsWith('?')){if($null-eq$Value){return};$Type=$Type.Substring(1)}
    if($Type.EndsWith('[]')){
        if($Value-isnot[array] -or $Value.Count-gt10000){Assert-AW $false ($Label+' must be a bounded array.') $Problems;return}
        $element=$Type.Substring(0,$Type.Length-2);for($i=0;$i-lt$Value.Count;$i++){Test-AWNode $Value[$i] $element ($Label+'['+$i+']') $Schemas $Problems ($Depth+1)};return
    }
    if($Type-ceq'nil'){Assert-AW ($null-eq$Value) ($Label+' must be the source null payload.') $Problems;return}
    if(-not$Schemas.ContainsKey($Type)){
        $schema=@{value=$Type};[void](Test-ABFields ([pscustomobject]@{value=$Value}) $schema ('CAP03-C '+$Label) $Problems);return
    }
    if($null-eq$Value -or $Value-isnot[pscustomobject] -or $Value-is[array]){Assert-AW $false ($Label+' must be a source DTO object.') $Problems;return}
    $schema=$Schemas[$Type];$names=@($Value.PSObject.Properties.Name)
    Assert-AW ($names.Count-eq$schema.Keys.Count) ($Label+' has missing or extra serialized fields.') $Problems
    foreach($name in $names){Assert-AW ($name-cin@($schema.Keys)) ($Label+' has an unknown or incorrectly cased serialized field: '+$name) $Problems}
    foreach($name in $schema.Keys){$prop=$Value.PSObject.Properties[$name];if($null-eq$prop){Assert-AW $false ($Label+' lacks '+$name) $Problems}else{Test-AWNode $prop.Value $schema[$name] ($Label+'.'+$name) $Schemas $Problems ($Depth+1)}}
}
function Test-AWShapeCore($Value,$Problems){$before=$Problems.Count;Test-AWNode $Value Global result (Get-AWSchemas) $Problems;return $Problems.Count-eq$before}
function Get-AWStatus([bool]$Complete=$true){if($Complete){return [pscustomobject]@{usable=$true;observation='Complete';capability='Supported';reason='None'}};return [pscustomobject]@{usable=$false;observation='Deferred';capability='Unsupported';reason='UnreviewedPredicate'}}
function Copy-AW($Value){return ConvertFrom-Json -InputObject (ConvertTo-Json -InputObject $Value -Depth 100 -Compress)}
function Get-AWHash($Value){$sha=[Security.Cryptography.SHA256]::Create();try{return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes((Get-ABCanonical $Value)))).Replace('-','')}finally{$sha.Dispose()}}
function Get-AWCell([int]$X,[int]$Z){return '('+$X+', 0, '+$Z+')'}
function Test-AWCounters($Rows,$Wrappers,$Problems){
    Assert-AW ($Rows.Count-eq$Wrappers.Count) 'counter census omitted or invented an inner.' $Problems
    for($i=0;$i-lt[math]::Min($Rows.Count,$Wrappers.Count);$i++){$c=$Rows[$i];$w=$Wrappers[$i];Assert-AW ($c.innerId-eq$w.inner.thingId -and $c.identity-ceq$w.innerIdentity -and $c.stackCalls-ge0 -and $c.hitPointReads-ge0) 'counter ID/reference/order or monotonic range differs.' $Problems}
}
function Test-AWThing($Thing,[string]$Def,[string]$Cell,[int]$MapId,[int]$Limit,$Faction,$Stuff,[int]$HP,[bool]$Spawned,$Problems){
    Assert-AW ($Thing.thingId-gt0 -and $Thing.def-ceq$Def -and $Thing.cell-ceq$Cell -and $Thing.count-eq1 -and $Thing.stackLimit-eq$Limit -and $Thing.hitPoints-eq$HP -and (Test-ABSame $Thing.faction $Faction) -and (Test-ABSame $Thing.stuff $Stuff) -and $Thing.spawned-eq$Spawned -and -not$Thing.destroyed -and $Thing.heldByFixtureMap-eq$Spawned -and (Test-ABSame $Thing.mapId $(if($Spawned){$MapId}else{$null})) -and $Thing.holderType-ceq$(if($Spawned){'Verse.Map'}else{'RimWorld.MinifiedThing'})) ('actual '+$Def+' physical state/custody differs.') $Problems
}
function Test-AWWrapper($W,$Receipt,[string]$Cell,[int]$MapId,$Faction,[int]$BaseRef,$Problems){
    $inner='HaulersDream.RuntimeHarness.Cap03WrapperInner';$outer='RimWorld.MinifiedThing'
    Test-AWThing $W.outer MinifiedThing $Cell $MapId 1 $null $null 100 $true $Problems
    Test-AWThing $W.inner HDHarness_CAP03C_Inner $Receipt.before.cell $MapId 1 $Faction $null 100 $false $Problems
    Assert-AW ($W.outerType-ceq$outer -and $W.innerType-ceq$inner -and $W.innerDefPackage-ceq'giwaffed.haulersdream.runtimeharness' -and $W.innerParentType-ceq$outer -and $W.outerIdentity-ceq('ref:'+($BaseRef+1)) -and $W.innerIdentity-ceq('ref:'+$BaseRef) -and $W.ownerIdentity-ceq('ref:'+($BaseRef+2)) -and $W.innerHoldingOwnerIdentity-ceq$W.ownerIdentity -and $W.innerParentIdentity-ceq$W.outerIdentity -and $W.sameInner -and $W.directContainsInner -and $W.innerOwnerMatches -and $W.innerParentIsOuter -and $W.innerSpawnedOrParentSpawned -and $W.innerMapHeldId-eq$MapId -and $W.directCount-eq1 -and $W.setupMaximumHitPoints-eq100 -and (Test-ABSame $W.directIds @($W.inner.thingId)) -and (Test-ABSame $W.outerComps @('RimWorld.CompForbiddable','RimWorld.CompStyleable')) -and $W.innerComps.Count-eq0 -and $W.inner.thingId-eq$Receipt.before.thingId -and $W.outer.thingId-eq$Receipt.after.outer.thingId) 'native wrapper/inner/direct owner/reference graph differs from normal minification.' $Problems
}
function Test-AWSettings($Value,[int]$Settings,[int]$Filter,$Owner,$Type,[bool]$Effective,[string]$FixedKind,$Problems){
    Assert-AW ($Value.settingsIdentity-ceq('ref:'+$Settings) -and $Value.filterIdentity-ceq('ref:'+$Filter) -and (Test-ABSame $Value.ownerIdentity $Owner) -and (Test-ABSame $Value.ownerType $Type) -and $Value.priority-ceq$(if($Effective){'Critical'}else{'Normal'})) 'settings/filter/owner/reference or priority differs.' $Problems
    $f=$Value.contents
    Assert-AW ($f.type-ceq'Verse.ThingFilter' -and -not$f.onlySpecial -and $f.hitPointsMin-eq$(if($Effective){0.5}else{0}) -and $f.hitPointsMax-eq1 -and $f.mentalBreakMin-eq0 -and $f.mentalBreakMax-eq1 -and $f.qualities-ceq'Awful~Legendary' -and (Test-ABSame $f.disallowedSpecials @(if(-not$Effective -and $FixedKind-ceq'shelf'){'AllowLargeCorpses'}))) 'effective/fixed native filter type/ranges/special census differs.' $Problems
    if($Effective){Assert-AW (Test-ABSame $f.allowedDefs @('HDHarness_CAP03C_Inner','Steel')) 'explicit effective allowed definitions differ.' $Problems}
    else{
        # Entire inspected native fixed filters, not only Steel/inner membership. Source shapes:
        # stockpile EverStorable(true), Shelf excludes Buildings, Neat excludes BuildingsNeatStorage.
        $profiles=@{zone=@(494,'CC19BE6DC43702975DF8314B8B28AE828D80BCCCFCA077BE39AC7DF485BB8CFD');shelf=@(367,'A7F9626A49276985DC214AA16CAA7727F655316D2C8257E9E550374803A03D6B');neat=@(456,'CD983FB2ACADE5B31A7E713DDD39676B7BF1E0B637E685F44AB765573A3167AA')}
        $profile=$profiles[$FixedKind];Assert-AW ($f.allowedDefs.Count-eq$profile[0] -and (Get-AWHash $f)-ceq$profile[1] -and 'Steel'-cin$f.allowedDefs -and ('HDHarness_CAP03C_Inner'-cin$f.allowedDefs)-eq($FixedKind-cne'shelf')) 'complete native fixed-filter catalog/category semantics differ.' $Problems
    }
}
function Test-AWDestination($P,$S,$D,[int]$Index,[int]$Ordinal,[int]$AX,[int]$AZ,$Problems){
    $zone=$Index-eq0;$asf=$S.asf;$resident=$S.resident -and $Ordinal-eq0;$anchor=Get-AWCell ($AX+5*$Ordinal) $AZ
    $cells=@($anchor);if($Index-eq2){$cells+=Get-AWCell ($AX+1) $AZ}
    # Id() allocates references in source observation order. Neat fixed settings remain shared.
    $refs=@(@(7,8,0,9,10,11,12),@(21,22,23,24,25,26,27),@(36,37,0,38,39,40,41),@(50,51,52,53,54,26,27))[$Index]
    if($Ordinal-eq1){$refs=@(55,56,57,58,59,26,27)}
    $type=$(if($zone){'RimWorld.Zone_Stockpile'}elseif($asf){'AdaptiveStorage.ThingClass'}else{'RimWorld.Building_Storage'})
    $key=$(if($zone){'zone:0'}else{'building:'+$D.thing.thingId});$fixedKind=$(if($zone){'zone'}elseif($asf){'neat'}else{'shelf'})
    Assert-AW ($D.key-ceq$key -and $D.identity-ceq('ref:'+$refs[0]) -and $D.slotIdentity-ceq('ref:'+$refs[1]) -and $D.runtimeType-ceq$type -and $null-eq$D.groupIdentity -and $D.registered -and $D.zoneRegistered-eq$zone -and (Test-ABSame $D.registryIdentity $(if($asf){'ref:'+$refs[2]}else{$null})) -and (Test-ABSame $D.cells $cells) -and (Test-ABSame $D.compTypes @(if($Index-eq2){'RimWorld.CompStyleable'}))) 'actual destination key/type/reference/slot/footprint/registration differs.' $Problems
    if($zone){Assert-AW ($null-eq$D.thing -and $null-eq$D.defPackage) 'stockpile invents a building.' $Problems}
    else{Assert-AW ($D.defPackage-ceq$(if($asf){'sbz.neatstorage'}else{'ludeon.rimworld'})) 'destination definition provenance differs.' $Problems;Test-AWThing $D.thing $(if($asf){'sbz_SmallCrate'}else{'Shelf'}) $anchor $P.mapId 1 $S.beforeProtected.actor.faction WoodLog 65 $true $Problems}
    Test-AWSettings $D.effective $refs[3] $refs[4] $D.identity $type $true $fixedKind $Problems
    Test-AWSettings $D.declaredFixed $refs[5] $refs[6] $null $null $false $fixedKind $Problems
    Assert-AW (Test-ABSame $D.declaredFixed $D.interfaceFixed) 'unlinked declared/interface fixed settings do not share their real object/content.' $Problems
    Assert-AW ($D.grids.Count-eq$cells.Count) 'native physical grid coverage differs.' $Problems
    for($i=0;$i-lt[math]::Min($D.grids.Count,$cells.Count);$i++){
        $expected=@(if(-not$zone){$D.thing};if($resident -and $i-eq0){$S.beforeProtected.wrappers[0].outer})
        Assert-AW ($D.grids[$i].cell-ceq$cells[$i] -and $D.grids[$i].nativeMaximum-eq$(if($zone){1}elseif($asf){6}else{3}) -and (Test-ABSame $D.grids[$i].things $expected)) 'complete physical native grid order/occupancy/capacity differs.' $Problems
    }
    if(-not$asf){Assert-AW ($null-eq$D.registry) 'native destination invents an ASF registry.' $Problems;return}
    # OccupiedRect describes the building footprint, including an empty crate.
    $r=$D.registry;$count=[int]$resident
    Assert-AW ($r.collectionType-ceq'AdaptiveStorage.ThingCollection' -and $r.count-eq$count -and $r.cellWiseCount-eq$count -and $r.cellCount-eq$count -and $r.slotLimit-eq6 -and $r.anyFree -and -not$r.packed -and -not$r.performanceFish -and $r.novelOverridesStack -and -not$r.cargoOverridesStack -and -not$r.acceptsNovelDef -and -not$r.acceptsCargoDef -and $r.acceptedDefs.Count-eq0 -and $r.acceptedShortHashes.Count-eq0 -and (Test-ABSame $r.occupied @($anchor)) -and $r.members.Count-eq$count) 'full ASF collection/capacity/accepted-def/occupied-cell census differs.' $Problems
    if($resident -and $r.members.Count-eq1){$m=$r.members[0];$w=$S.beforeProtected.wrappers[0];Assert-AW ($m.index-eq0 -and $m.indexOf-eq0 -and $m.thingId-eq$w.outer.thingId -and $m.shortHash-eq59807 -and $m.count-eq1 -and $m.stackLimit-eq1 -and $m.mapId-eq$P.mapId -and $m.storingParentId-eq$D.thing.thingId -and $m.def-ceq'MinifiedThing' -and $m.mapCell-ceq$anchor -and $m.mapPosition-ceq$anchor -and $m.holderType-ceq'Verse.Map' -and $m.spawned -and $m.contains -and $m.memberValid -and $m.targetValid -and $m.everStorable -and (Test-ABSame $m.compTypes $w.outerComps)) 'actual full wrapper membership/custody/target validity differs.' $Problems}
}
function Test-AWScene($P,$S,[int]$Index,[int]$CX,[int]$CZ,$Problems){
    $ids=@('incoming-native-stockpile','incoming-neat','resident-native-shelf','resident-neat');$asf=($Index%2)-eq1;$resident=$Index-ge2;$parentCount=$(if($Index-eq3){2}else{1})
    $ax=$CX+$(if($asf){10}else{-14});$az=$CZ+$(if($resident){8}else{-8});$b=$S.beforeProtected
    Assert-AW ($S.id-ceq$ids[$Index] -and $S.resident-eq$resident -and $S.asf-eq$asf -and $S.completed -and $S.restored -and $null-eq$S.error -and $S.startedTick-eq$P.startedTick -and $S.finishedTick-eq$P.finishedTick -and $S.zonesBefore-eq0 -and $S.zonesAfter-eq0 -and $S.groupsBefore-eq0 -and $S.groupsAfter-eq0 -and $S.minifications.Count-eq2 -and $b.wrappers.Count-eq2 -and $b.destinations.Count-eq$parentCount -and $S.preparations.Count-eq2*$parentCount -and $S.trials.Count-eq2 -and $S.nativeControls.Count-eq$(if($resident){5}else{4})) 'source scene roster/completion/coverage differs.' $Problems
    if($S.minifications.Count-ne2 -or $b.wrappers.Count-ne2 -or $b.destinations.Count-ne$parentCount){return}
    Assert-AW ($b.tick-eq$P.startedTick -and $b.mapId-eq$P.mapId -and $b.actorIdle -and (Test-ABSame $b $S.afterProtected) -and (Test-ABSame $b $S.afterNative)) 'neutral full physical snapshots changed across protected or native boundaries.' $Problems
    $faction=$b.actor.faction;Assert-AW ($faction-cmatch'^Faction_[0-9]+$') 'normal actor/player faction missing.' $Problems
    Test-AWThing $b.actor Human (Get-AWCell $CX ($CZ-20)) $P.mapId 1 $faction $null -1 $true $Problems
    Test-AWThing $b.steel Steel (Get-AWCell ($ax+1) ($az+4)) $P.mapId 75 $null $null -1 $true $Problems
    $baseRef=@(1,15,30,44)[$Index]
    for($i=0;$i-lt2;$i++){
        $r=$S.minifications[$i];$oldCell=Get-AWCell ($ax-3+2*$i) ($az+4)
        Test-AWThing $r.before HDHarness_CAP03C_Inner $oldCell $P.mapId 1 $faction $null 100 $true $Problems
        Test-AWWrapper $r.after $r $oldCell $P.mapId $faction ($baseRef+3*$i) $Problems
        Test-AWWrapper $b.wrappers[$i] $r $(if($resident -and $i-eq0){Get-AWCell $ax $az}else{$oldCell}) $P.mapId $faction ($baseRef+3*$i) $Problems
        $expectedInner=Copy-AW $r.before;$expectedInner.spawned=$false;$expectedInner.mapId=$null;$expectedInner.heldByFixtureMap=$false;$expectedInner.holderType='RimWorld.MinifiedThing'
        Assert-AW (Test-ABSame $r.after.inner $expectedInner) 'normal minification changed original inner physical identity or contents.' $Problems
        $subset=@($b.wrappers[0..$i]);Test-AWCounters $r.countersBefore $subset $Problems;Test-AWCounters $r.countersAfter $subset $Problems
        Assert-AW (Test-ABSame $r.countersBefore $r.countersAfter) 'normal minification receipt contradicts its counter boundary.' $Problems
        # Thing.PostMake -> Building setter reads once; Building.SpawnSetup -> repair lister reads once.
        # No reset, reflection seeding, override exemption or snapshot virtual HP invocation exists.
        foreach($c in $r.countersBefore){Assert-AW ($c.stackCalls-eq0 -and $c.hitPointReads-eq2) 'normally initialized inner baseline contradicts source setter/spawn reads.' $Problems}
    }
    for($i=0;$i-lt$parentCount;$i++){Test-AWDestination $P $S $b.destinations[$i] $Index $i $ax $az $Problems}
    Assert-AW ($S.parentKey-ceq$b.destinations[0].key -and $S.positiveParentKey-ceq$b.destinations[$parentCount-1].key -and $S.cell-ceq(Get-AWCell $ax $az) -and $S.positiveCell-ceq(Get-AWCell ($ax+$(if($Index-eq2){1}elseif($Index-eq3){5}else{0})) $az)) 'scene primary/matched positive address linkage differs.' $Problems
    foreach($rows in @('protectedBegin','protectedEnd','nativeEnd','retirementEnd')){Test-AWCounters $S.$rows $b.wrappers $Problems}
    Assert-AW ((Test-ABSame $S.protectedBegin $S.protectedEnd) -and (Test-ABSame $S.nativeEnd $S.retirementEnd)) 'protected or cleanup boundary changed the inner counters.' $Problems
    for($i=0;$i-lt2;$i++){$c=$S.protectedBegin[$i];Assert-AW ($c.stackCalls-eq0 -and $c.hitPointReads-eq$(if($i-eq0){@(2,2,3,4)[$Index]}else{2})) 'normal destination registration baseline contradicts source native acceptance reads.' $Problems}
    $previous=$S.protectedEnd;$routes=@('wrapper-versus-steel','wrapper-versus-peer','effective-filter-inner-hp','effective-recursive-inner-hp');if($resident){$routes+='full-resident-native-cell'}
    for($i=0;$i-lt[math]::Min($routes.Count,$S.nativeControls.Count);$i++){
        $n=$S.nativeControls[$i];$expected=$i-ge2 -and -not($i-eq3 -and $Index-eq2);$hp=$i-in@(2,3);$stack=$(if($i-in@(1,4)){1}else{0})
        Test-AWCounters $n.before $b.wrappers $Problems;Test-AWCounters $n.after $b.wrappers $Problems
        $argument=$(if($i-eq0){$b.steel.thingId}elseif($i-in@(1,4)){$b.wrappers[1].outer.thingId}else{$b.wrappers[0].outer.thingId})
        Assert-AW ($n.route-ceq$routes[$i] -and $n.receiverId-eq$b.wrappers[0].outer.thingId -and $n.argumentId-eq$argument -and $n.cell-ceq$S.cell -and (Test-ABSame $n.filterIdentity $(if($hp){$b.destinations[0].effective.filterIdentity}else{$null})) -and $n.expected-eq$expected -and $n.actual-eq$expected -and (Test-ABSame $n.before $previous) -and (Test-ABSame $n.before[1] $n.after[1]) -and $n.after[0].stackCalls-$n.before[0].stackCalls-eq$stack -and $(if($hp){$n.after[0].hitPointReads-gt$n.before[0].hitPointReads}else{$n.after[0].hitPointReads-eq$n.before[0].hitPointReads})) 'ordered native control result/receiver/filter/cell/per-inner directional counter delta differs.' $Problems
        $previous=$n.after
    }
    Assert-AW (Test-ABSame $previous $S.nativeEnd) 'native endpoint lacks its last actual native receipt.' $Problems
    $retired=@($b.wrappers[1].outer.thingId,$b.wrappers[0].outer.thingId);for($i=$parentCount-1;$i-ge0;$i--){if($null-ne$b.destinations[$i].thing){$retired+=$b.destinations[$i].thing.thingId}};$retired+=@($b.steel.thingId,$b.wrappers[1].inner.thingId,$b.wrappers[0].inner.thingId)
    Assert-AW ((Test-ABSame $S.retiredThingIds $retired) -and $S.retiredDestinations.Count-eq$parentCount) 'native wrapper-first retirement ID/order/census differs.' $Problems
    for($i=0;$i-lt[math]::Min($parentCount,$S.retiredDestinations.Count);$i++){$r=$S.retiredDestinations[$i];$d=$b.destinations[$i];Assert-AW ($r.parentKey-ceq$d.key -and (Test-ABSame $r.cells $d.cells) -and $r.slotsCleared -and -not$r.zoneRegistered -and (Test-ABSame $r.asfCount $(if($asf){0}else{$null}))) 'native/ASF destination cleanup leaves residual ownership or registration.' $Problems}
}
function Get-AWCosts($P,$S,$D,[string]$Kind,[string]$Operation){
    [long[]]$v=@(0L,0L,0L,0L,0L,0L,0L,0L,0L,0L,0L,0L,0L,0L)
    if($Operation-ceq'open'){$v[11]=$P.gameMaps;return ,$v}
    $resident=$Kind-ceq'resident-boundary';$positive=$Kind-ceq'ordinary-positive';[long]$items=[int]$resident;[long]$grid=$items+[int]($null-ne$D.thing)
    # ObserveCell: resolve once, observe and revalidate complete grid/limit/ASF census.
    if($Operation-ceq'cell'){$v[0]=1;$v[1]=1;$v[2]=2*$grid;$v[6]=$D.compTypes.Count+$(if($S.asf){4*$items}else{0});$v[7]=$(if($S.asf){2*(1+4*$items)}else{0});$v[8]=2;$v[11]=2;$v[13]=2+3*$items;return ,$v}
    # Incoming GetParcel guard precedes ValidateCell, output and all predicates.
    if($Kind-ceq'incoming-boundary'){$v[11]=1;return ,$v}
    [long]$validations=$(if($positive){2}else{1});$v[2]=$validations*$grid;$v[8]=$validations;$v[11]=1+$validations;$v[13]=10+5*$items
    $v[5]=3+$D.effective.contents.disallowedSpecials.Count+2*$D.declaredFixed.contents.disallowedSpecials.Count+$(if($S.asf){1+$D.declaredFixed.contents.disallowedSpecials.Count}else{0})+[int]$positive
    if($S.asf){$v[6]=2*$items*$validations;$v[7]=(1+4*$items)*$validations}
    # Native preflight inspects parent first (not EverStorable), then the full native wrapper.
    # The wrapper is not a top-up candidate, but still requires 1+2*2 comp/stack precharges.
    $v[6]+=$grid+5*$items
    if($positive){
        # ProjectionWork.NativeCellPredicate and globally installed ASF patch upper bounds.
        $v[3]=4*$grid;$v[4]=1;$v[6]+=2*$grid;$v[7]+=2*$grid+1;$v[8]+=$grid+1;$v[9]=1;$v[10]=1
    }
    return ,$v
}
function Test-AWPreparation($P,$S,$Row,$D,[bool]$Full,$Problems){
    [long]$cost=4L*([long]$P.thingDefs+1)*([long]$P.categoryDefs+1)*([long]$P.specialDefs+1)+3L*$D.cells.Count+$D.compTypes.Count+$P.gameMaps
    $status=$(if($Full){Get-AWStatus}else{[pscustomobject]@{usable=$false;observation='Deferred';capability='Supported';reason='ProviderInitializing'}})
    Assert-AW ($Row.parentKey-ceq$D.key -and $Row.stage-ceq$(if($Full){'complete-allowance'}else{'zero-allowance'}) -and $Row.sourceRequired-eq$cost -and $Row.allowance-eq$(if($Full){$cost}else{0}) -and $Row.declaredCost-eq$Row.allowance -and $Row.charged-eq$Row.allowance -and $Row.ready-eq$Full -and $Row.warmup -and $Row.indexed-eq$(if($Full -and $null-eq$D.thing){$D.cells.Count}else{0}) -and (Test-ABSame $Row.status $status) -and (Test-ABSame $Row.before $S.protectedBegin) -and (Test-ABSame $Row.after $Row.before)) 'preparation source reservation/readiness/warmup/index/counter boundary differs.' $Problems
}
function Get-AWPredicates{return @('destination-enabled','destination-faction','selected-priority','effective-thing-filter','concrete-fixed-filter','asf-declared-fixed-filter','asf-actual-member-capacity','native-IsGoodStoreCell','hd-explicit-context-filter')}
function Test-AWTrial($P,$S,$T,$D,[string]$Kind,[int]$ScopeRef,$Problems){
    $positive=$Kind-ceq'ordinary-positive';$incoming=$Kind-ceq'incoming-boundary';$resident=$Kind-ceq'resident-boundary';$id=$S.id+'/'+$Kind;$subject=$(if($incoming){$S.beforeProtected.wrappers[0].outer}else{$S.beforeProtected.steel})
    $address=$(if($positive){$S.positiveCell}else{$S.cell});$cell=Read-ABCell $address $Problems
    $open=Get-AWCosts $P $S $D $Kind open;$observed=Get-AWCosts $P $S $D $Kind cell;$eligibility=Get-AWCosts $P $S $D $Kind eligibility
    Assert-AW ($T.id-ceq$id -and $T.kind-ceq$Kind -and $T.query-ceq('CAP03-C/'+$id) -and $T.parcelId-ceq($id+'/parcel') -and $T.scopeIdentity-ceq('ref:'+$ScopeRef) -and $T.parentKey-ceq$D.key -and $T.cellAddress-ceq$address -and $T.subjectId-eq$subject.thingId -and $null-eq$T.error -and $T.completed -and $T.disposed -and (Test-ABSame $T.openStatus (Get-AWStatus)) -and (Test-ABSame $T.before $S.protectedBegin) -and (Test-ABSame $T.after $T.before) -and $T.expectedFilterCharge-eq$eligibility[5]) 'fresh trial/subject/parent/query/scope/closure/counter boundary differs.' $Problems
    Test-ABWork $T.allowance @(16L,8L,8192L,8192L,256L,4096L,8192L,8192L,16384L,256L,256L,16384L,0L,4096L) 'C full operation allowance' $Problems
    Test-ABWork $T.openWork $open 'C open operation' $Problems;Test-ABWork $T.afterOpen $open 'C after open' $Problems
    Test-ABWork $T.cell.work $observed 'C cell operation' $Problems;Test-ABWork $T.eligibility.work $eligibility 'C eligibility operation' $Problems
    $sum=@(for($i=0;$i-lt14;$i++){$open[$i]+$observed[$i]});Test-ABWork $T.afterCell $sum 'C after cell' $Problems
    $sum=@(for($i=0;$i-lt14;$i++){$sum[$i]+$eligibility[$i]});Test-ABWork $T.afterEligibility $sum 'C after eligibility' $Problems;Test-ABWork $T.afterDispose $sum 'C after dispose' $Problems
    $c=$T.cell;$items=[int]$resident;$maximum=$(if($S.asf){6}elseif($null-eq$D.thing){1}else{3})
    $prefix=$P.session+'/'+$P.mapId+'/'+$D.key+'/'+$cell.x+','+$cell.z
    Assert-AW ($c.fixtureScene-ceq$S.id -and $c.fixtureStage-ceq$Kind -and $c.observationId-eq1 -and $c.query-ceq$T.query -and $c.session-ceq$P.session -and $c.mapId-eq$P.mapId -and $c.tick-eq$P.startedTick -and $c.generation-eq1 -and $c.cell-ceq$address -and $c.parentKey-ceq$D.key -and $c.groupKey-ceq'concrete' -and $c.provider-ceq$(if($S.asf){$P.asfIdentity}else{'RimWorld physical slots'}) -and $c.vacantKey-ceq($prefix+'/vacant') -and $c.maximumSlots-eq$maximum -and $c.itemCount-eq$items -and $c.vacantSlots-eq$maximum-$items -and $c.gridEntries-eq$items+[int]($null-ne$D.thing) -and (Test-ABSame $c.status (Get-AWStatus)) -and $c.stacks.Count-eq$items) 'returned cell/resource/scope/status and actual occupancy linkage differ.' $Problems
    if($resident -and $c.stacks.Count-eq1){$w=$S.beforeProtected.wrappers[0].outer;$stackResource=$c.stacks[0];Assert-AW ($stackResource.key-ceq($prefix+'/stack:'+$w.thingId) -and $stackResource.thingId-eq$w.thingId -and $stackResource.def-ceq'MinifiedThing' -and $stackResource.count-eq1 -and $stackResource.stackLimit-eq1 -and $stackResource.deficit-eq0 -and (Test-ABSame $stackResource.providerTargetValid $(if($S.asf){$true}else{$null}))) 'full native resident resource was replaced or treated as a deficit.' $Problems}
    $e=$T.eligibility;$names=Get-AWPredicates;$predicates=@()
    if(-not$incoming){for($i=0;$i-lt$names.Count;$i++){$reached=$i-lt5 -or ($S.asf -and $i-lt7) -or ($positive -and $i-ge7);if($i-eq1 -and $null-eq$D.thing){$reached=$false};$predicates+=[pscustomobject]@{name=$names[$i];state=$(if($reached){'Eligible'}else{'NotEvaluated'});reason='None';targetId=$null}}}
    Assert-AW ($e.fixtureScene-ceq$S.id -and $e.fixtureStage-ceq$Kind -and $e.observationId-eq$c.observationId -and $e.parcelId-ceq$T.parcelId -and (Test-ABSame $e.status (Get-AWStatus $positive)) -and $e.state-ceq$(if($positive){'Eligible'}else{'NotEvaluated'}) -and $e.vacantEligible-eq$positive -and (Test-ABSame $e.unitsPerNewStack $(if($positive){75}else{$null})) -and (Test-ABSame $e.predicates $predicates) -and $e.topUps.Count-eq0) 'typed wrapper boundary, exact predicate reachability, quantity entitlement or ordinary positive differs.' $Problems
}
function Get-AWRecordPlan($P,$Problems){
    $plan=New-Object 'System.Collections.Generic.List[object]';$state=@{assertion=0};$texts=Get-AWAssertionTexts
    $emit={param($kind,$trial,$data) [void]$plan.Add([pscustomobject]@{kind=$kind;trial=$trial;data=$data})}
    $assert={param($id,$kind,$textKey,$jsonDetail)
        if($state.assertion-ge$P.assertions.Count){Assert-AW $false ('missing source assertion '+$id) $Problems;return}
        $r=$P.assertions[$state.assertion];$state.assertion++
        Assert-AW ($r.sequence-eq$state.assertion -and $r.id-ceq$id -and $r.kind-ceq$kind -and $r.passed) ('source assertion ID/kind/order/outcome differs: '+$id) $Problems
        if($null-ne$textKey){Assert-AW ($r.detail-ceq$texts[$textKey]) ('source assertion detail differs: '+$id) $Problems}
        else{
            # An omitted earlier assertion must remain a catalog rejection, not derail
            # later checks by parsing its now-misaligned prose as structured evidence.
            try{$actualDetail=ConvertFrom-Json -InputObject $r.detail;Assert-AW (Test-ABSame $actualDetail $jsonDetail) ('source assertion structured detail differs: '+$id) $Problems}
            catch{Assert-AW $false ('source assertion lacks its structured detail: '+$id) $Problems}
        }
        & $emit assertion $null $r
    }
    $spawn={param($thing) & $assert ('spawn-'+$thing.thingId+'-'+$thing.cell) fixture C06}
    $census={param($s,$stage,$snap,$counters)
        & $emit census-begin ($s.id+'/'+$stage) $counters;& $emit census-end ($s.id+'/'+$stage) $counters
        & $assert ($s.id+'-neutral-'+$stage) fixture N00;& $emit $stage $s.id $snap
    }
    $trial={param($s,$t)
        # Capture precedes opening/allowance assignment. Preserve every real null/default field.
        $begin=Copy-AW $t
        foreach($field in @('scopeIdentity','openStatus','cell','eligibility','allowance','openWork','afterOpen','afterCell','afterEligibility','afterDispose','after')){$begin.$field=$null}
        $begin.completed=$false;$begin.disposed=$false;$begin.expectedFilterCharge=0L
        & $emit trial-begin $s.id $begin
        & $emit open $t.id ([pscustomobject]@{status=$t.openStatus;query=$t.query;scopeIdentity=$t.scopeIdentity;work=$t.openWork})
        & $assert ($t.id+'-open') fixture $null $t.openStatus;& $assert ($t.id+'-open-work') behavior T03
        & $emit cell $t.id $t.cell;& $assert ($t.id+'-cell-work') behavior T04;& $assert ($t.id+'-cell-identity') fixture T05
        if($t.kind-ceq'resident-boundary'){& $assert ($t.id+'-resident-resource') fixture T06}
        & $emit eligibility $t.id $t.eligibility;& $assert ($t.id+'-eligibility-work') behavior T07;& $assert ($t.id+'-parcel-identity') behavior T08
        & $assert ($t.id+'-typed-outcome') behavior T11;& $assert ($t.id+'-quantity') behavior T12;& $assert ($t.id+'-filter-work') behavior T13;& $assert ($t.id+'-predicate-shape') behavior T14
        if($t.kind-cne'incoming-boundary'){foreach($name in (Get-AWPredicates)){& $assert ($t.id+'-predicate-'+$name) behavior T15}}
        & $assert ($t.id+'-disposed') behavior T09;& $assert ($t.id+'-callback-boundary') behavior T10;& $emit trial-result $s.id $t
    }
    & $assert expectation fixture S05;& $assert main-thread-map fixture S06;& $emit definition $null $P.definition;& $assert case-only-inner-definition fixture S07
    & $emit bindings $null ([pscustomobject]@{assemblies=$P.assemblies;bindings=$P.bindings});& $assert actual-asf fixture S08
    for($i=0;$i-lt$P.inputs.Count;$i++){& $emit input $null $P.inputs[$i];& $assert ('input-'+($i+1)) fixture $null $P.inputs[$i]}
    & $assert private-bounds fixture S09;& $assert normal-actor fixture S10;& $assert idle-actor fixture S11
    & $emit catalog $null $P.catalogStatus;& $assert catalog-created fixture $null $P.catalogStatus;& $emit patch-inventory $null $P.patchInventory
    foreach($s in $P.scenes){
        foreach($r in $s.minifications){& $spawn $r.before;& $assert ($s.id+'-healthy-inner-'+$r.before.thingId) fixture C08;& $spawn $r.after.outer;& $emit minification $s.id $r;& $assert ($s.id+'-native-inner-custody-'+$r.before.thingId) fixture C09}
        & $spawn $s.beforeProtected.steel
        foreach($d in $s.beforeProtected.destinations){if($null-ne$d.thing){& $spawn $d.thing;& $assert ($s.id+'-destination-'+$d.thing.thingId) fixture C07}}
        if($s.resident -and -not$s.asf){& $assert ($s.id+'-two-native-shelf-cells') fixture C00}
        foreach($d in $s.beforeProtected.destinations){& $assert ($s.id+'-effective-filter-'+$d.key) fixture C01}
        if($s.resident){& $spawn $s.beforeProtected.wrappers[0].outer}
        & $census $s physical-before $s.beforeProtected $s.protectedBegin
        & $assert ($s.id+'-two-full-wrappers') fixture C10
        foreach($d in $s.beforeProtected.destinations){& $assert ($s.id+'-physical-destination-'+$d.key) fixture C11;& $assert ($s.id+'-inner-fixed-semantics-'+$d.key) fixture C12;if($s.asf){& $assert ($s.id+'-nonfull-asf-'+$d.key) fixture C13}}
        & $assert ($s.id+'-correct-parcel-locations') fixture C14;& $emit protected-begin $s.id $s.protectedBegin
        foreach($r in $s.preparations){& $emit preparation $s.id $r;& $assert ($s.id+'-prepared-'+$r.parentKey+'-'+$r.stage) fixture T00;& $assert ($s.id+'-prepare-counters-'+$r.parentKey+'-'+$r.stage) behavior T01}
        foreach($t in $s.trials){& $trial $s $t}
        & $emit protected-end $s.id $s.protectedEnd;& $census $s physical-after-protected $s.afterProtected $s.protectedEnd
        & $assert ($s.id+'-protected-counters') behavior C02;& $assert ($s.id+'-protected-physical') behavior C03
        foreach($n in $s.nativeControls){& $emit native-control $s.id $n;& $assert ($s.id+'-native-'+$n.route) behavior T16}
        & $emit native-end $s.id $s.nativeEnd;& $census $s physical-after-native $s.afterNative $s.nativeEnd
        & $assert ($s.id+'-native-physical') behavior C04;& $assert ($s.id+'-retired') behavior C05;& $emit scene-result $s.id $s
    }
    & $assert four-independent-scenes behavior S00;& $assert eight-native-wrappers behavior S01;& $assert eight-closed-scopes behavior S02;& $assert eight-distinct-scopes behavior S03;& $assert same-tick fixture S04
    Assert-AW ($state.assertion-eq309 -and $P.assertions.Count-eq309 -and @($P.assertions.id|Select-Object -Unique).Count-eq309 -and $plan.Count-eq446) 'complete source 309-assertion/446-record catalog has omissions, repetitions or extras.' $Problems
    return $plan.ToArray()
}
function Test-AWECore($Value,[string]$ExpectedBehavior,$Problems){
    if(-not(Test-AWShapeCore $Value $Problems)){return}
    $p=$Value.storageProjectionWrappers
    Assert-AW ($Value.schemaVersion-eq1 -and $Value.processId-gt0 -and $Value.runId-cmatch'^[0-9a-f]{32}$' -and $Value.caseId-ceq'CAP03-C' -and $Value.status-ceq'passed' -and $Value.executingGameVersion-ceq'1.6.4871 rev591' -and $Value.installedVersionFile-ceq'1.6.4871 rev590' -and $Value.unityErrorsObserved-eq0 -and [datetimeoffset]$Value.finishedUtc-gt[datetimeoffset]$Value.startedUtc -and $Value.detail-ceq'Actual incoming/resident native minified wrapper containment and inner callback controls only; reviewed minified support and hauling remain unfinished.') 'global success/identity/time/error/detail contract differs.' $Problems
    Assert-AW ($ExpectedBehavior-ceq'satisfied' -and $p.expectedBehavior-ceq$ExpectedBehavior -and $p.caseId-ceq'CAP03-C' -and $p.contract-ceq'native-wrapper-boundary-v1' -and $p.status-ceq'passed' -and $null-eq$p.error -and $p.fixtureValid -and $p.requestedBehaviorSatisfied -and $p.expectationMatched -and -not$p.allCap03ComponentsSatisfied -and $p.scope-ceq'Incoming/resident native minified wrapper containment and independent inner Stack/HP counter controls only; not minified support or hauling.' -and (Test-ABSame $p.pendingControls @('reviewed-inner-identity-custody-predicate-support','actual-minified-hauling-and-save-lifecycle','large-groups-and-lifecycle','allocation-and-original-report-convergence')) -and $p.startedTick-ge6 -and $p.finishedTick-eq$p.startedTick -and $p.mainThread-gt0 -and $p.mapId-ge0 -and $p.session-cmatch'^[0-9a-f]{32}$' -and $p.thingDefs-eq1559 -and $p.categoryDefs-eq75 -and $p.specialDefs-eq38 -and $p.gameMaps-eq1 -and (Test-ABSame $p.catalogStatus (Get-AWStatus)) -and $p.scenes.Count-eq4) 'bounded component identity, profile, readiness, completion or unfinished scope differs.' $Problems
    $definition=[pscustomobject]@{def='HDHarness_CAP03C_Inner';runtimeType='HaulersDream.RuntimeHarness.Cap03WrapperInner';package='giwaffed.haulersdream.runtimeharness';category='Building';minifiedDef='MinifiedThing';outerType='RimWorld.MinifiedThing';minifiable=$true;everStorableWhenMinified=$true;everStorableAsBuilding=$false;useHitPoints=$true;withinBuildings=$true;withinNeat=$false;width=1;depth=1;outerStackLimit=1;categories=@('BuildingsFurniture')}
    Assert-AW (Test-ABSame $p.definition $definition) 'actual case-only definition/category/native wrapper shape differs.' $Problems
    Test-AWIdentity $Value $Problems
    $map=Get-ABOne $Value.assertions id real-map-initialized $Problems
    if($null-eq$map -or $map.observed-cnotmatch'^size=\(([1-9][0-9]*), 1, ([1-9][0-9]*)\); maps=1$'){Assert-AW $false 'map geometry unavailable.' $Problems;return}
    $sx=0;$sz=0;$xText=$Matches[1];$zText=$Matches[2]
    if(-not[int]::TryParse($xText,[ref]$sx) -or -not[int]::TryParse($zText,[ref]$sz) -or $sx-lt48 -or $sz-lt46){Assert-AW $false 'map geometry outside bounded native fixture range.' $Problems;return}
    $cx=[int][math]::Floor($sx/2);$cz=[int][math]::Floor($sz/2)
    if($p.scenes.Count-ne4){return}
    $allIds=New-Object 'System.Collections.Generic.List[int]';$scopes=New-Object 'System.Collections.Generic.List[string]';$actor=$p.scenes[0].beforeProtected.actor
    [void]$allIds.Add($actor.thingId)
    for($i=0;$i-lt4;$i++){
        $s=$p.scenes[$i];Test-AWScene $p $s $i $cx $cz $Problems
        Assert-AW (Test-ABSame $s.beforeProtected.actor $actor) 'independent scenes do not retain the same normal idle actor.' $Problems
        foreach($w in $s.beforeProtected.wrappers){[void]$allIds.Add($w.outer.thingId);[void]$allIds.Add($w.inner.thingId)};[void]$allIds.Add($s.beforeProtected.steel.thingId)
        for($j=0;$j-lt$s.beforeProtected.destinations.Count;$j++){$d=$s.beforeProtected.destinations[$j];if($null-ne$d.thing){[void]$allIds.Add($d.thing.thingId)};if($s.preparations.Count-gt2*$j+1){Test-AWPreparation $p $s $s.preparations[2*$j] $d $false $Problems;Test-AWPreparation $p $s $s.preparations[2*$j+1] $d $true $Problems}}
        if($s.trials.Count-eq2 -and $s.beforeProtected.destinations.Count-gt0){$scopeRefs=@(@(13,14),@(28,29),@(42,43),@(60,61))[$i];Test-AWTrial $p $s $s.trials[0] $s.beforeProtected.destinations[0] $(if($s.resident){'resident-boundary'}else{'incoming-boundary'}) $scopeRefs[0] $Problems;Test-AWTrial $p $s $s.trials[1] $s.beforeProtected.destinations[-1] ordinary-positive $scopeRefs[1] $Problems;foreach($t in $s.trials){[void]$scopes.Add($t.scopeIdentity)}}
    }
    Assert-AW ($allIds.Count-eq25 -and @($allIds|Select-Object -Unique).Count-eq25 -and $scopes.Count-eq8 -and @($scopes|Select-Object -Unique).Count-eq8) 'distinct physical scene objects/inner owners and fresh scopes were aliased or reused.' $Problems
    $plan=@(Get-AWRecordPlan $p $Problems)
    Assert-AW ($p.records.Count-eq$plan.Count) 'retained record catalog size differs from source plan.' $Problems
    for($i=0;$i-lt[math]::Min($p.records.Count,$plan.Count);$i++){$r=$p.records[$i];$want=$plan[$i];Assert-AW ($r.sequence-eq$i+1 -and $r.tick-eq$p.startedTick -and $r.kind-ceq$want.kind -and (Test-ABSame $r.trial $want.trial) -and (Test-ABSame (ConvertFrom-Json -InputObject $r.data) $want.data)) ('retained record '+($i+1)+' contradicts source chronology/complete DTO payload.') $Problems}
}


function Get-AWAssertionTexts {
    return @{
        'S00'='Every separate incoming/resident native/ASF scene completes and retires.'
        'S01'='Eight distinct native wrappers and eight distinct actual inner buildings, with no cross-scene ID reuse.'
        'S02'='One protected boundary scope and one ordinary positive scope per scene.'
        'S03'='Each operation pair used a fresh actual projection scope, not a recycled accepted handle.'
        'S04'='Synchronous main-thread fixture; no yielded frame/tick or executed hauling.'
        'S05'='Only explicit satisfied containment expectation is accepted.'
        'S06'='Actual initialized disposable home map on simulation thread.'
        'S07'='Actual patched native minifiable shape/category, without changing shared definitions.'
        'S08'='Reviewed actual ASF1.2.4 binary.'
        'S09'='No existing zone or player save cell overwritten.'
        'S10'='Actual normal human actor.'
        'S11'='No executing job or held cargo.'
        'C00'='Native Shelf supplies a matched empty second cell.'
        'C01'='Actual native effective filter permits both explicit defs and uses a nondefault HP range; no unreviewed special worker.'
        'C02'='No inner Stack or virtual HP getter invocation in preparation/projection/disposal.'
        'C03'='Full outer/inner custody, filters, grid and provider registry remain unchanged.'
        'C04'='Explicit counter-positive controls leave physical/filter state unchanged.'
        'C05'='All scene Things and native wrapper owners retired, native zone/group/slot registrations restored.'
        'C06'='Native spawn preserves exact object/map/cell ownership.'
        'C07'='Actual unlinked installed Neat or native Shelf class/definition.'
        'C08'='Normally initialized full HP and count1; no clamp or native field seeding.'
        'C09'='Native one-stack owner contains this exact original inner after normal minification.'
        'C10'='Actual full native wrappers with independent bound inner identities/custody.'
        'C11'='Exact real grid/capacity/filter/parent/slot identities before measurement.'
        'C12'='Native Shelf intentionally excludes the inner Building; stockpile/Neat accept its storable category.'
        'C13'='Normally registered valid non-full ASF collection; no full-member preflight masks the resident boundary.'
        'C14'='External ordinary/peer parcels, selected resident placement and a genuinely empty matched positive cell.'
        'T00'='Exact source-derived preparation reservation/readiness; an earlier unsupported or budget route cannot count as the wrapper boundary.'
        'T01'='Preparation does not invoke either inner callback.'
        'T03'='Open work and cumulative scope usage agree.'
        'T04'='Separate operation work reconciles to cumulative usage.'
        'T05'='Current scope returns the exact actual destination/occupancy before any eligibility call.'
        'T06'='Full real wrapper, not a top-up candidate, remains in the native preflight grid.'
        'T07'='Eligibility''s operation delta is distinguished from earlier cell work.'
        'T08'='Exact parcel and returned-cell binding.'
        'T09'='Scope closure preserves accounting and releases the active entry.'
        'T10'='No inner virtual callback across this exact scope lifetime.'
        'T11'='Required wrapper boundary differs from earlier filter/budget/provider failures and preserves an actual ordinary supported path.'
        'T12'='Containment publishes no quantity entitlement; empty positive cell uses real Steel stack limit.'
        'T13'='Source-derived filter precharges, separate from measured inner callback counts.'
        'T14'='Incoming guard precedes predicate construction; resident guard retains exact evaluated prefix.'
        'T15'='Exact allowed filter/ASF capacity boundary, then native and HD predicates remain unevaluated for resident containment.'
        'T16'='Post-protection actual native result and directional per-inner Stack/HP deltas; HP stat plumbing is not assumed to have one read.'
        'N00'='Census must not invoke either measured inner callback.'
    }
}

function Get-AWAssemblyCatalog {
    return @(foreach($spec in (Get-ABAssemblyCatalog)){
        if($spec.StartsWith('HaulersDream.RuntimeHarness|')){
            'HaulersDream.RuntimeHarness|0.1.0.0|15c008bf-ecf0-4270-a67c-34b7b81efe7f|0EBEC1537EB32F2D7652427098882E5C64F92AEFB6CBA1B69597602FDDC68AA6|Mods/HaulersDreamRuntimeHarness/Assemblies/HaulersDream.RuntimeHarness.dll'
        }else{$spec}
    })
}
function Get-AWBindingCatalog {
    # Exact bridge inputs 0..39 and source-native/inner bindings, resolved against frozen PE metadata.
    return @((Get-ABBindingCatalog)[0..39])+@(
        'Verse.ThingFilter.disallowedSpecialFilters;token=67125235;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'RimWorld.MinifyUtility.MakeMinified;token=100722736;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'RimWorld.MinifyUtility.GetInnerIfMinified;token=100722738;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'RimWorld.MinifiedThing.CanStackWith;token=100722716;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'RimWorld.MinifiedThing.GetDirectlyHeldThings;token=100722713;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'RimWorld.MinifiedThing.get_InnerThing;token=100722700;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'RimWorld.MinifiedThing.set_InnerThing;token=100722701;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'Verse.Thing.get_HitPoints;token=100677935;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'Verse.Thing.get_MaxHitPoints;token=100677937;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'HaulersDream.RuntimeHarness.Cap03WrapperInner.get_HitPoints;token=100664370;mvid=15c008bf-ecf0-4270-a67c-34b7b81efe7f',
        'HaulersDream.RuntimeHarness.Cap03WrapperInner.set_HitPoints;token=100664371;mvid=15c008bf-ecf0-4270-a67c-34b7b81efe7f',
        'HaulersDream.RuntimeHarness.Cap03WrapperInner.get_RawHitPoints;token=100664369;mvid=15c008bf-ecf0-4270-a67c-34b7b81efe7f',
        'HaulersDream.RuntimeHarness.Cap03WrapperInner.get_StackCalls;token=100664367;mvid=15c008bf-ecf0-4270-a67c-34b7b81efe7f',
        'HaulersDream.RuntimeHarness.Cap03WrapperInner.get_HitPointReads;token=100664368;mvid=15c008bf-ecf0-4270-a67c-34b7b81efe7f',
        'HaulersDream.RuntimeHarness.Cap03WrapperInner..ctor;token=100664373;mvid=15c008bf-ecf0-4270-a67c-34b7b81efe7f',
        'HaulersDream.RuntimeHarness.Cap03WrapperInner.CanStackWith;token=100664372;mvid=15c008bf-ecf0-4270-a67c-34b7b81efe7f',
        'Verse.ThingFilter.Allows;token=100685753;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'RimWorld.StorageSettings.AllowedToAccept;token=100723831;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'RimWorld.StoreUtility.IsGoodStoreCell;token=100696521;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'RimWorld.StoreUtility.NoStorageBlockersIn;token=100696515;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'RimWorld.Zone_Stockpile.GetParentStoreSettings;token=100711931;mvid=61e41735-6189-4da4-9d21-0260257b5097',
        'Verse.Zone.Delete;token=100672351;mvid=61e41735-6189-4da4-9d21-0260257b5097'
    )
}
function Get-AWPatchCatalog {return @(Get-ABPatchCatalog)}
function Get-AWGlobalAssertions($Value,$Problems){
    $native=Get-ABOne $Value.assemblies name Assembly-CSharp $Problems;if($null-eq$native){return @()}
    $path=$native.path.Replace('\','/');$suffix='/RimWorldWin64_Data/Managed/Assembly-CSharp.dll'
    if(-not$path.EndsWith($suffix) -or $path.Contains('/../') -or $path.Contains('/./')){$Problems.Add('CAP03-C native path does not describe the private runtime.');return @()}
    $runtime=$path.Substring(0,$path.Length-$suffix.Length);$base=$runtime.Substring(0,$runtime.Length-8)
    if($runtime-cnotmatch('/haulersdream-runtime-tests/'+[regex]::Escape($Value.runId)+'/runtime$')){$Problems.Add('CAP03-C assembly paths are not attached to this private run.')}
    $result=New-Object 'System.Collections.Generic.List[object]'
    foreach($spec in @(@('private-runtime-data-path',($runtime+'/RimWorldWin64_Data')),@('private-save-data-path',($base+'/SaveData').Replace('/','\')),@('private-mod-directory',($runtime+'/Mods').Replace('/','\')),@('private-player-log',($base+'/evidence/Player.log')),@('case-supported','CAP03-C'),@('negative-control-supported','None'),@('installed-version-file-matches-manifest','1.6.4871 rev590'),@('harness-compiled-against-running-game','5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A'))){$result.Add([pscustomobject]@{id=$spec[0];observed=$spec[1]})}
    $map=Get-ABOne $Value.assertions id real-map-initialized $Problems
    if($null-ne$map){if($map.observed-cnotmatch'^size=\(([1-9][0-9]*), 1, ([1-9][0-9]*)\); maps=([1-9][0-9]*)$'){$Problems.Add('CAP03-C actual initialized map dimensions/count malformed.')}else{$sx=0;$sz=0;$maps=0;$ax=$Matches[1];$az=$Matches[2];$am=$Matches[3];if(-not[int]::TryParse($ax,[ref]$sx) -or -not[int]::TryParse($az,[ref]$sz) -or -not[int]::TryParse($am,[ref]$maps) -or $maps-ne$Value.storageProjectionWrappers.gameMaps){$Problems.Add('CAP03-C map census/dimension exceeds native range or differs.')}};$result.Add([pscustomobject]@{id='real-map-initialized';observed=$map.observed})}
    $result.Add([pscustomobject]@{id='exact-active-mod-count';observed='actual=6; expected=6'})
    $mods=@(@('brrainz.harmony','Mods/Harmony'),@('ludeon.rimworld','Data/Core'),@('adaptive.storage.framework','Mods/AdaptiveStorageFramework'),@('sbz.neatstorage','Mods/NeatStorage'),@('giwaffed.haulersdream','Mods/HaulersDream'),@('giwaffed.haulersdream.runtimeharness','Mods/HaulersDreamRuntimeHarness'))
    if($Value.mods.Count-ne6){$Problems.Add('CAP03-C requires exactly six active mods.')}
    for($i=0;$i-lt6;$i++){$mod=$mods[$i];$root=($runtime+'/'+$mod[1]).Replace('/','\');if($i-ge$Value.mods.Count -or $Value.mods[$i].packageId-cne$mod[0] -or $Value.mods[$i].rootPath-cne$root){$Problems.Add('CAP03-C actual package/root order differs.')};$result.Add([pscustomobject]@{id='mod-order-root-'+$i;observed=$mod[0]+' @ '+$root})}
    $catalog=Get-AWAssemblyCatalog
    if($Value.assemblies.Count-ne$catalog.Count){$Problems.Add('CAP03-C loaded assembly catalog differs from the eighteen reviewed files.')}
    for($i=0;$i-lt$catalog.Count;$i++){
        $spec=$catalog[$i].Split('|');$row=Get-ABOne $Value.assemblies name $spec[0] $Problems;$expected=($runtime+'/'+$spec[4]).Replace('/','\')
        if($null-eq$row -or $row.path-cne$expected -or $row.assemblyVersion-cne$spec[1] -or $row.moduleVersionId-cne$spec[2] -or $row.sha256-cne$spec[3] -or $Value.assemblies[$i].name-cne$spec[0]){$Problems.Add('CAP03-C exact loaded file/module/order differs: '+$spec[0])}
        $result.Add([pscustomobject]@{id='single-assembly-'+$spec[0];observed='count=1'})
        $result.Add([pscustomobject]@{id='assembly-identity-'+$spec[0];observed=$expected+'; version='+$spec[1]+'; sha256='+$spec[3]})
    }
    $ticks=Get-ABOne $Value.assertions id real-game-ticks-advanced $Problems
    if($null-ne$ticks){$elapsed=0;if($ticks.observed-cnotmatch'^elapsedTicks=([0-9]+)$' -or -not[int]::TryParse($Matches[1],[ref]$elapsed) -or $elapsed-lt5 -or $elapsed-ge$Value.storageProjectionWrappers.startedTick){$Problems.Add('CAP03-C actual ready tick delay is invalid.')};$result.Add([pscustomobject]@{id='real-game-ticks-advanced';observed=$ticks.observed})}
    $result.Add([pscustomobject]@{id='no-unity-errors-after-harness-start';observed='observedErrors=0; threaded capture through terminal-result boundary'})
    return $result.ToArray()
}
function Test-AWIdentity($Value,$Problems){
    $p=$Value.storageProjectionWrappers;$expected=Get-AWGlobalAssertions $Value $Problems
    if($Value.assertions.Count-ne$expected.Count){$Problems.Add('CAP03-C global assertion catalog contains missing/duplicate/extra entries.')}
    for($i=0;$i-lt$expected.Count;$i++){$want=$expected[$i];$a=Get-ABOne $Value.assertions id $want.id $Problems;if($null-eq$a -or -not$a.passed -or $a.observed-cne$want.observed -or $Value.assertions[$i].id-cne$want.id){$Problems.Add('CAP03-C global assertion outcome/value/order differs: '+$want.id)}}
    $bound=@('HaulersDream','HaulersDream.Core','Assembly-CSharp','0Harmony','HaulersDream.RuntimeHarness','AdaptiveStorageFramework')
    if($p.assemblies.Count-ne6){$Problems.Add('CAP03-C binding assembly census must have six exact modules.')}
    for($i=0;$i-lt6;$i++){
        $a=Get-ABOne $Value.assemblies name $bound[$i] $Problems;if($null-eq$a -or $i-ge$p.assemblies.Count){continue};$row=$p.assemblies[$i]
        if($row.name-cne($a.name+', Version='+$a.assemblyVersion+', Culture=neutral, PublicKeyToken=null') -or $row.mvid-cne$a.moduleVersionId -or $row.path-cne$a.path -or $row.sha256-cne$a.sha256){$Problems.Add('CAP03-C bound module does not match its actual loaded file: '+$bound[$i])}
    }
    if(-not(Test-ABSame $p.bindings (Get-AWBindingCatalog)) -or -not(Test-ABSame $p.patchInventory (Get-AWPatchCatalog))){$Problems.Add('CAP03-C exact native/ASF/HD metadata binding or five-patch owner/kind/module/token catalog changed.')}
    if($p.nativeIdentity-cne'1.6.4871 rev591;Assembly-CSharp, Version=1.6.9676.17735, Culture=neutral, PublicKeyToken=null;mvid=61e41735-6189-4da4-9d21-0260257b5097' -or $p.asfIdentity-cne'AdaptiveStorageFramework, Version=1.2.4.0, Culture=neutral, PublicKeyToken=null;mvid=d7c605b3-e59a-4b26-af97-594bc5417053'){$Problems.Add('CAP03-C native/provider identity provenance differs.')}
    $inputSpecs=@(@('giwaffed.haulersdream.runtimeharness','Defs/Cap03WrapperDefs.xml','C6039286A8D9070DB9FC18AD7F7A7CFC4CA5ED9D3E99AB81A9BAB1780D85C4CE'),@('adaptive.storage.framework','Defs/ThingDefBase.xml','DECE4A55D724F4D1EE23B6F21C531BB4F5EF627EC93E4A4D02C7565FE73A242B'),@('sbz.neatstorage','1.6/Defs/ThingDefs_Buildings/Buildings_CrateAndPallet.xml','9B9F757E13A16327250B5F62BA15DC457F40F4B45DD7500CA9DEA0F56BC4D582'),@('ludeon.rimworld','Defs/ThingDefs_Buildings/Buildings_Furniture.xml','CEA362CA9451F0762F8A104B2344BD540B5F6E8663DD9A4E75F3C39B46607C55'),@('ludeon.rimworld','Defs/ThingDefs_Items/Items_Unfinished.xml','E095396DAD8A83421FF04E47010701CF56DF67E4E00C0D25C1704A51FB8E3130'))
    if($p.inputs.Count-ne5){$Problems.Add('CAP03-C requires all five exact harness/native/ASF/Neat definition inputs.')}
    for($i=0;$i-lt5;$i++){
        if($i-ge$p.inputs.Count){break};$spec=$inputSpecs[$i];$row=$p.inputs[$i];$mod=Get-ABOne $Value.mods packageId $spec[0] $Problems
        if($row.packageId-cne$spec[0] -or $row.relativePath-cne$spec[1] -or $row.sha256-cne$spec[2] -or $null-eq$mod -or $row.root-cne$mod.rootPath){$Problems.Add('CAP03-C input identity/order does not match the loaded private package.')}
    }
}
function Test-AWEventsCore($Value,$Events,[string]$RunId,$Problems){
    if(-not(Test-AWShapeCore $Value $Problems)){return}
    if($Events-isnot[array] -or $Events.Count-eq0){$Problems.Add('CAP03-C raw event array is absent.');return}
    $p=$Value.storageProjectionWrappers;$before=$Problems.Count;$lastTick=-1;$lastUtc=[datetimeoffset]::MinValue;$i=0
    foreach($row in $Events){
        $i++;$shapeStart=$Problems.Count;Test-AWNode $row Event ('event '+$i) (Get-AWSchemas) $Problems;if($Problems.Count-ne$shapeStart){continue}
        $utc=[datetimeoffset]$row.utc
        if($row.sequence-ne$i -or $row.caseId-cne'CAP03-C' -or $row.runId-cne$RunId -or $row.runId-cne$Value.runId -or $row.tick-lt$lastTick -or $row.tick-lt-1 -or $utc-lt$lastUtc -or $utc-lt[datetimeoffset]$Value.startedUtc){$Problems.Add('CAP03-C raw event sequence/tick/UTC/run identity is not continuous.')}
        $lastTick=$row.tick;$lastUtc=$utc
    }
    if($Problems.Count-ne$before){return}
    $plan=@(Get-AWRecordPlan $p $Problems);$component=@($Events|Where-Object {$_.phase.StartsWith('cap03-c-') -and $_.phase-cne'cap03-c-result'})
    if($component.Count-ne$plan.Count -or $component.Count-ne$p.records.Count){$Problems.Add('CAP03-C raw/retained component catalogs have missing, repeated or extra rows.')}
    for($i=0;$i-lt[math]::Min($component.Count,$plan.Count);$i++){
        $event=$component[$i];$record=ConvertFrom-Json -InputObject $event.detail;$expected=$plan[$i]
        $shapeStart=$Problems.Count;Test-AWNode $record Record ('raw record '+$i) (Get-AWSchemas) $Problems;if($Problems.Count-ne$shapeStart){continue}
        if($record.sequence-ne($i+1) -or $record.tick-ne$p.startedTick -or $event.tick-ne$record.tick -or $event.phase-cne('cap03-c-'+$expected.kind) -or $record.kind-cne$expected.kind -or $record.trial-cne$expected.trial -or $i-ge$p.records.Count -or -not(Test-ABSame $record $p.records[$i])){$Problems.Add('CAP03-C raw component record does not reconcile with its retained identity/catalog.')}
        $payload=ConvertFrom-Json -InputObject $record.data
        if(-not(Test-ABSame $payload $expected.data)){$Problems.Add('CAP03-C raw '+$record.kind+' payload differs from its typed result/source-derived expectation.')}
        if($i-gt0 -and $event.sequence-ne($component[$i-1].sequence+1)){$Problems.Add('CAP03-C synchronous component observations are not contiguous.')}
    }
    $terminal=Get-ABOne $Events phase cap03-c-result $Problems
    if($null-ne$terminal){$data=ConvertFrom-Json -InputObject $terminal.detail;if(-not(Test-ABSame $data $p) -or $terminal.tick-ne$p.finishedTick -or $component.Count-eq0 -or $terminal.sequence-ne($component[-1].sequence+1)){$Problems.Add('CAP03-C terminal component payload/boundary differs from the actual nested result.') }}
    $start=Get-ABOne $Events phase harness-start $Problems;$version=Get-ABOne $Events phase game-version-provenance $Problems;$new=Get-ABOne $Events phase new-game $Problems;$map=Get-ABOne $Events phase map-initialized $Problems
    $scenario=Get-ABOne $Events phase scenario-observed $Problems;$capture=Get-ABOne $Events phase error-capture-boundary $Problems;$end=Get-ABOne $Events phase terminal-result $Problems
    $globals=@($Events|Where-Object phase -CEQ assertion);$expectedGlobal=@(Get-AWGlobalAssertions $Value $Problems)
    if($globals.Count-ne$expectedGlobal.Count){$Problems.Add('CAP03-C complete global assertion event catalog differs.')}
    for($i=0;$i-lt[math]::Min($globals.Count,$expectedGlobal.Count);$i++){
        $expected=$expectedGlobal[$i];$observed=$expected.observed
        # Finish rewrites the retained clean assertion with the final closed-capture count without emitting another assertion.
        if($expected.id-ceq'no-unity-errors-after-harness-start'){$observed='observedErrors=0'}
        if($globals[$i].detail-cne($expected.id+': passed; '+$observed)){$Problems.Add('CAP03-C raw global assertion outcome/detail/order differs: '+$expected.id)}
    }
    if($null-eq$start -or $null-eq$version -or $null-eq$new -or $null-eq$map -or $null-eq$terminal -or $null-eq$scenario -or $null-eq$capture -or $null-eq$end -or $component.Count-eq0 -or $globals.Count-ne$expectedGlobal.Count){return}
    $ticks=Get-ABOne $Value.assertions id real-game-ticks-advanced $Problems;$elapsed=0
    if($null-ne$ticks -and $ticks.observed-cmatch'^elapsedTicks=([0-9]+)$'){[int]::TryParse($Matches[1],[ref]$elapsed)|Out-Null}
    $mapAssertion=Get-ABOne $Value.assertions id real-map-initialized $Problems;$size=''
    if($null-ne$mapAssertion -and $mapAssertion.observed-cmatch'^size=(.*); maps=[0-9]+$'){$size=$Matches[1]}
    if($start.detail-cne'case=CAP03-C; expectedBehavior=satisfied' -or $start.sequence-ne1 -or $start.tick-ne-1 -or $version.tick-ne-1 -or $version.detail-cne('Version.txt='+$Value.installedVersionFile+'; executing assembly reports='+$Value.executingGameVersion) -or $new.tick-ne0 -or $new.detail-cne'GameComponent lifecycle callback received.' -or $map.tick-lt1 -or $map.detail-cne('tick='+$map.tick+'; size='+$size) -or $p.startedTick-$map.tick-ne$elapsed){$Problems.Add('CAP03-C startup/native-version/new-map/ready-tick provenance differs.')}
    # Initialize is synchronous before new-game. Update's map assertion/environment capture and
    # map-initialized event are synchronous after assigning firstTick; monotone order alone cannot join them.
    for($i=0;$i-lt8;$i++){if($globals[$i].tick-ne$start.tick){$Problems.Add('CAP03-C initial global assertion tick differs from synchronous startup: '+$expectedGlobal[$i].id)}}
    for($i=8;$i-lt$globals.Count-2;$i++){if($globals[$i].tick-ne$map.tick){$Problems.Add('CAP03-C setup global assertion tick differs from synchronous map initialization: '+$expectedGlobal[$i].id)}}
    # Bootstrap emits the first six assertions, version provenance, then the two version checks, new-game,
    # map/mod/assembly checks, map initialized, ready ticks, the synchronous component, and the final clean assertion.
    $outer=New-Object 'System.Collections.Generic.List[object]';$outer.Add($start)
    foreach($row in $globals[0..5]){$outer.Add($row)};$outer.Add($version)
    foreach($row in $globals[6..7]){$outer.Add($row)};$outer.Add($new)
    for($i=8;$i-lt$globals.Count-2;$i++){$outer.Add($globals[$i])}
    $outer.Add($map);$outer.Add($globals[-2]);foreach($row in $component){$outer.Add($row)};$outer.Add($terminal)
    $outer.Add($globals[-1]);$outer.Add($scenario);$outer.Add($capture);$outer.Add($end)
    if($outer.Count-ne$Events.Count){$Problems.Add('CAP03-C outer lifecycle has unexpected/error/extra phases.')}
    for($i=0;$i-lt$outer.Count;$i++){if($outer[$i].sequence-ne$i+1){$Problems.Add('CAP03-C outer lifecycle/global setup/component/closure chronology is out of order.');break}}
    if($globals[-2].tick-ne$p.startedTick -or $globals[-1].tick-ne$p.finishedTick -or $scenario.tick-ne$p.finishedTick -or $capture.tick-ne$p.finishedTick -or $end.tick-ne$p.finishedTick -or $scenario.detail-cne'case=CAP03-C; expected=satisfied; requestedBehaviorSatisfied=True; expectationMatched=True' -or $capture.detail-cne'Threaded capture closed before terminal result; earlier startup and shutdown require whole Player.log review.' -or $end.detail-cne($Value.status+': '+$Value.detail) -or $end.sequence-ne$Events.Count -or [datetimeoffset]$Value.finishedUtc-lt[datetimeoffset]$scenario.utc -or [datetimeoffset]$Value.finishedUtc-gt[datetimeoffset]$capture.utc){$Problems.Add('CAP03-C result/scenario/error-closure/last-terminal ordering or error reconciliation is contradictory.')}
}

function Test-AsfWrapperShape($Value,$Problems){
    $old=$ErrorActionPreference;$ErrorActionPreference='Stop'
    try{return Test-AWShapeCore $Value $Problems}catch{$Problems.Add('CAP03-C malformed evidence shape: '+$_.Exception.Message);return $false}finally{$ErrorActionPreference=$old}
}
function Test-AsfWrapperEvidence($Value,[string]$ExpectedBehavior,$Problems){
    $old=$ErrorActionPreference;$ErrorActionPreference='Stop'
    try{Test-AWECore $Value $ExpectedBehavior $Problems|Out-Null}catch{$Problems.Add('CAP03-C malformed/inconsistent evidence: '+$_.Exception.Message)}finally{$ErrorActionPreference=$old}
}
function Test-AsfWrapperEvents($Value,$Events,[string]$RunId,$Problems){
    $old=$ErrorActionPreference;$ErrorActionPreference='Stop'
    try{Test-AWEventsCore $Value $Events $RunId $Problems|Out-Null}catch{$Problems.Add('CAP03-C malformed/inconsistent raw evidence: '+$_.Exception.Message)}finally{$ErrorActionPreference=$old}
}
