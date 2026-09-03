# xmip-core-powershell

The PowerShell operator surface: cmdlets and objects over the Xmip ABI.

**Not a subprocess.** An earlier design had this module driving the `xmip`
executable and shaping its JSON into objects; ADR-0014 replaced it. Scraping
JSON out of a subprocess is how BizTalk's console and its PowerShell provider
drifted apart — the two surfaces called different code and could answer the
same question differently. Here every surface calls the same ABI, so drift of
that kind has nothing to grow from.

PowerShell Core runs on .NET, so this module shares its binding assembly with
the `cli` and `gui` surfaces. What it adds is the PowerShell shape: cmdlets
with approved verbs, objects on the pipeline rather than text, and `-WhatIf`
on anything that changes the estate.

## State

Declared, empty, and honestly so: `maturity = "planned"` in
`architecture.toml`. The decisions that shape it are ADR-0014 (the operator
surfaces) and ADR-0012 (the module boundary); the ABI it will call is
`xmip-core-abi`.
