namespace Hourglass.Application;

public enum ApplicationErrorCode
{
    Validation,
    NotFound,
    RuntimeUnavailable,
    TransportFailure,
    Unsupported,
    PersistenceFailure,
    Conflict,
    Locked,
    InvalidTransition,
    InternalFailure
}

public sealed record ApplicationError(ApplicationErrorCode Code, string Message);

/// <summary>Expected command failures are values, so every frontend can map them consistently.</summary>
public abstract record ApplicationResult<T>
{
    public sealed record Success(T Value) : ApplicationResult<T>;

    public sealed record Failure(ApplicationError Error) : ApplicationResult<T>;
}
