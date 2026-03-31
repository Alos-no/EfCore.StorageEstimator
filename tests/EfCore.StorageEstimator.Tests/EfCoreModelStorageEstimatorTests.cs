namespace EfCore.StorageEstimator.Tests;

using Estimation;
using Microsoft.EntityFrameworkCore;
using Planning;

/// <summary>Unit tests for EF Core model-backed estimation behavior.</summary>
[Trait("Category", "Unit")]
public sealed class EfCoreModelStorageEstimatorTests
{
  #region Estimate

  [Fact]
  public void Estimate_WithEfCoreModel_UsesModelSchemaFactsAndModelNavigations()
  {
    // Arrange
    using var dbContext = CreateDbContext();
    var       service   = CreateService();
    var request = new StorageEstimateRequest
    {
      Model = dbContext.Model,
      Roots =
      [
        new StorageTraversalRoot(typeof(FleetRoot))
      ]
    };

    // Act
    var report = service.Estimate(request);

    // Assert
    report.Nodes.Should().HaveCount(2);

    var rootNode = report.Nodes.Single(node => node.EntityType == typeof(FleetRoot));
    rootNode.Path.Should().Be(nameof(FleetRoot));
    rootNode.EstimatedRows.Should().Be(3);
    rootNode.Schema.Should().NotBeNull();
    rootNode.Schema!.TableName.Should().Be("fleet_roots");
    rootNode.Schema.PropertyCount.Should().Be(2);
    rootNode.Schema.IndexCount.Should().Be(1);

    var childNode = report.Nodes.Single(node => node.EntityType == typeof(AircraftEntity));
    childNode.Path.Should().Be($"{nameof(FleetRoot)}.{nameof(FleetRoot.Aircraft)}");
    childNode.EstimatedRows.Should().Be(6);
    childNode.Schema.Should().NotBeNull();
    childNode.Schema!.TableName.Should().Be("aircraft");
    childNode.Schema.PropertyCount.Should().Be(3);
    childNode.Schema.IndexCount.Should().Be(2);

    report.Warnings.Should().ContainSingle(warning =>
                                             warning.Contains($"{nameof(FleetRoot)}.{nameof(FleetRoot.Missions)}"));
  }


  [Fact]
  public void Estimate_WithConventionForeignKeyIndex_IncludesThatIndexInSchemaAndEstimate()
  {
    // Arrange
    using var dbContext = CreateDbContext();
    var       service   = CreateService();
    var request = new StorageEstimateRequest
    {
      Model = dbContext.Model,
      Roots =
      [
        new StorageTraversalRoot(typeof(FleetRoot))
      ]
    };

    // Act
    var report = service.Estimate(request);

    // Assert
    var childNode = report.Nodes.Single(node => node.EntityType == typeof(AircraftEntity));
    childNode.Schema.Should().NotBeNull();
    childNode.Schema!.Indexes.Should().Contain(index =>
      index.PropertyNames.SequenceEqual(new[]
      {
        nameof(AircraftEntity.FleetRootId)
      }));
    childNode.Schema.IndexCount.Should().Be(2);
    childNode.IndexEstimates.Should().HaveCount(2);
    childNode.IndexEstimates.Should().Contain(index =>
      index.ColumnCount == 1 &&
      index.EstimatedBytes > 0);
  }


  [Fact]
  public void Estimate_WithModelMissingRootEntity_ThrowsStorageEstimatorConfigurationException()
  {
    // Arrange
    using var dbContext = CreateDbContext();
    var       service   = CreateService();
    var request = new StorageEstimateRequest
    {
      Model = dbContext.Model,
      Roots =
      [
        new StorageTraversalRoot(typeof(DetachedRoot))
      ]
    };

    // Act
    var act = () => service.Estimate(request);

    // Assert
    act.Should().Throw<Exceptions.StorageEstimatorConfigurationException>()
       .WithMessage("*not present in the supplied EF Core model*");
  }


  [Fact]
  public void Estimate_WithSizedModel_ComputesHeapAndIndexBytes()
  {
    // Arrange
    using var dbContext = CreateDbContext();
    var       service   = CreateService();
    var request = new StorageEstimateRequest
    {
      Model = dbContext.Model,
      Roots =
      [
        new StorageTraversalRoot(typeof(CapacityRoot))
      ]
    };

    // Act
    var report = service.Estimate(request);

    // Assert
    var rootNode = report.Nodes.Single(node => node.EntityType == typeof(CapacityRoot));
    rootNode.EstimatedRows.Should().Be(500);
    rootNode.EstimatedHeapBytes.Should().Be(40960);
    rootNode.EstimatedIndexBytes.Should().Be(57344);
    rootNode.EstimatedTotalBytes.Should().Be(98304);
    rootNode.IndexEstimates.Should().HaveCount(2);
    rootNode.IndexEstimates.Should().ContainSingle(index =>
                                                     index.Name == "ix_capacity_roots_name" &&
                                                     index.EstimatedBytes == 32768);
    rootNode.IndexEstimates.Should().ContainSingle(index =>
                                                     index.Name == "ix_capacity_roots_state_created" &&
                                                     index.EstimatedBytes == 24576);

    var childNode = report.Nodes.Single(node => node.EntityType == typeof(CapacityChild));
    childNode.EstimatedRows.Should().Be(1000);
    childNode.EstimatedHeapBytes.Should().Be(163840);
    childNode.EstimatedIndexBytes.Should().Be(73728);
    childNode.EstimatedTotalBytes.Should().Be(237568);
    childNode.IndexEstimates.Should().HaveCount(2);
    childNode.IndexEstimates.Should().ContainSingle(index =>
                                                     index.Name == "IX_capacity_children_CapacityRootId" &&
                                                     index.EstimatedBytes == 32768);

    report.TotalEstimatedRows.Should().Be(1500);
    report.TotalEstimatedHeapBytes.Should().Be(204800);
    report.TotalEstimatedIndexBytes.Should().Be(131072);
    report.TotalEstimatedBytes.Should().Be(335872);
  }


  [Fact]
  public void Estimate_WithVariableLengthFallback_UsesMaxLengthAndAddsWarning()
  {
    // Arrange
    using var dbContext = CreateDbContext();
    var       service   = CreateService();
    var request = new StorageEstimateRequest
    {
      Model = dbContext.Model,
      Roots =
      [
        new StorageTraversalRoot(typeof(FallbackRoot))
      ]
    };

    // Act
    var report = service.Estimate(request);

    // Assert
    var node = report.Nodes.Single(node => node.EntityType == typeof(FallbackRoot));
    node.EstimatedHeapBytes.Should().BeGreaterThan(0);
    report.Warnings.Should().Contain(warning =>
                                       warning.Contains("FallbackRoot.Name") &&
                                       warning.Contains("MaxLength"));
  }

  #endregion


  #region Helpers

  private static IStorageEstimator CreateService()
  {
    return new StorageEstimator();
  }


  private static TestPlanningDbContext CreateDbContext()
  {
    var options = new DbContextOptionsBuilder<TestPlanningDbContext>()
                  .UseNpgsql("Host=localhost;Database=storage_estimator_tests;Username=test;Password=test")
                  .Options;

    return new TestPlanningDbContext(options);
  }

  #endregion


  #region Test Models

  [StorageEntity(3)]
  private sealed class FleetRoot
  {
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [StorageNavigation(2)]
    public IReadOnlyList<AircraftEntity> Aircraft { get; } = [];

    public IReadOnlyList<UnplannedMission> Missions { get; } = [];
  }


  [StorageEntity(1)]
  private sealed class AircraftEntity
  {
    public int Id { get; set; }

    public int FleetRootId { get; set; }

    public string TailNumber { get; set; } = string.Empty;

    public FleetRoot Fleet { get; set; } = null!;
  }


  [StorageEntity(1)]
  private sealed class UnplannedMission
  {
    public int Id { get; set; }

    public int FleetRootId { get; set; }
  }


  private sealed class DetachedRoot
  {
    public int Id { get; set; }
  }


  [StorageEntity(500)]
  private sealed class CapacityRoot
  {
    public int Id { get; set; }

    [StorageField(AverageLength = 20)]
    public string Name { get; set; } = string.Empty;

    [StorageField(0.25d, AverageLength = 40)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    [StorageNavigation(2)]
    public IReadOnlyList<CapacityChild> Children { get; } = [];
  }


  [StorageEntity(1)]
  private sealed class CapacityChild
  {
    public int Id { get; set; }

    public int CapacityRootId { get; set; }

    [StorageField(AverageLength = 12)]
    public string Code { get; set; } = string.Empty;

    [StorageField(0.5d, AverageLength = 200)]
    public byte[]? Payload { get; set; }

    public CapacityRoot Root { get; set; } = null!;
  }


  [StorageEntity(50)]
  private sealed class FallbackRoot
  {
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
  }


  private sealed class TestPlanningDbContext : DbContext
  {
    public TestPlanningDbContext(DbContextOptions<TestPlanningDbContext> options)
      : base(options) { }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<FleetRoot>(entity =>
      {
        entity.ToTable("fleet_roots");
        entity.HasKey(root => root.Id);
        entity.Property(root => root.Name).HasMaxLength(64);
        entity.HasIndex(root => root.Name);
        entity.HasMany(root => root.Aircraft).WithOne(aircraft => aircraft.Fleet).HasForeignKey(aircraft => aircraft.FleetRootId);
        entity.HasMany(root => root.Missions).WithOne().HasForeignKey(mission => mission.FleetRootId);
      });

      modelBuilder.Entity<AircraftEntity>(entity =>
      {
        entity.ToTable("aircraft");
        entity.HasKey(aircraft => aircraft.Id);
        entity.Property(aircraft => aircraft.TailNumber).HasMaxLength(32);
        entity.HasIndex(aircraft => aircraft.TailNumber);
      });

      modelBuilder.Entity<UnplannedMission>(entity =>
      {
        entity.ToTable("missions");
        entity.HasKey(mission => mission.Id);
      });

      modelBuilder.Entity<CapacityRoot>(entity =>
      {
        entity.ToTable("capacity_roots");
        entity.HasKey(root => root.Id);
        entity.Property(root => root.Name).HasMaxLength(64);
        entity.Property(root => root.Notes).HasColumnType("text");
        entity.Property(root => root.CreatedAt).HasColumnType("timestamp without time zone");
        entity.HasIndex(root => root.Name).HasDatabaseName("ix_capacity_roots_name");
        entity.HasIndex(root => new
        {
          root.IsActive,
          root.CreatedAt
        }).HasDatabaseName("ix_capacity_roots_state_created");
        entity.HasMany(root => root.Children).WithOne(child => child.Root).HasForeignKey(child => child.CapacityRootId);
      });

      modelBuilder.Entity<CapacityChild>(entity =>
      {
        entity.ToTable("capacity_children");
        entity.HasKey(child => child.Id);
        entity.Property(child => child.Code).HasMaxLength(24);
        entity.Property(child => child.Payload).HasColumnType("bytea");
        entity.HasIndex(child => child.Code).IsUnique().HasDatabaseName("ix_capacity_children_code");
      });

      modelBuilder.Entity<FallbackRoot>(entity =>
      {
        entity.ToTable("fallback_roots");
        entity.HasKey(root => root.Id);
        entity.Property(root => root.Name).HasMaxLength(32);
      });
    }
  }

  #endregion
}
