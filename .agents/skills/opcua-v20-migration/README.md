# OPC UA 1.5.378 → 2.0 Migration Skill

A portable [Agent Skill](https://agentskills.io/specification) that packages the
OPC UA .NET Standard 1.5.378 → 2.0 migration knowledge so any Skill-compatible
runtime (Microsoft Agent Framework `AgentSkillsProvider` / `SkillsProvider`,
Anthropic Claude Code, Anthropic API, etc.) can load it on demand.

## When to use

Trigger this skill when a user asks for any of:

- "migrate to v20" / "update from master378" / "fix v20 build errors"
- "modernize Variant / ArrayOf / DateTimeUtc / ByteString APIs"
- "fix CS0246 on `<Type>Collection` wrappers"
- "address `UA0001`–`UA0022` analyzer warnings"
- "resolve `MIG01` from the source generator"
- "how do I install `OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer`"

## When NOT to use

- The user is starting a new OPC UA project from scratch — point them at
  `Docs/README.md` and the `Applications/ConsoleReferenceClient` /
  `ConsoleReferenceServer` samples instead.
- The user is migrating **within** 1.5.x (point or service-release upgrades).
- The user is debugging server-side OPC UA functional behaviour — try the
  `opcua-interop-tester` skill / agent instead.
- The user is upgrading from a release older than 1.5.378 — there is no
  documented direct path; advise an interim hop to 1.5.378 first.

## Layout

```
opcua-v20-migration/
├── SKILL.md                                      # Entry point. Levels 1 + 2. < 5K tokens.
├── README.md                                     # This file.
├── references/
│   ├── package-install.md                        # PackageReference + dotnet format
│   ├── analyzer-rules.md                         # Full UA0001-UA0022 + MIG01 reference
│   ├── source-generator.md                       # MigrationGenerator deep-dive + MIG01 playbook
│   ├── runtime-shim.md                           # Opc.Ua.MigrationAnalyzer.Core coverage
│   ├── migration-patterns.md                     # 14-section categorical playbook
│   ├── known-gaps.md                             # Legacy WinForms, Quickstarts.Servers, CS0050
│   └── compatibility-matrix.md                   # SDK / TFM / Roslyn API requirements
├── scripts/
│   └── apply-codefixes.ps1                       # dotnet format analyzers wrapper
└── assets/
    ├── PackageReference.example.xml
    └── Directory.Build.targets.example.xml       # NoWarn recipe for TreatWarningsAsErrors
```

## Canonical upstream docs

This skill **distils** the following authoritative repo files. When you update
the skill, also update them (and vice versa) so the three views stay in sync:

- [`Docs/MigrationGuide.md`](../../../Docs/MigrationGuide.md) — the human-facing
  migration guide.
- [`.github/agents/opcua-v20-migration.agent.md`](../../../.github/agents/opcua-v20-migration.agent.md)
  — the GitHub Copilot CLI / Copilot Coding Agent profile.
- [`Tools/Opc.Ua.MigrationAnalyzer/NugetREADME.md`](../../../Tools/Opc.Ua.MigrationAnalyzer/NugetREADME.md)
  — the package's own NuGet README.

## Loading the skill

### Microsoft Agent Framework (C#)

```csharp
using Microsoft.Agents.AI;

var skillsProvider = new AgentSkillsProvider(
    Path.Combine(repoRoot, ".agents/skills"));

AIAgent agent = client.AsAIAgent(new ChatClientAgentOptions
{
    AIContextProviders = [skillsProvider],
});
```

### Microsoft Agent Framework (Python)

```python
from pathlib import Path
from agent_framework import SkillsProvider

skills_provider = SkillsProvider.from_paths(
    skill_paths=Path(repo_root) / ".agents/skills",
)
```

### Anthropic Claude Code / Claude API

The skill is also loadable directly via the
[Anthropic Skills spec](https://agentskills.io/specification) — point the host
at `.agents/skills/opcua-v20-migration/`.

## Progressive disclosure

Per the William Zujkowski skill-authoring guide and the Agent Skills spec:

1. **Advertise** (~100 tokens): name + description from `SKILL.md` frontmatter
   are injected into the system prompt at session start.
2. **Load** (< 5K tokens): the `SKILL.md` body provides Level 1 quick-start
   + Level 2 implementation guidance.
3. **Read resources** (on demand): the `references/` files (each < ~5K tokens)
   are loaded via `read_skill_resource` only when the conversation needs them.
4. **Run scripts** (on demand): `scripts/apply-codefixes.ps1` invoked via
   `run_skill_script` to apply the analyzer auto-fix batch in one shot.

## License

MIT — same as the parent OPC UA .NET Standard repo
([LICENSE.txt](../../../LICENSE.txt)).
