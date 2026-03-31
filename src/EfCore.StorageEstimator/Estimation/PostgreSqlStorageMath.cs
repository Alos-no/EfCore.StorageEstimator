namespace EfCore.StorageEstimator.Estimation;

using System.Reflection;
using Planning;
using Schema;

/// <summary>Provides planning-grade PostgreSQL heap and secondary-index size estimation.</summary>
internal static class PostgreSqlStorageMath
{
  #region Constants & Statics

  private const double HeapPageBytes              = 8192;
  private const double HeapPageHeaderBytes        = 24;
  private const double HeapTupleHeaderBytes       = 23;
  private const double HeapItemPointerBytes       = 4;
  private const double IndexPageHeaderBytes       = 24;
  private const double BTreeSpecialSpaceBytes     = 16;
  private const double IndexTupleHeaderBytes      = 8;
  private const double IndexNullBitmapHeaderBytes = 16;
  private const double IndexRowPointerBytes       = 4;
  private const int    MaxAlignment               = 8;
  private const int    DefaultVariableLengthBytes = 256;
  private const int    DefaultSpatialLengthBytes  = 128;

  #endregion


  #region Methods

  public static EntityStorageEstimate EstimateClrEntity(
    Type                entityType,
    string              entityPath,
    double              rowCount,
    ICollection<string> warnings)
  {
    ArgumentNullException.ThrowIfNull(entityType);
    ArgumentException.ThrowIfNullOrWhiteSpace(entityPath);
    ArgumentNullException.ThrowIfNull(warnings);

    var properties = entityType
                     .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => !property.IsSpecialName && property.GetIndexParameters().Length == 0)
                     .Where(property => !IsNavigationProperty(property.PropertyType))
                     .Select(property => new PropertySizingInput(
                               property.Name,
                               property,
                               property.PropertyType,
                               property.PropertyType,
                               null,
                               IsNullableProperty(property.PropertyType),
                               IsVariableLength(property.PropertyType, property.PropertyType, null),
                               null,
                               null,
                               null))
                     .ToArray();
    var schema = new StorageEntitySchema(
      null,
      properties.Length,
      0,
      properties.Select(property => property.ToSchema()).ToArray(),
      []);

    return EstimateEntity(schema, properties, [], entityPath, rowCount, warnings);
  }


  public static EntityStorageEstimate EstimateEfEntity(
    EfCoreEntitySchema  entitySchema,
    string              entityPath,
    double              rowCount,
    ICollection<string> warnings)
  {
    ArgumentNullException.ThrowIfNull(entitySchema);
    ArgumentException.ThrowIfNullOrWhiteSpace(entityPath);
    ArgumentNullException.ThrowIfNull(warnings);

    var properties = entitySchema.Properties
                                 .Select(property => new PropertySizingInput(
                                           property.Name,
                                           property.PropertyInfo,
                                           property.ClrType,
                                           property.ProviderClrType,
                                           property.Schema.StoreType,
                                           property.Schema.IsNullable,
                                           property.Schema.IsVariableLength,
                                           property.Schema.MaxLength,
                                           property.Schema.Precision,
                                           property.Schema.Scale))
                                 .ToArray();

    return EstimateEntity(entitySchema.Schema, properties, entitySchema.Indexes, entityPath, rowCount, warnings);
  }

  #endregion


  #region Methods - Private

  private static EntityStorageEstimate EstimateEntity(
    StorageEntitySchema                schema,
    IReadOnlyList<PropertySizingInput> properties,
    IReadOnlyList<EfCoreIndexSchema>   indexes,
    string                             entityPath,
    double                             rowCount,
    ICollection<string>                warnings)
  {
    var propertyEstimates = properties
                            .Select(property => EstimateProperty(property, $"{entityPath}.{property.Name}", warnings))
                            .ToArray();
    var propertyLookup = propertyEstimates.ToDictionary(property => property.Name, StringComparer.Ordinal);
    var tupleHeaderBytesWithoutNulls = Align(HeapTupleHeaderBytes, MaxAlignment);
    var tupleHeaderBytesWithNulls = Align(
      HeapTupleHeaderBytes + GetNullBitmapBytes(properties),
      MaxAlignment);
    var averageHeapTupleBytes = EstimateAverageTupleBytes(
      tupleHeaderBytesWithoutNulls,
      tupleHeaderBytesWithNulls,
      propertyEstimates);
    var averageHeapRowBytes = averageHeapTupleBytes + HeapItemPointerBytes;
    var heapBytes           = EstimatePagedBytes(rowCount, averageHeapRowBytes, HeapPageHeaderBytes, 0);
    var indexEstimates = indexes
                         .Select(index => EstimateIndex(index, propertyLookup, rowCount))
                         .ToArray();

    return new EntityStorageEstimate(schema, heapBytes, indexEstimates);
  }


  private static PropertySizeEstimate EstimateProperty(
    PropertySizingInput property,
    string              propertyPath,
    ICollection<string> warnings)
  {
    var fieldAttribute = property.PropertyInfo?.GetCustomAttribute<StorageFieldAttribute>();

    if (property.IsVariableLength)
      return EstimateVariableLengthProperty(property, fieldAttribute, propertyPath, warnings);

    var size     = GetFixedSize(property, propertyPath, warnings);
    var fillRate = fieldAttribute?.FillRate ?? 1.0d;

    if (property.IsNullable && fieldAttribute is null && property.PropertyInfo is not null)
      warnings.Add(
        $"Using fallback fill rate 1.0 for nullable fixed-width property '{propertyPath}'. Add [{nameof(StorageFieldAttribute)}] to override it.");

    return new PropertySizeEstimate(
      property.Name,
      size,
      GetFixedAlignment(property, propertyPath, warnings),
      fillRate);
  }


  private static PropertySizeEstimate EstimateVariableLengthProperty(
    PropertySizingInput    property,
    StorageFieldAttribute? fieldAttribute,
    string                 propertyPath,
    ICollection<string>    warnings)
  {
    var     fillRate              = fieldAttribute?.FillRate ?? 1.0d;
    double? averageLength         = fieldAttribute?.AverageLength;
    var     shouldWarnForFallback = property.PropertyInfo is not null;

    if (averageLength is null or 0)
    {
      if (IsSpatialType(property.StoreType))
      {
        averageLength = DefaultSpatialLengthBytes;

        if (shouldWarnForFallback)
          warnings.Add(
            $"Using default average length {DefaultSpatialLengthBytes} for spatial property '{propertyPath}'. Add [{nameof(StorageFieldAttribute)}] to override it.");
      }
      else if (property.MaxLength is int maxLength)
      {
        averageLength = maxLength;

        if (shouldWarnForFallback)
          warnings.Add($"Using MaxLength={maxLength} as the fallback average length for variable-width property '{propertyPath}'.");
      }
      else if (IsNumericType(property.ClrType, property.ProviderClrType, property.StoreType))
      {
        averageLength = EstimateNumericStorageBytes(property.Precision, propertyPath, warnings);
      }
      else
      {
        averageLength = DefaultVariableLengthBytes;

        if (shouldWarnForFallback)
          warnings.Add(
            $"Using default average length {DefaultVariableLengthBytes} for variable-width property '{propertyPath}'. Add [{nameof(StorageFieldAttribute)}] to override it.");
      }
    }

    if (IsNumericType(property.ClrType, property.ProviderClrType, property.StoreType))
      return new PropertySizeEstimate(
        property.Name,
        averageLength.Value,
        GetVariableLengthAlignment(averageLength.Value),
        fillRate);

    var overheadBytes = averageLength <= 126
      ? 1
      : 4;

    var storedBytes = averageLength.Value + overheadBytes;

    return new PropertySizeEstimate(
      property.Name,
      storedBytes,
      GetVariableLengthAlignment(storedBytes),
      fillRate);
  }


  private static double EstimateNumericStorageBytes(
    int?                precision,
    string              propertyPath,
    ICollection<string> warnings)
  {
    if (precision is > 0)
      return 4 + (2 * Math.Ceiling(precision.Value / 4d));

    warnings.Add($"Using fallback numeric storage size 16 for '{propertyPath}' because precision was not configured.");

    return 16;
  }


  private static StorageIndexEstimate EstimateIndex(
    EfCoreIndexSchema                                 index,
    IReadOnlyDictionary<string, PropertySizeEstimate> propertyLookup,
    double                                            rowCount)
  {
    var keyProperties = index.Properties
                             .Select(property => propertyLookup[property.Name])
                             .ToArray();
    var averageTupleBytes = EstimateAverageTupleBytes(
      IndexTupleHeaderBytes,
      IndexNullBitmapHeaderBytes,
      keyProperties);
    var averageEntryBytes = averageTupleBytes + IndexRowPointerBytes;
    var estimatedBytes    = EstimatePagedBytes(rowCount, averageEntryBytes, IndexPageHeaderBytes, BTreeSpecialSpaceBytes) + HeapPageBytes;

    return new StorageIndexEstimate(
      index.Schema.Name,
      estimatedBytes,
      averageEntryBytes,
      index.Schema.ColumnCount,
      index.Schema.IsUnique);
  }


  private static double EstimateAverageTupleBytes(
    double                           headerBytesWithoutNulls,
    double                           headerBytesWithNulls,
    IReadOnlyList<PropertySizeEstimate> properties)
  {
    if (properties.Count == 0)
      return Align(headerBytesWithoutNulls, MaxAlignment);

    var allPresentProbability = properties.Aggregate(1.0d, (current, property) => current * property.FillRate);
    var allPresentBytesWithoutNulls = EstimateTupleBytesForAlwaysPresentProperties(headerBytesWithoutNulls, properties);

    if (allPresentProbability >= 1.0d)
      return allPresentBytesWithoutNulls;

    var allPresentBytesWithNulls = EstimateTupleBytesForAlwaysPresentProperties(headerBytesWithNulls, properties);
    var expectedBytesWithNullableHeader = EstimateExpectedTupleBytes(
      headerBytesWithNulls,
      properties);

    return (allPresentProbability * allPresentBytesWithoutNulls) +
      expectedBytesWithNullableHeader -
      (allPresentProbability * allPresentBytesWithNulls);
  }


  private static double EstimateTupleBytesForAlwaysPresentProperties(
    double                           headerBytes,
    IReadOnlyList<PropertySizeEstimate> properties)
  {
    var offset = headerBytes;

    foreach (var property in properties)
    {
      offset = Align(offset, property.AlignmentBytes);
      offset += property.BytesWhenPresent;
    }

    return Align(offset, MaxAlignment);
  }


  private static double EstimateExpectedTupleBytes(
    double                           headerBytes,
    IReadOnlyList<PropertySizeEstimate> properties)
  {
    var states = new Dictionary<int, TupleLayoutState>
    {
      [GetResidue(headerBytes)] = new TupleLayoutState(1.0d, headerBytes)
    };

    foreach (var property in properties)
    {
      var nextStates = new Dictionary<int, TupleLayoutState>();

      foreach (var state in states)
      {
        var residue     = state.Key;
        var layoutState = state.Value;

        if (property.FillRate < 1.0d)
          AddState(
            nextStates,
            residue,
            new TupleLayoutState(
              layoutState.Probability * (1.0d - property.FillRate),
              layoutState.WeightedOffsetSum * (1.0d - property.FillRate)));

        if (property.FillRate <= 0.0d)
          continue;

        var paddingBytes = GetPaddingBytes(residue, property.AlignmentBytes);
        var addedBytes   = paddingBytes + property.BytesWhenPresent;

        AddProbability(
          nextStates,
          GetResidue(residue + addedBytes),
          new TupleLayoutState(
            layoutState.Probability * property.FillRate,
            (layoutState.WeightedOffsetSum * property.FillRate) + (layoutState.Probability * property.FillRate * addedBytes)));
      }

      states = nextStates;
    }

    return states.Sum(state => state.Value.WeightedOffsetSum + (state.Value.Probability * GetPaddingBytes(state.Key, MaxAlignment)));
  }


  private static int GetPaddingBytes(
    int residue,
    int alignmentBytes)
  {
    if (alignmentBytes <= 1)
      return 0;

    var misalignment = residue % alignmentBytes;

    return misalignment == 0
      ? 0
      : alignmentBytes - misalignment;
  }


  private static int GetResidue(double offset)
  {
    return ((int)Math.Round(offset, MidpointRounding.AwayFromZero)) % MaxAlignment;
  }


  private static void AddProbability(
    IDictionary<int, TupleLayoutState> states,
    int                                residue,
    TupleLayoutState                   state)
  {
    if (state.Probability <= 0.0d)
      return;

    AddState(states, residue, state);
  }


  private static void AddState(
    IDictionary<int, TupleLayoutState> states,
    int                                residue,
    TupleLayoutState                   state)
  {
    if (state.Probability <= 0.0d)
      return;

    states[residue] = states.TryGetValue(residue, out var current)
      ? new TupleLayoutState(
        current.Probability + state.Probability,
        current.WeightedOffsetSum + state.WeightedOffsetSum)
      : state;
  }


  private static double EstimatePagedBytes(
    double rowCount,
    double averageEntryBytes,
    double pageHeaderBytes,
    double specialSpaceBytes)
  {
    if (rowCount <= 0)
      return 0;

    var usableBytesPerPage = HeapPageBytes - pageHeaderBytes - specialSpaceBytes;
    var rowsPerPage        = Math.Max(1, Math.Floor(usableBytesPerPage / averageEntryBytes));
    var pageCount          = Math.Ceiling(rowCount / rowsPerPage);

    return pageCount * HeapPageBytes;
  }


  private static double GetNullBitmapBytes(IReadOnlyList<PropertySizingInput> properties)
  {
    return properties.Any(property => property.IsNullable)
      ? Math.Ceiling(properties.Count / 8d)
      : 0;
  }


  private static int GetFixedSize(
    PropertySizingInput property,
    string              propertyPath,
    ICollection<string> warnings)
  {
    var nonNullableClrType      = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
    var nonNullableProviderType = Nullable.GetUnderlyingType(property.ProviderClrType) ?? property.ProviderClrType;
    var storeType               = property.StoreType ?? string.Empty;

    if (nonNullableProviderType.IsEnum)
      nonNullableProviderType = Enum.GetUnderlyingType(nonNullableProviderType);

    if (nonNullableClrType.IsEnum)
      nonNullableClrType = Enum.GetUnderlyingType(nonNullableClrType);

    if (nonNullableProviderType == typeof(bool) || nonNullableClrType == typeof(bool))
      return 1;

    if (nonNullableProviderType == typeof(byte) || nonNullableProviderType == typeof(sbyte) ||
        nonNullableClrType == typeof(byte) || nonNullableClrType == typeof(sbyte))
      return 1;

    if (nonNullableProviderType == typeof(short) || nonNullableProviderType == typeof(ushort) ||
        nonNullableClrType == typeof(short) || nonNullableClrType == typeof(ushort))
      return 2;

    if (nonNullableProviderType == typeof(int) || nonNullableProviderType == typeof(uint) ||
        nonNullableClrType == typeof(int) || nonNullableClrType == typeof(uint))
      return 4;

    if (nonNullableProviderType == typeof(long) || nonNullableProviderType == typeof(ulong) ||
        nonNullableClrType == typeof(long) || nonNullableClrType == typeof(ulong))
      return 8;

    if (nonNullableProviderType == typeof(float) || nonNullableClrType == typeof(float))
      return 4;

    if (nonNullableProviderType == typeof(double) || nonNullableClrType == typeof(double))
      return 8;

    if (nonNullableProviderType == typeof(Guid) || nonNullableClrType == typeof(Guid) ||
        storeType.Contains("uuid", StringComparison.OrdinalIgnoreCase))
      return 16;

    if (storeType.Contains("date", StringComparison.OrdinalIgnoreCase))
      return 4;

    if (storeType.Contains("timestamp", StringComparison.OrdinalIgnoreCase))
      return 8;

    if (storeType.Contains("interval", StringComparison.OrdinalIgnoreCase))
      return 16;

    if (storeType.Contains("time with time zone", StringComparison.OrdinalIgnoreCase))
      return 12;

    if (storeType.Contains("time", StringComparison.OrdinalIgnoreCase))
      return 8;

    if (nonNullableProviderType == typeof(DateTime) || nonNullableClrType == typeof(DateTime) ||
        nonNullableProviderType == typeof(DateTimeOffset) || nonNullableClrType == typeof(DateTimeOffset))
      return 8;

    if (nonNullableProviderType == typeof(TimeSpan) || nonNullableClrType == typeof(TimeSpan))
      return 16;

    warnings.Add($"Using fallback fixed-size storage 16 for '{propertyPath}' because its PostgreSQL storage size could not be inferred.");

    return 16;
  }


  private static int GetFixedAlignment(
    PropertySizingInput property,
    string              propertyPath,
    ICollection<string> warnings)
  {
    var nonNullableClrType      = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
    var nonNullableProviderType = Nullable.GetUnderlyingType(property.ProviderClrType) ?? property.ProviderClrType;
    var storeType               = property.StoreType ?? string.Empty;

    if (nonNullableProviderType.IsEnum)
      nonNullableProviderType = Enum.GetUnderlyingType(nonNullableProviderType);

    if (nonNullableClrType.IsEnum)
      nonNullableClrType = Enum.GetUnderlyingType(nonNullableClrType);

    if (nonNullableProviderType == typeof(bool) || nonNullableClrType == typeof(bool) ||
        nonNullableProviderType == typeof(byte) || nonNullableProviderType == typeof(sbyte) ||
        nonNullableClrType == typeof(byte) || nonNullableClrType == typeof(sbyte) ||
        nonNullableProviderType == typeof(Guid) || nonNullableClrType == typeof(Guid) ||
        storeType.Contains("uuid", StringComparison.OrdinalIgnoreCase))
      return 1;

    if (nonNullableProviderType == typeof(short) || nonNullableProviderType == typeof(ushort) ||
        nonNullableClrType == typeof(short) || nonNullableClrType == typeof(ushort))
      return 2;

    if (nonNullableProviderType == typeof(int) || nonNullableProviderType == typeof(uint) ||
        nonNullableClrType == typeof(int) || nonNullableClrType == typeof(uint) ||
        nonNullableProviderType == typeof(float) || nonNullableClrType == typeof(float) ||
        storeType.Contains("date", StringComparison.OrdinalIgnoreCase))
      return 4;

    if (nonNullableProviderType == typeof(long) || nonNullableProviderType == typeof(ulong) ||
        nonNullableClrType == typeof(long) || nonNullableClrType == typeof(ulong) ||
        nonNullableProviderType == typeof(double) || nonNullableClrType == typeof(double) ||
        nonNullableProviderType == typeof(DateTime) || nonNullableClrType == typeof(DateTime) ||
        nonNullableProviderType == typeof(DateTimeOffset) || nonNullableClrType == typeof(DateTimeOffset) ||
        nonNullableProviderType == typeof(TimeSpan) || nonNullableClrType == typeof(TimeSpan) ||
        storeType.Contains("timestamp", StringComparison.OrdinalIgnoreCase) ||
        storeType.Contains("interval", StringComparison.OrdinalIgnoreCase) ||
        storeType.Contains("time with time zone", StringComparison.OrdinalIgnoreCase) ||
        storeType.Contains("time", StringComparison.OrdinalIgnoreCase))
      return 8;

    warnings.Add($"Using fallback fixed-size alignment 8 for '{propertyPath}' because its PostgreSQL alignment could not be inferred.");

    return 8;
  }


  private static int GetVariableLengthAlignment(double storedBytes)
  {
    return storedBytes <= 126
      ? 1
      : 4;
  }


  private static bool IsNumericType(
    Type    clrType,
    Type    providerClrType,
    string? storeType)
  {
    var nonNullableClrType      = Nullable.GetUnderlyingType(clrType) ?? clrType;
    var nonNullableProviderType = Nullable.GetUnderlyingType(providerClrType) ?? providerClrType;

    return nonNullableClrType == typeof(decimal) ||
      nonNullableProviderType == typeof(decimal) ||
      (!string.IsNullOrWhiteSpace(storeType) &&
        (storeType.Contains("numeric", StringComparison.OrdinalIgnoreCase) ||
          storeType.Contains("decimal", StringComparison.OrdinalIgnoreCase)));
  }


  private static bool IsVariableLength(
    Type    clrType,
    Type    providerClrType,
    string? storeType)
  {
    var nonNullableClrType      = Nullable.GetUnderlyingType(clrType) ?? clrType;
    var nonNullableProviderType = Nullable.GetUnderlyingType(providerClrType) ?? providerClrType;

    if (nonNullableClrType == typeof(string) ||
        nonNullableClrType == typeof(byte[]) ||
        nonNullableProviderType == typeof(string) ||
        nonNullableProviderType == typeof(byte[]) ||
        IsSpatialType(storeType) ||
        IsScalarCollectionType(nonNullableClrType) ||
        IsScalarCollectionType(nonNullableProviderType))
      return true;

    return IsNumericType(nonNullableClrType, nonNullableProviderType, storeType);
  }


  private static bool IsNavigationProperty(Type propertyType)
  {
    var nonNullableType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

    if (nonNullableType == typeof(string) || nonNullableType == typeof(byte[]))
      return false;

    if (nonNullableType.IsArray)
      return !IsScalarCollectionType(nonNullableType);

    if (typeof(System.Collections.IEnumerable).IsAssignableFrom(nonNullableType))
      return !IsScalarCollectionType(nonNullableType);

    return nonNullableType.IsClass && nonNullableType != typeof(object);
  }


  private static bool IsSpatialType(string? storeType)
  {
    return !string.IsNullOrWhiteSpace(storeType) &&
      (storeType.Contains("geometry", StringComparison.OrdinalIgnoreCase) ||
        storeType.Contains("geography", StringComparison.OrdinalIgnoreCase));
  }


  private static bool IsNullableProperty(Type propertyType)
  {
    return !propertyType.IsValueType || Nullable.GetUnderlyingType(propertyType) is not null;
  }


  private static double Align(double value, int alignment)
  {
    return Math.Ceiling(value / alignment) * alignment;
  }


  private static bool IsScalarCollectionType(Type type)
  {
    if (type == typeof(string) || type == typeof(byte[]))
      return false;

    if (type.IsArray)
    {
      var elementType = type.GetElementType();

      return elementType is not null && elementType != typeof(byte) && IsScalarElementType(elementType);
    }

    if (!type.IsGenericType)
      return false;

    var genericTypeDefinition = type.GetGenericTypeDefinition();

    if (genericTypeDefinition != typeof(List<>) &&
        genericTypeDefinition != typeof(IReadOnlyList<>) &&
        genericTypeDefinition != typeof(ICollection<>) &&
        genericTypeDefinition != typeof(IEnumerable<>))
      return false;

    var elementTypeArgument = type.GetGenericArguments()[0];

    return elementTypeArgument != typeof(byte[]) && IsScalarElementType(elementTypeArgument);
  }


  private static bool IsScalarElementType(Type type)
  {
    var nonNullableType = Nullable.GetUnderlyingType(type) ?? type;

    return nonNullableType.IsPrimitive ||
      nonNullableType.IsEnum ||
      nonNullableType == typeof(string) ||
      nonNullableType == typeof(decimal) ||
      nonNullableType == typeof(Guid) ||
      nonNullableType == typeof(DateTime) ||
      nonNullableType == typeof(DateTimeOffset) ||
      nonNullableType == typeof(DateOnly) ||
      nonNullableType == typeof(TimeOnly) ||
      nonNullableType == typeof(TimeSpan);
  }

  #endregion


  #region Helpers

  internal sealed class EntityStorageEstimate(
    StorageEntitySchema                 schema,
    double                              heapBytes,
    IReadOnlyList<StorageIndexEstimate> indexEstimates)
  {
    public StorageEntitySchema Schema { get; } = schema ?? throw new ArgumentNullException(nameof(schema));

    public double HeapBytes { get; } = heapBytes >= 0
      ? heapBytes
      : throw new ArgumentOutOfRangeException(nameof(heapBytes), "Heap bytes cannot be negative.");

    public IReadOnlyList<StorageIndexEstimate> IndexEstimates { get; } =
      indexEstimates ?? throw new ArgumentNullException(nameof(indexEstimates));
  }


  private sealed class PropertySizingInput(
    string        name,
    PropertyInfo? propertyInfo,
    Type          clrType,
    Type          providerClrType,
    string?       storeType,
    bool          isNullable,
    bool          isVariableLength,
    int?          maxLength,
    int?          precision,
    int?          scale)
  {
    public string Name { get; } = name;

    public PropertyInfo? PropertyInfo { get; } = propertyInfo;

    public Type ClrType { get; } = clrType;

    public Type ProviderClrType { get; } = providerClrType;

    public string? StoreType { get; } = storeType;

    public bool IsNullable { get; } = isNullable;

    public bool IsVariableLength { get; } = isVariableLength;

    public int? MaxLength { get; } = maxLength;

    public int? Precision { get; } = precision;

    public int? Scale { get; } = scale;

    public StoragePropertySchema ToSchema()
    {
      return new StoragePropertySchema(
        Name,
        ClrType,
        StoreType,
        IsNullable,
        IsVariableLength,
        MaxLength,
        Precision,
        Scale);
    }
  }


  private sealed class PropertySizeEstimate(
    string name,
    double bytesWhenPresent,
    int    alignmentBytes,
    double fillRate)
  {
    public string Name { get; } = name;

    public double BytesWhenPresent { get; } = bytesWhenPresent;

    public int AlignmentBytes { get; } = alignmentBytes;

    public double FillRate { get; } = fillRate;

    public double AverageStoredBytes => BytesWhenPresent * FillRate;
  }


  private readonly record struct TupleLayoutState(
    double Probability,
    double WeightedOffsetSum);

  #endregion
}
