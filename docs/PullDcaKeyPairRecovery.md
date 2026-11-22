<!--
SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
SPDX-License-Identifier: MIT
-->

# Recover pull DCA key-pair requests

`PullDcaWorkflow` can resume an OPC 10000-12 new-key-pair request using a
protected checkpoint. Register the host's certificate installer and protected
store before selecting `PullDcaCertificateRequestMode.NewKeyPairRequest`.
For example, in the host's composition code:

```csharp
// protectedStore is the host's IProtectedPullDcaRecoveryStore implementation.
builder.Services.AddSingleton<IProtectedPullDcaRecoveryStore>(protectedStore);
gdsClient.AddPullDcaKeyPairRecovery("device-commissioning")
    .AddPullDcaWorkflow();
```

Use one workflow and one stable slot per device. The backend must enforce
exclusive writer ownership of the slot while the workflow is running, including
across processes accessing the same storage. It must protect stored entries,
temporary data, and backups against disclosure and tampering. Writes and
removals must be atomic and durable, and reads must never report an uncertain
or corrupt entry as absent. Neither the in-memory store nor the public signing
checkpoint store meets this contract.

- **Dispatching:** Start may have happened, but there is no durable request ID.
  The workflow stops. Reconcile the attempt with the registrar, stop the
  workflow, and use `ResetReconciledDispatchAsync` with the exact retained
  binding before retrying. Do not change the slot name to bypass the checkpoint.
- **Started:** The workflow resumes the original Finish. If a previously
  accepted Finish response was lost, OPC 10000-12 §7.9.5 uses a fresh key-pair
  request, not signing recovery.
- **Received:** The workflow uses the saved certificate and PFX without calling
  Finish again. It reads the current trust list and passes it to the installer.
  The adapter removes the protected checkpoint only after installation is
  verified for the matching certificate.

The OnboardingClient sample uses the same registration with
`--pull --keyPairRecoverySlot <slot>`. Supply the store in the sample host's
service collection before building it. Without a configured backend, the sample
fails before connecting; it does not persist private keys to its signing journal.
