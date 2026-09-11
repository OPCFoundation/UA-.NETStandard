# OPC UA browse-path targets in WoT forms

The OPC UA protocol binder accepts a source target identified by `uav:id`, an
encoded NodeId in `href`, or `uav:browsePath`. Path-only forms work for reads,
writes, native observation, Method Calls, and native event subscriptions. Planning
is available on every Bindings target framework; execution requires .NET 8 or
later.

This is **source protocol addressing**. It does not implement browse-path-based
projection placement or turn a local component relationship into an upstream Call
receiver.

## Authoring a path-only form

```json
{
  "@context": [
    "https://www.w3.org/2022/wot/td/v1.1",
    {
      "uav": "http://opcfoundation.org/UA/WoT-Binding/",
      "plant": "urn:example:plant"
    }
  ],
  "title": "Plant temperature",
  "base": "opc.tcp://opc.example:4840/UA/Factory/",
  "uav:browsePathAnchor": "i=85",
  "securityDefinitions": { "none": { "scheme": "nosec" } },
  "security": "none",
  "properties": {
    "temperature": {
      "type": "number",
      "forms": [{
        "href": "",
        "op": ["readproperty", "observeproperty"],
        "uav:browsePath": "plant:Pump/plant:Temperature"
      }]
    }
  }
}
```

`i=85` is the standard Objects folder. The endpoint remains
`opc.tcp://opc.example:4840/UA/Factory/`: the application resource path is not
treated as a NodeId or discarded. Use the security definitions appropriate to
the deployment; the example does not prescribe unsecured production access.

A path beginning with `/` starts at the AddressSpace Root (`i=84`), independently
of an authored anchor. A relative path uses the nearest enclosing explicit
`uav:browsePathAnchor`; only when no explicit anchor exists does it use the
nearest enclosing `uav:id`. An outer explicit anchor therefore outranks a nearer
fallback identity. Relative paths without a valid portable starting identity are
rejected rather than resolved from Root.

The closest declared path wins, from form to affordance to document. Its namespace
prefixes retain the context of the object that **declared the path**, including
ordered contexts, local overrides, and resets. A form-local context does not
reinterpret a path inherited from its affordance.

## Syntax and native resolution

Path syntax reuses the stack's OPC 10000-4 relative-path parser:

| Spelling | Meaning |
| --- | --- |
| `/Objects/plant:Pump` | Hierarchical references, starting at Root. |
| `plant:Pump.plant:Temperature` | Hierarchical first step, then an Aggregates reference. |
| `<HasComponent>plant:Temperature` | A named forward reference, including subtypes. |
| `<#HasComponent>plant:Temperature` | Exactly HasComponent, without its subtypes. |
| `<!HasComponent>plant:Parent` | A named inverse reference. |
| `plant:Pump&/Primary` | One BrowseName containing a literal slash. |
| `{https://example.org/model/}Temperature` | One URI-qualified BrowseName. |
| `nsu=https://example.org/model/;Temperature` | The alternative namespace-URI qualification. |

Annex A name escapes such as `&.`, `&:`, `&#`, and `&&` retain literal characters.
Slashes inside a namespace-URI qualifier do not divide path steps. Numeric
namespace prefixes are not portable; even namespace zero is written without a
numeric prefix or with a bound namespace URI. A named ReferenceType must be
known to the connected Session's type tree; an unknown reference type fails
explicitly.

Before each source operation, the channel:

1. Resolves portable anchor and qualified names against the current Session,
   without adding namespaces to its table.
2. Uses `TranslateBrowsePathsToNodeIds` and requires one complete, unambiguous
   target on that Server. Duplicate results naming the same Node are harmless;
   partial, remote, missing, or distinct multiple targets are rejected.
3. Requires any simultaneously declared form or href NodeId to identify that
   same Node.
4. Reads NodeClass and requires a Variable for property operations, a Method
   for invocation, or an Object/View for event monitoring.

Failures occur before the business Read, Write, Call, or monitored-item creation.
Cancellation is propagated. The selected source security requirements are
rechecked rather than assumed to survive a Session configuration change.

The admitted target retains its Session configuration revision and source
namespace, server-table, and factory context. A later Session snapshot cannot
replace that admission during monitored-item creation or payload translation.
Notifications use a copy of the admitted context, and publication rejects a
known invalidated generation. Explicit form and href identifiers must also be
local to the selected Server before their namespace-qualified identities are
compared. The executing registry rechecks its own path bounds even if a
different registry compiled the form.

The Method's `uav:callObjectId` remains separate from both its path target and its
browse-path anchor. Resolving a Method path never guesses a receiver from local
placement metadata.

## Observation and lifetime

Observed values and events still use native monitored items, not value polling.
Path-based subscriptions additionally revalidate their addressing after Session
configuration changes and periodically for source AddressSpace changes. A known
stale or invalid target cannot continue delivering apparently valid data.
Resolution failures produce a bad-status notification; a later successful
resolution can restore delivery.

Initial path items and replacements are prepared disabled. When a path changes
target, or recovers after an unavailable period, the existing native subscription
replaces its monitored item. Reporting is enabled
after its namespace context is ready, so its initial data value is not discarded
as a provisional notification. A generation change during creation or enablement
retains the obligation to obtain a fresh native sample. Recovery therefore
restores a Good notification even when the source value itself is unchanged.
Event filters are rebuilt using the admitted namespace mapping.

The maintenance loop belongs to the returned `IWotSubscription`, not to the
caller's creation cancellation token. Disposing it stops the loop and timer,
removes the Session callback, and deletes the native subscription. Applications
continue to own the Session lifetime according to `DisposeSession`.

```csharp
services.AddWotProtocolBinders()
    .AddOpcUaWotBinding(options =>
    {
        options.SessionFactory = ConnectSessionAsync;
        options.BrowsePathRefreshInterval = TimeSpan.FromSeconds(5);
    });
```

`BrowsePathRefreshInterval` defaults to ten seconds. `TimeProvider` is injectable
for deterministic scheduling. `WotBindingBounds.MaxBrowsePathElements` defaults
to 64, and `MaxUriLength` also bounds the path text. Offline capture has separate
safety ceilings, configurable on `WotBrowsePathTarget.FromForm`.

## Captured addressing and consumer impact

`WotFormExtractor` captures original document context before retaining detached
form snapshots. Href resolution retains that capture. Directly constructed forms
use the same context processor with the supplied plan context; they cannot
recover an enclosing document identity that the caller did not supply.

`WotAddressingDescriptor.BrowsePathTarget` exposes an immutable
`WotBrowsePathTarget`, whose `Elements` are URI-qualified `WotBrowsePathStep`
values rather than cached Session-local indexes. The Types library also exposes
`WotBrowsePathTarget.FromForm` for callers that need this capture without a
runtime executor.

Valid NodeId-only forms keep their existing operation path. Path addressing adds
translation and NodeClass checks, and active path subscriptions add addressing
maintenance requests. Consumers must not rely on a supplied path being ignored:
inconsistent simultaneous identities and malformed explicit identities fail
instead of selecting a different target silently.
