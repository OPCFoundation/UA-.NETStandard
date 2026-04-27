# CTT Test Runner

A .NET replacement for the OPC Foundation Compliance Test Tool (CTT) JavaScript host environment.
Executes the CTT's `.js` test scripts using the OPC UA .NET Standard stack and the [Jint](https://github.com/sebastienros/jint) JavaScript engine.

## Architecture

The CTT is a native C++ Qt5 application that uses Qt5Script (ECMAScript 5.1) to run ~3,848 compliance test scripts.
These scripts rely on ~250 C++ bound classes (`UaSession`, `UaNodeId`, `UaReadRequest`, etc.) that are compiled into the CTT executable.

This project **replaces the C++ host environment with .NET** so the same test scripts can run using the OPC UA .NET Standard stack:

```
CTT .js test scripts
        ↓
   Jint (ES5.1 engine)
        ↓
   C# host objects (UaSession → Opc.Ua.Client.Session)
        ↓
   OPC UA .NET Standard Stack
        ↓
   Server under test
```

## Prerequisites

1. **.NET 10.0 SDK** (or later)
2. **CTT installed** at `C:\Program Files\OPC Foundation\UA 1.05\Compliance Test Tool`
   (or specify `--ctt-dir` pointing to the directory containing `ServerProjects/Standard/`)
3. **A CTT project file** (`.ctt.xml`) with server URL and test node settings configured

## Usage

### List available conformance units
```bash
CttTestRunner --list --settings path/to/project.ctt.xml
```

### Run a single test script
```bash
CttTestRunner --settings path/to/project.ctt.xml \
  --file "C:\...\maintree\Discovery Services\Discovery Get Endpoints\Test Cases\001.js"
```

### Run all tests in a conformance unit
```bash
CttTestRunner --settings path/to/project.ctt.xml \
  --conformance-unit "Attribute Read"
```

### Export results to XML
```bash
CttTestRunner --settings path/to/project.ctt.xml \
  --conformance-unit "View Basic 2" \
  --result results.xml
```

### JSON output for agent/CI consumption
```bash
CttTestRunner --settings path/to/project.ctt.xml \
  --conformance-unit "Attribute Read" \
  --json
```

### Verbose logging (shows JS↔.NET calls)
```bash
CttTestRunner --settings path/to/project.ctt.xml \
  --file test.js --verbose
```

## CLI Options

| Option | Description |
|--------|-------------|
| `--settings <file>` | Path to `.ctt.xml` project file (required) |
| `--file <file>` | Run a specific `.js` test script |
| `--conformance-unit <name>` | Run all tests in a conformance unit |
| `--list` | List available conformance units |
| `--result <file>` | Write XML result file |
| `--json` | Emit JSON lines to stdout |
| `--ctt-dir <dir>` | CTT install directory |
| `--verbose` | Verbose JS↔.NET logging |

## How It Works

### JavaScript Host Environment

The runner provides these CTT APIs to JavaScript:

- **Ua* types** (~250 constructors): `UaReadRequest`, `UaNodeId`, `UaVariant`, `UaDataValue`, etc.
- **Session methods**: `session.read()`, `session.write()`, `session.browse()`, `session.getEndpoints()`, etc.
- **Global functions**: `include()`, `addLog()`, `addError()`, `readSetting()`, `isDefined()`, `print()`
- **Enumerations**: `TimestampsToReturn`, `MonitoringMode`, `BrowseDirection`, `Attribute`, `StatusCode`, `SecurityPolicy`, etc.
- **Helpers**: `Assert.*`, `Test.Execute()`, `MonitoredItem.fromSettings()`, `Settings.*`
- **Infrastructure**: `ServiceRegister`, `ExpectedAndAcceptedResults`, `KeyPairCollection`, `HostInfo`

### Test Execution Flow

1. Parse the `.ctt.xml` project file (Qt AbstractItemModelData format)
2. Create a Jint engine with all CTT APIs registered
3. Load the conformance unit's `initialize.js` (includes shared libraries, connects to server)
4. Execute the test script
5. Collect pass/fail/skip/error results from the test context

### Unimplemented API Telemetry

When a test accesses a CTT API that isn't implemented yet, the runner logs it:
```
⚠ Unimplemented APIs accessed during this test run:
  ⚠ session.addSubscriptionToThread
  ⚠ session.startThreadPublish
```

This drives incremental implementation of the API surface.

## Current Status

| Feature | Status |
|---------|--------|
| Project file parsing | ✅ Working |
| CTT library loading (include()) | ✅ Working |
| Ua* type constructors (~250) | ✅ Registered |
| Enumerations | ✅ Complete |
| OPC UA NodeId constants (Identifier.*) | ✅ Via reflection |
| Assert.* | ✅ Implemented |
| Settings/readSetting() | ✅ Working |
| MonitoredItem helpers | ✅ Implemented |
| session.read() | ✅ Implemented |
| session.write() | ✅ Implemented |
| session.browse() | ✅ Implemented |
| session.getEndpoints() | ✅ Implemented |
| session.findServers() | ✅ Implemented |
| Other session services | ⚠️ Stub (returns Good) |
| Full test execution | 🔄 In progress |

## Project Structure

```
CttTestRunner/
├── Program.cs                          # CLI entry point
├── TestRunner.cs                       # Test execution engine  
├── TestResults.cs                      # Result collection + XML export
├── CttTestRunner.csproj                # Project file
├── CttTestRunner.Config.xml            # OPC UA client configuration
├── Runtime/
│   ├── CttHostEnvironment.cs           # JS host environment
│   ├── CttTestContext.cs               # Per-test state tracking
│   ├── CttGlobals.cs                   # isDefined(), etc.
│   ├── CttEnumerations.cs             # All OPC UA enums for JS
│   ├── CttAssert.cs                    # Assert.* methods
│   ├── CttServiceRegister.cs          # Service tracking
│   ├── Types/
│   │   ├── CttUaSession.cs            # Session wrapper (Read/Write/Browse/...)
│   │   ├── CttTypeFactory.cs          # Registers all Ua* constructors
│   │   ├── CttIdentifierConstants.cs  # NodeId constants via reflection
│   │   └── CttMonitoredItemHelper.cs  # MonitoredItem factory methods
│   └── Settings/
│       └── CttProjectSettings.cs       # .ctt.xml parser
└── README.md
```
