namespace EfCore.StorageEstimator.Rendering;

using System.Text.Json;
using Estimation;

/// <summary>Renders a <see cref="StorageEstimateReport" /> as JSON.</summary>
public sealed class JsonReportRenderer
{
  #region Methods

  /// <summary>Renders a <see cref="StorageEstimateReport" /> as indented JSON.</summary>
  /// <param name="report">The report to render.</param>
  /// <returns>The JSON representation of the report.</returns>
  public string Render(StorageEstimateReport report)
  {
    ArgumentNullException.ThrowIfNull(report);

    var payload = new
    {
      report.TotalEstimatedRows,
      report.TotalEstimatedHeapBytes,
      report.TotalEstimatedIndexBytes,
      report.TotalEstimatedBytes,
      report.Warnings,
      Nodes = report.Nodes.Select(node => new
      {
        node.Path,
        EntityType = node.EntityType.FullName ?? node.EntityType.Name,
        node.EstimatedRows,
        node.Depth,
        node.EstimatedHeapBytes,
        node.EstimatedIndexBytes,
        node.EstimatedTotalBytes,
        Schema = node.Schema is null
          ? null
          : new
          {
            node.Schema.TableName,
            node.Schema.PropertyCount,
            node.Schema.IndexCount,
            Properties = node.Schema.Properties.Select(property => new
            {
              property.Name,
              ClrType = property.ClrType.FullName ?? property.ClrType.Name,
              property.StoreType,
              property.IsNullable,
              property.IsVariableLength,
              property.MaxLength,
              property.Precision,
              property.Scale
            }),
            Indexes = node.Schema.Indexes.Select(index => new
            {
              index.Name,
              index.IsUnique,
              index.ColumnCount,
              index.PropertyNames
            })
          },
        IndexEstimates = node.IndexEstimates.Select(index => new
        {
          index.Name,
          index.EstimatedBytes,
          index.AverageEntryBytes,
          index.ColumnCount,
          index.IsUnique
        })
      })
    };

    return JsonSerializer.Serialize(payload, new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
  }

  #endregion
}
