<!--
Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.

OPC Foundation MIT License 1.00

The complete license agreement can be found here:
http://opcfoundation.org/License/MIT/1.00/
-->

# Visual Inspection Agent

`VisualInspectionAgent` is the external deterministic orchestrator for the
[VisualInspectionCell](../VisualInspectionCell) server. It uses typed OPC UA
clients for Vision, AI Model Management, ISA-95 Job Control V2, and Alarms &
Conditions; it does not call server internals.

The model path never owns the production verdict. The agent captures a fixture
image, measures characteristics, routes the measurements through the AI
companion deployment's `Invoke` method for provenance, and then applies the
recipe rule locally before touching job control.

```mermaid
flowchart TD
    Start["Start cycle"] --> Capture["Get fixture PNG from FixtureImages"]
    Capture --> Measure["Measure BoreDiameter, SlotWidth, EdgeOffset"]
    Measure --> Invoke["Call deployment Invoke for provenance"]
    Invoke --> Judge["Apply deterministic recipe rule"]
    Judge --> Decision{"Verdict"}
    Decision -->|"Ok"| CompleteOk["Complete and close inspection job"]
    CompleteOk --> NextInspection["StoreAndStart VIS-INSP-BRACKET-001"]
    Decision -->|"NotOk"| CompleteBad["Complete and close inspection job"]
    CompleteBad --> Rework["StoreAndStart VIS-REWORK-REJECT-001"]
    Decision -->|"NotDecidable"| Hold["Hold for operator dialog"]
    Hold --> Correction["Submit ground-truth correction"]
```

## Running

Start the cell first:

```powershell
dotnet run --project samples\Vision\VisualInspectionCell\VisualInspectionCell.csproj -- --insecure
```

Then run the agent:

```powershell
dotnet run --project samples\Vision\VisualInspectionAgent\VisualInspectionAgent.csproj -- --server opc.tcp://localhost:62865/VisualInspectionCell --insecure --mode scripted --cycles 3
```

## Options

| Option | Meaning |
|---|---|
| `--server <url>` | Server endpoint. Default `opc.tcp://localhost:62865/VisualInspectionCell`. |
| `--insecure` | Demo-only certificate convenience. |
| `--mode scripted\|live-ai\|human` | Selects the operator/model mode. Default `scripted`. |
| `--cycles <n>` | Number of fixture cycles. Default `3`, minimum `1`. |
| `--operator-timeout <seconds>` | Bounded human wait. Default `10`, minimum `1`. |
| `--ai-endpoint <uri>` | Required for `live-ai`. |

## Modes

- `scripted` — the unattended path. The analyser and operator disposition are
  deterministic, and `--cycles N` makes the run finite.
- `live-ai` — requires `--ai-endpoint`. If no endpoint is configured, the agent
  exits before creating any ISA-95 job. There is no silent fallback to the
  simulated analyser, because a silently degraded sample appears to work while
  demonstrating nothing.
- `human` — waits for a real dialog subscriber for a bounded time. A timeout
  stops or holds; it never auto-approves and never blocks forever.

## Job policy

Quality and job execution state are separate facts. A defective part does not
mean the inspection job failed.

| Verdict | Inspection job | Next job |
|---|---|---|
| `Ok` | complete, close | schedule next inspection |
| `NotOk` | complete, close | schedule rework/reject order |
| `NotDecidable` | hold | none until the operator answers |

Scheduling chooses from the fixed catalogue exposed by the cell and calls ISA-95
V2 `StoreAndStart`. The agent never invents a job-order payload outside that
catalogue.

## Operator feedback and learning

For `NotDecidable`, the agent responds to `OperatorDispositionDialog` and sends
Vision feedback with `VisionFeedbackPurposeEnum.GroundTruthLabel`. An accepted
positive correction carries the measured characteristics. An accepted negative
example uses the `RetractAll` path. The same correction is submitted twice in
the current orchestrator to demonstrate that the cell counts a learning sample
idempotently per stable sample id.

## Related docs

- [Visual Inspection developer guide](../../../docs/VisualInspection.md)
- [VisualInspectionCell](../VisualInspectionCell)
- [Vision developer guide](../../../docs/Vision.md)
- [AI Model Management developer guide](../../../docs/AiModelManagement.md)
- [ISA-95 developer guide](../../../docs/ISA95.md)
