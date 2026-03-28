namespace EfCore.StorageEstimator.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>Validates storage-planning metadata usage.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StoragePlanningAnalyzer : DiagnosticAnalyzer
{
  #region Constants & Statics

  private const string AverageLengthPropertyName              = "AverageLength";
  private const string StorageEntityAttributeMetadataName     = "EfCore.StorageEstimator.Planning.StorageEntityAttribute";
  private const string StorageFieldAttributeMetadataName      = "EfCore.StorageEstimator.Planning.StorageFieldAttribute";
  private const string StorageNavigationAttributeMetadataName = "EfCore.StorageEstimator.Planning.StorageNavigationAttribute";

  internal static readonly DiagnosticDescriptor NavigationTargetMustBePlannedEntityRule = new(
    "EFSA001",
    "Storage navigation target must be a planned entity",
    "Property '{0}' targets '{1}', which is not annotated with [StorageEntity]",
    "Usage",
    DiagnosticSeverity.Warning,
    true,
    "StorageNavigationAttribute should only target entity types that also declare StorageEntityAttribute.");

  internal static readonly DiagnosticDescriptor ExpectedRowCountMustBePositiveRule = new(
    "EFSA002",
    "Storage entity expected row count must be positive",
    "Type '{0}' sets an expected row count of '{1}', which must be positive",
    "Usage",
    DiagnosticSeverity.Warning,
    true,
    "StorageEntityAttribute expected row count must be greater than zero.");

  internal static readonly DiagnosticDescriptor FillRateMustBeBetweenZeroAndOneRule = new(
    "EFSA003",
    "Storage field fill rate must be between 0 and 1",
    "Property '{0}' sets a fill rate of '{1}', which must be between 0 and 1 inclusive",
    "Usage",
    DiagnosticSeverity.Warning,
    true,
    "StorageFieldAttribute fill rate must be between zero and one inclusive.");

  internal static readonly DiagnosticDescriptor AverageLengthMustBeZeroOrGreaterRule = new(
    "EFSA004",
    "Storage field average length must be zero or greater",
    "Property '{0}' sets AverageLength to '{1}', which must be zero or greater",
    "Usage",
    DiagnosticSeverity.Warning,
    true,
    "StorageFieldAttribute AverageLength must be zero or greater.");

  internal static readonly DiagnosticDescriptor AverageMultiplicityMustBePositiveRule = new(
    "EFSA005",
    "Storage navigation average multiplicity must be positive",
    "Property '{0}' sets an average multiplicity of '{1}', which must be positive",
    "Usage",
    DiagnosticSeverity.Warning,
    true,
    "StorageNavigationAttribute average multiplicity must be greater than zero.");

  #endregion


  #region Properties & Fields - Public

  /// <inheritdoc />
  public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
  [
    NavigationTargetMustBePlannedEntityRule,
    ExpectedRowCountMustBePositiveRule,
    FillRateMustBeBetweenZeroAndOneRule,
    AverageLengthMustBeZeroOrGreaterRule,
    AverageMultiplicityMustBePositiveRule
  ];

  #endregion


  #region Methods Impl

  /// <inheritdoc />
  public override void Initialize(AnalysisContext context)
  {
    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
    context.EnableConcurrentExecution();

    context.RegisterCompilationStartAction(static startContext =>
    {
      var storageEntityAttribute     = startContext.Compilation.GetTypeByMetadataName(StorageEntityAttributeMetadataName);
      var storageFieldAttribute      = startContext.Compilation.GetTypeByMetadataName(StorageFieldAttributeMetadataName);
      var storageNavigationAttribute = startContext.Compilation.GetTypeByMetadataName(StorageNavigationAttributeMetadataName);

      if (storageEntityAttribute is null)
        if (storageFieldAttribute is null && storageNavigationAttribute is null)
          return;

      if (storageEntityAttribute is not null)
        startContext.RegisterSymbolAction(
          symbolContext => AnalyzeNamedType(symbolContext, storageEntityAttribute),
          SymbolKind.NamedType);

      if (storageFieldAttribute is null && storageNavigationAttribute is null)
        return;

      startContext.RegisterSymbolAction(
        symbolContext => AnalyzeProperty(symbolContext, storageEntityAttribute, storageFieldAttribute, storageNavigationAttribute),
        SymbolKind.Property);
    });
  }

  #endregion


  #region Methods - Private

  private static void AnalyzeNamedType(
    SymbolAnalysisContext context,
    INamedTypeSymbol      storageEntityAttribute)
  {
    var typeSymbol = (INamedTypeSymbol)context.Symbol;
    var attribute  = GetAttribute(typeSymbol, storageEntityAttribute);

    if (attribute is null)
      return;

    if (!TryGetDoubleConstructorArgument(attribute, 0, out var expectedRowCount) ||
        expectedRowCount > 0)
      return;

    context.ReportDiagnostic(Diagnostic.Create(
                               ExpectedRowCountMustBePositiveRule,
                               GetLocation(typeSymbol, attribute, context),
                               typeSymbol.Name,
                               expectedRowCount));
  }


  private static void AnalyzeProperty(
    SymbolAnalysisContext context,
    INamedTypeSymbol?     storageEntityAttribute,
    INamedTypeSymbol?     storageFieldAttribute,
    INamedTypeSymbol?     storageNavigationAttribute)
  {
    var property = (IPropertySymbol)context.Symbol;
    var storageField = storageFieldAttribute is null
      ? null
      : GetAttribute(property, storageFieldAttribute);
    var storageNavigation = storageNavigationAttribute is null
      ? null
      : GetAttribute(property, storageNavigationAttribute);

    AnalyzeFieldAttribute(context, property, storageField);

    if (storageNavigation is null)
      return;

    AnalyzeNavigationAttribute(context, property, storageNavigation);

    var targetType = GetNavigationTargetType(property.Type);

    if (targetType is null || targetType.TypeKind == TypeKind.Error)
      return;

    if (storageEntityAttribute is not null && HasAttribute(targetType, storageEntityAttribute))
      return;

    if (storageEntityAttribute is null)
      return;

    context.ReportDiagnostic(Diagnostic.Create(
                               NavigationTargetMustBePlannedEntityRule,
                               GetLocation(property, storageNavigation, context),
                               property.Name,
                               targetType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
  }


  private static void AnalyzeFieldAttribute(
    SymbolAnalysisContext context,
    IPropertySymbol       property,
    AttributeData?        storageField)
  {
    if (storageField is null)
      return;

    if (TryGetDoubleConstructorArgument(storageField, 0, out var fillRate) &&
        (fillRate < 0 || fillRate > 1))
      context.ReportDiagnostic(Diagnostic.Create(
                                 FillRateMustBeBetweenZeroAndOneRule,
                                 GetLocation(property, storageField, context),
                                 property.Name,
                                 fillRate));

    if (!TryGetNamedIntArgument(storageField, AverageLengthPropertyName, out var averageLength) ||
        averageLength >= 0)
      return;

    context.ReportDiagnostic(Diagnostic.Create(
                               AverageLengthMustBeZeroOrGreaterRule,
                               GetLocation(property, storageField, context),
                               property.Name,
                               averageLength));
  }


  private static void AnalyzeNavigationAttribute(
    SymbolAnalysisContext context,
    IPropertySymbol       property,
    AttributeData         storageNavigation)
  {
    if (!TryGetDoubleConstructorArgument(storageNavigation, 0, out var averageMultiplicity) ||
        averageMultiplicity > 0)
      return;

    context.ReportDiagnostic(Diagnostic.Create(
                               AverageMultiplicityMustBePositiveRule,
                               GetLocation(property, storageNavigation, context),
                               property.Name,
                               averageMultiplicity));
  }


  private static AttributeData? GetAttribute(ISymbol symbol, INamedTypeSymbol attributeSymbol)
  {
    return symbol
           .GetAttributes()
           .FirstOrDefault(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeSymbol));
  }


  private static Location GetLocation(
    ISymbol               symbol,
    AttributeData         attribute,
    SymbolAnalysisContext context)
  {
    return attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
      ?? symbol.Locations.FirstOrDefault()
      ?? Location.None;
  }


  private static bool TryGetDoubleConstructorArgument(
    AttributeData attribute,
    int           argumentIndex,
    out double    value)
  {
    if (attribute.ConstructorArguments.Length <= argumentIndex)
    {
      value = default;
      return false;
    }

    var argument = attribute.ConstructorArguments[argumentIndex];

    if (argument.Value is double doubleValue)
    {
      value = doubleValue;
      return true;
    }

    value = default;
    return false;
  }


  private static bool TryGetNamedIntArgument(
    AttributeData attribute,
    string        argumentName,
    out int       value)
  {
    var namedArgument = attribute.NamedArguments.FirstOrDefault(argument => argument.Key == argumentName);

    if (namedArgument.Equals(default(KeyValuePair<string, TypedConstant>)) || namedArgument.Value.Value is not int intValue)
    {
      value = default;
      return false;
    }

    value = intValue;
    return true;
  }


  private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attributeSymbol)
  {
    return GetAttribute(symbol, attributeSymbol) is not null;
  }


  private static ITypeSymbol? GetNavigationTargetType(ITypeSymbol propertyType)
  {
    if (propertyType is IArrayTypeSymbol arrayType)
      return arrayType.ElementType;

    if (propertyType is INamedTypeSymbol namedType)
    {
      if (namedType.IsGenericType &&
          namedType.TypeArguments.Length == 1 &&
          namedType.AllInterfaces.Any(@interface =>
                                        @interface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T))
        return namedType.TypeArguments[0];

      var enumerableInterface = namedType
                                .AllInterfaces
                                .FirstOrDefault(@interface =>
                                                  @interface.OriginalDefinition.SpecialType
                                                  == SpecialType.System_Collections_Generic_IEnumerable_T);

      if (enumerableInterface is not null && enumerableInterface.TypeArguments.Length == 1)
        return enumerableInterface.TypeArguments[0];
    }

    return propertyType;
  }

  #endregion
}
