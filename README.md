# OPC UA .NET Standard Stack

[![Release](https://img.shields.io/github/v/release/OPCFoundation/UA-.NETStandard?style=flat)](https://github.com/OPCFoundation/UA-.NETStandard/releases)
[![NuGet Downloads](https://img.shields.io/nuget/dt/OPCFoundation.NetStandard.Opc.Ua)](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/)
[![Build](https://opcfoundation.visualstudio.com/opcua-netstandard/_apis/build/status/OPCFoundation.UA-.NETStandard?branchName=master)](https://opcfoundation.visualstudio.com/opcua-netstandard/_build/latest?definitionId=14&branchName=master)
[![Tests](https://img.shields.io/azure-devops/tests/opcfoundation/opcua-netstandard/14/master?style=plastic&label=Tests)](https://opcfoundation.visualstudio.com/opcua-netstandard/_test/analytics?definitionId=14&contextType=build)
[![Coverage](https://codecov.io/gh/OPCFoundation/UA-.NETStandard/branch/master/graph/badge.svg?token=vDf5AnilUt)](https://codecov.io/gh/OPCFoundation/UA-.NETStandard)

> 🆕 **This is version 2.0 of the OPC UA .NET Standard Stack (current `master`).**
>
> Looking for the supported 1.x release? It lives on the
> [`master378`](https://github.com/OPCFoundation/UA-.NETStandard/tree/master378)
> branch (last release: `1.5.378`). All new feature work happens here on
> `master`; `master378` continues to receive security and critical-bug
> fixes for the 1.x line.

The official OPC Foundation reference implementation of OPC UA for
.NET — a certified, cross-platform stack with client, server, PubSub,
GDS, complex types, and source-generated tooling. Used in production
across industrial control, manufacturing, energy, and IoT systems.

## 📦 What it is

- **A full-stack OPC UA implementation** — Core / Client / Server /
  PubSub / GDS / LDS / Complex Types / Device Integration libraries
  built on .NET, with UA-TCP and HTTPS transports.
- **Cross-platform** — runs on .NET 10, .NET 9, .NET 8 (LTS),
  .NET Framework 4.8, and .NET Standard 2.0 / 2.1; ships
  Native-AOT-friendly assemblies.
- **Certified for compliance** — the reference server has been
  certified through an OPC Foundation Certification Test Lab and is
  continuously verified against the latest
  [Compliance Test Tool (CTT)](https://opcfoundation.org/developer-tools/certification-test-tools/opc-ua-compliance-test-tool-uactt/).
- **Companion-spec coverage** — Part 9 (Alarms & Conditions), Part 11
  (Historical Access), Part 13 (Aggregates), Part 16 (State Machines),
  Part 17 (Alias Names), Part 18 (Role Management), Part 20 (File
  Transfer), Part 100 (Device Integration), OPC 10100-1 (WoT
  Connectivity).
- **Modern developer surface** — first-class `Microsoft.Extensions.DependencyInjection`
  hosting (`services.AddOpcUa()`), fluent server + client builders,
  source-generated NodeManagers and DataTypes, and an MCP server so
  LLMs / Copilot can drive an OPC UA client.

For the full feature breakdown see
**[OPC UA Profiles and Facets](Docs/Profiles.md)** and the
**[What's New in 2.0](Docs/WhatsNewIn2.0.md)** tour.

## 🚀 Getting started

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
to build the repo. From the repository root:

```bash
dotnet restore UA.slnx
dotnet build UA.slnx
```

For the supported target frameworks and platform notes see
[Docs/PlatformBuild.md](Docs/PlatformBuild.md). For the official
NuGet packages see
[OPCFoundation.NetStandard.Opc.Ua](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/)
(meta) and the split
[Core](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Core/)
/ [Client](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Client/)
/ [Server](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Server/)
packages, plus the optional
[HTTPS binding](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Bindings.Https/).
A preview feed is available on
[Azure DevOps](https://opcfoundation.visualstudio.com/opcua-netstandard/_packaging?_a=feed&feed=opcua-preview%40Local).

### Sample applications

Each sample has its own `README.md` with build and run instructions.

**Reference applications**

- [Console Reference Server](Applications/ConsoleReferenceServer/README.md) —
  the certified reference server (with Quickstarts, CTT, and Mono
  configs). Also ships as a
  [Docker container](Docs/ContainerReferenceServer.md).
- [Console Reference Client](Applications/ConsoleReferenceClient/README.md) —
  cross-platform reference client demonstrating sessions, subscriptions,
  browsing, and method calls.
- [Console LDS Server](Applications/ConsoleLdsServer) — a standalone
  Local Discovery Server built on `Opc.Ua.Lds.Server`.
- [MCP Server](Applications/McpServer/README.md) — Model Context
  Protocol server that exposes OPC UA client operations as MCP tools,
  so an LLM / Copilot can browse, read, write, subscribe, and call
  methods on any OPC UA server.

**PubSub samples**

- [Console Reference Publisher](Applications/ConsoleReferencePublisher/README.md) —
  PubSub publisher across the supported transport profiles.
- [Console Reference Subscriber](Applications/ConsoleReferenceSubscriber/README.md) —
  matching subscriber.

**Minimal / Device-Integration samples**

- [Minimal Calc Server](Applications/MinimalCalcServer) — minimal
  server built on the source-generated NodeManager pipeline (Calc
  model).
- [Minimal Boiler Server](Applications/MinimalBoilerServer) — minimal
  Boiler-model server with the fluent state-machine builder;
  Native-AOT publishable.
- [Pump Device Integration Server](Applications/PumpDeviceIntegrationServer/README.md) —
  minimal Device Integration (Part 100) server using
  `Opc.Ua.Di.Server`'s fluent builder.

More sample projects are maintained in the companion
[OPC UA .NET Samples](https://github.com/OPCFoundation/UA-.NETStandard-Samples)
repository.

## 🔧 Migrating from 1.5.378 to 2.0

The 2.0 release introduces breaking API changes. The full prescriptive
guide is **[Docs/MigrationGuide.md](Docs/MigrationGuide.md)** — a thin
landing page that points at the per-area sub-docs in
**[Docs/migrate/2.0.x/](Docs/migrate/2.0.x/README.md)** (telemetry,
packages, source-generation, types, encoders, node-states, identity,
certificates, configuration, sessions / subscriptions,
alarms / model-change, TimeProvider).

Most of the mechanical migration work is automated:

- **`OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer` NuGet** —
  install it in your project to get analyzer warnings (`UA0001`–`UA0022`)
  + one-click code fixes for the patterns in the guide. Setup steps
  are in the package's
  [NugetREADME.md](Tools/Opc.Ua.MigrationAnalyzer/NugetREADME.md).
- **Migration agent skill** — the
  [`opcua-v20-migration`](.agents/skills/opcua-v20-migration/SKILL.md)
  skill walks Copilot / Claude / any coding agent through installing
  the NuGet, running `dotnet format analyzers` to apply auto-fixes,
  and handling the small residual manual patterns. The skill knows
  which sub-doc to load for each symptom so it stays context-efficient.

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

- [Documentation index](Docs/README.md) — every per-feature doc with a
  one-line description.
- [What's New in 2.0](Docs/WhatsNewIn2.0.md) — narrative tour of the
  1.5.378 → 2.0 changes grouped by theme and layer.
- [OPC UA Profiles and Facets](Docs/Profiles.md) — facets / transports /
  security policies the stack implements.
- [Migration Guide](Docs/MigrationGuide.md) — prescriptive
  per-version migration reference (links to
  [`Docs/migrate/2.0.x/`](Docs/migrate/2.0.x/README.md)).
- [OPC UA Online Reference](https://reference.opcfoundation.org/) —
  the official OPC 10000 series specification index.
- [OPC UA .NET Samples](https://github.com/OPCFoundation/UA-.NETStandard-Samples) —
  companion repository with more sample applications.
- [Preview NuGet feed](https://opcfoundation.visualstudio.com/opcua-netstandard/_packaging?_a=feed&feed=opcua-preview%40Local) —
  prerelease builds from Azure DevOps.
