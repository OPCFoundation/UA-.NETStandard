<!--
Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.

OPC Foundation MIT License 1.00

The complete license agreement can be found here:
http://opcfoundation.org/License/MIT/1.00/
-->

# Visual Inspection sample guide

The visual-inspection sample is a small manufacturing cell: a camera photographs
a machined bracket, a model path records measured characteristics and
confidence, deterministic code judges those measurements against a recipe, the
verdict drives an ISA-95 job order, and anything the machine cannot decide is
escalated to a human operator. The operator's answer is captured as ground truth
and counted as an AI learning sample.

The sample lives in two projects:

| Project | Role |
|---|---|
| [`samples/Vision/VisualInspectionCell`](../samples/Vision/VisualInspectionCell) | Server hosting Vision, AI Model Management, ISA-95 Job Control V2, and Alarms & Conditions in one address space. |
| [`samples/Vision/VisualInspectionAgent`](../samples/Vision/VisualInspectionAgent) | External orchestrator using typed clients to drive capture, inference provenance, verdicts, jobs, dialog handling, and feedback. |

```mermaid
graph TD
    Agent["VisualInspectionAgent<br/>deterministic orchestrator"]
    Cell["VisualInspectionCell<br/>OPC UA server"]
    Vision["Vision<br/>camera, media, pipeline, results, feedback"]
    AI["AI Model Management<br/>deployment Invoke, model provenance, learning job"]
    ISA95["ISA-95 Job Control V2<br/>fixed order catalogue"]
    AC["Alarms & Conditions<br/>operator dialog"]
    Recipe["Recipe rule<br/>tolerance intervals"]

    Agent -->|"typed Vision client"| Vision
    Agent -->|"typed AI client"| AI
    Agent -->|"typed ISA-95 client"| ISA95
    Agent -->|"AlarmClient response"| AC
    Cell --> Vision
    Cell --> AI
    Cell --> ISA95
    Cell --> AC
    Agent --> Recipe
```

## The model never decides

The central safety property is that model output is evidence, not authority. A
model may be involved in producing measured characteristics and a confidence,
but deterministic code applies the recipe tolerances and computes the verdict.
That matters because a plant that let a language model schedule production from
what it thought it saw in a photograph would have an image-shaped path straight
into job control. In this sample, image content can influence the measured
values, but it cannot become a free-form job-control instruction.

The sample also routes inference through the AI companion's deployment
`Invoke` method instead of letting the agent call a model privately in its own
process. That keeps the deployment node, model version, and usage accounting in
the address space and in the provenance trail. A private model call would make
the most important part of the loop invisible.

## Address-space composition

`VisualInspectionCell` composes four companion areas in one server process:

- Vision publishes `BracketFixtureCamera`, `FixtureImages`,
  `BracketInspectionPipeline`, inspection results, and feedback.
- AI Model Management publishes the primary deployment
  `visual-inspection-primary`, its model metadata, and the learning job whose
  `SamplesCollected` value is incremented by host code.
- ISA-95 Job Control V2 publishes the fixed job-order catalogue and the V2
  Methods the agent calls.
- Alarms & Conditions publishes the `OperatorDispositionDialog` condition.

`BracketInspectionPipeline` points at the AI deployment through `Deployment` and
at the learning job through `LearningJob`. The Vision companion deliberately
uses `NodeId` values for those bindings; it does not take a compile-time
dependency on the AI companion. This sample is the host that binds both models
and can therefore satisfy the learning-job counter semantics that standalone
Vision cannot.

## Recipe and verdict rule

The inspected part is a machined bracket with three dimensional characteristics
in millimetres:

| Characteristic | Nominal | Tolerance |
|---|---:|---:|
| `BoreDiameter` | 12.00 | ± 0.20 |
| `SlotWidth` | 8.00 | ± 0.15 |
| `EdgeOffset` | 20.00 | ± 0.25 |

For each characteristic, the rule builds an interval from the measured value and
physical uncertainty:

```text
measurement interval = actual ± uncertainty
tolerance interval   = [nominal - lowerTol, nominal + upperTol]
```

Then it classifies the characteristic:

- wholly inside the tolerance interval -> `Ok`
- wholly outside the tolerance interval -> `NotOk`
- straddling either tolerance limit -> `NotDecidable`

The part verdict is the worst characteristic verdict, with `NotOk` worse than
`NotDecidable`, and `NotDecidable` worse than `Ok`.

```mermaid
flowchart TD
    Measurement["Measured characteristic<br/>actual and uncertainty"] --> Interval["Build actual +/- uncertainty"]
    Interval --> Compare{"Compare with tolerance interval"}
    Compare -->|"wholly inside"| Ok["Ok"]
    Compare -->|"wholly outside"| NotOk["NotOk"]
    Compare -->|"straddles a limit"| NotDecidable["NotDecidable"]
    Ok --> Worst["Part takes worst characteristic verdict"]
    NotOk --> Worst
    NotDecidable --> Worst
```

## Why uncertainty is physical

The fixture images are 800 x 600 pixels at 10 px/mm. A feature edge can only land
on a pixel boundary, so a dimensional measurement carries one-pixel quantisation
uncertainty: 0.10 mm. That is the camera's pixel pitch. It is what makes
`VisionCharacteristicDataType.Uncertainty` meaningful, and it is why
`NotDecidable` arises naturally instead of being contrived.

The three fixtures exercise all branches:

| Fixture | Decisive characteristic | Interval | Verdict |
|---|---|---|---|
| `bracket-ok.png` | `BoreDiameter = 12.00` | `[11.90, 12.10]` is wholly inside `[11.80, 12.20]` | `Ok` |
| `bracket-not-ok.png` | `BoreDiameter = 12.60` | `[12.50, 12.70]` is wholly outside the bore tolerance | `NotOk` |
| `bracket-ambiguous.png` | `SlotWidth = 8.10` | `[8.00, 8.20]` straddles the 8.15 upper limit | `NotDecidable` |

The ambiguous fixture is intentionally mundane: the intended 8.15 mm slot is
81.5 pixels at 10 px/mm and cannot be drawn exactly. The raster image therefore
measures as 8.10 mm, and one pixel of uncertainty crosses the tolerance limit.
That is precisely the case the Vision `NotDecidable` value exists for.

## Inspection loop

The agent drives the process from outside the server. It discovers the Vision
pipeline, opens the media endpoint, follows the pipeline's `Deployment` to the
AI companion, discovers ISA-95 V2 endpoints, and finds the operator dialog.

```mermaid
flowchart TD
    Discover["Discover pipeline, media, deployment, jobs, dialog"] --> Capture["Get fixture PNG"]
    Capture --> Measure["Measure bracket geometry"]
    Measure --> Invoke["Call AI deployment Invoke"]
    Invoke --> Judge["Apply recipe rule"]
    Judge --> Submit["Submit inspection result to Vision Feedback"]
    Submit --> Verdict{"Verdict"}
    Verdict -->|"Ok"| CloseOk["Start, stop, and clear inspection job"]
    CloseOk --> Next["StoreAndStart inspection order"]
    Verdict -->|"NotOk"| CloseBad["Start, stop, and clear inspection job"]
    CloseBad --> Reject["StoreAndStart rework/reject order"]
    Verdict -->|"NotDecidable"| Hold["Hold for operator"]
```

The important separation is that quality outcome and job execution state are
separate facts. A defective part does not mean the inspection job failed.

| Verdict | Inspection job | Next job |
|---|---|---|
| `Ok` | complete, close | schedule next inspection |
| `NotOk` | complete, close | schedule rework/reject order |
| `NotDecidable` | hold | none until the operator answers |

Scheduling selects an order from a fixed allowlisted catalogue and calls V2
`StoreAndStart`. `InspectionJobControlProvider` accepts only
`VIS-INSP-BRACKET-001` and `VIS-REWORK-REJECT-001`; the agent never invents a
job payload.

## Escalation and ground truth

`NotDecidable` activates the human path. The design dispositions are
`AcceptAsOk`, `AcceptAsNotOk`, `Reinspect`, and `Stop`. The implementation maps
those dispositions onto the dialog response and a bounded timeout: it holds or
stops, but it does not auto-approve and does not block forever.

```mermaid
sequenceDiagram
    participant Agent as VisualInspectionAgent
    participant Vision as Vision Feedback
    participant Dialog as Operator Dialog
    participant Operator as Human Operator
    participant AI as AI Learning Job

    Agent->>Vision: SubmitInspectionResult NotDecidable
    Agent->>Dialog: Wait for disposition
    Dialog->>Operator: Request Accept, Reinspect, Reject, or Stop
    Operator-->>Dialog: Disposition
    Dialog-->>Agent: Response or timeout
    alt Accept as ground truth
        Agent->>Vision: SubmitCorrection GroundTruthLabel
        Vision->>AI: RecordLearningSampleAsync
        AI-->>Vision: Idempotent count result
    else Reinspect
        Agent->>Vision: No correction
        Agent->>Agent: Schedule inspection order
    else Stop or timeout
        Agent->>Agent: Hold or stop without approval
    end
```

The operator answer becomes a Vision §9 ground-truth correction. The cell's
feedback sink calls `AiNodeManager.RecordLearningSampleAsync` and uses a stable
sample id, so a retry does not count the same label twice. A negative example is
still a learning sample: it counts exactly once even when it carries no geometry.

This closes a limitation called out in the [Vision developer guide](Vision.md#limitations):
`SamplesCollected` belongs to the AI companion's `LearningJobType`, while Vision
only names the learning job by `NodeId`. A standalone Vision node manager cannot
increment a counter owned by another companion. A host binding Vision and AI
Model Management together can, and this cell is that host. See also the
[AI Model Management developer guide](AiModelManagement.md) for the deployment
and learning-job model.

## Modes

`VisualInspectionAgent` supports three modes:

- `scripted` — deterministic analyser, scripted operator policy, and a finite
  `--cycles N`. This is the unattended path.
- `live-ai` — a real model path. It requires `--ai-endpoint`; if no endpoint is
  configured, the agent exits before creating any job and never silently falls
  back to the simulated analyser.
- `human` — a real dialog subscriber path with a bounded
  `--operator-timeout`.

The no-silent-fallback rule is part of the sample's safety story. A sample that
quietly degrades can look green while proving neither model connectivity nor the
provenance path it claims to demonstrate.

## What is deliberately not implemented

The sample does not implement retraining or model promotion. It records learning
samples honestly and increments the AI learning-job count, but it does not fake
an MLOps workflow. A simulated retraining integration that appeared to work
would mislead readers about the one part of the specification a sample cannot
honestly demonstrate. The [AI Model Management sample](../samples/AI/README.md)
takes the same line.

## See also

- [Vision developer guide](Vision.md) — Vision sensors, media, pipelines,
  feedback, and the §9 limitation this sample closes by hosting AI as well.
- [AI Model Management developer guide](AiModelManagement.md) — deployment
  `Invoke`, model provenance, and learning jobs.
- [ISA-95 developer guide](ISA95.md) — Job Control V2 semantics and typed
  clients/providers.
- [Alarms and Conditions](AlarmsAndConditions.md) — `DialogConditionType` and
  client-side alarm response helpers.
- [Visual Inspection samples](../samples/Vision/README.md) — run commands and
  per-project READMEs.

