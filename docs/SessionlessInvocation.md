# Session-less Service invocation

OPC 10000-4 [§6.3](https://reference.opcfoundation.org/Core/Part4/v105/docs/6.3)
lets a Client invoke a subset of the Services without CreateSession and
ActivateSession. The request then carries either an Access Token in
`RequestHeader.authenticationToken`, or no token at all. This page shows how
to enable it on a server built with this stack.

## Contents

- [Which Services](#which-services)
- [Enabling it](#enabling-it)
- [Caller identity](#caller-identity)
- [Answers without the feature](#answers-without-the-feature)
- [Taking over completely](#taking-over-completely)
- [Calling the server](#calling-the-server)

## Which Services

Session-less invocation is limited to the Service Sets §6.3.1 names:

| Service Set | Services |
| --- | --- |
| View | Browse, BrowseNext, TranslateBrowsePathsToNodeIds (not RegisterNodes / UnregisterNodes) |
| Attribute | Read, Write, HistoryRead, HistoryUpdate |
| Method | Call |
| NodeManagement | AddNodes, AddReferences, DeleteNodes, DeleteReferences |
| Query | QueryFirst, QueryNext |

Every other Service, for example CreateSubscription or Publish, needs a
Session and is answered with `Bad_SessionIdInvalid` without one.
`SessionManager.IsSessionlessService(RequestType)` returns the same decision.

A request without a Session has no Session state to keep continuation
points in: Browse returns none, and BrowseNext reports every continuation
point as invalid.

## Enabling it

With dependency injection:

```csharp
services.AddOpcUa()
    .AddServer(options => { /* … */ })
    .AddSessionlessInvocation()          // OPC 10000-4 §6.3
    .AddJwtIssuer(options => { /* … */ }); // validates the Access Tokens
```

`AddSessionlessInvocation` registers `SessionlessInvocationOptions`. The
hosted server applies them to the session manager it uses, including one
created by `AddSessionManager(...)`, unless that manager was configured
already.

Without dependency injection, set the options on the session manager:

```csharp
var sessionManager = (SessionManager)server.CurrentInstance.SessionManager;
sessionManager.SessionlessInvocation = new SessionlessInvocationOptions();
```

## Caller identity

| Request | Identity | Option |
| --- | --- | --- |
| `authenticationToken` is a String NodeId | The Access Token, validated as an issued JWT token (OPC 10000-6 §7.6.5.2.3) by the server's identity registry, the authenticators ActivateSession uses | `AcceptAccessTokens` (default `true`) |
| no `authenticationToken` | An anonymous user | `AllowAnonymous` (default `false`) |

- §6.3.1 requires encryption for the Access Token. It is only accepted over
  a SecureChannel with `SignAndEncrypt`, or over HTTPS. On any other channel
  the request is rejected.
- A token no authenticator accepts is answered with the authenticator's
  error, or `Bad_IdentityTokenRejected`.
- A request without a token is answered with `Bad_IdentityTokenInvalid`
  unless `AllowAnonymous` is set.
- Session tokens are UInt32 or ByteString NodeIds. A token of that kind that
  names no Session stays `Bad_SessionIdInvalid`, which is what a Client
  whose Session expired needs to see.

Override `SessionManager.ValidateSessionlessRequestAsync` for different
rules.

## Answers without the feature

Without `AddSessionlessInvocation` and without a
`ValidateSessionLessRequest` handler, a request that carries no
`authenticationToken` is answered with `Bad_ServiceUnsupported`, as §6.3.1
prescribes for a Server that does not support session-less invocation.
Requests with a token that names no Session stay `Bad_SessionIdInvalid`.

## Taking over completely

A handler on `ISessionManager.ValidateSessionLessRequest` decides before the
built-in handling. It receives the token and the request type and sets the
identity or an error. The Service Set limit above applies before the
handler is called.

## Calling the server

Over opc.tcp, any client channel without a Session works, for example a
`SessionClient` on a channel from `ClientChannelManager`:

```csharp
var client = new SessionClient(channel, telemetry);
ReadResponse response = await client.ReadAsync(
    new RequestHeader { AuthenticationToken = new NodeId(accessToken, 0) },
    0,
    TimestampsToReturn.Neither,
    [new ReadValueId { NodeId = VariableIds.Server_ServerStatus_State, AttributeId = Attributes.Value }],
    ct);
```

Over the [REST binding](WebApi.md) the token is a String NodeId in the JSON
request header:

```json
{
  "RequestHeader": { "AuthenticationToken": "s=<access-token>" },
  "NodesToRead": [ { "NodeId": "i=2259", "AttributeId": 13 } ]
}
```
