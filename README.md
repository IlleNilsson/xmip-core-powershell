# xmip-core-powershell

The PowerShell operator surface: cmdlets and objects over the Xmip ABI, plus
a live Xmip status segment in the interactive prompt. A surface module
(ADR-0011, ADR-0012 clause 11) among the operator surfaces ADR-0014 names.

**Not a subprocess.** An earlier design had this module driving the `xmip`
executable and shaping its JSON into objects; ADR-0014 replaced it, and its
amendment of 2026-08-26 says why one definition of what an operator can do,
held at the ABI, is what keeps two surfaces from drifting apart. Here every
surface calls the same ABI, so drift of that kind has nothing to grow from.

PowerShell Core runs on .NET, so this module shares its binding assembly with
the `cli` and `gui` surfaces: the binding is `Xmip.Abi` in xmip-core-abi
(`dotnet/Xmip.Abi`), referenced as a project, and nothing in this repository
declares a struct of the header's own. What it adds is the PowerShell shape:
cmdlets with approved verbs, objects on the pipeline rather than text, and
`-WhatIf` on anything that changes the estate.

## State

Scaffolded, which is what `architecture.toml` says: `maturity = "scaffolded"`.
Three cmdlets describe the binding — `Get-XmipAbi`, `ConvertFrom-XmipStatus` and
`Get-XmipModuleDescriptor` — and four reach a running runtime through the
operator boundary in `xmip_operate.h` (ADR-0027): `Get-XmipHealth -Library
-Scope`, `Test-XmipNodeConfiguration -Library -Path`, and the two acts the
boundary carries, `Suspend-XmipScope -Library -Scope [-Who]` and
`Resume-XmipScope -Library -Scope`, both with `-WhatIf`, both emitting the
`ScopeOperation` the `xmip` executable renders. There is no Start-, Stop- or
Restart-XmipScope: the boundary has no such call, because the thing that
watches must not be able to stop the thing it watches. `tests/` holds the
Pester tests over them.

The public contract is the cmdlet names and the shape of the objects they
emit. Both are pre-alpha and unstable, like the cli's output — but objects, so
a caller filters and compares rather than parsing.

## The prompt

When the module is imported into an interactive shell it prepends a compact,
colored segment such as `[Xmip fine]` or `[Xmip holding]` to the prompt
already installed. It composes with posh-git and other prompt providers rather
than replacing their result, and restores the prompt it found when the module
is removed. A background observer follows the shared `Xmip.Surface` change
stream and updates an in-memory segment; the prompt itself performs no runtime
call, file read, subprocess, wait, or poll. Which surface it follows is stated
in `xmip.powershell.toml` beside the module, with the same `[Xmip]` keys as
the GUI hosts and the executable — `Surface = "native" | "snapshot"`,
`RuntimeLibrary`, `Snapshot` — and never guessed (ADR-0052 clause 3). With no
surface named, the prompt follows whatever library the one discovery rule
finds (`RuntimeLibrary`, else `XMIP_RUNTIME_LIBRARY`, else beside the module)
and says `[Xmip not configured]` while nothing answers. The mood's color is
the one `Xmip.Surface` names for it (ADR-0041); the console paints the nearest
of its sixteen.

## Runtime

Compiled by the .NET 11 preview SDK, targeting net10.0 — ADR-0014's amendment
of 2026-08-30. The SDK is the estate's; the target framework belongs to
whoever loads the assembly, and this one loads into pwsh 7.6.5, which hosts
.NET 10. Proven rather than assumed: the net11.0 build was refused by
`Import-Module` on the platform shell. Follows pwsh upward when pwsh moves.

## Dependencies

`Xmip.Abi` and `Xmip.Surface` from xmip-core-abi, referenced by project path
inside the composed estate (ADR-0014, amendment of 2026-09-09), and nothing
else. No Xmip Rust crate is referenced, and none may be — the same test the
cli states: that this project compiles without a single Xmip source file is
the proof the boundary works (ADR-0012 clause 2).

## Not this repository's

- Not the estate tooling. `Xmip/Xmip.psd1` in the platform repository lands
  commits and reconciles repositories; this operates a running Xmip. They
  share a prefix and nothing else, and their commands must not collide.
- Not a runtime, and holds no execution state.
- Not a competing prompt framework: it preserves and invokes the prompt that
  was installed before it.
- Not a drive. A PowerShell provider over the scope tree was proposed on
  2026-09-12 and declined on 2026-09-14 (ADR-0052): no record asks for one,
  and a provider is the drifting surface ADR-0014 warns against. Objects on
  the pipeline are the PowerShell shape here.

## Verification

`dotnet build`, then `Invoke-Pester -Path ./tests`. Sixteen tests, all of
them the PowerShell shape: the manifest and the exports agree, every verb is
approved, the two acts carry `-WhatIf` and no start, stop or restart exists,
the cmdlets emit objects, the configuration document ships beside the module,
and the prompt paints the color `Xmip.Surface` names for a mood. The binding's
agreement with `xmip_module.h` — every status the header defines, its name,
which are retryable and which terminal — is tested once, in `Xmip.Abi.Tests`
beside the binding (ADR-0014, amendment of 2026-09-09); the copy this
repository carried until 2026-09-14 tested the same assembly against the same
header and is gone.

`Get-XmipModuleDescriptor` against a conforming module is the same first
conformance rule the cli's probe exercises.

**The suite builds into a temporary directory and imports from there.** A
loaded binary module locks its own assembly: on 2026-09-03 a `dotnet build`
here failed ten retries because a pwsh session opened the day before still held
`bin/Debug/net10.0/Xmip.PowerShell.dll`. Testing out of `bin/` would make every
test run do that to the next build. The repository's own output is never
loaded.

The decisions that shape this are ADR-0014 (the operator surfaces), ADR-0027
(the operator boundary), ADR-0052 (one model every surface renders) and
ADR-0012 (the module boundary); the ABI it calls is `xmip-core-abi`. Until
2026-09-14 an `ARCHITECTURE.md` beside this file said the same things a
second time; ADR-0020 clause 1 is one document per subject, and this is it.
