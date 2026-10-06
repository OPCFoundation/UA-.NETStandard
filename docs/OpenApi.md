# OPC UA over OpenAPI

This guide shows how to let an OPC UA server built with this stack describe
its REST binding as an OpenAPI document, and how to talk to the server from
generated REST clients. It is task-oriented: serve the document, generate a
client, get the JSON right, and avoid the traps of generated clients.

The binding itself (routes, encoding negotiation, discovery, WebSocket
sub-profile) is described in the [REST binding reference](WebApi.md).

## Contents

- [When to use the REST binding](#when-to-use-the-rest-binding)
- [The document is generated](#the-document-is-generated)
- [Serving the document](#serving-the-document)
- [Generating the document without a server](#generating-the-document-without-a-server)
- [Generating a client](#generating-a-client)
- [Calling the server](#calling-the-server)
- [.NET clients without code generation](#net-clients-without-code-generation)
- [Wire format cheat sheet](#wire-format-cheat-sheet)
- [Known issues with generated clients](#known-issues-with-generated-clients)
- [How the document is kept correct](#how-the-document-is-kept-correct)
- [Troubleshooting](#troubleshooting)

## When to use the REST binding

| You have | Use |
| --- | --- |
| A .NET application that can reference this stack | `ManagedSession` over `opc.tcp`, or over REST with `UseWebApiEndpoint` if only HTTPS gets through the network |
| A web front end, a script, a low-code platform, a language without an OPC UA SDK | The REST binding with a client generated from the OpenAPI document |
| Subscriptions, events, browsing large address spaces | A session. Over REST this means long-polled `/publish`; `opc.tcp` is cheaper |

The REST binding runs on the same Kestrel listener as the HTTPS binary and
`opcua+uajson` transports: one port, no extra process.

## The document is generated

The OPC Foundation publishes the mapping of the OPC UA services to
OpenAPI 3.0 in
[`UA-Nodeset/OpenApi`](https://github.com/OPCFoundation/UA-Nodeset/tree/latest/OpenApi),
produced by the model compiler. This stack does not ship those files. The
routes in `WebApiServiceRoutes` and the structure definitions of the
request and response types are the source of truth for what the binding
accepts and sends, so the document is generated from them at runtime by
`WebApiOpenApiGenerator` (package `Opc.Ua.Core.Schema`). It cannot drift
from the wire format, and a test compares it with the published documents
(see [How the document is kept correct](#how-the-document-is-kept-correct)).

Two levels of detail keep the size in check. The published documents are
about 300 KB for the two service sets; the generated ones are:

| Level | Option | Content | Size (all services) |
| --- | --- | --- | --- |
| Operations | default | Paths, `operationId`s, and a JSON object as every request and response body | about 12 KB |
| Schemas | `OpenApiIncludeSchemas = true` | Additionally one component schema per request, response and contained structure or enumeration (100 for all services) | about 46 KB |

Use *Operations* to document and explore the API, for example in an API
portal. Use *Schemas* as the input of an OpenAPI client generator that emits
typed models.

| `WebApiServiceSet` | Services |
| --- | --- |
| `AllServices` | All 28: Discovery, Session, View, Attribute, Method, MonitoredItem, Subscription |
| `Sessionless` | Read, Write, HistoryRead, HistoryUpdate, Call, Browse, BrowseNext, TranslateBrowsePathsToNodeIds |

Every service is a `POST` to a lower-case path (`/read`, `/browse`,
`/createsubscription`, ...). The body is the bare `<Service>Request` and the
answer is the bare `<Service>Response`, with no envelope. NodeManagement and
Query are not part of either set.

## Serving the document

The binding does not serve the document unless you ask for it. Set
`OpenApiDocumentPath`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.WebApi;

services.AddOpcUa()
    .AddHttpsTransport()
    .AddWebApiTransport(opt =>
    {
        opt.ServiceSet = WebApiServiceSet.AllServices;  // default
        opt.OpenApiDocumentPath = "/openapi.json";      // default null: not served
        opt.OpenApiIncludeSchemas = true;               // default false
    });
```

The server needs an `https://` base address; the
[Transports guide](Transports.md) shows how to configure one.

What the route does:

- **It is one of the REST routes.** It is mapped in the same route group as
  the service routes and needs whatever they need. When an authentication
  scheme is registered (`AddWebApiBearerAuth`, `AddWebApiBasicAuth`, ...),
  `GET /openapi.json` requires an authenticated caller like `POST /read`.
  Only `/findservers` and `/getendpoints` are anonymous. The document is
  generated from the routes the host maps and reveals nothing of the address
  space, but it is not published to callers the service routes would turn
  away.
- **`servers` follows the path base.** Behind `UsePathBase("/opcua")` the
  document lists the relative server URL `/opcua/`, which OpenAPI 3.0
  clients resolve against the address they loaded the document from. Without
  a path base the list is omitted and OpenAPI assumes `/`.
- **The document is generated once** per server URL and kept in memory.

### Sessionless only

```csharp
.AddWebApiTransport(opt =>
{
    opt.ServiceSet = WebApiServiceSet.Sessionless;
    opt.OpenApiDocumentPath = "/openapi.json";
})
```

Only the eight sessionless routes are mapped, and the document describes
exactly those, so a generated client contains the operations the binding
maps. Discovery and session management remain available on the binary and
`opcua+uajson` endpoints of the same listener, for clients that need them.

The service set only limits the routes. Whether the server answers a request
that carries no session (OPC 10000-4 6.3) is decided by the server's session
manager: without a `ValidateSessionLessRequest` handler on `ISessionManager`,
such a request is answered with `Bad_SessionIdInvalid`.

### Composing your own pipeline

Hosts that build their own ASP.NET Core pipeline instead of using
`AddWebApiTransport` map the same endpoints directly and apply their own
conventions to the group:

```csharp
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseEndpoints(endpoints =>
{
    endpoints
        .MapWebApiEndpoints(new WebApiTransportOptions
        {
            ServiceSet = WebApiServiceSet.Sessionless,
            OpenApiDocumentPath = "/spec/opcua.json"
        })
        .RequireAuthorization();
});
```

The generator that builds the document is taken from the request services
(`WebApiOpenApiGenerator`, registered by `AddWebApiTransport`); a default
generator is used when none is registered.

## Generating the document without a server

`WebApiOpenApiGenerator` returns the document as a `JsonObject`, for example
to publish it to an API portal in a build step:

```csharp
using System.Text.Json.Nodes;
using Opc.Ua.Bindings;
using Opc.Ua.Schema.OpenApi;

var generator = new WebApiOpenApiGenerator();
JsonObject document = generator.Generate(
    WebApiServiceSet.Sessionless,
    includeSchemas: true,
    serverUrl: "https://plant.example.com/opcua/");
File.WriteAllText("opcua-sessionless.json", document.ToJsonString());
```

The generator reads the structure definitions from the encodeable types of
the OPC UA namespace. Pass an `IDataTypeDefinitionResolver` to the
constructor to describe other types. The generator builds the document with
`System.Text.Json.Nodes` and no reflection, so it is NativeAOT compatible
(see [Runtime schema generation](SchemaGeneration.md)).

## Generating a client

Point any OpenAPI 3.0 generator at the running server. Enable
`OpenApiIncludeSchemas` first, otherwise every body is an untyped object:

```bash
# .NET
openapi-generator-cli generate -g csharp \
    -i https://server:4843/openapi.json -o ./opcua-client \
    --additional-properties=packageName=Plant.OpcUa.Rest,targetFramework=net8.0

# TypeScript (fetch)
openapi-generator-cli generate -g typescript-fetch \
    -i https://server:4843/openapi.json -o ./opcua-client-ts

# Python
openapi-generator-cli generate -g python \
    -i https://server:4843/openapi.json -o ./opcua-client-py
```

When the server requires authentication, download the document with the
credentials the service routes accept and generate from the file.

The OPC Foundation publishes ready-generated clients built from its own
documents, so you can skip generation entirely:

| Language | Repository |
| --- | --- |
| .NET | [opcua-webapi-dotnet](https://github.com/OPCFoundation/opcua-webapi-dotnet) |
| Python | [opcua-webapi-python](https://github.com/OPCFoundation/opcua-webapi-python) |
| TypeScript | [opcua-webapi-typescript](https://github.com/OPCFoundation/opcua-webapi-typescript) |

A client generated from the sessionless document has eight operations; one
generated from the all-services document has 28. Generate against the
document your server actually serves.

## Calling the server

### A generated .NET client

The names below are those of the OPC Foundation .NET client; a client you
generate from the server's document has the same operations and models.

```csharp
using Opc.Ua.WebApi.Api;
using Opc.Ua.WebApi.Client;
using Opc.Ua.WebApi.Model;

var api = new DefaultApi(new Configuration { BasePath = "https://server:4843" });

ReadResponse response = await api.ReadAsync(new ReadRequest
{
    RequestHeader = new RequestHeader
    {
        AuthenticationToken = sessionToken, // from /createsession, see below
        TimeoutHint = 10000
    },
    TimestampsToReturn = 2, // Both
    NodesToRead =
    [
        new ReadValueId { NodeId = "ns=2;s=Pump01.Measurements.Flow", AttributeId = 13 }
    ]
});

DataValue flow = response.Results[0];
Console.WriteLine($"{flow.Value} (UaType {flow.UaType})");
```

Set the base path explicitly. Before relying on the status of a value, read
[the DataValue status issue](#datavalue-status-and-the-published-documents)
below.

### Sessions over REST

The session services work like their binary counterparts; the session is
identified by the `AuthenticationToken` in every `RequestHeader`:

1. `POST /createsession` and keep `AuthenticationToken` from the response.
2. `POST /activatesession` with that token and a `UserIdentityToken`.
3. Every further request carries
   `"RequestHeader": { "AuthenticationToken": "<token>" }`.
4. `POST /closesession` when done.

REST requests run against the REST endpoint the server announces for its
`https://` base address (`TransportProfileUri`
`http://opcfoundation.org/UA-Profile/Transport/https-uajson-openapi`,
OPC 10000-6 7.4.1); `/getendpoints` lists it with its user token policies.

For subscriptions, call `/createsubscription` and `/createmonitoreditems`,
then keep one `/publish` request outstanding. The server holds each
`/publish` request open until notifications arrive or the request's
`TimeoutHint` expires. Your HTTP client's timeout must be longer than that
hint (see [Long-poll `/publish`](WebApi.md#long-poll-publish)).

## .NET clients without code generation

If the client can reference this stack, you do not need a generated client:

| API | When |
| --- | --- |
| `ManagedSessionBuilder.UseWebApiEndpoint(url)` | You want the full `ISession`: reconnect, subscriptions, the typed companion-spec clients, all over REST |
| `WebApiClient` (`IWebApiClient`) | You want plain request/response calls with the stack's own request types and no session management |

```csharp
using Opc.Ua;
using Opc.Ua.Client.WebApi;

using var http = new HttpClient { BaseAddress = new Uri("https://server:4843/") };
using var client = new WebApiClient(http, new WebApiClientOptions
{
    Encoding = WebApiEncoding.Compact
});

ReadResponse response = await client.ReadAsync(new ReadRequest
{
    RequestHeader = new RequestHeader { AuthenticationToken = sessionToken },
    NodesToRead = new ReadValueId[]
    {
        new() { NodeId = VariableIds.Server_ServerStatus_CurrentTime, AttributeId = Attributes.Value }
    }
});
```

Both use the stack's own encoder and are therefore not affected by the
[known issues](#known-issues-with-generated-clients) below.

## Wire format cheat sheet

Useful when writing requests by hand or debugging a generated client. The
full rules are in [Part 6 5.4](https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4).

| OPC UA type | JSON | Example |
| --- | --- | --- |
| NodeId | String | `"i=2258"`, `"ns=2;s=Pump01"`, `"nsu=http://opcfoundation.org/UA/;i=2258"` |
| QualifiedName | String | `"2:Flow"` |
| Enumeration | Integer | `"TimestampsToReturn": 2` |
| DateTime | ISO 8601 string | `"2026-09-23T10:15:30.1234567+02:00"` |
| Int64, UInt64 | String | `"9007199254740993"` |
| StatusCode | Object, omitted when Good | `{ "Code": 2150891520 }` (Verbose adds `"Symbol"`) |
| LocalizedText | Object | `{ "Locale": "en", "Text": "Pump" }` |
| Variant | `UaType` + `Value` (+ `Dimensions` for matrices) | `{ "UaType": 6, "Value": 42 }`, `{ "UaType": 11, "Value": [1.5, 2.5] }` |
| DataValue | The Variant's fields plus `Status` and timestamps | `{ "UaType": 6, "Value": 42, "SourceTimestamp": "..." }` |
| ExtensionObject | `UaTypeId` + body | see below |

Common `UaType` values: 1 Boolean, 6 Int32, 7 UInt32, 10 Float, 11 Double,
12 String, 13 DateTime, 17 NodeId, 21 LocalizedText, 22 ExtensionObject.

**Omitted fields decode as their default.** A request only needs the fields
it sets; `"MaxAge"`, `"RequestHandle"` and friends can be left out.

**Encoding flavour.** The server answers in Compact JSON unless the request
asks for Verbose (`Content-Type` or `Accept: application/json;
encoding=verbose`). Verbose keeps default values and adds status symbols,
which helps when debugging. The generated document describes the Compact
encoding.

**ExtensionObject bodies** come in two forms. A client built with this
stack sends the structure's fields inline next to `UaTypeId`. Generated
clients cannot, because the `ExtensionObject` schema only has `UaTypeId`,
`UaEncoding` and `UaBody`, so they send the OPC UA *binary* encoding, base64
in `UaBody`, with `UaEncoding: 1`. The server accepts both. An anonymous
identity token for `/activatesession`, as a generated client sends it:

```json
"UserIdentityToken": {
  "UaTypeId": "i=321",
  "UaEncoding": 1,
  "UaBody": "CQAAAGFub255bW91cw=="
}
```

`i=321` is `AnonymousIdentityToken_Encoding_DefaultBinary`; the body is the
binary-encoded `PolicyId` `"anonymous"` (Int32 length, then UTF-8).

## Known issues with generated clients

### DataValue status and the published documents

Part 6 [Table 42](https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.2.18)
names the DataValue status field `"Status"`. The documents the OPC
Foundation publishes name it `"StatusCode"` (reported as
[UA-Nodeset#146](https://github.com/OPCFoundation/UA-Nodeset/issues/146)).
The stack follows the specification text: its encoder writes `"Status"`,
its decoder reads `"Status"` only, and the generated document names the
field `Status`.

| Client generated from | Effect |
| --- | --- |
| This server's document | Reads and writes the status correctly |
| The published OPC Foundation document, or a ready-generated client of the OPC Foundation | Ignores `"Status"` in responses and reports **Good** for bad and uncertain values; the status it sends in a `Write` is ignored by the server |

Until the publication and the specification text agree, generate clients
from the server's own document, or read `Status` from the raw response.
The stack's own clients (`ManagedSession`, `WebApiClient`) are unaffected.

### Validating responses against the published schemas

The published `DataValue` schema sets `"additionalProperties": false`. A
validator that enforces it rejects every DataValue with a non-Good status
that the stack sends, for the same reason. The generated schema names the
field `Status` and validates it.

### Polymorphic structures

Filters (`DataChangeFilter`, `EventFilter`), notifications and identity
tokens are ExtensionObjects. Generated clients only see the
`UaTypeId`/`UaEncoding`/`UaBody` envelope, so they have to produce and
consume the binary encoding (see the
[cheat sheet](#wire-format-cheat-sheet)). If the application needs event
filters or subscriptions, a .NET client using
[`UseWebApiEndpoint`](#net-clients-without-code-generation) is considerably
less work.

## How the document is kept correct

`WebApiOpenApiConformanceTests` (in `tests/Opc.Ua.Core.Schema.Tests/OpenApi/`)
compares the generated document with the two published documents, which are
embedded in the test assembly only (see the README next to them for the
source commit). Everything both describe has to be equal:

- the paths, `operationId`s and the request and response schema of every
  operation, for both service sets;
- the set of component schemas the operations reach (100 for all services);
- every enumeration with its members and values;
- for every structure the property names, types, formats, integer ranges and
  inheritance.

The differences that remain are listed in the test with their reason, and
the test fails when a listed difference disappears or a new one appears:

| Where | Generated | Published | Reason |
| --- | --- | --- | --- |
| `DataValue` status property | `Status` | `StatusCode` | Part 6 Table 42; UA-Nodeset#146 |
| `description`, `default` keywords | not written | specification links, .NET defaults | Documentation and defaults, not part of the contract; the Compact encoding omits default values |

`WebApiOpenApiWireFormatTests` validates messages the binding actually
writes (every request and response, plus populated samples with variants,
status codes, references and extension objects) against the generated
component schemas, so the schemas describe the wire format and not only the
names.

## Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| `GET /openapi.json` returns 404 | `OpenApiDocumentPath` is not set (the default) or differs, or the REST binding isn't registered | Set `OpenApiDocumentPath` in `AddWebApiTransport` |
| `GET /openapi.json` returns 401 | An authentication scheme is registered; the document requires the credentials the service routes require | Send the credentials |
| Generated client has untyped request and response bodies | The document was served without schemas | Set `OpenApiIncludeSchemas = true` |
| Swagger UI "Try it out" goes to the wrong host behind a proxy | The relative server URL is resolved against the proxy's internal address | Serve the document behind the same path base the clients use, or edit `servers` in a generated copy |
| `/createsession` returns an HTTP error, `/read` works | `ServiceSet = Sessionless`: the route is not mapped and the request falls through to the listener's binary/JSON handler | Use `AllServices`, or the binary endpoint for sessions |
| HTTP 400, empty body | The body is not valid OPC UA JSON for that request (misspelled property, wrong type, NodeId not parseable) | Compare with the [cheat sheet](#wire-format-cheat-sheet); the server logs the decode error at Information level |
| `ServiceResult` `BadSessionIdInvalid` | Request without, or with a stale, `AuthenticationToken` | Create and activate a session first; send its token in every `RequestHeader` |
| Bad values show up as Good in a generated client | [Status vs StatusCode](#datavalue-status-and-the-published-documents) | Generate the client from the server's document |
| `/publish` times out on the client (`BadTimeout`) | HTTP client timeout shorter than `TimeoutHint` | Raise the client timeout above the hint |
