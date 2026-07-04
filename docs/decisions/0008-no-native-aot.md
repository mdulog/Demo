# 0008 — Do Not Pursue Native AOT

**Status:** Accepted

## Context and Problem Statement

`feature/aot-support` attempted to make the API publishable with `dotnet publish -p:PublishAot=true`. The question is whether Native AOT is achievable for this codebase given its EF Core-based data access, and whether to continue investing in it.

## Considered Options

1. **Full Native AOT** — rewrite all EF Core query call sites into a form the AOT toolchain can handle; replace ASP.NET Core Identity (reflection-heavy) with a custom user store; source-generate all JSON contracts.
2. **Partial AOT (ReadyToRun / trimming only)** — drop `PublishAot`, keep other startup/size optimizations that don't forbid runtime JIT.
3. **No AOT** — publish as a normal JIT-compiled application; revert any AOT-motivated changes that have no standalone benefit.

## Decision Outcome

Chose **No AOT (option 3)**.

The branch got as far as: swapping `IdentityDbContext<IdentityUser>` for a custom `AppUser`/`IUserRepository` (removing Identity's reflection-heavy `UserManager`), adding `System.Text.Json` source-gen contexts for every request/response DTO, and fixing every resulting AOT trim/reflection warning down to zero. The published binary booted, and `/health` returned correctly. The first real database query — `POST /api/auth/register`, a plain `FirstOrDefaultAsync` on `Users` — crashed:

```
System.InvalidOperationException: Query wasn't precompiled and dynamic code isn't supported with NativeAOT.
```

We then tested EF Core 10's `dotnet ef dbcontext optimize --precompile-queries` — the feature specifically designed to make ad-hoc LINQ AOT-safe by generating build-time C# interceptors instead of relying on runtime `Expression.Compile()`. It rejected **every single query in the codebase**, including the simplest possible one-predicate lookup (`db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant(), ct)`), with `Dynamic LINQ queries are not supported when precompiling queries.` The manual `EF.CompileAsyncQuery` API doesn't help either — per Microsoft's own docs, it still relies on runtime code generation for result materialization, which NativeAOT forbids regardless of whether the query shape was precompiled.

**Why:**
- EF Core's own documentation labels NativeAOT + precompiled-query support "experimental," "not recommended for production," with stabilization only *targeted* for EF Core 10 — and at the pinned version (`10.*`), it cannot handle this codebase's completely idiomatic query style. This isn't "some queries need rewriting" — every query tested failed, with no confirmed shape that currently works.
- There is no partial credit: without precompiled queries, every `db.Xxx.Where(...)`/`FirstOrDefaultAsync(...)` across every feature slice (Auth, Events, Sync, Chat — dozens of call sites) would crash identically in production.
- The AOT-motivated changes (Identity swap, JSON source-gen) had no benefit independent of AOT succeeding, so once AOT was ruled out they were reverted rather than kept as unexplained complexity.

## Consequences

- **Easier:** The codebase stays on standard ASP.NET Core Identity and idiomatic ad-hoc EF Core LINQ — no custom user store or per-DTO source-gen context to maintain.
- **Harder:** Native AOT's startup-time and memory benefits are not available. `PublishAot`/`IsAotCompatible` were removed from `Pacevite.Api.csproj`; `dotnet publish` produces a normal JIT-compiled binary.
- **Revisit condition:** Retry only after EF Core's precompiled-query feature stabilizes *and* is confirmed (via a spike, not docs alone) to handle ordinary parameterized LINQ queries with closures over method parameters — the exact pattern this codebase uses everywhere. Until then, this isn't a "not yet implemented here" gap — it's a genuine limitation of the current EF Core release.
