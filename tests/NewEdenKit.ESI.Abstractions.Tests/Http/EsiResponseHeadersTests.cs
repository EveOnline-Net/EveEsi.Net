using NewEdenKit.ESI.Abstractions.Http;

namespace NewEdenKit.ESI.Abstractions.Tests.Http;

public sealed class EsiResponseHeadersTests
{
    [Fact]
    public void TypedAccessors_EsiHeaders_AreParsed()
    {
        var headers = Create(
            ("ETag", "\"abc123\""),
            ("Expires", "Sat, 17 Oct 2026 12:00:00 GMT"),
            ("Last-Modified", "Sat, 17 Oct 2026 11:00:00 GMT"),
            ("Cache-Control", "public, max-age=3600"),
            ("Content-Language", "en"),
            ("X-Pages", "7"),
            ("X-ESI-Error-Limit-Remain", "95"),
            ("X-ESI-Error-Limit-Reset", "42"),
            ("X-Ratelimit-Group", "char-asset"),
            ("X-Ratelimit-Limit", "1800/15m"),
            ("X-Ratelimit-Remaining", "1795"),
            ("X-Ratelimit-Used", "5"),
            ("Retry-After", "30"),
            ("X-ESI-Request-ID", "req-1"));

        headers.ETag.ShouldBe("\"abc123\"");
        headers.Expires.ShouldBe(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero));
        headers.LastModified.ShouldBe(new DateTimeOffset(2026, 10, 17, 11, 0, 0, TimeSpan.Zero));
        headers.MaxAge.ShouldBe(TimeSpan.FromHours(1));
        headers.ContentLanguage.ShouldBe("en");
        headers.Pages.ShouldBe(7);
        headers.ErrorLimitRemain.ShouldBe(95);
        headers.ErrorLimitReset.ShouldBe(TimeSpan.FromSeconds(42));
        headers.RateLimitGroup.ShouldBe("char-asset");
        headers.RateLimitLimit.ShouldBe("1800/15m");
        headers.RateLimitRemaining.ShouldBe(1795);
        headers.RateLimitUsed.ShouldBe(5);
        headers.RetryAfter.ShouldBe(TimeSpan.FromSeconds(30));
        headers.RequestId.ShouldBe("req-1");
    }

    [Fact]
    public void TypedAccessors_MissingHeaders_ReturnNull()
    {
        var headers = EsiResponseHeaders.Empty;

        headers.ETag.ShouldBeNull();
        headers.Expires.ShouldBeNull();
        headers.MaxAge.ShouldBeNull();
        headers.Pages.ShouldBeNull();
        headers.RetryAfter.ShouldBeNull();
    }

    [Theory]
    [InlineData("X-Pages", "seven")]
    [InlineData("Expires", "tomorrow")]
    [InlineData("Retry-After", "Sat, 17 Oct 2026 12:00:00 GMT")]
    public void TypedAccessors_MalformedValue_ReturnNull(string name, string value)
    {
        var headers = Create((name, value));

        headers.Pages.ShouldBeNull();
        headers.Expires.ShouldBeNull();
        headers.RetryAfter.ShouldBeNull();
    }

    [Fact]
    public void TryGetValues_IsCaseInsensitive()
    {
        var headers = Create(("etag", "\"x\""));

        headers.TryGetValues("ETAG", out var values).ShouldBeTrue();
        values.ShouldBe(["\"x\""]);
        headers.ETag.ShouldBe("\"x\"");
    }

    [Fact]
    public void Constructor_RepeatedNames_AreMergedInOrder()
    {
        var headers = Create(("Warning", "a"), ("warning", "b"));

        headers.Count.ShouldBe(1);
        headers.TryGetValues("Warning", out var values).ShouldBeTrue();
        values.ShouldBe(["a", "b"]);
    }

    [Fact]
    public void MaxAge_NoMaxAgeDirective_ReturnsNull()
    {
        Create(("Cache-Control", "no-cache")).MaxAge.ShouldBeNull();
    }

    private static EsiResponseHeaders Create(params (string Name, string Value)[] headers) =>
        new(headers.Select(h => new KeyValuePair<string, IEnumerable<string>>(h.Name, [h.Value])));
}
