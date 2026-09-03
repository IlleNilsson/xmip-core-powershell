# Repository architecture

Status: Accepted
Classification: Surface module
Maturity: pre-alpha
Owning capability: operator surfaces (ADR-0014)

## Responsibility

The Xmip PowerShell surface. Cmdlets and objects over the C ABI in
`xmip-core-abi` — never a subprocess, never scraped JSON. The three cmdlets
answer exactly what `xmip abi`, `xmip status` and `xmip probe` answer in
`xmip-core-cli`, because two surfaces disagreeing over one boundary is the
BizTalk console-versus-provider drift ADR-0014 exists to prevent.

## Runtime

Compiled by the .NET 11 preview SDK, targeting net10.0 — ADR-0014's amendment
of 2026-08-30. The SDK is the estate's; the target framework belongs to
whoever loads the assembly, and this one loads into pwsh 7.6.5, which hosts
.NET 10. Proven rather than assumed: the net11.0 build was refused by
`Import-Module` on the platform shell. Follows pwsh upward when pwsh moves.

## Public contracts

The cmdlet names and the shape of the objects they emit. Pre-alpha and
unstable, like the cli's output — but objects, so a caller filters and
compares rather than parsing.

## Dependencies

`include/xmip_module.h` from `xmip-core-abi`, and nothing else. No Xmip Rust
crate is referenced, and none may be — the same test the cli states: that
this project compiles without a single Xmip source file is the proof the
boundary works.

## Non-responsibilities

- Not the estate tooling. `Xmip/Xmip.psd1` in the platform repository lands
  commits and reconciles repositories; this operates a running Xmip. They
  share a prefix and nothing else, and their commands must not collide.
- Not a runtime, and holds no execution state.

## Verification

`dotnet build`, then `Invoke-Pester -Path ./tests`. Eighteen tests, and seven
of them compare this assembly against `xmip_module.h` rather than against
itself: every status the header defines, the name each takes, and which are
retryable and which terminal. ADR-0012 clause 1 makes the header normative and
this C# a convenience over it; that is now enforced rather than asserted in a
comment. They skip in a standalone clone, where the sibling header is absent.

`Get-XmipModuleDescriptor` against a conforming module is the same first
conformance rule the cli's probe exercises.

**The suite builds into a temporary directory and imports from there.** A
loaded binary module locks its own assembly: on 2026-09-03 a `dotnet build`
here failed ten retries because a pwsh session opened the day before still held
`bin/Debug/net10.0/Xmip.PowerShell.dll`. Testing out of `bin/` would make every
test run do that to the next build. The repository's own output is never
loaded.
