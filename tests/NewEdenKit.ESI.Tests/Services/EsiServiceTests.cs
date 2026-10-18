using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using NewEdenKit.ESI.Abstractions.Middleware;
using NewEdenKit.ESI.Abstractions.Requests;
using NewEdenKit.ESI.Abstractions.Results;
using NewEdenKit.ESI.Services;

namespace NewEdenKit.ESI.Tests.Services;

public sealed class EsiServiceTests
{
    private const string StatusOperation = "GetStatus";

    private readonly IEsiClientFactory _factory = Substitute.For<IEsiClientFactory>();
    private readonly List<IReadOnlyList<IEsiMiddleware>?> _snapshots = [];

    public EsiServiceTests()
    {
        _factory.CreateEsiClient(
                Arg.Any<string>(), Arg.Any<JsonTypeInfo<Status>>(), Arg.Any<Action<IEsiRequestParameters>?>(),
                Arg.Any<string?>(), Arg.Do<IReadOnlyList<IEsiMiddleware>?>(_snapshots.Add))
            .Returns(Substitute.For<IEsiRequest<Status>>());
        _factory.CreateEsiClient(
                Arg.Any<string>(), Arg.Any<Action<IEsiRequestParameters>?>(),
                Arg.Any<string?>(), Arg.Do<IReadOnlyList<IEsiMiddleware>?>(_snapshots.Add))
            .Returns(Substitute.For<IEsiRequest<EsiNoContent>>());
    }

    [Fact]
    public void CreateEsiClient_NoMiddleware_PassesEmptySnapshot()
    {
        new StatusService(_factory).GetStatus();

        _snapshots.ShouldHaveSingleItem().ShouldNotBeNull().ShouldBeEmpty();
    }

    [Fact]
    public void CreateEsiClient_ForwardsArgumentsToFactory()
    {
        Action<IEsiRequestParameters> configure = _ => { };

        new StatusService(_factory).GetStatus(configure, "token");

        _factory.Received(1).CreateEsiClient(
            StatusOperation, TestJsonContext.Default.Status, configure, "token", Arg.Any<IReadOnlyList<IEsiMiddleware>?>());
    }

    [Fact]
    public void CreateEsiClient_NoContent_UsesNonGenericFactoryOverload()
    {
        new StatusService(_factory).Ping();

        _factory.Received(1).CreateEsiClient(StatusOperation, null, null, Arg.Any<IReadOnlyList<IEsiMiddleware>?>());
    }

    [Fact]
    public void Use_InstancesAndCallbacks_AreSnapshotInRegistrationOrder()
    {
        var first = Substitute.For<IEsiMiddleware>();
        var third = Substitute.For<IEsiMiddleware>();
        var service = new StatusService(_factory);

        service.Use(first).Use((ctx, next) => next(ctx)).Use(third).GetStatus();

        var snapshot = _snapshots.ShouldHaveSingleItem().ShouldNotBeNull();
        snapshot.Count.ShouldBe(3);
        snapshot[0].ShouldBeSameAs(first);
        snapshot[2].ShouldBeSameAs(third);
    }

    [Fact]
    public void Use_ReturnsServiceInterfaceForChaining()
    {
        IStatusService service = new StatusService(_factory);

        var returned = service.Use((ctx, next) => next(ctx));

        returned.ShouldBeSameAs(service);
    }

    [Fact]
    public void Use_AfterRequestCreated_DoesNotChangeEarlierSnapshot()
    {
        var service = new StatusService(_factory);
        service.Use(Substitute.For<IEsiMiddleware>());
        service.GetStatus();

        service.Use(Substitute.For<IEsiMiddleware>());
        service.GetStatus();

        _snapshots.Count.ShouldBe(2);
        _snapshots[0]!.Count.ShouldBe(1);
        _snapshots[1]!.Count.ShouldBe(2);
    }

    [Fact]
    public void Use_SeparateInstances_DoNotShareMiddleware()
    {
        new StatusService(_factory).Use(Substitute.For<IEsiMiddleware>());

        new StatusService(_factory).GetStatus();

        _snapshots.ShouldHaveSingleItem().ShouldNotBeNull().ShouldBeEmpty();
    }

    [Fact]
    public void Use_Null_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new StatusService(_factory).Use((IEsiMiddleware)null!));
    }

    [Fact]
    public void Constructor_DerivedClassNotImplementingTSelf_Throws()
    {
        Should.Throw<InvalidOperationException>(() => new MismatchedService(_factory));
    }

    public sealed record Status(long Players);

    public interface IStatusService : IEsiService<IStatusService>
    {
        IEsiRequest<Status> GetStatus(Action<IEsiRequestParameters>? configure = null, string? token = null);

        IEsiRequest<EsiNoContent> Ping();
    }

    private sealed class StatusService(IEsiClientFactory factory) : EsiService<IStatusService>(factory), IStatusService
    {
        public IEsiRequest<Status> GetStatus(Action<IEsiRequestParameters>? configure = null, string? token = null) =>
            CreateEsiClient<Status>(StatusOperation, TestJsonContext.Default.Status, configure, token);

        public IEsiRequest<EsiNoContent> Ping() => CreateEsiClient(StatusOperation, null, null);
    }

    private sealed class MismatchedService(IEsiClientFactory factory) : EsiService<IStatusService>(factory);
}

[JsonSerializable(typeof(EsiServiceTests.Status))]
internal sealed partial class TestJsonContext : JsonSerializerContext;
