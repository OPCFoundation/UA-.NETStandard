## Security Enhancements for 1.05.7 Release

### Overview

Implements

- NIST p256 with AES-GCM and ChaCha20Poly1305;
- Chained Key Derivation after Renewal;
- Channel Bound Signatures for Server, Client and UserTokens

To implement
- Session Activation Tokens
- Other Curves
- RSA DH
- Verify new SequenceNumber model is used.

Known issues:
- Using RSA user certificates with ECC channel does not work;
- Encrypting passwords when Server does not have an RSA certificate does not work.

### Configuration
The server is configured to support ECC_nistP256_AES out of the box.  

Modify configuration file to try out different policies (easier to verify when only one policy enabled at a time).

SecurityTestClient loops through all available SecurityPolicies.

A powershell script  in SecurityTestClient folder that can create UserCertificates for testing.

The SecurityTokenLifetime = 60s and MaxBufferSize = 9000 bytes to trigger renewals and multiple chunks.
 
### Build and Run Default Tests
From root of repo:
```
dotnet build '.\UA Reference.sln' -c Debug
```

Create client certificate:
```
cd .\Applications\SecurityTestClient\bin\Debug\net8.0\
.\SecurityTestClient.exe
```

Copy client certificate to server trusted directory:
```
robocopy ".\pki\own\certs" "..\..\..\..\ConsoleReferenceServer\bin\Debug\net9.0\pki\trusted\certs" 
```

Start server (from repo root):
```
cd .\Applications\ConsoleReferenceServer\bin\Debug\net9.0\
.\ConsoleReferenceServer.exe
```

Should see:
```
.NET Core OPC UA Reference Server
OPC UA library: 1.5.377.12 @ 09/16/2025 03:17:56 -- 1.5.377.12-preview+a240d86fb7
Loading configuration from Quickstarts.ReferenceServer.
Check the certificate.
Start the server.
opc.tcp://whitecat:62541/
Server started. Press Ctrl-C to exit...
```

Start client  (from repo root):
```
cd .\Applications\SecurityTestClient\bin\Debug\net8.0\
.\SecurityTestClient.exe
```

Should see:
```
OPC UA Security Test Client
OPC UA library: 1.5.377.12 @ 09/16/2025 03:17:56 -- 1.5.377.12-preview+a240d86fb7
Connecting to... opc.tcp://localhost:62541
================================================================================
SECURITY-POLICY=ECC_nistP256_AES Sign
IDENTITY=sysadmin UserName
BadCertificateUntrusted 'Certificate is not trusted.'
```