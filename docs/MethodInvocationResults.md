# Complete method invocation results

Use `MethodInvocationResult` when an asynchronous method handler needs to return
an operation `ServiceResult`, ordered output values, and optional per-input
results together. The ordinary `OnCall` handlers remain available for methods
that only need an operation result and output values.

## Register a handler

Configure a predefined method through the existing
[fluent node-manager builder](NodeManagers.md#wiring-callbacks-the-configure-partial):

```csharp
builder.Node<MethodState>(methodId)
    .OnCallWithResult(async (context, method, objectId, inputs, ct) =>
    {
        MethodInvocationResult result =
            await device.InvokeAsync(objectId, inputs, ct).ConfigureAwait(false);
        return result;
    });
```

`OnCallWithResult` also works on the `IVirtualNodeBuilder` returned by
`ResolveNodes`. Its handler is applied to each materialized method without
replacing an existing handler. Typed method builders retain their concrete
`INodeBuilder<TState>` type when chaining this extension.

For direct configuration, assign the same asynchronous delegate to
`MethodState.OnCallMethodWithResultAsync`. Fluent registration rejects duplicate
complete-result handlers and mixing complete-result and ordinary handlers with
`BadConfigurationError`, in either registration order. A synchronous method
call returns `BadNotSupported` rather than blocking on a complete-result handler.

## Result contract

`MethodState.CallAsync` validates executability, access, input count, and input
types before invoking the handler. The handler receives the actual receiver
`objectId`, the supplied input arguments, and the caller's cancellation token.

- `OperationResult` must be non-null. Its status and diagnostic text are retained.
- `OutputArguments` use declaration order. A Good or Uncertain operation result
  must include every declared output. Bad operation results do not expose outputs.
- `InputArgumentResults` may be omitted. When present, it must include one
  non-null `ServiceResult` for every supplied input, including Good positions.
  The stack does not fabricate input results omitted by the handler.
- The result snapshots both argument arrays during construction.

A null callback result, an incomplete input-result array, or an incorrect output
count for a Good or Uncertain result produces `BadUnexpectedError` without
copying partial outputs.

## Resolved diagnostics

Store diagnostic strings in `ServiceResult`, not indexes from another response's
string table. When forwarding a decoded diagnostic, construct a `ServiceResult`
with the source response's string table; it resolves namespace URIs, symbolic
IDs, locale, and localized text before the server encodes them into its own
response table.

Nested diagnostic text is retained even when the inner status is Good.
Additional information still requires both the requested diagnostics mask and
user permission. Encoding respects `DiagnosticInfo.MaxInnerDepth`, counting the
outer diagnostic as one level.

These contracts complement the OPC UA Part 4 Call service's ordered argument
results and diagnostics; they do not add remote execution or change node
ownership.
