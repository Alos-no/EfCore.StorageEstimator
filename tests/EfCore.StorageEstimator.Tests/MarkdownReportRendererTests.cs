namespace EfCore.StorageEstimator.Tests;

using Estimation;
using Rendering;

/// <summary>Unit tests for <see cref="MarkdownReportRenderer" />.</summary>
[Trait("Category", "Unit")]
public sealed class MarkdownReportRendererTests
{
  #region Render

  [Fact]
  public void Render_WithWarnings_RendersMarkdownTableAndWarningsSection()
  {
    // Arrange
    var renderer = new MarkdownReportRenderer();
    var report = new StorageEstimateReport(
      [
        new StorageEstimateNode(
          "FleetRoot",
          typeof(object),
          3,
          0,
          8192,
          [
            new StorageIndexEstimate("ix_fleet_roots_name", 8192, 32, 1)
          ],
          new StorageEntitySchema("fleet_roots", 2, 1)),
        new StorageEstimateNode(
          "FleetRoot.Aircraft",
          typeof(string),
          6,
          1,
          16384,
          [
            new StorageIndexEstimate("ix_aircraft_tail_number", 8192, 28, 1)
          ],
          new StorageEntitySchema("aircraft", 3, 1))
      ],
      [
        "Stopped at undefined branch 'FleetRoot.Missions'."
      ]);

    // Act
    var markdown = renderer.Render(report);

    // Assert
    markdown.Should().Contain("# Storage Estimate Report");
    markdown.Should().Contain("| Path | Entity | Rows | Heap Bytes | Index Bytes | Total Bytes | Table | Properties | Indexes |");
    markdown.Should().Contain("| FleetRoot | Object | 3 | 8192 | 8192 | 16384 | fleet_roots | 2 | 1 |");
    markdown.Should().Contain("| FleetRoot.Aircraft | String | 6 | 16384 | 8192 | 24576 | aircraft | 3 | 1 |");
    markdown.Should().Contain("## Warnings");
    markdown.Should().Contain("- Stopped at undefined branch 'FleetRoot.Missions'.");
    markdown.Should().Contain("**Total Estimated Rows:** 9");
    markdown.Should().Contain("**Total Estimated Heap Bytes:** 24576");
    markdown.Should().Contain("**Total Estimated Index Bytes:** 16384");
    markdown.Should().Contain("**Total Estimated Bytes:** 40960");
  }


  [Fact]
  public void Render_WithoutWarnings_OmitsWarningsSection()
  {
    // Arrange
    var renderer = new MarkdownReportRenderer();
    var report = new StorageEstimateReport(
      [
        new StorageEstimateNode(
          "FleetRoot",
          typeof(object),
          1,
          0,
          4096)
      ],
      []);

    // Act
    var markdown = renderer.Render(report);

    // Assert
    markdown.Should().NotContain("## Warnings");
    markdown.Should().Contain("| FleetRoot | Object | 1 | 4096 | 0 | 4096 | - | - | - |");
  }

  #endregion
}
