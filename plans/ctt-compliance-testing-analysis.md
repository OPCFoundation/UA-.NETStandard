# OPC UA Compliance Test Tool (CTT) — Testing Analysis & Remediation Plan

**Date:** 2026-04-27  
**Server Under Test:** ConsoleReferenceServer (UA-.NETStandard, branch `asfixes`)  
**CTT Version:** 1.05.06-01.00.513  
**Server Port:** `opc.tcp://localhost:48010/Quickstarts/ReferenceServer`

---

## 1. Executive Summary

This document captures findings from attempting to run the OPC Foundation Compliance Test Tool (CTT) 
against the ConsoleReferenceServer. Headless CLI execution (`--file`, `--selection`) was investigated 
but consistently fails due to CTT initialization requirements that need interactive GUI setup. 
The report documents the CTT architecture, the setup process, current blockers, and a plan to 
achieve automated compliance testing.

---

## 2. CTT Architecture

### 2.1 Components

| Component | Type | Purpose |
|-----------|------|---------|
| `uacompliancetest.exe` | Native C++ (Qt5, 41MB) | Main GUI + CLI test runner |
| `Qt5Script.dll` | Qt5 JavaScript Engine | Executes .js test scripts |
| `AmlConsoleTestApplication.exe` | .NET 6.0 | AutomationML validator (NOT for UA tests) |
| `openssl.exe` | Native | Certificate generation for PKI |
| `create_ctt_pki.bat` | Batch | Generates ~90 test certificates |

### 2.2 JavaScript Test API

The CTT exposes **~250+ C++ classes** to JavaScript via Qt5Script bindings:

- **Core types:** `UaSession`, `UaChannel`, `UaNodeId`, `UaVariant`, `UaDataValue`, `UaStatusCode`
- **Service requests/responses:** `UaReadRequest`/`UaReadResponse`, `UaBrowseRequest`/`UaBrowseResponse`, etc.
- **Helpers:** `ReadHelper`, `WriteHelper`, `BrowseHelper`, `CallHelper`, `PublishHelper`, `SubscriptionHelper`
- **Infrastructure:** `Test.Execute()`, `MonitoredItem.fromSettings()`, `Settings.*`, `Assert.*`
- **Global functions:** `include()`, `addLog()`, `addError()`, `addWarning()`, `readSetting()`, `isDefined()`

### 2.3 Test Script Structure

Test scripts use CTT-specific JavaScript:
```javascript
// Typical test pattern (001.js)
function testName() {
    var items = MonitoredItem.fromSettings(Settings.ServerTest.NodeIds.Static.AllProfiles.Scalar.Settings, ...);
    return ReadHelper.Execute({ NodesToRead: items[0], TimestampsToReturn: TimestampsToReturn.Server });
}
Test.Execute({ Procedure: testName });
```

**Key dependency:** Scripts CANNOT run outside the CTT. They depend on C++ bindings compiled 
into the 41MB native executable. There is no standalone JavaScript runtime that can execute them.

### 2.4 Session Methods Exposed to JS

The `UaSession` object exposes these OPC UA service calls:
`activateSession`, `addNodes`, `addReferences`, `browse`, `browseNext`, `call`, `cancel`, 
`clearModelCache`, `clearPublishData`, `closeSession`, `createMonitoredItems`, `createSession`, 
`createSubscription`, `deleteMonitoredItems`, `deleteNodes`, `deleteReferences`, `deleteSubscriptions`, 
`findServers`, `historyRead`, `historyUpdate`, `modifyMonitoredItems`, `modifySubscription`, 
`publish`, `queryFirst`, `queryNext`, `read`, `registerNodes`, `registerServer`, `republish`, 
`translateBrowsePathsToNodeIds`, `unregisterNodes`, `write`

---

## 3. Test Inventory

| Category | Test Scripts | Description |
|----------|-------------|-------------|
| OPC UA FX | 561 | Field Exchange conformance |
| Security General | 420 | Security policy/mode tests |
| Base Information | 404 | Information model validation |
| Subscription Services | 243 | Subscription lifecycle |
| Monitored Item Services | 238 | Monitored item management |
| Alarms and Conditions | 207 | A&C conformance |
| Historical Access | 177 | HA read/update |
| GDS | 158 | Global Discovery Server |
| Data Access | 150 | DA profile compliance |
| View Services | 136 | Browse/TranslatePath |
| Attribute Services | 133 | Read/Write attributes |
| Auditing | 107 | Audit event generation |
| Address Space Model | 96 | Type system validation |
| Security User Token | 94 | User authentication |
| Discovery Services | 75 | FindServers/GetEndpoints |
| Miscellaneous | 65 | Other tests |
| Base Services | 62 | Diagnostics, general behavior |
| Session Services | 40 | Session lifecycle |
| **Total** | **3,848** | |

---

## 4. CLI Execution Modes

### 4.1 Available Flags

| Flag | Short | Description | Mode |
|------|-------|-------------|------|
| `--close` | `-c` | Close after tests complete | Server/Client |
| `--hidden` | `-h` | Don't show GUI window | Client |
| `--settings <file>` | `-s` | CTT project to open | Server/Client |
| `--selection <file>` | `-l` | Checked profiles/CU/tests | Server |
| `--result <file>` | `-r` | Where to store results | Server |
| `--file <file>` | `-f` | Specific test script | Server/Client |
| `--keepclienttrace` | `-k` | Keep client trace on exit | Server |

### 4.2 Exit Codes

| Code | Meaning |
|------|---------|
| `0` | OK — no errors or warnings |
| `1` | Warning — one or more warnings |
| `-1` | Error — one or more errors detected |

### 4.3 Observed Behavior

| Command | Duration | Exit | Result File | Analysis |
|---------|----------|------|-------------|----------|
| `--close --settings P --file F` | 1.4s | -1 | None | CTT init fails, no test runs |
| `--close --settings P --selection S` | 5-8s | 0 | None | No tests matched selection |
| `--settings P` (GUI only) | stays open | n/a | n/a | GUI opens successfully |
| `--settings P --file F` (no --close) | exits | -1 | None | Same init failure |

**Root cause of `--file` failure:** The CTT requires interactive PKI initialization 
(certificate creation dialogs) on first run with a project. In `--close`/`--file` mode, 
it cannot display these dialogs and fails with exit code -1.

**Root cause of `--selection` returning 0:** The selection XML format must be generated by 
the CTT GUI ("Project → Save Selection State"). Hand-crafted selection files don't match 
the CTT's internal conformance unit mapping structure.

---

## 5. PKI Setup

### 5.1 Certificate Generation

The CTT generates ~90 test certificates using `create_ctt_pki.bat`:

```
PKI/CA/certs/     - 90 DER certificates (app, user, CA, expired, wrong-sig, etc.)
PKI/CA/private/   - 90 PEM private keys
PKI/CA/crl/       - Certificate revocation lists
PKI/copyToServer/ - 66 certs to copy to server's trust store
  ApplicationInstance_PKI/trusted/certs/ - 29 CTT app certs
  ApplicationInstance_PKI/issuers/certs/ - 3 CA certs  
  X509UserIdentity_PKI/trusted/certs/   - 23 user certs
```

### 5.2 Server Trust Configuration

The server (`--autoaccept`) automatically trusts CTT certificates. The CTT needs the 
server's certificate in its own trust store — normally handled via an interactive trust 
dialog that can't be displayed in headless mode.

### 5.3 Certificate Status

| Store | Location | Status |
|-------|----------|--------|
| CTT PKI (project) | `TestResults/PKI/` | ✅ 254 files generated |
| CTT PKI (install) | `C:\...\ServerProjects\PKI/` | ✅ 254 files |
| Server trusted | `%ProgramData%\OPC Foundation\pki\trusted\certs\` | ✅ 29 CTT certs |
| Server issuers | `%ProgramData%\OPC Foundation\pki\issuers\certs\` | ✅ 3 CA certs |
| Server user trusted | `%ProgramData%\OPC Foundation\pki\user\trusted\certs\` | ✅ 23 user certs |

---

## 6. Project Configuration

### 6.1 Project File Issues

The repository's project file (`Applications/UAReferenceServer.ctt.xml`) has two issues:

1. **Missing `specificationversion`:** No `<ProjectInfo>` element, causing CTT to default 
   to "UACore version 1.00" which doesn't match any available ProfileSet (1.03/1.04/1.05).
   
2. **Port mismatch:** References port 62541 (blocked on this machine), needs 48010.

**Fix applied:** Added `<ProjectInfo ProjectType="ServerProject" specificationversion="1.05.06"/>` 
and changed port to 48010.

### 6.2 ProfileSet Loading

Available ProfileSets in `TestResults/Profiles/`:
- `ProfileSet_UACore_1.03_2025-11-09.xml` (469KB)
- `ProfileSet_UACore_1.04_2025-11-09.xml` (774KB)  
- `ProfileSet_UACore_1.05_2025-12-16.xml` (929KB) — **Current target**
- `ProfileSet_DI_1.04/1.05` and `ProfileSet_UAFX_1.00`

The 1.05 ProfileSet contains **1,159 conformance units** mapped to the 3,848 test scripts.

---

## 7. Remediation Plan

### 7.1 Critical: Complete Interactive CTT Setup (Priority 1)

**Goal:** Get CTT running end-to-end through the GUI so --selection mode works.

**Steps:**
1. Start CTT GUI: `uacompliancetest.exe --settings UAReferenceServer-48010.ctt.xml`
2. Dismiss startup wizard (may already be disabled via INI)
3. If "PKI folder does not exist" → Click Yes
4. If "Create certificates?" → Accept with KeyLength=2048
5. Wait for certificate generation to complete (~60s)
6. Navigate to Conformance Units tab
7. Check desired test categories (start with "Attribute Read", "View Basic 2", "Discovery Get Endpoints")
8. Use "Project → Save Selection State" to generate `.selection.xml`
9. Close CTT
10. Test headless: `uacompliancetest.exe --close --settings X --selection Y --result Z`

**Blocker:** Requires interactive access to the CTT GUI (Windows UI automation or manual).

### 7.2 High: Create Automated CTT Test Pipeline (Priority 2)

**Goal:** CI/CD integration for compliance testing.

**Prerequisites:** 
- Step 7.1 completed (selection file generated)
- CTT installed on CI agent (or use Docker with Windows container)

**Pipeline:**
```yaml
steps:
  - name: Start Reference Server
    run: |
      dotnet run --project Applications/ConsoleReferenceServer -- --ctt --autoaccept &
      sleep 5
      
  - name: Run CTT Tests
    run: |
      $ctt = "C:\Program Files\OPC Foundation\UA 1.05\Compliance Test Tool\uacompliancetest.exe"
      & $ctt --close --hidden --settings Applications/UAReferenceServer.ctt.xml `
             --selection Applications/UAReferenceServer.selection.xml `
             --result TestResults/ctt-results.xml
      if ($LASTEXITCODE -eq -1) { Write-Error "CTT tests failed" }
```

### 7.3 Medium: Fix Repository Project File (Priority 3)

**File:** `Applications/UAReferenceServer.ctt.xml`

**Changes needed:**
1. Add `<ProjectInfo>` with correct `specificationversion="1.05.06"`
2. Update documentation in `Applications/README.md` with setup instructions
3. Commit generated `.selection.xml` for CI reproducibility
4. Update port configuration to be parameterizable

### 7.4 Medium: Alternative Testing with MCP Server (Priority 4)

**Goal:** Use our OPC UA MCP server for programmatic compliance testing independent of CTT.

The MCP server already implements all 42 OPC UA service tools. We can create a 
test harness that replicates CTT test logic:

| CTT Test Category | MCP Tool | Status |
|-------------------|----------|--------|
| Discovery GetEndpoints | `GetEndpoints` | ✅ Verified |
| Attribute Read | `Read` | ✅ Verified |
| Attribute Write | `Write` | ✅ Verified |
| View Browse | `Browse`, `BrowseNext` | ✅ Verified |
| View TranslateBrowsePath | `TranslateBrowsePathsToNodeIds` | ✅ Verified |
| Method Call | `Call` | ✅ Verified |
| Subscription | `CreateSubscription`, `CreateMonitoredItems`, `Publish` | ✅ Verified |
| Session | `CreateSession`, `ActivateSession`, `CloseSession` | ✅ Verified |
| Node Management | `AddNodes`, `DeleteNodes` | ⚠️ Server limitation |
| Historical Access | `HistoryRead`, `HistoryUpdate` | ⚠️ Server limitation |

### 7.5 Low: Install ASP.NET Core 6.0 for AmlConsoleTestApplication (Priority 5)

The `AmlConsoleTestApplication.exe` is an AutomationML validator, **not** a test runner.
It is NOT needed for compliance testing. This is documented here to prevent future confusion.

---

## 8. Known Issues from Prior Testing (MCP Server)

From our MCP server interoperability testing (42 tools tested), the following issues were found:

### Working (34/42 tools):
All core services (Read, Write, Browse, Call, Subscribe, Publish, etc.) work correctly.

### Server Limitations (8/42 tools):
- `AddNodes` / `DeleteNodes` — "BadNotImplemented" (server doesn't support dynamic node management)
- `HistoryRead` / `HistoryUpdate` — "BadHistoryOperationUnsupported" (no historian configured)
- `QueryFirst` / `QueryNext` — "BadServiceUnsupported"
- `RegisterServer` — "BadNotImplemented"
- `Call` with some methods — Return "BadInvalidArgument" for complex type parameters

### Source Generator Issues (Fixed):
- **Encoding nodes missing** — Fixed: `CollectEncodingNodes()` added to NodeStateGenerator
- **EventNotifier not set** — Fixed: Inferred from HasEventSource/HasNotifier references
- **ModellingRule=None children missing** — Fixed: State machine states/transitions now emitted
- **RBAC on type definitions** — Fixed: `if(forInstance)` guards added

---

## 9. Appendix

### A. CTT INI Configuration

**Path:** `%APPDATA%\OPC Foundation\OPC Unified Architecture Compliance Test Tool.ini`

Key settings:
```ini
[MainWindow]
ShowStartupDialog=false    # Must be false for headless operation
ServerTestGuiHasBeenStarted=true
```

### B. Server Launch Command

```bash
dotnet run --project Applications/ConsoleReferenceServer -- --ctt --autoaccept
```

The `--ctt` flag configures the server for compliance testing (enables additional test nodes,
configures user authentication, etc.). The `--autoaccept` flag auto-trusts all client certificates.

### C. Certificate Generation Command

```bash
cd "C:\Program Files\OPC Foundation\UA 1.05\Compliance Test Tool"
create_ctt_pki.bat 2048 "D:\git\UA-.NETStandard\TestResults" 1
```

Parameters: `<KeySize> <ProjectDir> <IsProjectDir(1=yes)>`

### D. File Locations

| File | Purpose |
|------|---------|
| `Applications/UAReferenceServer.ctt.xml` | CTT project file (repo) |
| `TestResults/UAReferenceServer-48010-clean.ctt.xml` | Modified project (port 48010) |
| `TestResults/PKI/` | Generated CTT certificates (254 files) |
| `TestResults/Profiles/` | ProfileSet XML files |
| `plans/missing-nodes-analysis.md` | Address space analysis |
| `plans/filtered-missing-nodes.md` | Filtered analysis (admin auth) |
