using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using NewEdenKit.ESI.Abstractions.Http;
using NewEdenKit.ESI.Abstractions.Middleware;
using NewEdenKit.ESI.Abstractions.Requests;
using NewEdenKit.ESI.Abstractions.Results;
using NewEdenKit.ESI.Abstractions.Routing;
using NewEdenKit.ESI.Models.Assets;
using NewEdenKit.ESI.Models.FreelanceJobs;
using NewEdenKit.ESI.Models.Sovereignty;
using NewEdenKit.ESI.Models.Status;
using NewEdenKit.ESI.Services;

namespace NewEdenKit.ESI.Tests.Docs;

/// <summary>
/// The examples in <c>docs/usage.md</c>. Each <c>#region snippet: name</c> is the exact text of the matching snippet
/// in the guide (<see cref="UsageGuideTests"/> checks), so every documented example compiles.
/// </summary>
internal static class UsageExamples
{
    public static void Register(IServiceCollection services)
    {
        #region snippet: register
        services.AddEveEsi(o =>
            {
                o.ApplicationName = "MyApp/1.2";      // your app, sent in the User-Agent (required)
                o.Contact = "admin@example.com";      // how CCP can reach you (required)
            })
            .EnableCaching();                         // optional: response cache + ETags, in a local folder
        #endregion
    }

    public static void RegisterCustomized(IServiceCollection services)
    {
        #region snippet: register-options
        services.AddEveEsi(o =>
            {
                o.ApplicationName = "MyApp/1.2";
                o.Contact = "admin@example.com";
                o.Tenant = "singularity";             // default "tranquility"
            })
            .EnableCaching(c => c.AddInMemoryCacheStore(store => store.MaxEntries = 50_000))
            .Use<AcceptLanguageMiddleware>()          // global middleware, runs for every call
            .ConfigureHttpClient(http => http.SetHandlerLifetime(TimeSpan.FromMinutes(5)));
        #endregion
    }

    public static async Task<long> QuickStart(IAssets assets, long characterId, string accessToken, CancellationToken ct)
    {
        var total = 0L;
        #region snippet: quickstart
        // Inject a service (IAssets, ICharacter, IMarket, …), build the request, execute it.
        var result = await assets.GetCharactersCharacterIdAssets(characterId, accessToken, page: 1).ExecuteAsync(ct);
        if (result.IsSuccess)
        {
            foreach (var asset in result.Value) { total += asset.Quantity; }
        }
        else
        {
            // Never throws for HTTP errors: inspect result.StatusCode and result.Error (Kind, Message).
        }
        #endregion
        return total;
    }

    #region snippet: public-call
    public static async Task<string> CharacterName(ICharacter characters, long characterId, CancellationToken ct)
    {
        var result = await characters.GetCharactersDetail(characterId).ExecuteAsync(ct);

        return result.IsSuccess
            ? result.Value.Name
            : $"lookup failed: {result}";
    }
    #endregion

    #region snippet: secured-call
    public static async Task<double?> WalletBalance(IWallet wallet, long characterId, string accessToken, CancellationToken ct)
    {
        // Secured endpoints take the SSO access token as a plain string; its scopes must include the endpoint's.
        var result = await wallet.GetCharactersCharacterIdWallet(characterId, accessToken).ExecuteAsync(ct);
        return result.IsSuccess ? result.Value : null;
    }
    #endregion

    public static async Task<string> Describe(ICharacter characters, long characterId, CancellationToken ct)
    {
        #region snippet: results
        var result = await characters.GetCharactersDetail(characterId).ExecuteAsync(ct);

        if (result.IsSuccess)
        {
            return result.Value.Name;                 // Value is non-null when IsSuccess is true
        }

        return result.Error!.Kind switch
        {
            EsiErrorKind.Http when result.StatusCode == HttpStatusCode.NotFound => "no such character",
            EsiErrorKind.Http => $"ESI said {(int?)result.StatusCode}: {result.Error.Message}",
            EsiErrorKind.Transport => "ESI couldn't be reached; try again later",
            EsiErrorKind.Deserialization => "ESI sent something this library version can't read",
            _ => result.ToString(),
        };
        #endregion
    }

    #region snippet: page-pagination
    public static async Task<List<CharactersCharacterIdAssetsGetItem>> AllAssets(
        IAssets assets, long characterId, string accessToken, CancellationToken ct)
    {
        var all = new List<CharactersCharacterIdAssetsGetItem>();
        for (var page = 1; ; page++)
        {
            var result = await assets.GetCharactersCharacterIdAssets(characterId, accessToken, page).ExecuteAsync(ct);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException($"Assets page {page} failed: {result}");
            }

            all.AddRange(result.Value);
            if (page >= (result.Headers.Pages ?? 1))    // X-Pages: the total page count
            {
                return all;
            }
        }
    }
    #endregion

    #region snippet: cursor-pagination
    public static async Task<List<FreelanceJobsDetailFreelancejob>> AllFreelanceJobs(IFreelanceJobs jobs, CancellationToken ct)
    {
        var all = new List<FreelanceJobsDetailFreelancejob>();
        CursorPage? cursor = null;                    // null: the most recent records
        while (true)
        {
            var result = await jobs.GetFreelanceJobsListing(cursor: cursor).ExecuteAsync(ct);
            if (!result.IsSuccess || result.Value.FreelanceJobs.Count == 0)
            {
                return all;
            }

            all.AddRange(result.Value.FreelanceJobs);
            if (result.Value.Cursor?.Before is not { } before)
            {
                return all;
            }

            cursor = new CursorPage(Before: before);  // walk backwards in time
        }
    }
    #endregion

    public static async Task<IReadOnlyList<string>> Names(IUniverse universe, CancellationToken ct)
    {
        #region snippet: body
        var result = await universe.PostUniverseNames([2_112_000_000, 98_000_001, 30_000_142]).ExecuteAsync(ct);
        #endregion
        return result.IsSuccess ? [.. result.Value.Select(n => n.Name)] : [];
    }

    public static async Task<EsiResult<ServerStatus>> RequestMiddleware(IStatus status, CancellationToken ct)
    {
        #region snippet: request-middleware
        var result = await status.GetStatus()
            .Use(async (context, next) =>
            {
                context.Request.Headers["Accept-Language"] = "de";    // before the call
                await next(context);
                var remaining = context.Response?.Headers.ErrorLimitRemain;   // after the call
            })
            .ExecuteAsync(ct);
        #endregion
        return result;
    }

    public static async Task<ServerStatus?> ServiceMiddleware(IStatus status, CancellationToken ct)
    {
        #region snippet: service-middleware
        // Services are transient: middleware added here applies to requests this instance builds afterwards.
        var german = status.Use(new AcceptLanguageMiddleware());
        var result = await german.GetStatus().ExecuteAsync(ct);
        #endregion
        return result.Value;
    }

    #region snippet: unions
    public static string Owner(SovereigntySystemsSolarsystem system) => system.Claim switch
    {
        SovereigntySystemsSolarsystem.ClaimModel.Alliance a => $"alliance {a.Value.AllianceId}",
        SovereigntySystemsSolarsystem.ClaimModel.Faction f => $"faction {f.Value.FactionId}",
        SovereigntySystemsSolarsystem.ClaimModel.Unclaimed => "unclaimed",
        SovereigntySystemsSolarsystem.ClaimModel.Unknown u => $"something new: {u.Name}",   // a variant newer than this library
        _ => throw new UnreachableException(),
    };
    #endregion

    public static async Task<ServerStatus> Mocked(CancellationToken ct)
    {
        #region snippet: testing
        var request = Substitute.For<IEsiRequest<ServerStatus>>();
        request.ExecuteAsync(Arg.Any<CancellationToken>()).Returns(EsiResult<ServerStatus>.Success(
            HttpStatusCode.OK,
            new ServerStatus { Players = 31_234, ServerVersion = "2345678", StartTime = DateTimeOffset.UtcNow, Vip = false },
            EsiResponseHeaders.Empty));

        var status = Substitute.For<IStatus>();
        status.GetStatus().Returns(request);
        #endregion
        return (await status.GetStatus().ExecuteAsync(ct)).Value!;
    }
}

#region snippet: middleware-class
/// <summary>Asks ESI for German names and descriptions where it has them.</summary>
public sealed class AcceptLanguageMiddleware : IEsiMiddleware
{
    public async ValueTask InvokeAsync(EsiContext context, EsiNext next)
    {
        context.Request.Headers["Accept-Language"] = "de";
        await next(context);
    }
}
#endregion
