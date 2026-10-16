---
name: vyralbot-build
description: Build and test the VyralBot desktop .NET/WPF solution the way this repo expects — full solution build, UI-only iteration, fast VerifyBin verify variant, and the test suite. Use when asked to build, compile, verify, or test VyralBot desktop code, or before/after any change to VyralBot/src.
---

# Build & test VyralBot desktop

All commands run from `VyralBot/src/` (the solution root).

## Standard build

```bash
cd VyralBot/src
dotnet build VyralBot.sln -c Debug --nologo -v q
```

Gate: **0 errors** (warnings are not errors here; `TreatWarningsAsErrors=false`).
MVVMTK0034 warnings from `FlowEditorViewModel.cs` are pre-existing — ignore them,
do not "fix" them in unrelated changes.

## Fast iteration on the UI project only

```bash
dotnet build VyralBot.UI/VyralBot.UI.csproj -c Debug --nologo -v q
```

This builds all referenced projects transitively (UI references Api, Core, Data,
Integrations, Workflows) — usually what you want while iterating on one page.

## Fast verify variant (from past sessions; avoids clobbering bin outputs)

```bash
dotnet build VyralBot.sln -c Debug --nologo -v q -p:BaseOutputPath=VerifyBin/
```

## Tests

```bash
dotnet test
```

Keep the suite green — if your change breaks tests, fix them before reporting.
Historic baselines were 81 then 387 tests as the project grew; treat the current
count as the baseline.

## Environment notes

- All projects target `net10.0-windows`, x64, `UseWPF`. XAML compiles during
  build — a clean build means the XAML is valid too.
- Grep/ugrep will spew warnings scanning `bin/`, `obj/`, `VerifyBin/` — exclude
  those dirs in searches.
- `ViewModels/ChatViewModel.cs` is huge and fragile: surgical edits only.

## Common compile pitfalls (seen in this repo)

- Extension methods that exist for the webhook server's `WebApplication`
  (e.g. `RunAsync(CancellationToken)`, `Response.WriteAsync`) need the right
  usings: `Microsoft.Extensions.Hosting`, `Microsoft.AspNetCore.Http`.
- Untyped lambdas passed to `app.Map(...)` can bind to the wrong overload —
  type the parameter: `async (HttpContext context) => …`.
- `ExecutionContext` is ambiguous (`System.Threading` vs
  `VyralBot.Workflows.Runtime`) — alias the workflows one.
