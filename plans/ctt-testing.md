# Running the CTT against the reference server

How to run the OPC UA Compliance Test Tool (CTT) headless against `ConsoleReferenceServer` and how to
triage the results efficiently.

**Known CTT defects.** Many test cases fail, warn or skip against the reference server because of defects in
the CTT scripts or its native aggregate oracle, not in the server. They are listed with their Mantis issues in
[ctt-issues.md](ctt-issues.md), together with open questions, open server findings and CTT project
configuration notes. Read that file before a run and treat the failures it lists as expected; only
investigate what is not in it. When you find a new CTT defect, file it in Mantis and add the failing test, a
short abstract and the Mantis link there.

## Setup

Placeholders used below:

| Placeholder | Meaning |
| --- | --- |
| `<CttDir>` | CTT installation folder, containing `uacompliancetest.exe`. Usually `...\OPC Foundation\UA 1.05\Compliance Test Tool`. |
| `<ProjectDir>` | folder of a CTT **server** project (created in the CTT GUI) |
| `<Project>` | project name. The project files are `<Project>.ctt.xml`, `<Project>.selection.xml` and `<Project>.results.xml`. |
| `<LogDir>` | a work folder for server snapshots, project copies, selections, logs and results; **not** under `%TEMP%` (see *Pitfalls*) |

Inside `<ProjectDir>`:

| Item | Location |
| --- | --- |
| Script catalog | `testscripts.xml`, `maintree\` (test scripts), `library\` (helpers) |
| CU id → name map | `Profiles\UACore\ProfileSet_UACore_1.05_*.xml` (plus DI, UAFX and `ProfileSet_Custom.xml`) |
| CTT test PKI | `PKI\CA` (CTT client certificates), `PKI\copyToServer` (what the server must trust) |

Configure the project once in the GUI:

- **Server URL:** `opc.tcp://localhost:62541/Quickstarts/ReferenceServer`.
- **Node settings:** start from `samples/UAReferenceServer.ctt.xml`, which has the HA Profile, aggregate,
  alarm, user and NodeId settings for the reference server in CTT mode.
- **Certificate trust:** the CTT client certificate lives under `<ProjectDir>\PKI`.

Keep project copies on a short path (for example `<LogDir>\main\<Project>`): the certificate settings are
relative (`PKI/CA/certs/ctt_appT.der`), and from a copy about 230 characters deep the CTT fails with
*"LoadCertificate failed to load certificate"* and every test case errors.

## 1. Build and start the server

```powershell
dotnet build samples\Reference\ConsoleReferenceServer\ConsoleReferenceServer.csproj -c Release -f net10.0 -p:CustomTestTarget=net10.0
cd samples\Reference\ConsoleReferenceServer\bin\Release\net10.0
.\ConsoleReferenceServer.exe --ctt -a -c *> <LogDir>\refserver.log
```

- `--ctt` loads the `Ctt.ReferenceServer` configuration section (`Ctt.ReferenceServer.Config.xml`), and
  `ApplyCTTModeAsync` starts the CTT alarms and other presets. Always use it; without it many CUs fail for
  configuration reasons.
- `-a` auto-accepts the CTT client certificate. Without it, the first session is rejected unless the CTT
  certificate is in the server's trusted store. Never use `-a` for the security groups (section 9).
- `-c` logs to the console. Redirect it to a file to correlate server errors with CTT timestamps. `-l`/`-f`
  also write an app log file.
- Wait for the `Server started (... ms)` line (10–20 s), not only for the listening port: the port opens
  before startup completes, and a CTT that connects too early gets `BadServerHalted` from GetEndpoints and
  records no test case at all.
- Restart the server before every run. History, alarm and node-management tests change server state, and a
  reused server produces misleading follow-on failures.
- For runs you want to compare, copy the build output to a snapshot folder (`<LogDir>\srv`) and start the
  server from there, so a rebuild in the repository does not change the server under test. Confirm the build
  with the banner line `OPC UA library: ... +<sha>`. Do not use `dotnet build -o` for the snapshot.

## 2. Run the CTT from the command line

```powershell
$p = Start-Process -FilePath "<CttDir>\uacompliancetest.exe" `
  -ArgumentList @('--close','--hidden',
                  '--settings','"<ProjectDir>\<Project>.ctt.xml"',
                  '--selection','"<LogDir>\My.selection.xml"',
                  '--result','"<LogDir>\My.results.xml"') `
  -WorkingDirectory "<CttDir>" -PassThru
if (-not $p.WaitForExit($timeoutMinutes * 60000)) { Stop-Process -Id $p.Id -Force }
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

- `uacompliancetest.exe` is a GUI binary. Stdout and stderr stay empty; everything goes to the results XML.
- The results file is written only when the run ends, and a killed run leaves no results. Always wait with a
  timeout: scripts that open a message box (A & C CertificateExpiration) do so even with `--hidden` and wait
  for input forever. Split long selections into parts.
- The CTT **rewrites the selection file** you pass on exit. Pass a copy.
- The CTT re-saves the project (`*.ctt.xml`, `testscripts.xml`, `libmodel.xml`) on exit, and a re-saved copy
  can lose the `cleanup.js`/`manual.js` files of manual CUs (*"Could not open file …"* errors). Refresh a
  copy from `<ProjectDir>` before each run by adding only missing files:
  `robocopy <ProjectDir> <LogDir>\main\<Project> /E /XC /XN /XO`.
- **Results accumulate.** A results file that already exists gets a new top-level
  `ResultNode name="Debug RunN"` appended. Always analyze the last run node.
- Only one CTT instance may talk to the server at a time (see *Pitfalls* for sharing a machine).
- Full CTT documentation: `<CttDir>\help\command_line_interface.htm`.

## 3. Build a selection file

A selection file lists **conformance group → conformance unit → test case names**. The names must match the
CTT's names exactly, including en dashes (`Aggregate – Average`) and trailing spaces
(`Attribute Historical Read `). `initialize.js` and `cleanup.js` run automatically; don't list them.

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

Don't derive CU names from script paths. Many CUs reuse another CU's scripts; all 37 `Aggregate – *` CUs
point at `Aggregates/Aggregate - Base/...`. Resolve names by id instead: `testscripts.xml` lists
`ConformanceUnit id=...` with its test cases, and the profile sets map that id to `Name` and
`ConformanceGroup`. This generator builds a selection for every CU whose group or name matches a filter:

```powershell
$r = "<ProjectDir>"
$rank = { switch -Regex ($_.Name) { 'UACore_1\.05' {0} 'DI_1\.05' {1} 'UAFX' {2} 'UACore_1\.04' {3} default {4} } }
$sets = Get-ChildItem "$r\Profiles" -Recurse -Filter 'ProfileSet_*.xml' | Sort-Object $rank   # first set wins
$cuName=@{}; $cuGroup=@{}; $grpName=@{}
foreach ($f in $sets) {
  $raw = (Get-Content $f.FullName -Raw) -replace '\sxmlns:xmlns="[^"]*"', ''   # ProfileSet_Custom.xml is invalid
  $p = [xml]$raw
  foreach ($n in $p.SelectNodes("//*[local-name()='ConformanceGroup'][@Id]")) {
    if (-not $grpName.ContainsKey($n.Id)) { $grpName[$n.Id] = $n.Name } }
  foreach ($n in $p.SelectNodes("//*[local-name()='ConformanceUnit'][@Id and @Name]")) {
    if ($cuName.ContainsKey($n.Id)) { continue }
    $cuName[$n.Id] = $n.Name
    $g = $n.SelectSingleNode("*[local-name()='ConformanceGroup']"); if ($g) { $cuGroup[$n.Id] = $g.InnerText } } }
$x = [xml](Get-Content "$r\testscripts.xml" -Raw)
$filter = { param($g, $u) $g -in 'Historical Access','Aggregates' -or $u -match 'Hist' }   # <- adjust
$map = [ordered]@{}
foreach ($cu in $x.UaTestScripts.ConformanceUnits.ConformanceUnit) {
  $tcs = @($cu.TestCases.TestCase) | Where-Object { $_ }
  if (-not $tcs -or -not $cuName.ContainsKey($cu.id)) { continue }
  $u = $cuName[$cu.id]; $g = $grpName[$cuGroup[$cu.id]]
  if (& $filter $g $u) { $map["$g`t$u"] = @($tcs | ForEach-Object name) } }   # keys dedupe CUs listed twice
# Write $map as <ConformanceGroup>/<ConformanceUnit>/<TestCase> elements (UTF-8, no BOM).
```

Read the UACore 1.05 profile sets first: otherwise the 1.03 group names win (for example *Security* instead
of *Security General*) and the selection does not match the CTT tree. `testscripts.xml` lists some CUs twice
(Discovery Configuration); select each CU once.

## 4. Read the results

The results XML (`UaCttResults`) is a tree of `ResultNode` elements. Test-case nodes carry `groupkey` and
`unitkey`, and their children are the individual messages. The `testresult` attribute:

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

Each Error message is followed by Backtrace nodes. The first backtrace entry inside `maintree/` is the failing
line of the test; entries in `library/` show which helper raised the error. Message timestamps are in the
`timestamp` attribute; a test case lasts until the next one starts. Summary query:

```powershell
$x = [xml](Get-Content "<LogDir>\My.results.xml" -Raw)
$run = @($x.DocumentElement.ChildNodes)[-1]          # latest "Debug RunN"
$run.SelectNodes(".//ResultNode[@testresult='0' and @name='Error']") |
  ForEach-Object { $tc = $_.ParentNode
    while ($tc -is [Xml.XmlElement] -and -not $tc.GetAttribute('unitkey')) { $tc = $tc.ParentNode }
    [pscustomobject]@{ CU = $tc.GetAttribute('unitkey'); Test = $tc.GetAttribute('name')
                       Msg = ($_.GetAttribute('description') -split "`n")[0] } } |
  Group-Object CU, Test, Msg | Sort-Object Count -Descending |
  Select-Object Count, Name -First 80 | Format-Table -AutoSize -Wrap
```

### Compare two runs

Compare at test-case level, not by error counts: a changed count hides swapped failures, and an unchanged
count can hide them too.

```powershell
function Get-Cases([string]$file) {
  $x = [xml](Get-Content $file -Raw); $run = @($x.DocumentElement.ChildNodes)[-1]; $m = @{}
  foreach ($n in $run.SelectNodes('.//ResultNode[@unitkey]')) {
    $m["$($n.GetAttribute('unitkey')) | $($n.GetAttribute('name'))"] = $n.GetAttribute('testresult') }
  $m }
$new = Get-Cases '<LogDir>\new.results.xml'; $old = Get-Cases '<LogDir>\old.results.xml'
$new.Keys + $old.Keys | Sort-Object -Unique | Where-Object { $new[$_] -ne $old[$_] } |
  ForEach-Object { '{0,-4} -> {1,-4} {2}' -f $old[$_], $new[$_], $_ }
```

Then compare the normalized first line of every Error (digits replaced by `#`, handles stripped) per CU and
test case, to see which messages are new.

### Noise to ignore

- *"… Response.ResponseHeader.Timestamp shows a delay in excess of …"* warnings depend on machine load: on
  the first ActivateSession of a run they are warm-up, elsewhere they move between test cases from run to run.
  Several Monitored Item and Session test cases compare against `/Server Test/Time Tolerance` (100 ms). Do not
  build or run tests in parallel with a CTT run, and check the CPU load before blaming the server.
- A few A & C results depend on the alarm phase or even the date (see ctt-issues.md); compare several runs
  before calling a change a regression.

### Getting the values behind a failure

By default a project only records warnings and errors (`Advanced > Test Tool > SuppressLogEntries` is
checked). Aggregate failures then show only *"Query did not result in identical readings"* with no values. To
get evidence:

1. Copy the project to a scratch folder (`robocopy <ProjectDir> <LogDir>\cttcopy\<Project> /E
   /XF <Project>.results.xml`). Never patch the original project.
2. In the copy's `<Project>.ctt.xml`, find `SuppressLogEntries` and change the value in the next
   `Column column="1"` from `data="2"` to `data="0"`. `addLog()` output then lands in the results as
   `ResultNode name="Log"`.
3. `print()` output is **never** written to the results file; it only appears in the GUI output pane. Switch
   the relevant `print()` calls in the copy to `addLog()` with a unique prefix. For aggregates, the oracle
   comparison is `HAAggregateHelper.js` (`CompareValues`/`CompareHistoryData`); set `printResults = false`
   in `PerformAggregateCheck` so values are only logged for failing comparisons, and log the request (node,
   aggregate, start, end, interval, TreatUncertainAsBad, PercentDataGood/Bad, sloped, stepped).
4. Restart the server and rerun the same selection against the copy. Parse the prefixed Log entries into a
   CSV; whitespace inside a description wraps, so normalize it first.

To see what the server receives and when, add temporary `Console.WriteLine` probes to a throwaway server
build (request arrival in `TcpServerChannel.HandleIncomingMessageAsync`, service phases in `StandardServer`)
instead of trusting the CTT's timing messages; revert them afterwards.

## 5. Triage: CTT defect or server defect?

For every distinct error signature:

1. **Check [ctt-issues.md](ctt-issues.md).** Skip anything listed there, including the configuration notes.
2. **Open the script** at the backtrace line in `<ProjectDir>\maintree\...` (or `library\...`). JavaScript
   `TypeError`s (`... [undefined] is not an object`), wrong array indices, misspelled properties and inverted
   predicates are CTT defects.
3. **Check the spec.** Look up Part 4 (services), Part 11 (history), Part 12 (GDS) and Part 13 (aggregates) at
   <https://reference.opcfoundation.org>. Pay attention to service result vs operation result vs per-DataValue
   status.
4. **Check the server.** Correlate the timestamp with the server log. Reproduce with a focused test in
   `tests/Opc.Ua.Server.Tests` (for aggregates: `AggregateCttRegressionTests`, which runs the calculator
   directly and through the live history dispatcher) before changing server code. To decide whether a failure
   is new, build origin/master in a separate worktree and rerun only that CU with the same project copy.
5. **Classify** as a server issue (with a spec reference and the location in `src/Opc.Ua.Server` or
   `samples/Quickstarts.Servers`), a CTT issue (file it in Mantis and add it to `ctt-issues.md`), or
   configuration (a project setting such as a blank `ProcessingInterval` or a non-historizing node).

## 6. Alarms and Conditions

The *Alarms and Conditions* group (28 CUs, 133 test cases with scripts 1.05.513) mostly waits for alarm
events. Run it as one CTT process with a lower Alarm Cycle Time; that takes about 20 minutes, while running
each CU on its own takes well over an hour.

1. **Selection:** every CU of the group except `A & C CertificateExpiration`, which asks the operator to change
   the server clock through modal dialogs and hangs a `--hidden` run. Run that CU in the GUI if needed. The
   manual single-case CUs cost nothing.
2. **One CTT process, fresh server.** The CTT keeps one alarm thread for the whole group, so the initial event
   capture (one Alarm Cycle Time) is paid once, by the first A & C CU.
3. **Project copy with `/Server Test/Alarms and Conditions/Alarm Cycle Time` = 30** (default 60). The setting
   is the length of the initial capture, one third of the maximum time of every collector test case, and ten
   times the Enable `Test_003.js` refresh delay. The reference server's alarm sources run a 40 s sawtooth and
   every alarm type reports an event at most 11 s apart, so 30 s still captures every type, and the longest
   event chain (about 45 s: active → inactive → acknowledge → confirm) stays below the 90 s maximum.
4. **Timeout 45 minutes.**

If you must split the group, do not start a part with a Limit/Level CU unless
`/Server Test/Session/RequestedSessionTimeout` is larger than Alarm Cycle Time × 1000: the CU session idles
during the initial capture and times out.

How the A & C scripts spend time:

- **Initial capture.** `AlarmCollector.InitialEventCapture` records events for one Alarm Cycle Time in the CU
  that starts the alarm thread (`library/AlarmsAndConditions/AlarmCollector.js`).
- **Collector test cases** (`AlarmCollector.RunSingleTest`) track one condition per alarm type that sent an
  event and end when each has a pass, fail or skip, otherwise after 3 × Alarm Cycle Time
  (`GetMaximumTestTime`). A test case whose script never records a result for one type always costs the
  maximum.
- **Fixed waits** in scripts: Refresh `Test_006.js` 20 s, Refresh `Err_004.js` / Refresh2 `Err_003.js` 30 s,
  Comment `Err_006.js` up to 20 s.
- **Server alarm simulation.** `Alarms.AnalogSource` and `Alarms.BooleanSource` step by 5 per second from 50 to
  100, down to 0 and back (40 s), and every condition changes state at the same ticks. Do not make the
  simulation faster: acknowledge, confirm and comment test cases call methods with the EventId of the event
  they just received and fail with `BadEventIdUnknown` when the next state change has replaced it.

To see which alarm type keeps a test case open, add `addLog( "ACTIMING test=" + testName + " ms=" + duration )`
at the end of `RunSingleTest` in a project copy with `SuppressLogEntries` unchecked, and log the per-type
counters (`this.TestTypeResults`).

## 7. Session and Subscription Services

The *Session Services* group (4 CUs, 32 test cases, about 1 minute) and the *Subscription Services* group
(14 CUs, 215 test cases, about 11–13 minutes) run as **one** CTT process each on a fresh server; splitting them
per CU buys nothing. Give each group its own server: Session Base times sessions out and opens the maximum
number of sessions (`Err-019.js`). Use a 20-minute timeout.

- Session Change User and Subscription Durable log in with `/Server Test/Session/LoginNameGranted1`
  (`sysadmin`/`demo`); `LoginNameGranted2` is `user2`/`password1`. `LoginNameAccessDenied`
  (`username`/`password`) is not a user of the reference server.
- `Ctt.ReferenceServer.Config.xml` provides MaxSessionCount 75, MaxSubscriptionCount 100, MinSessionTimeout
  10 s, and disables the certificate-keyed authentication lockout. Session Base `012.js`, the only test case
  with an untrusted client certificate, uses SecurityPolicy None, so it passes with `-a`.

## 8. Monitored Item and Node Management Services

Both groups run with the normal recipe (`--ctt -a -c`), one CTT process per group and a fresh server:
Monitored Item Services (14 CUs, 208 test cases) takes about 10–11 minutes, Node Management Services (4 CUs,
15 test cases) under a minute.

- These groups are the most sensitive to machine load (timestamp tolerance warnings, see section 4).
- Leave `/Server Test/NodeIds/NodeManagement/RequestedNodeId` disabled.
- Delete Node `Err-002.js` adds 15,000 variables below one folder in batches of 5,000 and checks the response
  times; it is the load test for AddNodes below a parent with many children.

## 9. Security groups

*Security General* and *Security User Token* test that the server **rejects** untrusted, expired, revoked and
otherwise invalid client and user certificates. Never run them against a server started with `-a`:
auto-accept makes every negative certificate test fail. Run the server with its normal trust checks and trust
the CTT's test PKI instead.

### Certificate and trust setup

The CTT project ships the PKI the scripts expect in `<ProjectDir>\PKI\copyToServer` (created with
`<CttDir>\create_ctt_pki.bat`; `rename_copyToServer_certs.bat` only prefixes the file names with a date). It
mirrors a server's directory stores:

| CTT folder | Reference server store (`Ctt.ReferenceServer.Config.xml`) |
| --- | --- |
| `ApplicationInstance_PKI\trusted\certs`, `\crl` | `TrustedPeerCertificates` (`pki/trusted`) |
| `ApplicationInstance_PKI\issuers\certs`, `\crl` | `TrustedIssuerCertificates` (`pki/issuer`) |
| `X509UserIdentity_PKI\trusted\certs`, `\crl` | `TrustedUserCertificates` (`pki/trustedUser`) |
| `X509UserIdentity_PKI\issuers\certs`, `\crl` | `UserIssuerCertificates` (`pki/issuerUser`) |

The CTT connects with `ctt_appT` by default (`CreateSession.js`), which is in the trusted folder. The folders
deliberately also contain certificates that must be rejected at connect time (for example `ctt_appTE`
expired, `ctt_appTV` not yet valid, `ctt_appTSincorrect` bad signature) and CRLs that revoke the `...R`
certificates. Copy them as they are.

Keep that PKI away from the server's default stores under `%LocalAppData%\OPC Foundation\pki`: other runs
(and `-a`) add certificates there, and the Push Model test cases change the trust lists and the server
certificate.

1. Create `<LogDir>\secpki.orig\{own,trusted,issuer,trustedUser,issuerUser,rejected}` with `certs`/`crl`
   (`own` needs `certs` and `private`).
2. Copy the server's current RSA application certificate and key (`%LocalAppData%\OPC Foundation\pki\own\certs`
   and `\private`, `Quickstart Reference Server [<thumbprint>]`) into `secpki.orig\own`, so the CTT keeps
   trusting the server certificate it already knows. The file names contain `[...]`: use
   `Copy-Item -LiteralPath`, because `-Path` treats the brackets as a wildcard and silently copies nothing.
3. Copy the four `copyToServer` folders into `trusted`, `issuer`, `trustedUser` and `issuerUser` as in the
   table.
4. Before every part, restore the working PKI with `robocopy secpki.orig secpki /MIR`: Push Model and
   Certificate Administration test cases write to the stores.
5. Copy the server snapshot to `<LogDir>\srv-sec` and replace `%LocalApplicationData%/OPC Foundation/pki/`
   with the `secpki` path in its `Ctt.ReferenceServer.Config.xml` (six store paths). Leave
   `AutoAcceptUntrustedCertificates` false.
6. Start `<LogDir>\srv-sec\ConsoleReferenceServer.exe --ctt -c` (no `-a`).

If `secpki.orig\own` is empty the server creates a new certificate on startup and trusts nothing, and every
test case fails with `BadSecurityChecksFailed`; check the folders before a run.

### Server settings

- `Ctt.ReferenceServer.Config.xml` sets `MaxFailedAuthenticationAttempts` to 0. The user token CUs send many
  rejected tokens from the same client certificate; with the default lockout every later ActivateSession
  fails with `BadUserAccessDenied`. Security Invalid user token `001.js`/`002.js` test that lockout and need a
  run with the default value.
- It also sets `ChannelLifetime` to 120000: Security None and Security Basic 256 Sha256 keep SecureChannels
  without a Session idle for about 51 s and expect them to still be open.
- The CTT configuration enables Basic256Sha256 Sign and SignAndEncrypt plus None, and Anonymous, UserName and
  X509 user tokens on every endpoint.

### Splitting the groups

Run *Security User Token* (14 CUs, 66 test cases) as one part and split *Security General* (53 CUs, 314 test
cases) into Certificate Validation, certificate management (Push/Pull Model, Certificate Administration,
Default ApplicationInstance Certificate, Security Administration), Role and User Management, and the rest.
Restore the PKI and restart the server before every part. With scripts 1.05.513 most of *Security General* is
*Not Implemented*; the parts take seconds, except the rest part (Security None, Basic256Sha256, Time Sync) with
about 5 minutes.

## 10. Full run of every conformance group

To check a change against the whole CTT, run every CU that has test cases (3,752 test cases with scripts
1.05.513) in parts, each on a fresh server, and compare every part with a baseline run at test-case level
(section 4). Build the selections with the generator of section 3. The whole run takes about 80 minutes:

| Part | Server | Duration |
| --- | --- | --- |
| Address Space Model | `--ctt -a -c` | 2–3 min |
| Base Information | `--ctt -a -c` | 5–7 min |
| Base Services + View Services + Method Services | `--ctt -a -c` | < 1 min |
| Attribute Services / Data Access | `--ctt -a -c` | < 1 min / 2–3 min |
| Aggregates / Historical Access | `--ctt -a -c` | < 1 min each |
| Node Management / AliasName / Discovery | `--ctt -a -c` | < 1 min each |
| DI Base Model + all UAFX groups | `--ctt -a -c` | 1 min |
| Auditing | `--ctt -a -c` | 1 min |
| Monitored Item Services | `--ctt -a -c` | 10–11 min |
| GDS / Session Services | `--ctt -a -c` | 1–2 min each |
| Subscription Services | `--ctt -a -c` | 11–13 min |
| Miscellaneous, Base File Information, Protocol and Encoding, PubSub General, Redundancy, UserDefinedCG | `--ctt -a -c` | < 1 min |
| Alarms and Conditions without CertificateExpiration (section 6 project copy) | `--ctt -a -c` | 17–25 min |
| Security User Token | isolated PKI, `--ctt -c` (section 9) | < 1 min |
| Security General: Certificate Validation / certificate management / roles and users / rest | isolated PKI, `--ctt -c` | < 1 min each / 5 min |

Run the parts from a queue script, one after another. For each part:

1. Wait until the machine is idle (no `ConsoleReferenceServer`, no `uacompliancetest.exe` with `--settings`,
   no listener on port 62541) for about 45 s; see *Pitfalls*.
2. For the security parts, restore the PKI from `secpki.orig`.
3. Start the server from the snapshot with the part's arguments, redirecting stdout/stderr to a per-part log,
   and wait up to 150 s for `Server started`. On *"Failed to establish tcp listener"* stop it, wait 20 s and
   retry (up to five times).
4. Copy the part's selection to the run folder (the CTT rewrites it) and start the CTT with its own results
   file; wait with the part's timeout (about twice the durations above) and stop it on timeout.
5. Stop the server, and append one summary line per part (exit code, duration, counts per `testresult`) to a
   log file, so a long run can be followed and resumed.

Keep the selections, results and server logs of a run together in one folder and keep the baseline run's
folder unchanged; the comparison is only as good as the baseline.

## Pitfalls

- **Keep `<LogDir>` outside `%TEMP%`.** Windows temp cleanup deletes older files there, including the isolated
  PKI, selections and parts of project copies, in the middle of a multi-day investigation.
- Omitting `--result` silently overwrites `<Project>.results.xml`. Back it up first if the previous run matters.
- A server left running from an earlier session holds port 62541, and the CTT then tests an old build. Check
  `Get-Process ConsoleReferenceServer` and the banner sha.
- Several automated runs sharing one machine must take turns. Wait until no `ConsoleReferenceServer`, no
  `uacompliancetest.exe` with `--settings` and no listener on port 62541 has been seen for about a minute,
  check again immediately before starting the server, and treat *"Failed to establish tcp listener sockets on
  port 62541"* in the server output as "busy, retry later". Never stop a server or CTT you did not start; a
  `uacompliancetest.exe` without arguments is the GUI.
- Builds and test runs of other work on the same machine slow the server's responses; expect extra timestamp
  warnings and, in A & C, timing-dependent failures under load. Rerun a suspicious part on an idle machine
  before investigating it.
- PowerShell variable names are case-insensitive (`$t` and `$T` are the same variable), and `@( @(a) @(b) )`
  flattens; use `,@(...)` for rows of a parts table.
