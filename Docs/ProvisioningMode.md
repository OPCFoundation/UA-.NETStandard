# Provisioning Mode

## Overview

Provisioning mode is a special server startup mode designed to facilitate secure certificate provisioning and initial server configuration. When enabled, the server starts with a limited default namespace and enhanced security settings to allow administrators to safely configure the server before exposing the full application functionality.

## Purpose

The provisioning mode addresses the need for secure server initialization by:
- Providing a minimal server environment during initial setup
- Requiring authenticated user access (anonymous access is disabled)
- Enabling automatic certificate acceptance to simplify certificate provisioning
- Preventing access to application-specific data and functionality until the server is properly configured

This mode is particularly useful when:
- Setting up a new OPC UA server installation
- Provisioning certificates for production environments
- Performing initial server configuration in a secure manner

## How to Enable Provisioning Mode

### ConsoleReferenceServer

The ConsoleReferenceServer application supports provisioning mode via the `--provision` command line option:

```bash
dotnet ConsoleReferenceServer.dll --provision
```

### Command Line Options

When using provisioning mode, you can combine it with other options:

```bash
# Enable provisioning mode with console logging
dotnet ConsoleReferenceServer.dll --provision --console --log

# Enable provisioning mode with timeout
dotnet ConsoleReferenceServer.dll --provision --timeout 300
```

Note: When provisioning mode is enabled, auto-accept for untrusted certificates is automatically activated. You don't need to specify the `--autoaccept` option separately.

## Behavior in Provisioning Mode

When the server starts in provisioning mode:

### 1. Limited Namespace
- Only the core OPC UA namespace is exposed
- Application-specific node managers are not loaded
- Standard OPC UA objects (Objects, Types, Views) remain accessible
- Custom server nodes and data are not available

### 2. Authentication Requirements
- Anonymous authentication is disabled
- Users must authenticate using:
  - Username/password
  - X.509 certificate
  - Other supported authentication mechanisms

### 3. Certificate Handling
- Auto-accept for untrusted certificates is enabled
- Administrators can provision certificates without manual approval
- Certificate validation still occurs, but untrusted certificates are accepted

### 4. Reduced Attack Surface
- No application-specific functionality is exposed
- Minimal node managers are active
- Only core server capabilities are available

## Use Cases

### Initial Server Setup
1. Start the server in provisioning mode
2. Connect as an authenticated administrator
3. Provision required certificates
4. Configure server settings
5. Restart the server in normal mode

### Certificate Provisioning
1. Start the server with `--provision`
2. Use a GDS (Global Discovery Server) or certificate management tool
3. Push certificates to the server
4. Verify certificate installation
5. Exit provisioning mode

### Secure Configuration
1. Enable provisioning mode for configuration changes
2. Make necessary updates to server configuration
3. Test changes in the limited environment
4. Restart in production mode

## Implementation Details

### ReferenceServer Class

The provisioning mode is implemented in the `ReferenceServer` class:

```csharp
public bool ProvisioningMode { get; set; }
```

When `ProvisioningMode` is true:
- `CreateMasterNodeManager` skips loading application-specific node managers
- `GetUserTokenPolicies` removes anonymous authentication policies

### Programmatic Usage

To enable provisioning mode programmatically:

```csharp
var server = new ReferenceServer();
Quickstarts.Servers.Utils.EnableProvisioningMode(server);
```

Or directly:

```csharp
var server = new ReferenceServer
{
    ProvisioningMode = true
};
```

## Security Considerations

### Benefits
- Reduces attack surface during initial setup
- Requires authenticated access for configuration
- Prevents unauthorized access to application data

### Limitations
- Auto-accept mode accepts all certificates (including potentially malicious ones)
- Should only be used during controlled setup/configuration periods
- Not intended for long-term operation

### Best Practices
1. Use provisioning mode only during initial setup or maintenance windows
2. Ensure the server is not accessible from untrusted networks while in provisioning mode
3. Switch to normal mode as soon as provisioning is complete
4. Monitor and review all certificates that were auto-accepted
5. Remove or properly validate any questionable certificates before exiting provisioning mode

## Comparison with Normal Mode

| Feature | Normal Mode | Provisioning Mode |
|---------|-------------|-------------------|
| Namespace | Full application namespace | Core namespace only |
| Node Managers | All configured | Core managers only |
| Anonymous Access | Allowed (if configured) | Disabled |
| Auto-Accept Certificates | Optional | Enabled |
| Application Data | Accessible | Not available |
| Security Admin Access | Available | Required |

## Examples

### Start Server in Provisioning Mode

Windows:
```cmd
ConsoleReferenceServer.exe --provision --console --log
```

Linux/Mac:
```bash
dotnet ConsoleReferenceServer.dll --provision --console --log
```

### Expected Output

When provisioning mode is active, you should see log messages similar to:

```
[INF] Enabling provisioning mode.
[INF] Auto-accept enabled for provisioning mode.
[INF] Server is in provisioning mode - limited namespace enabled.
[INF] MasterNodeManager.Startup - NodeManagers=2
```

The `NodeManagers=2` indicates only the core node managers are loaded (CoreNodeManager and SystemNodeManager), confirming the limited namespace is in effect.

## Related Documentation

- [Certificates](Certificates.md) - Certificate management and storage
- [RoleBasedUserManagement](RoleBasedUserManagement.md) - User authentication and authorization
