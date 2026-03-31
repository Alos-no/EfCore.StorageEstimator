namespace EfCore.StorageEstimator.Schema;

using System.Reflection;
using Estimation;
using Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

/// <summary>Reads EF Core schema metadata from an <see cref="IModel" /> for estimator consumption.</summary>
internal sealed class EfCoreSchemaReader(IModel model)
{
  #region Properties & Fields - Non-Public

  private readonly Dictionary<Type, EfCoreEntitySchema>        _cache       = [];
  private readonly Dictionary<IEntityType, EfCoreEntitySchema> _entityCache = [];

  #endregion


  #region Methods

  public EfCoreEntitySchema GetEntitySchema(Type clrType)
  {
    ArgumentNullException.ThrowIfNull(clrType);

    if (_cache.TryGetValue(clrType, out var cachedSchema))
      return cachedSchema;

    var entityType = model.FindEntityType(clrType);

    if (entityType is null)
    {
      var matches = model
                    .GetEntityTypes()
                    .Where(candidate => candidate.ClrType == clrType)
                    .ToArray();

      if (matches.Length == 1)
        entityType = matches[0];
      else if (matches.Length > 1)
        throw new StorageEstimatorConfigurationException(
          $"Entity '{clrType.Name}' is present multiple times in the supplied EF Core model. Use a navigation-backed traversal path instead of a CLR-only lookup.");
    }

    if (entityType is null)
      throw new StorageEstimatorConfigurationException(
        $"Entity '{clrType.Name}' is not present in the supplied EF Core model.");

    var schema = GetEntitySchema(entityType);

    _cache.TryAdd(clrType, schema);

    return schema;
  }


  public EfCoreEntitySchema GetEntitySchema(IEntityType entityType)
  {
    ArgumentNullException.ThrowIfNull(entityType);

    if (_entityCache.TryGetValue(entityType, out var cachedSchema))
      return cachedSchema;

    var properties     = CreatePropertySchemas(entityType).ToArray();
    var propertyLookup = properties.ToDictionary(property => property.Name, StringComparer.Ordinal);
    var indexes = entityType
                  .GetIndexes()
                  .Select(index => CreateIndexSchema(index, propertyLookup))
                  .ToArray();
    var entitySchema = new EfCoreEntitySchema(
      entityType.ClrType,
      new StorageEntitySchema(
        entityType.GetTableName(),
        properties.Length,
        indexes.Length,
        properties.Select(property => property.Schema).ToArray(),
        indexes.Select(index => index.Schema).ToArray()),
      properties,
      indexes,
      CreateNavigationSchemas(entityType).ToArray());

    _entityCache.Add(entityType, entitySchema);
    _cache.TryAdd(entityType.ClrType, entitySchema);

    return entitySchema;
  }

  #endregion


  #region Methods - Private

  private static IEnumerable<EfCorePropertySchema> CreatePropertySchemas(IEntityType entityType)
  {
    foreach (var property in entityType.GetProperties())
      yield return CreatePropertySchema(property);

    foreach (var complexProperty in entityType.GetComplexProperties())
    foreach (var property in CreateComplexPropertySchemas(
               complexProperty.ComplexType,
               complexProperty.Name))
      yield return property;

    foreach (var navigation in entityType.GetNavigations().Where(IsInlineOwnedNavigation))
    foreach (var property in CreateOwnedPropertySchemas(
               navigation.TargetEntityType,
               navigation.Name))
      yield return property;
  }


  private static IEnumerable<EfCorePropertySchema> CreateComplexPropertySchemas(
    IComplexType complexType,
    string       prefix)
  {
    foreach (var property in complexType.GetProperties())
      yield return CreatePropertySchema(property, $"{prefix}.{property.Name}");

    foreach (var nestedComplexProperty in complexType.GetComplexProperties().Where(property => !property.IsCollection))
    foreach (var property in CreateComplexPropertySchemas(
               nestedComplexProperty.ComplexType,
               $"{prefix}.{nestedComplexProperty.Name}"))
      yield return property;
  }


  private static IEnumerable<EfCorePropertySchema> CreateOwnedPropertySchemas(
    IEntityType ownedEntityType,
    string      prefix)
  {
    foreach (var property in ownedEntityType.GetProperties().Where(property => !ShouldSkipInlineOwnedProperty(ownedEntityType, property)))
      yield return CreatePropertySchema(property, $"{prefix}.{property.Name}");

    foreach (var complexProperty in ownedEntityType.GetComplexProperties())
    foreach (var property in CreateComplexPropertySchemas(
               complexProperty.ComplexType,
               $"{prefix}.{complexProperty.Name}"))
      yield return property;

    foreach (var navigation in ownedEntityType.GetNavigations().Where(IsInlineOwnedNavigation))
    foreach (var property in CreateOwnedPropertySchemas(
               navigation.TargetEntityType,
               $"{prefix}.{navigation.Name}"))
      yield return property;
  }


  private static IEnumerable<EfCoreNavigationSchema> CreateNavigationSchemas(IEntityType entityType)
  {
    foreach (var navigation in entityType.GetNavigations().Where(navigation => !IsInlineOwnedNavigation(navigation)))
      yield return new EfCoreNavigationSchema(
        navigation.Name,
        navigation.TargetEntityType.ClrType,
        navigation.TargetEntityType,
        navigation.PropertyInfo);

    foreach (var skipNavigation in entityType.GetSkipNavigations())
      yield return new EfCoreNavigationSchema(
        skipNavigation.Name,
        skipNavigation.TargetEntityType.ClrType,
        skipNavigation.TargetEntityType,
        skipNavigation.PropertyInfo);
  }


  private static EfCorePropertySchema CreatePropertySchema(IProperty property)
  {
    return CreatePropertySchema(property, property.Name);
  }


  private static EfCorePropertySchema CreatePropertySchema(
    IProperty property,
    string    propertyName)
  {
    var providerClrType = property.GetProviderClrType()
      ?? property.GetValueConverter()?.ProviderClrType
      ?? property.ClrType;
    var storeType        = property.GetColumnType();
    var isVariableLength = IsVariableLength(property.ClrType, providerClrType, storeType);

    return new EfCorePropertySchema(
      propertyName,
      property.PropertyInfo,
      property.ClrType,
      providerClrType,
      new StoragePropertySchema(
        propertyName,
        property.ClrType,
        storeType,
        property.IsNullable,
        isVariableLength,
        property.GetMaxLength(),
        property.GetPrecision(),
        property.GetScale()));
  }


  private static EfCoreIndexSchema CreateIndexSchema(
    IIndex                                            index,
    IReadOnlyDictionary<string, EfCorePropertySchema> propertyLookup)
  {
    var properties = index.Properties
                          .Select(property => propertyLookup[property.Name])
                          .ToArray();
    var name = index.GetDatabaseName()
      ?? index.Name
      ?? string.Join("_", properties.Select(property => property.Name));

    return new EfCoreIndexSchema(
      new StorageIndexSchema(
        name,
        index.IsUnique,
        properties.Length,
        properties.Select(property => property.Name).ToArray()),
      properties);
  }


  private static bool ShouldSkipInlineOwnedProperty(
    IEntityType ownedEntityType,
    IProperty   property)
  {
    var ownership = ownedEntityType.FindOwnership();

    if (ownership is not null && ownership.Properties.Any(foreignKeyProperty => foreignKeyProperty.Name == property.Name))
      return true;

    return property.IsPrimaryKey();
  }


  private static bool IsInlineOwnedNavigation(INavigation navigation)
  {
    if (!navigation.ForeignKey.IsOwnership || navigation.IsCollection)
      return false;

    return string.Equals(
        navigation.DeclaringEntityType.GetTableName(),
        navigation.TargetEntityType.GetTableName(),
        StringComparison.Ordinal) &&
      string.Equals(
        navigation.DeclaringEntityType.GetSchema(),
        navigation.TargetEntityType.GetSchema(),
        StringComparison.Ordinal);
  }


  private static bool IsVariableLength(
    Type    clrType,
    Type    providerClrType,
    string? storeType)
  {
    if (IsScalarCollectionType(clrType) || IsScalarCollectionType(providerClrType))
      return true;

    if (clrType == typeof(string) ||
        clrType == typeof(byte[]) ||
        providerClrType == typeof(string) ||
        providerClrType == typeof(byte[]))
      return true;

    if (clrType == typeof(decimal) || clrType == typeof(decimal?) ||
        providerClrType == typeof(decimal) || providerClrType == typeof(decimal?))
      return true;

    if (string.IsNullOrWhiteSpace(storeType))
      return false;

    return storeType.Contains("char", StringComparison.OrdinalIgnoreCase) ||
      storeType.Contains("text", StringComparison.OrdinalIgnoreCase) ||
      storeType.Contains("bytea", StringComparison.OrdinalIgnoreCase) ||
      storeType.Contains("geometry", StringComparison.OrdinalIgnoreCase) ||
      storeType.Contains("geography", StringComparison.OrdinalIgnoreCase) ||
      storeType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
      storeType.Contains("[]", StringComparison.Ordinal) ||
      storeType.Contains("numeric", StringComparison.OrdinalIgnoreCase) ||
      storeType.Contains("decimal", StringComparison.OrdinalIgnoreCase);
  }

  private static bool IsScalarCollectionType(Type type)
  {
    if (type == typeof(string) || type == typeof(byte[]))
      return false;

    if (type.IsArray)
    {
      var elementType = type.GetElementType();

      return elementType is not null && elementType != typeof(byte);
    }

    return false;
  }

  #endregion
}

/// <summary>Represents the EF Core schema facts for one entity type.</summary>
internal sealed class EfCoreEntitySchema(
  Type                                  clrType,
  StorageEntitySchema                   schema,
  IReadOnlyList<EfCorePropertySchema>   properties,
  IReadOnlyList<EfCoreIndexSchema>      indexes,
  IReadOnlyList<EfCoreNavigationSchema> navigations)
{
  #region Properties & Fields - Public

  public Type ClrType { get; } = clrType ?? throw new ArgumentNullException(nameof(clrType));

  public StorageEntitySchema Schema { get; } = schema ?? throw new ArgumentNullException(nameof(schema));

  public IReadOnlyList<EfCorePropertySchema> Properties { get; } = properties ?? throw new ArgumentNullException(nameof(properties));

  public IReadOnlyList<EfCoreIndexSchema> Indexes { get; } = indexes ?? throw new ArgumentNullException(nameof(indexes));

  public IReadOnlyList<EfCoreNavigationSchema> Navigations { get; } = navigations ?? throw new ArgumentNullException(nameof(navigations));

  #endregion
}

/// <summary>Represents one EF Core scalar property.</summary>
internal sealed class EfCorePropertySchema(
  string                name,
  PropertyInfo?         propertyInfo,
  Type                  clrType,
  Type                  providerClrType,
  StoragePropertySchema schema)
{
  #region Properties & Fields - Public

  public string Name { get; } = string.IsNullOrWhiteSpace(name)
    ? throw new ArgumentException("A property name is required.", nameof(name))
    : name;

  public PropertyInfo? PropertyInfo { get; } = propertyInfo;

  public Type ClrType { get; } = clrType ?? throw new ArgumentNullException(nameof(clrType));

  public Type ProviderClrType { get; } = providerClrType ?? throw new ArgumentNullException(nameof(providerClrType));

  public StoragePropertySchema Schema { get; } = schema ?? throw new ArgumentNullException(nameof(schema));

  #endregion
}

/// <summary>Represents one EF Core secondary index.</summary>
internal sealed class EfCoreIndexSchema(
  StorageIndexSchema                  schema,
  IReadOnlyList<EfCorePropertySchema> properties)
{
  #region Properties & Fields - Public

  public StorageIndexSchema Schema { get; } = schema ?? throw new ArgumentNullException(nameof(schema));

  public IReadOnlyList<EfCorePropertySchema> Properties { get; } = properties ?? throw new ArgumentNullException(nameof(properties));

  #endregion
}

/// <summary>Represents one EF Core navigation edge.</summary>
internal sealed class EfCoreNavigationSchema(
  string        name,
  Type          targetClrType,
  IEntityType   targetEntityType,
  PropertyInfo? propertyInfo)
{
  #region Properties & Fields - Public

  public string Name { get; } = string.IsNullOrWhiteSpace(name)
    ? throw new ArgumentException("A navigation name is required.", nameof(name))
    : name;

  public Type TargetClrType { get; } = targetClrType ?? throw new ArgumentNullException(nameof(targetClrType));

  public IEntityType TargetEntityType { get; } = targetEntityType ?? throw new ArgumentNullException(nameof(targetEntityType));

  public PropertyInfo? PropertyInfo { get; } = propertyInfo;

  #endregion
}
