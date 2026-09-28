# Production readiness checklist

The samples and [Getting started](GettingStarted.md) use shortcuts that are
suitable only on a development computer: temporary certificate stores,
automatic certificate acceptance, anonymous users, and short reconnect limits.
Review this checklist before you deploy a client or server. Each item links to
the guide that explains the setting. The checklist does not replace a security
review of your deployment.

## Contents

- [Application identity and certificates](#application-identity-and-certificates)
- [Endpoints and message security](#endpoints-and-message-security)
- [Users and permissions](#users-and-permissions)
- [Secrets and private keys](#secrets-and-private-keys)
- [Resource limits](#resource-limits)
- [Diagnostics and auditing](#diagnostics-and-auditing)
- [Clients](#clients)
- [Deployment and operation](#deployment-and-operation)

## Application identity and certificates

- [ ] Give each installation a unique `ApplicationUri` that matches its
  application certificate. See [Concepts: applications](Concepts.md#applications).
- [ ] Set `PkiRoot` to a persistent, access-controlled directory. The hosted
  client and server default to a folder in the system temporary directory. See
  [Application identity and certificate defaults](DependencyInjection.md#application-identity-and-certificate-defaults).
- [ ] Keep `AutoAcceptUntrustedCertificates` disabled, and provision the
  trusted and issuer certificates, or CA-issued certificates, before peers
  connect. See [Certificates](Certificates.md).
- [ ] Keep SHA-1 certificates rejected and an RSA key size of at least 2048
  bits; both are the defaults. See
  [Server security and resource controls](DependencyInjection.md#server-security-and-resource-controls).
- [ ] Plan certificate renewal and trust-list updates, for example with
  certificate-expiration alarms, push management, or a Global Discovery Server.
  See [Certificate manager](CertificateManager.md) and [GDS](GDS.md).

## Endpoints and message security

- [ ] Offer `SignAndEncrypt` endpoints and keep `IncludeUnsecurePolicyNone`
  set to `false`; both are the hosted-server defaults. See
  [Server security and resource controls](DependencyInjection.md#server-security-and-resource-controls).
- [ ] Enable `IncludeEccPolicies` only when every client supports the ECC
  policies. ECC SecureChannel policies require the .NET 8 or later assemblies.
  See [ECC profiles: known limitations](EccProfiles.md#known-limitations).
- [ ] Configure clients to select a `SignAndEncrypt` endpoint. See
  [Concepts: endpoints, secure channels, and sessions](Concepts.md#endpoints-secure-channels-and-sessions).

## Users and permissions

- [ ] Decide whether anonymous access is acceptable. A hosted server without
  `UserTokenPolicies` advertises an `Anonymous` policy. See
  [User token policies](DependencyInjection.md#user-token-policies).
- [ ] Register an authenticator for each user-token policy that the server
  advertises, for example with `AddDefaultIdentityAuthenticators`. A policy
  without an authenticator does not authenticate users. See
  [Identity providers](IdentityProviders.md).
- [ ] Map identities to roles and restrict node permissions. See
  [Role-based user management](RoleBasedUserManagement.md).
- [ ] Set session and channel ceilings for the expected load, and keep the
  failed-authentication lockout (`MaxFailedAuthenticationAttempts`, default 5).
  See [Server security and resource controls](DependencyInjection.md#server-security-and-resource-controls).

## Secrets and private keys

- [ ] Keep passwords, tokens, and private keys out of source code,
  configuration files in source control, and container images.
- [ ] Store credentials through an `ISecretStore` that is backed by a durable,
  access-controlled secret service; a hosted server registers one with
  `AddSecretStore`. See [Dependency injection: server feature](DependencyInjection.md#server-feature).
- [ ] Where policy requires it, keep private keys in a TPM, HSM, PKCS#11 token,
  or key service. See [Crypto providers](CryptoProvider.md).

## Resource limits

- [ ] Review the transport quotas (`MaxMessageSize`, `MaxArrayLength`,
  `MaxByteStringLength`) and the service `OperationLimits`. See
  [Operation limits](DependencyInjection.md#operation-limits).
- [ ] Tune the admission rate limits, which are enabled by default, with
  `ConfigureRateLimits`. See [Rate limiting](RateLimiting.md).
- [ ] Review the resource-isolation profile; `Balanced` is the default. See
  [Server resource isolation](ResourceIsolation.md).
- [ ] Size the server for the expected sessions, subscriptions, and monitored
  items. See [Server scalability](ServerScalability.md).

## Diagnostics and auditing

- [ ] Send logs, metrics, and traces to your monitoring system through the
  host's logging and `ITelemetryContext`. See [Diagnostics](Diagnostics.md).
- [ ] Enable audit events when you must record security-relevant actions; they
  are off by default. See [Enabling auditing](Diagnostics.md#enabling-auditing).
- [ ] Decide whether to keep the server diagnostics nodes (`DiagnosticsEnabled`,
  default `true`), which add per-session and per-subscription work. See
  [Enabling diagnostics](Diagnostics.md#enabling-diagnostics).
- [ ] Use packet capture and key logs only for troubleshooting. Key logs make
  captured traffic decryptable. See [Diagnostics: security model](Diagnostics.md#security-model).

## Clients

- [ ] Keep the default reconnect policy for long-running clients. The Getting
  started client limits retries only to report errors quickly. See
  [Reconnect semantics](Sessions.md#reconnect-semantics-on-managedsession).
- [ ] Trust server certificates explicitly instead of accepting them
  automatically. See [Certificates](Certificates.md).
- [ ] Store namespace URIs, not namespace indexes, and resolve indexes after
  each connection. See [Concepts: node identity and namespaces](Concepts.md#node-identity-and-namespaces).
- [ ] Pass cancellation tokens and dispose sessions and subscriptions. See
  [Concepts: how the SDK is organized](Concepts.md#how-the-sdk-is-organized).

## Deployment and operation

- [ ] Keep the clocks of clients and servers synchronized, for example with
  NTP or PTP. Certificate validity, timestamps, and redundancy depend on it.
  See [Kubernetes: time synchronization](Kubernetes.md#time-synchronization).
- [ ] In containers, mount the certificate stores on persistent storage and
  supply secrets at run time. See
  [Container reference server](ContainerReferenceServer.md) and
  [Kubernetes: secrets, certificates, and trust](Kubernetes.md#secrets-certificates-and-trust).
- [ ] Use a Global Discovery Server or provisioning mode to manage certificates
  across many installations. See [GDS](GDS.md) and
  [Provisioning mode](ProvisioningMode.md).
- [ ] Add redundancy only when availability requirements need it. See
  [High availability](HighAvailability.md).
