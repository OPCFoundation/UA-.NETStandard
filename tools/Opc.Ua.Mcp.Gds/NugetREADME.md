# OPC UA GDS MCP workflows

Embeddable, separately selectable `gds`, `certificates`, `credentials`, and
`onboarding` profiles. Tools reuse the registered named UA session and generated
Part 12/Part 21 proxies; they never connect another GDS client behind the caller.

Register with `AddOpcUaMcpGds()` and `WithOpcUaGdsTools(profile)` or the composed
`McpToolProfileSet` overload. Registration does not grant authority. Configure
the corresponding `McpExecutionOptions` capability, exact caller grants and
allowed session names. Every domain invocation requires `SignAndEncrypt`.
Disabled capabilities hide the corresponding tools, including under `full`.
Stateful GDS workflows require a stack `ManagedSession`: each invocation uses
`SessionRequestContext` to bind dispatch to the original authentication and
transport generation. Retaining an `InnerSession` object is not sufficient
because recovery can recreate it in place. A generation change refuses later
dispatches, including file cleanup, rather than sending an old handle to a new
session. No borrowed context disposes the host's session or channel.

## Protected references

Use `AddOpcUaMcpSecrets(registry, aliases, issuanceStore)` to explicitly select
the host's writable `ISecretStore`. Register it before `AddOpcUaMcpGds`.
The gateway borrows stores; it never disposes them. Without an issuance store,
request/finish operations that can issue credentials fail closed. No implicit
in-memory credential store is created.

Inputs use `alias:name` or owner/resource-bound `issued:...` references, not
store paths or plaintext passwords, tokens, private keys or encoded tickets.
The inspection tools report the exact resource binding for root-bound aliases.
KeyCredential references use the service's actual `ResourceUri`; authorization
tokens use the resource accepted by the two-phase request.
Certificate private-key references use the application's URI binding.
Authorization proof/requestor-data aliases and refresh-token aliases bind to
the inspected `issuerResource`. Issued refresh references additionally retain
their issuer, so a token cannot be forwarded to another issuer sharing its
audience.

Token finish and refresh reserve both reference slots before remote dispatch.
A remote success followed by failed local persistence is an explicit partial
outcome, not a rollback or permission to retry issuance. An uncertain owned store
key is retained behind a cleanup reference until removal succeeds. Token expiry
comes from the server. `referenceExpiresAt` on other references is a local use
ceiling, not a claim about remote credential validity.

Hosts should call `PurgeExpiredAsync` from their existing maintenance loop and
dispose the gateway asynchronously at shutdown. Runtime consumers use
`GetDependency(reference, resource, capability)` to bind the returned
`Invalidated` task **and** `Reference.ExpiresAt` to their existing runtime
lifetime. They must stop on either signal/deadline and detach their own wait on
close. Resolve or revalidate before dispatch; material must not be cached past
revocation/expiry. The gateway owns no background timer or host store.
Host aliases intentionally have no inferred expiry or revocation event: the
store/registry interfaces do not supply one. Persistent runtime dependencies
must use issued references or a separately defined host lifecycle contract.

## Explicit remote effects

Directory queries preserve record ids, next-record ids and reset epochs.
Certificate requests, credential requests, collection, cancellation and
revocation are separate tools. The modern authorization flow is
`credential_start_token_request` then `credential_finish_token_request`;
`credential_refresh_token` is explicit. The obsolete one-shot token API is not
exposed. A token request can only be finished by its original caller on the same
root and session generation.

Push changes are caller-owned in addition to the server's Session ownership.
`certificate_apply_changes` and `certificate_cancel_changes` are always
explicit. No tool approves requests, polls indefinitely, elevates credentials,
reconnects, or chains independent business operations. After a transport failure
during a mutation the remote outcome may be unknown; inspect before retrying.

Public certificate, CSR and trust-list artifacts pass through
`McpFileTransfers`, with bounded memory serialization and root-confined async
publication. Private material never enters TransferRoot. Certificates are
validated as public DER; exported certificate bundles contain only public DER
encoded as base64. Trust lists use the standard binary `TrustListDataType`.
`certificate_finish_request` optionally writes `certificatePath` as leaf DER,
which can be supplied to a later explicit push or revocation tool without
extracting data from the chain bundle.

See the repository's [MCP guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/McpServer.md)
and [GDS guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/GDS.md).
