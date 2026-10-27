using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;
using OpenHisabKitab.Data.CompanyDb;
using OpenHisabKitab.Host.Application;

namespace OpenHisabKitab.Host.Grpc;

/// <summary>Turns a ServiceException into a gRPC status with the same message. Anything else is logged and hidden.</summary>
internal sealed class ErrorInterceptor(ILogger<ErrorInterceptor> log) : Interceptor
{
    public const string CurrentTrailer = "ohk-current-bin";

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try { return await continuation(request, context); }
        catch (Exception e) when (e is not RpcException) { throw Map(e, context); }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try { await continuation(request, responseStream, context); }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { }
        catch (Exception e) when (e is not RpcException) { throw Map(e, context); }
    }

    RpcException Map(Exception e, ServerCallContext context)
    {
        if (e is not ServiceException se)
        {
            log.LogError(e, "Call {Method} failed.", context.Method);
            return new RpcException(new Status(StatusCode.Internal, "Something went wrong. Details are in the host log."));
        }
        var trailers = new Metadata();
        if (se.Error == ServiceError.Conflict && Current(se.Current) is { } current)
            trailers.Add(CurrentTrailer, current.ToByteArray());
        return new RpcException(new Status(Code(se.Error), se.Message), trailers);
    }

    static IMessage? Current(object? current) => current switch
    {
        VoucherRow v => Wire.ToMsg(v),
        Party p => Wire.ToMsg(p),
        Item i => Wire.ToMsg(i),
        _ => null,
    };

    static StatusCode Code(ServiceError e) => e switch
    {
        ServiceError.NotAuthenticated => StatusCode.Unauthenticated,
        ServiceError.PermissionDenied => StatusCode.PermissionDenied,
        ServiceError.NotFound => StatusCode.NotFound,
        ServiceError.Conflict => StatusCode.Aborted,
        ServiceError.Locked or ServiceError.OpeningDifference or ServiceError.SetupRequired => StatusCode.FailedPrecondition,
        ServiceError.Invalid => StatusCode.InvalidArgument,
        ServiceError.Unavailable => StatusCode.Unavailable,
        _ => StatusCode.Unknown,
    };
}

/// <summary>Refuses a client whose version differs from the host's. GetInfo is open so a client can learn the version.</summary>
internal sealed class VersionInterceptor : Interceptor
{
    static readonly HashSet<string> Open = ["/ohk.v1.Session/GetInfo"];

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        Check(context);
        return continuation(request, context);
    }

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Check(context);
        return continuation(request, responseStream, context);
    }

    static void Check(ServerCallContext context)
    {
        if (Open.Contains(context.Method)) return;
        if (context.RequestHeaders.GetValue("ohk-version") != HostVersion.Current)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, $"Install Open Hisab Kitab {HostVersion.Current} to connect."));
    }
}

/// <summary>Resolves the bearer session token into the caller's context for the endpoints.</summary>
internal sealed class AuthInterceptor(SessionStore sessions) : Interceptor
{
    static readonly HashSet<string> Open = ["/ohk.v1.Session/GetInfo", "/ohk.v1.Session/SetupOwner", "/ohk.v1.Session/Login"];

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        Resolve(context);
        return continuation(request, context);
    }

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Resolve(context);
        return continuation(request, responseStream, context);
    }

    void Resolve(ServerCallContext context)
    {
        if (Open.Contains(context.Method)) return;
        var header = context.RequestHeaders.GetValue("authorization");
        const string prefix = "Bearer ";
        var token = header is not null && header.StartsWith(prefix, StringComparison.Ordinal) ? header[prefix.Length..] : null;
        var caller = token is null ? null : sessions.Resolve(token);
        if (caller is null) throw new RpcException(new Status(StatusCode.Unauthenticated, "Log in again."));
        context.UserState[Calls.ContextKey] = caller;
        context.UserState[Calls.TokenKey] = token!;
    }
}

internal static class Calls
{
    public const string ContextKey = "ohk.context";
    public const string TokenKey = "ohk.token";

    public static CallContext Caller(this ServerCallContext context) => (CallContext)context.UserState[ContextKey];

    public static string Token(this ServerCallContext context) => (string)context.UserState[TokenKey];

    public static string Machine(this ServerCallContext context) =>
        context.RequestHeaders.GetValue("ohk-machine") is { Length: > 0 } m ? m : "unknown";
}
