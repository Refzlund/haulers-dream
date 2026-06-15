---
"haulers-dream": minor
---

**New "Batch" bill mode — make a whole batch of a bill in one work session, with a single ingredient trip.**

Crafting and cooking bills now have three extra options in the repeat-mode dropdown, next to vanilla's "Do X times / Do until X / Do forever":

- **Batch: do X times**
- **Batch: do until X**
- **Batch: do forever**

When a bill is set to batch, the colonist fetches enough ingredients for the whole batch in **one trip**, makes them all at the bench one after another, then hauls everything to storage in one go — exactly the "plan prioritized crafting" flow, but automatic and per-bill. Because each item finishes individually, an interruption (drafting, power/fuel loss) only ever loses the single in-progress item, never the whole batch. If the bill's own count is reached partway through a batch (e.g. "Batch 10, until 40" when you're already at 35), only the remaining 5 are made and any unused ingredients are carried back to storage with the products.

**Food doesn't spoil while the colonist is working.** Raw ingredients carried for the batch are frozen for the duration of the bench work, then resume spoiling normally while walking to and from the bench — so a big cooking batch won't rot the ingredients mid-session.

**Setting the batch size.** Pick "Batch size: N…" from the same dropdown to set a per-bill amount with a slider. A new mod setting, **"Batch new bills by default"** (off by default), makes every newly-added batchable bill start in batch mode at a configurable **default batch size**, so you don't have to set it each time.

Applies to ordinary production bills (cooking, tailoring, simple crafting, etc.). Recipes that build an "unfinished thing" — sculpting, complex components, advanced weapons/armour — are not batched, because they already keep their progress across interruptions in vanilla.
