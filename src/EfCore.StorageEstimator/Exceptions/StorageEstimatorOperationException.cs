namespace EfCore.StorageEstimator.Exceptions;

/// <summary>Exception thrown when a StorageEstimator operation fails.</summary>
/// <remarks>
///   <para>
///     This exception carries context about the failed operation, including any partial results that were accumulated
///     before the failure occurred.
///   </para>
/// </remarks>
public class StorageEstimatorOperationException : StorageEstimatorException
{
  /// <summary>Initializes a new instance of the <see cref="StorageEstimatorOperationException" /> class.</summary>
  public StorageEstimatorOperationException() { }


  /// <summary>
  ///   Initializes a new instance of the <see cref="StorageEstimatorOperationException" /> class with a specified
  ///   error message.
  /// </summary>
  /// <param name="message">The message that describes the error.</param>
  public StorageEstimatorOperationException(string message)
    : base(message) { }


  /// <summary>
  ///   Initializes a new instance of the <see cref="StorageEstimatorOperationException" /> class with a specified
  ///   error message and a reference to the inner exception that caused this exception.
  /// </summary>
  /// <param name="message">The message that describes the error.</param>
  /// <param name="innerException">
  ///   The exception that is the cause of the current exception, or a null reference if no inner
  ///   exception is specified.
  /// </param>
  public StorageEstimatorOperationException(string message, Exception innerException)
    : base(message, innerException) { }


  /// <summary>
  ///   Initializes a new instance of the <see cref="StorageEstimatorOperationException" /> class with operation
  ///   context and optional partial results.
  /// </summary>
  /// <param name="message">The message that describes the error.</param>
  /// <param name="operationName">The name of the operation that failed.</param>
  /// <param name="partialResult">Any partial result accumulated before failure, if applicable.</param>
  /// <param name="innerException">The inner exception, if any.</param>
  public StorageEstimatorOperationException(
    string     message,
    string     operationName,
    object?    partialResult  = null,
    Exception? innerException = null)
    : base(message, innerException!)
  {
    OperationName = operationName;
    PartialResult = partialResult;
  }


  /// <summary>Gets the name of the operation that failed.</summary>
  public string? OperationName { get; }

  /// <summary>Gets any partial result that was accumulated before the failure occurred.</summary>
  /// <remarks>
  ///   <para>
  ///     For batch operations, this may contain results for items that were processed before the failure. Callers can
  ///     use this for metrics, logging, or partial recovery.
  ///   </para>
  /// </remarks>
  public object? PartialResult { get; }
}
