using System.Net;
using Microsoft.Extensions.DependencyInjection;
using NewEdenKit.ESI.Services;
using NewEdenKit.ESI.Tests.TestSupport;

namespace NewEdenKit.ESI.Tests.Docs;

/// <summary>
/// Keeps the consumer docs (README, <c>docs/usage.md</c>, <c>llms.txt</c>) honest: their code is the compiled code in
/// <see cref="UsageExamples"/>, and the examples
/// behave as the guide says. Run with <c>UPDATE_USAGE_GUIDE=1</c> to copy the regions into the docs.
/// </summary>
public sealed class UsageGuideTests : IDisposable
{
    private readonly FakeHttpMessageHandler _handler = new();
    private readonly ServiceProvider _provider;

    public UsageGuideTests()
    {
        var services = new ServiceCollection();
        services.AddEveEsi(o =>
        {
            o.ApplicationName = "DocsTests/1.0";
            o.Contact = "docs@example.com";
        });
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => _handler));
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public void Docs_ShowExactlyTheCompiledExamples()
    {
        var examples = Snippets.FromExamples();
        if (Environment.GetEnvironmentVariable("UPDATE_USAGE_GUIDE") == "1")
        {
            foreach (var document in Snippets.Documents)
            {
                Snippets.Update(document, examples);
            }
        }

        var shown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in Snippets.Documents)
        {
            foreach (var (name, code) in Snippets.FromDocument(document))
            {
                examples.ContainsKey(name).ShouldBeTrue($"{document} shows snippet '{name}', which UsageExamples.cs doesn't have.");
                code.ShouldBe(examples[name], $"{document} snippet '{name}' differs from UsageExamples.cs. Run with UPDATE_USAGE_GUIDE=1.");
                shown.Add(name);
            }
        }

        examples.Keys.Except(shown).ShouldBeEmpty("every snippet region is shown in the docs");
    }

    [Fact]
    public void Register_ResolvesEveryService()
    {
        var services = new ServiceCollection();

        UsageExamples.Register(services);
        UsageExamples.RegisterCustomized(new ServiceCollection());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICharacter>().ShouldNotBeNull();
    }

    [Fact]
    public async Task PublicCall_ReturnsTheName()
    {
        _handler.Respond(HttpStatusCode.OK, Character);

        var name = await UsageExamples.CharacterName(Service<ICharacter>(), 2_112_000_000, Ct);

        name.ShouldBe("CCP Bartender");
    }

    [Fact]
    public async Task Results_NotFound_IsAResultNotAnException()
    {
        _handler.Respond(HttpStatusCode.NotFound, """{"error":"Character not found"}""");

        (await UsageExamples.Describe(Service<ICharacter>(), 1, Ct)).ShouldBe("no such character");
    }

    [Fact]
    public async Task PagePagination_FollowsXPages()
    {
        _handler.Respond(HttpStatusCode.OK, Assets(1), ("X-Pages", "2")).Respond(HttpStatusCode.OK, Assets(2), ("X-Pages", "2"));

        var assets = await UsageExamples.AllAssets(Service<IAssets>(), 2_112_000_000, "token", Ct);

        assets.Select(a => a.ItemId).ShouldBe([1_000_000_000_001, 1_000_000_000_002]);
        _handler.Requests.Select(r => r.PathAndQuery).ShouldBe(["/characters/2112000000/assets?page=1", "/characters/2112000000/assets?page=2"]);
    }

    [Fact]
    public async Task CursorPagination_WalksBackUntilEmpty()
    {
        _handler.Respond(HttpStatusCode.OK, Jobs("b1", 1)).Respond(HttpStatusCode.OK, Jobs("b2", 1)).Respond(HttpStatusCode.OK, Jobs(null, 0));

        var jobs = await UsageExamples.AllFreelanceJobs(Service<IFreelanceJobs>(), Ct);

        jobs.Count.ShouldBe(2);
        _handler.Requests.Select(r => r.PathAndQuery).ShouldBe(["/freelance-jobs", "/freelance-jobs?before=b1", "/freelance-jobs?before=b2"]);
    }

    [Fact]
    public async Task Middleware_SendsAcceptLanguage()
    {
        await UsageExamples.RequestMiddleware(Service<IStatus>(), Ct);
        await UsageExamples.ServiceMiddleware(Service<IStatus>(), Ct);

        _handler.Requests.ShouldAllBe(r => r.Headers["Accept-Language"] == "de");
    }

    [Fact]
    public async Task Testing_TheMockedServiceReturnsTheCannedValue() =>
        (await UsageExamples.Mocked(Ct)).Players.ShouldBe(31_234);

    private const string Character = """
        {"achievement_score":0,"birthday":"2015-03-24T11:37:00Z","bloodline_id":3,"corporation_id":109299958,"gender":"male","name":"CCP Bartender","race_id":2,"security_status":0.0}
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Assets(int n) =>
        $$"""[{"is_singleton":false,"item_id":{{1_000_000_000_000 + n}},"location_flag":"Hangar","location_id":60003760,"location_type":"station","quantity":1,"type_id":34}]""";

    private static string Jobs(string? before, int count) =>
        $$"""{"freelance_jobs":[{{string.Join(",", Enumerable.Repeat(Job, count))}}]{{(before is null ? "" : $$""","cursor":{"before":"{{before}}"}""")}}}""";

    private const string Job = """{"id":"8c3f5f3e-5a5e-4c1b-9f0e-6f1d2a3b4c5d","last_modified":"2026-10-18T12:00:00Z","name":"Mine","progress":{"current":0,"desired":10},"state":"Active"}""";

    private T Service<T>()
        where T : notnull => _provider.GetRequiredService<T>();
}
