# OPC UA .NET Standard Stack

[![Release](https://img.shields.io/github/v/release/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard/releases)
[![NuGet Downloads](https://img.shields.io/nuget/dt/OPCFoundation.NetStandard.Opc.Ua)](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/)
[![CI](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/buildandtest.yml/badge.svg?branch=master)](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/buildandtest.yml?query=branch%3Amaster)
[![CodeQL](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/codeql-analysis.yml/badge.svg?branch=master)](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/codeql-analysis.yml?query=branch%3Amaster)
[![Coverage](https://codecov.io/gh/OPCFoundation/UA-.NETStandard/branch/master/graph/badge.svg)](https://codecov.io/gh/OPCFoundation/UA-.NETStandard)
[![Nightly](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/nightly.yml/badge.svg?branch=master&event=schedule)](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/nightly.yml?query=branch%3Amaster+event%3Aschedule)
[![Stress Tests](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/stress-test.yml/badge.svg?branch=master&event=schedule)](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/stress-test.yml?query=branch%3Amaster+event%3Aschedule)
[![Connection Stability](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/stability-test.yml/badge.svg?branch=master&event=schedule)](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/stability-test.yml?query=branch%3Amaster+event%3Aschedule)
[![Foreign Interop](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/interop-foreign.yml/badge.svg?branch=master&event=schedule)](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/interop-foreign.yml?query=branch%3Amaster+event%3Aschedule)
[![HA Long-Haul](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/sample-ha-longhaul.yml/badge.svg?branch=master&event=schedule)](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/sample-ha-longhaul.yml?query=branch%3Amaster+event%3Aschedule)
[![CTT Regression](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/ctt.yml/badge.svg?branch=master&event=schedule)](https://github.com/OPCFoundation/UA-.NETStandard/actions/workflows/ctt.yml?query=branch%3Amaster+event%3Aschedule)

> 🆕 **This is version 2.0 of the OPC UA .NET Standard Stack (current `master`).**
>
> Looking for the supported 1.x release? It lives on the
> [`master378`](https://github.com/OPCFoundation/UA-.NETStandard/tree/master378)
> branch (last release: `1.5.378`). All new feature work happens here on
> `master`; `master378` continues to receive security and critical-bug
> fixes for the 1.x line.

The official OPC Foundation reference implementation of OPC UA for
.NET — a cross-platform stack with client, server, PubSub,
GDS, complex types, and source-generated tooling. Used in production
across industrial control, manufacturing, energy, and IoT systems.

## 📦 What it is

- **A full-stack OPC UA implementation** — Core / Client / Server /
  PubSub / GDS / LDS / Complex Types / Device Integration / Positioning libraries
  built on .NET, with UA-TCP and HTTPS transports.
- **Cross-platform** — runs on .NET 10, .NET 9, .NET 8 (LTS),
  .NET Framework 4.8; ships
  Native-AOT-friendly assemblies.
- **Conformance regression testing** — the reference server is tested with the
  [Compliance Test Tool (CTT)](https://opcfoundation.org/developer-tools/certification-test-tools/opc-ua-compliance-test-tool-uactt/)
  through the [CTT regression workflow](.github/workflows/ctt.yml).
  These results identify regressions; they do not constitute product
  certification.
- **Companion-spec coverage** — Part 9 (Alarms & Conditions),
  [Part 10 (Programs)](docs/Programs.md), Part 11
  (Historical Access), Part 13 (Aggregates), Part 16 (State Machines),
  Part 17 (Alias Names), Part 18 (Role Management), Part 20 (File
  Transfer), Part 100 (Device Integration), Part 110 (Asset Management
  Basics), Parts 210/211 (Relative
  Spatial Location and Global Positioning), OPC-10030 (ISA-95 Common
  Model) with OPC-10031-4 Job Control V1/V2, OPC 30270 / OPC UA for Asset
  Administration Shell V2 and V3, OPC 10100-1
  (WoT Connectivity), Part 200 (Industrial Automation), OPC 40001-1/-2/-3/-4/-101
  (Machinery with Process Values, Job Management, Energy and Result
  Transfer), OPC 34100 (Energy Consumption Management), OPC 30081
  (PA-DIM), OPC 40010-1 (Robotics), OPC 40100-1/-2
  ([Machine Vision control and asset management](docs/Vision.md)),
  OPC 40501-1 and OPC 40505
  ([Machine Tools and independent wireless peripherals](docs/MachineTool.md)),
  OPC 40210 ([Geometric Measurement Systems](docs/GMS.md), with its documented
  upstream Production-facet exception), OPC 40020-1
  ([Cranes and Hoists](docs/CranesHoists.md)),
  OPC 40223 (Pumps and Vacuum Pumps),
  OPC 30010 ([AutoID Devices](docs/AutoID.md)),
  OPC 40200 (Weighing Technology) with OPC 30050 (PackML).
- **Hosting and code generation** — `Microsoft.Extensions.DependencyInjection`
  hosting (`services.AddOpcUa()`), fluent server + client builders,
  source-generated NodeManagers and DataTypes, and an MCP server so
  LLMs / Copilot can drive an OPC UA client.
- **Recoverable execution and Async Services** — the default-off [RSE prototype](docs/AsyncServices.md) uses five manager Methods beneath ordinary `ISession` calls, explicit acknowledgement, and separately qualified Session, Owner and Durable recovery.

For the full feature breakdown see
**[OPC UA Profiles and Facets](docs/Profiles.md)** and the
**[What's New in 2.0](docs/WhatsNewIn2.0.md)** tour.

## 🚀 Getting started

To run a first OPC UA client and server, from NuGet packages or from the
samples in this repository, follow **[Getting started](docs/GettingStarted.md)**.
The **[documentation index](docs/README.md)** then leads from the core concepts
to advanced and experimental features.

The [recycling operator console](samples/Recycling/README.md#operator-console-walkthrough) demonstrates
live browser monitoring, native companion-specification inspection, material genealogy,
and authorized controls over the [synthetic recycling line](samples/Recycling/README.md).

You need a stable [.NET SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
version **10.0.400 or later** to build the repo. `global.json` selects the
highest installed stable SDK, including newer major versions. From the repository root:

```bash
dotnet restore UA.slnx
dotnet build UA.slnx
```

For the supported target frameworks and platform notes see
the [Developer Guide](docs/DeveloperGuide.md#packages-platform-support-and-versioning). The official NuGet packages
are published to nuget.org under the `OPCFoundation.NetStandard`
prefix — the meta package
[OPCFoundation.NetStandard.Opc.Ua](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/)
bundles a default set of stack packages; see its
[package inventory](nuget/MetaPackageREADME.md#what-you-get).
Reference PubSub and other optional feature packages separately, or select
individual packages directly, e.g.
`OPCFoundation.NetStandard.Opc.Ua.Client` for clients or
`OPCFoundation.NetStandard.Opc.Ua.Server` for servers. Preview builds from
every successful `master` build are available from the
[GitHub Packages NuGet feed](https://nuget.pkg.github.com/OPCFoundation/index.json),
which is the only feed they are published to. nuget.org receives a version
only through a manually approved promotion from a `release/<major>.<minor>`
branch — that is where the `2.0.0-preview.N` packages already on nuget.org
came from, via the earlier `release/2.0.0` line. To use those, enable
prerelease packages and float with `2.0.0-preview.*` until `2.0.0` is
released. The XRegistry, WoT Connectivity, Robotics, CranesHoists,
Redundancy, Positioning, OpenUSD, and AI package families, the Vision.OpenUsd
renderer, and the Robotics MCP extension remain
preview packages when the root version is promoted to a stable version, and
keep their own `-preview.N` versions on nuget.org. This includes all CranesHoists
model/server/client/MCP packages and their Debug variants.
The Vision, MachineVision, MachineTool (including WMTP), and GMS
model/client/server packages and their MCP packages follow the root version
without a preview-only override;
development builds still inherit the root prerelease version. See
[docs/ReleaseProcess.md](docs/ReleaseProcess.md) for how and when stable
versions are released.

### Samples

The [platform-independent sample applications](docs/Samples.md) show how to use
the stack's core services and companion models in client/server workflows.
More applications, including platform-specific examples, are available in
the companion
[OPC UA .NET Samples repository](https://github.com/OPCFoundation/UA-.NETStandard-Samples).

### Developer tools

- [UaLens](docs/UaLens.md) — NativeAOT-capable desktop engineering workspace for secure connections,
  monitoring, alarms, structured values, history, administration, PubSub, continuity diagnostics, and guided
  companion-model tasks. See [build and artifact qualification](tools/Opc.Ua.Lens/README.md#publish-and-package).
- [OPC UA MCP Server](docs/McpServer.md) — installable .NET tool and container
  exposing OPC UA client operations to LLMs and Copilot. Embeddable libraries
  provide Part 4 services, PubSub, diagnostics, Robotics and Vision, plus
  [industrial companion profiles](docs/McpServer.md#industrial-companion-tools) for AMB, Machinery,
  Scales, Pumps, AutoID, Device Integration, ISA-95 and Positioning. The
  [WoT and xRegistry profiles](docs/McpServer.md#wot-and-registry-tools) add
  asset/registry management and bounded document validation, planning and
  NodeSet conversion. [Stack workflow profiles](docs/McpServer.md#stack-workflow-tools)
  provide GDS/credential issuance, roles/users, files, alarms, history, schemas,
  and host-authorized PubSub and live WoT execution.

## 🔧 Migrating from 1.5.378 to 2.0

The 2.0 release introduces breaking API changes, and comes with a full
[prescriptive migration guide](docs/MigrationGuide.md) that links to
[per-area documentation](docs/migrate/2.0.x/README.md) for the migration
sub-topics.

Most of the mechanical migration work is automated:

- **`OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer` NuGet** —
  install it in your project to get 26 analyzer rules through `UA0030`
  (excluding `UA0013`, `UA0016`, `UA0017`, and the shim-only `UA0029`) plus
  one-click code fixes for the patterns in the guide. Setup steps
  are in the package's
  [NugetREADME.md](tools/Opc.Ua.MigrationAnalyzer/NugetREADME.md).
- **Migration agent skill** — the
  [`opcua-v20-migration`](.agents/skills/opcua-v20-migration/SKILL.md)
  skill walks Copilot / Claude / any coding agent through installing
  the NuGet, running `dotnet format analyzers` to apply auto-fixes,
  and handling the remaining manual changes. It selects the migration
  sub-document for each symptom.

Copilot CLI discovers the skill automatically inside a clone. To install it
for use in any repository:

```bash
copilot plugin marketplace add OPCFoundation/UA-.NETStandard
copilot plugin install opcua-v20-migration@opcua-dotnet
```

If you are still on 1.x and not ready to upgrade, stay on the
[`master378`](https://github.com/OPCFoundation/UA-.NETStandard/tree/master378)
branch — it continues to receive security and critical-bug fixes.

## 🤝 Contributing and license

Community contributions are welcome. Fork the repository, make your
changes on a branch, and open a pull request; see
[CONTRIBUTING.md](CONTRIBUTING.md) for the contribution workflow.

Contributors must sign the OPC Foundation
[Contributor License Agreement (CLA)](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf).
The CLA "I AGREE" gate is presented automatically on your first PR.

The project is licensed under the
[OPC Foundation MIT License](LICENSE.txt). Report security
vulnerabilities via the process documented in
[SECURITY.md](SECURITY.md).

## 📚 Further reading

- [Documentation index](docs/README.md) — every per-feature doc with a
  one-line description.
- [What's New in 2.0](docs/WhatsNewIn2.0.md) — narrative tour of the
  1.5.378 → 2.0 changes grouped by theme and layer.
- [OPC UA Profiles and Facets](docs/Profiles.md) — facets / transports /
  security policies the stack implements.
- [Migration Guide](docs/MigrationGuide.md) — prescriptive
  per-version migration reference (links to
  [`docs/migrate/2.0.x/`](docs/migrate/2.0.x/README.md)).
- [OPC UA Online Reference](https://reference.opcfoundation.org/) —
  the official OPC 10000 series specification index.
- [Preview Nuget package feed](https://nuget.pkg.github.com/OPCFoundation/index.json) —
  prerelease builds from every successful `master` build.
