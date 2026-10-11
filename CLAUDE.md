# Ntxinh.EFCore.Bulks — Claude Code Instructions

## Project type

NuGet class library — transaction-aware bulk insert/update for EF Core on SQL Server (`BulkInsertAsync`, `BulkUpdateAsync`, `GetInsertSql<T>`, `GetTableName<T>`). Published at https://www.nuget.org/packages/Ntxinh.EFCore.Bulks. **Not** an application — just a focused public API surface. Main consumer: `azure_co4_backend` (QXM).

Design: `docs/superpowers/specs/2026-10-11-production-ready-rewrite-design.md`.

## Layout

```
Ntxinh.EFCore.Bulks.slnx
Directory.Build.props             # net8.0;net10.0, nullable, latest-recommended analyzers, warnings as errors
Directory.Packages.props          # central package management, TFM-conditional EF floors, transitive pinning
.github/workflows/ci.yml          # build, test, pack; publish on v* tags (Trusted Publishing)
src/Ntxinh.EFCore.Bulks/
  BulkExtensions.cs               # public: BulkInsertAsync x2, BulkUpdateAsync
  SqlTemplateExtensions.cs        # public: GetInsertSql, GetTableName
  BulkOptions.cs, InsertSqlOptions.cs
  Internal/                       # TableMap, EntityDataReader, BulkExecutor, MemberSelector
tests/Ntxinh.EFCore.Bulks.Tests/  # xUnit v3; unit + Testcontainers SQL Server 2022 integration
```

## Stack

- **Targets**: `net8.0` (EF Core SqlServer 8.0.14 floor) and `net10.0` (10.0.0 floor). Floors are the *lowest* supported version — raising them breaks consumers on older patches (NU1605). Raise only to clear a NuGet audit failure.
- **Runtime deps**: `Microsoft.EntityFrameworkCore.SqlServer` only.
- **Tests**: xUnit v3, Testcontainers.MsSql. Integration tests need Docker; with Podman prefix commands with
  `env DOCKER_HOST=unix:///run/user/1000/podman/podman.sock TESTCONTAINERS_RYUK_DISABLED=true`.

## Build / test

```bash
dotnet build -c Release
dotnet test tests/Ntxinh.EFCore.Bulks.Tests --filter "FullyQualifiedName~TableMapTests"   # unit only
env DOCKER_HOST=unix:///run/user/1000/podman/podman.sock TESTCONTAINERS_RYUK_DISABLED=true dotnet test
```

## Versioning & publishing

- SemVer, from the git tag: push `vX.Y.Z` (or `vX.Y.Z-rc.N`) and CI publishes. The csproj `<Version>` is a dev default.
- `EnablePackageValidation` with `PackageValidationBaselineVersion` = last released version: an accidental breaking change fails `dotnet pack`.
- Metadata (`PackageId`, `Description`, `PackageTags`, `PackageIcon`) lives in `src/Ntxinh.EFCore.Bulks/Ntxinh.EFCore.Bulks.csproj`.

## Conventions

- File-scoped namespaces, nullable, implicit usings, `var` preferred. `.editorconfig` violations are build errors (PascalCase methods incl. tests, no unused usings).
- Public API = the extension classes and option records in the package root namespace. Everything else is `internal` under `Ntxinh.EFCore.Bulks.Internal`.
- Values never go into SQL text; identifiers only via `TableMap.Delimit` / `ColumnMap.Delimited`.
- No new NuGet dependencies (runtime or test) without explicit confirmation.
- Package versions go in `Directory.Packages.props`.
- README is shipped with the package — update it (absolute links only) whenever public API changes; add a `CHANGELOG.md` entry for every release.

## Backwards compatibility

Published package with consumers.

- **Adding** a method, overload or option property: safe (minor).
- **Renaming/removing** public members, changing return types, or changing defaults (`CheckConstraints`, `Timeout`), transaction semantics or identity handling: breaking — major version and a `BREAKING CHANGE:` commit footer.

When in doubt, ask before changing a public signature.

## Known gaps (by design, add when a consumer needs them)

Bulk upsert/MERGE, bulk delete, bulk read, identity output, owned/complex types, inheritance (TPH/TPT/TPC), non-SQL Server providers.

## Tooling

- `.mcp.json` configures the Roslyn MCP server pointed at `Ntxinh.EFCore.Bulks.slnx`.
- `.editorconfig` is at the repo root — respect it.
