<!--
Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.

OPC Foundation MIT License 1.00

The complete license agreement can be found here:
http://opcfoundation.org/License/MIT/1.00/
-->

# Vision samples

The Vision samples show OPC UA Vision in realistic production cells rather
than as isolated image-processing calls.

| Sample | Role |
|---|---|
| [VisualInspectionCell](VisualInspectionCell) | Server hosting Vision, AI Model Management, ISA-95 Job Control V2, and Alarms & Conditions for a machined-bracket inspection cell. |
| [VisualInspectionAgent](VisualInspectionAgent) | External deterministic orchestrator that connects with typed clients, measures fixture images, applies the recipe, routes jobs, and records operator ground truth. |

The pair demonstrates a safety pattern that is deliberately stronger than
"ask a model whether the part is good": the model path can produce measured
characteristics and confidence, but deterministic recipe code owns the quality
verdict. A photographed image therefore cannot become a free-form instruction to
job control.

```mermaid
graph TD
    Agent["VisualInspectionAgent<br/>typed OPC UA clients"]
    Cell["VisualInspectionCell<br/>OPC UA server"]
    Vision["Vision<br/>sensor, clip endpoint, pipeline, results"]
    AI["AI Model Management<br/>deployment, Invoke, learning job"]
    ISA95["ISA-95 Job Control V2<br/>inspection and rework orders"]
    Alarms["Alarms & Conditions<br/>operator dialog"]

    Agent -->|"capture and submit results"| Vision
    Agent -->|"Invoke for provenance"| AI
    Agent -->|"StoreAndStart / transitions"| ISA95
    Agent -->|"respond to dialog"| Alarms
    Cell --> Vision
    Cell --> AI
    Cell --> ISA95
    Cell --> Alarms
```

## Running the pair

Prerequisites: .NET 10 SDK.

Start the server:

```powershell
dotnet run --project samples\Vision\VisualInspectionCell\VisualInspectionCell.csproj -- --insecure
```

Then run the orchestrator in the unattended deterministic mode:

```powershell
dotnet run --project samples\Vision\VisualInspectionAgent\VisualInspectionAgent.csproj -- --server opc.tcp://localhost:62865/VisualInspectionCell --insecure --mode scripted --cycles 3
```

The server publishes static PNG fixture images. The agent retrieves a fixture,
measures the bracket geometry, calls the AI deployment's `Invoke` path so model
provenance and usage are visible in the address space, applies the recipe, and
routes the ISA-95 order according to the deterministic verdict.

Use `--mode live-ai --ai-endpoint <uri>` only when a real model endpoint is
configured. `live-ai` fails before creating any job when no endpoint is named;
it never silently falls back to the deterministic analyser, because a sample
that quietly degrades appears to work while demonstrating nothing.

## See also

- [Visual Inspection developer guide](../../docs/VisualInspection.md) — the
  design, safety boundary, verdict rule, escalation, and learning feedback path.
- [Vision developer guide](../../docs/Vision.md) — the Vision companion and its
  §9 feedback / learning semantics.
- [AI Model Management developer guide](../../docs/AiModelManagement.md) — the
  deployment `Invoke` and learning-job ownership used by the cell.
- [ISA-95 developer guide](../../docs/ISA95.md) — Job Control V2 clients,
  providers, and job-state semantics.
- [Alarms and Conditions](../../docs/AlarmsAndConditions.md) — the Part 9
  dialog condition model used for human disposition.
