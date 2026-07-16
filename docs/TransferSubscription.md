# TransferSubscriptions

## Overview

The transfer subscription service is fundamental for *zero data loss* scenarios, where network connections are interrupted, clients have to restart or load balancing requires to transfer sessions to other clients.
Using the transfer subscription service set allows to recover from such situations and to keep the data loss as minimal as possible.

The TransferSubscriptions service for the Server and Client libraries is supported from Nuget version `1.4.368` and consists of the following elements:

* Updated C# Stack that supports the *TransferSubscriptions* service for Client and Server as specified in  in [Part 4](https://reference.opcfoundation.org/v104/Core/docs/Part4/5.13.7/)
* The updated server library supports to
  * Transfer subscriptions with optional inital values according to the service specification using the service call.
  * **Breaking change**: Modified `INodeManager` interface which is extended with a `TransferMonitoredItems` [method](https://github.com/OPCFoundation/UA-.NETStandard/blob/8c1a659ecf5c1616b3b7f132706324b90f9ff015/Libraries/Opc.Ua.Server/NodeManager/INodeManager.cs#L309), to support the transfer of the monitored items hosted in various types of NodeManagers and to implement a unified way to queue initial values.
  * Ported sample `NodeManager` implementations, which can be taken as a reference to port other custom implementations, e.g. [here](https://github.com/OPCFoundation/UA-.NETStandard/blob/8c1a659ecf5c1616b3b7f132706324b90f9ff015/Applications/Quickstarts.Servers/SampleNodeManager/SampleNodeManager.cs#L2937).
* Updated client library which supports multiple ways to transfer subscriptions, applicable for any compliant server.
The following use cases are supported to transfer a subscription:
  * **Transfer from active session**: The client creates a new session in the same process and transfers subscriptions into the new session while the old session is still active.
  * **Transfer from closed session**: The client closes the old session but has the new `DeleteSubscriptionsOnClose` property set to `false`. The old session is closed, but the subscriptions remain *abandoned* on the server and keep collecting samples. The client creates a new session and transfers the abandoned subscriptions to the new session, for which the client library has still all information available.
  * **Client restart from persisted storage**: The client saves subscriptions to a persisted xml stream, restarts itself, creates a new session, loads the saved subscriptions from xml stream and transfers the subscriptions on the server.
  * **Transfer after session reconnect**: The client `SessionReconnectHandler` can not reconnect to the existing session, it creates a new session and tries to transfer a subscription before recreating the full subscriptions.
* Client Session library enhancements:
  * A new Session property `DeleteSubscriptionsOnClose` which, if set to `false`, does not delete the subscriptions if a session is closed on the server. To preserve the legacy behavior, the default of the property is `true`.
  * A new Subscription property `RepublishAfterRestart` which assumes, if set to `true`, that unacknowledged publish requests on the server after a transfer need to be republished to minimize the data loss. Otherwise remaining publish requests are only acknowledged after the transfer.
  * A new `Session.TransferSubscriptions` [method](https://github.com/OPCFoundation/UA-.NETStandard/blob/8c1a659ecf5c1616b3b7f132706324b90f9ff015/Libraries/Opc.Ua.Client/Session.cs#L3410) to transfer a subscription managed by the client library. The new API can implicitly handle all of the above mentioned use cases. A requirement on the server side is not only the support of the TransferSubscriptions service, but also the implementation of the `GetMonitoredItems` ([see Part5](https://reference.opcfoundation.org/v104/Core/docs/Part5/9.1/)) standard method.
  * Existing clients which use the `SessionReconnectHandler` get the improved support (no client changes necessary).
* The updated C# [Reference Server](../Applications/ConsoleReferenceServer) with ported NodeManager samples to support the new [TransferSubscriptions](https://reference.opcfoundation.org/v104/Core/docs/Part4/5.13.7/) service set.
* Multiple [unit tests](https://github.com/OPCFoundation/UA-.NETStandard/blob/8c1a659ecf5c1616b3b7f132706324b90f9ff015/Tests/Opc.Ua.Client.Tests/SubscriptionTest.cs#L455) demonstrating the use cases for subscription transfer.
* Updated NodeManagers with reference implementation of the necessary functions.

## Porting an existing server

If a server is derived from the StandardServer class and if the custom NodeManagers all support the new TransferMonitoredItems method, the support becomes implicitly available.
Typically the following porting steps are necessary:

* Use the new `MonitoredItem` constructor which has no `Session` parameter, it is implicitly available in the `Subscription` and the `MonitoredItem`can not keep a private reference when the subscription is transferred.
* Add the `TransferMonitoredItems` method from another `NodeManager` sample to the custom `NodeManager` implementations.
* Depending on the `NodeManager` implementation, add or fix the `ReadInitialValue` method. The monitored item transfer must be able to queue an unfiltered initial value, if requested.
* More subtle changes might be required, e.g. how the monitored item handle to read the attributes is obtained.
* Once the server builds, if available run a CTT test against a node in the ported NodeManager.
* A sample Commit which ports the NetStandard-Samples codebase from 367 to 368 is [here](https://github.com/OPCFoundation/UA-.NETStandard-Samples/pull/267/commits/5d990b7f39880941a5e788d17b903fd41254a804).

## Known limitations and issues

* **There is no opt out**.
* **Breaking change**: There is currently no support for NodeManagers to *not* support the new transfer service. Unless the NodeManagers are all ported to support the monitored items transfer, build errors will prevent from using the latest 1.4.368 library.
* There is no client sample for special use cases like e.g. the client restart in a docker container.
* In some .NET Core 3.1 projects a warning CS8032 occurs due to missing analyzer. Current believe is this warning can be safely disabled.

## Recovering from unsolicited `Good_SubscriptionTransferred`

Per [OPC UA Part 4 §5.14.7](https://reference.opcfoundation.org/Core/Part4/v105/docs/5.14.7) the server emits a `StatusChangeNotification` with `Good_SubscriptionTransferred` on the **old** Session whenever the subscription is transferred away to a **new** Session via `TransferSubscriptions`. The receiving Session is expected to treat the subscription as gone and stop dispatching for it.

Some servers have been observed to deliver this notification against a subscription the client has just **freshly created** on its current Session — for example, Kepware after a server re-initialisation can leak the pre-restart subscription's pending status notifications onto the new session (especially noticeable because subscription identifiers are re-used starting at `1`). The client then sees the per-subscription dispatch silently disabled even though, from its point of view, the subscription is alive and should keep receiving data. Tracked as issue [#3540](https://github.com/OPCFoundation/UA-.NETStandard/issues/3540).

To opt into automatic in-place recovery, set `SubscriptionRecoveryPolicy.RecreateOnUnsolicitedTransfer`:

```csharp
// Classic (V1) subscription
var subscription = new Subscription(telemetry, options)
{
    RecoveryPolicy = SubscriptionRecoveryPolicy.RecreateOnUnsolicitedTransfer,
    FastDataChangeCallback = OnDataChange
};
```

```csharp
// V2 subscription options
var options = new Opc.Ua.Client.Subscriptions.SubscriptionOptions
{
    PublishingEnabled = true,
    RecoveryPolicy = SubscriptionRecoveryPolicy.RecreateOnUnsolicitedTransfer
};
```

When the policy is `RecreateOnUnsolicitedTransfer` and a `Good_SubscriptionTransferred` arrives while the subscription is still actively owned by this Session, the SDK will:

1. Drop every queued acknowledgement targeting the dead subscription id — this prevents `BadSubscriptionIdInvalid` ack errors on servers that re-use subscription identifiers across generations.
2. Recreate the subscription on the **same** Session via `CreateSubscription`, obtaining a fresh server-side subscription identifier.
3. Re-issue the monitored items so the data flow resumes against the new identifier.

The default is `SubscriptionRecoveryPolicy.ReportOnly`, which preserves the spec-strict behaviour: the `PublishStateChangedMask.Transferred` flag is raised (V1) or `PublishState.Transferred` is dispatched (V2) so the application can react manually, and the per-subscription publish dispatch is stopped (V1) or left as a no-op (V2).

### Caveats

* Auto-recovery is **not** lossless failover. The server-side retransmission queue and any triggering relationships tied to the invalidated subscription identifier are lost; only the subscription's wire-level options and the configured monitored items are re-applied.
* The recovery path runs against the same Session. If the Session itself is reconnecting, the V1 implementation defers to the reconnect pipeline rather than racing it; the V2 implementation lets the in-place recreate run when the subscription is `Created`.
* The "unsolicited" classification is conservative — concurrent recovery dispatches collapse into one — but it does not attempt to detect every legitimate cross-session/cross-client failover. If your design relies on another Session/Client legitimately pulling subscriptions away via `TransferSubscriptions`, keep the default `ReportOnly` policy and handle the `PublishStatusChanged` event explicitly.
