; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
MODELGEN001 | ModelSourceGenerator | Error | ModelSourceGenerator
MODELGEN002 | ModelSourceGenerator | Warning | ModelSourceGenerator
MODELGEN003 | ModelSourceGenerator | Error | ModelSourceGenerator
MODELGEN010 | ModelSourceGenerator | Warning | [NodeManager] attribute binding error
MODELGEN011 | ModelSourceGenerator | Error | [NodeManager] class must be partial
MODELGEN012 | ModelSourceGenerator | Info | Multiple referenced assemblies expose same model
MODELGEN013 | ModelSourceGenerator | Info | Model already provided by referenced assembly
MODELGEN020 | ModelSourceGenerator | Warning | BrowseName requires C# string-literal escaping (UASG_BROWSENAME_UNSAFE)
MODELGEN021 | ModelSourceGenerator | Error | [DataType] namespace could not be resolved
MODELGEN030 | ModelSourceGenerator | Error | WoT model could not be parsed
MODELGEN031 | ModelSourceGenerator | Error | WoT model could not be converted to a NodeSet2 model
MODELGEN032 | ModelSourceGenerator | Warning | WoT model conversion produced a warning
MODELGEN033 | ModelSourceGenerator | Info | WoT model conversion note
MODELGEN034 | ModelSourceGenerator | Error | WoT model virtual NodeSet2 path collides with another input
