namespace EfCore.StorageEstimator.Tests;

using System.Text.Json;
using Estimation;
using Microsoft.EntityFrameworkCore;
using Planning;

/// <summary>Integration tests covering broader EF Core model shapes.</summary>
[Trait("Category", "Integration")]
public sealed class EfCoreModelIntegrationTests
{
  #region Estimate

  [Fact]
  public void Estimate_WithComplexOwnedSkipNavigationAndProviderSpecificTypes_UsesTheExpandedEfCoreModel()
  {
    // Arrange
    using var dbContext = CreateDbContext();
    var       service   = CreateService();
    var request = new StorageEstimateRequest
    {
      Model = dbContext.Model,
      Roots =
      [
        new StorageTraversalRoot(typeof(AdvancedRoot))
      ]
    };

    // Act
    var report = service.Estimate(request);

    // Assert
    report.Nodes.Should().HaveCount(3);

    var rootNode = report.Nodes.Single(node => node.EntityType == typeof(AdvancedRoot));
    rootNode.EstimatedRows.Should().Be(5);
    rootNode.Schema.Should().NotBeNull();
    rootNode.Schema!.TableName.Should().Be("advanced_roots");
    rootNode.Schema.PropertyCount.Should().Be(10);
    rootNode.Schema.IndexCount.Should().Be(2);
    rootNode.Schema.Properties.Select(property => property.Name).Should().Contain(
    [
      nameof(AdvancedRoot.Name),
      nameof(AdvancedRoot.Labels),
      nameof(AdvancedRoot.Metadata),
      nameof(AdvancedRoot.Cost),
      $"{nameof(AdvancedRoot.Audit)}.{nameof(AuditStamp.CreatedBy)}",
      $"{nameof(AdvancedRoot.Audit)}.{nameof(AuditStamp.CreatedAt)}",
      $"{nameof(AdvancedRoot.Settings)}.{nameof(AdvancedSettings.IsEnabled)}",
      $"{nameof(AdvancedRoot.Settings)}.{nameof(AdvancedSettings.Notes)}",
      "ShadowCode"
    ]);
    rootNode.Schema.Properties.Single(property => property.Name == nameof(AdvancedRoot.Labels)).IsVariableLength.Should().BeTrue();
    rootNode.Schema.Properties.Single(property => property.Name == nameof(AdvancedRoot.Metadata)).IsVariableLength.Should().BeTrue();
    rootNode.Schema.Properties.Single(property => property.Name == nameof(AdvancedRoot.Cost)).IsVariableLength.Should().BeTrue();
    rootNode.Schema.Indexes.Should().ContainSingle(index =>
                                                     index.Name == "ix_advanced_roots_name_cost" &&
                                                     index.ColumnCount == 2);
    rootNode.Schema.Indexes.Should().ContainSingle(index =>
                                                     index.Name == "ix_advanced_roots_shadow_code" &&
                                                     index.PropertyNames.SequenceEqual(new[]
                                                     {
                                                       "ShadowCode"
                                                     }));
    rootNode.EstimatedHeapBytes.Should().BeGreaterThan(0);
    rootNode.EstimatedIndexBytes.Should().BeGreaterThan(0);

    report.Nodes.Should().ContainSingle(node =>
                                          node.Path == $"{nameof(AdvancedRoot)}.{nameof(AdvancedRoot.Metrics)}" &&
                                          node.EntityType == typeof(AdvancedMetric) &&
                                          node.EstimatedRows == 10);
    report.Nodes.Should().ContainSingle(node =>
                                          node.Path == $"{nameof(AdvancedRoot)}.{nameof(AdvancedRoot.Tags)}" &&
                                          node.EntityType == typeof(AdvancedTag) &&
                                          node.EstimatedRows == 15);
    report.Nodes.Should().NotContain(node => node.Path == $"{nameof(AdvancedRoot)}.{nameof(AdvancedRoot.Settings)}");
    report.Warnings.Should().NotContain(warning => warning.Contains($"{nameof(AdvancedRoot)}.{nameof(AdvancedRoot.Tags)}"));
    report.Warnings.Should().NotContain(warning => warning.Contains($"{nameof(AdvancedRoot)}.{nameof(AdvancedRoot.Labels)}"));
    report.Warnings.Should().NotContain(warning => warning.Contains($"{nameof(AdvancedRoot)}.{nameof(AdvancedRoot.Metadata)}"));
  }


  [Fact]
  public void Estimate_WithInheritanceMappingStrategies_ReadsDerivedRootsAcrossTphTptAndTpc()
  {
    // Arrange
    using var dbContext = CreateDbContext();
    var       service   = CreateService();
    var request = new StorageEstimateRequest
    {
      Model = dbContext.Model,
      Roots =
      [
        new StorageTraversalRoot(typeof(TphSensorRecord)),
        new StorageTraversalRoot(typeof(TptSensorRecord)),
        new StorageTraversalRoot(typeof(TpcSensorRecord))
      ]
    };

    // Act
    var report = service.Estimate(request);

    // Assert
    report.Nodes.Should().HaveCount(3);
    report.Warnings.Should().BeEmpty();

    report.Nodes.Should().ContainSingle(node =>
                                          node.EntityType == typeof(TphSensorRecord) &&
                                          node.Schema!.TableName == "tph_sensor_records" &&
                                          node.Schema.PropertyCount >= 3);
    report.Nodes.Should().ContainSingle(node =>
                                          node.EntityType == typeof(TptSensorRecord) &&
                                          node.Schema!.TableName == "tpt_sensor_records" &&
                                          node.Schema.PropertyCount >= 3);
    report.Nodes.Should().ContainSingle(node =>
                                          node.EntityType == typeof(TpcSensorRecord) &&
                                          node.Schema!.TableName == "tpc_sensor_records" &&
                                          node.Schema.PropertyCount >= 3);
  }

  #endregion


  #region Helpers

  private static IStorageEstimator CreateService()
  {
    return new StorageEstimator();
  }


  private static AdvancedPlanningDbContext CreateDbContext()
  {
    var options = new DbContextOptionsBuilder<AdvancedPlanningDbContext>()
                  .UseNpgsql("Host=localhost;Database=storage_estimator_integration_tests;Username=test;Password=test")
                  .Options;

    return new AdvancedPlanningDbContext(options);
  }

  #endregion


  #region Test Models

  [StorageEntity(5)]
  private sealed class AdvancedRoot
  {
    public int Id { get; set; }

    [StorageField(AverageLength = 24)]
    public string Name { get; set; } = string.Empty;

    [StorageField(AverageLength = 96)]
    public string[] Labels { get; set; } = [];

    [StorageField(0.5d, AverageLength = 320)]
    public JsonDocument? Metadata { get; set; }

    public decimal Cost { get; set; }

    public AuditStamp Audit { get; set; } = new();

    public AdvancedSettings Settings { get; set; } = new();

    [StorageNavigation(2)]
    public IReadOnlyList<AdvancedMetric> Metrics { get; } = [];

    [StorageNavigation(3)]
    public IReadOnlyList<AdvancedTag> Tags { get; } = [];
  }


  private sealed class AuditStamp
  {
    public DateTimeOffset CreatedAt { get; set; }

    [StorageField(AverageLength = 32)]
    public string CreatedBy { get; set; } = string.Empty;
  }


  private sealed class AdvancedSettings
  {
    public bool IsEnabled { get; set; }

    [StorageField(0.25d, AverageLength = 80)]
    public string? Notes { get; set; }
  }


  [StorageEntity(1)]
  private sealed class AdvancedMetric
  {
    public int Id { get; set; }

    [StorageField(AverageLength = 16)]
    public string Code { get; set; } = string.Empty;
  }


  [StorageEntity(1)]
  private sealed class AdvancedTag
  {
    public int Id { get; set; }

    [StorageField(AverageLength = 12)]
    public string Value { get; set; } = string.Empty;

    public IReadOnlyList<AdvancedRoot> Roots { get; } = [];
  }


  private abstract class TphRecordBase
  {
    public int Id { get; set; }

    public DateOnly CapturedOn { get; set; }
  }


  [StorageEntity(7)]
  private sealed class TphSensorRecord : TphRecordBase
  {
    public short Temperature { get; set; }
  }


  private abstract class TptRecordBase
  {
    public int Id { get; set; }

    public TimeOnly CapturedAt { get; set; }
  }


  [StorageEntity(8)]
  private sealed class TptSensorRecord : TptRecordBase
  {
    public Guid SensorId { get; set; }
  }


  private abstract class TpcRecordBase
  {
    public int Id { get; set; }

    public Guid BatchId { get; set; }
  }


  [StorageEntity(9)]
  private sealed class TpcSensorRecord : TpcRecordBase
  {
    public bool IsHealthy { get; set; }
  }


  private sealed class AdvancedPlanningDbContext : DbContext
  {
    public AdvancedPlanningDbContext(DbContextOptions<AdvancedPlanningDbContext> options)
      : base(options) { }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<AdvancedRoot>(entity =>
      {
        entity.ToTable("advanced_roots");
        entity.HasKey(root => root.Id);
        entity.Property(root => root.Name).HasMaxLength(64);
        entity.Property(root => root.Labels).HasColumnType("text[]");
        entity.Property(root => root.Metadata).HasColumnType("jsonb");
        entity.Property(root => root.Cost).HasPrecision(12, 2);
        entity.Property<string>("ShadowCode").HasMaxLength(40);
        entity.HasIndex(root => new
        {
          root.Name,
          root.Cost
        }).HasDatabaseName("ix_advanced_roots_name_cost");
        entity.HasIndex("ShadowCode").HasDatabaseName("ix_advanced_roots_shadow_code");
        entity.ComplexProperty(root => root.Audit, complex =>
        {
          complex.Property(audit => audit.CreatedAt).HasColumnType("timestamp with time zone");
          complex.Property(audit => audit.CreatedBy).HasMaxLength(64);
        });
        entity.OwnsOne(root => root.Settings, owned => { owned.Property(settings => settings.Notes).HasColumnType("text"); });
        entity.OwnsMany(root => root.Metrics, owned =>
        {
          owned.ToTable("advanced_metrics");
          owned.WithOwner().HasForeignKey("AdvancedRootId");
          owned.Property(metric => metric.Code).HasMaxLength(16);
          owned.Property<int>("Id");
          owned.HasKey("Id");
          owned.HasIndex(metric => metric.Code).HasDatabaseName("ix_advanced_metrics_code");
        });
        entity.HasMany(root => root.Tags).WithMany(tag => tag.Roots).UsingEntity("advanced_root_tags");
      });

      modelBuilder.Entity<AdvancedTag>(entity =>
      {
        entity.ToTable("advanced_tags");
        entity.HasKey(tag => tag.Id);
        entity.Property(tag => tag.Value).HasMaxLength(32);
        entity.HasIndex(tag => tag.Value).HasDatabaseName("ix_advanced_tags_value");
      });

      modelBuilder.Entity<TphRecordBase>(entity =>
      {
        entity.ToTable("tph_sensor_records");
        entity.HasDiscriminator<string>("record_type")
              .HasValue<TphSensorRecord>("sensor");
      });

      modelBuilder.Entity<TphSensorRecord>(entity => { entity.Property(record => record.CapturedOn).HasColumnType("date"); });

      modelBuilder.Entity<TptRecordBase>(entity =>
      {
        entity.UseTptMappingStrategy();
        entity.ToTable("tpt_record_bases");
      });

      modelBuilder.Entity<TptSensorRecord>(entity =>
      {
        entity.ToTable("tpt_sensor_records");
        entity.Property(record => record.CapturedAt).HasColumnType("time without time zone");
      });

      modelBuilder.Entity<TpcRecordBase>(entity => { entity.UseTpcMappingStrategy(); });

      modelBuilder.Entity<TpcSensorRecord>(entity => { entity.ToTable("tpc_sensor_records"); });
    }
  }

  #endregion
}
