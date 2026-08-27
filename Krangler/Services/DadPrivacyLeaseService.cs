using System;
using System.Text.Json;

namespace Krangler.Services;

public static class DadPrivacyLeaseContract
{
    public const string AcquireIpcName = "Krangler.DadPrivacyLease.AcquireFromJson";
    public const string ReleaseIpcName = "Krangler.DadPrivacyLease.ReleaseFromJson";
    public const string StatusIpcName = "Krangler.DadPrivacyLease.GetStatusJson";

    public sealed class Request
    {
        public string Token { get; init; } = string.Empty;
    }

    public sealed record Response(
        bool Ok,
        string Code,
        bool LeaseActive,
        bool OwnedByRequester,
        bool NamePrivacyActive,
        bool ChatPrivacyActive,
        bool IncludesSelf,
        string Status,
        string Error);
}

public readonly record struct DadPrivacyLeaseEffects(
    bool NameKranglingEnabled,
    bool ChatKranglingEnabled,
    bool SkipSelfNameKrangling);

public static class DadPrivacyLeasePolicy
{
    public static DadPrivacyLeaseEffects Resolve(
        bool leaseActive,
        bool configuredMasterEnabled,
        bool configuredNameKrangling,
        bool configuredChatKrangling,
        bool configuredSkipSelf)
        => new(
            leaseActive || (configuredMasterEnabled && configuredNameKrangling),
            leaseActive || (configuredMasterEnabled && configuredChatKrangling),
            !leaseActive && configuredSkipSelf);
}

public sealed class DadPrivacyLeaseService : IDisposable
{
    public const string AcquiredCode = "acquired";
    public const string ReacquiredCode = "reacquired";
    public const string ReplacedCode = "replaced-stale-owner";
    public const string ReleasedCode = "released";
    public const string NotHeldCode = "not-held";
    public const string NotOwnerCode = "not-owner";
    public const string OwnedCode = "owned";
    public const string OwnedByOtherCode = "owned-by-other";
    public const string InvalidRequestCode = "invalid-request";
    public const string DisposedCode = "disposed";

    private const int MaxTokenLength = 256;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8,
    };

    private readonly object sync = new();
    private readonly Action leaseActivityChanged;
    private string? ownerToken;
    private bool disposed;

    public DadPrivacyLeaseService(Action leaseActivityChanged)
    {
        this.leaseActivityChanged = leaseActivityChanged ?? throw new ArgumentNullException(nameof(leaseActivityChanged));
    }

    public bool IsActive
    {
        get
        {
            lock (sync)
                return ownerToken != null;
        }
    }

    public string AcquireFromJson(string requestJson)
    {
        if (!TryReadToken(requestJson, out var token))
            return SerializeInvalidRequest();

        DadPrivacyLeaseContract.Response response;
        var becameActive = false;
        lock (sync)
        {
            if (disposed)
            {
                response = CreateResponse(false, DisposedCode, false, false, "Krangler privacy lease service is unavailable.");
            }
            else if (ownerToken == null)
            {
                ownerToken = token;
                becameActive = true;
                response = CreateResponse(true, AcquiredCode, true, true, "DAD privacy lease acquired.");
            }
            else if (string.Equals(ownerToken, token, StringComparison.Ordinal))
            {
                response = CreateResponse(true, ReacquiredCode, true, true, "DAD privacy lease already owned by this token.");
            }
            else
            {
                ownerToken = token;
                response = CreateResponse(true, ReplacedCode, true, true, "DAD privacy lease replaced stale ownership.");
            }
        }

        if (becameActive)
            leaseActivityChanged();

        return Serialize(response);
    }

    public string ReleaseFromJson(string requestJson)
    {
        if (!TryReadToken(requestJson, out var token))
            return SerializeInvalidRequest();

        DadPrivacyLeaseContract.Response response;
        var becameInactive = false;
        lock (sync)
        {
            if (disposed)
            {
                response = CreateResponse(false, DisposedCode, false, false, "Krangler privacy lease service is unavailable.");
            }
            else if (ownerToken == null)
            {
                response = CreateResponse(false, NotHeldCode, false, false, "No DAD privacy lease is active.");
            }
            else if (!string.Equals(ownerToken, token, StringComparison.Ordinal))
            {
                response = CreateResponse(false, NotOwnerCode, true, false, "DAD privacy lease is owned by another token.");
            }
            else
            {
                ownerToken = null;
                becameInactive = true;
                response = CreateResponse(true, ReleasedCode, false, false, "DAD privacy lease released.");
            }
        }

        if (becameInactive)
            leaseActivityChanged();

        return Serialize(response);
    }

    public string GetStatusJson(string requestJson)
    {
        if (!TryReadToken(requestJson, out var token))
            return SerializeInvalidRequest();

        lock (sync)
        {
            if (disposed)
                return Serialize(CreateResponse(false, DisposedCode, false, false, "Krangler privacy lease service is unavailable."));

            if (ownerToken == null)
                return Serialize(CreateResponse(true, NotHeldCode, false, false, "No DAD privacy lease is active."));

            var ownedByRequester = string.Equals(ownerToken, token, StringComparison.Ordinal);
            return Serialize(CreateResponse(
                true,
                ownedByRequester ? OwnedCode : OwnedByOtherCode,
                true,
                ownedByRequester,
                ownedByRequester
                    ? "DAD privacy lease is owned by this token."
                    : "DAD privacy lease is owned by another token."));
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            ownerToken = null;
            disposed = true;
        }
    }

    private static DadPrivacyLeaseContract.Response CreateResponse(
        bool ok,
        string code,
        bool leaseActive,
        bool ownedByRequester,
        string status)
        => new(
            ok,
            code,
            leaseActive,
            ownedByRequester,
            leaseActive,
            leaseActive,
            leaseActive,
            status,
            ok ? string.Empty : status);

    private string SerializeInvalidRequest()
    {
        bool leaseActive;
        lock (sync)
            leaseActive = ownerToken != null;

        return Serialize(new DadPrivacyLeaseContract.Response(
            false,
            InvalidRequestCode,
            leaseActive,
            false,
            leaseActive,
            leaseActive,
            leaseActive,
            "DAD privacy lease request was rejected.",
            "Request must be a JSON object containing a non-empty token of at most 256 characters."));
    }

    private static bool TryReadToken(string requestJson, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(requestJson))
            return false;

        try
        {
            var request = JsonSerializer.Deserialize<DadPrivacyLeaseContract.Request>(requestJson, JsonOptions);
            if (request == null || string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > MaxTokenLength)
                return false;

            token = request.Token;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Serialize(DadPrivacyLeaseContract.Response response)
        => JsonSerializer.Serialize(response, JsonOptions);
}
