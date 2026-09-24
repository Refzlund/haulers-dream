# Shared fixture source-path correction

Use `controller-v3` for the same three shared-provider profiles. No executable script, action contract, source fixture, DLL/PDB, roster, product or private runtime relative path changed from `controller-v2`. This is a data-path-only successor; the original inputs and failed run `ffe2be20315f4e658e1f98c8f39e6f7b` remain intact.

That run's native log reports admission failure while hashing a 278-character TDFindLib CookieCutter source path. Both versioned files exist and match their admitted SHA/size, supporting a native path-length limitation. The full 163-file TDFindLib package was copied to `C:/Users/Arthur/AppData/Local/Temp/hd-f45-tdfind-20260920`; all corresponding sizes/hashes and full tree census match, with originals rechecked. `short-dependency-copy.json` contains every source/copy pair. The copied package's longest path is 195 characters; all new input pins are at most 239. Existing runtime relative paths remain unchanged (failed run maximum was 255).

Frozen new source manifest: `A9C6B9FA985C3B223A56C82F72616705DFCA96969121855D7350D51F07474656`; selection: `A073F5558834E562AAD45718EA175065C28FE2325BF4AB7B8D664E041462E1E2`. Reuse standard `controller-v3/run.py Prepare --profile candidate-original-ego-cl --review <actual review> --review-sha256 <actual hash>`, then root-controlled Launch/Verify after inspecting actual results. The other unchanged profile names are `candidate-all-pb-first` and `candidate-all-it-first`.

No runtime operation was performed by this path-correction task. Native admission success and all behavioral outcomes remain to be observed; byte-identical relocation alone does not claim them.
