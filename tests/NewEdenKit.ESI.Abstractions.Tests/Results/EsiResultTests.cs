using System.Net;
using NewEdenKit.ESI.Abstractions.Http;
using NewEdenKit.ESI.Abstractions.Results;
using NewEdenKit.ESI.Abstractions.Tests.TestSupport;

namespace NewEdenKit.ESI.Abstractions.Tests.Results;

public sealed class EsiResultTests
{
    [Fact]
    public void Success_HasValueAndNoError()
    {
        var result = EsiResult<TestDto>.Success(HttpStatusCode.OK, new TestDto(1_000_000_000_000), EsiResponseHeaders.Empty);

        result.IsSuccess.ShouldBeTrue();
        result.IsNotModified.ShouldBeFalse();
        result.StatusCode.ShouldBe(HttpStatusCode.OK);
        result.Value.Id.ShouldBe(1_000_000_000_000);
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Success_NullValue_Throws()
    {
        Should.Throw<ArgumentNullException>(() => EsiResult<TestDto>.Success(HttpStatusCode.OK, null!, EsiResponseHeaders.Empty));
    }

    [Fact]
    public void Success_NoContent_IsSuccess()
    {
        var result = EsiResult<EsiNoContent>.Success(HttpStatusCode.NoContent, default, EsiResponseHeaders.Empty);

        result.IsSuccess.ShouldBeTrue();
        result.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public void NotModified_IsNeitherSuccessNorError()
    {
        var result = EsiResult<TestDto>.NotModified(EsiResponseHeaders.Empty);

        result.IsSuccess.ShouldBeFalse();
        result.IsNotModified.ShouldBeTrue();
        result.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        result.Value.ShouldBeNull();
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Failure_HttpError_KeepsStatusAndError()
    {
        var error = new EsiError(EsiErrorKind.Http, "Character not found");

        var result = EsiResult<TestDto>.Failure(error, HttpStatusCode.NotFound);

        result.IsSuccess.ShouldBeFalse();
        result.IsNotModified.ShouldBeFalse();
        result.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        result.Error.ShouldBeSameAs(error);
        result.Headers.ShouldBeSameAs(EsiResponseHeaders.Empty);
        result.ToString().ShouldBe("Failure (404): Character not found");
    }

    [Fact]
    public void Failure_Transport_HasNoStatusAndKeepsException()
    {
        var exception = new HttpRequestException("connection refused");
        var error = new EsiError(EsiErrorKind.Transport, exception.Message) { Exception = exception };

        var result = EsiResult<TestDto>.Failure(error);

        result.StatusCode.ShouldBeNull();
        result.Error!.Kind.ShouldBe(EsiErrorKind.Transport);
        result.Error.Exception.ShouldBeSameAs(exception);
        result.Error.Details.ShouldBeEmpty();
        result.ToString().ShouldBe("Failure (no response): connection refused");
    }
}
