namespace EfCore.StorageEstimator.Estimation;

using Microsoft.EntityFrameworkCore.Metadata;

/// <summary>Represents an estimation request containing one or more root entities to analyze.</summary>
public sealed class StorageEstimateRequest
{
  #region Properties & Fields - Public

  /// <summary>Gets or sets the root entities that define the traversal entry points for the estimate.</summary>
  public IReadOnlyList<StorageTraversalRoot> Roots { get; init; } = [];

  /// <summary>Gets or sets the optional EF Core model used for schema-backed estimation.</summary>
  public IModel? Model { get; init; }

  #endregion
}

/// <summary>Represents a single root entity selection for an estimate run.</summary>
/// <param name="entityType">The CLR entity type to use as the traversal root.</param>
public sealed class StorageTraversalRoot(Type entityType)
{
  #region Properties & Fields - Public

  /// <summary>Gets the CLR entity type to use as the traversal root.</summary>
  public Type EntityType { get; } = entityType ?? throw new ArgumentNullException(nameof(entityType));

  /// <summary>Gets or sets an optional explicit row-count override for this root.</summary>
  public double? EntityCountOverride { get; init; }

  /// <summary>Gets or sets an optional label used as the display path for this root.</summary>
  public string? Label { get; init; }

  #endregion
}
