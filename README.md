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
`Get-XmipModuleDescriptor` — and emit what `xmip-cli abi`, `status` and
`probe` render: `AbiBoundaries` (both boundaries), `StatusMeaning`, and the
probe's own `Conforms` and `Complaint`, all from `Xmip.Abi`. Four read a
running Xmip: `Get-XmipHealth -Scope`, the two acts the boundary carries,
`Suspend-XmipScope -Scope [-Who]` and `Resume-XmipScope -Scope`, both with
`-WhatIf` and both emitting the `ScopeOperation` `xmip-cli pause` renders, and
`Test-XmipNodeConfiguration -Path`, which emits the `ConfigurationVerdict`
`xmip-cli validate` renders. There is no Start-, Stop- or Restart-XmipScope:
the boundary has no such call, because the thing that watches must not be able
to stop the thing it watches. `tests/` holds the Pester tests over them.

**The cmdlets read the surface `xmip-cli` reads** (ADR-0052 clause 1). The
three that take a `-Scope` take `-Remote`, `-Snapshot` and `-Library` as the
executable takes `--remote`, `--snapshot` and `--runtime`, in the same order —
a web host, then one cluster's snapshot, then a runtime library, then
`xmip.powershell.toml`, then the runtime rule — because both ask
`SurfaceChoice.Stated` in `Xmip.Surface`. A `-Scope` may be a wildcard over
the scopes that exist, `Get-XmipHealth -Scope 'xmip:///C1/node/R*'`, selected
by the executable's own `ScopeSelection`: each topmost scope it names is
answered in turn and none is rolled up with another, and a pattern that names
nothing is a non-terminating error that says REFUSED, the pattern and what
there is. `Test-XmipNodeConfiguration -Library` finds its runtime the same
way `xmip-cli validate --runtime` does. Until 2026-09-24 every one of them
demanded `-Library`, loaded the binding itself and took a scope literally.

The public contract is the cmdlet names and the shape of the objects they
emit. Both are pre-alpha and unstable, like the cli's output — but objects, so
a caller filters and compares rather than parsing.

## The prompt

When the module is imported into an interactive shell it adds a compact,
colored segment such as `[R:12 P:11 S:10]` to the prompt already
installed, where posh-git puts a repository's state — after the path and
before the closing `>`, as in `D:\Repos\Xmip [main] [R:12 P:11 S:10]>`
(the owner, 2026-09-18) — and says it the same way and no wider: five
figures with their letters — R, P and S for what the three stages count
(Streams received, Journeys in process, Messages sent), T for Retrying and F
for Failed. No mood is spelled out; the color carries it: a stage letter is
green, yellow or red by the worst leaf on that stage, T is yellow and F is
red (the owner, 2026-09-15; ADR-0052). T and F are there only when something
is retrying or has failed, so a quiet cluster reads `[R:12 P:11 S:10]` and
a troubled one `[R:12 P:11 S:10 T:3 F:1]`, T in yellow and F in red. A colon
stands between a letter and its number wherever there is a number (the
owner, 2026-09-18). It wears posh-git's clothes, by the owner's word the same
evening: yellow brackets, the cyan posh-git gives a branch in step with its
remote for a stage that is fine, and posh-git's own `≡` when every stage is
fine and nothing is retrying or failed. It keeps posh-git's order too,
`[main ≡ +0 ~1 -0]` there and `[orders ≡ R:12 P:11 S:10]` here: first what the
prompt is at — the node's name where it follows one node, the cluster's
where it follows a cluster, in the color of the worst stage — then `≡` when
square, then the counts. R, P and S are the stage letters; a cluster or a
node is called whatever its operator called it, and that name says nothing
about what it does. A count is kept short in K, M and G, `R:5.3K`,
`S:1.2M`, because Xmip counts past what an integer holds and a line that
grows with its numbers goes wild. A count written that short hides its own
movement, so its number is painted by which way it is going since the last
publication: orange where it rose, magenta where it rose by a tenth or more,
cooler where it fell, icy where it fell by as much. The letter keeps the
mood's color; a count below a thousand shows its own movement and is not
painted. A stage figure the publisher has not published is a
dash, never a zero. It composes with posh-git and other prompt
providers rather than replacing their result, and restores the prompt it
found when the module is removed. A background observer follows the shared `Xmip.Surface` change
stream and updates an in-memory segment; the prompt itself performs no runtime
call, file read, subprocess, wait, or poll. Which surface it follows is stated
in `xmip.powershell.toml` beside the module, with the same `[Xmip]` keys as
the GUI hosts and the executable — `Surface = "native" | "snapshot" |
"remote"`, `RuntimeLibrary`, `Snapshot`, `Url` — and never guessed (ADR-0052
clause 3). Remote, it follows a web host's surface hub over SignalR and is
told when that host's surface changes (ADR-0052, amendment 2026-09-15), over
TLS: it presents the certificate `Certificate` and `PrivateKey` name and
checks the host's against `TrustAnchor` (else `XMIP_CERTIFICATE`,
`XMIP_PRIVATE_KEY`, `XMIP_TRUST_ANCHOR`), and `-Remote` does the same; plain
http is refused to anything but this machine (ADR-0063 clause 1). With no
surface named, the prompt follows whatever library the one discovery rule
finds (`RuntimeLibrary`, else `XMIP_RUNTIME_LIBRARY`, else beside the module)
and shows nothing while nothing answers, as posh-git shows nothing outside a
repository: that Xmip is connected is obvious where the figures show (the
owner, 2026-09-18). Only a document that names a surface this build does not
know says a word, `[Xmip misconfigured]`. The mood's color is
the one the runtime names for it (`observe::Health::color`, ADR-0041, asked
through `Xmip.Surface`); the console paints the nearest of its sixteen, and the
name takes the color of the worst stage by the runtime's own worst-first
order.

## What it audits

The module audits as program `Xmip.PowerShell` through the audit capability,
reached through the runtime's library (ADR-0062; `ModuleAudit` over
`ProgramAudit` in `Xmip.Surface`). Every cmdlet derives from `XmipCommand`,
which records, once for all of them: every error a cmdlet writes or ends
on, and every exception that leaves it, as a `failure` whose action is the
cmdlet's name, with its bound parameters, the user, the error's identifier,
category and target as properties; and, for the two acts that change the
estate, `Suspend-XmipScope` and `Resume-XmipScope`, each scope's `begin` and
`finished`. The prompt records what it used to swallow as action `prompt`: a
publication it could not read (once until a tick reads again), a document
that names a surface this build does not know, and whatever else ended its
observer. Anything the module leaves unhandled in the session is recorded as
`unhandled` from the import on. Records go to `<AuditDirectory>/audit.toml`,
`AuditDirectory` in `xmip.powershell.toml`'s `[Xmip]` table resolved from
beside the module; unset, the capability decides — `XMIP_AUDIT_DIRECTORY`,
else the operating system's log, which also takes a record the directory
cannot.

## Seeing it

In a fresh pwsh, nothing imported yet, from the estate root:

```powershell
dotnet build module/core/operation/powershell/src/Xmip.PowerShell
Import-Module posh-git
Import-Module ./Xmip/Xmip.psd1
Import-Module ./module/core/operation/powershell/src/Xmip.PowerShell/bin/Debug/net10.0/Xmip.PowerShell.psd1
Start-XmipTest -Suite Playground -Cluster C1 -Test RoundTrip `
    -Nodes alpha, beta, gamma `
    -NodeCapability @{ alpha = 'receive'; beta = 'process'; gamma = 'send' }
```

`C1` and the three node names are arguments, nothing more: name them anything
a file can be called. `C1` is written here only because the shipped
`xmip.powershell.toml` follows that roll's snapshot.

Press Enter after the import and the prompt is as it was, with no segment,
until the roll publishes; a few seconds after the roll starts it says
`[R:12 P:11 S:10]`, in color, and moves on every Enter. `Stop-XmipTest`
ends the roll. Build before you import, never after, in the same session: a
loaded module locks its assemblies, and a build into them fails. For that
reason the import belongs in the session that wants the segment and not in
a console's start-up command line or a profile: a console that always holds
the module always blocks its build, and it is `pwsh`, so
`Get-Process -Name xmip-*` does not find it. posh-git must be loaded for the
segment to sit beside a repository's state; a console started with
`-NoProfile` has skipped the profile that imports it.

A roll started under another name is followed too: `Start-XmipTest` tells
the prompt in its session which snapshot its roll publishes, so `-Cluster
CC1` shows CC1 and not the C1 the shipped document names (ADR-0052,
amendment 2026-09-18). A roll started in another session is followed by
writing its file into `xmip.powershell.toml`.

The executable reads the same file: `xmip-cli show xmip:///C1` from the cli's
build directory answers with the same figures, because its `xmip.cli.toml`
ships pointing at the same snapshot (ADR-0052, amendment 2026-09-18).

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
  commits and reconciles repositories; this operates a running Xmip. Their
  commands must not collide. The estate tooling uses this module and keeps no
  rule of its own: where it reads a node's declared capability or a published
  snapshot it builds this module into a directory of its own session and
  calls `Xmip.Surface` through it (ADR-0052, amendment 2026-09-24).
- Not a runtime, and holds no execution state.
- Not a competing prompt framework: it preserves and invokes the prompt that
  was installed before it.
- Not a drive. A PowerShell provider over the scope tree was proposed on
  2026-09-12 and declined on 2026-09-14 (ADR-0052): no record asks for one,
  and a provider is the drifting surface ADR-0014 warns against. Objects on
  the pipeline are the PowerShell shape here.

## Verification

`dotnet build`, then `Invoke-Pester -Path ./tests`. Thirty-nine tests, all
of them the PowerShell shape: the manifest and the exports agree, every verb is
approved, the two acts carry `-WhatIf` and no start, stop or restart exists,
the cmdlets emit objects and read the surface the line names, a failure
and an act land as audit records, the configuration document ships beside
the module, and the prompt paints the
color the runtime names for a mood. The rules beneath them — which surface
wins, what a wildcard selects, what a status means, whether a module
conforms — are tested once, in `Xmip.Surface.Test` and `Xmip.Abi.Tests`, and
the runtime's own — containment, the stage words, a mood's word, color and
order — once in Rust, where they are written; the module's build carries the
runtime library the rules are called in. The binding's
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
