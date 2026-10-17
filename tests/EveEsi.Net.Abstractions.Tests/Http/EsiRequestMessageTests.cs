using System.Net;
using EveEsi.Net.Abstractions.Http;

namespace EveEsi.Net.Abstractions.Tests.Http;

public sealed class EsiRequestMessageTests
{
    [Fact]
    public void ToRelativeUri_RouteValues_AreSubstituted()
    {
        var request = new EsiRequestMessage(HttpMethod.Get, "/characters/{character_id}/contacts/{contact_id}");
        request.RouteValues.Add(new("character_id", "1000000000000"));
        request.RouteValues.Add(new("contact_id", "42"));

        request.ToRelativeUri().ShouldBe("/characters/1000000000000/contacts/42");
    }

    [Fact]
    public void ToRelativeUri_QueryPairs_KeepOrderAndRepeats()
    {
        var request = new EsiRequestMessage(HttpMethod.Delete, "/characters/{character_id}/contacts");
        request.RouteValues.Add(new("character_id", "1"));
        request.Query.Add(new("contact_ids", "2"));
        request.Query.Add(new("contact_ids", "3"));
        request.Query.Add(new("page", "1"));

        request.ToRelativeUri().ShouldBe("/characters/1/contacts?contact_ids=2&contact_ids=3&page=1");
    }

    [Fact]
    public void ToRelativeUri_Values_AreEscaped()
    {
        var request = new EsiRequestMessage(HttpMethod.Get, "/killmails/{killmail_hash}");
        request.RouteValues.Add(new("killmail_hash", "a/b c"));
        request.Query.Add(new("search", "Jita & Amarr"));

        request.ToRelativeUri().ShouldBe("/killmails/a%2Fb%20c?search=Jita%20%26%20Amarr");
    }

    [Fact]
    public void ToRelativeUri_MissingRouteValue_Throws()
    {
        var request = new EsiRequestMessage(HttpMethod.Get, "/characters/{character_id}");

        Should.Throw<InvalidOperationException>(() => request.ToRelativeUri());
    }

    [Fact]
    public void Headers_AreCaseInsensitive()
    {
        var request = new EsiRequestMessage(HttpMethod.Get, "/status");

        request.Headers["If-None-Match"] = "\"abc\"";

        request.Headers["if-none-match"].ShouldBe("\"abc\"");
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.NotModified, false)]
    [InlineData((HttpStatusCode)420, false)]
    public void EsiResponse_IsSuccessStatusCode_Is2xx(HttpStatusCode status, bool expected)
    {
        new EsiResponse(status, EsiResponseHeaders.Empty, ReadOnlyMemory<byte>.Empty).IsSuccessStatusCode.ShouldBe(expected);
    }
}
