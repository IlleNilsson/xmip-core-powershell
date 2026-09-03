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

Scaffolded, which is what `architecture.toml` says: `maturity = "scaffolded"`.
Three cmdlets answer over the ABI — `Get-XmipAbi`, `ConvertFrom-XmipStatus` and
`Get-XmipModuleDescriptor` — and `tests/` holds eighteen Pester tests over them.

Seven of those compare this assembly against `xmip_module.h` itself: every
status the header defines, the name each one takes, and which are retryable and
which terminal. ADR-0012 clause 1 makes the header normative and the C# here a
convenience over it, and a comment saying so cannot enforce it. Changing one
enum value fails four tests.

The decisions that shape this are ADR-0014 (the operator surfaces) and ADR-0012
(the module boundary); the ABI it calls is `xmip-core-abi`.
