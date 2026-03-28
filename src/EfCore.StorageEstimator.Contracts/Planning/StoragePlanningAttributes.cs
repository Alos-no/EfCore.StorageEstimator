namespace EfCore.StorageEstimator.Planning;

using System.Diagnostics;

/// <summary>Declares planning metadata for an entity type.</summary>
/// <param name="expectedRowCount">The expected row count when this entity is used as a root.</param>
[Conditional(StorageEstimatorContractsConstants.EmitPlanningAttributesCompilationSymbol)]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class StorageEntityAttribute(double expectedRowCount) : Attribute
{
  #region Properties & Fields - Public

  /// <summary>Gets the expected row count when this entity is used as a root.</summary>
  public double ExpectedRowCount { get; } = expectedRowCount > 0
    ? expectedRowCount
    : throw new ArgumentOutOfRangeException(nameof(expectedRowCount), "Expected row count must be positive.");

  #endregion
}

/// <summary>Declares planning metadata for a scalar field.</summary>
/// <param name="fillRate">The expected fill rate as a value between 0 and 1 inclusive.</param>
[Conditional(StorageEstimatorContractsConstants.EmitPlanningAttributesCompilationSymbol)]
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class StorageFieldAttribute(double fillRate = 1.0d) : Attribute
{
  private readonly int _averageLength;


  #region Properties & Fields - Public

  /// <summary>Gets the expected fill rate as a value between 0 and 1 inclusive.</summary>
  public double FillRate { get; } = fillRate is >= 0 and <= 1
    ? fillRate
    : throw new ArgumentOutOfRangeException(nameof(fillRate), "Fill rate must be between 0 and 1 inclusive.");

  /// <summary>Gets or sets the expected average payload length for variable-width values.</summary>
  public int AverageLength
  {
    get => _averageLength;
    init => _averageLength = value >= 0
      ? value
      : throw new ArgumentOutOfRangeException(nameof(value), "Average length must be zero or greater.");
  }

  #endregion
}

/// <summary>Declares planning metadata for a navigation.</summary>
/// <param name="averageMultiplicity">The expected average number of related rows produced by this navigation.</param>
[Conditional(StorageEstimatorContractsConstants.EmitPlanningAttributesCompilationSymbol)]
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class StorageNavigationAttribute(double averageMultiplicity) : Attribute
{
  #region Properties & Fields - Public

  /// <summary>Gets the expected average number of related rows produced by this navigation.</summary>
  public double AverageMultiplicity { get; } = averageMultiplicity > 0
    ? averageMultiplicity
    : throw new ArgumentOutOfRangeException(nameof(averageMultiplicity), "Average multiplicity must be positive.");

  /// <summary>Gets or sets whether a cycle on this navigation should be treated as intentional and left without a warning.</summary>
  public bool SuppressCycleWarning { get; init; }

  #endregion
}

/// <summary>
///   Declares that a navigation is an intentional traversal boundary and should not emit an undefined-branch
///   warning.
/// </summary>
[Conditional(StorageEstimatorContractsConstants.EmitPlanningAttributesCompilationSymbol)]
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class StorageTraversalBoundaryAttribute : Attribute;
