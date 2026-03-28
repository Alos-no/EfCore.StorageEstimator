namespace EfCore.StorageEstimator.Exceptions;

/// <summary>Base exception for all StorageEstimator library errors.</summary>
/// <remarks>
///   <para>
///     This base exception allows callers to catch all library-specific errors while still enabling specific exception
///     handling for derived types.
///   </para>
/// </remarks>
public class StorageEstimatorException : Exception
{
  /// <summary>Initializes a new instance of the <see cref="StorageEstimatorException" /> class.</summary>
  public StorageEstimatorException() { }


  /// <summary>
  ///   Initializes a new instance of the <see cref="StorageEstimatorException" /> class with a specified error
  ///   message.
  /// </summary>
  /// <param name="message">The message that describes the error.</param>
  public StorageEstimatorException(string message)
    : base(message) { }


  /// <summary>
  ///   Initializes a new instance of the <see cref="StorageEstimatorException" /> class with a specified error
  ///   message and a reference to the inner exception that caused this exception.
  /// </summary>
  /// <param name="message">The message that describes the error.</param>
  /// <param name="innerException">
  ///   The exception that is the cause of the current exception, or a null reference if no inner
  ///   exception is specified.
  /// </param>
  public StorageEstimatorException(string message, Exception innerException)
    : base(message, innerException) { }
}
