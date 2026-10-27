namespace OpenHisabKitab.Host.Application;

public enum ServiceError { NotAuthenticated, PermissionDenied, NotFound, Conflict, Locked, Invalid, OpeningDifference, SetupRequired, Unavailable, TooNew }

/// <summary>
/// A refusal the user can act on. The message is shown as is, so it follows the writing rules of spec 6.7.
/// A Conflict carries the current record so the caller can show it.
/// </summary>
public sealed class ServiceException(ServiceError error, string message, object? current = null) : Exception(message)
{
    public ServiceError Error { get; } = error;

    public object? Current { get; } = current;

    public static ServiceException Denied() => new(ServiceError.PermissionDenied, "You do not have permission for this.");
}
