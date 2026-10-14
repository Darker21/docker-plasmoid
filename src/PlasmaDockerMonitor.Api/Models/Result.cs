using PlasmaDockerMonitor.Api.Enums;

namespace PlasmaDockerMonitor.Api.Models;

/// <summary>
/// Outcome of a service-layer operation that returns no data (e.g. start/stop/restart),
/// decoupled from any particular transport (Docker.DotNet exception, HTTP status, etc.)
/// so endpoints only ever need to inspect this shape.
/// </summary>
public class Result
{
    /// <summary>
    /// Indicates success
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The kind of error that occured
    /// </summary>
    public ResultErrorKind? ErrorKind { get; set; }

    /// <summary>
    /// Details of the error that occured
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates instance of a success result
    /// </summary>
    /// <returns>Success response</returns>
    public static Result Ok() => new()
    {
        Success = true,
    };

    /// <summary>
    /// Creates instance of a failure result
    /// </summary>
    /// <param name="errorKind">Kind of error thrown/caught</param>
    /// <param name="errorMessage">Message to detail the error</param>
    /// <returns>Failure response</returns>
    public static Result Fail(ResultErrorKind errorKind, string errorMessage) => new()
    {
        Success = false,
        ErrorKind = errorKind,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// Outcome of a service-layer operation that returns data on success (e.g. fetching
/// containers), decoupled from any particular transport so endpoints only ever need
/// to inspect this shape. A <see cref="Result{TData}"/> is itself a <see cref="Result"/>,
/// so callers that only care about success/failure (not the data) can use either
/// interchangeably.
/// </summary>
public class Result<TData> : Result where TData : class
{
    /// <summary>
    /// Data returned from operation
    /// </summary>
    public TData? Data { get; set; }

    /// <summary>
    /// Creates instance of a success result
    /// </summary>
    /// <param name="data">The data to return along with the result</param>
    /// <returns>Success response</returns>
    public static Result<TData> Ok(TData data) => new()
    {
        Success = true,
        Data = data
    };

    /// <summary>
    /// Creates instance of a failure result
    /// </summary>
    /// <param name="errorKind">Kind of error thrown/caught</param>
    /// <param name="errorMessage">Message to detail the error</param>
    /// <returns>Failure response</returns>
    public new static Result<TData> Fail(ResultErrorKind errorKind, string errorMessage) => new()
    {
        Success = false,
        ErrorKind = errorKind,
        ErrorMessage = errorMessage
    };
}