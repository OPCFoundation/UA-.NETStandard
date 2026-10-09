# OPC UA concepts for SDK users

This guide explains the ideas the rest of the documentation assumes. Read it
alongside [Getting started](GettingStarted.md) or immediately afterward. It
describes the concepts and the matching SDK types without detailing each
API. Each section links to the guide with complete contracts and examples.

## Contents

- [Applications](#applications)
- [Endpoints, secure channels, and sessions](#endpoints-secure-channels-and-sessions)
- [Application trust and user identity](#application-trust-and-user-identity)
- [The address space](#the-address-space)
- [Node identity and namespaces](#node-identity-and-namespaces)
- [Values, status codes, and timestamps](#values-status-codes-and-timestamps)
- [Service results and errors](#service-results-and-errors)
- [Subscriptions and monitored items](#subscriptions-and-monitored-items)
- [Events and alarms](#events-and-alarms)
- [Information models and companion specifications](#information-models-and-companion-specifications)
- [Client/server and PubSub](#clientserver-and-pubsub)
- [How the SDK is organized](#how-the-sdk-is-organized)
- [Where to go next](#where-to-go-next)

## Applications

An OPC UA application is a client, a server, or both. Each application
instance has an *application instance certificate* and an *ApplicationUri*.
The URI identifies that installed instance and must match the URI in its
certificate. A product URI identifies the product rather than one installation.

In this SDK, you set the application name, URIs, and certificate-store root when
you configure a client or server. The hosting builders create the application
certificate on first start if the store does not contain one.

## Endpoints, secure channels, and sessions

A server offers one or more *endpoints*. An endpoint combines:

- A URL, such as `opc.tcp://localhost:62541/MinimalBoilerServer`.
- A transport, such as UA-TCP (`opc.tcp`), HTTPS, or WebSockets.
- A *security mode*: `None`, `Sign`, or `SignAndEncrypt`.
- A *security policy*, such as `Basic256Sha256`, which selects the algorithms.
- The user-token types the server accepts.

A client usually calls the discovery service `GetEndpoints` and selects the
endpoint whose security matches its requirements. The client then establishes
two layers:

| Layer | Purpose | Authenticated by |
| --- | --- | --- |
| Secure channel | Signs and encrypts the messages between the two application instances. | Application instance certificates. |
| Session | Carries the user identity, request context, and subscriptions. | A user token, such as anonymous, user name, X.509, or an issued token. |

A session can survive the loss of its channel: the client opens a new channel
and reactivates the same session. `ManagedSession` performs this recovery and,
if necessary, recreates the session. It is the recommended client entry point
for new applications. See [Sessions](Sessions.md) and [Transports](Transports.md).

## Application trust and user identity

Application trust and user identity are separate decisions:

- **Application trust** decides whether this *software instance* may open a
  secure channel. Each application keeps certificate stores: its own
  certificates, *trusted* peers, *issuers* needed to validate chains, and
  *rejected* certificates. An administrator approves a peer by placing its
  certificate, or the CA that issued it, in the trusted store.
- **User identity** decides *who* is using the session and what that user may
  do. The server authenticates the user token when the session is activated
  and maps the identity to roles and permissions.

Accepting unknown certificates automatically is a local-testing shortcut only;
it removes the protection against connecting to, or accepting, an impostor.
See [Certificates](Certificates.md), [Identity providers](IdentityProviders.md),
and [Role-based user management](RoleBasedUserManagement.md).

## The address space

A server exposes its data as an *address space*: a graph of *nodes* connected
by *references*. Each node has a *node class*:

| Node class | Represents |
| --- | --- |
| Object | A device, machine, or other entity; also folders. |
| Variable | A value that can be read, written, or monitored. |
| Method | An operation that a client can call on an object. |
| ObjectType, VariableType | The definitions that instances are based on. |
| DataType, ReferenceType | The definitions of value types and relationship types. |
| View | A subset of the address space. |

References have types. Hierarchical references, such as `Organizes`,
`HasComponent`, and `HasProperty`, form the tree you browse. Other references
describe relationships, such as `HasTypeDefinition`, which links an instance to
its type. Every server has a standard `Objects` folder (`ObjectIds.ObjectsFolder`)
that contains the `Server` object with status, capabilities, and diagnostics.

A server-side *node manager* owns part of the address space and implements its
behavior. See [Node managers](NodeManagers.md).

## Node identity and namespaces

A `NodeId` combines a *namespace index* with an identifier, which is a number,
a string, a GUID, or an opaque byte string. The index refers to an entry in the
server's namespace table (`NamespaceArray`), which lists namespace URIs:

- Index 0 is always the OPC UA namespace, `http://opcfoundation.org/UA/`.
- Index 1 is the server's own local namespace.
- Other indexes are assigned by the server and can differ between servers.

Store and exchange the namespace *URI*, not the index. Resolve the index from
the connected session's namespace table (`session.NamespaceUris`) when needed.
An `ExpandedNodeId` can carry the URI itself. Generated model constants use
this form; convert one with `ExpandedNodeId.ToNodeId(id, session.NamespaceUris)`.
Standard node IDs such as `VariableIds.Server_ServerStatus_CurrentTime` are in
namespace 0 and need no conversion.

A node's *BrowseName* is a `QualifiedName`: a name with a namespace index.
Clients can translate a browse path of names into a `NodeId`.

`NodeId`, `QualifiedName`, `Variant`, `DataValue`, and several other built-in
types are value types in this SDK. Test them with `IsNull` rather than comparing
them with `null`. See [Improved type safety](migrate/2.0.x/types.md) for details.

## Values, status codes, and timestamps

Reading a variable returns a `DataValue`, which contains four parts:

| Part | SDK member | Meaning |
| --- | --- | --- |
| Value | `WrappedValue` (`Variant`) | The value, or no value. |
| Status | `StatusCode` | Whether the value can be used. |
| Source timestamp | `SourceTimestamp` (`DateTimeUtc`) | When the value was produced at its source. |
| Server timestamp | `ServerTimestamp` (`DateTimeUtc`) | When the server last received or confirmed the value. |

Always check the status before you use the value. A status is *Good*,
*Uncertain* (usable with caution), or *Bad* (do not use the value). Use
`StatusCode.IsGood`, `StatusCode.IsUncertain`, and `StatusCode.IsBad`, or compare
with a constant such as `status == StatusCodes.BadNodeIdUnknown`.

A `Variant` holds any OPC UA built-in type, an array (`ArrayOf<T>`), a matrix
(`MatrixOf<T>`), or a structure (`ExtensionObject`). Extract a value with the
checked `TryGetValue` pattern:

```csharp
if (!StatusCode.IsBad(value.StatusCode) &&
    value.WrappedValue.TryGetValue(out DateTimeUtc time))
{
    Console.WriteLine($"{time} ({value.StatusCode})");
}
```

All OPC UA date and time values are UTC and use `DateTimeUtc`. Binary data uses
`ByteString`. Server-defined structures can be decoded at run time with
[Complex types](ComplexTypes.md), or compiled with
[Source-generated data types](SourceGeneratedDataTypes.md).

## Service results and errors

Clients call *services*, such as `Read`, `Write`, `Browse`, and `Call`. Most
services process a list of operations and return one result per operation, so
one failed item does not fail the whole call. Check each result's status.

A failure of the whole request, such as a timeout or a security error, raises a
`ServiceResultException`. Its `StatusCode` identifies the cause.
Convenience helpers can convert a Bad operation result into an exception; for
example, `ReadValueAsync` throws when the value's status is Bad.

## Subscriptions and monitored items

Polling with `Read` is inefficient for values that change. A *subscription*
lets the server report changes instead:

1. The client creates a *subscription* in its session. The *publishing
   interval* controls how often the server sends notifications.
2. The client adds *monitored items*. Each one watches a node attribute, usually
   a variable's value, or an event source. The *sampling interval* controls how
   often the server checks for changes; the *queue size* controls how many
   changes it keeps between notifications.
3. The server sends data-change or event notifications, and *keep-alive*
   messages when nothing has changed. If the client stops requesting
   notifications, the subscription expires after its lifetime.

The SDK offers callback subscriptions for long-lived application monitoring and
`IAsyncEnumerable` streams for short observations, such as waiting for a state
change. See [Subscriptions](Subscriptions.md).

## Events and alarms

Objects that are *event notifiers* report events, such as audit events or
state changes. A client subscribes to them with an event filter that selects the
event fields and types it needs. Alarms and conditions extend events with state
that operators acknowledge, confirm, or shelve. See
[Alarms and conditions](AlarmsAndConditions.md).

## Information models and companion specifications

An *information model* is a set of types and instances for a domain. The base
OPC UA model defines the server object, core types, and standard services.
*Companion specifications* build domain models on top of it, such as
Device Integration (OPC 10000-100) or Machinery (OPC 40001-1).

Models are exchanged as *NodeSet2* XML files. This SDK can use a model in two
ways:

- Generate typed code at build time, including a server node manager and typed
  client proxies for object types. See [Node managers](NodeManagers.md).
- Load NodeSet2 files into a running server without generated code. See
  [Runtime NodeSets](RuntimeNodeSets.md).

Models depend on other models; for example, Device Integration depends on the
base model. See [Model dependencies](ModelDependencies.md).

## Client/server and PubSub

OPC UA defines two communication patterns:

| | Client/server | PubSub |
| --- | --- | --- |
| Connection | A session between one client and one server. | No session; publishers send messages to a transport. |
| Interaction | Requests (read, write, browse, call) and subscriptions. | One-way dataset messages, plus optional actions. |
| Security | Secure channel plus per-session user identity. | Message security with group keys from a Security Key Service. |
| Transports | UA-TCP, HTTPS, WebSockets. | UDP, MQTT, Kafka, Ethernet. |

In PubSub, a publisher's *DataSetWriter* turns a *published dataset* into
dataset messages, and a *WriterGroup* sends them in network messages at a
configured interval over a *PubSubConnection*. A subscriber's *DataSetReader*
selects messages by publisher, writer group, and dataset writer identifiers,
and decodes them with the dataset metadata. Messages use UADP (binary) or JSON
encoding. PubSub applications do not need a client or server. See
[PubSub](PubSub.md).

## How the SDK is organized

- **Packages.** Start with `OPCFoundation.NetStandard.Opc.Ua.Client` or
  `OPCFoundation.NetStandard.Opc.Ua.Server`. Optional packages add transports,
  complex types, PubSub, redundancy, and companion models. See
  [Packages, platform support, and versioning](DeveloperGuide.md#packages-platform-support-and-versioning).
- **Hosting.** `services.AddOpcUa()` registers clients, servers, and features
  with .NET dependency injection and the Generic Host. Direct construction,
  such as `ManagedSessionBuilder` or `new StandardServer(telemetry)`, remains
  available. See [Dependency injection](DependencyInjection.md).
  In this documentation, *dependency injection* refers to the .NET pattern,
  while *DI* in [Device Integration](DeviceIntegration.md) refers to OPC 10000-100.
- **Asynchronous APIs.** Operations are asynchronous and accept a
  `CancellationToken`. Await them; do not block on their results.
- **Telemetry.** Components obtain loggers, metrics, and traces from an
  `ITelemetryContext`. With hosting, the SDK adapts the host's logging. See
  [Diagnostics](Diagnostics.md).
- **Ownership.** Dispose sessions, subscriptions, and certificate handles that
  you create. `ManagedSession` and subscriptions support `await using`.

## Where to go next

Return to the [documentation index](README.md) and follow the path for your
role: a client, a server, or a PubSub application. If you have not yet run a
client and server, start with [Getting started](GettingStarted.md). To add your
own model to a server and use it from a client, continue with
[Your first information model](FirstModel.md).
