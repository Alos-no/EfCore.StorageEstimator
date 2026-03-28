namespace EfCore.StorageEstimator;

using Estimation;

/// <summary>Provides the primary service interface for the storage estimator runtime.</summary>
public interface IStorageEstimator
{
  #region Methods

  /// <summary>Estimates storage footprint and row expansion for the supplied request.</summary>
  /// <param name="request">The estimate request to execute.</param>
  /// <returns>The structured estimate report.</returns>
  StorageEstimateReport Estimate(StorageEstimateRequest request);


  /// <summary>Estimates storage footprint and row expansion for the supplied request.</summary>
  /// <param name="request">The estimate request to execute.</param>
  /// <param name="cancellationToken">The cancellation token.</param>
  /// <returns>A task containing the structured estimate report.</returns>
  ValueTask<StorageEstimateReport> EstimateAsync(
    StorageEstimateRequest request,
    CancellationToken      cancellationToken = default);

  #endregion
}
