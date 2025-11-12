# OPC UA .NET Standard Stack - GitHub Copilot Instructions

## Project Overview

This is the official OPC UA .NET Standard Stack from the OPC Foundation. It provides a reference implementation of OPC UA (Open Platform Communications Unified Architecture) targeting .NET Standard, allowing development of applications that run on multiple platforms including Windows, Linux, iOS, and Android.

### Key Technologies
- **Language**: C# with LangVersion 13.0
- **Target Frameworks**: .NET Standard 2.0/2.1, .NET Framework 4.8, .NET 8.0 (LTS), .NET 9.0, .NET 10.0 (LTS)
- **Project Type**: Class libraries, console applications, and reference implementations
- **Architecture**: OPC UA Stack with Client, Server, Configuration, Complex Types, GDS, and PubSub components

## Code Style and Standards

### General Guidelines
- Follow the `.editorconfig` settings strictly
- Use 4 spaces for indentation in C# files
- Maximum line length: 120 characters
- End-of-line: CRLF
- Always insert final newline
- Trim trailing whitespace
- Use UTF-8 encoding
- Defined in .editorconfig
- Add the OPC Foundation MIT license header to all source files. Exception: All files in Opc.Ua.Core project which have a dual license header.

### C# Conventions
- **Braces**: Use Allman style (braces on new line for control blocks, types, and methods)
- **Null-checking**: Use throw expressions and conditional delegate calls when appropriate
- **Access modifiers**: Always specify access modifiers explicitly
- **Code analysis**: All code must pass Roslyn analyzers (Roslynator.Analyzers and Roslynator.Formatting.Analyzers)
- **Warnings**: Treat warnings as errors (`TreatWarningsAsErrors` is enabled)

### Naming Conventions
- Follow standard C# naming conventions
- Defined in .editorconfig
- Assembly prefix: `Opc.Ua`
- Package prefix: `OPCFoundation.NetStandard`

## Security Requirements

### Critical Security Practices
- **Never hardcode credentials, certificates, or secrets** in source code
- **Certificate Management**: All certificates must be managed through the certificate store system (see `Docs/Certificates.md`)
- **Security Profiles**: Support SHA-2 (up to SHA512), ECC profiles (NIST & Brainpool), and modern encryption standards
- **Authentication**: Properly implement anonymous, username, and X.509 certificate user authentication
- **Audit and Redaction**: Use the new audit and redaction interfaces for sensitive information
- Report security vulnerabilities to securityteam@opcfoundation.org
- Reference security bulletins at https://opcfoundation.org/security-bulletins/

### OPC UA Specific Security
- Always implement proper encryption for UA-TCP and HTTPS transports
- Support required security policies: Basic256Sha256, Aes128Sha256RsaOaep, Aes256Sha256RsaPss
- Implement role-based access control using WellKnownRoles when applicable

## Testing Standards

### Test Requirements
- All new features must include unit tests
- Use NUnit framework (match existing test projects)
- Test projects follow naming convention: `<Component>.Tests`
- Tests must be deterministic and pass in CI/CD environment
- Code coverage is monitored via Coverlet
- Run tests with `dotnet test` from solution root
- Use NUnit asserts methods, no other library or classic Nunit asserts.

### Test Organization
- Place tests in the `Tests/` directory
- Mirror the structure of the code being tested
- Use descriptive test method names that explain what is being tested
- Do not use _ in test method names; use PascalCase

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
- Analysis level is set to "preview" with "all" analysis mode
- .NET analyzers are enabled
- Package validation is enabled
- .editorconfig is enforced during build. Fix all errors, warnings, and informational messages. Do not suppress without marking the suppression with a comment explaining why and a TODO to fix later.

## OPC UA Specific Guidelines

### Core Concepts to Understand
- **Nodes and NodeManagers**: Understand the node structure and how to implement custom node managers
- **Sessions and Subscriptions**: Properly manage client sessions and subscriptions (including durable subscriptions)
- **Data Encoding**: Use the binary encoding/decoding system for OPC UA data types
- **Complex Types**: Support for complex type definitions and their serialization
- **Transport**: UA-TCP and HTTPS transports with reverse connect capability

### Key Features
- Support for OPC UA 1.05 specification
- All new code should use Async/await (TAP), APM and synchronous to Async are not allowed.
- Observability is plumbed through via `ITelemetryContext`. Use it to create a `ILogger` for logging.

## Documentation

### When to Update Documentation
- Update relevant docs in `Docs/` when adding new features
- Update `README.md` for significant changes
- Keep `NugetREADME.md` updated for package-related changes
- Document breaking changes prominently

### Documentation Style
- Use clear, technical language
- Include code examples where appropriate
- Reference OPC UA specifications when relevant
- Maintain consistent markdown formatting

## Dependencies and Package Management

### Package Guidelines
- Use centralized package management (Directory.Packages.props)
- Audit packages for security vulnerabilities (NuGetAudit is enabled)
- Only add necessary dependencies
- Prefer stable, well-maintained packages
- Check compatibility with all target frameworks

### Key Dependencies
- System.* packages for .NET functionality
- Avoid unnecessary external dependencies
- Most functionality is implemented within the stack itself

## Contributing

### Before Submitting Changes
- Ensure all tests pass
- Run code analysis and fix all warnings
- Follow the existing code structure and patterns
- Check that your changes don't break compatibility
- Review security implications of your changes
- Contributors must agree to the OPC Foundation CLA

### Pull Request Guidelines
- Provide clear description of changes
- Reference any related issues
- Ensure CI/CD pipelines pass
- Be responsive to code review feedback

## Common Tasks

### Adding New OPC UA Functionality
1. Review OPC UA specification for the feature
2. Implement in appropriate library (Client, Server, or Core)
3. Add comprehensive tests
4. Update documentation
5. Consider backward compatibility

### Certificate Management
- Never commit certificates to the repository
- Use the certificate store APIs
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
  - Core: https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Core/
  - Client: https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Client/
  - Server: https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Server/
- Preview Feed: https://opcfoundation.visualstudio.com/opcua-netstandard/_packaging?_a=feed&feed=opcua-preview%40Local
