# EfCore.StorageEstimator

[![CI](https://github.com/Alos-no/EfCore.StorageEstimator/actions/workflows/CI.yml/badge.svg)](https://github.com/Alos-no/EfCore.StorageEstimator/actions/workflows/CI.yml)
[![NuGet](https://img.shields.io/nuget/v/EfCore.StorageEstimator?color=27ae60)](https://www.nuget.org/packages/EfCore.StorageEstimator/)
[![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-27ae60)](LICENSE)

`EfCore.StorageEstimator` estimates PostgreSQL heap, index, and traversal growth from EF Core models plus explicit workload assumptions.

It is built for production prognostics: estimating production volumes, row growth, and storage growth so teams can shape and refine schemas early, size infrastructure, and anticipate operational needs before production launch.

> *Plan capacity deliberately. Do not leave production growth to chance.*

## What The Project Contains

| Package / Tool | Purpose |
|---|---|
| `EfCore.StorageEstimator.Contracts` | Planning attributes and report/request DTOs. |
| `EfCore.StorageEstimator` | Runtime estimator, EF Core schema reader, PostgreSQL sizing math, and renderers. |
| `EfCore.StorageEstimator.Analyzers` | Roslyn diagnostics that validate annotation usage. |

The repository multi-targets `.NET 8`, `.NET 9`, and `.NET 10` in [Directory.Build.props](Directory.Build.props).

## Core Model

The estimator combines two input sources:

- Schema facts from EF Core `IModel` when you supply `StorageEstimateRequest.Model`.
- Planning facts from attributes declared in [StoragePlanningAttributes.cs](src/EfCore.StorageEstimator.Contracts/Planning/StoragePlanningAttributes.cs).

The runtime starts from one or more `StorageTraversalRoot` values in [StorageEstimateRequest.cs](src/EfCore.StorageEstimator.Contracts/Estimation/StorageEstimateRequest.cs), resolves a row count for each root, estimates the root table, then traverses only navigations explicitly marked with `[StorageNavigation(...)]` in [StorageEstimator.cs](src/EfCore.StorageEstimator/StorageEstimator.cs).

Traversal rules:

- `[StorageEntity(expectedRowCount)]` defines the default row count when a type is used as a root.
- `[StorageField(fillRate, AverageLength = ...)]` refines nullable fill rates and average payload sizes.
- `[StorageNavigation(averageMultiplicity)]` declares that a navigation should be traversed and by how much it expands rows.
- `[StorageNavigation(..., SuppressCycleWarning = true)]` marks an intentional cycle.
- `[StorageTraversalBoundary]` marks an intentional stop so the estimator does not warn about an undefined branch.

If no EF Core model is supplied, the runtime falls back to CLR reflection. If a model is supplied, it uses EF Core metadata for table names, property store types, indexes, and navigations.

## Installation

Use the packages by role:

```bash
dotnet add package EfCore.StorageEstimator.Contracts
dotnet add package EfCore.StorageEstimator.Analyzers
```

For a dedicated estimate runner:

```bash
dotnet add package EfCore.StorageEstimator
```

| Project role | Package | Why |
|---|---|---|
| Domain / model project | `EfCore.StorageEstimator.Contracts` | Brings in the planning attributes and DTO contracts only. |
| Domain / model project | `EfCore.StorageEstimator.Analyzers` | Adds Roslyn diagnostics for invalid estimator metadata. |
| Estimate runner / tooling host | `EfCore.StorageEstimator` | Brings in the runtime estimator, schema reader, sizing logic, and renderers. |

Production application code should reference `EfCore.StorageEstimator.Contracts` and the analyzer. The full `EfCore.StorageEstimator` package is for the process that actually executes estimates.

## Quick Start

Programmatic use is through `IStorageEstimator` in [IStorageEstimator.cs](src/EfCore.StorageEstimator/IStorageEstimator.cs) and DI registration in [ServiceCollectionExtensions.cs](src/EfCore.StorageEstimator/ServiceCollectionExtensions.cs). This belongs in a dedicated estimate runner or analysis host, not in the normal domain model project that only declares planning metadata.

```csharp
using EfCore.StorageEstimator;
using EfCore.StorageEstimator.Estimation;
using EfCore.StorageEstimator.Planning;
using EfCore.StorageEstimator.Rendering;
using Microsoft.Extensions.DependencyInjection;

// Register the estimator runtime in a dedicated analysis process.
var services = new ServiceCollection()
  .AddStorageEstimator()
  .BuildServiceProvider();

// Resolve the runtime service that executes estimate requests.
var estimator = services.GetRequiredService<IStorageEstimator>();

// Build an estimate request from an EF Core model plus one or more traversal roots.
var report = estimator.Estimate(new StorageEstimateRequest
{
  // Supply the compiled EF Core model so table names, store types, and indexes come from real metadata.
  Model = dbContext.Model,
  Roots =
  [
    // Root 1: use the default row count declared on the entity via [StorageEntity(...)].
    new StorageTraversalRoot(typeof(Project)),

    // Root 2: override the default root row count and give the root a friendlier display label.
    new StorageTraversalRoot(typeof(ExternalShare))
    {
      EntityCountOverride = 5_000,
      Label = "Public Sharing"
    }
  ]
});

// Render the structured report to Markdown for human-readable output.
var markdown = new MarkdownReportRenderer().Render(report);
Console.WriteLine(markdown);
```

Minimal annotated model:

```csharp
using EfCore.StorageEstimator.Planning;

// Declares the default root row count when Project is selected as a traversal root.
[StorageEntity(12)]
public sealed class Project
{
  // Provides an average payload length for a variable-width column.
  [StorageField(AverageLength = 64)]
  public string Name { get; set; } = string.Empty;

  // Declares that traversal should expand into Tasks with an average multiplicity of 8 per project.
  [StorageNavigation(8)]
  public IReadOnlyList<TaskItem> Tasks { get; } = [];
}

// Declares the default root row count if TaskItem is ever estimated directly.
[StorageEntity(1)]
public sealed class TaskItem
{
  // Nullable variable-width field with a 50% fill rate and an average payload length of 120 bytes.
  [StorageField(0.5d, AverageLength = 120)]
  public string? Notes { get; set; }
}
```

## Using EF Core Models

Model-backed estimation is the preferred mode because it uses EF Core metadata instead of guessing from CLR types.

What the runtime reads from the EF Core model:

- table names
- store types
- nullability
- max length / precision / scale
- indexes
- navigations

This is implemented through [EfCoreSchemaReader.cs](src/EfCore.StorageEstimator/Schema/EfCoreSchemaReader.cs) and used by [StorageEstimator.cs](src/EfCore.StorageEstimator/StorageEstimator.cs) and [PostgreSqlStorageMath.cs](src/EfCore.StorageEstimator/Estimation/PostgreSqlStorageMath.cs).

## Warnings

Warnings are part of normal operation. They tell you where the estimate is falling back to generic assumptions.

Typical warnings:

- undefined branch warning: a navigation points to a known entity type but is not marked with `[StorageNavigation]`
- cycle warning: traversal would revisit an entity type already on the current path
- fallback fill rate warning: nullable fixed-width property has no `[StorageField]`
- default average length warning: variable-width property has no `AverageLength`

The runtime emits these in `StorageEstimateReport.Warnings`. Use them to tighten annotations until your estimate is explicit enough for your scenario.

## Analyzer Rules

The analyzer package validates common annotation mistakes in [StoragePlanningAnalyzer.cs](src/EfCore.StorageEstimator.Analyzers/StoragePlanningAnalyzer.cs).

Current rules:

| Rule | Meaning |
|---|---|
| `EFSA001` | `[StorageNavigation]` targets a type that is not annotated with `[StorageEntity]`. |
| `EFSA002` | `[StorageEntity]` row count must be greater than zero. |
| `EFSA003` | `[StorageField]` fill rate must be between `0` and `1`. |
| `EFSA004` | `[StorageField(AverageLength = ...)]` must be zero or greater. |
| `EFSA005` | `[StorageNavigation]` multiplicity must be greater than zero. |

## Attribute Emission Switch

Planning attributes are emitted by default. Consumers can disable emission by setting `EfCoreStorageEstimatorEmitPlanningAttributes=false`.

This is intended for release builds where the model project should keep the contracts package for source compatibility but strip the storage-planning annotations from the compiled binary.

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <EfCoreStorageEstimatorEmitPlanningAttributes>false</EfCoreStorageEstimatorEmitPlanningAttributes>
</PropertyGroup>
```

With that switch disabled, usages of `[StorageEntity]`, `[StorageField]`, `[StorageNavigation]`, and `[StorageTraversalBoundary]` are compiled away from the consuming assembly.

This is wired through [Directory.Build.props](Directory.Build.props) and the transitive props file [EfCore.StorageEstimator.Contracts.props](src/EfCore.StorageEstimator.Contracts/buildTransitive/EfCore.StorageEstimator.Contracts.props). For how `buildTransitive` assets flow through NuGet `PackageReference`, see the official NuGet/MSBuild docs: <https://learn.microsoft.com/en-us/nuget/concepts/msbuild-props-and-targets>.

## Repository Layout

```text
EfCore.StorageEstimator/
|-- src/
|   |-- EfCore.StorageEstimator/
|   |-- EfCore.StorageEstimator.Contracts/
|   `-- EfCore.StorageEstimator.Analyzers/
|-- tests/
|   |-- EfCore.StorageEstimator.Tests/
|   `-- EfCore.StorageEstimator.Analyzers.Tests/
|-- samples/
|   `-- EfCore.StorageEstimator.Sample/
|-- FEAT-fleet-storage-cost-analyzer.md
|-- CLAUDE.md
`-- LIBRARY-STANDARD.md
```

## Build And Test

```bash
dotnet restore EfCore.StorageEstimator.slnx
dotnet build EfCore.StorageEstimator.slnx
dotnet test EfCore.StorageEstimator.slnx
```

The sample application is in [Program.cs](samples/EfCore.StorageEstimator.Sample/Program.cs).

## Recommendations

- Keep root row counts on root entities, and use `EntityCountOverride` only for scenario-specific runs.
- Treat warnings as missing planning data, not as noise.

## License

Apache 2.0. See [LICENSE](LICENSE).
