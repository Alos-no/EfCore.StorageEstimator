namespace EfCore.StorageEstimator;

using System.Collections;
using System.Reflection;
using Estimation;
using Exceptions;
using Planning;
using Schema;

/// <summary>Default implementation of <see cref="IStorageEstimator" />.</summary>
internal sealed class StorageEstimator : IStorageEstimator
{
  #region Methods Impl

  /// <inheritdoc />
  public StorageEstimateReport Estimate(StorageEstimateRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    if (request.Roots.Count == 0)
      throw new StorageEstimatorConfigurationException(
        "A storage estimate request must define at least one root entity.");

    var nodes        = new List<StorageEstimateNode>();
    var warnings     = new List<string>();
    var schemaReader = request.Model is null ? null : new EfCoreSchemaReader(request.Model);

    foreach (var root in request.Roots)
    {
      var rootPath = GetRootPath(root);
      var pathEntityTypes = new HashSet<Type>
      {
        root.EntityType
      };

      if (schemaReader is null)
      {
        var rootRowCount = ResolveRootRowCount(root);
        var rootEstimate = PostgreSqlStorageMath.EstimateClrEntity(root.EntityType, rootPath, rootRowCount, warnings);

        nodes.Add(new StorageEstimateNode(
                    rootPath,
                    root.EntityType,
                    rootRowCount,
                    0,
                    rootEstimate.HeapBytes,
                    rootEstimate.IndexEstimates,
                    rootEstimate.Schema));

        TraverseClrEntity(
          root.EntityType,
          rootPath,
          rootRowCount,
          1,
          pathEntityTypes,
          nodes,
          warnings);
      }
      else
      {
        var rootSchema   = schemaReader.GetEntitySchema(root.EntityType);
        var rootRowCount = ResolveRootRowCount(root);
        var rootEstimate = PostgreSqlStorageMath.EstimateEfEntity(rootSchema, rootPath, rootRowCount, warnings);

        nodes.Add(new StorageEstimateNode(
                    rootPath,
                    root.EntityType,
                    rootRowCount,
                    0,
                    rootEstimate.HeapBytes,
                    rootEstimate.IndexEstimates,
                    rootEstimate.Schema));

        TraverseEfEntity(
          schemaReader,
          rootSchema,
          rootPath,
          rootRowCount,
          1,
          pathEntityTypes,
          nodes,
          warnings);
      }
    }

    return new StorageEstimateReport(nodes, warnings);
  }


  /// <inheritdoc />
  public ValueTask<StorageEstimateReport> EstimateAsync(
    StorageEstimateRequest request,
    CancellationToken      cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();

    return ValueTask.FromResult(Estimate(request));
  }

  #endregion


  #region Methods - Private

  private static string GetRootPath(StorageTraversalRoot root)
  {
    return string.IsNullOrWhiteSpace(root.Label)
      ? root.EntityType.Name
      : root.Label.Trim();
  }


  private static double ResolveRootRowCount(StorageTraversalRoot root)
  {
    if (root.EntityCountOverride is double overrideRowCount)
      return overrideRowCount > 0
        ? overrideRowCount
        : throw new StorageEstimatorConfigurationException(
          $"Root '{root.EntityType.Name}' must define a positive number for {nameof(StorageTraversalRoot.EntityCountOverride)}.");

    var entityAttribute = root.EntityType.GetCustomAttribute<StorageEntityAttribute>();

    return entityAttribute?.ExpectedRowCount
      ?? throw new StorageEstimatorConfigurationException(
        $"A row count is required for root '{root.EntityType.Name}'. Define [{nameof(StorageEntityAttribute)}] or {nameof(StorageTraversalRoot.EntityCountOverride)}.");
  }


  private static void TraverseClrEntity(
    Type                             entityType,
    string                           currentPath,
    double                           currentRows,
    int                              depth,
    HashSet<Type>                    pathEntityTypes,
    ICollection<StorageEstimateNode> nodes,
    ICollection<string>              warnings)
  {
    foreach (var property in entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
    {
      if (!TryGetNavigationTargetType(property, out var targetType))
        continue;

      var branchPath                 = $"{currentPath}.{property.Name}";
      var navigationAttribute        = property.GetCustomAttribute<StorageNavigationAttribute>();
      var traversalBoundaryAttribute = property.GetCustomAttribute<StorageTraversalBoundaryAttribute>();
      var targetEntityAttribute      = targetType.GetCustomAttribute<StorageEntityAttribute>();

      if (navigationAttribute is null)
      {
        if (traversalBoundaryAttribute is null &&
            targetEntityAttribute is not null &&
            !pathEntityTypes.Contains(targetType))
          warnings.Add($"Stopped at undefined branch '{branchPath}'. Add [{nameof(StorageNavigationAttribute)}] to traverse it.");

        continue;
      }

      if (pathEntityTypes.Contains(targetType))
      {
        if (!navigationAttribute.SuppressCycleWarning)
          warnings.Add($"Cycle detected at '{branchPath}'. Traversal stopped before revisiting '{targetType.Name}'.");

        continue;
      }

      var childRows = currentRows * navigationAttribute.AverageMultiplicity;
      var childPathEntityTypes = new HashSet<Type>(pathEntityTypes)
      {
        targetType
      };
      var childEstimate = PostgreSqlStorageMath.EstimateClrEntity(targetType, branchPath, childRows, warnings);

      nodes.Add(new StorageEstimateNode(
                  branchPath,
                  targetType,
                  childRows,
                  depth,
                  childEstimate.HeapBytes,
                  childEstimate.IndexEstimates,
                  childEstimate.Schema));

      TraverseClrEntity(
        targetType,
        branchPath,
        childRows,
        depth + 1,
        childPathEntityTypes,
        nodes,
        warnings);
    }
  }


  private static void TraverseEfEntity(
    EfCoreSchemaReader               schemaReader,
    EfCoreEntitySchema               entitySchema,
    string                           currentPath,
    double                           currentRows,
    int                              depth,
    HashSet<Type>                    pathEntityTypes,
    ICollection<StorageEstimateNode> nodes,
    ICollection<string>              warnings)
  {
    foreach (var navigation in entitySchema.Navigations)
    {
      var branchPath                 = $"{currentPath}.{navigation.Name}";
      var navigationAttribute        = navigation.PropertyInfo?.GetCustomAttribute<StorageNavigationAttribute>();
      var traversalBoundaryAttribute = navigation.PropertyInfo?.GetCustomAttribute<StorageTraversalBoundaryAttribute>();
      var targetEntityAttribute      = navigation.TargetClrType.GetCustomAttribute<StorageEntityAttribute>();

      if (navigationAttribute is null)
      {
        if (traversalBoundaryAttribute is null &&
            targetEntityAttribute is not null &&
            !pathEntityTypes.Contains(navigation.TargetClrType))
          warnings.Add($"Stopped at undefined branch '{branchPath}'. Add [{nameof(StorageNavigationAttribute)}] to traverse it.");

        continue;
      }

      if (pathEntityTypes.Contains(navigation.TargetClrType))
      {
        if (!navigationAttribute.SuppressCycleWarning)
          warnings.Add($"Cycle detected at '{branchPath}'. Traversal stopped before revisiting '{navigation.TargetClrType.Name}'.");

        continue;
      }

      var childRows = currentRows * navigationAttribute.AverageMultiplicity;
      var childPathEntityTypes = new HashSet<Type>(pathEntityTypes)
      {
        navigation.TargetClrType
      };
      var childSchema   = schemaReader.GetEntitySchema(navigation.TargetEntityType);
      var childEstimate = PostgreSqlStorageMath.EstimateEfEntity(childSchema, branchPath, childRows, warnings);

      nodes.Add(new StorageEstimateNode(
                  branchPath,
                  navigation.TargetClrType,
                  childRows,
                  depth,
                  childEstimate.HeapBytes,
                  childEstimate.IndexEstimates,
                  childEstimate.Schema));

      TraverseEfEntity(
        schemaReader,
        childSchema,
        branchPath,
        childRows,
        depth + 1,
        childPathEntityTypes,
        nodes,
        warnings);
    }
  }


  private static bool TryGetNavigationTargetType(PropertyInfo property, out Type targetType)
  {
    ArgumentNullException.ThrowIfNull(property);

    if (property.GetIndexParameters().Length > 0)
    {
      targetType = null!;
      return false;
    }

    var propertyType = property.PropertyType;

    if (propertyType == typeof(string) || propertyType == typeof(byte[]))
    {
      targetType = null!;
      return false;
    }

    if (TryGetCollectionElementType(propertyType, out targetType))
      return true;

    if (!propertyType.IsClass)
    {
      targetType = null!;
      return false;
    }

    targetType = propertyType;
    return true;
  }


  private static bool TryGetCollectionElementType(Type propertyType, out Type elementType)
  {
    if (propertyType.IsArray)
    {
      elementType = propertyType.GetElementType()!;
      return elementType != typeof(byte);
    }

    if (propertyType == typeof(string))
    {
      elementType = null!;
      return false;
    }

    var enumerableType = propertyType.IsGenericType &&
      propertyType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
        ? propertyType
        : propertyType
          .GetInterfaces()
          .FirstOrDefault(@interface =>
                            @interface.IsGenericType &&
                            @interface.GetGenericTypeDefinition() == typeof(IEnumerable<>));

    if (enumerableType is null)
    {
      elementType = null!;
      return false;
    }

    elementType = enumerableType.GetGenericArguments()[0];

    return elementType != typeof(char) && !typeof(IDictionary).IsAssignableFrom(propertyType);
  }

  #endregion
}
