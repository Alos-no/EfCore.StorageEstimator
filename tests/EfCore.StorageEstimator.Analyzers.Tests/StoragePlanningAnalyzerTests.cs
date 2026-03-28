namespace EfCore.StorageEstimator.Analyzers.Tests;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>Unit tests for <see cref="StoragePlanningAnalyzer" />.</summary>
[Trait("Category", "Unit")]
public sealed class StoragePlanningAnalyzerTests
{
  #region Analyze

  [Fact]
  public async Task Analyze_WithUnplannedNavigationTarget_ReportsDiagnostic()
  {
    // Arrange
    const string source = """
                          using System;
                          using System.Collections.Generic;
                          using EfCore.StorageEstimator.Planning;

                          namespace EfCore.StorageEstimator.Planning
                          {
                            [AttributeUsage(AttributeTargets.Class)]
                            public sealed class StorageEntityAttribute : Attribute
                            {
                              public StorageEntityAttribute(double expectedRowCount) { }
                            }

                            [AttributeUsage(AttributeTargets.Property)]
                            public sealed class StorageNavigationAttribute : Attribute
                            {
                              public StorageNavigationAttribute(double averageMultiplicity) { }
                            }
                          }

                          [StorageEntity(1)]
                          public sealed class FleetRoot
                          {
                            [StorageNavigation(2)]
                            public List<Aircraft> Aircraft { get; } = new List<Aircraft>();
                          }

                          public sealed class Aircraft
                          {
                          }
                          """;

    // Act
    var diagnostics = await GetDiagnosticsAsync(source);

    // Assert
    var diagnostic = Assert.Single(diagnostics);
    Assert.Equal("EFSA001", diagnostic.Id);
    Assert.Contains("Aircraft", diagnostic.GetMessage());
    Assert.Contains("not annotated with [StorageEntity]", diagnostic.GetMessage());
  }


  [Fact]
  public async Task Analyze_WithPlannedNavigationTarget_DoesNotReportDiagnostic()
  {
    // Arrange
    const string source = """
                          using System;
                          using System.Collections.Generic;
                          using EfCore.StorageEstimator.Planning;

                          namespace EfCore.StorageEstimator.Planning
                          {
                            [AttributeUsage(AttributeTargets.Class)]
                            public sealed class StorageEntityAttribute : Attribute
                            {
                              public StorageEntityAttribute(double expectedRowCount) { }
                            }

                            [AttributeUsage(AttributeTargets.Property)]
                            public sealed class StorageNavigationAttribute : Attribute
                            {
                              public StorageNavigationAttribute(double averageMultiplicity) { }
                            }
                          }

                          [StorageEntity(1)]
                          public sealed class FleetRoot
                          {
                            [StorageNavigation(2)]
                            public List<Aircraft> Aircraft { get; } = new List<Aircraft>();
                          }

                          [StorageEntity(1)]
                          public sealed class Aircraft
                          {
                          }
                          """;

    // Act
    var diagnostics = await GetDiagnosticsAsync(source);

    // Assert
    Assert.Empty(diagnostics);
  }


  [Fact]
  public async Task Analyze_WithNonPositiveExpectedRowCount_ReportsDiagnostic()
  {
    // Arrange
    const string source = """
                          using System;
                          using EfCore.StorageEstimator.Planning;

                          namespace EfCore.StorageEstimator.Planning
                          {
                            [AttributeUsage(AttributeTargets.Class)]
                            public sealed class StorageEntityAttribute : Attribute
                            {
                              public StorageEntityAttribute(double expectedRowCount) { }
                            }
                          }

                          [StorageEntity(0)]
                          public sealed class FleetRoot
                          {
                          }
                          """;

    // Act
    var diagnostics = await GetDiagnosticsAsync(source);

    // Assert
    var diagnostic = Assert.Single(diagnostics);
    Assert.Equal("EFSA002", diagnostic.Id);
    Assert.Contains("expected row count", diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
  }


  [Fact]
  public async Task Analyze_WithOutOfRangeFillRate_ReportsDiagnostic()
  {
    // Arrange
    const string source = """
                          using System;
                          using EfCore.StorageEstimator.Planning;

                          namespace EfCore.StorageEstimator.Planning
                          {
                            [AttributeUsage(AttributeTargets.Property)]
                            public sealed class StorageFieldAttribute : Attribute
                            {
                              public StorageFieldAttribute(double fillRate = 1.0d) { }

                              public int AverageLength { get; init; }
                            }
                          }

                          public sealed class FleetRoot
                          {
                            [StorageField(1.5d)]
                            public string? SerialNumber { get; init; }
                          }
                          """;

    // Act
    var diagnostics = await GetDiagnosticsAsync(source);

    // Assert
    var diagnostic = Assert.Single(diagnostics);
    Assert.Equal("EFSA003", diagnostic.Id);
    Assert.Contains("fill rate", diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
  }


  [Fact]
  public async Task Analyze_WithNegativeAverageLength_ReportsDiagnostic()
  {
    // Arrange
    const string source = """
                          using System;
                          using EfCore.StorageEstimator.Planning;

                          namespace EfCore.StorageEstimator.Planning
                          {
                            [AttributeUsage(AttributeTargets.Property)]
                            public sealed class StorageFieldAttribute : Attribute
                            {
                              public StorageFieldAttribute(double fillRate = 1.0d) { }

                              public int AverageLength { get; init; }
                            }
                          }

                          public sealed class FleetRoot
                          {
                            [StorageField(AverageLength = -1)]
                            public string? SerialNumber { get; init; }
                          }
                          """;

    // Act
    var diagnostics = await GetDiagnosticsAsync(source);

    // Assert
    var diagnostic = Assert.Single(diagnostics);
    Assert.Equal("EFSA004", diagnostic.Id);
    Assert.Contains("AverageLength", diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
  }


  [Fact]
  public async Task Analyze_WithNonPositiveAverageMultiplicity_ReportsDiagnostic()
  {
    // Arrange
    const string source = """
                          using System;
                          using EfCore.StorageEstimator.Planning;

                          namespace EfCore.StorageEstimator.Planning
                          {
                            [AttributeUsage(AttributeTargets.Class)]
                            public sealed class StorageEntityAttribute : Attribute
                            {
                              public StorageEntityAttribute(double expectedRowCount) { }
                            }

                            [AttributeUsage(AttributeTargets.Property)]
                            public sealed class StorageNavigationAttribute : Attribute
                            {
                              public StorageNavigationAttribute(double averageMultiplicity) { }
                            }
                          }

                          [StorageEntity(1)]
                          public sealed class FleetRoot
                          {
                            [StorageNavigation(0)]
                            public Aircraft Aircraft { get; init; } = new Aircraft();
                          }

                          [StorageEntity(1)]
                          public sealed class Aircraft
                          {
                          }
                          """;

    // Act
    var diagnostics = await GetDiagnosticsAsync(source);

    // Assert
    var diagnostic = Assert.Single(diagnostics);
    Assert.Equal("EFSA005", diagnostic.Id);
    Assert.Contains("average multiplicity", diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
  }


  [Fact]
  public async Task Analyze_WithValidPlanningValues_DoesNotReportDiagnostic()
  {
    // Arrange
    const string source = """
                          using System;
                          using System.Collections.Generic;
                          using EfCore.StorageEstimator.Planning;

                          namespace EfCore.StorageEstimator.Planning
                          {
                            [AttributeUsage(AttributeTargets.Class)]
                            public sealed class StorageEntityAttribute : Attribute
                            {
                              public StorageEntityAttribute(double expectedRowCount) { }
                            }

                            [AttributeUsage(AttributeTargets.Property)]
                            public sealed class StorageFieldAttribute : Attribute
                            {
                              public StorageFieldAttribute(double fillRate = 1.0d) { }

                              public int AverageLength { get; init; }
                            }

                            [AttributeUsage(AttributeTargets.Property)]
                            public sealed class StorageNavigationAttribute : Attribute
                            {
                              public StorageNavigationAttribute(double averageMultiplicity) { }
                            }
                          }

                          [StorageEntity(100)]
                          public sealed class FleetRoot
                          {
                            [StorageField(0.8d, AverageLength = 32)]
                            public string? SerialNumber { get; init; }

                            [StorageNavigation(2.5d)]
                            public List<Aircraft> Aircraft { get; } = new List<Aircraft>();
                          }

                          [StorageEntity(10)]
                          public sealed class Aircraft
                          {
                          }
                          """;

    // Act
    var diagnostics = await GetDiagnosticsAsync(source);

    // Assert
    Assert.Empty(diagnostics);
  }

  #endregion


  #region Methods - Private

  private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
  {
    var syntaxTree = CSharpSyntaxTree.ParseText(source);
    var references = new[]
    {
      MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
      MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
      MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
      MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
      MetadataReference.CreateFromFile(typeof(System.Runtime.GCSettings).Assembly.Location)
    };
    var compilation = CSharpCompilation.Create(
      "StoragePlanningAnalyzerTests",
      [syntaxTree],
      references,
      new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new StoragePlanningAnalyzer());

    return await compilation
                 .WithAnalyzers(analyzers)
                 .GetAnalyzerDiagnosticsAsync();
  }

  #endregion
}
