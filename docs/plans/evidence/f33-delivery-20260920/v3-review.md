# F33 v3 arena correction review

**Accepted for the next candidate run; no concrete source/build blocker.** Independent review by `f45_build_recipe`, 2026-09-20, limited to the final fixture delta from retained `DeliveryIntent.v2.cs.txt` and actual host build. No product or execution input was edited by this review; no native process was launched.

The 80 owned granite walls form the complete perimeter of the same 21×21 cleared arena. The center site, stock, actor start and shape probe nine tiles from center all remain inside. Native construction/delivery, damage, threat responses and ordinary follow-up remain active. All four outcome expectations and observation bounds are unchanged.

`ParkMapPawns` retains the existing WorldPawns lease and records each actual identity/position/faction. Calling it again after native destruction/perimeter creation handles newly released pawns; the post-clear guard rejects remaining spawned pawns, arena fire or hostile things. Owned walls enter the existing cleanup list and are destroyed before original pawn placement is restored. There is no post-failure healing, invulnerability, suppressed AI or teleporting to manufacture a pass. Early downed/dead failure and actual target/health details improve diagnosis if isolation proves insufficient.

The current fixture SHA is `7326AC196C265843047A67B525E60B4FF3E8CFC6855BE8D3F3B775F88FCBD71D`. All three selected host source/project hashes match their build record; Bootstrap/project are unchanged. The complete host-v3 log reports zero warnings and zero errors. Actual DLL/PDB hashes match the record:

- DLL: `CF0A7046B77F3FACDD3B73C87720FBE7A7D64105BD14F728D7660BF32F784C2B`.
- PDB: `19C3C377E02D143B6E18598770CB896BCA6C91033A19211AC252341262C0BB93`.
- Actual reflection-only MVID: `fcb5a8c5-a26e-4595-99ec-7f11ae4c8fd2`.

Product HD `B6F29DE2` and Core `2098CFCE` remain byte-identical to accepted v2. Retained v2 fixture matches `0687B458`; its partial run `c9d2...` remains failed, with native PID 25836 joined exit 0 and Verify reporting no protected changes. The baseline reproduction also remains unchanged.

The next run must still demonstrate all four outcomes, including normal autonomous work and absence of forced HaulOnly continuation. Arena isolation is a fixture correction, not product acceptance or proof that the unidentified prior danger was a particular object type.
