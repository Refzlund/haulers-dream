# Disclaimer — how this mod is made

This mod is largely implemented by **Fable 5** and **Opus 4.8** (Anthropic's Claude models), with
relatively few direct interventions from a human developer (me). For many users this is an important
distinction, so it is stated plainly here.

All code is human-reviewed by me, is fully available in the
[GitHub repository](https://github.com/Refzlund/haulers-dream), and every update ships through a Pull
Request — so the complete history and every change are open to inspection.

Hauler's Dream covers a great many features, with the deliberate goal of intertwining them into a
single, unified system backed by optimized algorithms that improve colonist efficiency. Compatibility
with other mods is built by cloning those mods and actively studying their architecture, then
incorporating dedicated compatibility layers into Hauler's Dream.

For reference, a mod of this scope would otherwise take many months — if not years — of manual work;
the accelerated development is what makes it feasible. The whole thing is held together by a large
headless test suite and a repo-wide performance pass (the per-tick logic is allocation-tested), so all
of those features stay light and don't stutter even in big, heavily-modded colonies.
