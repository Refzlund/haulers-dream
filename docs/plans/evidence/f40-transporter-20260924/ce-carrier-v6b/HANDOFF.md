# F40 CE v6b: exact original producer host

Ready for root review; no Prepare/native here. V6's source/build and its failed Prepare remain unchanged. My v6 handoff claim about predecessor-host equality was wrong: I missed the later original-image loop. Root's refusal correctly caught it.

[source.diff](source.diff) contains only the controller's original-producer comparison and contract correction. That comparison now permits the **exact** retained producer56b55 with host SHA `0EDB20F2EE1BCC68B1C1997F1BDF7640E2C926E2FAD87CB729B4422247B48E80` / MVID `514d25c8-4f75-4fd2-aad7-4fb21886085d`. It still hashes that retained host, compares the actual producer result and manifest, and validates all ten other images unchanged. Normal consumer admission and prepared manifest still use v6 host `94BC2F22…` / MVID `870c1793-233d-475c-a58b-c4d7cc83a447`.

[controller-validation-audit.json](controller-validation-audit.json) executes the exact extracted image loop read-only: all eleven actual original images pass; a different producer, wrong host SHA/MVID, wrong product or CE SHA, and substituting the consumer host all fail. Parser/current-consumer binding checks also pass (9/9). No fixture/product rebuild: exact v6 compiled scene and host reused.

Use this folder's controller/launch/selection for **F40-CE-RESTART**, same original56b55 tick194 save5D3D44B8… and original checkpoint.txt, short TEMP/TMP `C:/HDQA/runtime-temp`. Selection SHA `05A1113C6FB4A8B4D981D452A149F5791EE37C14495C131CF772F542B180B28B`; diff SHA `323B9E06B8A30730B67C3CD1EC7C68DFD21CFC019E021FE7B3EF9FD1FDDEFD81`. Product/Core/CE and all v6 physical/job/save oracles unchanged. The original failed consumer remains47/49; no native success is claimed.
