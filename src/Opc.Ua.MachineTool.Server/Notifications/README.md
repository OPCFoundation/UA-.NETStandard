# MachineTool notifications and instance capabilities

These bindings implement the released OPC 40501-1 1.02 notification model using
the existing Machinery owner, generated states, standard condition lifecycle,
and shared client subscription engine. They do not issue machine commands.

## Explicit selection

```csharp
await using var machine = await context.AddMachineTool("Mill")
    .WithProvider(machineObservations)
    .WithIdentification(identity =>
    {
        identity.Manufacturer = new LocalizedText("Example");
        identity.SerialNumber = "Mill-1";
        identity.ProductInstanceUri = "urn:example:mill-1";
    })
    .WithNotifications(new MachineToolNotificationOptions
    {
        Provider = operatorNotifications,
        MaximumAlerts = 128,
        MaximumMessages = 128,
        RefreshTimeout = TimeSpan.FromSeconds(30)
    })
    .BuildAsync(cancellationToken);

await machine.RefreshNotificationsAsync(cancellationToken);
```

`IMachineToolNotificationProvider.ReadAsync(afterSequence, cancellationToken)`
returns the complete alert inventory and consecutive informational messages
after the accepted sequence. Sequences start at one. Gaps, replays, duplicate
alert keys, invalid severities, missing usable timestamps, and oversized batches
are errors, not truncation or successful empty observations.

The complete initial batch is validated before the machine factory runs.
Providers remain application-owned. A missing selected provider fails setup;
omitting `WithNotifications` preserves the mandatory-only standard model.

## Standard semantics

The released NodeSet and sections 8.6.1–8.6.2 place `Messages` below
`MachineTool/Notification`, **not** below `Monitoring`. The `MonitoringType`
reference in the Errors and Alerts CU is a prose inconsistency; this binding
preserves the released model layout.

| Observation | Standard type | Severity |
|---|---|---|
| Error with active/inactive state | `AlertType` or supplied generated subtype | 667–1000 |
| Warning with active/inactive state | `AlertType` or supplied generated subtype | 334–666 |
| Informational message without active/inactive state | Concrete application-owned subtype of `NotificationEventType` | 1–333 |

An alert has a stable provider key and standard condition identity. Activation
makes it unacknowledged. Clearing the physical cause does not acknowledge it:
an inactive, unacknowledged alert remains retained. The provider must keep that
identity until it is inactive and no longer retained. Removing an active or
unacknowledged alert is rejected.

Enable, Disable, AddComment, Acknowledge and ConditionRefresh use the stack's
existing Part 9 implementation. `AllowedOperations` can restrict the four
instance methods; server role/user permissions still apply independently.
Confirmation, shelving, reset, hardware error-clear and other optional methods
are not invented or enabled by this binding. The production module continues to
own its separate `InterruptionConditionType` instances and events.

The supplied quality and acquisition time remain on condition samples; event
receive time is distinct. The root, Notification and Messages notifier paths
use native event propagation. Node removal and server shutdown follow the
containing machine's Machinery registration lifetime.

Provider and event callbacks must honor cancellation. A refresh has a
cooperative deadline of at most one minute. A callback cannot await disposal or
recursively refresh its own runtime. Accepted publication failures retain a
bounded set of snapshots with stable EventIds for the next refresh to retry;
external sinks must be idempotent because a failed sink may already have
delivered part of a publication.

## Passive clients

```csharp
await using var client = session.CreateMachineToolClient(telemetry);

var conditions = await client.ObserveConditionsAsync(machineId,
    new MachineToolObservationOptions
    {
        Window = TimeSpan.FromSeconds(10),
        MaximumNotifications = 100
    }, cancellationToken);

var events = await client.ObserveEventsAsync(machineId,
    new MachineToolObservationOptions
    {
        Window = TimeSpan.FromSeconds(5),
        MaximumNotifications = 50
    }, cancellationToken);

var production = await client.ReadProductionAsync(machineId,
    new MachineToolProductionReadOptions
    {
        MaximumElements = 4096,
        MaximumReferences = 16384
    }, cancellationToken);
```

Each event workflow owns exactly one managed subscription and releases it on
completion, cancellation, failure or facade disposal. Limits are 1–100 retained
notifications, at most one minute, and one mebibyte of decoded event payload.
Server overflow and local count truncation are explicit. A retained refresh
requires both RefreshStart and RefreshEnd; timeout before completion is an error.
Neither workflow acknowledges, enables, clears or configures anything.

Generated event records are accompanied by the original namespace-qualified
Variant fields. `WithEventDecoder` accepts bounded generated companion decoder
registrations before observation, allowing GMS or vendor fields without another
subscription implementation.

Production reads preserve actual generated/vendor identities, ordered jobs and
programs, part sets, parts, counters, CurrentState and LastTransition samples.
They do not invent a longer state history. Individual DataValues retain their
quality and timestamps; a multi-request inventory is not an atomic equipment
snapshot. Changed available NodeVersions reject racing topology reads.
`DataValue.Null` distinguishes absent optional members from access/service
failures, which remain errors.

## Capability declarations

`machine.Capabilities` and `IConformanceContributor` describe that instance's
validated selections. The existing manager aggregates weak contributions; the
registration does not become another resource owner or retain removed machine
providers. WMTP and GMS-specific contributions remain independent.

The optional client-facing declaration uses the distinct library extension
namespace `urn:opcfoundation:netstandard:machinetool:capabilities`. Its read-only
`Capabilities` object is not a new standard MachineTool ObjectType or method.
`GetCapabilitiesAsync` returns explicit absence on servers without this
declaration rather than guessing conformance from namespace names.

Monitoring claims count the actual machine's variables and event notifiers and
compare them with the configured, published server-wide/per-subscription quotas.
The minimum claim requires all actual items and at least 20 items in one
subscription/session. `WithMonitoringRequirements` declares deployment
requirements without changing quotas; the full claim evaluates at least half,
rounded up, of both required session and subscription counts. These limits are
not reservations against concurrent clients.

Basic and BasicSecure profile claims additionally inspect the actual advertised
secure endpoint descriptions and certificate user-token policies. A None-only
endpoint is insufficient. Profile URIs come from released Table 121; CU names
come from section 14.1, including its canonical `SoftwareInformation` and
`Production Machinery Job Management` spellings.

## Companion observation projection

`WithObservationProjection` supplies a pure prepare callback for additional
generated companion observations. It runs after generic production, equipment
and prognosis preparation, before any commit. Rejection rolls back the prepared
generic update. Its returned, nonthrowing assignment action runs after generic
assignments and before change-mask/event publication, during both initialization
and refresh. Preparation must not mutate published nodes, perform I/O, acquire
provider data, or reenter the runtime.
