# OPC UA xRegistry HTTP transport

This package implements an HTTP transport over `IXRegistryEndpoint`, not a registry
business engine. It targets the xRegistry **1.0-rc4** HTTP binding at
[`a1544396d63b74cdf1de5da6a269d02b88696802`](https://github.com/xregistry/spec/blob/a1544396d63b74cdf1de5da6a269d02b88696802/core/http.md).
The upstream specification is a release candidate. Neither this transport nor the
separate experimental OPC UA transaction extension is a final xRegistry standard.

The `HttpClient` endpoint is available on the project's portable library targets.
The ASP.NET Core minimal route API is compiled **only for .NET 8 and later**.
Both paths use open JSON values and explicit request delegates without
reflection-based serializer contracts, MVC discovery or runtime code generation.

## Direct client and dependency injection

```csharp
using Opc.Ua.XRegistry.Http;
using Opc.Ua.XRegistry.Protocol;

using var handler = new HttpClientHandler
{
    AllowAutoRedirect = false,
    UseCookies = false,
    UseDefaultCredentials = false
};
using var client = new HttpClient(handler);
var endpoint = new XRegistryHttpEndpoint(client, registryRoot);

XRegistryEndpointDescription description =
    await endpoint.InspectAsync(callerContext, cancellationToken);
XRegistryResponse result = await endpoint.ExecuteAsync(
    new XRegistryRequest(XRegistryAction.Read, "/schemagroups/mygroup/schemas/myschema")
    {
        View = XRegistryView.Metadata,
        Context = callerContext,
        Parameters = [new("inline", "meta")]
    },
    cancellationToken);
```

The endpoint does not dispose the supplied `HttpClient`. That client's credential
provider is the upstream identity. A protocol call context is **not** converted to
authentication headers. Use isolated clients/endpoints when mapping callers to
different upstream credential profiles.

```csharp
IHttpClientBuilder clientBuilder = services.AddXRegistryHttpEndpoint(
    registryRoot,
    new XRegistryHttpOptions
    {
        IsQualifiedBinding = deploymentHasQualifiedThisBackend
    },
    clientName: "registry-upstream");
```

This registers `XRegistryHttpEndpoint` and the default `IXRegistryEndpoint`, backed
by a named client with automatic redirects, cookies and ambient credentials
disabled. Credential-provider handlers can be configured on `clientBuilder`.
Do not install mutation retry handlers. The direct constructor cannot inspect or
override an injected client's handler configuration: **the caller must disable
redirects, retries and authentication-challenge replay there too**. Checking a
changed final response URI detects a misconfigured redirect pipeline but cannot
undo an already followed redirect.

`InspectAsync` retrieves the root, effective `/model`, and `/capabilities`. Missing,
malformed or failed inspection is an error, never an empty registry or a fabricated
legacy profile. `XRegistryHttpException.Response` retains a backend rejection,
including full Problem Details or opaque error bytes.
Reads obtain the effective model when needed to identify representations and
navigation links, including nested links in root/export responses. A root/export
rejection is retained without an additional model request.

Mutation guarantees are true only when **all** of these hold:

- `IsQualifiedBinding` explicitly attests deployment qualification, including
  failure atomicity and identical-write touch behavior.
- The root reports `specversion: "1.0-rc4"`, and capabilities include that revision
  in `specversions`.
- The `available` capability explicitly marks `entities`, `modelsource`, or
  `capabilities` as mutable. Each request also checks its corresponding aspect.
  Top-level `mutable` describes mutable capability configuration, not entity access.

Version advertisement alone is not qualification. Unqualified writes are rejected
before any mutation is sent. HTTP always reports `SupportsOperationReplay = false`
and rejects non-null protocol `OperationId` values. Each accepted mutation is one
complete HTTP request, sent once; a timeout or lost response is not retried.

## Modern HTTP hosting

```csharp
using Opc.Ua.XRegistry.Http;

app.UseAuthentication();
app.UseAuthorization();

app.MapXRegistry(
    "/registry",
    authoritativeEndpoint,
    new XRegistryHttpRouteOptions(publicRoot)
    {
        AuthorizeAsync = static (http, request, cancellationToken) =>
            new ValueTask<bool>(
                !request.IsMutation || http.User.IsInRole("xregistry-writer"))
    })
    .RequireAuthorization();
```

`MapXRegistry(IEndpointRouteBuilder, string, IXRegistryEndpoint,
XRegistryHttpRouteOptions)` returns an `IEndpointConventionBuilder`. The pattern is
a literal mount, such as `/registry` or `/`, not a catch-all template supplied by
the caller. `PublicRoot` is configured; request `Host` and forwarding headers never
choose the published registry origin.

`XRegistryHttpRouteOptions(Uri publicRoot)` provides:

| Option | Behavior |
|---|---|
| `Transport` | Validated `XRegistryHttpOptions` limits and root policy. |
| `RequireAuthenticatedUser` | Defaults to true. Upstream operator credentials do not satisfy it. |
| `CreateContextAsync` | Optional trusted inbound identity mapper: `Func<HttpContext, CancellationToken, ValueTask<XRegistryCallContext>>`. |
| `AuthorizeAsync` | Optional independent policy: `Func<HttpContext, XRegistryRequest, CancellationToken, ValueTask<bool>>`. Without it, only reads and OPTIONS are allowed. |

By default the context comes from authenticated `HttpContext.User`, with subject
from NameIdentifier, `sub`, or Name; roles, authentication authority and `sid` are
preserved. Arbitrary identity headers are never trusted. Hosts provide their
authentication schemes, credential validation, TLS listener configuration and
challenge behavior; `.RequireAuthorization()` can apply their ASP.NET policy.

Authorization receives action, canonical path, view, ordered parameters and caller
context **before inspection or payload processing**, with no request payload yet.
It can also filter advertised methods. The same context reaches inspection and
execution. Reads can be explicitly opened to anonymous callers; writes still need
an explicit authorization policy.

Mutations require atomic and conditional guarantees, write-touch for non-delete
operations, and both `SupportsPreparedMutations` and `IXRegistryPreparedEndpoint`.
The gateway obtains a preview through `PrepareAsync`, serializes and validates
the entire HTTP body and headers, and only then calls `CommitAsync`. Success reuses
the encoded preview without post-commit response shaping. Encoding failure disposes
the preparation without publication; an intervening registry change rejects the
candidate instead of rebasing it. Missing guarantees reject before any mutation.

A transport failure after commit may still leave an **unknown outcome**; it does
not imply rollback or authorize automatic retries. Ordinary outbound HTTP endpoints
do not provide remote preparation merely because they conform to the HTTP binding.
When supplied, `XRegistryEndpointDescription.PublicRoot` identifies authoritative
navigation URLs for rebasing to the frontend's configured root. Arbitrary foreign
navigation links are rejected, not followed.

## Supported wire profile

The transport maps GET/HEAD, PUT, PATCH, POST, DELETE and OPTIONS. It forwards root,
capabilities/offered capabilities, model/modelsource, export, discovery and
model-defined group/resource/meta/version routes to the authoritative endpoint.
It does not synthesize documents for unsupported APIs. PUT on collections and
PATCH on a document view without `$details` are rejected before execution.

- Resource and Version document views come from model `hasdocument` (default true),
  never from Content-Type. Raw `ByteString` bytes are exact, including JSON-looking
  content, non-UTF-8 data and present zero-length documents. Metadata views preserve
  JSON numbers, booleans, arrays, objects and explicit nulls.
- `$details` is a literal wire suffix on Resources/Versions. An encoded dollar in an
  identifier is not the suffix. A resource with `hasdocument: false` remains metadata
  with or without the suffix, and its emitted self URL has no suffix.
- Document metadata headers have PATCH semantics even for PUT/POST. An omitted
  attribute stays absent; a supplied scalar map is a full replacement. Missing
  Content-Type is decoded as explicit `contenttype: null`. If a protocol request
  supplies both metadata `contenttype` and `ContentType`, they must agree.
- Headers support model-defined scalar values and scalar maps, strict UTF-8
  percent encoding, legacy quoted values and explicit null deletion. Epoch zero
  and arbitrarily wide canonical integer literals are not narrowed or conflated
  with absent/null preconditions.
- Metadata-body writes reject extra `xRegistry-*` attribute headers. Complex
  headers, empty replacement maps, literal string `"null"`, dotted top-level names,
  HTTP case-colliding names and values that would change JSON type cannot be
  represented losslessly as document headers and are rejected.
- All query parameters retain order, repetitions, empty values and valueless flags.
  This includes `inline`, `filter`, `sort`, `doc`, `binary`, `collections`, `ignore`,
  `epoch`, `specversion`, default-version flags and unknown flags. The provider
  implements their semantics and ignores unknown/unsupported optional flags as
  required by its profile. The HTTP layer does not invent query semantics.
- Status codes, complete Problem Details, Allow, correlation, Location,
  Content-Location and Link relation/target pairs are retained. Known registry
  navigation URLs become canonical registry-relative protocol links and are
  rendered against the configured public root when hosted. Arbitrary extension
  strings and embedded documents are not rewritten.
- OPTIONS does not mutate and filters methods against the wire profile, guarantees
  and authorization. Allow and Access-Control-Allow-Methods agree and include
  OPTIONS. HEAD performs a read and retains Content-Length without sending a body.
- Incoming identity, gzip and deflate bodies are bounded both before and after
  decompression, with at most two encodings. Outgoing bodies use identity encoding.
  Metadata uses UTF-8 JSON; raw document content types are preserved.

An operation needing otherwise unrepresentable document headers can use **one**
metadata-body request containing its complete inline document and attributes, if
the provider supports that core representation. Otherwise it must be rejected.
Splitting it into metadata and document writes or using read/check/write is not an
atomic workaround.

## Security, limits and unsupported optional areas

Registry roots require HTTPS. `AllowLoopbackHttp` permits only explicit localhost
or loopback-IP HTTP roots for local use; it never permits arbitrary plaintext
remote roots. Credentials, queries, fragments, malformed escapes, traversal and
ambiguous separators are rejected. Navigation links must remain on the configured
origin **and inside its registry root**. Redirects are surfaced, never followed by
the transport. External document URL metadata is preserved without fetching it;
cross-origin document redirects are deliberately unsupported by this profile.

Default limits are 32 MiB per encoded/decoded body, JSON depth 64, 128 headers,
64 KiB of header names/values, 128 query parameters and 16,384 URI characters.
`RequestTimeout` defaults to 30 seconds over inspection/execution/body reads;
hosting additionally bounds response writing. Cancellation propagates throughout.
Small independent error budgets ensure a tiny configured payload limit still
produces a meaningful rejection. Configure the same or tighter representability
limits in the authoritative provider.

This module does **not** implement a registry engine, cursor issuance or automatic
page traversal, external model/schema resolution, external document retrieval,
HTTP operation replay, push/watch endpoints, compression on writes/responses,
Brotli/zstd, Range responses, or ETag/date validator semantics. HTTP `If-*`
validator preconditions are explicitly rejected rather than silently turned into
unconditional writes; use xRegistry epoch preconditions. Link parameters other than
relation and target have no field in the shared protocol contract. Discovery,
inline/base64 documents and optional query features are passed through to the
provider, not advertised as independently implemented business features here.

Independent NUnit golden-handler and ASP.NET TestHost tests live in
`tests\Opc.Ua.XRegistry.Http.Tests`; they do not rely on a registry business engine
or a same-codec client/server roundtrip for expected wire values.
