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
  rejected unless the CTT cert is in the server's trusted store.
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
   `src/Opc.Ua.Server` or `samples/Quickstarts.Servers`), a CTT issue (add to
   `ctt-issues.md` with the test, line, the reason it is wrong and the recommended fix),
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
CreateSession waited for the CTT's 20 s request timeout (see [ctt-issues.md](ctt-issues.md), open server
findings).

### Expected result

With the server fixes of 2026-09-14:

- **Subscription Services:** errors only in Subscription Minimum 02 `020.js` (issue 19) and Subscription
  Durable `012.js` (C38). Warnings: Durable `002.js` (RevisedLifetimeInHours 10 for a requested UInt32 max,
  expected), Publish Min 05 `003.js` (project configuration), and CloseSession latency in Subscription Basic
  `Err-011.js` (always) and Publish Basic `cleanup.js` (sometimes).
- **Session Services:** errors only in Session Base `Err-002.js`, `Err-005.js` and `Err-022.js` (C37).
  Skips: `Err-009.js` (no Kerberos in the CTT) and `Err-023.js` (the server offers SecurityPolicy None).

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
