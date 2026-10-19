# OPC UA MCP Server

The OPC UA MCP Server exposes OPC UA Part 4 service calls as
[Model Context Protocol (MCP)](https://modelcontextprotocol.io) tools. AI
assistants such as Claude, GitHub Copilot, VS Code Copilot, and Cursor can
use these tools to connect to OPC UA servers and interact with industrial
automation systems through natural language.

This is the user-facing tool catalogue: what to call, what it does and how to
interpret the outcome. C# registration, request fields and JSON examples belong
to the owning package's `NugetREADME.md`, linked from each catalogue below.

## Contents

- [What It Does](#what-it-does)
- [Tool Profiles](#tool-profiles)
  - [MachineTool, WMTP, GMS and CranesHoists](#machinetool-wmtp-gms-and-craneshoists)
  - [MachineTool, WMTP, GMS and CranesHoists](#machinetool-wmtp-gms-and-craneshoists)
  - [Vision-guided Robotics](#vision-guided-robotics)
  - [Robot Intent tools](#robot-intent-tools)
  - [Perception tools](#perception-tools)
  - [Machine Vision tools](#machine-vision-tools)
  - [Industrial companion tools](#industrial-companion-tools)
  - [WoT and registry tools](#wot-and-registry-tools)
  - [Stack workflow tools](#stack-workflow-tools)
- [Resources](#resources)
  - [Multi-Session Support](#multi-session-support)
- [Installation](#installation)
  - [Option 1: Run without installing (.NET 10 SDK)](#option-1-run-without-installing-net-10-sdk)
  - [Option 2: Install as a .NET global tool](#option-2-install-as-a-net-global-tool)
  - [Option 3: Run from source](#option-3-run-from-source)
  - [Option 4: Install from local build](#option-4-install-from-local-build)
- [Configuration](#configuration)
  - [Certificate user identities](#certificate-user-identities)
  - [Typed method arguments and structured values](#typed-method-arguments-and-structured-values)
  - [Run on demand without a global installation](#run-on-demand-without-a-global-installation)
  - [Claude Desktop](#claude-desktop)
  - [VS Code / GitHub Copilot](#vs-code--github-copilot)
  - [Cursor](#cursor)
  - [HTTP Transport (for remote clients)](#http-transport-for-remote-clients)
  - [Companion file transfers](#companion-file-transfers)
  - [WoT document dependency policy](#wot-document-dependency-policy)
  - [Privileged and live execution policy](#privileged-and-live-execution-policy)
- [Usage](#usage)
  - [Companion workflows](#companion-workflows)
  - [Companion results, errors and bounds](#companion-results-errors-and-bounds)
  - [Typical Workflow](#typical-workflow)
  - [NodeId Formats](#nodeid-formats)
  - [Common Well-Known NodeIds](#common-well-known-nodeids)
  - [Error Handling](#error-handling)
- [PubSub Tools](#pubsub-tools)
- [Architecture](#architecture)
  - [Packages](#packages)
  - [Embedding the tools in your own MCP server](#embedding-the-tools-in-your-own-mcp-server)
  - [Embedding industrial companions](#embedding-industrial-companions)
  - [Embedding WoT and xRegistry](#embedding-wot-and-xregistry)
  - [Remaining tooling gaps](#remaining-tooling-gaps)
- [Security Notes](#security-notes)
- [Agent-Usability Quality Gate](#agent-usability-quality-gate)
- [Requirements](#requirements)

## What It Does

The MCP server wraps the OPC UA .NET Standard client library, translating between JSON-based MCP tool calls and OPC UA binary protocol operations. A [tool profile](#tool-profiles) selects the catalog at startup. The default `full` profile registers all modules, but host-disabled privileged and live tools remain hidden. A narrower profile exposes only the subset relevant to its workflow. The table below describes the general service surface; domain and higher-level workflows have their own profiles.

| Service Set | Tools | Description |
|---|---|---|
| **Connection** | `GetEndpoints`, `Connect`, `ConnectCertificate`, `Disconnect`, `GetConnectionStatus` | Endpoint discovery and session lifecycle management |
| **Attribute** | `Read`, `Write`, `HistoryRead`, `HistoryUpdate` | Read/write node attributes and historical data |
| **View** | `Browse`, `BrowseNext`, `TranslateBrowsePaths`, `RegisterNodes`, `UnregisterNodes`, `QueryFirst`, `QueryNext` | Navigate and query the address space |
| **Node Management** | `AddNodes`, `AddReferences`, `DeleteNodes`, `DeleteReferences` | Modify the address space |
| **Method** | `Call` | Invoke OPC UA methods |
| **Subscription** | `CreateSubscription`, `ModifySubscription`, `SetPublishingMode`, `Publish`, `Republish`, `DeleteSubscriptions`, `TransferSubscriptions` | Manage notification subscriptions |
| **MonitoredItem** | `CreateMonitoredItems`, `ModifyMonitoredItems`, `SetMonitoringMode`, `SetTriggering`, `DeleteMonitoredItems` | Monitor data changes and events |
| **Discovery** | `FindServers`, `FindServersOnNetwork`, `RegisterServer`, `RegisterServer2` | Discover servers and register |
| **PKI Management** | `ListCertificates`, `TrustCertificate`, `RemoveCertificate`, `GetPkiStorePaths` | Manage certificate trust lists |
| **Configuration** | `GetConfiguration`, `SetTransportConfiguration`, `SetClientConfiguration`, `SetSecurityConfiguration`, `SetConfiguration` | View/modify in-memory client settings; `SetConfiguration` is the `full`-profile compatibility tool |
| **NodeSet Export** | `ExportNodeSet`, `ExportNodeSetPerNamespace` | Export address space to NodeSet2 XML |
| **Convenience** | `ReadValue`, `ReadValues`, `WriteValue`, `BrowseAll`, `CallMethod`, `ReadNode`, `Cancel` | Simplified high-level operations |
| **Packet Capture** | `list_interfaces`, `start_capture`, `stop_capture`, `list_captures`, `get_capture`, `capture_now`, `list_active_channels`, `dump_keys`, `decode_pcap_with_keys`, `summarize_service_calls`, `replay_pcap`, `stop_replay`, `list_replays` | OPC UA-aware packet capture, offline decode, service-call summaries, replay |

Values preserve UA types, quality and timestamps. Missing values are not
confused with Boolean false or numeric zero. See the
[Core API/JSON reference](../tools/Opc.Ua.Mcp.Core/NugetREADME.md#arguments-and-json-results).

### Packet capture tools

| Tool | What it does |
|---|---|
| `list_interfaces` | Lists capture-capable network interfaces without opening them. |
| `start_capture` | Starts capture from a network interface or in-process channel. |
| `stop_capture` | Stops capture and finalizes its artifacts. |
| `list_captures` | Lists active and retained captures. |
| `get_capture` | Returns a capture artifact or formatted analysis. |
| `capture_now` | Captures for a bounded interval and returns the output. |
| `list_active_channels` | Lists in-process secure channels and current token information. |
| `dump_keys` | Exports sensitive channel-key material for authorized diagnostics. |
| `decode_pcap_with_keys` | Decodes a capture offline using its key log. |
| `summarize_service_calls` | Summarizes service counts, latency and errors. |
| `replay_pcap` | Replays a conversation as a mock server or explicitly authorized mock client. |
| `stop_replay` | Stops an active replay. |
| `list_replays` | Lists active and recently completed replays. |

Key export, decode and replay are disabled until the host opts in. Treat
capture/key-log output as secret material, not ordinary logs. Caller-supplied
paths stay within the capture root; replay does not grant arbitrary outbound
access. For registration, selectors, paths and output formats, see the
[Diagnostics package reference](../tools/Opc.Ua.Mcp.Diagnostics/NugetREADME.md).

## Tool Profiles

The server selects its tool catalog through a **tool profile**, a set of tool classes registered at startup. Selecting a profile limits the tool descriptions sent to the client; `full` includes the complete catalog.

| Profile | Tool classes registered | Typical use case |
|---|---|---|
| `core` | Configuration, Connection, Convenience | Minimal footprint: connect, read/write values, adjust settings |
| `services` | Attribute, Configuration, Connection, Convenience, Discovery, Method, MonitoredItem, Node Management, Subscription, View | Full OPC UA Part 4 client workflows without PKI/PubSub/packet capture |
| `administration` | Configuration, Connection, NodeSet Export, PKI Management | Certificate trust management and NodeSet export |
| `pubsub` | PubSub runtime, discovery, action, and capture tools (plus PubSub decode when diagnostics tools are enabled) | Part 14 PubSub publish/subscribe, discovery, and capture workflows |
| `diagnostics` | Connection, Packet Capture (plus decode/replay when diagnostics tools are enabled) | OPC UA-aware packet capture, offline decode, and replay |
| `robotics` | Connection plus Robot Intent discovery, paged monitoring, control, mission and Vision-guided Pick tools | Commanding and monitoring a Robot Intent controller |
| `vision` | Connection, draft perception tools, and released OPC 40100 control/asset tools | [Perception](#perception-tools) and [Machine Vision](#machine-vision-tools) workflows |
| `machinetool` | Connection, MachineTool monitoring/production/jobs/files and bundled WMTP tools | OPC 40501-1 machines and independently hosted OPC 40505 peripherals |
| `gms` | Connection, typed systems/equipment/results and bounded observation | OPC 40210 monitoring and result workflows, without invented acquisition/control verbs |
| `craneshoists` | Connection, native crane inventory/snapshots/observation and authorized limit requests | OPC 40020-1 equipment monitoring, not physical motion control |
| `amb` | Connection plus asset discovery, reads, observations and explicit asset changes | Asset inventory, health, maintenance and documentation |
| `machinery` | Connection plus machine building blocks, process values, energy, job information and results | Machine monitoring and result retrieval; compose with `isa95` for job commands |
| `scales` | Connection plus weights, products, recipes and scale/PackML commands | Weighing workflows; compose with `di` for explicit product locking |
| `pumps` | Connection plus pump identity, values, ports, supervision and maintenance | Inspect pump state without inventing control methods |
| `di` | Connection plus device topology, identification, locks, transfers and software updates | Device configuration and explicitly requested updates |
| `isa95` | Connection plus common objects and V1/V2 job order/response tools | Job submission, control and monitoring |
| `positioning` | Connection plus spatial frames, global locations and coordinate conversion | Inspect and transform relative/global positions |
| `wot` | Connection plus legacy asset management and WoT-specific registry state, validation, refresh and events | Manage a WoT server without starting protocol executors in the MCP host |
| `xregistry` | Connection plus generic registry groups, resources, versions, labels, content and events | Reusable registry workflows, including compatible WoT specializations |
| `wot-documents` | Document inspection, validation, binding planning and NodeSet conversion | No UA connection required; external dependencies are disabled unless explicitly allowed by the host and request |
| `gds`, `certificates`, `credentials`, `onboarding` | Directory, pull/push certificates, reference-only credentials/tokens and device tickets | Explicit host grants and encrypted authorized sessions for administrative operations |
| `roles`, `users`, `files`, `alarms`, `history` | General client workflows independent of a companion model | Server permissions, protected-target guards, bounded transfers/observations and generation-bound history cursors |
| `schemas` | Type discovery/loading, XSD/BSD/UA JSON, PubSub and REST OpenAPI artifacts | Registered types can be inspected/generated without a UA connection; artifacts remain bounded and root-confined |
| `pubsub-runtime`, `pubsub-configuration`, `pubsub-bridge`, `pubsub-security` | Named transports, remote configuration, declarative adapters/bridges and protected SKS | Host capabilities and exact endpoint/topic/interface rules; native backend prerequisites remain explicit |
| `wot-runtime` | Explicit live HTTP(S), MQTT, Modbus TCP and OPC UA forms | Named leases, pinned documents/forms, credential references and operation/network allowlists |
| `full` (default) | All registered catalogues, subject to capability filters | Catalogue selection is not an execution grant; privileged and live capabilities default off |

`full` is the default for the current major version — `core` and the other bounded profiles are opt-in. Select a profile with:

- The `--profile` CLI option, e.g. `opcua-mcp --profile core`
- The `McpServer:ToolProfile` configuration value
- The `OPCUA_MCP_TOOL_PROFILE` environment variable

**Profiles compose.** Select several with a comma-separated `--profile`, such
as `vision,robotics`. The host includes shared connection tools only once.
The [BinPickingClient sample](../samples/Robotics/BinPickingClient) uses this
composition; see the [Vision package reference](../tools/Opc.Ua.Mcp.Vision/NugetREADME.md#composed-with-robotics)
for embedding it.

Tool counts, including those in `full`, change as tools are added or removed. Use the tables above or `tools/list` to inspect the catalog exposed by a running server.

The [industrial companion tools](#industrial-companion-tools) cover additional
families, cross-family handoffs, bounded observations and configured file
transfers. Catalogues are additive to `full`; bounded profiles retain their
documented tool sets. Profiles select a catalogue, not permissions.

### MachineTool, WMTP, GMS and CranesHoists

| Tools | Workflow |
| --- | --- |
| `machinetool_list_devices`, `machinetool_read_machine` | Subtype-aware machine/peripheral discovery, raw monitoring and actual capability declarations |
| `machinetool_read_equipment`, `machinetool_read_production`, `machinetool_read_prognoses` | Native tools/life, retained production and forecasts |
| `machinetool_observe_values`, `machinetool_observe_machine`, `machinetool_observe_events` | Private bounded data/event/retained-condition observations |
| `machinetool_query_jobs`, `machinetool_control_job` | Actual ISA95 V2 order/response services, including all eleven standard control verbs |
| `machinetool_list_files`, `machinetool_read_file`, `machinetool_change_file` | Bounded remote FileSystem or qualified WorkMasters workflows |
| `wmtp_read_peripheral`, `wmtp_count_records`, `wmtp_report_records` | Status/measurements and independent nonconsuming work/service queries |
| `wmtp_configure_measurement`, `wmtp_configure_peripheral` | Explicit trigger/delta, calibration and UTC/time-zone requests |
| `wmtp_delete_records`, `wmtp_abort_operation`, `wmtp_transfer` | Explicit record deletion, authorized peripheral abort and provider byte transfer |
| `gms_list_systems`, `gms_read_system`, `gms_read_results` | Native GMS inventory, equipment/production, metadata, characteristics and corrections |
| `gms_download_result`, `gms_observe`, `gms_acknowledge_results` | Bounded transfer/observation and separate consented result acknowledgment |

WMTP belongs to the MachineTool package/profile, not a separate profile.
`machinetool_list_devices` also works on WMTP-only endpoints, which need no machine
object. Measurement `nodeId` targets configuration; `valueNodeId` identifies
the actual AnalogSignal variable for `machinetool_observe_values`.

DataValue replies preserve quality, source/server time and numeric type.
Optional absence remains distinct from unavailable acquired data. Whole
inventory/report/byte limits reject excess data rather than silently paging it.
Queries use native JSON objects/arrays, not JSON embedded in strings.

Mutation tools require explicit confirmation, and still enforce actual
session/server/provider authorization. ISA95 ReturnStatus is a UInt64 bitmap
whose success bit is **1**, not UA Good=0. Accepted job requests do not prove
physical execution. File paths are provider-root-relative OPC UA paths, not
local host paths; writes do not silently create files or overwrite a different
destination.

File-change `options` groups `destination`, `contentBase64`, `maximumBytes`
and `recursive`; those are not separate top-level parameters. GMS result
queries similarly group `filterField`, `filterValue` and `orderBy` under a
native `query` object. Both reject unknown or duplicate fields before I/O.

WMTP count/report never consumes records. Count selects all/time; report
supports all/time/index/first/last; deletion selects all/time/index.
Trigger/delta configuration preserves the both-not-zero invariant and checked
numeric representation. Firmware/licence success means provider acceptance,
not installation or activation. Cancellation/timeouts never implicitly send
AbortOperation, retry uncertain writes, or compensate equipment effects.

GMS tools do not invent measurement-start, calibration-execution, GMS job-start
or correction-control methods. Result acknowledgment is distinct from condition
acknowledgment. Complete GMS Production-facet claims remain withheld for the
documented undefined upstream statistics CU.

Crane tools expose observed systems/axes/motors/protections/loads and only the
standard authorized speed-limit setpoint/enable requests. They do not actuate
crane motion or convert an accepted setpoint into reported active hardware
state. See [CranesHoists](CranesHoists.md) for its catalog.

Every parameter, default, enum, consent flag and bound is present in the live
MCP schema. API and JSON examples are in the
[MachineTool/WMTP](../tools/Opc.Ua.Mcp.MachineTool/NugetREADME.md),
[GMS](../tools/Opc.Ua.Mcp.GMS/NugetREADME.md) and
[CranesHoists](../tools/Opc.Ua.Mcp.CranesHoists/NugetREADME.md) package references.
The family support tables retain their conformance limits.

### Vision-guided Robotics

`robotics_vision_pick` closes the common perception-to-action path without
making an agent copy a result NodeId and several resource NodeIds between tool
calls. It runs one detection inference on the controller's named session, then
submits either one Pick or a two-step Pick/Place mission.

Detection selection is deterministic: exact DetectionId/ClassLabel filters and
the confidence threshold are applied first, then highest confidence wins with
ordinal DetectionId and original result order as tie-breakers. The result
contains Vision result/pipeline/sensor/model/frame provenance, the selected
pose, and the authoritative intent or mission handle. The helper does not
request authority, wait, retry, cancel, or reinterpret a server refusal.
See the [Robotics request contract](../tools/Opc.Ua.Mcp.Robotics/NugetREADME.md)
for structured selectors and JSON examples.

### Robot Intent tools

Read capabilities before commanding and request authority explicitly. A refusal
such as a safety limit, unsupported capability, wrong mode or another control
owner is a server decision, not a reason for an automatic retry. These tools
do not change operational mode, clear stops, implement safety-rated control or
bypass the robot's path planning and safety system.

| Tools | What they do |
| --- | --- |
| `robotics_list_controllers`, `robotics_read_controller` | Discover controllers and read supported intents, facets and lookup tables. |
| `robotics_read_state` | Read live controller state. |
| `robotics_list_operations`, `robotics_list_missions` | Return filtered, bounded work summaries. |
| `robotics_wait_operation`, `robotics_wait_mission` | Wait for the selected submitted operation, bounded by a timeout; timeout returns current state, not a fabricated failure. |
| `robotics_request_control`, `robotics_release_control` | Explicitly obtain or release command authority. |
| `robotics_cancel_intent`, `robotics_cancel_all` | Cancel the selected intent or all applicable work. |
| `robotics_pause`, `robotics_resume`, `robotics_retry` | Issue the corresponding explicit control command. |
| `robotics_submit_joint_move`, `robotics_submit_linear_move`, `robotics_submit_circular_move` | Submit a joint, linear or circular motion intent. |
| `robotics_submit_trajectory`, `robotics_submit_cartesian_path` | Submit a trajectory or Cartesian path intent. |
| `robotics_submit_force`, `robotics_submit_arc_weld`, `robotics_submit_spot_weld` | Submit a supported force or welding intent. |
| `robotics_submit_dispense`, `robotics_submit_fasten`, `robotics_submit_surface_finish` | Submit a supported process intent. |
| `robotics_submit_palletise`, `robotics_submit_grasp`, `robotics_submit_release`, `robotics_submit_pick`, `robotics_submit_place` | Submit the selected material-handling intent. |
| `robotics_submit_tool_change`, `robotics_submit_set_output`, `robotics_submit_call_program`, `robotics_submit_wait` | Submit an explicit tool, output, program or wait intent. |
| `robotics_submit_mission`, `robotics_update_mission`, `robotics_cancel_mission` | Submit a mission, update its horizon or cancel it. |
| `robotics_vision_pick` | Run detection and submit a deterministic Pick or Pick/Place, retaining provenance and submission handles. |

Resolution by published name is read-only; ambiguous names fail rather than
selecting a controller or target arbitrarily. No tool acquires control as a
side effect. See the [Robotics API/JSON reference](../tools/Opc.Ua.Mcp.Robotics/NugetREADME.md).

### Perception tools

| Tools | What they do |
| --- | --- |
| `vision_list_sensors`, `vision_list_pipelines`, `vision_list_frames`, `vision_list_calibrations` | Discover sensors, inference pipelines, coordinate frames and calibrations. |
| `vision_read_sensor`, `vision_read_pipeline`, `vision_read_extrinsic_calibration` | Inspect selected sensor, pipeline and calibration metadata. |
| `vision_read_detection_result`, `vision_read_inspection_result`, `vision_read_segmentation_result` | Retrieve the corresponding complete result. |
| `vision_get_frame` | Return an encoded image as MCP image content with its MIME type, or an actionable rendering failure. |
| `vision_get_frame_metadata` | Inspect the frame without transferring its pixels. |
| `vision_run_inference` | Run one inference and return its identity, kind, provenance and a bounded summary or handle. |
| `vision_start_continuous_inference`, `vision_stop_inference` | Explicitly start or stop continuous inference. |
| `vision_submit_detections`, `vision_submit_inspection_result` | Submit off-server perception results. |
| `vision_submit_correction`, `vision_submit_image_reference` | Provide correction feedback or a referenced image. |
| `vision_read_frame`, `vision_compose_pose`, `vision_compose_transform` | Inspect frames and compose a pose or transform between them. |

The tools use an existing named session. Request a full result only when needed;
inference already returns enough identity and provenance to follow up. See the
[Vision API/JSON reference](../tools/Opc.Ua.Mcp.Vision/NugetREADME.md).

### Machine Vision tools

| Tool | What it does |
| --- | --- |
| `vision_machine_list` | Discover released control systems or assets. |
| `vision_machine_read_state` | Read control/automatic states, with optional status and step details. |
| `vision_machine_command` | Issue an explicit control, simulation, synchronization or ConfirmAll command. |
| `vision_machine_read_asset` | Read bounded, namespace-qualified inventory and health values. |
| `vision_machine_recipes` | Manage recipes/products and their metadata handles. |
| `vision_machine_configurations` | Manage configurations and their metadata handles. |
| `vision_machine_results` | Read result metadata/components and release metadata handles. |
| `vision_machine_report_safety` | Report safety information without bypassing controller safety. |
| `vision_machine_file` | Transfer chunks and inspect or wait for file-commit completion. |
| `vision_machine_recipe_object` | Prepare a selected recipe or manage its product links. |
| `vision_machine_condition` | Explicitly acknowledge, confirm or comment on an exact condition occurrence. |
| `vision_machine_capabilities` | Distinguish model support from actual optional interfaces and executable methods. |
| `vision_machine_relationships` | Page same-entity, optical, data-transmission and interface references one hop at a time. |
| `vision_machine_observe` | Collect bounded events or refreshed conditions, then clean up its own subscription. |
| `vision_machine_upload_recipe` | Register/upload a complete small recipe and wait for commit, without preparing it. |
| `vision_machine_upload_configuration` | Register/upload a complete small configuration without activating it. |
| `vision_machine_download_result` | Download complete small result content and release file/metadata handles. |

Namespace presence does not prove optional functionality. Start with
capabilities, then inspect state or relationships. Observations never acknowledge
anything and leave other subscriptions open; a completed refresh does not
guarantee that no notifications were dropped. Cancellation and failed setup
remain errors.

Metadata pages cap at 100 entries; complete-file tools handle at most 1 MiB.
Use chunked file operations for larger content. Downloads return a complete
file or fail, never a truncated success. Upload, preparation, activation and
acquisition remain separate actions. Commit waits cap at 60 seconds and do not
retry a commit. See the [Vision API/JSON reference](../tools/Opc.Ua.Mcp.Vision/NugetREADME.md#machine-vision-json-contract)
for bounds, structured identifiers and result fields.

### Industrial companion tools

The MCP server exposes the stack's typed companion clients as task-oriented
tools. It does not create another OPC UA connection or reimplement the models:
each invocation resolves the current named session and calls the existing
client or source-generated ObjectType proxy.

Select a single industrial profile or compose the catalogs needed by a workflow:

```powershell
dnx OPCFoundation.NetStandard.Opc.Ua.Mcp -- --profile amb
dnx OPCFoundation.NetStandard.Opc.Ua.Mcp -- --profile machinery,isa95
dnx OPCFoundation.NetStandard.Opc.Ua.Mcp -- --profile scales,di
```

| Profile | Scope | Model guide |
| --- | --- | --- |
| `amb` | Asset discovery, identification, health, maintenance, documentation links, locations and relationships | [AMB](AMB.md) |
| `machinery` | Machines and building blocks, state, counters, process values, energy, job information and results | [Machinery](Machinery.md) |
| `scales` | Weights and units, products, recipes, scale-specific operations and PackML | [Scales](Scales.md) |
| `pumps` | Pump identity, configuration, operational measurements, supervision and maintenance | [Pumps](Pumps.md) |
| `di` | Device topology and identity, locking, parameter transfer and software update | [Device Integration](DeviceIntegration.md) |
| `isa95` | Common objects and V1/V2 job order control, responses and status | [ISA-95](ISA95.md) |
| `positioning` | Relative spatial frames, global positions, zones and coordinate conversion | [Positioning](Positioning.md) |

`full` includes every industrial tool, but also includes unrelated workflows
and consequential operations. Prefer a smaller profile or composition for
agent work. Profile selection is not an authorization check; use `tools/list`
to inspect the exact names and input schemas.

IA, PA-DIM, ECM and PackML are represented through the typed clients that use
them, not through duplicate catalogs. WoT Connectivity and xRegistry have
[separate profiles](#wot-and-registry-tools); general AI management and OpenUSD
remain outside this set. See [companion workflows](#companion-workflows)
for cross-family handoffs, [file transfers](#companion-file-transfers) for host
policy, and [embedding industrial companions](#embedding-industrial-companions)
for library registration.

#### AMB tools

| Tools | What they do |
| --- | --- |
| `amb_discover_assets`, `amb_find_assets` | Discover local assets or find them through aliases. |
| `amb_list_aliases`, `amb_read_category` | Browse aliases and inspect their category/version information. |
| `amb_read_asset` | Read identification, health, maintenance, documentation or a live snapshot. |
| `amb_browse_locations` | Browse an asset's location relationships. |
| `amb_observe` | Observe selected asset changes for a bounded interval. |
| `amb_write_asset_id` | Explicitly change an asset identifier. |
| `amb_acknowledge` | Acknowledge the exact condition/event occurrence selected by the caller. |
| `amb_add_documentation_link`, `amb_write_documentation_link`, `amb_remove_documentation_link` | Explicitly add, edit or remove an asset documentation link. |

Reads never acknowledge alarms or change identifiers. Remote references are
returned but not followed. Snapshots are sequential live reads, not atomic
plant snapshots; no unsupported AMB historical interface is invented.
[API/JSON reference](../tools/Opc.Ua.Mcp.AMB/NugetREADME.md).

#### Machinery tools

| Tools | What they do |
| --- | --- |
| `machinery_list_machines`, `machinery_list_components`, `machinery_list_building_blocks` | Discover actual machine, component and advertised building-block identities. |
| `machinery_read` | Read identification, health, counters or current item/operating state. |
| `machinery_list_lifetime_counters`, `machinery_list_equipment` | Inspect lifetime thresholds and equipment identity/life. |
| `machinery_list_process_values`, `machinery_read_process_value` | Discover process values and read measurements, setpoints, limits, units and quality. |
| `machinery_zero_point_adjustment` | Explicitly request calibration-changing zero-point adjustment. |
| `machinery_list_energy`, `machinery_read_meter` | Discover resources/meters/sub-meters and read typed measurements. |
| `machinery_list_jobs`, `machinery_read_job_parameters` | Read published orders/responses and their typed parameters. |
| `machinery_get_job_endpoints` | Return actual order-receiver and optional response-provider endpoints for ISA-95 follow-up. |
| `machinery_list_results`, `machinery_read_result`, `machinery_list_published_results` | Find results, read metadata/content and inspect published result variables. |
| `machinery_download_result` | Stream a bounded result into a new local artifact. |
| `machinery_observe` | Collect bounded state, result-ready, notification or zero-adjustment events. |

There are no item-state or operating-mode setters. Missing job roles are not
substituted, and Machinery has no response receiver. Result searches that reach
their limit must be narrowed; completed handles are released.
[API/JSON reference](../tools/Opc.Ua.Mcp.Machinery/NugetREADME.md).

#### Scales tools

| Tools | What they do |
| --- | --- |
| `scales_list`, `scales_read`, `scales_observe` | Discover scales, inspect weights/products/recipes/facets and observe bounded changes. |
| `scales_set_zero`, `scales_set_tare`, `scales_clear_tare`, `scales_set_preset_tare` | Explicitly adjust zero or tare state. |
| `scales_register_weight` | Register a weight using the selected scale's operation. |
| `scales_add_product`, `scales_remove_product` | Add or remove a product. |
| `scales_select_product`, `scales_deselect_product`, `scales_switch_product` | Explicitly change the selected product. |
| `scales_packml_command`, `scales_standard_command` | Invoke a supported, finite PackML or standard scale command. |
| `scales_set_reference_piece_weight`, `scales_set_number_of_reference_pieces`, `scales_start_reference` | Configure or start reference weighing. |
| `scales_set_draft_shields` | Set the supported draft-shield state. |
| `scales_weigh_vehicle` | Invoke a selected vehicle-weighing operation. |
| `scales_set_feeder_speed` | Explicitly set feeder speed. |
| `scales_add_recipe`, `scales_add_recipe_element`, `scales_process_recipe` | Create a recipe/element or explicitly process a recipe. |

An absent weight capability is distinct from an uninitialized measurement.
Creating a recipe does not start it. For a lock-protected product, compose
`scales,di` and use the discovered Lock child; no scale command takes or breaks
the lock implicitly.
[API/JSON reference](../tools/Opc.Ua.Mcp.Scales/NugetREADME.md).

#### Pumps tools

| Tool | What it does |
| --- | --- |
| `pumps_list_pumps` | Discover pumps, deduplicating those visible through multiple folders. |
| `pumps_read_nameplate` | Read identity and discover markings. |
| `pumps_list_markings` | Page markings from the returned markings folder, not the pump itself. |
| `pumps_read_group` | Read a selected measurement, supervision or maintenance group. |
| `pumps_list_ports` | Inspect pump ports. |
| `pumps_read_value_set` | Read typed values with their quality and units. |
| `pumps_read_snapshot` | Read a live pump snapshot. |
| `pumps_classify_type` | Classify the pump's actual type. |

These tools are read-only and do not invent start/stop controls.
[API/JSON reference](../tools/Opc.Ua.Mcp.Pumps/NugetREADME.md).

#### Device Integration tools

| Tools | What they do |
| --- | --- |
| `di_discover_devices`, `di_browse_topology` | Discover devices/subtypes and browse selected topology roots. |
| `di_read_device` | Read identification and discover optional lock, transfer and update objects. |
| `di_read_property`, `di_read_functional_group`, `di_read_lock` | Inspect exact properties, group children or lock state. |
| `di_write_property` | Explicitly change only AssetId or ComponentName. |
| `di_acquire_lock`, `di_renew_lock`, `di_release_lock`, `di_break_lock` | Invoke the requested lock operation, never a fallback to another one. |
| `di_transfer_to_device`, `di_transfer_from_device` | Start a parameter transfer; reading from the device also changes its offline parameter set. |
| `di_fetch_transfer_results` | Fetch one bounded result chunk, retaining continuation and diagnostics. |
| `di_read_software_update`, `di_observe_software_update` | Inspect package/state information or observe one update state machine. |
| `di_prepare_software_update`, `di_abort_prepare` | Explicitly prepare or abort preparation. |
| `di_upload_software_package` | Stream and commit a host-approved software artifact. |
| `di_install_software_package`, `di_install_files` | Explicitly install a package or already uploaded server files. |
| `di_uninstall_software`, `di_resume_installation`, `di_confirm_software_update` | Explicitly uninstall, resume or confirm an update. |

Use the discovered Lock child and retain the same named session while holding
it. Only zero is a successful device lock return status; a Good Call response
does not imply lock acquisition. Transfer initiation is not completion: fetch
results until the device reports completion.

Discover update support, prepare if required, upload, and then deliberately
choose installation/confirmation. These actions are never chained. Upload does
not invoke Installation, but DirectLoading devices may apply software at commit.
There is no reboot/power-cycle command. Observations last at most 30 seconds and
do not turn missing state machines into empty successful results.
[API/JSON reference](../tools/Opc.Ua.Mcp.Di/NugetREADME.md).

#### ISA-95 tools

| Tools | What they do |
| --- | --- |
| `isa95_list_common_objects`, `isa95_read_common_object` | Discover common-model objects/classes and read typed properties. |
| `isa95_discover_job_endpoints` | Discover actual V1/V2 order-receiver, response-provider and response-receiver roles. |
| `isa95_v1_receive_order` | Issue a selected V1 store/start/update/stop/cancel/clear command. |
| `isa95_v1_query_responses`, `isa95_v1_receive_response` | Query a V1 provider or submit a response to its receiver. |
| `isa95_v2_store`, `isa95_v2_store_and_start`, `isa95_v2_update` | Submit a complete typed V2 order for the requested action. |
| `isa95_v2_start`, `isa95_v2_stop`, `isa95_v2_pause`, `isa95_v2_resume` | Issue one explicit V2 lifecycle command. |
| `isa95_v2_abort`, `isa95_v2_revoke_start`, `isa95_v2_cancel`, `isa95_v2_clear` | Issue the selected abort/revoke/cancel/clear action. |
| `isa95_v2_query_responses`, `isa95_v2_receive_response` | Query a V2 response provider or submit a response to its receiver. |
| `isa95_v2_observe_status` | Observe typed job-status events for at most 30 seconds/500 events. |

Call the actual role discovered for the operation; never substitute an order
receiver for a missing response role. Only bitmap value `1` means model success.
Zero, mixed failure bits and unknown vendor bits remain refusals even when the
UA service call succeeds. Compose `machinery,isa95` for Machinery job handoffs.
[API/JSON reference](../tools/Opc.Ua.Mcp.ISA95/NugetREADME.md).

#### Positioning tools

| Tool | What it does |
| --- | --- |
| `positioning_rsl_list` | Discover spatial-object lists, objects and frames. |
| `positioning_rsl_read` | Read a position/frame or resolve its world-frame chain. |
| `positioning_rsl_observe` | Observe positions, frames or list-version changes. |
| `positioning_gpos_list` | Discover zones in GlobalLocations. |
| `positioning_gpos_read` | Read global positions/locations or inspect a zone transform and fit quality. |
| `positioning_gpos_transform` | Convert global/local coordinates using the selected zone's control points. |
| `positioning_gpos_observe` | Observe bounded global position/location changes. |

All tools are read-only. Use the actual server units and CRS; unsupported
transformers fail rather than silently substituting WGS84. Raw readings retain
optional fields and quality. World-frame results retain their base chain;
zone fits report dimension, rank, residuals and invertibility.
[API/JSON reference](../tools/Opc.Ua.Mcp.Positioning/NugetREADME.md).

### WoT and registry tools

The WoT and xRegistry modules contribute 35 domain tools. The `wot` profile
contains 16 server-facing tools, `xregistry` contains 14, and `wot-documents`
contains five document tools. Connection tools are shared exactly once when
session-scoped profiles are composed:

```powershell
opcua-mcp --profile wot
opcua-mcp --profile wot,xregistry
opcua-mcp --profile wot-documents
opcua-mcp --profile core,wot,xregistry,wot-documents
```

WoT legacy asset management and the registry are independent capabilities.
`wot_inspect_server` reports each separately and accepts explicitly discovered
roots when a server uses different placement. Missing optional capabilities
are not treated as supported empty results. Draft WoT registry and xRegistry
model identities are described in [WoT connectivity](WoTConnectivity.md) and
[xRegistry](XRegistry.md); package release-channel policy is independent of
model maturity.

| Workflow | Tools |
| --- | --- |
| WoT discovery and inspection | `wot_inspect_server`, `wot_list_assets`, `wot_inspect_asset`, `wot_read_resource_state` |
| Explicit endpoint operations | `wot_discover_endpoints`, `wot_test_connection` |
| Legacy asset lifecycle and TD files | `wot_create_asset`, `wot_create_asset_for_endpoint`, `wot_delete_asset`, `wot_upload_thing_description`, `wot_download_thing_description` |
| WoT registry operations | `wot_validate_resource`, `wot_set_enabled`, `wot_set_default_version`, `wot_refresh`, `wot_observe_events` |
| Generic registry reads | `xregistry_discover`, `xregistry_list`, `xregistry_read`, `xregistry_resolve_content`, `xregistry_observe_events` |
| Generic registry creation, content and labels | `xregistry_create_group`, `xregistry_create_resource`, `xregistry_upload_document`, `xregistry_download_document`, `xregistry_set_label`, `xregistry_remove_label` |
| Explicit deletion scopes | `xregistry_delete_group`, `xregistry_delete_resource`, `xregistry_delete_version` |
| Document processing | `wot_inspect_document`, `wot_validate_document`, `wot_plan_bindings`, `wot_to_nodeset`, `wot_from_nodeset` |

For asset data-plane operations, `wot_inspect_asset` returns property/action
NodeIds and action argument metadata. Compose with `core` or `services` and use
the existing read/write/call tools; the WoT module does not create a second
generic service catalogue.

Registry operations distinguish a logical Resource from a concrete Version.
Deleting a Resource removes its versions; deleting a Version removes only that
version. Tools check the hierarchy and concurrency token before dispatch. Ambiguous legacy
flat layouts remain inspectable but cannot be used to disguise a whole-resource
deletion as a single-version deletion.

Mutations require `SignAndEncrypt`. Where a method supports an expected epoch or
generation, callers supply it explicitly; zero requires an explicit
unconditional-operation opt-in. FileType writes have no atomic epoch argument,
so tools do not pretend that a metadata pre-read provides such protection.
Uploads target concrete versions rather than mutable default-version aliases.
No tool implicitly enables, refreshes, promotes, retries or acquires a lock.
Configured server-side automatic behavior is not overridden.

`wot_validate_resource` changes stored server state, unlike offline document
validation. `wot_refresh` preserves partial outcomes and never re-runs a mutation
to retrieve more result detail. Upload does not implicitly enable, promote or
refresh content.

See the [WoT API/JSON reference](../tools/Opc.Ua.Mcp.WotCon/NugetREADME.md)
and [xRegistry contract](../tools/Opc.Ua.Mcp.XRegistry/NugetREADME.md).

#### WoT document tools

| Tool | What it does |
| --- | --- |
| `wot_inspect_document` | Identify a supplied document and report bounded metadata without fetching dependencies. |
| `wot_validate_document` | Validate supported content; distinguish syntax-only results from authoring/conformance checks. |
| `wot_plan_bindings` | Explain supported forms, binder versions/maturity and diagnostics without executing them. |
| `wot_to_nodeset` | Convert a document or verified complete document set into a new NodeSet artifact. |
| `wot_from_nodeset` | Convert a NodeSet into an artifact containing every linked WoT document. |

These tools need no UA session. Consumer conversion preserves unknown vocabulary;
readable JSON is not automatically a convertible Binding projection or certified
W3C TD. Planning never activates HTTP, MQTT, Modbus or OPC UA interactions.

Linked sets round-trip together rather than silently dropping non-root members.
Artifacts are verified by digest, bounded and published without overwrite;
failures leave no partial output. Remote dependencies require both request
opt-in and host permission. For archive format, source selectors, limits and
examples, see the [WoT document contract](../tools/Opc.Ua.Mcp.WotCon/NugetREADME.md#document-input-and-artifact-contract).

### Stack workflow tools

The higher-level profiles reuse the stack's typed clients, file and history
helpers, schema providers and installed protocol executors. They are independent
of industrial companion models and remain separately embeddable:

| Package catalogue | Profiles | Scope |
| --- | --- | --- |
| [GDS](../tools/Opc.Ua.Mcp.Gds/NugetREADME.md) | `gds`, `certificates`, `credentials`, `onboarding` | Directory registration/query, certificate requests and push configuration, KeyCredential and token issuance/refresh, Part 21 tickets |
| [Workflows](../tools/Opc.Ua.Mcp.Workflows/NugetREADME.md) | `roles`, `users`, `files`, `alarms`, `history` | Explicit identity administration, bounded regular/temporary files, conditions and finite event observations, historical reads and mutations |
| [Schemas](../tools/Opc.Ua.Mcp.Schema/NugetREADME.md) | `schemas` | Bounded data-type discovery/loading, XSD/BSD/UA JSON, PubSub and REST OpenAPI generation |
| [PubSub](../tools/Opc.Ua.Mcp.PubSub/NugetREADME.md) | `pubsub-runtime`, `pubsub-configuration`, `pubsub-bridge`, `pubsub-security` | Named runtimes, standard remote configuration, declarative adapters/bridges and SKS |
| [Live WoT](../tools/Opc.Ua.Mcp.WotCon/NugetREADME.md#live-protocol-execution) | `wot-runtime` | Explicitly selected HTTP(S), MQTT, Modbus TCP and OPC UA forms; property/action/event operations |

For example, select historian and file workflows without registering certificate
or live protocol tools:

```powershell
opcua-mcp --profile files,history
opcua-mcp --profile gds,certificates
opcua-mcp --profile wot-documents,wot-runtime
```

These commands select catalogues, not permissions. Read the
[host execution policy](#privileged-and-live-execution-policy) before enabling
administration or live execution. Issue and refresh results contain opaque
secret references rather than credentials. Files and generated artifacts use
`TransferRoot`; histories retain private caller/session-bound cursors, not raw
continuation points that can be replayed against a different session.

Mutation results retain ordered server statuses and do not imply an atomic
batch, automatic retry, successful credential persistence, or completed remote
configuration activation.
The package references above document registration APIs, JSON request/response
contracts and examples.

## Resources

The host exposes connected sessions as MCP resources for inspection of status,
security and namespace tables. URI templates and protocol details are in the
[Core reference](../tools/Opc.Ua.Mcp.Core/NugetREADME.md#arguments-and-json-results).

### Multi-Session Support

Use `Connect` to create a named session and select that name on subsequent
operations. Omission is valid only when one session is connected. Connecting to
another server does not close existing sessions, and `Disconnect` affects only
the selected session.

## Installation

### Option 1: Run without installing (.NET 10 SDK)

Run the published tool directly from nuget.org. `dotnet tool exec` downloads
and caches the tool without adding a global tool installation:

```bash
# Run the latest release
dotnet tool exec OPCFoundation.NetStandard.Opc.Ua.Mcp

# Or stay on one release line, for example 2.0.x
dotnet tool exec "OPCFoundation.NetStandard.Opc.Ua.Mcp@2.0.*"

# Short form of dotnet tool exec
dnx OPCFoundation.NetStandard.Opc.Ua.Mcp
```

### Option 2: Install as a .NET global tool

```bash
dotnet tool install --global OPCFoundation.NetStandard.Opc.Ua.Mcp
```

After installation, the `opcua-mcp` command is available globally.

The tool includes every [tool profile](#tool-profiles), including composed
sets such as `vision,robotics` or `machinetool,gms,craneshoists`.
Robotics and CranesHoists embedding libraries remain preview-only.
Vision, MachineTool and GMS embedding libraries follow the normal
root release version; see [Packages](#packages).

### Option 3: Run from source

```bash
cd tools/Opc.Ua.Mcp
dotnet run -c Release
```

### Option 4: Install from local build

```bash
dotnet pack tools/Opc.Ua.Mcp/Opc.Ua.Mcp.csproj -c Release
dotnet tool install --global --prerelease --add-source tools/Opc.Ua.Mcp/bin/Release OPCFoundation.NetStandard.Opc.Ua.Mcp
```

If multiple prerelease versions are visible from configured sources, add
`--version <the-version-produced-by-dotnet-pack>` to select the local package
explicitly.

## Configuration

### Certificate user identities

The host can provision named X.509 user identities in
`McpServer:CertificateIdentities:<name>`. Each entry selects an existing certificate
store using `StoreType`, `StorePath`, and exactly one of `SubjectName` or `Thumbprint`.
For example, a local host can set:

```powershell
$env:McpServer__CertificateIdentities__observer__StoreType = 'Directory'
$env:McpServer__CertificateIdentities__observer__StorePath = 'D:\OpcUaIdentities\observer'
$env:McpServer__CertificateIdentities__observer__SubjectName = 'CN=PlantObserver'
```

Call `ConnectCertificate` with `endpointUrl` and `certificateIdentity: "observer"`.
It always uses Certificate authentication and accepts the same `securityMode`,
`securityPolicy`, and `name` selectors as `Connect`, but no automatic-trust switch.
`Connect` retains its original eight arguments for Anonymous and Username authentication;
`authType: "Certificate"` on that tool fails without an identity selector.
The public C# `ConnectAsync` overload accepting `certificateIdentity` remains available.
Tool arguments cannot supply certificate-store paths, private keys or key passwords.
An embedding host can inject `ICertificatePasswordProvider` for protected keys.
Missing, unknown or ambiguous references fail rather than falling back to another identity.
Application-certificate trust and server-side user authorization still apply independently;
`ConnectCertificate` never enables automatic trust. Only expose configured identities to trusted MCP callers.

### Typed method arguments and structured values

For structured data, explicitly load the selected DataType closure with
`schema_load_types` before reading or calling methods. Generic reads and calls retain
structured fields through UA codecs. `Call` and `CallMethod` accept
`inputArgumentsJson`, an explicit OPC UA JSON Variant array, instead of legacy string
`inputArguments`. For example, an Int64 and Int32 pair is
`[{"UaType":8,"Value":"0"},{"UaType":6,"Value":1}]`: UA JSON represents Int64 values
as strings. A structure uses `UaType: 22` and a `Value` object containing `UaTypeId`
and its fields. The existing string-only argument behavior is unchanged.

### Run on demand without a global installation

Launch through `dotnet tool exec` or `dnx`, with tool arguments after `--`.
See the [executable's client configuration examples](../tools/Opc.Ua.Mcp/McpREADME.md#client-configuration).

### Claude Desktop

Configure `opcua-mcp` in `claude_desktop_config.json`; see the
[client configuration examples](../tools/Opc.Ua.Mcp/McpREADME.md#client-configuration).

### VS Code / GitHub Copilot

Configure the executable in `.vscode/mcp.json`; see the
[client configuration examples](../tools/Opc.Ua.Mcp/McpREADME.md#client-configuration).

### Cursor

Use the executable's
[client configuration examples](../tools/Opc.Ua.Mcp/McpREADME.md#client-configuration)
in Cursor's MCP settings.

### HTTP Transport (for remote clients)

By default, the server uses stdio transport for local tool integration. For remote clients, use the Streamable HTTP transport, exposed only at the `/mcp` path (there is no root route):

```bash
opcua-mcp --transport http --port 5100
```

The server listens at `http://localhost:5100/mcp`. `--transport sse` is a deprecated alias for `http` kept for the current major version only — prefer `http` in new configurations.

### Companion file transfers

DI package uploads and Machinery result downloads use an explicitly configured,
host-owned directory. They are disabled until that directory is configured and
already exists:

```powershell
$env:OPCUA_MCP_TRANSFER_ROOT = 'D:\OpcUaTransfer'
$env:OPCUA_MCP_MAX_TRANSFER_BYTES = '67108864'
opcua-mcp --profile di,machinery
```

The default byte limit is 64 MiB; callers cannot raise the host's limit.
Embedding and configuration keys are documented in the
[Core transfer contract](../tools/Opc.Ua.Mcp.Core/NugetREADME.md#transfer-paths).

Tool paths must resolve beneath the root. Symlinks, junctions, path escapes
and Windows alternate data streams are rejected. The host must keep the root
and its ancestors unwritable by untrusted local users; portable path checks
cannot prevent a privileged local process replacing directories concurrently.
Do not use a shared world-writable directory as the transfer root.

Uploads check the initial length and enforce the limit again while reading.
Downloads enforce the limit while writing, use a temporary file, publish the
completed file without overwriting an existing destination, and remove partial
files on failure or cancellation. Parent directories must already exist.
There is no arbitrary URL fetching or large inline base64 transport.
Uploading a software package does not invoke the Installation or Confirmation
methods; those are separate explicit operations. A server's DirectLoading
profile may apply the software when the upload commits.

### WoT document dependency policy

Document processing starts with explicitly supplied text/files and dependencies.
It does not fetch URLs merely because they occur in a document. Remote
dependencies require both explicit opt-in on that invocation
and an exact host-configured HTTPS origin allowlist. An empty allowlist denies
all remote retrieval.

Redirects, ambient credentials, proxies and loopback are blocked. Private
addresses require host permission. Size, count, depth and deadline limits cover
the whole operation, including linked sets and dependencies. Results provide
provenance and digests, not large inline documents. See the
[WoT host/API reference](../tools/Opc.Ua.Mcp.WotCon/NugetREADME.md#remote-dependency-policy)
for settings, network validation and JSON examples.

### Privileged and live execution policy

**Selecting `full` or an administrative profile does not grant authority.**
The host requires independent capabilities, exact caller grants and network
rules. Generic `Call`, `CallMethod`, `Write`, `WriteValue`,
node/reference changes and relevant history/file paths also enforce protected
model authority. A disabled administrative workflow cannot be reached through
a less-specific tool. Untyped secret-returning methods require a typed,
secret-safe workflow even when issuance is enabled.

HTTP calls need authenticated identity; anonymous callers cannot gain
privileged authority or another caller's retained handles. Local stdio identity
also needs an explicit grant. See the
[Core host-policy reference](../tools/Opc.Ua.Mcp.Core/NugetREADME.md#host-policy-and-configuration)
for configuration and embedding.

The remote server's permissions, identities and security mode remain additional
requirements; host grants do not create a `SecurityAdmin` role or trust a
certificate. Session request contexts bind multi-call file handles, transactions
and history continuation points to their actual UA generation. Reconnection
must fail or invalidate the old operation, not send an old handle to a new
session.

Borrowing a host-provisioned session requires an exact `SessionNames` grant.
Caller-created connections remain private to their caller; a session-use grant
does not authorize replacing or disconnecting a host registration. A reused name
does not inherit the previous registration's secret authority.

Protected secret bindings include the approved registration and authenticated
endpoint/certificate identity, not only advertised application URI and model
NodeId. Legacy host aliases are pinned on first approved use. Caller-created or
autoaccepted targets cannot consume those protected aliases. GDS keeps public
resource labels and third-party audiences separate from its bound secret issuer.

Local peer-trust changes also require `CertificateAdministration` before opening
or mutating a store; listing a rejected certificate does not authorize trusting
it. Vision file handles are opaque caller/target/registration/generation-bound
host mappings, even though their wire type remains `uint`. A Vision file tool
cannot use an arbitrary server handle to modify a protected TrustList.

Generic mutation tools refuse unsupported session lifetimes instead of risking
dispatch to a replacement. Custom-session embedding requirements are documented
in the [Core package](../tools/Opc.Ua.Mcp.Core/NugetREADME.md#generic-mutations-and-session-lifetime).

Live TCP network grants identify an exact scheme, host, port, operation and
resource plus permitted CIDR addresses. Empty grants deny all. DNS is checked
at each actual connection/reconnection and the socket uses the approved IP,
while TLS retains the original hostname. HTTP paths, MQTT topics, UA node/method
selectors and Modbus unit/function/range selectors are separate operation
checks. UDP and Ethernet additionally require their explicit local interface,
peer and frame selectors. No MCP tool installs a capture driver or grants OS
privileges.

Remote PubSub configuration also requires a `configure-connect` grant for every
effective endpoint, using an empty resource selector for connection activation.
This applies to connections without writer or reader groups: authentication,
discovery and status traffic can still start. Separate publish/subscribe grants
must cover every effective writer-group, per-writer and metadata queue, including
aggregate-message fallbacks. Mutation tools authorize their actual direction and
existing parent connection, not a caller-supplied mode or substitute endpoint.
Unreadable parent address/transport settings fail authorization rather than
falling back to caller-provided settings.

Named runtimes default to eight per host, two per caller, five minutes of idle
time and a thirty-minute absolute lease. Pending activation and closing
resources count against quotas. Closing waits for owned work; failed cleanup
is reported and retains capacity rather than claiming the resource was freed.
Credential expiry/revocation and UA-generation invalidation close dependent
runtimes independently of another client request. Runtime handles are opaque,
caller-bound and local to one host instance; a runtime name is not authority.
The pre-existing UDP convenience runtime retains its explicit until-stop
lifetime, with the new host execution gate.

Credential, password, token, private-key and SKS inputs are references, not
plaintext tool arguments. The host must choose its secret store explicitly.
Results distinguish remote issuance from local persistence, so a failed store operation
does not imply the remote request rolled back or may safely be repeated.
Private material never belongs in `TransferRoot`.

The default managed Kafka backend currently lacks a supported public
per-connection hook for enforcing policy on broker-discovered destinations.
**Live MCP Kafka execution fails closed** rather than treating a bootstrap
allowlist as complete egress protection. Kafka configuration/schema capabilities
remain available. A policy-capable backend/dependency integration is required
before this limitation can be removed; reflection into private client internals
is not used.

`wot-documents` remains inspection/planning/conversion only. Live preparation
uses the same bounded document resolver with explicitly supplied local
dependencies, pins the selected Thing Description's content digest and exact
form, and does not activate a channel. CoAP, BACnet, PROFINET and LoRaWAN
planners do not imply an installed live executor.

## Usage

### Companion workflows

Connect using the usual MCP connection tools. Every family accepts
`sessionName`; omit it only when there is exactly one active session.
Discovery returns explicit node identifiers. Use those identifiers for later
reads and actions rather than guessing namespace indexes or selecting the first
matching object. Returned remote references are information, not an instruction
to connect to another server.

Related reads share finite facet selectors where that keeps the catalog small.
Commands remain explicit and do not implicitly acquire a lock, install a
package, retry a refused operation or change another state.

Two workflows intentionally compose catalogs:

- **Machine jobs:** discover the real order receiver and optional response
  provider through Machinery, then use the canonical ISA-95 tools with those
  role identifiers. A Machinery machine does not expose an ISA-95 response
  receiver. An absent response provider must not be replaced with the order
  receiver.
- **Scale products:** discover the product's DI lock and use the DI tools to
  acquire, renew and release it explicitly around protected changes. The
  Scales tools never acquire or break a lock on the caller's behalf.

There are no invented Machinery item-state or operation-mode setters: those
state machines have no standard client cause methods. Pumps and Positioning
remain observational. AMB history is not promised where the typed client has
no history implementation.

`machinery_list_results` rejects timezone-less creation-time bounds rather than
interpreting them in the host's local timezone. See its package contract for
formats and examples.

### Companion results, errors and bounds

Companion results retain UA types, quality and timestamps. Uninitialized or
non-finite measurements are not reported as zero or absence; see the
[Core JSON contract](../tools/Opc.Ua.Mcp.Core/NugetREADME.md#arguments-and-json-results)
for encoding.

A service failure is an MCP error with its original status. Model-specific
return codes, including ISA-95 job and DI lock statuses, are not confused
with a successful UA service response. Missing optional capabilities are
distinguished from an empty supported result. A timeout does not prove that
a remote mutation did not occur; inspect server state before deciding to retry.

Cancellation is checked before dispatch and propagates when an in-progress
operation observes it. Once a command or upload callback returns its completed
result, late cancellation does not replace that result with a cancellation
error: a mutation or software-package commit may already have taken effect.
Downloads still honor cancellation before publishing their local destination.

Most companion lists default to 100 entries, with a maximum of 500.
Offsets are bounded and refer to a fresh live enumeration, not a durable
snapshot. Concurrent server changes can shift entries between pages.

Observations are finite MCP calls, not persistent background subscriptions.
They accept an item limit and a duration of at most 30 seconds, and report
whether the source completed or the item/duration bound ended the call.
External request cancellation propagates. Disposing the invocation's
enumerator removes its own monitored items; it does not dispose the managed
session's shared streaming subscription.

### Typical Workflow

Discover endpoints with `GetEndpoints`, then `Connect` using the intended
security and identity. Browse with `BrowseAll` before using discovered nodes
in `ReadValue`, `WriteValue` or `CallMethod`. For persistent notification
workflows, create a subscription and monitored items, then retrieve queued
notifications with `Publish`.

Use `ListCertificates` to inspect trust before an explicit `TrustCertificate`;
do not auto-accept certificates in production. Inspect configuration before
changing it. `ExportNodeSet` writes an address-space export, while
`ExportNodeSetPerNamespace` splits it by namespace. Finish with `Disconnect`
for the selected session.

### NodeId Formats

Use identifiers returned by discovery. Supported notation and examples are in
the [Core argument reference](../tools/Opc.Ua.Mcp.Core/NugetREADME.md#arguments-and-json-results).

### Common Well-Known NodeIds

The [Core reference](../tools/Opc.Ua.Mcp.Core/NugetREADME.md#arguments-and-json-results)
lists standard Root, Objects, Types, Views, Server and CurrentTime identifiers.

### Error Handling

Tools return errors rather than pretending an unsupported or refused operation
succeeded. Not all servers support all services:

| Status Code | Meaning |
|---|---|
| `BadServiceUnsupported` | Server doesn't implement this service |
| `BadNodeIdUnknown` | NodeId doesn't exist |
| `BadNotWritable` | Node attribute is read-only |
| `BadMethodInvalid` | Method not found on the specified object |
| `BadUserAccessDenied` | Insufficient permissions |

## PubSub Tools

In addition to the client services above, the server exposes OPC UA PubSub
(Part 14) tools, backed by `Opc.Ua.PubSub` and
`Opc.Ua.PubSub.Diagnostics`. See
[Diagnostics.md §5](Diagnostics.md#5-pubsub-packet-capture-and-dissection) for
the capture / dissection details.

**Configuration and Security Key Service workflows** have dedicated profiles.
Use those tools for typed, authorized mutations and secret-safe key handling;
generic Call is not a bypass for protected or secret-returning operations.
The [PubSub package reference](../tools/Opc.Ua.Mcp.PubSub/NugetREADME.md)
contains registration, transport selectors and JSON contracts.

**In-process publish/subscribe runtime:**

| Tool | Purpose |
| --- | --- |
| `pubsub_runtime_start_publisher` / `pubsub_runtime_start_subscriber` | Start an in-process UDP publisher / subscriber |
| `pubsub_runtime_publish` | Publish a DataSet update |
| `pubsub_runtime_read_received` | Read DataSets received by the subscriber |
| `pubsub_runtime_status` / `pubsub_runtime_stop` | Status / stop the runtime |

**Discovery** (Part 14 §7.2.4.6 — send a discovery request from the active
runtime and collect publisher responses):

| Tool | Purpose |
| --- | --- |
| `pubsub_discover_metadata` | Learn the field-level schema of a DataSetWriter (names, field count) |
| `pubsub_discover_writer_config` | Learn a publisher's WriterGroupId and DataSetWriterIds |
| `pubsub_discover_publisher_endpoints` | Learn a publisher's transport endpoint URLs |

**Actions** (Part 14 §7.2.5.6 — request/response over PubSub):

| Tool | Purpose |
| --- | --- |
| `pubsub_invoke_action` | Invoke an action target and await the correlated response |
| `pubsub_register_action_responder` | Register a demo/echo responder for round-trip testing |
| `pubsub_bind_action_method` | Bind an action to a server method (ObjectId/MethodId) |
| `pubsub_list_action_targets` / `pubsub_list_action_responders` | List known targets / registered responders |

**Capture and dissection:**

| Tool | Purpose |
| --- | --- |
| `pubsub_start_capture` / `pubsub_stop_capture` / `pubsub_capture_status` | Manage an in-process PubSub capture session |
| `pubsub_write_pcap` | Flush captured frames to `.pcap` / `.pcapng` |
| `pubsub_dissect_capture` | Dissect captured frames (decrypts encrypted UADP when a key log is supplied) |
| `pubsub_decode_pcap` | Decode a libpcap file of UDP PubSub traffic |
| `pubsub_load_keylog` | Load a PubSub key log for offline decryption |

## Architecture

The MCP tools ship as independent libraries plus the executable that composes them.
The executable owns only transport, logging and CLI plumbing; every tool lives
in a library that an application can reference on its own.

### Packages

| Package | Tools | Depends on |
|---|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Core` | Part 4 services, connection, configuration, PKI, NodeSet export | `Opc.Ua.Core`, `.Configuration`, `.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub` | PubSub transports, remote configuration, declarative bridges, SKS, actions and discovery | Core + `Opc.Ua.PubSub`, existing transport and Adapter packages |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Diagnostics` | UA-TCP capture, decode, replay | Core + `Opc.Ua.Core.Diagnostics` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub.Diagnostics` | PubSub capture, decode | Core + `Opc.Ua.PubSub.Diagnostics` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Robotics` | Robot Intent discovery, typed control/missions, paged monitoring, Vision-guided Pick | Core + `Opc.Ua.Robotics.Client` + `Opc.Ua.Vision.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Vision` | Vision discovery, monitoring, seeing, inference, feedback and poses; released Machine Vision capabilities, topology, bounded events/conditions, control and complete file workflows | Core + `Opc.Ua.Vision.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.MachineTool` | MachineTool and bundled WMTP workflows | Core + `Opc.Ua.MachineTool.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.GMS` | GMS systems, results, downloads and observations | Core + `Opc.Ua.GMS.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.CranesHoists` | Native crane monitoring and authorized limit requests | Core + `Opc.Ua.CranesHoists.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.AMB` | Asset inventory, health, maintenance and documentation | Core + `Opc.Ua.AMB.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Machinery` | Machine monitoring, process values, energy, jobs and results | Core + `Opc.Ua.Machinery.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Scales` | Weighing, products, recipes and PackML | Core + `Opc.Ua.Scales.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Pumps` | Pump values, supervision and maintenance | Core + `Opc.Ua.Pumps.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Di` | Device topology, locks, transfers and software updates | Core + `Opc.Ua.Di.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.ISA95` | Common objects and job control V1/V2 | Core + `Opc.Ua.ISA95.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Positioning` | Spatial frames, global locations and transforms | Core + `Opc.Ua.Positioning.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.WotCon` | WoT assets, registry state, documents and independently registered live runtime tools | Core + `Opc.Ua.WotCon.Client` + `.Bindings` + `.Bindings.Mqtt` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.XRegistry` | Generic registry/version/label/content tools | Core + `Opc.Ua.XRegistry.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Gds` | GDS, certificates, credential/token references and onboarding | Core + `Opc.Ua.Gds.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Workflows` | Roles/users, files, alarms/conditions and history | Core + existing typed Client workflows |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Schema` | Bounded type and schema workflows | Core + `Opc.Ua.Core.Schema` + `Opc.Ua.PubSub.Schema` |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp` | the ready-to-run `opcua-mcp` tool | all of the above |

Every tool-bearing library keeps its MCP tool classes and partials in `Tools/`;
registration, configuration and supporting services stay outside that folder.
Folder placement does not change public namespaces or tool names.

The libraries multi-target `net8.0;net9.0;net10.0`; the executable targets
`net10.0`. `Mcp.Robotics` and `Mcp.CranesHoists` remain preview-only,
including their Debug variants. `Mcp.Vision`, `Mcp.MachineTool`, `Mcp.GMS`,
the other industrial companion, WoT, xRegistry and MCP libraries, and the
`opcua-mcp` tool follow the root release channel: a stable root release produces
stable versions, while development builds retain the root prerelease version.

### Embedding the tools in your own MCP server

Embedding APIs, profile composition and JSON contracts are documented in each
package README, not in this tool catalogue. Start with the
[Core registration guide](../tools/Opc.Ua.Mcp.Core/NugetREADME.md#usage), then use
the owning package's reference:

| Area | API/JSON reference |
| --- | --- |
| Core services, authority and shared transfers | [Core](../tools/Opc.Ua.Mcp.Core/NugetREADME.md) |
| Capture and replay | [Diagnostics](../tools/Opc.Ua.Mcp.Diagnostics/NugetREADME.md) |
| PubSub transport/configuration/bridges/SKS | [PubSub](../tools/Opc.Ua.Mcp.PubSub/NugetREADME.md) |
| PubSub capture | [PubSub diagnostics](../tools/Opc.Ua.Mcp.PubSub.Diagnostics/NugetREADME.md) |
| Robot Intent | [Robotics](../tools/Opc.Ua.Mcp.Robotics/NugetREADME.md) |
| Perception and Machine Vision | [Vision](../tools/Opc.Ua.Mcp.Vision/NugetREADME.md) |
| MachineTool and bundled wireless peripherals | [MachineTool/WMTP](../tools/Opc.Ua.Mcp.MachineTool/NugetREADME.md) |
| Geometric measurement systems | [GMS](../tools/Opc.Ua.Mcp.GMS/NugetREADME.md) |
| Cranes and hoists | [CranesHoists](../tools/Opc.Ua.Mcp.CranesHoists/NugetREADME.md) |
| Asset management | [AMB](../tools/Opc.Ua.Mcp.AMB/NugetREADME.md) |
| Machines and results | [Machinery](../tools/Opc.Ua.Mcp.Machinery/NugetREADME.md) |
| Weighing | [Scales](../tools/Opc.Ua.Mcp.Scales/NugetREADME.md) |
| Pump inspection | [Pumps](../tools/Opc.Ua.Mcp.Pumps/NugetREADME.md) |
| Device integration | [DI](../tools/Opc.Ua.Mcp.Di/NugetREADME.md) |
| Jobs | [ISA-95](../tools/Opc.Ua.Mcp.ISA95/NugetREADME.md) |
| Coordinates | [Positioning](../tools/Opc.Ua.Mcp.Positioning/NugetREADME.md) |
| WoT management, documents and live runtime | [WoTCon](../tools/Opc.Ua.Mcp.WotCon/NugetREADME.md) |
| Registry operations | [xRegistry](../tools/Opc.Ua.Mcp.XRegistry/NugetREADME.md) |
| Directory, certificates, credentials and onboarding | [GDS](../tools/Opc.Ua.Mcp.Gds/NugetREADME.md) |
| Roles/users, files, alarms and history | [Workflows](../tools/Opc.Ua.Mcp.Workflows/NugetREADME.md) |
| Type/schema artifacts | [Schema](../tools/Opc.Ua.Mcp.Schema/NugetREADME.md) |

See [Core's NativeAOT limitations](../tools/Opc.Ua.Mcp.Core/NugetREADME.md#trimming-and-native-aot)
before selecting an ahead-of-time host; reflection-free model encoding is not
the same as complete host registration support.

### Embedding industrial companions

Each family is a separate package. The
[Machinery composition example](../tools/Opc.Ua.Mcp.Machinery/NugetREADME.md#composed-registration)
shows the Machinery/ISA-95 combination. Adding a model dependency does not
implicitly enable another catalogue.

Connection tools are shared and deduplicated when profiles compose. The
single-profile companion registration helpers include their connection tools;
the `McpToolProfileSet` Core registration owns the composed connection surface.
The idempotent connection-registration helper also permits direct composition
of the MachineTool, GMS and crane packages without selecting duplicate Core tools.

### Embedding WoT and xRegistry

The [WoT package reference](../tools/Opc.Ua.Mcp.WotCon/NugetREADME.md#composing-registry-tools)
shows registry composition and the separate live registration. Sharing a package
does not activate executors, select `wot-runtime`, or grant execution authority.

### Remaining tooling gaps

The library inventory distinguishes a missing task-oriented catalogue from
features already reachable through the generic services or another companion.
The following capabilities remain outside these new profiles:

| Area | Missing dedicated workflow |
| --- | --- |
| `Opc.Ua.AI*` | General model/source/deployment/dataset and inference/evaluation/learning-job management; Vision already provides domain-specific inference tools |
| `Opc.Ua.OpenUsd*` | General representation/binding/scene inspection and asset export; the standalone connector and Vision backend are not a general MCP catalogue |
| Managed Kafka live egress | The pinned backend cannot yet apply host network policy to every broker-discovered connection; live execution is explicitly refused |
| `Opc.Ua.Redundancy*` | Dedicated replica diagnostics and authorized maintenance workflows; this does not imply arbitrary host or Kubernetes administration |
| Higher-level client helpers | General state-machine and alias administration beyond the domain-specific workflows |

IA, PA-DIM, ECM and PackML are already represented through the companion clients
that use them. Core types, transport, cryptography and server-hosting modules
are infrastructure, not reasons to create duplicate tool catalogues. No direct
HSM/secret-management, process-launch or unbounded external-fetch interface is
introduced.

## Security Notes

- Companion file transfers are disabled until an explicit host-owned root is
  configured and are limited to 64 MiB by default. See
  [file transfer policy](#companion-file-transfers).
- Companion commands preserve server permissions and refusals. Discovery never
  requests authority or takes locks. Upload does not invoke Installation, but
  DirectLoading devices may apply software when the upload commits.
- Privileged/live capabilities default off, independently of the selected tool
  catalogue. See [execution policy](#privileged-and-live-execution-policy).
- The `autoAcceptCerts` parameter is for **testing only**. In production, configure proper certificate trust using the OPC UA certificate stores under `%LocalApplicationData%/OPC Foundation/pki/`.
- The server supports simultaneous named OPC UA sessions. Use the intended session identifier for each operation and disconnect that session when finished; connecting to another server does not require closing unrelated sessions.
- Application certificates are automatically created on first use and stored in the local certificate store.
- Logs are written to `%LocalApplicationData%/OPC Foundation/Logs/McpServer.log.txt`.

## Agent-Usability Quality Gate

Contributors can run the same deterministic static and live-probe grading used in CI:

```powershell
.\tools\Opc.Ua.Mcp\Test-McpGrade.ps1 -IncludePrivilegedCatalogues `
    -AdditionalProfiles @('machinetool', 'gms', 'craneshoists',
        'gds', 'certificates', 'credentials', 'onboarding',
        'roles', 'users', 'files', 'alarms', 'history', 'schemas',
        'pubsub-runtime', 'pubsub-configuration', 'pubsub-bridge',
        'pubsub-security', 'wot-runtime')
```

The script builds the server, checks out `mcpgrade` at the reviewed commit pinned in the script,
grades `core`, `full` and each additional Streamable HTTP catalog, and probes the full stdio catalog.
Reports are written under `artifacts/mcpgrade` by default.

`-IncludePrivilegedCatalogues` enables all host catalogue capabilities for the
grading process without granting a caller, session, secret or network operation.
It restores the previous process environment afterward. CI uses this switch so
an administrative profile cannot receive a misleading passing grade for an
empty or connection-only catalogue. Omit it only when deliberately checking the
default host-disabled view.

The existing gates remain unchanged: `core` and additional bounded profiles
require at least 98 with no errors or warnings; `full` requires at least 95,
retaining only its historical catalogue-size and service-schema exceptions.
C003/C004 probe findings remain failures.

Use `-Profiles` for a focused catalogue check. An already-built assembly in an
isolated artifact directory can be selected without rebuilding:

```powershell
.\tools\Opc.Ua.Mcp\Test-McpGrade.ps1 -SkipBuild `
    -AssemblyPath 'C:\build\Opc.Ua.Mcp.dll' -Profiles craneshoists `
    -ResultsDirectory 'C:\build\mcpgrade'
```

The full stdio probe runs whenever `full` is selected. A focused check does not
replace the complete default matrix.

The repository `.mcpgraderc.json` disables N002 after descriptions have been disambiguated.
`RegisterServer`/`RegisterServer2` are OPC UA standard service names, while
`ReadValue`/`ReadValues` and `GetConfiguration`/`SetConfiguration` are compatibility-sensitive
singular/plural and get/set pairs. These names must not be changed solely to satisfy edit-distance
heuristics.

## Requirements

- .NET 10 SDK or later
- An OPC UA server to connect to
