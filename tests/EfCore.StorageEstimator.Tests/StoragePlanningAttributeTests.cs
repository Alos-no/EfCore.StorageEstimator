namespace EfCore.StorageEstimator.Tests;

using Planning;

/// <summary>Unit tests for storage-planning attributes.</summary>
[Trait("Category", "Unit")]
public sealed class StoragePlanningAttributeTests
{
  #region StorageFieldAttribute

  [Fact]
  public void StorageFieldAttribute_WithNegativeAverageLength_ThrowsArgumentOutOfRangeException()
  {
    // Act
    var act = () => new StorageFieldAttribute
    {
      AverageLength = -1
    };

    // Assert
    act.Should().Throw<ArgumentOutOfRangeException>()
       .WithParameterName("value");
  }

  #endregion
}
