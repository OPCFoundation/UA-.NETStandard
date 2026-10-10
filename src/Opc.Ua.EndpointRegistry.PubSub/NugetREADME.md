# OPC 30455 PubSub Binding

Reusable correspondence and server integration for the MQTT JSON and UADP Part 14
profiles. Metadata reads and registration never configure or connect a broker.

`PubSubBindingRules` checks native configurations without I/O.
`EndpointRegistryPubSubBinding` attaches to the server-owned PubSub configuration
view and the registry's single committed state. It invalidates References before
retirement, surfaces read-only consumer Endpoints and inline Messages, and exposes
native `PubSubBindingSnapshotDataType` values after activation.

Remote observations require a configured Publisher identity, authorized broker
locator and explicit Publisher MQTT version. They never acquire native PubSub
targets. Schema associations must be explicitly supplied for the same metadata
configuration version; this module does not register schemas or apply PubSub
configuration.

## Construction and ownership

Register `EndpointRegistryPubSubNodeManagerFactory` with the ordinary server
alongside its existing Endpoint Registry and PubSub factories. The registry must
initialize first. The PubSub view can activate afterwards: the integration observes
real Objects rather than guessing NodeIds from names. For DI, call
`AddEndpointRegistryPubSubBinding` on `IOpcUaServerBuilder` after registering those
features. It registers only the integration manager, not another runtime or store.

For direct integration, construct `EndpointRegistryPubSubBinding` with the two
existing managers, the server message context and `ITelemetryContext`, then await
`StartAsync`. Dispose the binding before disposing its managers.
The application must implement the additive `IPubSubConfigurationLifecycle`
contract; the standard `PubSubApplication` does. Custom applications without a
pre-retirement notification cannot safely host this facet and are rejected.

Options supply stable reserved identifier prefixes, local surfacing policy, explicit
schema associations, credential-configuration authorization and remote Publisher
bindings. Endpoints declaring authorization alternatives require `AuthorizeBinding`;
catalog metadata is never interpreted as credentials.
Alternatives are read from `protocoloptions.authorization`, and binding references
are withheld unless the configured policy approves them.

## Native snapshots and lifecycle

`Snapshots` returns deep copies of the generated model's
`PubSubBindingSnapshotDataType`: Source, Endpoint, Message, Schema, Configuration,
PublishedDataSets, DataSetMetaData, CompleteConfiguration and Issues. Endpoint Objects
also expose the optional read-only `PubSubBindings` property. Missing metadata or
configuration-version disagreements produce incomplete snapshots and no corresponding
binding Reference. Schema associations must describe exactly the same DataSet metadata.

`PubSubNodeManager.ConfigurationView` provides a coherent native configuration and
actual WriterGroup, writer and reader Objects. Its asynchronous observer seam
invalidates associations before old runtime components or Objects retire, and
activates associations after registering the new Objects. Registry activation uses
the same ordering. The facet introduces no synchronous wait on asynchronous work.

`RegistryNativeProvider` reserves collection identifier prefixes. Provider commits
use the ordinary semantic validation, exact no-op comparison, epoch exhaustion,
CAS and activation path. The shared mutation engine preserves server-owned fields;
the provider does not author epochs. Direct, nested and enclosing ordinary writes,
compatibility patches and Delete cannot change a reserved entry. A configuration
fingerprint advances a surfaced epoch even for encoding-only configuration changes.

## Authenticated retained discovery

`RemotePubSubPublisherBinding` names the authenticated identity, PublisherId,
authorized Topic prefix/connection Topic, independently authorized broker locator
and explicit Publisher MQTT version. `ObserveRemoteAsync` accepts decoded native
observations only after the host authenticates the sender. Metadata identifies its
Publisher, WriterGroup and DataSetWriter. Missing facts do not become invented
Endpoint metadata.

Connection and metadata Topics have independent retained lifetimes. Timestamp and
MessageId watermarks reject old/equal-time and duplicate messages, including replay
after a retained clear or expiry. The bounded replay budget fails closed when full.
A service-owned periodic sweep removes MQTT 5 expired observations;
`ExpireRemoteAsync` supports deterministic explicit sweeps. Neither API discovers,
subscribes to or connects to a broker. Remote sources never acquire invented native
identities or References; a genuinely hosted local component can still correspond
to a surfaced Message Definition.
If removal publication fails, the sweep retries reconciliation without discarding
the retained tombstone or replay watermark.

The adapter owns wire authentication and native decoding. Transport engines and
complete protocol encoders are out of scope. Schema registration remains separate:
call `RefreshAsync` when existing schema associations change. Restrict native
configuration snapshot access if ConnectionProperties contain sensitive configuration.

## Validation

`tests\Opc.Ua.EndpointRegistry.PubSub.Tests` contains the NUnit suite and a committed
oracle with pinned source hashes, independent Python outcomes and Python-generated
Core binaries. Regenerate it with `Tools\harvest_oracle.py --spec-root <read-only-root>
--output Vectors\correspondence.json`, with bytecode writes disabled. Runtime tests
use disabled in-process configuration, secured TCP sessions and paused retirement
races. No external broker is required.
