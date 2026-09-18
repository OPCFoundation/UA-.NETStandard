# Generation-bound session clients

`ISessionBindingProvider` is an optional capability for operations whose identity
must not change during a sequence of requests or after a generated client is
returned. Stock `Session` and `ManagedSession` implement it. Existing `ISession`,
`ISessionClient` and `ITransportChannel` implementations acquire no new mandatory
members, and ordinary unbound clients retain their normal reconnect behavior.

## Capture and use

Establish and authenticate the owning session through the usual direct factory
or [client DI setup](Sessions.md). Then capture a client:

```csharp
if (session is not ISessionBindingProvider provider)
{
    throw new ServiceResultException(StatusCodes.BadNotSupported);
}

ISessionClient binding = await provider.CreateBindingAsync(ct);
try
{
    DataValue state = await binding.ReadValueAsync(
        VariableIds.Server_ServerStatus_State, ct);
    var file = new FileTypeClient(binding, fileNodeId, telemetry);
    uint handle = await file.OpenAsync(1, ct);
    try
    {
        ByteString bytes = await file.ReadAsync(handle, 4096, ct);
    }
    finally
    {
        await file.CloseAsync(handle, CancellationToken.None);
    }
}
finally
{
    await binding.CloseAsync(CancellationToken.None);
    binding.Dispose();
}
```

The existing generated ObjectType clients consume `ISessionClient`, so no
parallel Read/Browse/Call implementation or new connection manager is needed.
Capture does not create a new server session or open document content.

The binding is non-owning with respect to the original session and network
connection. Closing or disposing it invalidates only the captured client and
releases its local diagnostics. It does not hold an exclusive gate for its
lifetime. The application continues to own the original session's lifetime.

## What is captured

The binding retains the authenticated native transport generation, the
session/authentication incarnation, and private namespace/server mapping
snapshots. A source map mutation, including a change followed by restoration of
the same values, invalidates the old binding. `StringTable.GetSnapshot` captures
the mapping and its mutation `Version` together.

Capture also checks that the owning session's maps agree with the live context
of the transport actually selected for dispatch. Contradictory holders reject
capture. Both the source context and the copied context track table-holder
generations: replacing either table invalidates the binding even if the original
reference is restored before the next request. Assigning an unchanged reference
does not itself invalidate a binding.

The bound client cannot replace its channel/session incarnation or override its
authentication token. Its endpoint and channel-certificate observations are
captured values, not references to a later selected peer. Its generated clients
continue interpreting NodeIds against the captured maps.

For UA Secure Conversation binary transports, dispatch validation and submission
to the captured inner channel occur at the native channel owner's replacement
gate. Response completion is awaited outside that gate. Managed bindings compose
the lease's entry, swap and reconnect generation checks with that native path;
they do not enter the ordinary managed idempotent-retry loop.

Before-dispatch invalidation rejects the request. A request already dispatched
can complete only against its captured transport; if the binding is invalidated
while it is in flight, its result is rejected rather than treated as evidence for
the replacement generation. A transport failure associated with proven
invalidation retains the original exception as its cause.

## Invalidation, cancellation and support

Invalidated bindings report `BadSecurityChecksFailed`; a locally closed client
can also report its normal closed-client status. Invalidation is permanent for
that binding, even if the owning capability later becomes current again.
Cancellation remains
`OperationCanceledException` and is not converted into a binding success.
Capture on a closed session fails, and reconnect is not supported on the captured
transport itself.

After a legitimate reconnect, namespace-table update or authorized endpoint
relocation, capture a fresh binding and resolve portable identities again.
An old binding fails rather than silently retargeting old NodeIds. This does not
change the origin/identity policy applied by a higher-level client.

The stock implementation supports UA-SC binary native channels (including their
TCP and byte-transport-derived channels) and managed leases over those channels.
Other transport implementations and custom mapping-table subclasses without the
required owner tracking reject capture with `BadNotSupported`, as do custom
message-context implementations. Custom session
adapters may expose `ISessionBindingProvider` only if they enforce the complete
dispatch/result guarantee; comparing current endpoint labels is not sufficient.
No support is inferred merely from an adapter implementing `ISession`.

This is an additive dispatch capability, not a new resolver, cache, reconnect
engine, registry persistence mechanism or server-restoration phase.

## Federation

[xRegistry federation](XRegistry.md#federation) captures both the referencing and
remote clients. Metadata/type checks and generated Resource/Versions/FileType
access use those captured clients and their maps. `VerifyLogicalResourceAsync`
releases its temporary binding; `FollowExternalReferenceAsync` returns the remote
generated client with its binding retained. Callers release that returned
`ResourceTypeClient.Session` when finished, after closing any file handles.

Custom forwarding adapters without the binding capability are rejected with
`BadNotSupported` before native verification or content operations. A changed
origin cannot be accepted by matching local Xids/NodeIds, and replacing the owning
session after Follow cannot redirect the returned client's first Open/Read.
