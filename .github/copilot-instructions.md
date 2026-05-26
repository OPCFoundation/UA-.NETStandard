# OPC UA .NET Standard Stack - GitHub Copilot Instructions

## Project Overview

This is the official OPC UA .NET Standard Stack from the OPC Foundation. It provides a reference implementation of OPC UA (Open Platform Communications Unified Architecture) targeting .NET Standard, allowing development of applications that run on multiple platforms including Windows, Linux, iOS, and Android.

### Key Technologies
- **SDK**: .net 10 SDK
- **Language**: C# with LangVersion 14.0
- **Target Frameworks**: .NET Standard 2.0/2.1, .NET Framework 4.8, .NET 8.0 (LTS), .NET 9.0, .NET 10.0 (LTS)
- **Project Type**: Class libraries, console applications, and reference implementations
- **Architecture**: OPC UA Stack with Client, Server, Configuration, Complex Types, GDS, and PubSub components

## Build and Development

### Building the Project
- **Prerequisites**: .NET SDK 10.0
- **Restore dependencies**: `dotnet restore 'UA.slnx'`
- **Build**: Use Visual Studio 2026 or `dotnet build`
- **Key solutions**:
  - `UA.slnx` - Contains all projects

### Project Structure
- `Libraries/` - Core OPC UA libraries (Client, Server, Configuration, etc.)
- `Applications/` - Reference applications (ConsoleReferenceServer, etc.)
- `Tests/` - Unit and integration tests
- `Stack/` - Core stack implementation
- `Docs/` - Documentation files

### Build Properties
- `Directory.Build.props` - Central build properties
- `Directory.Packages.props` - Centralized package management
- Analysis level is set to "preview" with the "all" analysis mode
- .NET analyzers are enabled
- Package validation is enabled
- .editorconfig is enforced during build. Fix all errors, warnings, and informational messages. Do not suppress without marking the suppression with a comment explaining why and a TODO to fix later. 

## Core Concepts to Understand
- **Nodes and NodeManagers**: Understand the node structure and how to implement custom node managers
- **Sessions and Subscriptions**: Properly manage client sessions and subscriptions (including durable subscriptions)
- **Data Encoding**: Use the binary encoding/decoding system for OPC UA data types
- **Complex Types**: Support for complex type definitions and their serialization
- **Transport**: UA-TCP and HTTPS transports with reverse connect capability

## IMPORTANT RULES
- All new code should use Async/await (TAP), APM and synchronous to Async are not allowed. 
- DO NOT create SYNC over ASYNC (GetAwaiter().GetResult(), Wait(), Result) unless explicitly requested/confirmed.
- All types implementing INullable must never be used or added via the "System.Nullable<T>" (T?). Instead use .IsNull check on the type to determine whether it is null and the .Null or default to create a null value.
- ALWAYs use TryGet or TryGetValue, or similar on struct types over casting. NEVER use .AsBoxedValue or .Value of the Variant type. 
- DO NOT use [Obsolete] API.
- All new functionality added must wire into the DI infrastructure and be injectable.  Direct "create"/construct path should also be available as fallback.
- Consider making new functionality available using a fluent API (and ideally integrated into the existing API if it can extend it).
- Maintain compatibility with the previous version (1.5.378, master378 branch) in master, mark any replaced API with [Obsolete]. Already marked [Obsolete] API in 1.5.378 (master378) can be removed/replaced in master.
- DO REUSE existing features and concepts (docs/*) in new features, e.g. 
  - All source generated code, in particular ObjectType proxies should be used over manually calling service calls inside new clients.
  - Consider using the source generators to implement emitting "boilerplate", especially if it is related to the OPC UA standard (e.g. information model).
  - Base services: File System, Certificate manager, Secret store, State machine, Alarms and conditions Streaming subscription, Sessions, etc. in new code. (Documented in docs/*).
  - Observability is plumbed through via `ITelemetryContext`. Use it to create a `ILogger` for logging.
- Use modern C# and .net and add a polyfill to Opc.Ua.Types/Polyfills if API is not available on older platforms.  
- Use new "extension" keyword when you need to define "static" or "property" extensions. Use old style extension methods for the rest.
- Support the latest version of the OPC UA specifications (1.05.07 for OPC UA core)

### Code Style and Standards
- Add the OPC Foundation MIT license header to all source files.
- NEVER use #region/#endregion directives. Remove them when you encounter them.
- ALWAYS add a line break after a statement ending with `;`
- ALWAYS Follow the `.editorconfig` settings *strictly*. Fix all warnings, errors and informational messages before proposing a fix.
  - Use 4 spaces for indentation in C# files
  - Maximum line length: 120 characters
  - End-of-line: CRLF
  - Always insert final newline
  - Trim trailing whitespace
  - Use UTF-8 encoding
  - Order of members in classes, struct, records: Constructor, Properties and non-private Events, Methods, Fields. Each ordered from public->protected->internal->private.
  - **Braces**: Use Allman style (braces on new line for control blocks, types, and methods)
  - **Null-checking**: Use throw expressions and conditional delegate calls when appropriate but at minimum in all public API
  - **Access modifiers**: Always specify access modifiers explicitly.
  - **Code analysis**: All code must pass Roslyn analyzers (Roslynator.Analyzers and Roslynator.Formatting.Analyzers)
  - **Warnings**: Treat warnings as errors (`TreatWarningsAsErrors` is enabled)
- Follow standard C# naming conventions. Do not use underscores in method names.
- Assembly prefix: `Opc.Ua` (Except applications, or if otherwise requested)
- Package prefix: `OPCFoundation.NetStandard`

### Security Requirements
- **Never hardcode credentials, certificates, or secrets** in source code
- **Certificate Management**: All certificates must be managed through the certificate store system (see `Docs/CertificateManager.md`)
- **Secrets**: All secrets must be managed through the secret store system (see `Docs/CertificateManager.md`)
- **Security Profiles**: do not use hash algorithms other than SHA2 or higher.
- **Authentication**: Properly implement anonymous, username, and X.509 certificate user authentication
- **Audit and Redaction**: Use the audit and redaction APIs for sensitive information

### Testing Standards
- Use NUnit or TUNIT framework (match existing test projects)
  - DO NOT mix Nunit and TUNIT in the same project.
  - DO Use NUnit asserts methods (Assert.That).  Use the TUNIT assertions when using TUNIT. 
  - DO Use Moq for mocking in NUnit tests.  Use the TUNIT mock libraries for TUNIT tests.
  - DO NOT USE the classic Nunit asserts (E.g. Assert.AreEquals) or other libraries.
- All new features must include unit tests. Tests should be simple and cover positive and negative scenarios. Unit tests go into the corresponding <project>.Tests project
  - DO Maintain or improve code coverage 
  - All projects should have at least 80% coverage (exception Applications, and Test projects).
- All client/server as well as pub/sub features must also have Integration tests. Integration tests go into integration projects <component/area>.Tests.
- Tests must be deterministic and pass in CI/CD environment
- Code coverage is monitored via Coverlet and MUST NOT decrease
- Run all tests with `dotnet test` from solution root on UA.slnx
- When updating tests fix above for the test only.
- Mirror the structure of the code being tested
- Use descriptive test method names that explain what is being tested
- DO NOT use _ in test method names; use PascalCase

### Documentation

#### When to Update Documentation
- Add a new doc in `Docs/` when adding new features and link from `/docs/README.md`
- Review all other docs for consistency and when needed, link the new doc from other docs.
- When manual migration from 1.5.378 (master378) is required or code was marked [Obsolete], update migrationguide.md
- Update `/README.md` for significant changes
- Keep `NugetREADME.md` updated for package-related changes
- Document breaking changes prominently

#### Documentation Style
- Use clear, technical language
- Include code examples where appropriate. For new Client side features, always add code examples to show how to use the API and explain the developer experience.
- Reference OPC UA specifications when relevant
- Maintain consistent markdown formatting

### Dependencies and Package Management
- Use centralized package management (Directory.Packages.props)
- Audit packages for security vulnerabilities (NuGetAudit is enabled)
- Only add necessary dependencies and ask for approval for new dependencies. Most functionality is implemented within the stack itself so check the repo first. 
  - Prefer stable, well-maintained packages. 
  - Do not use packages with incompatible license (e.g. GPL, AGPL or commercial)
  - Check compatibility with all target frameworks
- Prefer AOT and trimmable packages over others.

### Contributing

#### Before Submitting Changes
- Ensure all tests pass
- Run code analysis and fix all warnings
- Follow the existing code structure and patterns
- Check that your changes don't break compatibility
- Review security implications of your changes
- Contributors must agree to the OPC Foundation CLA

#### Pull Request Guidelines
- Provide clear description of changes
- Reference any related issues
- Ensure CI/CD pipelines pass
- Use the opc-ua-codestyle-enforcer agent if needed to ensure compliance before opening pull requests.

## Common Tasks

### Adding New OPC UA Functionality
1. Review OPC UA specification for the feature using the online reference or appropriate tools available (e.g. MCP).
2. Implement in appropriate library/libraries (add new ones for new companion spec, or find the appropriate place in an existing one).
3. Add comprehensive tests (see test requirements)
4. Update documentation (see documentation requirements)
5. Consider backward compatibility (see earlier)

### Certificate Management
- NEVER commit certificates or secrets of any kind to the repository
- Use the certificate manager APIs
- Follow guidelines in `Docs/Certificates.md`
- Test with different certificate configurations

### Performance Considerations
- Be mindful of memory allocations in hot paths
- Use async/await properly to avoid blocking
- Consider thread safety
- Profile performance-critical code
- Avoid locking where possible. If needed use SemaphoreSlim to lock instead of lock keyword

## Resources

- OPC UA Specification: https://reference.opcfoundation.org/
- Documentation: See `Docs/` directory
- Samples Repository: https://github.com/OPCFoundation/UA-.NETStandard-Samples
- NuGet Packages:
  - Types: https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Types/
  - Core.Types: https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Core.Types/
  - Core: https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Core/
  - Client: https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Client/
  - Server: https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Server/
- Preview Feed: https://opcfoundation.visualstudio.com/opcua-netstandard/_packaging?_a=feed&feed=opcua-preview%40Local
