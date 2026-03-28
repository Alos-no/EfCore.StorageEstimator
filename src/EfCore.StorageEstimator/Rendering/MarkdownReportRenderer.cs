namespace EfCore.StorageEstimator.Rendering;

using System.Globalization;
using Estimation;

/// <summary>Renders a <see cref="StorageEstimateReport" /> as Markdown.</summary>
public sealed class MarkdownReportRenderer
{
  #region Methods

  /// <summary>Renders a <see cref="StorageEstimateReport" /> as Markdown.</summary>
  /// <param name="report">The report to render.</param>
  /// <returns>The Markdown representation of the report.</returns>
  public string Render(StorageEstimateReport report)
  {
    ArgumentNullException.ThrowIfNull(report);

    var lines = new List<string>
    {
      "# Storage Estimate Report",
      string.Empty,
      "| Path | Entity | Rows | Heap Bytes | Index Bytes | Total Bytes | Table | Properties | Indexes |",
      "| --- | --- | ---: | ---: | ---: | ---: | --- | ---: | ---: |"
    };

    foreach (var node in report.Nodes)
      lines.Add(
        $"| {node.Path} | {node.EntityType.Name} | {FormatNumber(node.EstimatedRows)} | " +
        $"{FormatNumber(node.EstimatedHeapBytes)} | " +
        $"{FormatNumber(node.EstimatedIndexBytes)} | " +
        $"{FormatNumber(node.EstimatedTotalBytes)} | " +
        $"{node.Schema?.TableName ?? "-"} | " +
        $"{FormatSchemaValue(node.Schema?.PropertyCount)} | " +
        $"{FormatSchemaValue(node.Schema?.IndexCount)} |");

    lines.Add(string.Empty);
    lines.Add($"**Total Estimated Rows:** {FormatNumber(report.TotalEstimatedRows)}");
    lines.Add($"**Total Estimated Heap Bytes:** {FormatNumber(report.TotalEstimatedHeapBytes)}");
    lines.Add($"**Total Estimated Index Bytes:** {FormatNumber(report.TotalEstimatedIndexBytes)}");
    lines.Add($"**Total Estimated Bytes:** {FormatNumber(report.TotalEstimatedBytes)}");

    if (report.Warnings.Count > 0)
    {
      lines.Add(string.Empty);
      lines.Add("## Warnings");
      lines.Add(string.Empty);

      foreach (var warning in report.Warnings)
        lines.Add($"- {warning}");
    }

    return string.Join(Environment.NewLine, lines);
  }

  #endregion


  #region Methods - Private

  private static string FormatNumber(double value)
  {
    return value.ToString("0.##", CultureInfo.InvariantCulture);
  }


  private static string FormatSchemaValue(int? value)
  {
    return value?.ToString(CultureInfo.InvariantCulture) ?? "-";
  }

  #endregion
}
