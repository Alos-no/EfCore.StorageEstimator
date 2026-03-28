namespace EfCore.StorageEstimator;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>Provides extension methods for setting up StorageEstimator services in an <see cref="IServiceCollection" />.</summary>
public static class ServiceCollectionExtensions
{
  #region Methods

  /// <summary>Registers the <see cref="IStorageEstimator" /> runtime.</summary>
  /// <param name="services">The <see cref="IServiceCollection" /> to add the services to.</param>
  /// <returns>The <see cref="IServiceCollection" /> so that additional calls can be chained.</returns>
  public static IServiceCollection AddStorageEstimator(this IServiceCollection services)
  {
    services.TryAddSingleton<IStorageEstimator, StorageEstimator>();

    return services;
  }

  #endregion
}
