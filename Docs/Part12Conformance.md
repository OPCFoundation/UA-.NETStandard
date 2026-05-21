# OPC 10000-12 Part 12 Conformance Matrix

Status key: ✅ Implemented | ⚠️ Partial | ❌ Not implemented | N/A Not applicable

## GDS Directory (§6.5)

| Section | Feature | Status | Source |
|---------|---------|--------|--------|
| §6.5.3 | DirectoryType | ✅ | `ApplicationsNodeManager.cs` |
| §6.5.4 | FindApplications | ✅ | `OnFindApplications` |
| §6.5.5 | ApplicationRecordDataType / rcp+ rules | ✅ | `ApplicationsDatabaseBase.ValidateApplication` |
| §6.5.6 | RegisterApplication | ✅ | `OnRegisterApplication` + `DiscoveryAdminOrAppAdmin` |
| §6.5.7 | UpdateApplication | ✅ | `OnUpdateApplication` + `DiscoveryAdminOrSelfAdminOrAppAdmin` |
| §6.5.8 | UnregisterApplication | ✅ | `OnUnregisterApplicationAsync` + cert revocation |
| §6.5.9 | GetApplication | ✅ | `OnGetApplication` |
| §6.5.10 | QueryApplications | ✅ | `OnQueryApplications` |
| §6.5.11 | QueryServers | ✅ | `OnQueryServers` |
| §6.5.12 | ApplicationRegistrationChangedAuditEvent | ✅ | `AuditEvents.ReportApplicationRegistrationChangedAuditEvent` |

## Certificate Management — Pull Model (§7.6)

| Section | Feature | Status | Source |
|---------|---------|--------|--------|
| §7.6.4 | StartNewKeyPairRequest | ✅ | `OnStartNewKeyPairRequest` + audit |
| §7.6.5 | StartSigningRequest | ✅ | `OnStartSigningRequestAsync` + audit |
| §7.6.6 | FinishRequest | ✅ | `OnFinishRequestAsync` + issuer chain |
| §7.6.7 | GetCertificateGroups | ✅ | `OnGetCertificateGroups` |
| §7.6.8 | GetTrustList | ✅ | `OnGetTrustList` |
| §7.6.9 | RevokeCertificate | ✅ | `OnRevokeCertificateAsync` + audit |
| §7.6.10 | GetCertificates | ✅ | `OnGetCertificates` |
| §7.6.11 | CheckRevocationStatus | ✅ | `OnCheckRevocationStatusAsync` + ValidityTime |
| §7.6.12 | GetCertificateStatus | ✅ | `OnGetCertificateStatus` |

## Roles and Privileges (§7.2)

| Feature | Status | Source |
|---------|--------|--------|
| DiscoveryAdmin | ✅ | `GdsRole.DiscoveryAdmin` |
| CertificateAuthorityAdmin | ✅ | `GdsRole.CertificateAuthorityAdmin` |
| RegistrationAuthorityAdmin | ✅ | `GdsRole.RegistrationAuthorityAdmin` |
| ApplicationSelfAdmin | ✅ | `GdsRole.ApplicationSelfAdmin` |
| ApplicationAdmin | ✅ | `GdsRole.ApplicationAdmin` + `AdministeredApplicationIds` |
| BadSecurityModeInsufficient | ✅ | `AuthorizationHelper.HasAuthenticatedSecureChannel` |

## Push Management — ServerConfiguration (§7.10)

| Section | Feature | Status | Source |
|---------|---------|--------|--------|
| §7.10.3 | UpdateCertificate | ✅ | `ConfigurationNodeManager.UpdateCertificateAsync` |
| §7.10.4 | CreateSigningRequest | ✅ | `ConfigurationNodeManager.CreateSigningRequestAsync` |
| §7.10.5 | ApplyChanges | ✅ | `ConfigurationNodeManager.ApplyChanges` |
| §7.10.6 | CreateSelfSignedCertificate | ✅ | `ConfigurationNodeManager.CreateSelfSignedCertificateAsync` |
| §7.10.7 | GetCertificates | ✅ | `ConfigurationNodeManager.GetCertificates` |
| §7.10.8 | GetRejectedList | ✅ | `ConfigurationNodeManager.GetRejectedList` |
| §7.10.10 | ConfirmUpdate | ⚠️ | `ConfigurationFileState` generated; `StubManagedApplicationsNodeManager` provides extension point |
| §7.10.16 | ApplicationConfigurationType / ManagedApplications | ⚠️ | `IManagedApplicationsNodeManager` interface + `StubManagedApplicationsNodeManager` stub |

## TrustList (§7.8)

| Feature | Status | Source |
|---------|--------|--------|
| TrustListType / OpenWithMasks | ✅ | Core `TrustList.cs` |
| CloseAndUpdate | ✅ | Core `TrustList.cs` + TrustListUpdatedAuditEvent |
| AddCertificate / RemoveCertificate | ✅ | Core `TrustList.cs` |
| LastUpdateTime | ✅ | Set during init + CloseAndUpdate |
| Writable / UserWritable | ✅ | Set to true for GDS groups |
| ActivityTimeout / DefaultValidationOptions | ✅ | Generated from model CSV |
| CertificateExpirationAlarm | ⚠️ | Property values populated at startup; active-state deferred |
| TrustListOutOfDateAlarm | ⚠️ | Property values populated at startup; active-state deferred |

## Audit Events

| Event Type | Status | Source |
|------------|--------|--------|
| ApplicationRegistrationChangedAuditEvent | ✅ | Register/Update/Unregister |
| CertificateRequestedAuditEvent | ✅ | StartNewKeyPair + StartSigning |
| CertificateDeliveredAuditEvent | ✅ | FinishRequest |
| CertificateRevokedAuditEvent | ✅ | RevokeCertificate |
| CertificateUpdateRequestedAuditEvent | ✅ | UpdateCertificate (push) |
| TrustListUpdatedAuditEvent | ✅ | Core TrustList.cs |
| KeyCredentialRequestedAuditEvent | ✅ | StartRequest |
| KeyCredentialDeliveredAuditEvent | ✅ | FinishRequest |
| KeyCredentialRevokedAuditEvent | ✅ | Revoke |
| AccessTokenIssuedAuditEvent | ✅ | RequestAccessToken |
| Secrets redacted from audit payloads | ✅ | RedactedPrivateKeyPassword / RedactedPrivateKey |

## KeyCredentialService (§8)

| Feature | Status | Source |
|---------|--------|--------|
| StartRequest | ✅ | `OnKeyCredentialStartRequest` |
| FinishRequest | ✅ | `OnKeyCredentialFinishRequest` |
| Revoke | ✅ | `OnKeyCredentialRevoke` |
| InMemoryKeyCredentialRequestStore | ✅ | `IKeyCredentialRequestStore.cs` |
| Client proxy | ✅ | `KeyCredentialServiceClient.cs` |

## AuthorizationService (§9)

| Feature | Status | Source |
|---------|--------|--------|
| GetServiceDescription | ✅ | `OnGetServiceDescription` |
| RequestAccessToken | ⚠️ | Stub returns Bad_NotSupported |
| StartRequestToken (RC) | ❌ | Model-only; no handler |
| FinishRequestToken (RC) | ❌ | Model-only; no handler |
| RefreshToken (RC) | ❌ | Model-only; no handler |
| SupportedRoles (RC) | ✅ | Model property exposed |
| Client proxy | ✅ | `AuthorizationServiceClient.cs` |

## LDS / LDS-ME (§4–5)

| Feature | Status | Source |
|---------|--------|--------|
| FindServers | ✅ | `LdsServer.FindServersAsync` |
| GetEndpoints | ✅ | `LdsServer.GetEndpointsAsync` |
| RegisterServer | ✅ | `LdsServer.RegisterServerAsync` |
| RegisterServer2 | ✅ | `LdsServer.RegisterServer2Async` |
| FindServersOnNetwork | ✅ | `LdsServer.FindServersOnNetworkAsync` |
| LDS-ME capability | ✅ | `ComputeServerCapabilities` |
| mDNS _opcua-tcp._tcp | ✅ | `MulticastDiscovery.OpcUaServiceType` |
| rcp+ reverse-connect | ✅ | `MulticastDiscovery.ReverseConnectScheme` |
| Annex C TXT keys | ✅ | path / caps / rc |
| Annex D identifiers | ✅ | LDS / LDS-ME |
