namespace EfCore.StorageEstimator.Tests;

using Estimation;
using Rendering;

/// <summary>Unit tests for <see cref="JsonReportRenderer" />.</summary>
[Trait("Category", "Unit")]
public sealed class JsonReportRendererTests
{
  #region Render

  [Fact]
  public void Render_WithReport_RendersStructuredJson()
  {
    // Arrange
    var renderer = new JsonReportRenderer();
    var report = new StorageEstimateReport(
      [
        new StorageEstimateNode(
          "FleetRoot",
          typeof(object),
          5,
          0,
          8192,
          [
            new StorageIndexEstimate("ix_fleet_root_name", 4096, 24, 1)
          ],
          new StorageEntitySchema("fleet_roots", 2, 1))
      ],
      []);

    // Act
    var json = renderer.Render(report);

    // Assert
    json.Should().Contain("\"totalEstimatedRows\":5");
    json.Should().Contain("\"totalEstimatedHeapBytes\":8192");
    json.Should().Contain("\"totalEstimatedIndexBytes\":4096");
    json.Should().Contain("\"totalEstimatedBytes\":12288");
    json.Should().Contain("\"path\":\"FleetRoot\"");
    json.Should().Contain("\"name\":\"ix_fleet_root_name\"");
  }

  #endregion
}
