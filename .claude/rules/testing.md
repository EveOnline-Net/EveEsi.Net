---
paths:
  - "tests/**"
---

# Testing

- xUnit + Shouldly + NSubstitute. No other assertion or mocking libraries.
- One test project per `src` project: `tests/EveEsi.Net.<Project>.Tests`.
- Test names: `Method_Scenario_ExpectedResult`.
- Arrange/act/assert, one behaviour per test. Use `[Theory]` for input variations.
- **HTTP:** use a hand-written `FakeHttpMessageHandler` that returns canned responses and records requests.
  NSubstitute can't mock the protected `SendAsync`. Never hit the real ESI in unit tests.
- **Middleware:** test against a hand-built `EsiContext` and a stub `EsiNext`. Cover pass-through,
  short-circuit and the response-after-`next` paths.
- **`Use` overloads:** for each level (global, service, request), test that both an `IEsiMiddleware` instance
  and an async callback get registered and run, and that callbacks and instances keep their registration order.
- **Pipeline ordering:** test that the levels run global → service → request, and that the service
  snapshot is taken when the request is created.
- **Factory validation:** one test per build-time check (unknown operation ID, missing token on a route with a
  scope, missing route value when the URI is built).
- **Converters:** round-trip tests with real ESI JSON samples in `TestData/`. Required for each union shape and for
  enum edge cases (unknown value → `Unknown`, unconvertible members such as hex colours or digits).
- **ID width:** converter and DTO tests include IDs above `int.MaxValue` (e.g. `1_000_000_000_000`).
- **Generator:** snapshot tests. A small fixture spec goes in and the expected `.g.cs` must come out.
  Generation failures (unclassifiable schema, enum conflict) have their own tests.
- **Results:** assert on `EsiResult<T>` (status, `IsSuccess`, `Error`), never on thrown exceptions for HTTP errors.
