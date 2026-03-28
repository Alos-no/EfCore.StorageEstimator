namespace EfCore.StorageEstimator.Estimation;

/// <summary>Represents the result of an estimation run.</summary>
public sealed class StorageEstimateReport
{
  #region Constructors

  /// <summary>Initializes a new instance of the <see cref="StorageEstimateReport" /> class.</summary>
  /// <param name="nodes">The traversed estimate nodes.</param>
  /// <param name="warnings">The warnings produced during estimation.</param>
  public StorageEstimateReport(
    IReadOnlyList<StorageEstimateNode> nodes,
    IReadOnlyList<string>              warnings)
  {
    Nodes    = nodes ?? throw new ArgumentNullException(nameof(nodes));
    Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
  }

  #endregion


  #region Properties & Fields - Public

  /// <summary>Gets the traversed estimate nodes.</summary>
  public IReadOnlyList<StorageEstimateNode> Nodes { get; }

  /// <summary>Gets the warnings produced during estimation.</summary>
  public IReadOnlyList<string> Warnings { get; }

  /// <summary>Gets the aggregate estimated rows across all traversed nodes.</summary>
  public double TotalEstimatedRows => Nodes.Sum(node => node.EstimatedRows);

  /// <summary>Gets the aggregate estimated heap bytes across all traversed nodes.</summary>
  public double TotalEstimatedHeapBytes => Nodes.Sum(node => node.EstimatedHeapBytes);

  /// <summary>Gets the aggregate estimated secondary-index bytes across all traversed nodes.</summary>
  public double TotalEstimatedIndexBytes => Nodes.Sum(node => node.EstimatedIndexBytes);

  /// <summary>Gets the aggregate estimated bytes across all traversed nodes.</summary>
  public double TotalEstimatedBytes => Nodes.Sum(node => node.EstimatedTotalBytes);

  #endregion
}

/// <summary>Represents one traversed node in the estimate output.</summary>
public sealed class StorageEstimateNode
{
  #region Constructors

  /// <summary>Initializes a new instance of the <see cref="StorageEstimateNode" /> class.</summary>
  /// <param name="path">The logical traversal path.</param>
  /// <param name="entityType">The CLR entity type represented by the node.</param>
  /// <param name="estimatedRows">The estimated number of rows represented by the node.</param>
  /// <param name="depth">The traversal depth starting at zero for roots.</param>
  /// <param name="schema">The optional EF Core schema facts captured for the node.</param>
  public StorageEstimateNode(
    string               path,
    Type                 entityType,
    double               estimatedRows,
    int                  depth,
    StorageEntitySchema? schema = null)
    : this(
      path,
      entityType,
      estimatedRows,
      depth,
      0,
      [],
      schema) { }


  /// <summary>Initializes a new instance of the <see cref="StorageEstimateNode" /> class with storage estimates.</summary>
  /// <param name="path">The logical traversal path.</param>
  /// <param name="entityType">The CLR entity type represented by the node.</param>
  /// <param name="estimatedRows">The estimated number of rows represented by the node.</param>
  /// <param name="depth">The traversal depth starting at zero for roots.</param>
  /// <param name="estimatedHeapBytes">The estimated heap bytes used by the represented rows.</param>
  /// <param name="indexEstimates">The estimated secondary-index storage for the represented rows.</param>
  /// <param name="schema">The optional EF Core schema facts captured for the node.</param>
  public StorageEstimateNode(
    string                               path,
    Type                                 entityType,
    double                               estimatedRows,
    int                                  depth,
    double                               estimatedHeapBytes,
    IReadOnlyList<StorageIndexEstimate>? indexEstimates = null,
    StorageEntitySchema?                 schema         = null)
  {
    Path = string.IsNullOrWhiteSpace(path)
      ? throw new ArgumentException("A node path is required.", nameof(path))
      : path;
    EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
    EstimatedRows = estimatedRows > 0
      ? estimatedRows
      : throw new ArgumentOutOfRangeException(nameof(estimatedRows), "Estimated rows must be positive.");
    Depth = depth >= 0
      ? depth
      : throw new ArgumentOutOfRangeException(nameof(depth), "Depth cannot be negative.");
    EstimatedHeapBytes = estimatedHeapBytes >= 0
      ? estimatedHeapBytes
      : throw new ArgumentOutOfRangeException(nameof(estimatedHeapBytes), "Estimated heap bytes cannot be negative.");
    IndexEstimates = indexEstimates ?? [];
    Schema         = schema;
  }

  #endregion


  #region Properties & Fields - Public

  /// <summary>Gets the logical traversal path.</summary>
  public string Path { get; }

  /// <summary>Gets the CLR entity type represented by the node.</summary>
  public Type EntityType { get; }

  /// <summary>Gets the estimated number of rows represented by the node.</summary>
  public double EstimatedRows { get; }

  /// <summary>Gets the traversal depth starting at zero for roots.</summary>
  public int Depth { get; }

  /// <summary>Gets the optional EF Core schema facts associated with the node.</summary>
  public StorageEntitySchema? Schema { get; }

  /// <summary>Gets the estimated heap bytes used by the represented rows.</summary>
  public double EstimatedHeapBytes { get; }

  /// <summary>Gets the estimated secondary-index storage associated with the represented rows.</summary>
  public IReadOnlyList<StorageIndexEstimate> IndexEstimates { get; }

  /// <summary>Gets the aggregate estimated secondary-index bytes associated with the represented rows.</summary>
  public double EstimatedIndexBytes => IndexEstimates.Sum(index => index.EstimatedBytes);

  /// <summary>Gets the aggregate estimated storage bytes associated with the represented rows.</summary>
  public double EstimatedTotalBytes => EstimatedHeapBytes + EstimatedIndexBytes;

  #endregion
}

/// <summary>Represents the EF Core schema facts captured for an entity node.</summary>
public sealed class StorageEntitySchema
{
  #region Constructors

  /// <summary>Initializes a new instance of the <see cref="StorageEntitySchema" /> class.</summary>
  /// <param name="tableName">The relational table name, when known.</param>
  /// <param name="propertyCount">The number of scalar properties defined on the entity.</param>
  /// <param name="indexCount">The number of indexes defined on the entity.</param>
  /// <param name="properties">The scalar property facts captured for the entity.</param>
  /// <param name="indexes">The secondary-index facts captured for the entity.</param>
  public StorageEntitySchema(
    string?                               tableName,
    int                                   propertyCount,
    int                                   indexCount,
    IReadOnlyList<StoragePropertySchema>? properties = null,
    IReadOnlyList<StorageIndexSchema>?    indexes    = null)
  {
    TableName = tableName;
    PropertyCount = propertyCount >= 0
      ? propertyCount
      : throw new ArgumentOutOfRangeException(nameof(propertyCount), "Property count cannot be negative.");
    IndexCount = indexCount >= 0
      ? indexCount
      : throw new ArgumentOutOfRangeException(nameof(indexCount), "Index count cannot be negative.");
    Properties = properties ?? [];
    Indexes    = indexes ?? [];
  }

  #endregion


  #region Properties & Fields - Public

  /// <summary>Gets the relational table name, when known.</summary>
  public string? TableName { get; }

  /// <summary>Gets the number of scalar properties defined on the entity.</summary>
  public int PropertyCount { get; }

  /// <summary>Gets the number of indexes defined on the entity.</summary>
  public int IndexCount { get; }

  /// <summary>Gets the scalar property facts captured for the entity.</summary>
  public IReadOnlyList<StoragePropertySchema> Properties { get; }

  /// <summary>Gets the secondary-index facts captured for the entity.</summary>
  public IReadOnlyList<StorageIndexSchema> Indexes { get; }

  #endregion
}

/// <summary>Represents one scalar property captured from the EF Core model.</summary>
public sealed class StoragePropertySchema
{
  #region Constructors

  /// <summary>Initializes a new instance of the <see cref="StoragePropertySchema" /> class.</summary>
  /// <param name="name">The CLR or model property name.</param>
  /// <param name="clrType">The CLR type exposed by the property.</param>
  /// <param name="storeType">The relational store type, when known.</param>
  /// <param name="isNullable">Whether the property allows null values.</param>
  /// <param name="isVariableLength">Whether the property is variable-width on disk.</param>
  /// <param name="maxLength">The configured maximum length, when known.</param>
  /// <param name="precision">The configured numeric precision, when known.</param>
  /// <param name="scale">The configured numeric scale, when known.</param>
  public StoragePropertySchema(
    string  name,
    Type    clrType,
    string? storeType,
    bool    isNullable,
    bool    isVariableLength,
    int?    maxLength = null,
    int?    precision = null,
    int?    scale     = null)
  {
    Name = string.IsNullOrWhiteSpace(name)
      ? throw new ArgumentException("A property name is required.", nameof(name))
      : name;
    ClrType          = clrType ?? throw new ArgumentNullException(nameof(clrType));
    StoreType        = storeType;
    IsNullable       = isNullable;
    IsVariableLength = isVariableLength;
    MaxLength        = maxLength;
    Precision        = precision;
    Scale            = scale;
  }

  #endregion


  #region Properties & Fields - Public

  /// <summary>Gets the CLR or model property name.</summary>
  public string Name { get; }

  /// <summary>Gets the CLR type exposed by the property.</summary>
  public Type ClrType { get; }

  /// <summary>Gets the relational store type, when known.</summary>
  public string? StoreType { get; }

  /// <summary>Gets a value indicating whether the property allows null values.</summary>
  public bool IsNullable { get; }

  /// <summary>Gets a value indicating whether the property is variable-width on disk.</summary>
  public bool IsVariableLength { get; }

  /// <summary>Gets the configured maximum length, when known.</summary>
  public int? MaxLength { get; }

  /// <summary>Gets the configured numeric precision, when known.</summary>
  public int? Precision { get; }

  /// <summary>Gets the configured numeric scale, when known.</summary>
  public int? Scale { get; }

  #endregion
}

/// <summary>Represents one secondary index captured from the EF Core model.</summary>
public sealed class StorageIndexSchema
{
  #region Constructors

  /// <summary>Initializes a new instance of the <see cref="StorageIndexSchema" /> class.</summary>
  /// <param name="name">The index name.</param>
  /// <param name="isUnique">Whether the index is unique.</param>
  /// <param name="columnCount">The number of indexed columns.</param>
  /// <param name="propertyNames">The indexed property names.</param>
  public StorageIndexSchema(
    string                 name,
    bool                   isUnique,
    int                    columnCount,
    IReadOnlyList<string>? propertyNames = null)
  {
    Name = string.IsNullOrWhiteSpace(name)
      ? throw new ArgumentException("An index name is required.", nameof(name))
      : name;
    IsUnique = isUnique;
    ColumnCount = columnCount > 0
      ? columnCount
      : throw new ArgumentOutOfRangeException(nameof(columnCount), "Column count must be positive.");
    PropertyNames = propertyNames ?? [];
  }

  #endregion


  #region Properties & Fields - Public

  /// <summary>Gets the index name.</summary>
  public string Name { get; }

  /// <summary>Gets a value indicating whether the index is unique.</summary>
  public bool IsUnique { get; }

  /// <summary>Gets the number of indexed columns.</summary>
  public int ColumnCount { get; }

  /// <summary>Gets the indexed property names.</summary>
  public IReadOnlyList<string> PropertyNames { get; }

  #endregion
}

/// <summary>Represents the storage estimate for one secondary index.</summary>
public sealed class StorageIndexEstimate
{
  #region Constructors

  /// <summary>Initializes a new instance of the <see cref="StorageIndexEstimate" /> class.</summary>
  /// <param name="name">The index name.</param>
  /// <param name="estimatedBytes">The estimated bytes used by the index.</param>
  /// <param name="averageEntryBytes">The estimated average bytes per index entry.</param>
  /// <param name="columnCount">The number of indexed columns.</param>
  /// <param name="isUnique">Whether the index is unique.</param>
  public StorageIndexEstimate(
    string name,
    double estimatedBytes,
    double averageEntryBytes,
    int    columnCount,
    bool   isUnique = false)
  {
    Name = string.IsNullOrWhiteSpace(name)
      ? throw new ArgumentException("An index name is required.", nameof(name))
      : name;
    EstimatedBytes = estimatedBytes >= 0
      ? estimatedBytes
      : throw new ArgumentOutOfRangeException(nameof(estimatedBytes), "Estimated bytes cannot be negative.");
    AverageEntryBytes = averageEntryBytes > 0
      ? averageEntryBytes
      : throw new ArgumentOutOfRangeException(nameof(averageEntryBytes), "Average entry bytes must be positive.");
    ColumnCount = columnCount > 0
      ? columnCount
      : throw new ArgumentOutOfRangeException(nameof(columnCount), "Column count must be positive.");
    IsUnique = isUnique;
  }

  #endregion


  #region Properties & Fields - Public

  /// <summary>Gets the index name.</summary>
  public string Name { get; }

  /// <summary>Gets the estimated bytes used by the index.</summary>
  public double EstimatedBytes { get; }

  /// <summary>Gets the estimated average bytes per index entry.</summary>
  public double AverageEntryBytes { get; }

  /// <summary>Gets the number of indexed columns.</summary>
  public int ColumnCount { get; }

  /// <summary>Gets a value indicating whether the index is unique.</summary>
  public bool IsUnique { get; }

  #endregion
}
