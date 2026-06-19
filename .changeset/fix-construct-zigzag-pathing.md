---
"haulers-dream": patch
---

fix: builders no longer zig-zag across a wall/fence line when delivering construction materials in one inventory trip. A multi-site delivery now drives to the **nearest remaining build site from where the pawn is standing** on each hop (a greedy nearest-neighbour route), instead of following the queue's fixed distance-from-a-single-anchor order — which sent the pawn concentrically around the first-filled site in an alternating-sides pattern, turning short walks into long back-and-forth trips. Single-site deliveries are byte-identical; vanilla's own hand-carry batching is unchanged.
