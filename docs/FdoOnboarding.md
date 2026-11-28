<!-- SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL -->
<!-- SPDX-License-Identifier: MIT -->

# FDO onboarding for OPC UA GDS

The opt-in `OPCFoundation.NetStandard.Opc.Ua.Gds.Fdo` package connects the
FDO Owner and Device enrollment callbacks to OPC UA onboarding on .NET 10 and
.NET 11. The base GDS packages do not depend on FDO. The `Fdo` library owns
protocol authentication, protected exchange and credential replacement.

On the Owner, use `FdoRegistrarEnrollmentPolicy` as both the FDO enrollment
handler and the GDS external-authentication policy. Configure the approved
wire-order Device GUID and attestation certificate SHA-256 pin, the DCA
application and product URIs, and the GDS signing group. Register the same
policy instance with both paths. The ordinary external authentication provider
still verifies the configured CA trust; the application policy still approves
software requirements and the identity.

Supply `IFdoEnrollmentReceiptStore` for each product and application pair.
Every writer for that pair must share an atomic storage scope. The Owner
persists the pending receipt before returning the certificate, then commits
that exact issuance after authenticated `Done20`. Commit does not prove that
the Device received `DoneAck20`. An interrupted run must be reconciled before
new issuance; the module neither overwrites an existing receipt nor assumes
an unsuccessful callback left storage unchanged.

On the Device, construct `FdoDeviceEnrollmentHandler` per session with the
expected Device GUID and exact CSR. Supply a validation callback that checks
the issued leaf against enrollment trust and the locally retained key without
installing it, and an installation callback that durably persists the key
pair before activation. The adapter only hands the validated leaf to the
installation callback after the FDO Device authenticates `DoneAck20`. The host
must reconcile uncertain installation before starting a new session.

The adapters support initial client-key enrollment only. The
[loopback sample](../samples/Gds/FdoOnboarding/README.md) shows a local
deployment with demo PKI and single-writer receipt storage. Do not use the
sample's storage or approval defaults as a distributed deployment policy.
