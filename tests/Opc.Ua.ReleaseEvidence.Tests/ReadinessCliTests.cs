// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.ReleaseEvidence.Tests
{
    [TestFixture]
    public sealed class ReadinessCliTests
    {
        [TestCase("template", 0, "pending")]
        [TestCase("submitted", 0, "authentication-required")]
        [TestCase("stale-policy", 0, "stale")]
        [TestCase("expired", 0, "stale")]
        [TestCase("revoked", 0, "revoked")]
        [TestCase("forged-approval", 2, "")]
        [TestCase("unknown-field", 2, "")]
        [TestCase("missing-check", 2, "")]
        [TestCase("duplicate-check", 2, "")]
        [TestCase("wrong-category", 2, "")]
        [TestCase("unattributed", 2, "")]
        [TestCase("restricted-reference", 2, "")]
        [TestCase("bad-timestamp", 2, "")]
        public async Task ReadinessCannotBeEstablishedByRecordShapesAsync(
            string scenario, int expectedExit, string expectedState)
        {
            string root = FindRoot();
            string work = Directory.CreateTempSubdirectory("opcua-readiness-").FullName;
            try
            {
                JsonNode progress = JsonNode.Parse(await File.ReadAllTextAsync(
                    Path.Combine(root, ".azurepipelines", "readiness-progress.json")).ConfigureAwait(false))!;
                JsonArray records = progress["records"]!.AsArray();
                JsonNode record = records[0]!;
                switch (scenario)
                {
                    case "submitted":
                    case "stale-policy":
                    case "expired":
                        record["status"] = "submitted";
                        byte[] policy = await File.ReadAllBytesAsync(
                            Path.Combine(root, ".azurepipelines", "release-policy.json")).ConfigureAwait(false);
                        record["policyDigest"] = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(policy));
                        record["controllerSha"] = new string('a', 40);
                        record["evidenceRecord"] = "record:synthetic-evidence";
                        record["approvalRecord"] = "record:synthetic-approval";
                        record["reviewerAuthorityRecord"] = "record:synthetic-reviewer";
                        record["primaryAssignmentRecord"] = "record:synthetic-primary";
                        record["deputyAssignmentRecord"] = "record:synthetic-deputy";
                        record["reviewedAt"] = "2020-01-01T00:00:00Z";
                        record["revalidateAfter"] = scenario == "expired"
                            ? "2020-01-02T00:00:00Z" : "9998-01-01T00:00:00Z";
                        if (scenario == "stale-policy")
                        {
                            record["policyDigest"] = "sha256:" + new string('b', 64);
                        }
                        break;
                    case "revoked":
                        record["status"] = "revoked";
                        break;
                    case "forged-approval":
                        record["status"] = "approved";
                        break;
                    case "unknown-field":
                        record["operationalApprovalVerified"] = true;
                        break;
                    case "missing-check":
                        records.RemoveAt(0);
                        break;
                    case "duplicate-check":
                        records.Add(record.DeepClone());
                        break;
                    case "wrong-category":
                        record["category"] = "stewardship";
                        break;
                    case "unattributed":
                        record["status"] = "submitted";
                        break;
                    case "restricted-reference":
                        record["evidenceRecord"] = "https://example.invalid/restricted-case";
                        break;
                    case "bad-timestamp":
                        record["reviewedAt"] = "2020-99-99T00:00:00Z";
                        break;
                    case "template":
                        break;
                    default:
                        throw new ArgumentException("Unknown fixture.", nameof(scenario));
                }
                string input = Path.Combine(work, "input.json");
                string output = Path.Combine(work, "output.json");
                await File.WriteAllTextAsync(input, progress.ToJsonString()).ConfigureAwait(false);
                (int code, string log) = await RunAsync(
                    "validate-readiness", "--repository-root", root, "--input", input, "--output", output)
                    .ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(expectedExit), log);
                if (expectedExit == 0)
                {
                    JsonNode report = JsonNode.Parse(await File.ReadAllTextAsync(output).ConfigureAwait(false))!;
                    Assert.That(report["status"]!.GetValue<string>(), Is.EqualTo("pending"));
                    Assert.That(report["structurallyValid"]!.GetValue<bool>(), Is.True);
                    Assert.That(report["operationalApprovalVerified"]!.GetValue<bool>(), Is.False);
                    Assert.That(report["records"]!.AsArray(), Has.Count.EqualTo(records.Count));
                    Assert.That(report["records"]![0]!["state"]!.GetValue<string>(), Is.EqualTo(expectedState));
                }
                else
                {
                    Assert.That(File.Exists(output), Is.False,
                        "Invalid inputs must not leave a success-shaped result.");
                    Assert.That(log, Does.Not.Contain("example.invalid"));
                }
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        [TestCase("vulnerability", "2028-01-31T08:00:00Z", "2028-02-14T08:00:00Z")]
        [TestCase("incident", "2028-01-31T08:00:00Z", "2028-02-29T08:00:00Z")]
        [TestCase("incident", "2027-01-31T08:00:00Z", "2027-02-28T08:00:00Z")]
        public async Task TabletopKeepsIndependentAwarenessAndFinalClocksAsync(
            string route, string finalEvent, string finalExpected)
        {
            JsonObject request = Scenario(route);
            request["awareness"] = "2027-01-01T12:30:00+02:00";
            request[route == "incident" ? "incidentNotification" : "remedyAvailable"] = finalEvent;
            JsonObject result = await RunScenarioAsync(request).ConfigureAwait(false);
            Assert.That(ParseTime(result["earlyWarning"]!), Is.EqualTo(ParseTime("2027-01-02T10:30:00Z")));
            Assert.That(ParseTime(result["notification"]!), Is.EqualTo(ParseTime("2027-01-04T10:30:00Z")));
            Assert.That(ParseTime(result["finalReport"]!), Is.EqualTo(ParseTime(finalExpected)));
            Assert.That(result["userInformation"]!.GetValue<string>(), Is.EqualTo(
                route == "incident" ? "timely-incident-user-information" : "not-triggered-by-this-route"));
            Assert.That(result["foundationReviewRequired"]!.GetValue<bool>(), Is.True);
            Assert.That(result["operationalExerciseCompleted"]!.GetValue<bool>(), Is.False);
        }

        [Test]
        public async Task CalendarMonthUsesDeclaredZoneAcrossDaylightSavingChangeAsync()
        {
            JsonObject request = Scenario("incident");
            request["calendarTimeZone"] = "Europe/Berlin";
            request["incidentNotification"] = "2028-03-15T12:00:00+01:00";
            JsonObject result = await RunScenarioAsync(request).ConfigureAwait(false);
            Assert.That(ParseTime(result["finalReport"]!), Is.EqualTo(ParseTime("2028-04-15T12:00:00+02:00")));
            Assert.That(result["calendarTimeZone"]!.GetValue<string>(), Is.EqualTo("Europe/Berlin"));
        }

        [TestCase("vulnerability", "developmentInvolvement", "no")]
        [TestCase("vulnerability", "activelyExploited", "no")]
        [TestCase("incident", "providedDevelopmentSystem", "no")]
        [TestCase("incident", "productSecurityImpact", "no")]
        [TestCase("vulnerability", "developmentInvolvement", "unknown")]
        [TestCase("incident", "providedDevelopmentSystem", "unknown")]
        public async Task UncertainScopeKeepsProvisionalClocksWithoutClaimingMandatoryScopeAsync(
            string route, string field, string value)
        {
            bool provisional = value == "unknown";
            JsonObject request = Scenario(route);
            request[field] = value;
            JsonObject result = await RunScenarioAsync(request).ConfigureAwait(false);
            Assert.That(result["scope"]!.GetValue<string>(), Is.EqualTo(
                provisional ? "scope-review-required" : "mandatory-scope-not-established"));
            Assert.That(result.ContainsKey("earlyWarning"), Is.EqualTo(provisional));
            Assert.That(result.ContainsKey("finalReport"), Is.False);
        }

        [TestCase("voluntary", "current-secure-reporting-route")]
        [TestCase("cooperation", "retrieval-language-delivery")]
        public async Task VoluntaryAndCooperationCasesDoNotInventADeadlineAsync(string route, string record)
        {
            JsonObject request = Scenario(route);
            request.Remove("awareness");
            JsonObject result = await RunScenarioAsync(request).ConfigureAwait(false);
            Assert.That(result.ContainsKey("earlyWarning"), Is.False);
            Assert.That(result.ContainsKey("notification"), Is.False);
            Assert.That(result.ContainsKey("finalReport"), Is.False);
            Assert.That(result["clockAssumption"]!.GetValue<string>(), Is.EqualTo("no-universal-deadline-assumed"));
            Assert.That(result["requiredRecords"]!.ToJsonString(), Does.Contain(record));
        }

        [TestCase("live")]
        [TestCase("no-offset")]
        [TestCase("identifier")]
        [TestCase("wrong-route-event")]
        [TestCase("notification-before-awareness")]
        [TestCase("ambiguous-calendar-time")]
        public async Task UnsupportedScenarioCannotBecomeAnOperationalRecordAsync(string mutation)
        {
            JsonObject request = Scenario("incident");
            switch (mutation)
            {
                case "live":
                    request["synthetic"] = false;
                    break;
                case "no-offset":
                    request["awareness"] = "2028-01-01T10:30:00";
                    break;
                case "identifier":
                    request["caseIdentifier"] = "not-needed-to-start-clocks";
                    break;
                case "wrong-route-event":
                    request["remedyAvailable"] = "2028-01-01T10:30:00Z";
                    break;
                case "notification-before-awareness":
                    request["incidentNotification"] = "2020-01-01T10:30:00Z";
                    break;
                case "ambiguous-calendar-time":
                    request["calendarTimeZone"] = "Europe/Berlin";
                    request["incidentNotification"] = "2028-09-29T02:30:00+02:00";
                    break;
                default:
                    throw new ArgumentException("Unknown fixture.", nameof(mutation));
            }
            string work = Directory.CreateTempSubdirectory("opcua-tabletop-").FullName;
            try
            {
                string input = Path.Combine(work, "input.json");
                string output = Path.Combine(work, "output.json");
                await File.WriteAllTextAsync(input, request.ToJsonString()).ConfigureAwait(false);
                (int code, string log) = await RunAsync("tabletop", "--input", input, "--output", output)
                    .ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(2), log);
                Assert.That(File.Exists(output), Is.False);
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        private static JsonObject Scenario(string route)
        {
            return new JsonObject
            {
                ["schemaVersion"] = 1,
                ["synthetic"] = true,
                ["route"] = route,
                ["calendarTimeZone"] = "UTC",
                ["developmentInvolvement"] = "yes",
                ["activelyExploited"] = "yes",
                ["providedDevelopmentSystem"] = "yes",
                ["productSecurityImpact"] = "yes",
                ["awareness"] = "2027-01-01T10:30:00Z"
            };
        }

        private static async Task<JsonObject> RunScenarioAsync(JsonObject request)
        {
            string work = Directory.CreateTempSubdirectory("opcua-tabletop-").FullName;
            try
            {
                string input = Path.Combine(work, "input.json");
                string output = Path.Combine(work, "output.json");
                await File.WriteAllTextAsync(input, request.ToJsonString()).ConfigureAwait(false);
                (int code, string log) = await RunAsync("tabletop", "--input", input, "--output", output)
                    .ConfigureAwait(false);
                Assert.That(code, Is.Zero, log);
                return JsonNode.Parse(await File.ReadAllTextAsync(output).ConfigureAwait(false))!.AsObject();
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        private static DateTimeOffset ParseTime(JsonNode value)
        {
            return ParseTime(value.GetValue<string>());
        }

        private static DateTimeOffset ParseTime(string value)
        {
            return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
        }

        private static string FindRoot()
        {
            DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
            {
                directory = directory.Parent;
            }
            return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root is required.");
        }

        private static async Task<(int Code, string Output)> RunAsync(params string[] arguments)
        {
            string configuration = new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Parent!.Name;
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add(Path.Combine(FindRoot(), "tools", "Opc.Ua.ReleaseEvidence",
                "bin", configuration, "net10.0", "Opc.Ua.ReleaseEvidence.dll"));
            foreach (string argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }
            process.Start();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            return (process.ExitCode,
                await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false));
        }
    }
}
