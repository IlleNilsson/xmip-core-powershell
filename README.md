# xmip-core-powershell

The PowerShell operator surface: cmdlets and objects over the Xmip ABI, a live
`Xmip:` provider, and an operational summary in the interactive prompt.

**Not a subprocess.** An earlier design had this module driving the `xmip`
executable and shaping its JSON into objects; ADR-0014 replaced it. Scraping
JSON out of a subprocess is how BizTalk's console and its PowerShell provider
drifted apart — the two surfaces called different code and could answer the
same question differently. Here every surface calls the same ABI, so drift of
that kind has nothing to grow from.

PowerShell Core runs on .NET, so this module shares its binding assembly with
the `cli` and `gui` surfaces: the binding is `Xmip.Abi` in xmip-core-abi
(`dotnet/Xmip.Abi`), referenced as a project, and nothing in this repository
declares a struct of the header's own. What it adds is the PowerShell shape:
cmdlets with approved verbs, objects on the pipeline rather than text, and
`-WhatIf` on anything that changes the estate.

## State

Scaffolded, which is what `architecture.toml` says: `maturity = "scaffolded"`.
Three cmdlets describe the binding — `Get-XmipAbi`, `ConvertFrom-XmipStatus` and
`Get-XmipModuleDescriptor` — and two reach a running runtime through the
operator boundary in `xmip_operate.h` (ADR-0027): `Get-XmipHealth -Library
-Scope` and `Test-XmipNodeConfiguration -Library -Path`. `tests/` holds the
Pester tests over them.

When the module is imported into an interactive shell it creates the `Xmip:`
drive and prepends a compact, colored segment such as
`[R12 P10 S9 T1 F0]` to the prompt. The five independently colored letters
mean Received, Processed, Sent, Retrying and Failed
already installed. It composes with posh-git and other prompt providers rather
than replacing their result. A background observer follows the shared
`Xmip.Surface` change stream and updates an in-memory segment; the prompt
itself performs no runtime call, file read, subprocess, wait, or poll.
`XMIP_RUNTIME_LIBRARY` selects the native runtime by the estate-wide rule.
For a published file instead, set `XMIP_SNAPSHOT`.

Navigate the same live scope tree with `Get-ChildItem Xmip:\`. Every returned
object carries health plus the five activity figures. `Set-Item` applies an
operation to a scope, for example `Set-Item Xmip:\edge-01\receive\orders
-Value Paused`; the matching `Suspend-XmipScope`, `Resume-XmipScope`,
`Start-XmipScope`, `Stop-XmipScope`, and `Restart-XmipScope` commands accept
either provider paths or Xmip scope URIs and support `-WhatIf`. The current
runtime implements pause/resume; start/stop/restart remain explicit unsupported
operations until runtime host-service lifecycle is implemented.

Seven of those compare this assembly against `xmip_module.h` itself: every
status the header defines, the name each one takes, and which are retryable and
which terminal. ADR-0012 clause 1 makes the header normative and the C# here a
convenience over it, and a comment saying so cannot enforce it. Changing one
enum value fails four tests.

The decisions that shape this are ADR-0014 (the operator surfaces) and ADR-0012
(the module boundary); the ABI it calls is `xmip-core-abi`.
