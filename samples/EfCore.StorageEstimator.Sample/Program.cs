// -----------------------------------------------------------------------------
// EfCore.StorageEstimator Sample Application
// -----------------------------------------------------------------------------
// This sample demonstrates the preferred integration path:
// build a real EF Core model, then estimate storage from dbContext.Model plus
// explicit planning assumptions on roots, fields, and navigations.
// -----------------------------------------------------------------------------

using System.Text.Json;
using EfCore.StorageEstimator;
using EfCore.StorageEstimator.Estimation;
using EfCore.StorageEstimator.Planning;
using EfCore.StorageEstimator.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddStorageEstimator();

using var host = builder.Build();
using var scope = host.Services.CreateScope();
using var dbContext = SamplePlanningDbContext.Create();

var estimator = scope.ServiceProvider.GetRequiredService<IStorageEstimator>();
var renderer = new MarkdownReportRenderer();

// The estimator reads table names, store types, nullability, and indexes from
// the compiled EF Core model. No database connection is opened for this.
var report = estimator.Estimate(new StorageEstimateRequest
{
  Model = dbContext.Model,
  Roots =
  [
    new StorageTraversalRoot(typeof(SampleProject))
  ]
});

Console.WriteLine(renderer.Render(report));

[StorageEntity(120)]
internal sealed class SampleProject
{
  public Guid Id { get; set; }

  [StorageField(AverageLength = 48)]
  public string Name { get; set; } = string.Empty;

  [StorageField(0.4d, AverageLength = 96)]
  public string[] Regions { get; set; } = [];

  [StorageField(0.75d, AverageLength = 512)]
  public JsonDocument? Metadata { get; set; }

  public decimal BudgetEstimate { get; set; }

  public ProjectAudit Audit { get; set; } = new();

  public ProjectSettings Settings { get; set; } = new();

  [StorageNavigation(12)]
  public IReadOnlyList<SampleAsset> Assets { get; } = [];

  [StorageNavigation(4)]
  public IReadOnlyList<SampleTag> Tags { get; } = [];
}


internal sealed class ProjectAudit
{
  public DateTimeOffset CreatedAt { get; set; }

  [StorageField(AverageLength = 32)]
  public string CreatedBy { get; set; } = string.Empty;
}


internal sealed class ProjectSettings
{
  public bool IsPublic { get; set; }

  [StorageField(0.3d, AverageLength = 160)]
  public string? Notes { get; set; }
}


[StorageEntity(1)]
internal sealed class SampleAsset
{
  public Guid Id { get; set; }

  public Guid ProjectId { get; set; }

  [StorageField(AverageLength = 96)]
  public string FileName { get; set; } = string.Empty;

  [StorageField(AverageLength = 32)]
  public string MimeType { get; set; } = string.Empty;

  public long OriginalBytes { get; set; }

  [StorageField(0.2d, AverageLength = 24_000)]
  public byte[]? Thumbnail { get; set; }

  [StorageField(0.35d, AverageLength = 320)]
  public JsonDocument? ProcessingMetadata { get; set; }

  public SampleProject Project { get; set; } = null!;
}


[StorageEntity(1)]
internal sealed class SampleTag
{
  public Guid Id { get; set; }

  [StorageField(AverageLength = 24)]
  public string Label { get; set; } = string.Empty;

  public IReadOnlyList<SampleProject> Projects { get; } = [];
}


internal sealed class SamplePlanningDbContext(DbContextOptions<SamplePlanningDbContext> options) : DbContext(options)
{
  public static SamplePlanningDbContext Create()
  {
    var options = new DbContextOptionsBuilder<SamplePlanningDbContext>()
      // A provider is still required so EF Core produces PostgreSQL-specific
      // store types and index metadata, but the estimator only reads Model.
      .UseNpgsql("Host=localhost;Database=storage_estimator_sample;Username=sample;Password=sample")
      .Options;

    return new SamplePlanningDbContext(options);
  }


  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<SampleProject>(entity =>
    {
      entity.ToTable("sample_projects");
      entity.HasKey(project => project.Id);
      entity.Property(project => project.Name).HasMaxLength(96);
      entity.Property(project => project.Regions).HasColumnType("text[]");
      entity.Property(project => project.Metadata).HasColumnType("jsonb");
      entity.Property(project => project.BudgetEstimate).HasPrecision(14, 2);
      entity.HasIndex(project => project.Name).HasDatabaseName("ix_sample_projects_name");
      entity.HasIndex(project => new
      {
        project.Name,
        project.BudgetEstimate
      }).HasDatabaseName("ix_sample_projects_name_budget");
      entity.ComplexProperty(project => project.Audit, complex =>
      {
        complex.Property(audit => audit.CreatedAt).HasColumnType("timestamp with time zone");
        complex.Property(audit => audit.CreatedBy).HasMaxLength(64);
      });
      entity.OwnsOne(project => project.Settings, owned =>
      {
        owned.Property(settings => settings.Notes).HasColumnType("text");
      });
      entity.HasMany(project => project.Assets)
        .WithOne(asset => asset.Project)
        .HasForeignKey(asset => asset.ProjectId);
      entity.HasMany(project => project.Tags)
        .WithMany(tag => tag.Projects)
        .UsingEntity("sample_project_tags");
    });

    modelBuilder.Entity<SampleAsset>(entity =>
    {
      entity.ToTable("sample_assets");
      entity.HasKey(asset => asset.Id);
      entity.Property(asset => asset.FileName).HasMaxLength(128);
      entity.Property(asset => asset.MimeType).HasMaxLength(64);
      entity.Property(asset => asset.Thumbnail).HasColumnType("bytea");
      entity.Property(asset => asset.ProcessingMetadata).HasColumnType("jsonb");
      entity.HasIndex(asset => new
      {
        asset.ProjectId,
        asset.FileName
      }).HasDatabaseName("ix_sample_assets_project_file");
    });

    modelBuilder.Entity<SampleTag>(entity =>
    {
      entity.ToTable("sample_tags");
      entity.HasKey(tag => tag.Id);
      entity.Property(tag => tag.Label).HasMaxLength(32);
      entity.HasIndex(tag => tag.Label).IsUnique().HasDatabaseName("ux_sample_tags_label");
    });
  }
}
