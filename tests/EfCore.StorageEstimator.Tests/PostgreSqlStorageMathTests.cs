namespace EfCore.StorageEstimator.Tests;

using Estimation;
using Microsoft.EntityFrameworkCore;
using Planning;
using Schema;

/// <summary>Focused tests for PostgreSQL row and index layout math.</summary>
[Trait("Category", "Unit")]
public sealed class PostgreSqlStorageMathTests
{
  #region EstimateClrEntity

  [Fact]
  public void EstimateClrEntity_WithInterleavedFixedWidthColumns_AccountsForRepeatedAttributeAlignment()
  {
    // Arrange
    var warnings = new List<string>();

    // Act
    var estimate = PostgreSqlStorageMath.EstimateClrEntity(
      typeof(InterleavedAlignmentEntity),
      nameof(InterleavedAlignmentEntity),
      180,
      warnings);

    // Assert
    estimate.HeapBytes.Should().Be(16384);
    warnings.Should().BeEmpty();
  }


  [Fact]
  public void EstimateClrEntity_WithNullableLeadingColumn_OnlyAddsHeapNullBitmapWhenRowsActuallyContainNulls()
  {
    // Arrange
    var warnings = new List<string>();

    // Act
    var estimate = PostgreSqlStorageMath.EstimateClrEntity(
      typeof(NullableLeadingColumnEntity),
      nameof(NullableLeadingColumnEntity),
      190,
      warnings);

    // Assert
    estimate.HeapBytes.Should().Be(8192);
    warnings.Should().BeEmpty();
  }

  #endregion


  #region EstimateEfEntity

  [Fact]
  public void EstimateEfEntity_WithNullableCompositeIndex_AccountsForIndexNullBitmapAndAlignment()
  {
    // Arrange
    using var dbContext = CreateDbContext();
    var warnings     = new List<string>();
    var schemaReader = new EfCoreSchemaReader(dbContext.Model);
    var schema       = schemaReader.GetEntitySchema(typeof(NullableCompositeIndexEntity));

    // Act
    var estimate = PostgreSqlStorageMath.EstimateEfEntity(
      schema,
      nameof(NullableCompositeIndexEntity),
      280,
      warnings);

    // Assert
    estimate.IndexEstimates.Should().ContainSingle(index =>
      index.Name == "ix_nullable_composite_entities_layout" &&
      index.EstimatedBytes == 24576 &&
      index.AverageEntryBytes == 32);
    warnings.Should().BeEmpty();
  }

  #endregion


  #region Helpers

  private static NullableIndexPlanningDbContext CreateDbContext()
  {
    var options = new DbContextOptionsBuilder<NullableIndexPlanningDbContext>()
                  .UseNpgsql("Host=localhost;Database=storage_estimator_math_tests;Username=test;Password=test")
                  .Options;

    return new NullableIndexPlanningDbContext(options);
  }

  #endregion


  #region Test Models

  private sealed class InterleavedAlignmentEntity
  {
    public bool FlagOne { get; set; }

    public int CountOne { get; set; }

    public bool FlagTwo { get; set; }

    public int CountTwo { get; set; }

    public bool FlagThree { get; set; }
  }


  private sealed class NullableLeadingColumnEntity
  {
    [StorageField(0.5d)]
    public bool? OptionalFlag { get; set; }

    public long Value { get; set; }
  }


  [StorageEntity(280)]
  private sealed class NullableCompositeIndexEntity
  {
    public int GroupId { get; set; }

    [StorageField(0.5d)]
    public bool? OptionalFlag { get; set; }

    public int Sequence { get; set; }

    public bool IsActive { get; set; }
  }


  private sealed class NullableIndexPlanningDbContext : DbContext
  {
    public NullableIndexPlanningDbContext(DbContextOptions<NullableIndexPlanningDbContext> options)
      : base(options) { }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<NullableCompositeIndexEntity>(entity =>
      {
        entity.ToTable("nullable_composite_entities");
        entity.HasKey(item => item.GroupId);
        entity.HasIndex(item => new
        {
          item.GroupId,
          item.OptionalFlag,
          item.Sequence,
          item.IsActive
        }).HasDatabaseName("ix_nullable_composite_entities_layout");
      });
    }
  }

  #endregion
}
