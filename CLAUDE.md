# Ntxinh.EFCore.Bulks — Claude Code Instructions

## Project type

NuGet class library — bulk SQL helpers for EF Core (`BulkInsertAsync`, `BulkInsertMultipleTablesAsync`, `Generate*Query<T>` extensions). Published at https://www.nuget.org/packages/Ntxinh.EFCore.Bulks. **Not** an application — there is no architecture (VSA/Clean/DDD) to defend, just a focused public API surface.

## Layout

```
Ntxinh.EFCore.Bulks.slnx          # solution
Directory.Build.props             # net10.0, nullable enabled, latest-recommended analyzers
Directory.Packages.props          # central package management (CPM)
.mcp.json                         # Roslyn MCP server config
src/
  Ntxinh.EFCore.Bulks/            # the published library (Microsoft.NET.Sdk, net10.0)
    Extensions/                   # extension methods are the public API surface
    Dtos/                         # DTOs for multi-table bulk ops
    SqlBulkCopyHelper.cs
    DataTableHelper.cs
  Ntxinh.EFCore.Bulks.Demo/       # console app with SQLite, exercises the API
  Ntxinh.EFCore.Bulks.Tests/      # empty (no tests yet)
```

## Stack

- **Target**: `net10.0` only
- **Library deps**: `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.SqlServer` (10.0.0)
- **Demo deps**: `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design`
- **DB targets**: SQL Server is the primary target (uses `SqlBulkCopy`); demo uses SQLite for ad-hoc verification
- **Tests**: none scaffolded yet

## Build / run

```bash
dotnet build                                  # builds the slnx
dotnet run --project src/Ntxinh.EFCore.Bulks.Demo   # exercise the API end-to-end
```

## Versioning & publishing

Public API is the value of this project. Treat changes carefully.

- Bump `<Version>` in `src/Ntxinh.EFCore.Bulks/Ntxinh.EFCore.Bulks.csproj` for any release
- Library version tracks .NET major (currently `10.0.x`)
- Package metadata (`PackageId`, `Description`, `PackageTags`, `PackageIcon`) lives in that csproj
- `GeneratePackageOnBuild=true`, so `dotnet build` produces the `.nupkg` under `bin/Debug/`
- Publish steps are in the README (`dotnet nuget push ...`)

## Conventions

- C# **file-scoped namespaces**, **nullable** enabled, **implicit usings**, **`var`** preferred
- Public API lives under `Extensions/` as static classes with `this DbContext` extension methods. Keep that shape — consumers depend on it
- New `Generate*Query<T>` helpers should follow the same signature pattern (return tuple `(string sql, ColumnInfo pk)` when the caller needs the PK, otherwise just `string`)
- Don't take dependencies on additional NuGet packages without explicit confirmation — small surface area is a feature
- Add new package versions to `Directory.Packages.props`, not to individual csproj files
- Don't add a `TestProject` SDK type or test framework packages without confirmation — tests are intentionally not scaffolded
- README is shipped with the package (`PackageReadmeFile`); update it whenever public API changes

## Backwards compatibility

This is a published NuGet package. Consumers exist.

- **Adding** an extension method or overload: safe
- **Renaming** or **removing** a public method: breaking change — needs a major version bump and a note in the commit message
- **Changing return types** on `Generate*Query<T>` helpers: breaking — same rule
- **Behavioral changes** to `BulkInsertAsync` (e.g., transaction semantics, identity handling): also breaking from the consumer's perspective — flag it explicitly

When in doubt, ask before changing a method signature in `Extensions/`.

## Known issues / tech debt

- Transitive `System.Security.Cryptography.Xml` 9.0.0 (via `Microsoft.EntityFrameworkCore.Design` in the Demo project) has high-severity CVEs. Build emits `NU1903` warnings. Not shipped to consumers (Design is `PrivateAssets=all`), but worth pinning a safe version (`>= 9.0.5`) in `Directory.Packages.props` if it gets noisy.
- ~93 nullable warnings (CS8625, CS8603, etc.) in existing code. Nullable is enabled, but the existing code predates it — fix opportunistically, don't do a big bang pass without asking.
- `Ntxinh.EFCore.Bulks.Tests/` is empty. If a bug needs reproduction or a regression-prone path (e.g., MERGE upsert) needs coverage, ask before scaffolding xUnit + Testcontainers.
- TODO from README: `GenerateMergeUpsertQuery<T>(isMulipleData: true, useTempTable: true)` and `SqlTransaction` support are not implemented yet.

## Opt-in hardening (deferred)

These were considered during init but not turned on, because they'd surface pre-existing issues:

- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — currently blocked by the `NU1903` transitive CVE
- `<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>` — same reason

Enable both once `System.Security.Cryptography.Xml` is pinned to a safe version.

## Tooling

- `.mcp.json` configures the Roslyn MCP server pointed at `Ntxinh.EFCore.Bulks.slnx` for `/code-review`, `/health-check`, etc.
- `.editorconfig` is at the repo root — respect it
