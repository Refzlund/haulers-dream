# F11 v15 consumer ready for independent source review

Separate consumer only; no original v13/v14 selection, source, product or save was changed. Read `DIAGNOSIS.md` and the complete three-file `source.diff`. No Prepare, native launch, staging or commit by this author.

- Host: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f11-quantity-lifecycle-20260924-v1/host-build-v15b/Assemblies/HaulersDream.RuntimeHarness.dll`
- SHA-256: `1BCAE974CBBB24ED4666C7317B2E3850EB442143D146A296B5AF9E9EAB4553AC`
- MVID: `2d163b9f-b43f-45f7-b13b-d3fe185998f2`
- Local authoritative `selection.json`: `2F1E4EB3149F55E8C805A6CD2C1DD1BCE1539A984280B4DDAF1B73ED1B07EC31`
- Build v15b: 3.03 seconds, zero warnings/errors, exit 0. Reflection-only metadata reader joined exit 0. Prior setup/reader failures are retained explicitly.
- Audit: 1,340 checks passed; 321 preserved inputs/capture files, 89 exact compiled source pairs and 85 actual reference pairs. All 22 PowerShell files parse without errors.

Prepare only `F11-RESAVED`, satisfied, Built, None, using this folder's controller, local selection host and unchanged selected `candidate-runtime-v5`. The exact original predecessor is `13680dad781a4d09811ef322de7ce350`, with native source save `C:/HDQA/runtime-temp/haulersdream-runtime-tests/13680dad781a4d09811ef322de7ce350/SaveData/Saves/F11QuantityCheckpoint.rws` and its original `evidence/checkpoint.txt`. Save SHA is `8C60B105E62BF5A8ECEF04BFE9CC29DB96CAFA30C8FD3CEB5DCB1FCAC7841358`; record SHA is `099A3A3C1A34CBDCBAD3BCCE23EBEEC8E509F615726281429A7301B13AFCDB55`. The original producer host remains `D215F8E88756D22E4C2DA8D242F67E024B504B248EB9D4979FB2AB20A0A0DF86` / `8c032e23-9da7-4dbf-a542-912c96846f78`. Both original HD/Core images are required unchanged, not merely compatible versions.

Use process-local TEMP/TMP `C:/HDQA/runtime-temp`, the explicit protected player-save root from the existing selection context, and the repository private-desktop wrapper around this folder's `launch.ps1`. The consumer does not start an input worker or show a window. Root owns Prepare/native. Do not replace or update the original save, rerun the producer, or use another predecessor.

Expected evidence: actual native LoadedGame at 184 on its load worker; exact untouched source Steel11957×8, forbidden dropped Steel11958×7, WoodLog11940×5, Human11934/11937, original tag/Keep/preferences/jobs; native paused continuation at 185 with those same values; ordinary undraft-induced unload of exactly 5; original Steel11957×3 retained/tagged with Keep 3; exact native unload end Incompletable on the reviewed Keep-only path; all 15 accounted; no second unload, no active/queued unload, no exact-job reservations, 180 stable ticks. Review complete events/logs, actual images, retained XML, cleanup and process/protected-file receipts. Any other terminal or custody change fails rather than being explained away.

Producer UI/cancellation/quantity controls remain the accepted original capture; this host change tests only its saved-remainder continuation. The new consumer still needs a fresh actual native capture and independent review before F11 closure. Optional-provider/MP boundaries remain separately scoped.
