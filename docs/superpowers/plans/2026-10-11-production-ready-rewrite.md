# Ntxinh.EFCore.Bulks 2.0 Rewrite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 8.x/10.x string-building helpers with a small, safe, transaction-aware SQL Server bulk library (2.0.0) and migrate its one consumer, `azure_co4_backend`.

**Architecture:** An internal `TableMap` reads the EF model once per entity type (table, schema, columns, converters, identity/computed flags). `EntityDataReader` streams entities into `SqlBulkCopy`; `BulkExecutor` runs every write on the DbContext's own `SqlConnection`, joining the caller's transaction or opening one inside the execution strategy. Bulk update = bulk copy into a `#temp` table, then one `UPDATE … JOIN`. Public API: four `DbContext` extension methods plus two option records.

**Tech Stack:** C# (LangVersion latest), net8.0 + net10.0, EF Core SqlServer 8.0.14 / 10.0.0, Microsoft.Data.SqlClient (transitive), xUnit v3 3.2.2, Testcontainers.MsSql 4.16.0 (SQL Server 2022), GitHub Actions, NuGet Trusted Publishing.

**Spec:** `docs/superpowers/specs/2026-10-11-production-ready-rewrite-design.md` (read it first; this plan argues from it).

**Spec amendments made while planning** (already written into the spec):
1. Identifiers are delimited by `TableMap.Delimit` (`[name]`, `]` doubled), not EF's `ISqlGenerationHelper` — InMemory (the consumer's unit-test provider) does not register that service, and `GetInsertSql` must work there.
2. Duplicate `matchOn` values are detected in SQL (`GROUP BY … HAVING COUNT(*) > 1`) after the temp load, so SQL Server collation decides equality. Input validation (null / wrong-type elements) happens before any connection is used.
3. The temp table is dropped on success; on failure the rollback removes it.
4. EF floors are 8.0.14 (net8.0) and 10.0.0 (net10.0) — the lowest supported, so QXM (pinned 8.0.14) restores without NU1605.
5. Release goes through `2.0.0-rc.1` so QA validates the published package before `2.0.0`.
6. Consumer: bulk updates use `.DistinctBy(key)` (old `CASE WHEN` first-match semantics); the multi-table insert sites keep `Timeout = TimeSpan.Zero` (old `new SqlBulkCopyOptionsDto()` had `Timeout = 0` = no limit).

## Global Constraints

- Target frameworks: `net8.0;net10.0` for both `src/Ntxinh.EFCore.Bulks` and `tests/Ntxinh.EFCore.Bulks.Tests`.
- Library runtime dependency: `Microsoft.EntityFrameworkCore.SqlServer` only — `8.0.14` on net8.0, `10.0.0` on net10.0. No other runtime package without asking the user.
- Test-only packages (exact): `xunit.v3` 3.2.2, `xunit.runner.visualstudio` 3.1.5, `Microsoft.NET.Test.Sdk` 18.10.1, `Testcontainers.MsSql` 4.16.0. No others without asking.
- All versions live in `Directory.Packages.props` (central package management); never put `Version=` on a `PackageReference`.
- Package version is SemVer from the git tag (`v2.0.0-rc.1` → `2.0.0-rc.1`); csproj `<Version>` is a dev default `2.0.0-dev`.
- Build must be clean with `TreatWarningsAsErrors=true`, `Nullable=enable`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild=true`, `GenerateDocumentationFile=true` (src).
- In tests, pass expected collections to `Assert.Equal` as arrays (`new[] { … }`), not collection expressions: xUnit's overloads make `[...]` ambiguous.
- `.editorconfig` rules that are build errors here: methods (including test methods) PascalCase — **no underscores in test names**; private static fields PascalCase; file-scoped namespaces; usings outside the namespace; explicit accessibility modifiers; no unnecessary usings (IDE0005). Order usings: `System*` group, blank line, `Microsoft*` group, blank line, `Ntxinh*`/other.
- Every public type and member has an XML doc comment.
- Values never appear in SQL text. Identifiers enter SQL text only through `TableMap.Delimit` / `ColumnMap.Delimited` / `TableMap.Table`, or as generated `#bulk_<guid:N>` names.
- Public API is exactly: `BulkExtensions.BulkInsertAsync<T>`, `BulkExtensions.BulkInsertAsync(Type, IEnumerable, …)`, `BulkExtensions.BulkUpdateAsync<T>`, `SqlTemplateExtensions.GetInsertSql<T>`, `SqlTemplateExtensions.GetTableName<T>`, records `BulkOptions`, `InsertSqlOptions`. Everything else is `internal` in namespace `Ntxinh.EFCore.Bulks.Internal`.
- SQL Server image: `mcr.microsoft.com/mssql/server:2022-latest`. Locally Docker is not installed; Podman 5.8 is, with its socket active. Prefix every integration test command with `env DOCKER_HOST=unix:///run/user/1000/podman/podman.sock TESTCONTAINERS_RYUK_DISABLED=true` (works in fish and bash). CI (GitHub-hosted ubuntu) needs no prefix.
- Analyzer warnings that fire **only in test code** because of test-model shape may be added to the test csproj `<NoWarn>` with a one-line comment. Never suppress in `src` except the single documented `CA2100` pragma in `BulkExecutor.Command`.
- Commits that change public API end with a `BREAKING CHANGE:` footer (project CLAUDE.md rule).
- Work happens on branch `feat/v2-rewrite` (already created; the spec commit is on it). Outward-facing steps (push, PR, tag, nuget.org, consumer repo push) require the user's explicit go-ahead at that step.

## Review Focus

1. **A bulk call made in a `finally` after the caller's transaction was committed or rolled back but not yet disposed** (four QXM sites do this) → the library must start its own transaction and write the rows, not try to reuse the finished one. Pinned by `TransactionTests.StartsOwnTransactionAfterCallerTransactionCompleted` (Task 5).
2. **The non-generic insert receiving a list with a `null` or wrong-typed element** (QXM's `BulkInsertJob` carries `IReadOnlyList<object>`) → `ArgumentException` naming the problem, before any connection is opened, nothing written. Pinned by `ValidationTests.BulkInsertRejectsNullElements` / `BulkInsertRejectsElementsOfAnotherType` (Task 4).
3. **`matchOn` values that are `NULL`** → those rows match nothing; no error, no stray updates. Pinned by `BulkUpdateTests.NullMatchOnValuesMatchNothing` (Task 6).
4. **A table name containing `]`** → correctly delimited both as the `SqlBulkCopy` destination and in SQL text. Pinned by `TableMapTests.EscapesClosingBracketInNames` (Task 1) and `BulkInsertTests.WritesToTableWhoseNameContainsBracket` (Task 4).
5. **`GetInsertSql` on a non-SQL Server model** (QXM's repository unit tests run on EF InMemory) → returns SQL without throwing. Pinned by QXM's existing `SlabBidApplicationTotalRepositoryCharacterizationTests.CalculateBidApplicationTotals_HandsEveryItemRowToTheItemInsert` staying green (Task 9).

---

## File Structure

```
Directory.Build.props                       MODIFY  multi-target, warnings as errors, CI build flag
Directory.Packages.props                    MODIFY  TFM-conditional EF, test packages, transitive pinning
Ntxinh.EFCore.Bulks.slnx                    MODIFY  drop Demo, add tests
CLAUDE.md, README.md                        MODIFY  (Task 7)
CHANGELOG.md                                CREATE  (Task 7)
.github/workflows/ci.yml                    CREATE  (Task 7)
.vscode/launch.json, .vscode/tasks.json     DELETE  (Demo-only)
src/Ntxinh.EFCore.Bulks.Demo/               DELETE
src/Ntxinh.EFCore.Bulks/
  Ntxinh.EFCore.Bulks.csproj                MODIFY  packaging metadata, InternalsVisibleTo
  Constants.cs, DataTableHelper.cs, Helpers.cs, ObjectExtensions.cs,
  SqlBulkCopyHelper.cs, TypeDefaultValue.cs, Dtos/, Extensions/   DELETE
  BulkExtensions.cs                         CREATE  public: BulkInsertAsync x2, BulkUpdateAsync
  SqlTemplateExtensions.cs                  CREATE  public: GetInsertSql, GetTableName
  BulkOptions.cs                            CREATE  public record
  InsertSqlOptions.cs                       CREATE  public record
  Internal/TableMap.cs                      CREATE  ColumnMap + TableMap (EF metadata, cache, delimiting)
  Internal/EntityDataReader.cs              CREATE  DbDataReader over entities
  Internal/BulkExecutor.cs                  CREATE  connection/transaction, SqlBulkCopy, commands
  Internal/MemberSelector.cs                CREATE  x => x.A / x => new { x.A, x.B } → columns
tests/Ntxinh.EFCore.Bulks.Tests/
  Ntxinh.EFCore.Bulks.Tests.csproj          CREATE
  TestModel.cs                              CREATE  entities + TestDbContext
  TableMapTests.cs                          CREATE  unit
  SqlTemplateTests.cs                       CREATE  unit
  MemberSelectorTests.cs                    CREATE  unit
  ValidationTests.cs                        CREATE  unit (argument checks, no database)
  SqlServerFixture.cs                       CREATE  Testcontainers SQL Server, collection fixture
  BulkInsertTests.cs                        CREATE  integration
  TransactionTests.cs                       CREATE  integration
  BulkUpdateTests.cs                        CREATE  integration
```

Common commands (run from the repo root):

```bash
dotnet build -c Release                                                    # both TFMs, must be 0 warnings
dotnet test tests/Ntxinh.EFCore.Bulks.Tests --filter "FullyQualifiedName~TableMapTests"     # unit, no container
env DOCKER_HOST=unix:///run/user/1000/podman/podman.sock TESTCONTAINERS_RYUK_DISABLED=true \
  dotnet test tests/Ntxinh.EFCore.Bulks.Tests                              # everything
```

The first integration run pulls the ~1.6 GB SQL Server image; allow several minutes.

---

### Task 1: Project skeleton and EF metadata map

**Files:**
- Delete: `src/Ntxinh.EFCore.Bulks.Demo/`, `src/Ntxinh.EFCore.Bulks/{Constants.cs,DataTableHelper.cs,Helpers.cs,ObjectExtensions.cs,SqlBulkCopyHelper.cs,TypeDefaultValue.cs}`, `src/Ntxinh.EFCore.Bulks/Dtos/`, `src/Ntxinh.EFCore.Bulks/Extensions/`, `.vscode/launch.json`, `.vscode/tasks.json`
- Modify: `Directory.Build.props`, `Directory.Packages.props`, `Ntxinh.EFCore.Bulks.slnx`, `src/Ntxinh.EFCore.Bulks/Ntxinh.EFCore.Bulks.csproj`
- Create: `src/Ntxinh.EFCore.Bulks/Internal/TableMap.cs`, `tests/Ntxinh.EFCore.Bulks.Tests/Ntxinh.EFCore.Bulks.Tests.csproj`, `tests/Ntxinh.EFCore.Bulks.Tests/TestModel.cs`, `tests/Ntxinh.EFCore.Bulks.Tests/TableMapTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (namespace `Ntxinh.EFCore.Bulks.Internal`, all `internal`):
  - `sealed record ColumnMap(string PropertyName, string ColumnName, Type ProviderType, bool IsIdentity, bool IsStoreGenerated, Func<object, object?> Read)` with `string Delimited { get; }`
  - `sealed class TableMap` with `Type ClrType`, `string Table` (delimited, schema-qualified when a schema is set), `IReadOnlyList<ColumnMap> Columns` (EF order: key first, then alphabetical), `IReadOnlyList<ColumnMap> PrimaryKey` (empty for keyless), `IReadOnlyList<ColumnMap> InsertColumns(bool keepIdentity)`, `ColumnMap Column(string propertyName, string argumentName)`, `static TableMap For(DbContext db, Type clrType)`, `static string Delimit(string name)`
  - Test model (namespace `Ntxinh.EFCore.Bulks.Tests`): `Customer`, `Order`, `OrderStatus`, `OrderLine`, `AuditRow`, `Owner`, `Address`, `Animal`, `Dog`, `Secretive`, `TestDbContext.Create(string connectionString = "Server=unused;Database=unused", bool retryOnFailure = false)`

- [ ] **Step 1: Delete the old implementation, the Demo and its editor config**

```bash
git rm -r -q src/Ntxinh.EFCore.Bulks.Demo src/Ntxinh.EFCore.Bulks/Dtos src/Ntxinh.EFCore.Bulks/Extensions \
  src/Ntxinh.EFCore.Bulks/Constants.cs src/Ntxinh.EFCore.Bulks/DataTableHelper.cs src/Ntxinh.EFCore.Bulks/Helpers.cs \
  src/Ntxinh.EFCore.Bulks/ObjectExtensions.cs src/Ntxinh.EFCore.Bulks/SqlBulkCopyHelper.cs src/Ntxinh.EFCore.Bulks/TypeDefaultValue.cs \
  .vscode/launch.json .vscode/tasks.json
rm -rf src/Ntxinh.EFCore.Bulks.Tests   # empty placeholder directory, may not exist
```

- [ ] **Step 2: Replace `Directory.Build.props`**

```xml
<Project>

  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <!-- GitHub Actions sets CI=true: deterministic paths for SourceLink and the symbol package. -->
  <PropertyGroup Condition="'$(CI)' == 'true'">
    <ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
  </PropertyGroup>

</Project>
```

- [ ] **Step 3: Replace `Directory.Packages.props`**

```xml
<Project>

  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <!-- Lowest EF Core the library supports per TFM; consumers may run any later patch.
       Raise only to clear a NuGet audit (NU190x) failure, to the lowest clean patch. -->
  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
    <PackageVersion Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.0.14" />
  </ItemGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <PackageVersion Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <PackageVersion Include="Testcontainers.MsSql" Version="4.16.0" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageVersion Include="xunit.v3" Version="3.2.2" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Replace `src/Ntxinh.EFCore.Bulks/Ntxinh.EFCore.Bulks.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <PackageId>Ntxinh.EFCore.Bulks</PackageId>
    <Version>2.0.0-dev</Version>
    <Authors>ntxinh</Authors>
    <Copyright>Copyright ntxinh</Copyright>
    <Description>Transaction-aware SqlBulkCopy insert and temp-table bulk update for EF Core on SQL Server, driven by your EF model.</Description>
    <PackageTags>bulk;sqlbulkcopy;efcore;sqlserver</PackageTags>
    <PackageLicenseFile>LICENSE</PackageLicenseFile>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PackageIcon>icon.png</PackageIcon>
    <RepositoryUrl>https://github.com/ntxinh/Ntxinh.EFCore.Bulks.git</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    <EnablePackageValidation>true</EnablePackageValidation>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Ntxinh.EFCore.Bulks.Tests" />
  </ItemGroup>

  <ItemGroup>
    <None Include="..\..\LICENSE" Pack="true" PackagePath="\" />
    <None Include="..\..\README.md" Pack="true" PackagePath="\" />
    <None Include="..\..\icon.png" Pack="true" PackagePath="\" />
  </ItemGroup>

</Project>
```

- [ ] **Step 5: Replace `Ntxinh.EFCore.Bulks.slnx`**

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/Ntxinh.EFCore.Bulks/Ntxinh.EFCore.Bulks.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/Ntxinh.EFCore.Bulks.Tests/Ntxinh.EFCore.Bulks.Tests.csproj" />
  </Folder>
</Solution>
```

- [ ] **Step 6: Create `tests/Ntxinh.EFCore.Bulks.Tests/Ntxinh.EFCore.Bulks.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <!-- xUnit needs public test classes (CA1515); the test model exposes a rowversion byte[] (CA1819);
         test code awaits without ConfigureAwait (CA2007). -->
    <NoWarn>$(NoWarn);CA1515;CA1819;CA2007</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Testcontainers.MsSql" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="xunit.v3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Ntxinh.EFCore.Bulks\Ntxinh.EFCore.Bulks.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

- [ ] **Step 7: Create `tests/Ntxinh.EFCore.Bulks.Tests/TestModel.cs`**

The model is shared by unit tests (never connects) and integration tests (`EnsureCreated` in a container). `Owner`, `Animal`/`Dog` and `Secretive` exist only to prove unsupported mappings are rejected.

```csharp
using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks.Tests;

public enum OrderStatus
{
    Draft,
    Placed,
}

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string Name { get; set; } = "";
    public OrderStatus Status { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Note { get; set; }
    public int NameLength { get; set; }
    public byte[] Version { get; set; } = [];

    // Getter-only: EF does not map it.
    public string Display => $"{Name} ({Status})";
}

public class OrderLine
{
    public int OrderId { get; set; }
    public int LineNo { get; set; }
    public decimal Price { get; set; }
    public int Qty { get; set; }
}

public class AuditRow
{
    public string Message { get; set; } = "";
}

public class Owner
{
    public int Id { get; set; }
    public Address Address { get; set; } = new();
}

public class Address
{
    public string City { get; set; } = "";
}

public class Animal
{
    public int Id { get; set; }
}

public class Dog : Animal
{
    public string Breed { get; set; } = "";
}

public class Secretive
{
    public int Id { get; set; }
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public static TestDbContext Create(string connectionString = "Server=unused;Database=unused", bool retryOnFailure = false) =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                if (retryOnFailure)
                {
                    sql.EnableRetryOnFailure();
                }
            })
            .Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>();
        modelBuilder.Entity<Order>(order =>
        {
            order.ToTable("Orders", "sales");
            order.Property(x => x.Name).HasMaxLength(200);
            order.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            order.Property(x => x.Total).HasPrecision(18, 2);
            order.Property(x => x.Note).HasColumnName("note_text").HasMaxLength(200);
            order.Property(x => x.NameLength).HasComputedColumnSql("LEN([Name])");
            order.Property(x => x.Version).IsRowVersion();
            order.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId);
        });
        modelBuilder.Entity<OrderLine>(line =>
        {
            line.HasKey(x => new { x.OrderId, x.LineNo });
            line.Property(x => x.Price).HasPrecision(18, 2);
        });
        modelBuilder.Entity<AuditRow>().HasNoKey().ToTable("Audit]Rows");
        modelBuilder.Entity<Owner>().OwnsOne(x => x.Address);
        modelBuilder.Entity<Animal>();
        modelBuilder.Entity<Dog>();
        modelBuilder.Entity<Secretive>().Property<string>("Secret");
    }
}
```

- [ ] **Step 8: Write the failing tests `tests/Ntxinh.EFCore.Bulks.Tests/TableMapTests.cs`**

```csharp
using Ntxinh.EFCore.Bulks.Internal;

namespace Ntxinh.EFCore.Bulks.Tests;

public sealed class TableMapTests
{
    private static TableMap Map<T>()
    {
        using var db = TestDbContext.Create();
        return TableMap.For(db, typeof(T));
    }

    private static string[] Names(IEnumerable<ColumnMap> columns) => columns.Select(c => c.PropertyName).ToArray();

    [Fact]
    public void ResolvesSchemaQualifiedDelimitedTableName()
    {
        Assert.Equal("[sales].[Orders]", Map<Order>().Table);
        Assert.Equal("[Customer]", Map<Customer>().Table);
    }

    [Fact]
    public void EscapesClosingBracketInNames()
    {
        Assert.Equal("[Audit]]Rows]", Map<AuditRow>().Table);
        Assert.Equal("[a]]b]", TableMap.Delimit("a]b"));
    }

    [Fact]
    public void MapsRenamedColumnsAndFlagsDatabaseGeneratedOnes()
    {
        var map = Map<Order>();

        Assert.Equal("note_text", map.Column("Note", "set").ColumnName);
        Assert.Equal("[note_text]", map.Column("Note", "set").Delimited);
        Assert.True(map.Columns.Single(c => c.PropertyName == "Id").IsIdentity);
        Assert.True(map.Columns.Single(c => c.PropertyName == "NameLength").IsStoreGenerated);
        Assert.True(map.Columns.Single(c => c.PropertyName == "Version").IsStoreGenerated);
        Assert.False(map.Columns.Single(c => c.PropertyName == "Total").IsStoreGenerated);
    }

    [Fact]
    public void InsertColumnsSkipIdentityUnlessKept()
    {
        var map = Map<Order>();

        Assert.Equal(new[] { "CreatedAt", "CustomerId", "Name", "Note", "Status", "Total" }, Names(map.InsertColumns(keepIdentity: false)));
        Assert.Equal(new[] { "Id", "CreatedAt", "CustomerId", "Name", "Note", "Status", "Total" }, Names(map.InsertColumns(keepIdentity: true)));
    }

    [Fact]
    public void ReadAppliesValueConvertersAndKeepsNulls()
    {
        var map = Map<Order>();
        var order = new Order { Status = OrderStatus.Placed, Total = 1.5m, Note = null };

        Assert.Equal("Placed", map.Column("Status", "set").Read(order));
        Assert.Equal(typeof(string), map.Column("Status", "set").ProviderType);
        Assert.Equal(1.5m, map.Column("Total", "set").Read(order));
        Assert.Null(map.Column("Note", "set").Read(order));
    }

    [Fact]
    public void PrimaryKeyFollowsKeyOrderAndIsEmptyForKeylessTypes()
    {
        Assert.Equal(new[] { "OrderId", "LineNo" }, Names(Map<OrderLine>().PrimaryKey));
        Assert.Empty(Map<AuditRow>().PrimaryKey);
    }

    [Fact]
    public void CachesOneMapPerModelAndType()
    {
        Assert.Same(Map<Order>(), Map<Order>());
    }

    [Theory]
    [InlineData(typeof(Owner))]
    [InlineData(typeof(Animal))]
    [InlineData(typeof(Dog))]
    [InlineData(typeof(Secretive))]
    public void RejectsUnsupportedMappings(Type type)
    {
        using var db = TestDbContext.Create();

        var error = Assert.Throws<NotSupportedException>(() => TableMap.For(db, type));
        Assert.Contains(type.Name, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTypesOutsideTheModel()
    {
        using var db = TestDbContext.Create();

        Assert.Throws<InvalidOperationException>(() => TableMap.For(db, typeof(string)));
    }

    [Fact]
    public void ColumnRejectsUnmappedAndDatabaseGeneratedProperties()
    {
        var map = Map<Order>();

        Assert.Equal("set", Assert.Throws<ArgumentException>(() => map.Column("Display", "set")).ParamName);
        Assert.Equal("matchOn", Assert.Throws<ArgumentException>(() => map.Column("NameLength", "matchOn")).ParamName);
    }
}
```

- [ ] **Step 9: Run the tests to verify they fail**

Run: `dotnet test tests/Ntxinh.EFCore.Bulks.Tests --filter "FullyQualifiedName~TableMapTests"`
Expected: build FAILS with `CS0246: The type or namespace name 'TableMap' could not be found` (and `ColumnMap`).

- [ ] **Step 10: Create `src/Ntxinh.EFCore.Bulks/Internal/TableMap.cs`**

```csharp
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Ntxinh.EFCore.Bulks.Internal;

/// <summary>One mapped column: how to read its value from an entity and how to name it in SQL.</summary>
internal sealed record ColumnMap(
    string PropertyName,
    string ColumnName,
    Type ProviderType,
    bool IsIdentity,
    bool IsStoreGenerated,
    Func<object, object?> Read)
{
    public string Delimited => TableMap.Delimit(ColumnName);
}

/// <summary>What the EF model says about one entity's table, built once per model and CLR type.</summary>
internal sealed class TableMap
{
    private static readonly ConditionalWeakTable<IModel, ConcurrentDictionary<Type, TableMap>> Cache = new();

    private TableMap(Type clrType, string table, IReadOnlyList<ColumnMap> columns, IReadOnlyList<ColumnMap> primaryKey)
    {
        ClrType = clrType;
        Table = table;
        Columns = columns;
        PrimaryKey = primaryKey;
    }

    public Type ClrType { get; }

    /// <summary>Delimited table name, schema-qualified when the model sets a schema: <c>[sales].[Orders]</c>.</summary>
    public string Table { get; }

    public IReadOnlyList<ColumnMap> Columns { get; }

    public IReadOnlyList<ColumnMap> PrimaryKey { get; }

    public IReadOnlyList<ColumnMap> InsertColumns(bool keepIdentity) =>
        Columns.Where(c => !c.IsStoreGenerated && (keepIdentity || !c.IsIdentity)).ToList();

    public ColumnMap Column(string propertyName, string argumentName)
    {
        var column = Columns.FirstOrDefault(c => c.PropertyName == propertyName)
            ?? throw new ArgumentException($"'{propertyName}' is not a mapped column of {ClrType.Name}.", argumentName);
        return column.IsStoreGenerated
            ? throw new ArgumentException($"'{propertyName}' is computed by the database and cannot be written or matched.", argumentName)
            : column;
    }

    public static TableMap For(DbContext db, Type clrType)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clrType);
        return Cache.GetOrCreateValue(db.Model).GetOrAdd(clrType, static (type, model) => Build(model, type), db.Model);
    }

    // SQL Server delimiting rules. EF's ISqlGenerationHelper is not used because the InMemory provider lacks it
    // and GetInsertSql must work there (consumer unit tests).
    public static string Delimit(string name) => $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static TableMap Build(IModel model, Type clrType)
    {
        var entityType = model.FindEntityType(clrType)
            ?? throw new InvalidOperationException($"{clrType.Name} is not an entity type in this DbContext's model.");
        var name = entityType.GetTableName()
            ?? throw new InvalidOperationException($"{clrType.Name} is not mapped to a table.");
        var schema = entityType.GetSchema();

        if (entityType.BaseType is not null || entityType.GetDirectlyDerivedTypes().Any())
        {
            throw Unsupported(clrType, "it is part of an inheritance hierarchy");
        }

        if (entityType.GetNavigations().Any(n => n.TargetEntityType.IsOwned()))
        {
            throw Unsupported(clrType, "it has owned types");
        }

        if (entityType.GetComplexProperties().Any())
        {
            throw Unsupported(clrType, "it has complex properties");
        }

        if (model.GetEntityTypes().Count(e => e.GetTableName() == name && e.GetSchema() == schema) > 1)
        {
            throw Unsupported(clrType, $"another entity type is mapped to table {name}");
        }

        var store = StoreObjectIdentifier.Table(name, schema);
        var columns = entityType.GetProperties().Select(p => MapColumn(p, store, clrType)).ToList();
        IReadOnlyList<ColumnMap> primaryKey = entityType.FindPrimaryKey() is { } key
            ? key.Properties.Select(p => columns.Single(c => c.PropertyName == p.Name)).ToList()
            : [];
        var table = schema is null ? Delimit(name) : $"{Delimit(schema)}.{Delimit(name)}";

        return new TableMap(clrType, table, columns, primaryKey);
    }

    private static ColumnMap MapColumn(IProperty property, StoreObjectIdentifier store, Type clrType)
    {
        if (property.IsShadowProperty())
        {
            throw Unsupported(clrType, $"'{property.Name}' is a shadow property");
        }

        var member = (MemberInfo?)property.PropertyInfo ?? property.FieldInfo
            ?? throw Unsupported(clrType, $"'{property.Name}' has no readable CLR member");

        // Compiled once per column; avoids reflection per row.
        var entity = Expression.Parameter(typeof(object), "entity");
        var access = Expression.MakeMemberAccess(Expression.Convert(entity, member.DeclaringType!), member);
        var getter = Expression.Lambda<Func<object, object?>>(Expression.Convert(access, typeof(object)), entity).Compile();

        var converter = property.GetTypeMapping().Converter;
        Func<object, object?> read = converter is null ? getter : e => converter.ConvertToProvider(getter(e));
        var providerType = converter?.ProviderClrType ?? property.ClrType;

        return new ColumnMap(
            property.Name,
            property.GetColumnName(store) ?? throw Unsupported(clrType, $"'{property.Name}' has no column in table {store.Name}"),
            Nullable.GetUnderlyingType(providerType) ?? providerType,
            IsIdentity: property.GetValueGenerationStrategy() == SqlServerValueGenerationStrategy.IdentityColumn,
            IsStoreGenerated: property.GetComputedColumnSql() is not null
                || (property.IsConcurrencyToken && property.ValueGenerated == ValueGenerated.OnAddOrUpdate),
            Read: read);
    }

    private static NotSupportedException Unsupported(Type clrType, string reason) =>
        new($"Bulk operations do not support {clrType.Name}: {reason}.");
}
```

- [ ] **Step 11: Run the tests to verify they pass**

Run: `dotnet test tests/Ntxinh.EFCore.Bulks.Tests --filter "FullyQualifiedName~TableMapTests"`
Expected: PASS, 13 tests on net8.0 and 13 on net10.0 (9 facts + 4 theory rows — the runner reports each theory row).

If restore reports `NU1901`–`NU1904` (vulnerable transitive package) as an error: add `<PackageVersion Include="<package>" Version="<lowest patched version>" />` to the unconditional `ItemGroup` in `Directory.Packages.props` (transitive pinning applies it) and rerun. Do not lower `TreatWarningsAsErrors`.

- [ ] **Step 12: Verify a clean Release build of both TFMs**

Run: `dotnet build -c Release`
Expected: `Build succeeded.` with `0 Warning(s)` and `0 Error(s)`. If an analyzer fires in `src`, fix the code (do not suppress). If it fires only in test files because of the test model's shape, add the ID to the test csproj `<NoWarn>` with a comment.

- [ ] **Step 13: Commit**

```bash
git add -A
git commit -m "feat!: rebuild on an EF-metadata table map, multi-target net8/net10

Remove the DataTable/string-building implementation, the SQLite demo and its
editor config. Add TableMap (schema, delimiting, converters, identity and
computed detection, unsupported-mapping checks) and an xUnit v3 test project.

BREAKING CHANGE: every 8.x/10.x public type and method is removed; the 2.0 API
is added in the following commits."
```

---

### Task 2: SQL templates (`GetTableName`, `GetInsertSql`)

**Files:**
- Create: `src/Ntxinh.EFCore.Bulks/InsertSqlOptions.cs`, `src/Ntxinh.EFCore.Bulks/SqlTemplateExtensions.cs`
- Test: `tests/Ntxinh.EFCore.Bulks.Tests/SqlTemplateTests.cs`

**Interfaces:**
- Consumes: `TableMap.For(DbContext, Type)`, `TableMap.Table`, `TableMap.InsertColumns(bool)`, `ColumnMap.PropertyName`, `ColumnMap.Delimited` (Task 1).
- Produces (namespace `Ntxinh.EFCore.Bulks`, public):
  - `public sealed record InsertSqlOptions { public IReadOnlyCollection<string> UtcNowColumns { get; init; } = []; }`
  - `public static string GetTableName<T>(this DbContext db) where T : class`
  - `public static string GetInsertSql<T>(this DbContext db, InsertSqlOptions? options = null) where T : class`

- [ ] **Step 1: Write the failing tests `tests/Ntxinh.EFCore.Bulks.Tests/SqlTemplateTests.cs`**

```csharp
namespace Ntxinh.EFCore.Bulks.Tests;

public sealed class SqlTemplateTests
{
    [Fact]
    public void GetTableNameIsSchemaQualifiedAndDelimited()
    {
        using var db = TestDbContext.Create();

        Assert.Equal("[sales].[Orders]", db.GetTableName<Order>());
        Assert.Equal("[Audit]]Rows]", db.GetTableName<AuditRow>());
    }

    [Fact]
    public void GetInsertSqlListsWritableColumnsWithPropertyNamedParameters()
    {
        using var db = TestDbContext.Create();

        Assert.Equal(
            "INSERT INTO [sales].[Orders] ([CreatedAt], [CustomerId], [Name], [note_text], [Status], [Total]) "
            + "VALUES (@CreatedAt, @CustomerId, @Name, @Note, @Status, @Total);",
            db.GetInsertSql<Order>());
    }

    [Fact]
    public void GetInsertSqlKeepsNonIdentityKeys()
    {
        using var db = TestDbContext.Create();

        Assert.Equal(
            "INSERT INTO [OrderLine] ([OrderId], [LineNo], [Price], [Qty]) VALUES (@OrderId, @LineNo, @Price, @Qty);",
            db.GetInsertSql<OrderLine>());
    }

    [Fact]
    public void GetInsertSqlWritesUtcNowForListedColumnsAndIgnoresUnknownNames()
    {
        using var db = TestDbContext.Create();

        var sql = db.GetInsertSql<Order>(new InsertSqlOptions { UtcNowColumns = ["CreatedAt", "UpdatedAt"] });

        Assert.Equal(
            "INSERT INTO [sales].[Orders] ([CreatedAt], [CustomerId], [Name], [note_text], [Status], [Total]) "
            + "VALUES (GETUTCDATE(), @CustomerId, @Name, @Note, @Status, @Total);",
            sql);
    }

    [Fact]
    public void TemplatesRejectUnsupportedTypes()
    {
        using var db = TestDbContext.Create();

        Assert.Throws<NotSupportedException>(() => db.GetInsertSql<Owner>());
        Assert.Throws<InvalidOperationException>(() => db.GetTableName<string>());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Ntxinh.EFCore.Bulks.Tests --filter "FullyQualifiedName~SqlTemplateTests"`
Expected: build FAILS with `CS1061: 'TestDbContext' does not contain a definition for 'GetTableName'` and `CS0246: ... 'InsertSqlOptions' could not be found`.

- [ ] **Step 3: Create `src/Ntxinh.EFCore.Bulks/InsertSqlOptions.cs`**

```csharp
namespace Ntxinh.EFCore.Bulks;

/// <summary>Options for <see cref="SqlTemplateExtensions.GetInsertSql{T}"/>.</summary>
public sealed record InsertSqlOptions
{
    /// <summary>
    /// CLR property names whose value is written as <c>GETUTCDATE()</c> instead of a parameter.
    /// Names the entity does not have are ignored, so one list can serve every entity.
    /// </summary>
    public IReadOnlyCollection<string> UtcNowColumns { get; init; } = [];
}
```

- [ ] **Step 4: Create `src/Ntxinh.EFCore.Bulks/SqlTemplateExtensions.cs`**

```csharp
using Microsoft.EntityFrameworkCore;

using Ntxinh.EFCore.Bulks.Internal;

namespace Ntxinh.EFCore.Bulks;

/// <summary>SQL text built from the EF Core model, for Dapper or raw ADO.NET.</summary>
public static class SqlTemplateExtensions
{
    /// <summary>Returns the delimited, schema-qualified table name of <typeparamref name="T"/>, e.g. <c>[sales].[Orders]</c>.</summary>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> is not mapped to a table.</exception>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> uses a mapping bulk operations do not support.</exception>
    public static string GetTableName<T>(this DbContext db) where T : class =>
        TableMap.For(db, typeof(T)).Table;

    /// <summary>
    /// Returns <c>INSERT INTO [schema].[Table] ([Column], …) VALUES (@Property, …);</c> with one parameter per CLR
    /// property name, for executing once per entity (e.g. Dapper <c>ExecuteAsync(sql, entities)</c>).
    /// Identity, computed and rowversion columns are omitted.
    /// </summary>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> is not mapped to a table.</exception>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> uses a mapping bulk operations do not support.</exception>
    public static string GetInsertSql<T>(this DbContext db, InsertSqlOptions? options = null) where T : class
    {
        var map = TableMap.For(db, typeof(T));
        var utcNow = (options ?? new InsertSqlOptions()).UtcNowColumns;
        var columns = map.InsertColumns(keepIdentity: false);
        var names = string.Join(", ", columns.Select(c => c.Delimited));
        var values = string.Join(", ", columns.Select(c => utcNow.Contains(c.PropertyName) ? "GETUTCDATE()" : $"@{c.PropertyName}"));
        return $"INSERT INTO {map.Table} ({names}) VALUES ({values});";
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Ntxinh.EFCore.Bulks.Tests --filter "FullyQualifiedName~SqlTemplateTests|FullyQualifiedName~TableMapTests"`
Expected: PASS (all).

- [ ] **Step 6: Commit**

```bash
git add src/Ntxinh.EFCore.Bulks/InsertSqlOptions.cs src/Ntxinh.EFCore.Bulks/SqlTemplateExtensions.cs tests/Ntxinh.EFCore.Bulks.Tests/SqlTemplateTests.cs
git commit -m "feat: add GetTableName and GetInsertSql templates

BREAKING CHANGE: replaces GenerateInsertQuery and GenerateDeleteQuery. The
GETUTCDATE() rewrite of CreatedAt/UpdatedAt/CreatedOn/UpdatedOn is now opt-in
via InsertSqlOptions.UtcNowColumns; table names are schema-qualified and delimited."
```

---

### Task 3: Member selector for `set` / `matchOn`

**Files:**
- Create: `src/Ntxinh.EFCore.Bulks/Internal/MemberSelector.cs`
- Test: `tests/Ntxinh.EFCore.Bulks.Tests/MemberSelectorTests.cs`

**Interfaces:**
- Consumes: `TableMap.Column(string propertyName, string argumentName)` (Task 1).
- Produces: `internal static class MemberSelector { public static IReadOnlyList<ColumnMap> Columns<T>(Expression<Func<T, object>> selector, TableMap map, string argumentName); }` — accepts `x => x.Prop` or `x => new { x.A, x.B }`; duplicates collapse; throws `ArgumentException` (with `ParamName == argumentName`) for anything else, unmapped or database-generated members, or an empty selection.

- [ ] **Step 1: Write the failing tests `tests/Ntxinh.EFCore.Bulks.Tests/MemberSelectorTests.cs`**

```csharp
using System.Linq.Expressions;

using Ntxinh.EFCore.Bulks.Internal;

namespace Ntxinh.EFCore.Bulks.Tests;

public sealed class MemberSelectorTests
{
    private static readonly TableMap OrderMap = CreateOrderMap();

    private static TableMap CreateOrderMap()
    {
        using var db = TestDbContext.Create();
        return TableMap.For(db, typeof(Order));
    }

    private static string[] Columns(Expression<Func<Order, object>> selector) =>
        MemberSelector.Columns(selector, OrderMap, "set").Select(c => c.ColumnName).ToArray();

    [Fact]
    public void AcceptsASingleValueTypeMember()
    {
        Assert.Equal(new[] { "Total" }, Columns(x => x.Total));
    }

    [Fact]
    public void AcceptsAnAnonymousObjectOfMembers()
    {
        Assert.Equal(new[] { "Total", "note_text" }, Columns(x => new { x.Total, x.Note }));
    }

    [Fact]
    public void RepeatedMembersCountOnce()
    {
        Assert.Equal(new[] { "Total" }, Columns(x => new { x.Total, Again = x.Total }));
    }

    [Fact]
    public void RejectsMethodCalls()
    {
        Assert.Equal("set", Assert.Throws<ArgumentException>(() => Columns(x => x.Name.Trim())).ParamName);
    }

    [Fact]
    public void RejectsMembersOfOtherObjects()
    {
        var other = new Order();

        Assert.Throws<ArgumentException>(() => Columns(x => other.Total));
    }

    [Fact]
    public void RejectsUnmappedAndComputedMembers()
    {
        Assert.Throws<ArgumentException>(() => Columns(x => x.Display));
        Assert.Throws<ArgumentException>(() => Columns(x => x.NameLength));
    }

    [Fact]
    public void RejectsAnEmptySelection()
    {
        Assert.Throws<ArgumentException>(() => Columns(x => new { }));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Ntxinh.EFCore.Bulks.Tests --filter "FullyQualifiedName~MemberSelectorTests"`
Expected: build FAILS with `CS0103: The name 'MemberSelector' does not exist in the current context`.

- [ ] **Step 3: Create `src/Ntxinh.EFCore.Bulks/Internal/MemberSelector.cs`**

```csharp
using System.Linq.Expressions;

namespace Ntxinh.EFCore.Bulks.Internal;

/// <summary>Turns <c>x =&gt; x.Prop</c> or <c>x =&gt; new { x.A, x.B }</c> into mapped columns.</summary>
internal static class MemberSelector
{
    public static IReadOnlyList<ColumnMap> Columns<T>(Expression<Func<T, object>> selector, TableMap map, string argumentName)
    {
        // Value-type members are boxed to object: x => (object)x.Total.
        var body = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : selector.Body;
        IEnumerable<Expression> members = body is NewExpression anonymous ? anonymous.Arguments : [body];

        var columns = members
            .Select(member => member is MemberExpression access && access.Expression == selector.Parameters[0]
                ? map.Column(access.Member.Name, argumentName)
                : throw new ArgumentException(
                    $"'{member}' is not a property of {typeof(T).Name}. Use x => x.Prop or x => new {{ x.A, x.B }}.",
                    argumentName))
            .Distinct()
            .ToList();

        return columns.Count > 0
            ? columns
            : throw new ArgumentException("Select at least one property.", argumentName);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Ntxinh.EFCore.Bulks.Tests --filter "FullyQualifiedName~MemberSelectorTests"`
Expected: PASS (7 tests per TFM).

- [ ] **Step 5: Commit**

```bash
git add src/Ntxinh.EFCore.Bulks/Internal/MemberSelector.cs tests/Ntxinh.EFCore.Bulks.Tests/MemberSelectorTests.cs
git commit -m "feat: parse set/matchOn member selectors into mapped columns"
```

---

### Task 4: Bulk insert end to end (reader, executor, public insert API, SQL Server fixture)

**Files:**
- Create: `src/Ntxinh.EFCore.Bulks/BulkOptions.cs`, `src/Ntxinh.EFCore.Bulks/Internal/EntityDataReader.cs`, `src/Ntxinh.EFCore.Bulks/Internal/BulkExecutor.cs`, `src/Ntxinh.EFCore.Bulks/BulkExtensions.cs`
- Test: `tests/Ntxinh.EFCore.Bulks.Tests/ValidationTests.cs`, `tests/Ntxinh.EFCore.Bulks.Tests/SqlServerFixture.cs`, `tests/Ntxinh.EFCore.Bulks.Tests/BulkInsertTests.cs`

**Interfaces:**
- Consumes: `TableMap.For`, `TableMap.Table`, `TableMap.InsertColumns`, `ColumnMap.ColumnName`, `ColumnMap.ProviderType`, `ColumnMap.Read` (Task 1).
- Produces:
  - `public sealed record BulkOptions { int BatchSize; TimeSpan Timeout = 30s; bool KeepIdentity; bool CheckConstraints = true; }` (init-only properties)
  - `public static Task<int> BulkInsertAsync<T>(this DbContext db, IEnumerable<T> entities, BulkOptions? options = null, CancellationToken cancellationToken = default) where T : class`
  - `public static Task<int> BulkInsertAsync(this DbContext db, Type entityType, IEnumerable entities, BulkOptions? options = null, CancellationToken cancellationToken = default)`
  - `internal sealed class EntityDataReader(IEnumerable rows, IReadOnlyList<ColumnMap> columns) : DbDataReader` with `int RowCount`
  - `internal static class BulkExecutor` with `Task<int> RunAsync(DbContext db, Func<SqlConnection, SqlTransaction, CancellationToken, Task<int>> work, CancellationToken cancellationToken)`, `Task<int> CopyAsync(SqlConnection connection, SqlTransaction transaction, string destination, IEnumerable rows, IReadOnlyList<ColumnMap> columns, SqlBulkCopyOptions flags, BulkOptions options, CancellationToken cancellationToken)`, `SqlCommand Command(SqlConnection connection, SqlTransaction transaction, string sql, BulkOptions options)`
  - `private static ICollection Materialize(IEnumerable entities, Type entityType)` in `BulkExtensions` (validates elements)
  - Test fixture: `SqlServerFixture` (`ConnectionString`, `CreateContext(bool retryOnFailure = false)`, `ResetAsync()`), `SqlServerCollection.Name == "SqlServer"`

- [ ] **Step 1: Write the failing unit tests `tests/Ntxinh.EFCore.Bulks.Tests/ValidationTests.cs`**

These use `Server=unused`: they pass only if the library rejects or short-circuits before touching a connection.

```csharp
namespace Ntxinh.EFCore.Bulks.Tests;

public sealed class ValidationTests
{
    [Fact]
    public async Task BulkInsertOfNothingReturnsZeroWithoutConnecting()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestDbContext.Create();

        Assert.Equal(0, await db.BulkInsertAsync(Array.Empty<Customer>(), cancellationToken: ct));
    }

    [Fact]
    public async Task BulkInsertRejectsTypesOutsideTheModel()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestDbContext.Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.BulkInsertAsync(typeof(string), new[] { "x" }, cancellationToken: ct));
    }

    [Fact]
    public async Task BulkInsertRejectsNullElements()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestDbContext.Create();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => db.BulkInsertAsync(typeof(Customer), new object?[] { new Customer(), null }, cancellationToken: ct));
        Assert.Contains("null", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BulkInsertRejectsElementsOfAnotherType()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestDbContext.Create();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => db.BulkInsertAsync(typeof(Customer), new object[] { new Order() }, cancellationToken: ct));
        Assert.Contains(nameof(Order), error.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Create the SQL Server fixture `tests/Ntxinh.EFCore.Bulks.Tests/SqlServerFixture.cs`**

```csharp
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using Testcontainers.MsSql;

namespace Ntxinh.EFCore.Bulks.Tests;

/// <summary>One SQL Server 2022 container per test run; tests in the collection run one at a time.</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString { get; private set; } = "";

    public TestDbContext CreateContext(bool retryOnFailure = false) => TestDbContext.Create(ConnectionString, retryOnFailure);

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await container.StartAsync(ct);
        ConnectionString = new SqlConnectionStringBuilder(container.GetConnectionString()) { InitialCatalog = "BulksTests" }.ConnectionString;
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync(ct);
    }

    public ValueTask DisposeAsync() => container.DisposeAsync();

    public async Task ResetAsync()
    {
        await using var db = CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM [OrderLine]; DELETE FROM [sales].[Orders]; DELETE FROM [Customer]; DELETE FROM [Audit]]Rows];",
            TestContext.Current.CancellationToken);
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
```

- [ ] **Step 3: Write the failing integration tests `tests/Ntxinh.EFCore.Bulks.Tests/BulkInsertTests.cs`**

```csharp
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class BulkInsertTests(SqlServerFixture fixture) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task<Customer> AddCustomerAsync(TestDbContext db, string name, CancellationToken ct)
    {
        var customer = new Customer { Name = name };
        db.Add(customer);
        await db.SaveChangesAsync(ct);
        return customer;
    }

    [Fact]
    public async Task InsertsEveryWritableColumnFaithfully()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        var customer = await AddCustomerAsync(db, "c", ct);
        var createdAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(1234567);
        Order[] orders =
        [
            new() { CustomerId = customer.Id, Name = "O'Brien – 日本", Status = OrderStatus.Placed, Total = 12.34m, CreatedAt = createdAt, Note = null },
            new() { CustomerId = customer.Id, Name = "plain", Status = OrderStatus.Draft, Total = 0.01m, CreatedAt = createdAt, Note = "it's fine" },
        ];

        var inserted = await db.BulkInsertAsync(orders, cancellationToken: ct);

        Assert.Equal(2, inserted);
        var saved = await db.Set<Order>().AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        Assert.Collection(
            saved,
            first =>
            {
                Assert.Equal("O'Brien – 日本", first.Name);
                Assert.Equal(OrderStatus.Placed, first.Status);
                Assert.Equal(12.34m, first.Total);
                Assert.Equal(createdAt, first.CreatedAt);
                Assert.Null(first.Note);
                Assert.Equal(first.Name.Length, first.NameLength);
                Assert.NotEmpty(first.Version);
            },
            second =>
            {
                Assert.Equal("plain", second.Name);
                Assert.Equal("it's fine", second.Note);
            });
        var storedStatuses = await db.Database.SqlQueryRaw<string>("SELECT [Status] AS [Value] FROM [sales].[Orders]").ToListAsync(ct);
        Assert.Contains("Placed", storedStatuses);
    }

    [Fact]
    public async Task KeepIdentityWritesSuppliedIdsOtherwiseTheDatabaseAssignsThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();

        await db.BulkInsertAsync(new[] { new Customer { Id = 1000, Name = "kept" } }, new BulkOptions { KeepIdentity = true }, ct);
        await db.BulkInsertAsync(new[] { new Customer { Id = 1000, Name = "assigned" } }, cancellationToken: ct);

        var ids = await db.Set<Customer>().AsNoTracking().ToDictionaryAsync(x => x.Name, x => x.Id, ct);
        Assert.Equal(1000, ids["kept"]);
        Assert.NotEqual(1000, ids["assigned"]);
    }

    [Fact]
    public async Task RejectsOrphanForeignKeyAndRollsBackEarlierBatches()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        var customer = await AddCustomerAsync(db, "c", ct);
        Order[] orders =
        [
            new() { CustomerId = customer.Id, Name = "a" },
            new() { CustomerId = customer.Id, Name = "b" },
            new() { CustomerId = -1, Name = "orphan" },
        ];

        await Assert.ThrowsAsync<SqlException>(() => db.BulkInsertAsync(orders, new BulkOptions { BatchSize = 1 }, ct));

        var count = await db.Set<Order>().CountAsync(ct);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task CheckConstraintsOffAllowsOrphanForeignKey()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();

        var inserted = await db.BulkInsertAsync(new[] { new Order { CustomerId = -1, Name = "orphan" } }, new BulkOptions { CheckConstraints = false }, ct);

        Assert.Equal(1, inserted);
    }

    [Fact]
    public async Task NonGenericOverloadInsertsObjectsOfTheEntityType()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        IReadOnlyList<object> rows = [new Customer { Name = "a" }, new Customer { Name = "b" }];

        var inserted = await db.BulkInsertAsync(typeof(Customer), rows, cancellationToken: ct);

        Assert.Equal(2, inserted);
    }

    [Fact]
    public async Task WritesToTableWhoseNameContainsBracket()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();

        var inserted = await db.BulkInsertAsync(new[] { new AuditRow { Message = "x" } }, cancellationToken: ct);

        Assert.Equal(1, inserted);
        var count = await db.Set<AuditRow>().CountAsync(ct);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task InsertSqlRunsWithNamedParameters()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();

        var affected = await db.Database.ExecuteSqlRawAsync(db.GetInsertSql<Customer>(), new object[] { new SqlParameter("@Name", "templated") }, ct);

        Assert.Equal(1, affected);
        Assert.True(await db.Set<Customer>().AnyAsync(x => x.Name == "templated", ct));
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/Ntxinh.EFCore.Bulks.Tests --filter "FullyQualifiedName~ValidationTests|FullyQualifiedName~BulkInsertTests"`
Expected: build FAILS with `CS1061: 'TestDbContext' does not contain a definition for 'BulkInsertAsync'` and `CS0246: ... 'BulkOptions' could not be found`.

- [ ] **Step 5: Create `src/Ntxinh.EFCore.Bulks/BulkOptions.cs`**

```csharp
namespace Ntxinh.EFCore.Bulks;

/// <summary>Options for <see cref="BulkExtensions"/> operations.</summary>
public sealed record BulkOptions
{
    /// <summary>Rows sent to SQL Server per batch; 0 (default) sends all rows as one batch. Batches still share one transaction.</summary>
    public int BatchSize { get; init; }

    /// <summary>Timeout for each SQL Server call; <see cref="TimeSpan.Zero"/> means no limit. Default 30 seconds.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Insert the entities' identity values instead of letting SQL Server assign them. Default false.</summary>
    public bool KeepIdentity { get; init; }

    /// <summary>
    /// Enforce CHECK and FOREIGN KEY constraints during the copy. Default true; turning it off is faster
    /// but leaves the table's foreign keys marked untrusted in SQL Server.
    /// </summary>
    public bool CheckConstraints { get; init; } = true;
}
```

- [ ] **Step 6: Create `src/Ntxinh.EFCore.Bulks/Internal/EntityDataReader.cs`**

`SqlBulkCopy` only needs forward-only `Read`/`GetValue`; the typed getters delegate to `GetValue` and byte/char streaming is unsupported (`EnableStreaming` stays off, so `SqlBulkCopy` never asks for it).

```csharp
using System.Collections;
using System.Data.Common;

namespace Ntxinh.EFCore.Bulks.Internal;

/// <summary>Streams entities to SqlBulkCopy one row at a time; no DataTable copy is built.</summary>
internal sealed class EntityDataReader(IEnumerable rows, IReadOnlyList<ColumnMap> columns) : DbDataReader
{
    private readonly IEnumerator rowEnumerator = rows.GetEnumerator();
    private object? current;

    public int RowCount { get; private set; }

    public override int FieldCount => columns.Count;

    public override bool HasRows => true;

    public override bool IsClosed => false;

    public override int RecordsAffected => -1;

    public override int Depth => 0;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        if (!rowEnumerator.MoveNext())
        {
            current = null;
            return false;
        }

        current = rowEnumerator.Current;
        RowCount++;
        return true;
    }

    public override object GetValue(int ordinal) =>
        columns[ordinal].Read(current ?? throw new InvalidOperationException("Read() has not returned a row.")) ?? DBNull.Value;

    public override int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, columns.Count);
        for (var i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }

        return count;
    }

    public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;

    public override string GetName(int ordinal) => columns[ordinal].ColumnName;

    public override int GetOrdinal(string name)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (string.Equals(columns[i].ColumnName, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(name), name, "No such column.");
    }

    public override Type GetFieldType(int ordinal) => columns[ordinal].ProviderType;

    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);

    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);

    public override char GetChar(int ordinal) => (char)GetValue(ordinal);

    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);

    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);

    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);

    public override float GetFloat(int ordinal) => (float)GetValue(ordinal);

    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);

    public override short GetInt16(int ordinal) => (short)GetValue(ordinal);

    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);

    public override long GetInt64(int ordinal) => (long)GetValue(ordinal);

    public override string GetString(int ordinal) => (string)GetValue(ordinal);

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
        throw new NotSupportedException();

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
        throw new NotSupportedException();

    public override bool NextResult() => false;

    public override IEnumerator GetEnumerator() => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            (rowEnumerator as IDisposable)?.Dispose();
        }

        base.Dispose(disposing);
    }
}
```

- [ ] **Step 7: Create `src/Ntxinh.EFCore.Bulks/Internal/BulkExecutor.cs`**

This version always opens its own transaction. Task 5 adds joining the caller's transaction and the execution strategy, driven by failing tests.

```csharp
using System.Collections;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ntxinh.EFCore.Bulks.Internal;

/// <summary>Runs bulk work on the DbContext's own SqlConnection inside a transaction.</summary>
internal static class BulkExecutor
{
    public static async Task<int> RunAsync(
        DbContext db,
        Func<SqlConnection, SqlTransaction, CancellationToken, Task<int>> work,
        CancellationToken cancellationToken)
    {
        if (db.Database.GetDbConnection() is not SqlConnection connection)
        {
            throw new NotSupportedException($"Bulk operations require the SQL Server provider; this DbContext uses {db.Database.ProviderName}.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await work(connection, (SqlTransaction)transaction.GetDbTransaction(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public static async Task<int> CopyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string destination,
        IEnumerable rows,
        IReadOnlyList<ColumnMap> columns,
        SqlBulkCopyOptions flags,
        BulkOptions options,
        CancellationToken cancellationToken)
    {
        using var bulkCopy = new SqlBulkCopy(connection, flags, transaction)
        {
            DestinationTableName = destination,
            BatchSize = options.BatchSize,
            BulkCopyTimeout = (int)options.Timeout.TotalSeconds,
        };
        for (var i = 0; i < columns.Count; i++)
        {
            bulkCopy.ColumnMappings.Add(i, columns[i].ColumnName);
        }

        await using var reader = new EntityDataReader(rows, columns);
        await bulkCopy.WriteToServerAsync(reader, cancellationToken);
        return reader.RowCount;
    }

    public static SqlCommand Command(SqlConnection connection, SqlTransaction transaction, string sql, BulkOptions options)
    {
#pragma warning disable CA2100 // SQL holds only identifiers delimited by TableMap and generated temp names; values never appear in it.
        return new SqlCommand(sql, connection, transaction) { CommandTimeout = (int)options.Timeout.TotalSeconds };
#pragma warning restore CA2100
    }
}
```

- [ ] **Step 8: Create `src/Ntxinh.EFCore.Bulks/BulkExtensions.cs`**

```csharp
using System.Collections;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using Ntxinh.EFCore.Bulks.Internal;

namespace Ntxinh.EFCore.Bulks;

/// <summary>SqlBulkCopy-based writes for EF Core on SQL Server, driven by the DbContext's model.</summary>
/// <remarks>
/// Every operation uses the DbContext's own connection. It joins <c>Database.CurrentTransaction</c> when there is one;
/// otherwise it runs in its own transaction, so either all rows are written or none.
/// </remarks>
public static class BulkExtensions
{
    /// <summary>Inserts <paramref name="entities"/> with SqlBulkCopy and returns the number of rows inserted.</summary>
    /// <remarks>Database-generated identity values are not written back to the entities.</remarks>
    /// <exception cref="ArgumentException"><paramref name="entities"/> contains a null element.</exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> is not mapped to a table.</exception>
    /// <exception cref="NotSupportedException">The mapping or the database provider is not supported.</exception>
    public static Task<int> BulkInsertAsync<T>(
        this DbContext db,
        IEnumerable<T> entities,
        BulkOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : class
        => db.BulkInsertAsync(typeof(T), entities, options, cancellationToken);

    /// <summary>
    /// Inserts <paramref name="entities"/>, each an instance of <paramref name="entityType"/>, with SqlBulkCopy and
    /// returns the number of rows inserted. Use when the type is only known at run time (e.g. queued work).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="entities"/> contains a null element or an element of another type.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="entityType"/> is not mapped to a table.</exception>
    /// <exception cref="NotSupportedException">The mapping or the database provider is not supported.</exception>
    public static async Task<int> BulkInsertAsync(
        this DbContext db,
        Type entityType,
        IEnumerable entities,
        BulkOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(entities);
        options ??= new BulkOptions();

        var map = TableMap.For(db, entityType);
        var rows = Materialize(entities, entityType);
        if (rows.Count == 0)
        {
            return 0;
        }

        var columns = map.InsertColumns(options.KeepIdentity);
        var flags = (options.CheckConstraints ? SqlBulkCopyOptions.CheckConstraints : SqlBulkCopyOptions.Default)
            | (options.KeepIdentity ? SqlBulkCopyOptions.KeepIdentity : SqlBulkCopyOptions.Default);

        return await BulkExecutor.RunAsync(
            db,
            (connection, transaction, ct) => BulkExecutor.CopyAsync(connection, transaction, map.Table, rows, columns, flags, options, ct),
            cancellationToken);
    }

    // One pass before any connection is used: a bad element must fail fast and write nothing.
    private static ICollection Materialize(IEnumerable entities, Type entityType)
    {
        var rows = entities as ICollection ?? entities.Cast<object>().ToList();
        foreach (var row in rows)
        {
            if (row is null)
            {
                throw new ArgumentException("The entities collection contains a null element.", nameof(entities));
            }

            if (!entityType.IsInstanceOfType(row))
            {
                throw new ArgumentException($"The entities collection contains a {row.GetType().Name}; expected {entityType.Name}.", nameof(entities));
            }
        }

        return rows;
    }
}
```

- [ ] **Step 9: Run the unit tests to verify they pass**

Run: `dotnet test tests/Ntxinh.EFCore.Bulks.Tests --filter "FullyQualifiedName~ValidationTests"`
Expected: PASS (4 tests per TFM), each finishing in well under a second (a test that hangs ~15 s means something tried to connect to `Server=unused` — that is a bug).

- [ ] **Step 10: Run the integration tests to verify they pass**

Run: `env DOCKER_HOST=unix:///run/user/1000/podman/podman.sock TESTCONTAINERS_RYUK_DISABLED=true dotnet test tests/Ntxinh.EFCore.Bulks.Tests --filter "FullyQualifiedName~BulkInsertTests"`
Expected: PASS (7 tests per TFM). The first run pulls the SQL Server image.

If the container fails to start under Podman with a socket error, run `systemctl --user start podman.socket` and retry. If `KeepIdentity...` or `Writes...Bracket...` fail, stop and report the exact error — do not change assertions.

- [ ] **Step 11: Verify a clean Release build**

Run: `dotnet build -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 12: Commit**

```bash
git add src/Ntxinh.EFCore.Bulks tests/Ntxinh.EFCore.Bulks.Tests
git commit -m "feat: streaming BulkInsertAsync on the DbContext connection

Entities stream through a DbDataReader into SqlBulkCopy inside a transaction.
CHECK/FK constraints are enforced by default, inputs are validated before any
connection is used, and the call returns the number of rows inserted.

BREAKING CHANGE: BulkInsertAsync no longer takes a connection string or a
DataTable, returns Task<int>, enforces constraints by default (BulkOptions.CheckConstraints)
and throws instead of silently skipping. BulkInsertMultipleTablesAsync, SqlBulkCopyOptionsDto
and DataTableHelper are removed; use a DbContext transaction around several BulkInsertAsync calls."
```

---

### Task 5: Join the caller's transaction and honour the execution strategy

**Files:**
- Modify: `src/Ntxinh.EFCore.Bulks/Internal/BulkExecutor.cs` (method `RunAsync` only)
- Test: `tests/Ntxinh.EFCore.Bulks.Tests/TransactionTests.cs`

**Interfaces:**
- Consumes: `BulkExecutor.RunAsync` (Task 4), `SqlServerFixture` (Task 4), `BulkInsertAsync` (Task 4).
- Produces: same `RunAsync` signature; behaviour: uses `Database.CurrentTransaction` when present (never commits it), otherwise opens and commits its own transaction inside `Database.CreateExecutionStrategy().ExecuteAsync`.

- [ ] **Step 1: Write the tests `tests/Ntxinh.EFCore.Bulks.Tests/TransactionTests.cs`**

```csharp
using System.Data;

using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class TransactionTests(SqlServerFixture fixture) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<int> CustomerCountAsync(CancellationToken ct)
    {
        await using var fresh = fixture.CreateContext();
        return await fresh.Set<Customer>().CountAsync(ct);
    }

    [Fact]
    public async Task CallerTransactionRollbackDiscardsBulkRows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await db.BulkInsertAsync(new[] { new Customer { Name = "rolled back" } }, cancellationToken: ct);
            var visibleInside = await db.Set<Customer>().CountAsync(ct);
            Assert.Equal(1, visibleInside);
            await transaction.RollbackAsync(ct);
        }

        Assert.Equal(0, await CustomerCountAsync(ct));
    }

    [Fact]
    public async Task CallerTransactionCommitKeepsBulkRows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await db.BulkInsertAsync(new[] { new Customer { Name = "kept" } }, cancellationToken: ct);
            await transaction.CommitAsync(ct);
        }

        Assert.Equal(1, await CustomerCountAsync(ct));
    }

    [Fact]
    public async Task RetryingExecutionStrategyIsHonoured()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext(retryOnFailure: true);

        var inserted = await db.BulkInsertAsync(new[] { new Customer { Name = "retry" } }, cancellationToken: ct);

        Assert.Equal(1, inserted);
    }

    // QXM calls bulk inserts from finally blocks after committing or rolling back, before disposing.
    [Fact]
    public async Task StartsOwnTransactionAfterCallerTransactionCompleted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await transaction.RollbackAsync(ct);

        await db.BulkInsertAsync(new[] { new Customer { Name = "after" } }, cancellationToken: ct);

        Assert.Equal(1, await CustomerCountAsync(ct));
    }

    [Fact]
    public async Task LeavesACallerOpenedConnectionOpen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        await db.Database.OpenConnectionAsync(ct);

        await db.BulkInsertAsync(new[] { new Customer { Name = "open" } }, cancellationToken: ct);

        Assert.Equal(ConnectionState.Open, db.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task LeavesAClosedConnectionClosed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();

        await db.BulkInsertAsync(new[] { new Customer { Name = "closed" } }, cancellationToken: ct);

        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
}
```

- [ ] **Step 2: Run the tests to verify the new behaviour is missing**

Run: `env DOCKER_HOST=unix:///run/user/1000/podman/podman.sock TESTCONTAINERS_RYUK_DISABLED=true dotnet test tests/Ntxinh.EFCore.Bulks.Tests --filter "FullyQualifiedName~TransactionTests"`
Expected: FAIL —
- `CallerTransactionRollbackDiscardsBulkRows` and `CallerTransactionCommitKeepsBulkRows` with `InvalidOperationException: The connection is already in a transaction and cannot participate in another transaction.`
- `RetryingExecutionStrategyIsHonoured` with `InvalidOperationException: The configured execution strategy 'SqlServerRetryingExecutionStrategy' does not support user-initiated transactions.`
- The other three pin existing behaviour and pass already.

- [ ] **Step 3: Replace `RunAsync` in `src/Ntxinh.EFCore.Bulks/Internal/BulkExecutor.cs`**

Replace the whole `RunAsync` method and the class summary with:

```csharp
/// <summary>Runs bulk work on the DbContext's own SqlConnection, inside the caller's transaction or an owned one.</summary>
internal static class BulkExecutor
{
    public static async Task<int> RunAsync(
        DbContext db,
        Func<SqlConnection, SqlTransaction, CancellationToken, Task<int>> work,
        CancellationToken cancellationToken)
    {
        if (db.Database.GetDbConnection() is not SqlConnection connection)
        {
            throw new NotSupportedException($"Bulk operations require the SQL Server provider; this DbContext uses {db.Database.ProviderName}.");
        }

        // EF clears CurrentTransaction on commit/rollback, so a finished-but-undisposed caller transaction is not joined.
        if (db.Database.CurrentTransaction is { } callerTransaction)
        {
            return await work(connection, (SqlTransaction)callerTransaction.GetDbTransaction(), cancellationToken);
        }

        // Retrying strategies (EnableRetryOnFailure) reject transactions opened outside the strategy.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(
            async ct =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var result = await work(connection, (SqlTransaction)transaction.GetDbTransaction(), ct);
                await transaction.CommitAsync(ct);
                return result;
            },
            cancellationToken);
    }
```

(`CopyAsync` and `Command` stay as written in Task 4.)

- [ ] **Step 4: Run all integration and unit tests**

Run: `env DOCKER_HOST=unix:///run/user/1000/podman/podman.sock TESTCONTAINERS_RYUK_DISABLED=true dotnet test tests/Ntxinh.EFCore.Bulks.Tests`
Expected: PASS (everything, both TFMs).

- [ ] **Step 5: Commit**

```bash
git add src/Ntxinh.EFCore.Bulks/Internal/BulkExecutor.cs tests/Ntxinh.EFCore.Bulks.Tests/TransactionTests.cs
git commit -m "feat: join the caller's transaction and run inside the execution strategy

BREAKING CHANGE: a bulk write inside an open DbContext transaction now commits or
rolls back with it instead of committing independently on a separate connection."
```

---

### Task 6: `BulkUpdateAsync`

**Files:**
- Modify: `src/Ntxinh.EFCore.Bulks/BulkExtensions.cs` (add `using System.Linq.Expressions;` and the `BulkUpdateAsync` method)
- Test: `tests/Ntxinh.EFCore.Bulks.Tests/BulkUpdateTests.cs`, `tests/Ntxinh.EFCore.Bulks.Tests/ValidationTests.cs` (append three tests)

**Interfaces:**
- Consumes: `TableMap.For`, `TableMap.PrimaryKey`, `TableMap.Table`, `ColumnMap.Delimited` (Task 1); `MemberSelector.Columns` (Task 3); `BulkExecutor.RunAsync/CopyAsync/Command`, `Materialize` (Tasks 4–5).
- Produces: `public static Task<int> BulkUpdateAsync<T>(this DbContext db, IEnumerable<T> entities, Expression<Func<T, object>> set, Expression<Func<T, object>>? matchOn = null, BulkOptions? options = null, CancellationToken cancellationToken = default) where T : class` — returns rows updated.

- [ ] **Step 1: Append the failing validation tests to `tests/Ntxinh.EFCore.Bulks.Tests/ValidationTests.cs`** (inside the class)

```csharp
    [Fact]
    public async Task BulkUpdateOfNothingReturnsZeroWithoutConnecting()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestDbContext.Create();

        Assert.Equal(0, await db.BulkUpdateAsync(Array.Empty<Order>(), set: x => x.Total, cancellationToken: ct));
    }

    [Fact]
    public async Task BulkUpdateOfAKeylessTypeNeedsMatchOn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestDbContext.Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.BulkUpdateAsync(new[] { new AuditRow() }, set: x => x.Message, cancellationToken: ct));
    }

    [Fact]
    public async Task BulkUpdateRejectsAColumnInBothSetAndMatchOn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestDbContext.Create();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => db.BulkUpdateAsync(
            new[] { new Order() },
            set: x => new { x.Total, x.CustomerId },
            matchOn: x => x.CustomerId,
            cancellationToken: ct));
        Assert.Equal("set", error.ParamName);
    }
```

- [ ] **Step 2: Write the failing integration tests `tests/Ntxinh.EFCore.Bulks.Tests/BulkUpdateTests.cs`**

```csharp
using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class BulkUpdateTests(SqlServerFixture fixture) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task<Customer> AddCustomerAsync(TestDbContext db, string name, CancellationToken ct)
    {
        var customer = new Customer { Name = name };
        db.Add(customer);
        await db.SaveChangesAsync(ct);
        return customer;
    }

    private static async Task<List<Order>> OrdersAsync(TestDbContext db, CancellationToken ct) =>
        await db.Set<Order>().AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);

    [Fact]
    public async Task UpdatesOnlySetColumnsMatchedOnPrimaryKey()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        var customer = await AddCustomerAsync(db, "c", ct);
        await db.BulkInsertAsync(
            new[]
            {
                new Order { CustomerId = customer.Id, Name = "a", Total = 1m, Note = "n1" },
                new Order { CustomerId = customer.Id, Name = "b", Total = 2m, Note = "n2" },
            },
            cancellationToken: ct);
        var changes = await OrdersAsync(db, ct);
        foreach (var order in changes)
        {
            order.Total += 100m;
            order.Note = null;
            order.Name = "ignored";
        }

        var updated = await db.BulkUpdateAsync(changes, set: x => new { x.Total, x.Note }, cancellationToken: ct);

        Assert.Equal(2, updated);
        var saved = await OrdersAsync(db, ct);
        Assert.Equal(new[] { "a", "b" }, saved.Select(x => x.Name));
        Assert.Equal(new[] { 101m, 102m }, saved.Select(x => x.Total));
        Assert.All(saved, x => Assert.Null(x.Note));
    }

    [Fact]
    public async Task UpdatesRowsWithCompositeKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        await db.BulkInsertAsync(
            new[]
            {
                new OrderLine { OrderId = 1, LineNo = 1, Price = 10m, Qty = 1 },
                new OrderLine { OrderId = 1, LineNo = 2, Price = 20m, Qty = 1 },
                new OrderLine { OrderId = 2, LineNo = 1, Price = 30m, Qty = 1 },
            },
            cancellationToken: ct);

        var updated = await db.BulkUpdateAsync(new[] { new OrderLine { OrderId = 1, LineNo = 2, Price = 99m } }, set: x => x.Price, cancellationToken: ct);

        Assert.Equal(1, updated);
        var lines = await db.Set<OrderLine>().AsNoTracking().OrderBy(x => x.OrderId).ThenBy(x => x.LineNo).ToListAsync(ct);
        Assert.Equal(new[] { 10m, 99m, 30m }, lines.Select(x => x.Price));
        Assert.All(lines, x => Assert.Equal(1, x.Qty));
    }

    [Fact]
    public async Task MatchOnNonKeyColumnUpdatesEveryMatchingRow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        var a = await AddCustomerAsync(db, "a", ct);
        var b = await AddCustomerAsync(db, "b", ct);
        await db.BulkInsertAsync(
            new[]
            {
                new Order { CustomerId = a.Id, Name = "a1" },
                new Order { CustomerId = a.Id, Name = "a2" },
                new Order { CustomerId = a.Id, Name = "a3" },
                new Order { CustomerId = b.Id, Name = "b1" },
            },
            cancellationToken: ct);

        var updated = await db.BulkUpdateAsync(
            new[] { new Order { CustomerId = a.Id, Note = "bulk" } },
            set: x => x.Note,
            matchOn: x => x.CustomerId,
            cancellationToken: ct);

        Assert.Equal(3, updated);
        var saved = await OrdersAsync(db, ct);
        Assert.Equal(new string?[] { "bulk", "bulk", "bulk", null }, saved.Select(x => x.Note));
    }

    [Fact]
    public async Task NullMatchOnValuesMatchNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        var customer = await AddCustomerAsync(db, "c", ct);
        await db.BulkInsertAsync(
            new[]
            {
                new Order { CustomerId = customer.Id, Name = "a", Note = "x", Total = 1m },
                new Order { CustomerId = customer.Id, Name = "b", Note = null, Total = 2m },
            },
            cancellationToken: ct);

        var updated = await db.BulkUpdateAsync(
            new[] { new Order { Note = "x", Total = 10m }, new Order { Note = null, Total = 20m } },
            set: x => x.Total,
            matchOn: x => x.Note,
            cancellationToken: ct);

        Assert.Equal(1, updated);
        Assert.Equal(new[] { 10m, 2m }, (await OrdersAsync(db, ct)).Select(x => x.Total));
    }

    [Fact]
    public async Task DuplicateMatchOnValuesThrowAndChangeNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        var customer = await AddCustomerAsync(db, "c", ct);
        await db.BulkInsertAsync(new[] { new Order { CustomerId = customer.Id, Name = "a" } }, cancellationToken: ct);

        await Assert.ThrowsAsync<ArgumentException>(() => db.BulkUpdateAsync(
            new[] { new Order { CustomerId = customer.Id, Note = "first" }, new Order { CustomerId = customer.Id, Note = "second" } },
            set: x => x.Note,
            matchOn: x => x.CustomerId,
            cancellationToken: ct));

        Assert.All(await OrdersAsync(db, ct), x => Assert.Null(x.Note));
    }

    [Fact]
    public async Task UpdateRollsBackWithCallerTransaction()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        var customer = await AddCustomerAsync(db, "c", ct);
        await db.BulkInsertAsync(new[] { new Order { CustomerId = customer.Id, Name = "a", Total = 1m } }, cancellationToken: ct);
        var order = Assert.Single(await OrdersAsync(db, ct));

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            order.Total = 50m;
            await db.BulkUpdateAsync(new[] { order }, set: x => x.Total, cancellationToken: ct);
            await transaction.RollbackAsync(ct);
        }

        Assert.Equal(1m, Assert.Single(await OrdersAsync(db, ct)).Total);
    }

    [Fact]
    public async Task RetryingContextCanUpdate()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext(retryOnFailure: true);
        await db.BulkInsertAsync(new[] { new Customer { Name = "before" } }, cancellationToken: ct);
        var customer = await db.Set<Customer>().AsNoTracking().SingleAsync(ct);
        customer.Name = "after";

        var updated = await db.BulkUpdateAsync(new[] { customer }, set: x => x.Name, cancellationToken: ct);

        Assert.Equal(1, updated);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Ntxinh.EFCore.Bulks.Tests --filter "FullyQualifiedName~ValidationTests|FullyQualifiedName~BulkUpdateTests"`
Expected: build FAILS with `CS1061: 'TestDbContext' does not contain a definition for 'BulkUpdateAsync'`.

- [ ] **Step 4: Add `BulkUpdateAsync` to `src/Ntxinh.EFCore.Bulks/BulkExtensions.cs`**

Add `using System.Linq.Expressions;` after `using System.Collections;`. Insert this method after the non-generic `BulkInsertAsync` (before `Materialize`):

```csharp
    /// <summary>
    /// Updates the <paramref name="set"/> columns of existing rows from <paramref name="entities"/>, matching rows on
    /// <paramref name="matchOn"/> (default: the primary key), and returns the number of rows updated.
    /// </summary>
    /// <remarks>
    /// Rows are bulk-copied into a temporary table and applied with one <c>UPDATE … JOIN</c>; values never appear in SQL text.
    /// A <paramref name="matchOn"/> value may match several rows (all are updated); rows whose value is NULL match nothing.
    /// </remarks>
    /// <param name="db">The context whose model and connection are used.</param>
    /// <param name="entities">Rows carrying the match values and the new values.</param>
    /// <param name="set">Columns to update: <c>x =&gt; x.Price</c> or <c>x =&gt; new { x.Price, x.Note }</c>.</param>
    /// <param name="matchOn">Columns that identify the rows to update; default is the primary key.</param>
    /// <param name="options">Batch size and timeout; <see cref="BulkOptions.KeepIdentity"/> and <see cref="BulkOptions.CheckConstraints"/> do not apply.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ArgumentException">
    /// A selector is not a plain property selection, a column is in both <paramref name="set"/> and <paramref name="matchOn"/>,
    /// <paramref name="entities"/> contains a null element, or two entities share a <paramref name="matchOn"/> value.
    /// </exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> is not mapped to a table, or has no primary key and <paramref name="matchOn"/> is null.</exception>
    /// <exception cref="NotSupportedException">The mapping or the database provider is not supported.</exception>
    public static async Task<int> BulkUpdateAsync<T>(
        this DbContext db,
        IEnumerable<T> entities,
        Expression<Func<T, object>> set,
        Expression<Func<T, object>>? matchOn = null,
        BulkOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(set);
        options ??= new BulkOptions();

        var map = TableMap.For(db, typeof(T));
        var setColumns = MemberSelector.Columns(set, map, nameof(set));
        var keyColumns = matchOn is not null
            ? MemberSelector.Columns(matchOn, map, nameof(matchOn))
            : map.PrimaryKey.Count > 0
                ? map.PrimaryKey
                : throw new InvalidOperationException($"{typeof(T).Name} has no primary key; pass matchOn.");
        if (setColumns.Intersect(keyColumns).Any())
        {
            throw new ArgumentException("A column cannot be in both set and matchOn.", nameof(set));
        }

        var rows = Materialize(entities, typeof(T));
        if (rows.Count == 0)
        {
            return 0;
        }

        var temp = $"#bulk_{Guid.NewGuid():N}";
        var columns = keyColumns.Concat(setColumns).ToList();
        var selectList = string.Join(", ", columns.Select(c => c.Delimited));
        var keyList = string.Join(", ", keyColumns.Select(c => c.Delimited));
        var joinOn = string.Join(" AND ", keyColumns.Select(c => $"t.{c.Delimited} = s.{c.Delimited}"));
        var assignments = string.Join(", ", setColumns.Select(c => $"t.{c.Delimited} = s.{c.Delimited}"));

        return await BulkExecutor.RunAsync(
            db,
            async (connection, transaction, ct) =>
            {
                // SELECT INTO copies exact column types and collations. Creating it is transactional,
                // so a rollback on any failure below also removes it.
                await using (var create = BulkExecutor.Command(connection, transaction, $"SELECT TOP 0 {selectList} INTO {temp} FROM {map.Table};", options))
                {
                    await create.ExecuteNonQueryAsync(ct);
                }

                await BulkExecutor.CopyAsync(connection, transaction, temp, rows, columns, SqlBulkCopyOptions.KeepIdentity, options, ct);

                // Checked in SQL so SQL Server's own equality and collation decide what is a duplicate.
                await using (var duplicates = BulkExecutor.Command(connection, transaction, $"SELECT TOP 1 1 FROM {temp} GROUP BY {keyList} HAVING COUNT(*) > 1;", options))
                {
                    if (await duplicates.ExecuteScalarAsync(ct) is not null)
                    {
                        throw new ArgumentException("matchOn values must be unique within entities; SQL Server would apply one of the duplicates at random.");
                    }
                }

                int updated;
                await using (var update = BulkExecutor.Command(connection, transaction, $"UPDATE t SET {assignments} FROM {map.Table} AS t INNER JOIN {temp} AS s ON {joinOn};", options))
                {
                    updated = await update.ExecuteNonQueryAsync(ct);
                }

                await using (var drop = BulkExecutor.Command(connection, transaction, $"DROP TABLE {temp};", options))
                {
                    await drop.ExecuteNonQueryAsync(ct);
                }

                return updated;
            },
            cancellationToken);
    }
```

- [ ] **Step 5: Run the whole suite**

Run: `env DOCKER_HOST=unix:///run/user/1000/podman/podman.sock TESTCONTAINERS_RYUK_DISABLED=true dotnet test tests/Ntxinh.EFCore.Bulks.Tests`
Expected: PASS (everything, both TFMs).

- [ ] **Step 6: Verify a clean Release build**

Run: `dotnet build -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add src/Ntxinh.EFCore.Bulks/BulkExtensions.cs tests/Ntxinh.EFCore.Bulks.Tests/BulkUpdateTests.cs tests/Ntxinh.EFCore.Bulks.Tests/ValidationTests.cs
git commit -m "feat: add BulkUpdateAsync via temp table and UPDATE JOIN

BREAKING CHANGE: replaces GenerateBulkUpdateQuery. Values are no longer
interpolated into SQL; rows match on the primary key unless matchOn is given,
and duplicate matchOn values throw instead of picking the first."
```

---

### Task 7: Documentation, CI and package verification

**Files:**
- Modify: `README.md`, `CLAUDE.md`
- Create: `CHANGELOG.md`, `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: the finished public API (Tasks 2, 4, 6).
- Produces: a packable 2.0 package; CI workflow with jobs `build` and `publish`; publish needs repo secret `NUGET_USER` and a nuget.org trusted-publishing policy (Task 8).

- [ ] **Step 1: Replace `README.md`** (shipped inside the package; links must be absolute)

````markdown
# Ntxinh.EFCore.Bulks

Transaction-aware bulk insert and bulk update for EF Core on SQL Server, built on `SqlBulkCopy` and your EF model.

- NuGet: https://www.nuget.org/packages/Ntxinh.EFCore.Bulks
- Targets `net8.0` (EF Core 8.0.14+) and `net10.0` (EF Core 10.0.0+)
- SQL Server only

```sh
dotnet add package Ntxinh.EFCore.Bulks
```

## Bulk insert

```cs
using Ntxinh.EFCore.Bulks;

var inserted = await db.BulkInsertAsync(orders);                     // rows inserted
await db.BulkInsertAsync(orders, new BulkOptions { BatchSize = 5_000, Timeout = TimeSpan.FromMinutes(2) });
await db.BulkInsertAsync(typeof(Order), rows);                       // type known only at run time
```

- Table, schema, column names and value converters come from the EF model.
- Identity columns are assigned by SQL Server unless `KeepIdentity = true`. Generated values are **not** written back to the entities.
- Computed and rowversion columns are skipped.
- CHECK and FOREIGN KEY constraints are enforced (`CheckConstraints = true` by default).

## Bulk update

```cs
// Match on the primary key, update two columns.
await db.BulkUpdateAsync(orders, set: x => new { x.Total, x.Note });

// Match on another column; every matching row is updated.
await db.BulkUpdateAsync(patches, set: x => x.Note, matchOn: x => x.CustomerId);
```

Rows are bulk-copied into a temporary table and applied with one `UPDATE … JOIN`; values never appear in SQL text.
`matchOn` values must be unique within the input (otherwise `ArgumentException`). Rows whose `matchOn` value is `NULL` match nothing.

## Transactions

- If the DbContext has a transaction open, the operation joins it; you commit or roll back.
- Otherwise each call runs in its own transaction: all rows or none (also across batches).
- Contexts configured with `EnableRetryOnFailure` are supported.
- The DbContext's own connection is used, so Managed Identity / token authentication works and no connection string is needed.

Several tables, all or nothing:

```cs
await using var transaction = await db.Database.BeginTransactionAsync();
await db.BulkInsertAsync(orders);
await db.BulkInsertAsync(lines);
await transaction.CommitAsync();
```

## SQL templates (Dapper)

```cs
var sql = db.GetInsertSql<Order>();
// INSERT INTO [sales].[Orders] ([CreatedAt], [CustomerId], ...) VALUES (@CreatedAt, @CustomerId, ...);
await connection.ExecuteAsync(sql, orders);

var stamped = db.GetInsertSql<Order>(new InsertSqlOptions { UtcNowColumns = ["CreatedAt", "UpdatedAt"] });
// CreatedAt is written as GETUTCDATE(); names an entity lacks are ignored.

var table = db.GetTableName<Order>();   // [sales].[Orders]
```

## Options

| `BulkOptions` | Default | Meaning |
|---|---|---|
| `BatchSize` | `0` | Rows per batch; `0` = one batch |
| `Timeout` | 30 s | Per SQL Server call; `TimeSpan.Zero` = no limit |
| `KeepIdentity` | `false` | Write the entities' identity values (insert only) |
| `CheckConstraints` | `true` | Enforce CHECK/FK constraints (insert only); `false` leaves FKs untrusted |

## Errors

| Situation | Exception |
|---|---|
| Entity not in the model or not mapped to a table | `InvalidOperationException` |
| Keyless entity updated without `matchOn` | `InvalidOperationException` |
| Owned or complex types, inheritance, table splitting, shadow properties | `NotSupportedException` |
| Database provider other than SQL Server | `NotSupportedException` |
| Bad selector, column in both `set` and `matchOn`, null or wrong-typed element, duplicate `matchOn` value | `ArgumentException` |
| Anything SQL Server rejects | `SqlException`, unchanged |

Nothing fails silently: there is no "skip and return" path.

## Upgrading from 8.x / 10.x

2.0 is a breaking rewrite. See the migration table in the [changelog](https://github.com/ntxinh/Ntxinh.EFCore.Bulks/blob/main/CHANGELOG.md).

## Contributing

```sh
dotnet build -c Release          # net8.0 + net10.0, warnings are errors
dotnet test                      # integration tests start SQL Server 2022 with Testcontainers (needs Docker)
```

With Podman instead of Docker:

```sh
env DOCKER_HOST=unix:///run/user/$(id -u)/podman/podman.sock TESTCONTAINERS_RYUK_DISABLED=true dotnet test
```

### Releasing (maintainers)

Releases are published by CI from a tag: `git tag v2.0.1 && git push origin v2.0.1`. Prerelease tags (`v2.1.0-rc.1`) publish prereleases.

One-time setup for NuGet Trusted Publishing (no long-lived API key):

1. nuget.org → your account → **Trusted Publishing** → add a policy: owner `ntxinh`, repository `Ntxinh.EFCore.Bulks`, workflow file `ci.yml`.
2. GitHub → repository → Settings → Secrets → Actions: add `NUGET_USER` = your nuget.org username.
````

- [ ] **Step 2: Create `CHANGELOG.md`**

````markdown
# Changelog

## 2.0.0

A rewrite for production use. **Every public API changed.** Targets net8.0 (EF Core 8.0.14+) and net10.0 (EF Core 10.0.0+).

### Added

- `BulkUpdateAsync<T>(entities, set, matchOn?)` — temp table + `UPDATE … JOIN`; values never appear in SQL text.
- `GetTableName<T>()`.
- `BulkOptions.CheckConstraints` (default `true`) and `InsertSqlOptions.UtcNowColumns`.
- Write methods return the number of rows affected.

### Changed behaviour

- Uses the DbContext's own connection; joins `Database.CurrentTransaction`, otherwise runs in its own transaction inside the execution strategy (`EnableRetryOnFailure` works).
- Throws instead of silently doing nothing (unmapped entity, missing connection string, `Persist Security Info=false`, …).
- CHECK and FOREIGN KEY constraints are enforced by default; set `CheckConstraints = false` for the old behaviour.
- Table names are schema-qualified and delimited (`[sales].[Orders]`); value converters are applied; computed and rowversion columns are skipped.
- `GETUTCDATE()` for `CreatedAt` / `UpdatedAt` / `CreatedOn` / `UpdatedOn` is no longer automatic: list the columns in `InsertSqlOptions.UtcNowColumns`.
- `GetInsertSql` keeps non-identity primary keys (the old generator always dropped the first key column).

### Migration from 8.x / 10.x

| 8.x / 10.x | 2.0 |
|---|---|
| `BulkInsertAsync<T>(data, options, connectionString, ct)` | `BulkInsertAsync<T>(data, options, ct)` |
| `BulkInsertAsync(type, DataTableHelper.CreateDataTable<T>(data), …)` | `BulkInsertAsync(type, data, options, ct)` |
| `BulkInsertMultipleTablesAsync(dtos, …)` | `BeginTransactionAsync` → one `BulkInsertAsync` per table → `CommitAsync` |
| `SqlBulkCopyOptionsDto` | `BulkOptions` (note: `new SqlBulkCopyOptionsDto()` meant `Timeout = 0`, i.e. no limit — use `Timeout = TimeSpan.Zero`) |
| `GenerateInsertQuery<T>()` | `GetInsertSql<T>(new InsertSqlOptions { UtcNowColumns = ["CreatedAt", "UpdatedAt", "CreatedOn", "UpdatedOn"] })` for identical output |
| `GenerateBulkUpdateQuery<T>(rows, [key, col1, col2])` + `ExecuteSqlRawAsync` | `BulkUpdateAsync(rows.DistinctBy(x => x.Key), set: x => new { x.Col1, x.Col2 }, matchOn: x => x.Key)` |
| `GenerateDeleteQuery<T>().Item1` | `$"DELETE FROM {db.GetTableName<T>()}"` or EF `ExecuteDeleteAsync` |
| `GenerateUpdateQuery`, `GenerateSelectQuery`, `GenerateMergeUpsertQuery`, `GenerateCreateTableQuery`, `GenerateDropTableQuery`, `GenerateTruncateTableQuery` | Removed |
| `DataTableHelper`, `SqlBulkCopyHelper`, `ExtractDbContext`, `ObjectExtensions`, `TypeDefaultValue`, `Helpers`, `Constants`, DTO classes | Removed |
````

- [ ] **Step 3: Create `.github/workflows/ci.yml`**

```yaml
name: ci

on:
  push:
    branches: [main]
    tags: ['v*']
  pull_request:

permissions:
  contents: read

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            8.0.x
            10.0.x

      - name: Version
        run: |
          if [[ "$GITHUB_REF" == refs/tags/v* ]]; then
            echo "VERSION=${GITHUB_REF_NAME#v}" >> "$GITHUB_ENV"
          else
            echo "VERSION=0.0.0-ci.${GITHUB_RUN_NUMBER}" >> "$GITHUB_ENV"
          fi

      - name: Build
        run: dotnet build -c Release -p:Version=$VERSION

      - name: Test (SQL Server via Testcontainers)
        run: dotnet test -c Release --no-build

      - name: Pack
        run: dotnet pack src/Ntxinh.EFCore.Bulks -c Release --no-build -p:Version=$VERSION -o artifacts

      - uses: actions/upload-artifact@v4
        with:
          name: packages
          path: artifacts/

  publish:
    if: startsWith(github.ref, 'refs/tags/v')
    needs: build
    runs-on: ubuntu-latest
    permissions:
      contents: read
      id-token: write   # NuGet Trusted Publishing (OIDC)
    steps:
      - uses: actions/download-artifact@v4
        with:
          name: packages
          path: artifacts

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: NuGet login (short-lived key)
        id: login
        uses: NuGet/login@v1
        with:
          user: ${{ secrets.NUGET_USER }}

      - name: Push (the .snupkg next to it is pushed too)
        run: dotnet nuget push "artifacts/*.nupkg" --api-key ${{ steps.login.outputs.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json
```

- [ ] **Step 4: Replace `CLAUDE.md`**

````markdown
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
````

- [ ] **Step 5: Pack and inspect the package**

Run:
```bash
dotnet pack src/Ntxinh.EFCore.Bulks -c Release -p:Version=2.0.0 -o artifacts
unzip -l artifacts/Ntxinh.EFCore.Bulks.2.0.0.nupkg
unzip -p artifacts/Ntxinh.EFCore.Bulks.2.0.0.nupkg Ntxinh.EFCore.Bulks.nuspec
ls artifacts
```
Expected:
- pack succeeds (package validation passes: net8.0 and net10.0 expose the same API);
- the listing contains `lib/net8.0/Ntxinh.EFCore.Bulks.dll`, `lib/net8.0/Ntxinh.EFCore.Bulks.xml`, the same two under `lib/net10.0/`, `README.md`, `LICENSE`, `icon.png`;
- the nuspec has dependency groups `net8.0` → `Microsoft.EntityFrameworkCore.SqlServer` `8.0.14` and `net10.0` → `10.0.0` and **no other dependencies**, plus a `<repository … commit="…">` element (SourceLink);
- `artifacts/Ntxinh.EFCore.Bulks.2.0.0.snupkg` exists.

Then `rm -rf artifacts` (it is build output; confirm `artifacts/` is ignored by `.gitignore` or leave it uncommitted).

- [ ] **Step 6: Full verification**

Run: `dotnet build -c Release && env DOCKER_HOST=unix:///run/user/1000/podman/podman.sock TESTCONTAINERS_RYUK_DISABLED=true dotnet test -c Release --no-build`
Expected: 0 warnings; all tests pass on both TFMs.

- [ ] **Step 7: Commit**

```bash
git add README.md CHANGELOG.md CLAUDE.md .github/workflows/ci.yml
git commit -m "docs: 2.0 README, changelog with migration guide, CI with trusted publishing"
```

---

### Task 8: Ship 2.0.0-rc.1 (user-gated, outward-facing)

**Files:** none (git, GitHub, nuget.org).

**Interfaces:**
- Consumes: branch `feat/v2-rewrite` with Tasks 1–7.
- Produces: `Ntxinh.EFCore.Bulks 2.0.0-rc.1` on nuget.org, consumed by Task 9.

Every step below publishes something. **Ask the user before each one** and stop if they decline.

- [ ] **Step 1: Push the branch and open a PR to `main`** (ask first)

```bash
git push -u origin feat/v2-rewrite
gh pr create --base main --title "2.0: production-ready rewrite" --body "Implements docs/superpowers/specs/2026-10-11-production-ready-rewrite-design.md. Breaking: see CHANGELOG.md."
```
Expected: the `ci / build` check runs and passes (both TFMs, Testcontainers).

- [ ] **Step 2: The user merges the PR** (or approves `gh pr merge --squash --delete-branch`). Then:

```bash
git switch main && git pull --ff-only
```

- [ ] **Step 3: One-time Trusted Publishing setup (the user does this in a browser)**

1. nuget.org → account → **Trusted Publishing** → add policy: owner `ntxinh`, repository `Ntxinh.EFCore.Bulks`, workflow file `ci.yml`.
2. `gh secret set NUGET_USER` (value: the nuget.org username).

- [ ] **Step 4: Tag and push the release candidate** (ask first)

```bash
git tag v2.0.0-rc.1
git push origin v2.0.0-rc.1
gh run watch --exit-status
```
Expected: `build` and `publish` jobs succeed; within ~15 minutes `https://www.nuget.org/packages/Ntxinh.EFCore.Bulks/2.0.0-rc.1` lists the package (prerelease).

If `publish` fails at `NuGet/login`, re-check the policy owner/repo/workflow name; fallback (user decision): store an API key as secret `NUGET_API_KEY` and replace the login step's output with `${{ secrets.NUGET_API_KEY }}`.

---

### Task 9: Migrate `azure_co4_backend` to 2.0.0-rc.1

All paths in this task are relative to `/home/exodia/GitRepos/Company/QXM_QA/azure_co4_backend` (call it `$QXM`). Work in a new worktree so the user's current checkout (`chore/change-order-job-targeting-tests`) is untouched. Line numbers below are from `qa` at planning time — edit by content, not number.

**Files (in `$QXM`):**
- Modify: `Directory.Packages.props` (Ntxinh.EFCore.Bulks version), `Src/QXM.Core/BackgroundTasks/BulkInsertJob.cs`, `Src/QXM.Core/Repositories/{SlabOrderApplicationTotalRepository,TileBidLineTotalRepository,TileBidAppTotalRepository,SlabOrderLineTotalRepository,SlabBidApplicationTotalRepository,SlabOrderTotalRepository,TileBidTotalRepository}.cs`, `Src/QXM.Core/Services/Pricing/{SlabBidLinePricingService,SlabBidPricingService}.cs`, every `packages.lock.json` that restore changes
- Create: `Src/QXM.Core/Utils/QxmBulkExtensions.cs`, `Tests/QXM.Core.UnitTests/BulksMappingTests.cs`
- Temporary (created and deleted in this task): `Tests/QXM.Core.UnitTests/BulksSqlSnapshotTests.cs`

**Interfaces:**
- Consumes: `Ntxinh.EFCore.Bulks 2.0.0-rc.1` — `BulkInsertAsync(Type, IEnumerable, BulkOptions?, CancellationToken)`, `BulkInsertAsync<T>`, `BulkUpdateAsync<T>(entities, set, matchOn, options, ct)`, `GetInsertSql<T>(InsertSqlOptions?)`, `GetTableName<T>()`, `BulkOptions`, `InsertSqlOptions`.
- Produces (QXM, namespace `QXM.Core.Utils`): `QxmBulkExtensions.InsertSql<T>(this DbContext)`, `QxmBulkExtensions.BulkInsertTablesAsync(this DbContext, IEnumerable<(Type EntityType, IEnumerable<object> Rows)>, CancellationToken = default)`; `BulkInsertJob(Guid JobId, Type clrEntityType, IReadOnlyList<object> Data)`.

- [ ] **Step 1: Create a worktree off `qa`**

```bash
QXM=/home/exodia/GitRepos/Company/QXM_QA/azure_co4_backend
git -C $QXM fetch origin qa
git -C $QXM worktree add .worktrees/chore/upgrade-ntxinh-efcore-bulks-2 -b chore/upgrade-ntxinh-efcore-bulks-2 origin/qa
cd $QXM/.worktrees/chore/upgrade-ntxinh-efcore-bulks-2
```

All remaining commands in this task run from that worktree.

- [ ] **Step 2: Snapshot the SQL the current 8.0.14 package generates**

Create `Tests/QXM.Core.UnitTests/BulksSqlSnapshotTests.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ntxinh.EFCore.Bulks;
using QXM.Core.Models.Entities;
using QXM.Core.Providers.AzureSearch.Ef;
using QXM.Core.Providers.Dapper;
using QXM.Core.Providers.HttpContext;
using QXM.Shared.Services;

namespace QXM.Core.UnitTests;

// Temporary: proves 2.0 emits the same Dapper SQL as 8.0.14. Deleted in Task 9 Step 9.
public class BulksSqlSnapshotTests
{
    [Fact]
    public void WriteSnapshot()
    {
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=unused;Database=unused").Options,
            Substitute.For<IHttpContextProvider>(),
            Substitute.For<IBidEfSearchService>(),
            Substitute.For<IDapperProvider>(),
            Substitute.For<ITenantService>(),
            Substitute.For<IMediator>());

        string[] sql =
        [
            db.GenerateInsertQuery<SlabOrderApplicationTotals>(),
            db.GenerateInsertQuery<OrderBillingCode>(),
            db.GenerateInsertQuery<OrderAppItemTotals>(),
            db.GenerateInsertQuery<OrderAppItemOptionAssignmenTotals>(),
            db.GenerateInsertQuery<SlabBidApplicationWeightTotals>(),
            db.GenerateInsertQuery<AppItemTotals>(),
            db.GenerateInsertQuery<SlabOrderApplicationWeightTotals>(),
            db.GenerateInsertQuery<SlabOrderTotals>(),
            db.GenerateInsertQuery<SlabOrderTravel>(),
            db.GenerateInsertQuery<SlabOrderSiteProtectionDetails>(),
            db.GenerateInsertQuery<SlabOrderItemTotals>(),
            db.GenerateInsertQuery<SlabOrderMaterialRemovals>(),
            db.GenerateInsertQuery<SlabOrderMaterialTotals>(),
            db.GenerateDeleteQuery<OrderBillingCode>().Item1,
        ];

        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "bulks-old.sql"), sql.Select(s => s.ReplaceLineEndings(" ")));
    }
}
```

Run: `dotnet test Tests/QXM.Core.UnitTests --filter "FullyQualifiedName~BulksSqlSnapshotTests"`
Expected: PASS; `/tmp/bulks-old.sql` has 14 lines, none empty. An empty line means 8.0.14 silently failed for that type — note which, and report it to the user before continuing.

- [ ] **Step 3: Bump the package and refresh lock files**

In `Directory.Packages.props` change `<PackageVersion Include="Ntxinh.EFCore.Bulks" Version="8.0.14" />` to `Version="2.0.0-rc.1"`. Then:

```bash
dotnet restore QXM.sln --force-evaluate
git status --short   # expect Directory.Packages.props and packages.lock.json files only
```
Expected: restore succeeds with no `NU1605` (the package's EF floor is 8.0.14, equal to QXM's pin).

- [ ] **Step 4: Add `Src/QXM.Core/Utils/QxmBulkExtensions.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Ntxinh.EFCore.Bulks;

namespace QXM.Core.Utils;

/// <summary>QXM conventions on top of Ntxinh.EFCore.Bulks 2.x.</summary>
public static class QxmBulkExtensions
{
    // The 8.x GenerateInsertQuery rewrote these columns to GETUTCDATE(); keep that output.
    private static readonly InsertSqlOptions AuditColumns = new() { UtcNowColumns = ["CreatedAt", "UpdatedAt", "CreatedOn", "UpdatedOn"] };

    // The 8.x multi-table insert passed new SqlBulkCopyOptionsDto(), whose Timeout = 0 meant "no limit".
    private static readonly BulkOptions NoTimeout = new() { Timeout = TimeSpan.Zero };

    /// <summary>Dapper INSERT for <typeparamref name="T"/>, with audit timestamps written as GETUTCDATE().</summary>
    public static string InsertSql<T>(this DbContext db) where T : class => db.GetInsertSql<T>(AuditColumns);

    /// <summary>Inserts into several tables, all or nothing. Replaces BulkInsertMultipleTablesAsync.</summary>
    public static async Task BulkInsertTablesAsync(
        this DbContext db,
        IEnumerable<(Type EntityType, IEnumerable<object> Rows)> tables,
        CancellationToken cancellationToken = default)
    {
        var owned = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            foreach (var (entityType, rows) in tables)
            {
                await db.BulkInsertAsync(entityType, rows, NoTimeout, cancellationToken);
            }

            if (owned is not null)
            {
                await owned.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            if (owned is not null)
            {
                await owned.DisposeAsync();
            }
        }
    }
}
```

- [ ] **Step 5: Mechanical replacements (non-comment lines only)**

```bash
cd Src/QXM.Core
# 15 Dapper insert templates → QXM helper (same SQL as 8.0.14)
sed -i '/^\s*\/\//! s/\.GenerateInsertQuery</.InsertSql</g' \
  Repositories/SlabOrderApplicationTotalRepository.cs Repositories/TileBidLineTotalRepository.cs \
  Repositories/TileBidAppTotalRepository.cs Repositories/SlabOrderLineTotalRepository.cs \
  Repositories/SlabBidApplicationTotalRepository.cs Repositories/SlabOrderTotalRepository.cs
# 4 generic bulk inserts: drop the connection string argument
sed -i '/^\s*\/\//! s/, options: null, connectionString: _configuration\.GetConnectionString(QXM\.Shared\.SqlConst\.Default))/)/' \
  Repositories/TileBidLineTotalRepository.cs Repositories/TileBidAppTotalRepository.cs Repositories/SlabOrderLineTotalRepository.cs
# 9 queued jobs: DataTable snapshot → list copy
sed -i '/^\s*\/\//! { /new BulkInsertJob/ s/DataTableHelper\.CreateDataTable<\w\+>(\(\w\+\))/\1.ToList<object>()/ }' \
  Repositories/TileBidLineTotalRepository.cs Repositories/SlabOrderLineTotalRepository.cs
cd ../..
grep -rnE "GenerateInsertQuery|connectionString: _configuration|new BulkInsertJob" Src/QXM.Core --include=*.cs | grep -vE ":\s*//"
```
Expected from the final grep: only the 9 `new BulkInsertJob(..., <list>.ToList<object>())` lines remain.

`ToList<object>()` copies the list so later changes to the list don't affect the queued job, as the old `DataTable` snapshot didn't. **Check each of the 9 queue sites**: read from the queue call to the end of its method; if a queued entity's *properties* are modified after queuing, stop and report — the snapshot semantics would differ.

Add `using QXM.Core.Utils;` to each of the six repository files edited above if it is not already imported (the build in Step 8 will tell you).

- [ ] **Step 6: Rewrite `BulkInsertJob` and the delete template**

`Src/QXM.Core/BackgroundTasks/BulkInsertJob.cs`:
- change the record to `public record BulkInsertJob(Guid JobId, Type clrEntityType, IReadOnlyList<object> Data) : INotification;`
- replace the insert line with `await _dbContext.BulkInsertAsync(model.clrEntityType, model.Data, cancellationToken: cancellationToken);`
- remove the now-unused `IConfiguration` constructor parameter, the `_configuration` field and `using Microsoft.Extensions.Configuration;`, and `using System.Data;` (only the commented-out block used `DataTable`).

`Src/QXM.Core/Repositories/SlabOrderApplicationTotalRepository.cs`, the `DELETE AND INSERT OrderBillingCode` block:

```csharp
            var sqlDeleteStatement = $"DELETE FROM {AppDbContext.GetTableName<OrderBillingCode>()}";
```

- [ ] **Step 7: Rewrite the multi-table inserts and the bulk updates**

**Multi-table (5 methods):** `SlabBidLinePricingService.UpsertForNow`, `SlabBidLinePricingService.UpsertForLater`, `SlabBidPricingService.UpsertForNow`, `SlabBidPricingService.UpsertForLater`, `SlabBidApplicationTotalRepository.UpsertForNow`. In each, on non-comment lines only:

```csharp
// before
var inputs = new List<BulkInsertMultipleTablesDto>();
// after
var inputs = new List<(Type EntityType, IEnumerable<object> Rows)>();

// before (list argument)
inputs.Add(new BulkInsertMultipleTablesDto
{
    ClrEntityType = typeof(SlabBidLineTotals),
    DataTable = DataTableHelper.CreateDataTable<SlabBidLineTotals>(app_lineTotals),
});
// after
inputs.Add((typeof(SlabBidLineTotals), app_lineTotals));

// before (single entity: bidTotal, bidWeightTotal)
inputs.Add(new BulkInsertMultipleTablesDto
{
    ClrEntityType = typeof(SlabBidTotals),
    DataTable = DataTableHelper.CreateDataTable<SlabBidTotals>(bidTotal),
});
// after
inputs.Add((typeof(SlabBidTotals), new[] { bidTotal }));

// before
await _appDbContext.BulkInsertMultipleTablesAsync(inputs, options: new SqlBulkCopyOptionsDto(), connectionString: _configuration.GetConnectionString(QXM.Shared.SqlConst.Default));
// after (AppDbContext in the repository)
await _appDbContext.BulkInsertTablesAsync(inputs);
```

The live `inputs.Add` calls to convert: `SlabBidLinePricingService` — `SlabBidLineTotals(app_lineTotals)`, `BidLineEdgeProcessTotals(app_edgeProcessTotals)`, `SlabBidLineQtyProcessTotals(app_lineQtyProcessTotals)`, `SlabBidApplicationWeightTotals(applicationWeightTotals)`, `SlabBidLineProcessWeightTotals(bidLineProcessWeightTotals)`; `SlabBidPricingService` — `SlabBidTotals(bidTotal)` single, `BidItemTotals(bidItemTotals)`, `BidSlabTotals(bidSlabTotals)`, `SlabBidWeightTotals(bidWeightTotal)` single; `SlabBidApplicationTotalRepository` — `SlabBidLineProcessTotals(processTotals)`. Leave the commented-out `inputs.Add` blocks as they are. Add `using QXM.Core.Utils;` where needed.

These calls run in `finally` blocks after a committed or rolled-back (not yet disposed) transaction; 2.0 starts its own transaction there (pinned by `TransactionTests.StartsOwnTransactionAfterCallerTransactionCompleted`).

**Bulk updates (4 live sites).** `DistinctBy` keeps the first row per key — what the old `CASE WHEN` chain did.

`SlabBidApplicationTotalRepository` — the two callers and the helper (add `using System.Linq.Expressions;`):

```csharp
        await BulkUpdateLineHeadersAsync(
            result.PriceOverridePatches.Select(x => new SlabBidLineHeaders { BidLineHeaderId = x.AppId, PriceOverride = x.PriceOverride }).ToList(),
            x => x.PriceOverride);
        await BulkUpdateLineHeadersAsync(
            result.PricePerSqFtPatches.Select(x => new SlabBidLineHeaders { BidLineHeaderId = x.AppId, PricePerSqFtOverride = x.PricePerSqFtOverride }).ToList(),
            x => x.PricePerSqFtOverride);
```

```csharp
    private async Task BulkUpdateLineHeadersAsync(List<SlabBidLineHeaders> rows, Expression<Func<SlabBidLineHeaders, object>> set)
    {
        if (rows.Count == 0) return;

        try
        {
            await AppDbContext.BulkUpdateAsync(rows.DistinctBy(x => x.BidLineHeaderId), set, matchOn: x => x.BidLineHeaderId);
        }
        catch (Exception ex)
        {
            _appInsightService.TrackException(ex);
        }
    }
```

`TileBidAppTotalRepository.PatchPriceOverridesAsync`:

```csharp
        protected virtual async Task PatchPriceOverridesAsync(List<SlabBidLineHeaders> rows)
        {
            if (rows.Count == 0) return;

            await AppDbContext.BulkUpdateAsync(rows.DistinctBy(x => x.BidLineHeaderId), set: x => x.PriceOverride, matchOn: x => x.BidLineHeaderId);
        }
```

`TileBidTotalRepository` (non-PK match: `SlabBidHeaderTotals` key is `SlabBidHeaderTotalsId`):

```csharp
            if (appTotals.Any())
            {
                var affectedRows = await AppDbContext.BulkUpdateAsync(
                    appTotals
                        .Select(x => new SlabBidHeaderTotals
                        {
                            SlabBidHeaderId = x.AppId,
                            DistributedPrice = x.DistributedPrice,
                            BidLevelCharge = x.BidLevelCharge,
                            BidLevelCost = x.BidLevelCost,
                        })
                        .DistinctBy(x => x.SlabBidHeaderId),
                    set: x => new { x.DistributedPrice, x.BidLevelCharge, x.BidLevelCost },
                    matchOn: x => x.SlabBidHeaderId);
            }
```

`SlabOrderTotalRepository` (non-PK match: `SlabOrderApplicationTotals` key is `Id`):

```csharp
            if (appTotals.Any())
            {
                var affectedRows = await AppDbContext.BulkUpdateAsync(
                    appTotals
                        .Select(x => new SlabOrderApplicationTotals
                        {
                            OrderApplicationId = x.AppId,
                            DistributedPrice = x.DistributedPrice,
                        })
                        .DistinctBy(x => x.OrderApplicationId),
                    set: x => x.DistributedPrice,
                    matchOn: x => x.OrderApplicationId);
            }
```

Behaviour note for the PR description: the old generator emitted `THEN ` (a SQL syntax error) for a `null` value, so clearing a price override never worked; 2.0 writes `NULL`.

- [ ] **Step 8: Build**

Run: `dotnet build QXM.sln`
Expected: builds with no new warnings or errors. Remaining references to `DataTableHelper`, `BulkInsertMultipleTablesDto`, `SqlBulkCopyOptionsDto` or `GenerateBulkUpdateQuery` outside comments are compile errors here — convert them with the patterns in Step 7.

- [ ] **Step 9: Compare the new SQL with the snapshot, then replace the snapshot test with a permanent mapping test**

Edit `Tests/QXM.Core.UnitTests/BulksSqlSnapshotTests.cs`: replace every `db.GenerateInsertQuery<X>()` with `db.InsertSql<X>()`, the last entry with `$"DELETE FROM {db.GetTableName<OrderBillingCode>()};"`, `bulks-old.sql` with `bulks-new.sql`, and add `using QXM.Core.Utils;`. Run it, then compare with table names normalised:

```bash
dotnet test Tests/QXM.Core.UnitTests --filter "FullyQualifiedName~BulksSqlSnapshotTests"
diff <(sed -E 's/(INSERT INTO|DELETE FROM) \S+/\1 T/' /tmp/bulks-old.sql) \
     <(sed -E 's/(INSERT INTO|DELETE FROM) \S+/\1 T/' /tmp/bulks-new.sql) && echo IDENTICAL
diff /tmp/bulks-old.sql /tmp/bulks-new.sql   # shows only table-name delimiting/schema
```
Expected: `IDENTICAL`; the raw diff differs only in table names (`tblX` → `[tblX]` or `[dbo].[tblX]`). **Any difference in a column list or VALUES list (e.g. a primary key the old generator dropped) → stop and report to the user**: that type's Dapper insert would behave differently.

Delete the snapshot test and add the permanent `Tests/QXM.Core.UnitTests/BulksMappingTests.cs`:

```bash
rm Tests/QXM.Core.UnitTests/BulksSqlSnapshotTests.cs   # never committed
```

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ntxinh.EFCore.Bulks;
using QXM.Core.Models.Entities;
using QXM.Core.Providers.AzureSearch.Ef;
using QXM.Core.Providers.Dapper;
using QXM.Core.Providers.HttpContext;
using QXM.Core.Utils;
using QXM.Shared.Services;

namespace QXM.Core.UnitTests;

/// <summary>
/// Ntxinh.EFCore.Bulks rejects mappings it cannot write (shadow properties, owned types, inheritance) with
/// NotSupportedException at call time. These tests surface that at build time for every entity QXM bulk-writes.
/// </summary>
public class BulksMappingTests
{
    private static AppDbContext SqlServerModel() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=unused;Database=unused").Options,
        Substitute.For<IHttpContextProvider>(),
        Substitute.For<IBidEfSearchService>(),
        Substitute.For<IDapperProvider>(),
        Substitute.For<ITenantService>(),
        Substitute.For<IMediator>());

    [Fact]
    public void EveryBulkInsertedOrUpdatedEntityIsSupported()
    {
        using var db = SqlServerModel();

        string[] tables =
        [
            db.GetTableName<SlabBidLineTotals>(),
            db.GetTableName<TileProcessPieceTotals>(),
            db.GetTableName<TileProcessTrimEdgeTotals>(),
            db.GetTableName<TileProcessTrimPartTotals>(),
            db.GetTableName<SlabBidLineProcessTotals>(),
            db.GetTableName<BidLineEdgeProcessTotals>(),
            db.GetTableName<SlabBidLineProcessWeightTotals>(),
            db.GetTableName<SlabBidLineQtyProcessTotals>(),
            db.GetTableName<SlabBidApplicationWeightTotals>(),
            db.GetTableName<SlabBidHeaderTotals>(),
            db.GetTableName<SlabBidTotals>(),
            db.GetTableName<BidItemTotals>(),
            db.GetTableName<BidSlabTotals>(),
            db.GetTableName<SlabBidWeightTotals>(),
            db.GetTableName<SlabOrderLineQtyProcessTotals>(),
            db.GetTableName<SlabOrderLineTotals>(),
            db.GetTableName<SlabOrderProcessTotals>(),
            db.GetTableName<OrderLineEdgeProcessTotals>(),
            db.GetTableName<SlabOrderProcessWeightTotals>(),
            db.GetTableName<SlabBidLineHeaders>(),
            db.GetTableName<SlabOrderApplicationTotals>(),
            db.GetTableName<OrderBillingCode>(),
        ];

        tables.ShouldAllBe(t => t.StartsWith("["));
    }

    [Fact]
    public void EveryDapperInsertTemplateBuilds()
    {
        using var db = SqlServerModel();

        string[] sql =
        [
            db.InsertSql<SlabOrderApplicationTotals>(),
            db.InsertSql<OrderBillingCode>(),
            db.InsertSql<OrderAppItemTotals>(),
            db.InsertSql<OrderAppItemOptionAssignmenTotals>(),
            db.InsertSql<SlabBidApplicationWeightTotals>(),
            db.InsertSql<AppItemTotals>(),
            db.InsertSql<SlabOrderApplicationWeightTotals>(),
            db.InsertSql<SlabOrderTotals>(),
            db.InsertSql<SlabOrderTravel>(),
            db.InsertSql<SlabOrderSiteProtectionDetails>(),
            db.InsertSql<SlabOrderItemTotals>(),
            db.InsertSql<SlabOrderMaterialRemovals>(),
            db.InsertSql<SlabOrderMaterialTotals>(),
        ];

        sql.ShouldAllBe(s => s.StartsWith("INSERT INTO ["));
    }
}
```

If `EveryBulkInsertedOrUpdatedEntityIsSupported` throws `NotSupportedException` for a type, **stop and report** the type and reason to the user (8.x silently skipped such columns; 2.0 refuses them) — do not work around it.

- [ ] **Step 10: Run the QXM.Core unit tests**

Run: `dotnet test Tests/QXM.Core.UnitTests`
Expected: all pass, including `BulksMappingTests` and `SlabBidApplicationTotalRepositoryCharacterizationTests.CalculateBidApplicationTotals_HandsEveryItemRowToTheItemInsert` (Review Focus 5: `InsertSql` on the InMemory model must not throw).

- [ ] **Step 11: Commit in the QXM worktree**

```bash
git add -A
git commit -m "chore: upgrade Ntxinh.EFCore.Bulks to 2.0.0-rc.1

- Dapper insert templates via QxmBulkExtensions.InsertSql (same SQL as 8.0.14)
- multi-table inserts via BulkInsertTablesAsync (all or nothing, no timeout as before)
- bulk updates via BulkUpdateAsync with explicit matchOn (parameter-free; NULL now writes NULL)
- BulkInsertJob carries entities instead of a DataTable
- BulksMappingTests guard every bulk-written entity's mapping

Runtime changes: FK/CHECK constraints are now enforced on bulk inserts; bulk
operations use the DbContext connection and throw instead of silently skipping."
```

- [ ] **Step 12: Push and open the PR to `qa`** (ask the user first; they may prefer their `/open-pr` skill)

```bash
git push -u origin chore/upgrade-ntxinh-efcore-bulks-2
gh pr create --base qa --title "chore: upgrade Ntxinh.EFCore.Bulks to 2.0.0-rc.1" --body-file <(git log -1 --format=%B)
```

- [ ] **Step 13: QA verification (the user, on the QA environment after merge to `qa`)**

1. Before deploying: for 5 bids and 5 orders, record row counts and sums of `DistributedPrice`, `BidLevelCharge`, `BidLevelCost` from the totals tables touched above.
2. Deploy; re-run bid and order pricing recalculation for the same bids/orders; compare.
3. Watch App Insights for 48 h for exceptions from the bulk paths. Expected new surface: `SqlException` FK violations (previously silent orphan inserts) and `NotSupportedException` (none, if `BulksMappingTests` passed). Each FK violation is a pre-existing data bug — report, don't suppress with `CheckConstraints = false` without the user's decision.

---

### Task 10: Release 2.0.0

**Files:** `src/Ntxinh.EFCore.Bulks/Ntxinh.EFCore.Bulks.csproj` (library); `$QXM/Directory.Packages.props` (consumer).

**Interfaces:**
- Consumes: QA sign-off from Task 9 Step 13.
- Produces: `Ntxinh.EFCore.Bulks 2.0.0` on nuget.org; package-validation baseline for future releases.

- [ ] **Step 1: Tag 2.0.0 on the rc commit** (ask first; only after QA sign-off; if fixes were needed, release another rc first)

```bash
cd /home/exodia/GitRepos/MyGits/Ntxinh.EFCore.Bulks
git switch main && git pull --ff-only
git tag v2.0.0 && git push origin v2.0.0
gh run watch --exit-status
```
Expected: `https://www.nuget.org/packages/Ntxinh.EFCore.Bulks/2.0.0` is listed.

- [ ] **Step 2: Set the package-validation baseline**

In `src/Ntxinh.EFCore.Bulks/Ntxinh.EFCore.Bulks.csproj`, under `<EnablePackageValidation>true</EnablePackageValidation>` add:

```xml
    <PackageValidationBaselineVersion>2.0.0</PackageValidationBaselineVersion>
```

Run: `dotnet pack src/Ntxinh.EFCore.Bulks -c Release -p:Version=2.0.1-dev -o /tmp/bulks-pack && rm -rf /tmp/bulks-pack`
Expected: pack succeeds (no API difference from the 2.0.0 baseline).

```bash
git switch -c chore/package-validation-baseline
git add src/Ntxinh.EFCore.Bulks/Ntxinh.EFCore.Bulks.csproj
git commit -m "build: validate future packages against the 2.0.0 baseline"
```
Push and open a PR to `main` (ask first).

- [ ] **Step 3: Move QXM from rc to 2.0.0** (in a new QXM branch off `qa`, ask before pushing)

Change `Version="2.0.0-rc.1"` to `Version="2.0.0"` in `$QXM/Directory.Packages.props`, run `dotnet restore QXM.sln --force-evaluate`, `dotnet test Tests/QXM.Core.UnitTests`, commit `chore: Ntxinh.EFCore.Bulks 2.0.0`, and open a PR to `qa`.
