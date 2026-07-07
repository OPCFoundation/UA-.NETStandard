# Issue #3937 follow-up analysis plan

## Current codebase state (from repository analysis)
- Regression coverage already exists for issue #3937 in `/home/runner/work/UA-.NETStandard/UA-.NETStandard/Tests/Opc.Ua.SourceGeneration.Tests/ModelGeneratorTests.cs`:
  - `GenerateAndCompileModelDesignReferencingNodeSet2TypesTest`
  - `GenerateAndCompileModelDesignReferencingNodeSet2TypesReversedInputOrderTest`
- Cross-model fixtures exist in:
  - `/home/runner/work/UA-.NETStandard/UA-.NETStandard/Tests/Opc.Ua.SourceGeneration.Core.Tests/Resources/CrossModelInstances.ModelDesign.xml`
  - `/home/runner/work/UA-.NETStandard/UA-.NETStandard/Tests/Opc.Ua.SourceGeneration.Core.Tests/Resources/CrossModelTypes.NodeSet2.xml`
- Dependency-handling paths are implemented in:
  - `/home/runner/work/UA-.NETStandard/UA-.NETStandard/Tools/Opc.Ua.SourceGeneration.Core/NodesetFile.cs`
  - `/home/runner/work/UA-.NETStandard/UA-.NETStandard/Tools/Opc.Ua.SourceGeneration.Core/Schema/ModelDesignValidator.cs`
  - `/home/runner/work/UA-.NETStandard/UA-.NETStandard/Tools/Opc.Ua.SourceGeneration.Core/Generators.cs`
  - `/home/runner/work/UA-.NETStandard/UA-.NETStandard/Tools/Opc.Ua.SourceGeneration.Core/Generators/IGeneratorContext.cs`

## Structured plan
1. Confirm scope from the linked issue comment and map it to one concrete failing scenario.
2. Reproduce the failure locally using a targeted source-generation test or a new focused fixture that mirrors the reported model layout.
3. Trace the exact resolution path (ModelDesign loading, dependency import, and TypeDefinition/DataType linking) to identify where the reported scenario diverges from existing #3937 coverage.
4. Implement the minimal fix in the source-generation core path where resolution currently breaks.
5. Add or extend regression tests so the specific issue-comment scenario fails before the fix and passes after it.
6. Run targeted generator tests first, then broader affected test scope to confirm no regressions in existing cross-model generation behavior.
7. Summarize root cause, fix scope, and residual risks/edge cases for review.

## Validation plan
- Primary: `Opc.Ua.SourceGeneration.Tests` regression tests (including both existing #3937 tests).
- Secondary: related `Opc.Ua.SourceGeneration.Core.Tests` coverage for dependency import/linking paths.
- Ensure input-order independence and referenced-model dependency behavior remain intact.

## Clarifications needed
1. Should this plan target only the Roslyn source generator path, or also any standalone model generation tooling path if both are affected by the issue comment?
2. Do you want the final implementation scope limited strictly to the exact issue-comment repro, or should it include adjacent hardening for similar cross-model reference shapes?
3. If the issue comment includes additional sample files or exact diagnostics beyond `MODELGEN003`, should those be treated as required acceptance criteria?
