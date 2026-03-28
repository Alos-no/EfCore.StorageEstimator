namespace EfCore.StorageEstimator.Tests;

using Estimation;
using Planning;

/// <summary>Unit tests for <see cref="StorageEstimator" />.</summary>
[Trait("Category", "Unit")]
public sealed class StorageEstimatorTests
{
  #region Estimate

  [Fact]
  public void Estimate_WithNullRequest_ThrowsArgumentNullException()
  {
    // Arrange
    var service = CreateService();

    // Act
    var act = () => service.Estimate(null!);

    // Assert
    act.Should().Throw<ArgumentNullException>()
       .WithParameterName("request");
  }


  [Fact]
  public void Estimate_WithNoRoots_ThrowsStorageEstimatorConfigurationException()
  {
    // Arrange
    var service = CreateService();
    var request = new StorageEstimateRequest();

    // Act
    var act = () => service.Estimate(request);

    // Assert
    act.Should().Throw<Exceptions.StorageEstimatorConfigurationException>()
       .WithMessage("*at least one root*");
  }


  [Fact]
  public void Estimate_WithAnnotatedRootAndNavigation_ReturnsExpectedRowExpansion()
  {
    // Arrange
    var service = CreateService();
    var request = new StorageEstimateRequest
    {
      Roots =
      [
        new StorageTraversalRoot(typeof(TestFleet))
      ]
    };

    // Act
    var report = service.Estimate(request);

    // Assert
    report.Nodes.Should().HaveCount(2);
    report.Nodes.Should().ContainSingle(node =>
                                          node.Path == nameof(TestFleet) &&
                                          node.EntityType == typeof(TestFleet) &&
                                          node.EstimatedRows == 5 &&
                                          node.Depth == 0);
    report.Nodes.Should().ContainSingle(node =>
                                          node.Path == $"{nameof(TestFleet)}.{nameof(TestFleet.Aircraft)}" &&
                                          node.EntityType == typeof(TestAircraft) &&
                                          node.EstimatedRows == 10 &&
                                          node.Depth == 1);
    report.TotalEstimatedRows.Should().Be(15);
    report.Warnings.Should().BeEmpty();
  }


  [Fact]
  public void Estimate_WithRootCountOverride_UsesOverrideInsteadOfEntityMetadata()
  {
    // Arrange
    var service = CreateService();
    var request = new StorageEstimateRequest
    {
      Roots =
      [
        new StorageTraversalRoot(typeof(TestFleet))
        {
          EntityCountOverride = 3
        }
      ]
    };

    // Act
    var report = service.Estimate(request);

    // Assert
    report.Nodes.Should().ContainSingle(node =>
                                          node.Path == nameof(TestFleet) &&
                                          node.EstimatedRows == 3);
    report.Nodes.Should().ContainSingle(node =>
                                          node.Path == $"{nameof(TestFleet)}.{nameof(TestFleet.Aircraft)}" &&
                                          node.EstimatedRows == 6);
    report.TotalEstimatedRows.Should().Be(9);
  }


  [Fact]
  public void Estimate_WithUnannotatedNavigationToPlannedEntity_StopsBranchAndAddsWarning()
  {
    // Arrange
    var service = CreateService();
    var request = new StorageEstimateRequest
    {
      Roots =
      [
        new StorageTraversalRoot(typeof(BoundaryRoot))
      ]
    };

    // Act
    var report = service.Estimate(request);

    // Assert
    report.Nodes.Should().ContainSingle(node =>
                                          node.Path == nameof(BoundaryRoot) &&
                                          node.EstimatedRows == 4);
    report.Warnings.Should().ContainSingle(warning =>
                                             warning.Contains($"{nameof(BoundaryRoot)}.{nameof(BoundaryRoot.Orders)}"));
  }


  [Fact]
  public void Estimate_WithCycle_DetectsCycleAndStopsRecursion()
  {
    // Arrange
    var service = CreateService();
    var request = new StorageEstimateRequest
    {
      Roots =
      [
        new StorageTraversalRoot(typeof(CyclicCompany))
      ]
    };

    // Act
    var report = service.Estimate(request);

    // Assert
    report.Nodes.Should().HaveCount(2);
    report.Nodes.Should().ContainSingle(node =>
                                          node.Path == nameof(CyclicCompany) &&
                                          node.EstimatedRows == 2);
    report.Nodes.Should().ContainSingle(node =>
                                          node.Path == $"{nameof(CyclicCompany)}.{nameof(CyclicCompany.Teams)}" &&
                                          node.EstimatedRows == 6);
    report.Warnings.Should().ContainSingle(warning =>
                                             warning.Contains(
                                               $"{nameof(CyclicCompany)}.{nameof(CyclicCompany.Teams)}.{nameof(CyclicTeam.Company)}"));
  }


  [Fact]
  public void Estimate_WithMissingRootRowCount_ThrowsStorageEstimatorConfigurationException()
  {
    // Arrange
    var service = CreateService();
    var request = new StorageEstimateRequest
    {
      Roots =
      [
        new StorageTraversalRoot(typeof(UnplannedRoot))
      ]
    };

    // Act
    var act = () => service.Estimate(request);

    // Assert
    act.Should().Throw<Exceptions.StorageEstimatorConfigurationException>()
       .WithMessage("*row count*UnplannedRoot*");
  }


  [Fact]
  public void Estimate_WithNegativeRootCountOverride_ThrowsStorageEstimatorConfigurationException()
  {
    // Arrange
    var service = CreateService();
    var request = new StorageEstimateRequest
    {
      Roots =
      [
        new StorageTraversalRoot(typeof(TestFleet))
        {
          EntityCountOverride = -1
        }
      ]
    };

    // Act
    var act = () => service.Estimate(request);

    // Assert
    act.Should().Throw<Exceptions.StorageEstimatorConfigurationException>()
       .WithMessage("*positive number*");
  }

  #endregion


  #region EstimateAsync

  [Fact]
  public async Task EstimateAsync_WithCancellationRequested_ThrowsOperationCanceledException()
  {
    // Arrange
    var service = CreateService();
    var request = new StorageEstimateRequest
    {
      Roots =
      [
        new StorageTraversalRoot(typeof(TestFleet))
      ]
    };
    var cancellationTokenSource = new CancellationTokenSource();
    await cancellationTokenSource.CancelAsync();

    // Act
    var act = async () => await service.EstimateAsync(request, cancellationTokenSource.Token);

    // Assert
    await act.Should().ThrowAsync<OperationCanceledException>();
  }

  #endregion


  #region Helpers

  private static IStorageEstimator CreateService()
  {
    return new StorageEstimator();
  }

  #endregion


  #region Test Models

  [StorageEntity(5)]
  private sealed class TestFleet
  {
    [StorageNavigation(2)]
    public IReadOnlyList<TestAircraft> Aircraft { get; } = [];
  }


  [StorageEntity(1)]
  private sealed class TestAircraft { }


  [StorageEntity(4)]
  private sealed class BoundaryRoot
  {
    public IReadOnlyList<BoundaryOrder> Orders { get; } = [];
  }


  [StorageEntity(1)]
  private sealed class BoundaryOrder { }


  [StorageEntity(2)]
  private sealed class CyclicCompany
  {
    [StorageNavigation(3)]
    public IReadOnlyList<CyclicTeam> Teams { get; } = [];
  }


  [StorageEntity(1)]
  private sealed class CyclicTeam
  {
    [StorageNavigation(1)]
    public CyclicCompany? Company { get; set; }
  }


  private sealed class UnplannedRoot { }

  #endregion
}
