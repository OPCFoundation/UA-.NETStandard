# Running the CTT against the reference server

How to run the OPC UA Compliance Test Tool (CTT) headless against
`ConsoleReferenceServer`, and how to triage the results. Findings that are CTT
script or configuration defects are recorded in [ctt-issues.md](ctt-issues.md).
Check that file before reporting a failure, and add new CTT defects there.

## Setup

Placeholders used below:

| Placeholder | Meaning |
| --- | --- |
| `<CttDir>` | CTT installation folder, containing `uacompliancetest.exe`. Usually `...\OPC Foundation\UA 1.05\Compliance Test Tool`. |
| `<ProjectDir>` | folder of a CTT **server** project (created in the CTT GUI) |
| `<Project>` | project name. The project files are `<Project>.ctt.xml`, `<Project>.selection.xml` and `<Project>.results.xml`. |
| `<LogDir>` | any scratch folder for logs, selections and results |

Inside `<ProjectDir>`:

| Item | Location |
| --- | --- |
| Script catalog | `testscripts.xml`, `maintree\` (test scripts), `library\` (helpers) |
| CU id → name map | `Profiles\UACore\ProfileSet_UACore_1.05_*.xml` |

Configure the project once in the GUI:
- **Server URL:** `opc.tcp://localhost:62541/Quickstarts/ReferenceServer`.
- **Node settings:** the HAProfile, aggregate, alarm and user settings for the reference
  server in CTT mode.
- **Certificate trust:** the CTT client certificate lives under `<ProjectDir>\PKI`.

## 1. Build and start the server

```powershell
dotnet build samples\Reference\ConsoleReferenceServer\ConsoleReferenceServer.csproj -c Release -f net10.0 -p:CustomTestTarget=net10.0
cd samples\Reference\ConsoleReferenceServer\bin\Release\net10.0
.\ConsoleReferenceServer.exe --ctt -a -c *> <LogDir>\refserver.log
```

- `--ctt` loads the `Ctt.ReferenceServer` configuration section
  (`Ctt.ReferenceServer.Config.xml`), and `ApplyCTTModeAsync` starts the CTT alarms and
  other presets. Always use it; without it many CUs fail for configuration reasons.
- `-a` auto-accepts the CTT client certificate. Without it, the first session is
  rejected unless the CTT cert is in the server's trusted store. Do not use `-a` for the security
  groups (section 8).
- `-c` logs to the console. Redirect to a file so you can correlate server errors with
  CTT timestamps. `-l`/`-f` also write an app log file.
- The server is ready when it prints `Server started (... ms)`; allow about 10 s. Run it
  as a background task and wait until port 62541 is listening:
  `Get-NetTCPConnection -LocalPort 62541 -State Listen`.
- Restart the server before every run. History, alarm and node-management tests change
  server state, and a reused server produces misleading follow-on failures.
- Before a comparison run, confirm the build commit: the server banner prints
  `OPC UA library: ... +<sha>`.

## 2. Run the CTT from the command line

```powershell
$p = Start-Process -FilePath "<CttDir>\uacompliancetest.exe" `
  -ArgumentList @('--close','--hidden',
                  '--settings','"<ProjectDir>\<Project>.ctt.xml"',
                  '--selection','"<LogDir>\My.selection.xml"',
                  '--result','"<LogDir>\My.results.xml"') `
  -WorkingDirectory "<CttDir>" `
  -PassThru -Wait
$p.ExitCode   # -1 = errors, 0 = OK, 1 = warnings only
```

| Flag | Meaning |
| --- | --- |
| `-s`, `--settings <file>` | CTT project to open (required) |
| `-l`, `--selection <file>` | checked profiles/CUs/test cases. Omit it to run the project's saved selection. |
| `-r`, `--result <file>` | results file. Omit it and the project's `<Project>.results.xml` is overwritten. |
| `-c`, `--close` | exit when finished (required for automation) |
| `-h`, `--hidden` | no GUI window |
| `-f`, `--file <script>` | run one specific script (mainly client testing) |

Things to know:

- `uacompliancetest.exe` is a GUI binary. Use `Start-Process -Wait -PassThru` to wait
  for it and read its exit code. Stdout and stderr stay empty; everything goes to the
  results XML.
- The CTT **rewrites the selection file** you pass on exit. Pass a copy you don't mind
  losing.
- The CTT also re-saves the project (`*.ctt.xml`, `testscripts.xml`, `libmodel.xml`) on
  exit. That is normal.
- Run it as a background task. A 4-test smoke run takes about 30 s. The full history
  selection (1,265 cases) takes about 1.5–2 minutes.
- **Results accumulate.** A results file that already exists gets a new top-level
  `ResultNode name="Debug RunN"` appended. Always analyze the last run node, not the
  whole file.
- The results file is written only when the run ends. Wait with a timeout instead of `-Wait`
  (`$p.WaitForExit($ms)`, then `Stop-Process` on timeout): scripts that open a message box still
  do so with `--hidden` and wait for input forever (A & C CertificateExpiration, see
  [ctt-issues.md](ctt-issues.md) C44). A killed run leaves no results, so split long selections.
- Only one CTT instance should talk to the server at a time. Check with
  `Get-Process uacompliancetest` before starting another.
- Full CTT documentation: `<CttDir>\help\command_line_interface.htm`.

## 3. Build a selection file

A selection file lists **conformance group → conformance unit → test case names**.
The names must match the CTT's names exactly, including en dashes (`Aggregate – Average`)
and trailing spaces (`Attribute Historical Read `). `initialize.js` and `cleanup.js`
run automatically; don't list them.

```xml
<UaTestCaseSelection xsi:noNamespaceSchemaLocation="testcaseselection.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
 <ProjectInfo ProjectType="ServerProject" specificationversion="1.05.006" binaryversion="2.00.000+002" scriptversion="1.05.513"/>
 <ConformanceGroups>
  <ConformanceGroup name="Address Space Model">
   <ConformanceUnit name="Address Space Base">
    <TestCases>
     <TestCase name="001.js"/>
    </TestCases>
   </ConformanceUnit>
  </ConformanceGroup>
 </ConformanceGroups>
</UaTestCaseSelection>
```

Copy the `ProjectInfo` versions from `<ProjectDir>\testscripts.xml`.

Don't derive CU names from script paths. Many CUs reuse another CU's scripts; all 37
`Aggregate – *` CUs point at `Aggregates/Aggregate - Base/...`. Resolve names by id
instead. `testscripts.xml` lists `ConformanceUnit id=...` with its test cases, and the
UACore profile set maps that id to `Name` and `ConformanceGroup`. This generator builds
a selection for every CU whose group or name matches a filter:

```powershell
$r = "<ProjectDir>"
$p = [xml](Get-Content (Get-ChildItem "$r\Profiles\UACore\ProfileSet_UACore_1.05_*.xml")[0].FullName -Raw)
$cuName=@{}; $cuGroup=@{}; $grpName=@{}
foreach ($n in $p.SelectNodes("//*[local-name()='ConformanceGroup'][@Id]")) { $grpName[$n.Id] = $n.Name }
foreach ($n in $p.SelectNodes("//*[local-name()='ConformanceUnit'][@Id and @Name]")) {
  $cuName[$n.Id] = $n.Name
  $g = $n.SelectSingleNode("*[local-name()='ConformanceGroup']"); if ($g) { $cuGroup[$n.Id] = $g.InnerText } }
$x = [xml](Get-Content "$r\testscripts.xml" -Raw)
$filter = { param($g, $u) $g -in 'Historical Access','Aggregates' -or $u -match 'Hist' }   # <- adjust
$map = [ordered]@{}
foreach ($cu in $x.UaTestScripts.ConformanceUnits.ConformanceUnit) {
  $tcs = @($cu.TestCases.TestCase) | Where-Object { $_ }
  if (-not $tcs -or -not $cuName.ContainsKey($cu.id)) { continue }
  $u = $cuName[$cu.id]; $g = $grpName[$cuGroup[$cu.id]]
  if (& $filter $g $u) { $map["$g`t$u"] = @($tcs | ForEach-Object name) } }
# Write $map as <ConformanceGroup>/<ConformanceUnit>/<TestCase> elements (UTF-8, no BOM).
```

The **full history** selection is the filter above: every CU in *Historical Access* and
*Aggregates*, plus every CU with `Hist` in its name (Attribute Historical Read/Update,
Auditing History Services, Base Info History * Capabilities). With scripts 1.05.513
that's 70 CUs and 1,265 test cases. Most Historical Access CUs other than Read Raw have
one placeholder case.

## 4. Read the results

The results XML (`UaCttResults`) is a tree of `ResultNode` elements. Test-case nodes
carry `groupkey` and `unitkey`, and their children are the individual messages. The
`testresult` attribute:

| Code | Meaning |
| --- | --- |
| 0 | Error (`name="Error"` message, or a test case that failed) |
| 1 | Warning |
| 2 | Recommendation / inconclusive |
| 3 | Not implemented (manual test) |
| 4 | Skipped (usually missing configuration) |
| 5 | Not supported (the server reports it doesn't support the feature) |
| 6 | Pass |
| 7 | Backtrace entry (`filename`, `linenumber`) that follows an Error/Warning |

Each Error message is followed by Backtrace nodes. The first backtrace entry inside
`maintree/` is the failing line of the test. Entries in `library/` show which helper
raised the error. Summary query:

```powershell
$x = [xml](Get-Content "<LogDir>\My.results.xml" -Raw)
$run = @($x.DocumentElement.ChildNodes)[-1]          # latest "Debug RunN"
$run.SelectNodes(".//ResultNode[@testresult='0' and @name='Error']") |
  ForEach-Object { $tc = $_.ParentNode
    [pscustomobject]@{ CU = $tc.GetAttribute('unitkey'); Test = $tc.GetAttribute('name')
                       Msg = ($_.GetAttribute('description') -split "`n")[0] } } |
  Group-Object CU, Test, Msg | Sort-Object Count -Descending |
  Select-Object Count, Name -First 80 | Format-Table -AutoSize -Wrap
```

### Getting the values behind a failure

By default a project only records warnings and errors
(`Advanced > Test Tool > SuppressLogEntries` is checked). Aggregate failures then show
only *"Query did not result in identical readings"* with no values. To get evidence:

1. Copy the project to a scratch folder, e.g.
   `robocopy <ProjectDir> <LogDir>\cttcopy\<Project> /E /XF <Project>.results.xml`.
   Never patch the original project.
2. In the copy's `<Project>.ctt.xml`, find `SuppressLogEntries` and change the value in
   the next `Column column="1"` from `data="2"` to `data="0"`. `addLog()` output then
   lands in the results as `ResultNode name="Log"`.
3. `print()` output is **never** written to the results file; it only appears in the GUI
   output pane. The aggregate oracle comparison (`HAAggregateHelper.js`,
   `CompareValues`/`CompareHistoryData`) uses `print()`. In the copy, switch those calls
   to `addLog()` with a unique prefix (for example `AGGDIAG SERVER` / `AGGDIAG CTT`). Set
   `printResults = false` in `PerformAggregateCheck` so values are only logged for failing
   comparisons, and log the request (node, aggregate, start, end, interval,
   TreatUncertainAsBad, PercentDataGood/Bad, sloped, stepped) at the failure branch.
4. Restart the server and rerun the same selection against the copy. Error counts should
   match the official run within a handful of entries. Then parse the prefixed Log
   entries into a CSV; whitespace inside a description wraps, so normalize it first.

The CTT's own setup and teardown run as `beforeTest.js` / `afterTest.js`. A warning such
as *ActivateSession ... delay in excess of 200ms* on the first session is warm-up noise.

## 5. Triage: CTT defect or server defect?

For every distinct error signature:

1. **Check [ctt-issues.md](ctt-issues.md).** Skip anything already documented there,
   including the known aggregate oracle differences and project configuration notes.
2. **Open the script** at the backtrace line in `<ProjectDir>\maintree\...` (or
   `library\...`). JavaScript `TypeError`s (`... [undefined] is not an object`), wrong
   array indices, misspelled properties and inverted predicates are CTT defects.
3. **Check the spec.** Look up Part 4 (services), Part 11 (history) and Part 13
   (aggregates) at <https://reference.opcfoundation.org>. Pay attention to service result
   vs operation result vs per-DataValue status.
4. **Check the server.** Correlate the timestamp with the server log. Reproduce with a
   focused test in `tests/Opc.Ua.Server.Tests` (for aggregates:
   `AggregateCttRegressionTests`, which runs the calculator directly and through the
   live history dispatcher) before changing server code.
5. **Classify** as a server issue (with a spec reference and the location in
   `src/Opc.Ua.Server` or `samples/Quickstarts.Servers`), a CTT issue (file it in Mantis and add the failing test, a short
   abstract and the Mantis link to `ctt-issues.md`),
   or configuration (a project setting such as a blank `ProcessingInterval` or a
   non-historizing node).

## 6. Alarms and Conditions

The *Alarms and Conditions* group (28 CUs, 133 test cases with scripts 1.05.513) mostly waits
for alarm events. Run it as one CTT process, without A & C CertificateExpiration, with a lower
Alarm Cycle Time in a project copy. That takes about 19 minutes; running each CU on its own
takes about 87 minutes plus a hang.

### Recommended run

1. **Selection:** every CU of the group except `A & C CertificateExpiration` (27 CUs, 128 test
   cases). CertificateExpiration asks the operator to change the server clock through modal
   dialogs and hangs a `--hidden` run ([ctt-issues.md](ctt-issues.md) C44); run it in the GUI if
   needed. The manual single-case CUs cost nothing.
2. **One CTT process, fresh server.** The CTT keeps one alarm thread for the whole group, so the
   initial event capture (one Alarm Cycle Time) is paid once, by the first A&C CU.
3. **Project copy with `/Server Test/Alarms and Conditions/Alarm Cycle Time` = 30** (default 60).
   The setting is the length of the initial capture and one third of the maximum time of every
   collector test case. The reference server's alarm sources run a 40 s sawtooth and every alarm
   type reports an event at most 11 s apart, so 30 s still captures every type, and the longest
   event chain (about 45 s: active → inactive → acknowledge → confirm) stays below the 90 s maximum.
4. **Timeout 45 minutes.** The CTT writes results only at the end.

If you must split the group, do not start a part with a Limit/Level CU unless
`/Server Test/Session/RequestedSessionTimeout` is larger than Alarm Cycle Time × 1000: the CU session
idles during the initial capture, times out, and four test cases run to their maximum (C40).

### Where the time goes

Measured on 2026-09-14 (CTT 1.05.06, scripts 1.05.513, `ConsoleReferenceServer --ctt`):

| CU | Own run, fresh server, cycle 60, before | Whole group, cycle 30, after | Dominant wait |
| --- | --- | --- | --- |
| Basic | 1:39 | 0:12 | initial capture (60 s) when first |
| Enable | 4:50 | 0:25 | `Test_003.js` phase race (C42), `Err_004.js`/`Err_005.js` event stall (C43) |
| Acknowledge | 2:48 | 1:43 | initial capture (first CU of the group run: 44 s), events |
| Confirm | 2:53 | 0:59 | events |
| Alarm | 4:48 | 1:54 | `Test_002.js` always runs to 3 × cycle (C41) |
| Refresh | 2:44 | 1:28 | fixed waits: `Test_006.js` 20 s, `Err_004.js` 30 s |
| Refresh2 | 3:02 | 1:44 | fixed waits: `Test_006.js` 20 s, `Err_003.js` 30 s |
| Shelving | 3:16 | 1:02 | one analog period in `Test_002.js` |
| Comment | 4:21 | 2:21 | four skipped test cases retry three times each (C39) |
| Exclusive Limit | 14:23 | 1:45 | before: session timeout during capture (C40); after: one limit sweep in `Test_002.js`/`Test_003.js` |
| Exclusive Level | 14:04 | 1:34 | same |
| Non-Exclusive Limit | 14:09 | 1:46 | same |
| Non-Exclusive Level | 14:05 | 1:46 | same |
| CertificateExpiration | hangs (modal dialog) | excluded | C44 |
| 13 manual CUs (incl. Dialog) | seconds | seconds | — |
| **Total** | **about 87 min + hang** | **18:45** | |

The same group run with the default cycle of 60 s took 24:21 and 29:23. The spread comes from A & C
Enable: `Test_003.js` missed alarm types while the server's boolean and analog alarm sources stepped a
tick apart (C42, fixed on the server side), and after the `Err_004.js` burst the CTT alarm thread can stop
returning events (C43), so `Err_004.js` and `Err_005.js` run to 3 × cycle (6 minutes at cycle 60, 3 at
cycle 30).

How the A&C scripts spend time:

- **Initial capture.** `AlarmCollector.InitialEventCapture` records events for one Alarm Cycle Time in the
  CU that starts the alarm thread (`library/AlarmsAndConditions/AlarmCollector.js`, lines 305–319).
- **Collector test cases** (`AlarmCollector.RunSingleTest`) track one condition per alarm type that sent
  an event and end when each has a pass, fail or skip, otherwise after 3 × Alarm Cycle Time
  (`GetMaximumTestTime`). A test case whose script never records a result for one type (C41, C42)
  always costs the maximum.
- **Server alarm simulation.** `Alarms.AnalogSource` and `Alarms.BooleanSource` step by 5 per second
  from 50 to 100, down to 0 and back (40 s). Every condition changes state at the same ticks (70, 90,
  85, 65, 30, 10, 15, 35), so the CTT sees an event from every alarm type at most 11 s apart and all
  limit states within one period. The shortest state lasts 4 s. Do not make the simulation faster:
  acknowledge, confirm and comment test cases call methods with the EventId of the event they just
  received and fail with `BadEventIdUnknown` when the next state change has replaced it.
- **Fixed waits** in scripts: Refresh `Test_006.js` 20 s, Refresh `Err_004.js` / Refresh2 `Err_003.js`
  30 s, Comment `Err_006.js` up to 20 s.

To measure it yourself, the test-case `ResultNode` timestamps are start times: a test case lasts until
the next one starts. For more detail, add
`addLog( "ACTIMING test=" + testName + " ms=" + duration )` at the end of `RunSingleTest` in a project
copy with `SuppressLogEntries` unchecked (section 4); counters per alarm type
(`this.TestTypeResults`) show which type kept a test case open.

### Expected result

With the server fixes of 2026-09-14, the recommended run reports errors only in A & C Alarm
`Test_002.js` (C10) and `Test_004.js` (C11), and A & C Enable `Test_002.js` (C12/C39). A & C Comment
skips `Test_001.js`–`Test_004.js` (C39) and `Err_006.js`; ten Shelving test cases pass without testing
anything unless chattering alarms are configured (see ctt-issues.md, CTT project configuration notes).
## 7. Session and Subscription Services

The *Session Services* group (4 CUs, 32 test cases) and the *Subscription Services* group (14 CUs,
215 test cases) are fast once the server is healthy. Run each group as **one** CTT process on a fresh
server; splitting them per CU buys nothing.

### Recommended run

1. **Selection:** every CU of the group, built with the generator in section 3 (filter
   `$g -eq 'Session Services'` or `$g -eq 'Subscription Services'`). Give each group its own fresh
   server; Session Base times sessions out and opens the maximum number of sessions (`Err-019.js`).
2. **Server:** `ConsoleReferenceServer.exe --ctt -a -c`. Session Change User and Subscription Durable `004.js` log in with
   `/Server Test/Session/LoginNameGranted1` (sysadmin/demo); `LoginNameAccessDenied` (username/password) is
   not a user of the reference server, so Session Base `Err-010.js` is rejected as it expects.
   `Ctt.ReferenceServer.Config.xml` provides MaxSessionCount 75, MinSessionTimeout 10 s and disables the
   certificate-keyed authentication lockout. The only test case with a not-trusted client certificate,
   Session Base `012.js`, uses SecurityPolicy None, where the certificate is not validated, and it
   passes with `-a`.
3. **Timeouts:** 20 minutes per group. The CTT writes results only at the end.
4. **Project path:** keep the project copy on a short path (for example `%TEMP%\ctt\<Project>`). The
   certificate settings are relative (`PKI/CA/certs/ctt_appT.der`); from a copy about 230 characters deep
   the CTT fails with *"LoadCertificate failed to load certificate"* and every test case errors.

### Timings

Measured on 2026-09-14 (CTT 1.05.06, scripts 1.05.513, fresh server per part):

| CU | Cases | Duration |
| --- | --- | --- |
| Subscription Basic | 99 | 5:17 |
| Subscription Durable | 20 | 1:45 |
| Subscription Minimum 02 | 29 | 2:05 |
| Subscription Minimum 05 | 12 | 0:40 |
| Subscription Publish Basic / Min 05 / Min 10 | 8 / 5 / 4 | 0:27 / 0:49 / 0:21 |
| Subscription Transfer | 29 | 0:47 |
| Subscription Multiple, PublishRequest Queue Overflow, Retransmission Queue, Durable StorageLevel High/Medium/Small | 1–3 each | 0:08–0:18 (manual or no test cases) |
| **Subscription Services, one process** | 215 | **10:56** |
| **Session Services, one process** | 32 | **0:45** (9:03 before the ActivateSession fix) |

The durations include the CTT's own start-up and project loading of about 10–15 s.

Before the server fix of 2026-09-14 the Session group took 9 minutes: after Session Base `002.js` every
CreateSession waited for the CTT's 20 s request timeout.

### Expected result

With the server fixes of 2026-09-14:

- **Subscription Services:** errors only in Subscription Minimum 02 `020.js` (issue 19) and Subscription
  Durable `012.js` (C38). Warnings: Durable `002.js` (RevisedLifetimeInHours 10 for a requested UInt32 max,
  expected), Publish Min 05 `003.js` (project configuration), and CloseSession delay warnings in Subscription Basic
  `Err-011.js` (always) and Publish Basic `cleanup.js` (sometimes; a CTT artifact, [ctt-issues.md](ctt-issues.md) C50).
- **Session Services:** errors only in Session Base `Err-002.js`, `Err-005.js` and `Err-022.js` (C37).
  Skips: `Err-009.js` (no Kerberos in the CTT) and `Err-023.js` (the server offers SecurityPolicy None).

## 8. Monitored Item and Node Management Services

Both groups run with the normal recipe (`--ctt -a -c`), one CTT process per group and a fresh server.

| Group | CUs / test cases | Duration (2026-09-14) |
| --- | --- | --- |
| Monitored Item Services | 14 / 208 | 9:45 |
| Node Management Services | 4 / 15 | 0:30 |

- Several Monitored Item test cases compare the request and response timestamps against
  `/Server Test/Time Tolerance` (100 ms). Do not build or run tests in parallel with the group; a busy
  machine turns into *"… Timestamp shows a delay in excess of …"* warnings.
- Leave `/Server Test/NodeIds/NodeManagement/RequestedNodeId` disabled ([ctt-issues.md](ctt-issues.md) C34).
- Delete Node `Err-002.js` adds 15,000 variables below one folder; it is the regression test for AddNodes
  below a parent with many children.

Expected results with the fixes of 2026-09-14: Monitor Basic `039.js` fails (C17) and `038.js` warns (C32);
Node Management Add Node `Err-008.js` fails (issue 9). Everything else passes or is *Not Implemented*, except
one or two sporadic *"Timestamp shows a delay"* warnings that move between Monitored Item test cases from run
to run; they depend on the machine load.

## 9. Security groups

*Security General* and *Security User Token* test that the server **rejects** untrusted, expired,
revoked and otherwise invalid client and user certificates. Never run them against a server started
with `-a`: auto-accept makes every negative certificate test fail. Run the server with its normal
trust checks and trust the CTT's test PKI instead.

### Certificate and trust setup

The CTT project ships the PKI the scripts expect in `<ProjectDir>\PKI\copyToServer` (created with
`<CttDir>\create_ctt_pki.bat`; `rename_copyToServer_certs.bat` only prefixes the file names with a
date). It mirrors a server's directory stores:

| CTT folder | Reference server store (`Ctt.ReferenceServer.Config.xml`) |
| --- | --- |
| `ApplicationInstance_PKI\trusted\certs`, `\crl` | `TrustedPeerCertificates` (`pki/trusted`) |
| `ApplicationInstance_PKI\issuers\certs`, `\crl` | `TrustedIssuerCertificates` (`pki/issuer`) |
| `X509UserIdentity_PKI\trusted\certs`, `\crl` | `TrustedUserCertificates` (`pki/trustedUser`) |
| `X509UserIdentity_PKI\issuers\certs`, `\crl` | `UserIssuerCertificates` (`pki/issuerUser`) |

The CTT connects with `ctt_appT` by default (`CreateSession.js`), which is in the trusted folder, so no
`-a` is needed for the positive tests. The folders deliberately also contain certificates that must be
rejected at connect time (for example `ctt_appTE` expired, `ctt_appTV` not yet valid, `ctt_appTSincorrect`
bad signature) and CRLs that revoke the `...R` certificates. Copy them as they are.

Keep that PKI away from the server's default stores under `%LocalAppData%\OPC Foundation\pki`: other
runs (and `-a`) add certificates there, and the Push Model test cases change the trust lists and the
server certificate. Recommended layout:

1. Create `<LogDir>\secpki\{own,trusted,issuer,trustedUser,issuerUser,rejected}` with `certs`/`crl`
   (`own` needs `certs` and `private`).
2. Copy the server's current application certificate and key (`pki\own\certs` and `pki\own\private`,
   `Quickstart Reference Server*`) into `secpki\own`, so the CTT keeps trusting the server certificate it
   already knows. Copy the four `copyToServer` folders as in the table above.
3. Keep a pristine copy (`secpki.orig`) and restore it (`robocopy secpki.orig secpki /MIR`) before every
   part: Push Model and Security Certificate Administration test cases write to the stores.
4. Copy `samples\Reference\ConsoleReferenceServer\bin\Release\net10.0` to `<LogDir>\srv-sec` and replace
   `%LocalApplicationData%/OPC Foundation/pki` with the `secpki` path in its
   `Ctt.ReferenceServer.Config.xml` (six store paths). Leave `AutoAcceptUntrustedCertificates` false.
5. Start `<LogDir>\srv-sec\ConsoleReferenceServer.exe --ctt -c` (no `-a`).

### Server and project settings

- User names: `/Server Test/Session/LoginNameGranted1` = `sysadmin`/`demo` and `LoginNameGranted2` =
  `user2`/`password1` exist in `ReferenceServer.cs`. `LoginNameAccessDenied` (`username`/`password`) does
  not exist; see [ctt-issues.md](ctt-issues.md) for what that means for Security User Name Password 2
  `012.js`.
- `Ctt.ReferenceServer.Config.xml` sets `MaxFailedAuthenticationAttempts` to 0. The user token CUs send
  many rejected tokens from the same client certificate; with the default lockout every later
  ActivateSession fails with `BadUserAccessDenied`. Security Invalid user token `001.js`/`002.js` test that
  lockout and therefore need a run with the default value.
- The CTT config enables Basic256Sha256 Sign and SignAndEncrypt plus None, and Anonymous, UserName and
  X509 user tokens on every endpoint.

### Splitting the groups

Run *Security User Token* (14 CUs, 66 test cases) as one part and split *Security General* (53 CUs, 314
test cases) into Certificate Validation, certificate management (Push/Pull Model, Certificate
Administration, Default ApplicationInstance Certificate, Security Administration), Role and User
Management, and the rest. Restore the PKI and restart the server before every part. With scripts 1.05.513
most of *Security General* is *Not Implemented*, so every part finishes in under 30 seconds.

Wait for the server's `Server started` line, not only for the listening port: the port opens before startup
completes, and a CTT that connects too early gets `BadServerHalted` from GetEndpoints and records no test case
at all for the part.

### Expected results

With the fixes of 2026-09-14 and the setup above:

| Part | Result |
| --- | --- |
| Security Certificate Validation | 23 pass, 24 *Not Implemented*; skips `004.js` (CTT cannot send an empty certificate), `049.js`/`050.js` (no Basic128Rsa15 endpoint) |
| Security None CreateSession ActivateSession (and 1.0) | all pass |
| Other Security General CUs | *Not Implemented* only |
| Security User Token | Anonymous `002.js` fails (C35), User Name Password 2 `015.js` fails (C36); skips Anonymous `003.js` and User Name Password 2 `002.js` (not applicable); X509 18 of 18 automated cases pass |

Negative certificate and user token tests passing here is only meaningful without `-a`.

## 10. Full run of every conformance group

To check a change against the whole CTT, run every CU that has test cases in parts, each on a fresh server.
Build the selections with the generator of section 3, but read `ProfileSet_UACore_1.05_*` first, then DI 1.05,
UAFX, the older UACore sets and `ProfileSet_Custom.xml` (strip its invalid `xmlns:xmlns` attribute before parsing).
Otherwise the 1.03 group names win (for example *Security* instead of *Security General*) and the selection does
not match the CTT tree. `testscripts.xml` lists Discovery Configuration twice; select each CU id once.

Parts used on 2026-09-15 (CTT 1.05.06, scripts 1.05.513, 3,752 test cases, 23 parts, about 80 minutes):

| Part | Server | Duration |
| --- | --- | --- |
| Address Space Model | `--ctt -a -c` | 2:44 |
| Base Information | `--ctt -a -c` | 4:49 |
| Base Services + View Services + Method Services | `--ctt -a -c` | 0:16 |
| Attribute Services / Data Access | `--ctt -a -c` | 0:17 / 2:17 |
| Aggregates / Historical Access | `--ctt -a -c` | 0:26 / 0:17 |
| Node Management / AliasName / Discovery | `--ctt -a -c` | 0:26 / 0:12 / 0:12 |
| DI Base Model + all UAFX groups | `--ctt -a -c` | 1:06 |
| Auditing | `--ctt -a -c` | 0:50 |
| Monitored Item Services | `--ctt -a -c` | 9:49 |
| GDS / Session Services | `--ctt -a -c` | 0:55 / 0:43 |
| Subscription Services | `--ctt -a -c` | 11:01 |
| Miscellaneous, Base File Information, Protocol and Encoding, PubSub General, Redundancy, UserDefinedCG | `--ctt -a -c` | 0:26 |
| Alarms and Conditions without CertificateExpiration (Alarm Cycle Time 30, section 6) | `--ctt -a -c` | 18:03 |
| Security User Token | isolated PKI, `--ctt -c` (section 9) | 0:30 |
| Security General: Certificate Validation / certificate management / roles and users / rest | isolated PKI, `--ctt -c` | 0:11 / 0:18 / 0:22 / 4:35 |

Use the project settings of `samples/UAReferenceServer.ctt.xml` for the #4479 nodes (see ctt-issues.md, *CTT
project configuration notes*). Compare every part with an earlier run at test-case level (`ResultNode` elements
with a `unitkey`) and by normalized first error line; a changed error count alone hides swapped failures. Warnings
*"… Timestamp shows a delay in excess of …"* on the first ActivateSession of a part are warm-up noise.

Result against origin/master + #4477, #4482, #4485, #4486 (2026-09-15): every part matches the individual group
runs of 2026-09-13/14, except

- Aggregates: 4,498 error messages (was 4,210). Minimum, MinimumActualTime and MaximumActualTime `001-02.js`… now
  fail because #4477 implements the Part 13 Uncertain rules the oracle lacks (C48, C49).
- Auditing: 0 errors (the event queue size bug was fixed by #4480).
- A & C Confirm `Test_001.js` can fail for all alarm types depending on the alarm phase (C10).
- Newly covered: UAFX (no FX model), PubSub Publisher UADP (no PubSub publisher) and Security None /
  Basic256Sha256 `007.js`/`005.js`. Those two failed (also on origin/master) because the server closed idle
  SecureChannels after 30 s of silence while the CTT needed 41 s for the step (C50); they pass with the
  `ChannelLifetime` of 120000 in `Ctt.ReferenceServer.Config.xml` (see ctt-issues.md, open server findings).

Repeated on 2026-09-22 against origin/master (#4503 and later merged) plus an inactivity-cleanup server change
that was withdrawn afterwards (see the run of 2026-09-24 below). Every part
matches the run above at test-case level, except

- Aggregates: 4,562 error messages (was 4,498). Aggregate – DeltaBounds now also differs on the Double and Float
  nodes because #4503 computes the difference for Uncertain bounds (U4).
- Monitored Item Services: Monitor Value Change V2 `020.js` passes (it was skipped while the sample ByteString
  array still had elements shorter than four bytes).
- A & C Confirm `Test_001.js` passed this time; it depends on the alarm phase (C10).
- Security None `007.js` and Security Basic256Sha256 `005.js` pass with the default 30 s `ChannelLifetime`.
- Node Management Delete Node `Err-002.js` warned about 200 ms AddNodes/DeleteNodes responses (the batches took
  400–800 ms before the BrowseName index of #4486 and 141–172 ms after it), so the 100 ms tolerance is tight
  rather than the old behavior being back.

Repeated on 2026-09-24 without the inactivity-cleanup change (server code as on origin/master) and with
`ChannelLifetime` 120000 in `Ctt.ReferenceServer.Config.xml`. Compared with the run of 2026-09-22 at test-case level:

- Security None `007.js` passes and Security Basic256Sha256 `005.js` has only the C50 CloseSession delay warnings,
  as with the server change.
- All other parts have the same error signatures. The other differences are *"… Timestamp shows a delay in excess
  of …"* warnings: other builds and test runs kept the machine at up to 99% CPU. Session Services run back to back
  on the same build with `ChannelLifetime` 30000 and 120000 on an idle machine gave identical results.
- Under that load A & C Refresh2 `Err_003.js`/`Err_004.js` and Shelving `initialize.js` failed with
  `BadSubscriptionIdInvalid` in two runs with 120000 and one with 30000; on the idle machine the whole A & C part
  passed them again (689 passed, 2026-09-22: 638). A & C Comment `Test_001.js`…`Test_004.js` now pass instead of
  being skipped because the wrapped call-time difference of C39 is positive at this date, and A & C Confirm
  `Test_001.js` failed this time (C10).

A project copy that the CTT has re-saved can lose the `cleanup.js`/`manual.js` of manual CUs (104 files in the
copies used here), which shows up as *"Could not open file …"* errors in the affected CUs. Refresh a copy from
`<ProjectDir>` (adding only missing files) before a comparison run.

Keep the run directory, the isolated PKI (`secpki.orig`) and the selections outside `%TEMP%`: a Windows temp cleanup
removed their older files during the run of 2026-09-24. Without its certificates the isolated server created a new
application certificate and trusted nothing, so every Security User Token test case failed with
`BadSecurityChecksFailed`. Check that `secpki.orig\own` and `secpki.orig\trusted` are populated before a run.

## Pitfalls

- Omitting `--result` silently overwrites `<Project>.results.xml`. Back it up first if
  the previous run matters.
- A server left running from an earlier session holds port 62541, and the CTT then
  tests an old build. Check `Get-Process ConsoleReferenceServer` and the banner sha.
- Several automated runs sharing one machine must take turns. Wait until no
  `ConsoleReferenceServer`, no `uacompliancetest.exe` with `--settings` and no listener on port 62541
  has been seen for about a minute, check again immediately before starting the server, and treat
  *"Failed to establish tcp listener sockets on port 62541"* in the server output as "busy, retry later".
  Never stop a server or CTT you did not start; a `uacompliancetest.exe` without arguments is the GUI.
