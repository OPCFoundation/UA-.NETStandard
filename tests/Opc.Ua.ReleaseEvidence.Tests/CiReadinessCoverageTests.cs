/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.ReleaseEvidence.Tests
{
    /// <summary>
    /// Exercises readiness and synthetic reporting through the in-process parser without claiming approval.
    /// </summary>
    [TestFixture]
    public sealed class CiReadinessCoverageTests
    {
        /// <summary>
        /// Verifies that all required handoffs are retained and submitted record shapes never authenticate approval.
        /// </summary>
        [TestCase("pending", "pending")]
        [TestCase("submitted", "authentication-required")]
        [TestCase("expired", "stale")]
        [TestCase("stale-policy", "stale")]
        [TestCase("revoked", "revoked")]
        public async Task ReadinessPreservesHandoffsWithoutAuthenticatingRecordShapesAsync(
            string scenario, string expectedState)
        {
            using var work = new CiEvidenceWorkspace();
            JsonObject progress = await ReadProgressAsync().ConfigureAwait(false);
            JsonArray records = progress["records"]!.AsArray();
            JsonObject record = records[0]!.AsObject();
            string policyDigest = CiEvidenceWorkspace.Hash(await File.ReadAllBytesAsync(Path.Combine(
                CiEvidenceWorkspace.RepositoryRoot, ".azurepipelines", "release-policy.json")).ConfigureAwait(false));
            if (scenario is "submitted" or "expired" or "stale-policy")
            {
                Submit(record, policyDigest);
                if (scenario == "expired")
                {
                    record["revalidateAfter"] = "2020-01-02T00:00:00Z";
                }
                else if (scenario == "stale-policy")
                {
                    record["policyDigest"] = "sha256:" + new string('0', 64);
                }
            }
            else
            {
                record["status"] = scenario;
            }
            await work.WriteJsonAsync("readiness.json", progress).ConfigureAwait(false);
            int code = await Program.Main(
            [
                "validate-readiness", "--repository-root", CiEvidenceWorkspace.RepositoryRoot,
                "--input", work.At("readiness.json"), "--output", work.At("report.json")
            ]).ConfigureAwait(false);
            ReadinessValidation report = await work.Files.ReadModelAsync(
                work.At("report.json"), ReadinessJsonContext.Default.ReadinessValidation,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(code, Is.Zero);
            Assert.That(report.SchemaVersion, Is.EqualTo(1));
            Assert.That(report.Status, Is.EqualTo("pending"));
            Assert.That(report.StructurallyValid, Is.True);
            Assert.That(report.OperationalApprovalVerified, Is.False);
            Assert.That(report.PolicyDigest, Is.EqualTo(policyDigest));
            Assert.That(report.Records.Select(r => r.Id),
                Is.EqualTo(records.Select(r => r!["id"]!.GetValue<string>())));
            Assert.That(report.Records[0].State, Is.EqualTo(expectedState));
            Assert.That(report.Records.Skip(1).Select(r => r.State),
                Is.EqualTo(records.Skip(1).Select(r => r!["status"]!.GetValue<string>())));
        }

        /// <summary>
        /// Verifies incomplete scope, invalid intervals, and invented approvals fail without a readiness report.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The fixture scenario is unsupported.</exception>
        [TestCase("missing")]
        [TestCase("duplicate")]
        [TestCase("category")]
        [TestCase("scope")]
        [TestCase("unattributed")]
        [TestCase("invented-approval")]
        [TestCase("unknown-field")]
        [TestCase("empty-interval")]
        [TestCase("future-review")]
        public async Task ReadinessRejectsInvalidHandoffsWithoutWritingApprovalAsync(string scenario)
        {
            using var work = new CiEvidenceWorkspace();
            JsonObject progress = await ReadProgressAsync().ConfigureAwait(false);
            JsonArray records = progress["records"]!.AsArray();
            JsonObject record = records[0]!.AsObject();
            switch (scenario)
            {
                case "missing":
                    records.RemoveAt(0);
                    break;
                case "duplicate":
                    records.Add(record.DeepClone());
                    break;
                case "category":
                    record["category"] = "stewardship";
                    break;
                case "scope":
                    record["groups"] = new JsonArray();
                    break;
                case "unattributed":
                    record["status"] = "submitted";
                    break;
                case "invented-approval":
                    record["status"] = "approved";
                    break;
                case "unknown-field":
                    record["operationalApprovalVerified"] = true;
                    break;
                case "empty-interval":
                case "future-review":
                    Submit(record, CiEvidenceWorkspace.Hash(await File.ReadAllBytesAsync(Path.Combine(
                        CiEvidenceWorkspace.RepositoryRoot, ".azurepipelines", "release-policy.json"))
                        .ConfigureAwait(false)));
                    if (scenario == "empty-interval")
                    {
                        record["revalidateAfter"] = "2020-01-01T00:00:00Z";
                    }
                    else
                    {
                        record["reviewedAt"] = "9997-01-01T00:00:00Z";
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }
            await work.WriteJsonAsync("readiness.json", progress).ConfigureAwait(false);
            int code = await Program.Main(
            [
                "validate-readiness", "--repository-root", CiEvidenceWorkspace.RepositoryRoot,
                "--input", work.At("readiness.json"), "--output", work.At("report.json")
            ]).ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(2));
            Assert.That(File.Exists(work.At("report.json")), Is.False);
        }

        /// <summary>
        /// Verifies independent elapsed deadlines and calendar-month clamping across leap years and daylight saving.
        /// </summary>
        [TestCaseSource(nameof(s_clockCases))]
        public async Task SyntheticClocksPreserveIndependentEventsAndCalendarBoundariesAsync(
            string route, string zone, string finalEvent, string expectedFinal)
        {
            TabletopRequest request = Scenario(route) with
            {
                CalendarTimeZone = zone,
                RemedyAvailable = route == "vulnerability" ? finalEvent : null,
                IncidentNotification = route == "incident" ? finalEvent : null
            };
            TabletopResult report = await CalculateAsync(request).ConfigureAwait(false);
            Assert.That(report.Scope, Is.EqualTo("mandatory-scope-established"));
            Assert.That(ParseTime(report.EarlyWarning!), Is.EqualTo(ParseTime("2027-01-02T10:30:00Z")));
            Assert.That(ParseTime(report.Notification!), Is.EqualTo(ParseTime("2027-01-04T10:30:00Z")));
            Assert.That(ParseTime(report.FinalReport!), Is.EqualTo(ParseTime(expectedFinal)));
            Assert.That(ParseTime(report.FinalReport!).Offset, Is.EqualTo(ParseTime(expectedFinal).Offset));
            Assert.That(report.CalendarTimeZone, Is.EqualTo(zone));
            Assert.That(report.UserInformation, Is.EqualTo(route == "incident"
                ? "timely-incident-user-information" : "not-triggered-by-this-route"));
            Assert.That(report.ClockAssumption, Is.EqualTo(route == "incident"
                ? "24-and-72-elapsed-hours-from-awareness;calendar-month-in-declared-zone;end-of-month-clamp"
                : "24-and-72-elapsed-hours-from-awareness;14-elapsed-days-from-remedy"));
            Assert.That(report.RequiredRecords, Is.EqualTo(s_requiredReportingRecords));
            AssertNonOperational(report);
        }

        /// <summary>
        /// Verifies negative scope takes precedence over unknown facts and unknown scope retains provisional clocks.
        /// </summary>
        [TestCaseSource(nameof(s_scopeCases))]
        public async Task SyntheticScopeDistinguishesExcludedAndProvisionalReportingAsync(
            string route, string first, string second, bool provisional)
        {
            TabletopRequest request = Scenario(route) with
            {
                DevelopmentInvolvement = first,
                ActivelyExploited = second,
                ProvidedDevelopmentSystem = first,
                ProductSecurityImpact = second
            };
            TabletopResult report = await CalculateAsync(request).ConfigureAwait(false);
            Assert.That(report.Scope, Is.EqualTo(provisional
                ? "scope-review-required" : "mandatory-scope-not-established"));
            Assert.That(report.FinalReport, Is.Null);
            Assert.That(report.UserInformation, Is.EqualTo(route == "incident" && provisional
                ? "scope-review-required" : "not-triggered-by-this-route"));
            if (provisional)
            {
                Assert.That(ParseTime(report.EarlyWarning!), Is.EqualTo(ParseTime("2027-01-02T10:30:00Z")));
                Assert.That(ParseTime(report.Notification!), Is.EqualTo(ParseTime("2027-01-04T10:30:00Z")));
            }
            else
            {
                Assert.That(report.EarlyWarning, Is.Null);
                Assert.That(report.Notification, Is.Null);
            }
            AssertNonOperational(report);
        }

        /// <summary>
        /// Verifies voluntary and cooperation routes retain their own record requirements without mandatory clocks.
        /// </summary>
        [TestCase("voluntary")]
        [TestCase("cooperation")]
        public async Task NonMandatoryRoutesDoNotInventDeadlinesAsync(string route)
        {
            TabletopResult report = await CalculateAsync(Scenario(route) with { Awareness = null })
                .ConfigureAwait(false);
            Assert.That(report.Scope, Is.EqualTo(route));
            Assert.That(report.ClockAssumption, Is.EqualTo("no-universal-deadline-assumed"));
            Assert.That(report.UserInformation, Is.EqualTo("not-triggered-by-this-route"));
            Assert.That(report.EarlyWarning, Is.Null);
            Assert.That(report.Notification, Is.Null);
            Assert.That(report.FinalReport, Is.Null);
            string[] expectedRecords = route == "cooperation"
                ? ["authenticated-request", "approved-policy-revision", "retrieval-language-delivery"]
                : ["voluntary-scope-decision", "current-secure-reporting-route"];
            Assert.That(report.RequiredRecords, Is.EqualTo(expectedRecords));
            AssertNonOperational(report);
        }

        /// <summary>
        /// Verifies live, inconsistent, or ambiguous scenarios never leave an operational-looking output document.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The fixture scenario is unsupported.</exception>
        [TestCase("live")]
        [TestCase("route")]
        [TestCase("scope")]
        [TestCase("no-offset")]
        [TestCase("wrong-event")]
        [TestCase("notification-before-awareness")]
        [TestCase("ambiguous-calendar")]
        [TestCase("invalid-calendar")]
        [TestCase("voluntary-clock")]
        public async Task InvalidSyntheticScenariosLeaveNoOperationalReportAsync(string scenario)
        {
            using var work = new CiEvidenceWorkspace();
            TabletopRequest request = Scenario("incident");
            request = scenario switch
            {
                "live" => request with { Synthetic = false },
                "route" => request with { Route = "unsupported" },
                "scope" => request with { ProductSecurityImpact = "maybe" },
                "no-offset" => request with { Awareness = "2027-01-01T10:30:00" },
                "wrong-event" => request with { RemedyAvailable = "2028-01-31T08:00:00Z" },
                "notification-before-awareness" => request with { IncidentNotification = "2020-01-01T10:30:00Z" },
                "ambiguous-calendar" => request with
                {
                    CalendarTimeZone = "Europe/Berlin",
                    IncidentNotification = "2028-09-29T02:30:00+02:00"
                },
                "invalid-calendar" => request with
                {
                    CalendarTimeZone = "Europe/Berlin",
                    IncidentNotification = "2028-02-26T02:30:00+01:00"
                },
                "voluntary-clock" => request with { Route = "voluntary" },
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            await work.WriteModelAsync("scenario.json", request, ReadinessJsonContext.Default.TabletopRequest)
                .ConfigureAwait(false);
            int code = await Program.Main(
            [
                "tabletop", "--input", work.At("scenario.json"), "--output", work.At("report.json")
            ]).ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(2));
            Assert.That(File.Exists(work.At("report.json")), Is.False);
        }

        private static async Task<JsonObject> ReadProgressAsync()
        {
            return JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(
                CiEvidenceWorkspace.RepositoryRoot, ".azurepipelines", "readiness-progress.json"))
                .ConfigureAwait(false))!.AsObject();
        }

        private static void Submit(JsonObject record, string policyDigest)
        {
            record["status"] = "submitted";
            record["policyDigest"] = policyDigest;
            record["controllerSha"] = new string('a', 40);
            record["evidenceRecord"] = "record:synthetic-evidence";
            record["approvalRecord"] = "record:synthetic-approval";
            record["reviewerAuthorityRecord"] = "record:synthetic-reviewer";
            record["primaryAssignmentRecord"] = "record:synthetic-primary";
            record["deputyAssignmentRecord"] = "record:synthetic-deputy";
            record["reviewedAt"] = "2020-01-01T00:00:00Z";
            record["revalidateAfter"] = "9998-01-01T00:00:00Z";
        }

        private static TabletopRequest Scenario(string route)
        {
            return new TabletopRequest
            {
                SchemaVersion = 1,
                Synthetic = true,
                Route = route,
                CalendarTimeZone = "UTC",
                DevelopmentInvolvement = "yes",
                ActivelyExploited = "yes",
                ProvidedDevelopmentSystem = "yes",
                ProductSecurityImpact = "yes",
                Awareness = "2027-01-01T12:30:00+02:00"
            };
        }

        private static async Task<TabletopResult> CalculateAsync(TabletopRequest request)
        {
            using var work = new CiEvidenceWorkspace();
            await work.WriteModelAsync("scenario.json", request, ReadinessJsonContext.Default.TabletopRequest)
                .ConfigureAwait(false);
            int code = await Program.Main(
            [
                "tabletop", "--input", work.At("scenario.json"), "--output", work.At("report.json")
            ]).ConfigureAwait(false);
            Assert.That(code, Is.Zero);
            return await work.Files.ReadModelAsync(
                work.At("report.json"), ReadinessJsonContext.Default.TabletopResult,
                CancellationToken.None).ConfigureAwait(false);
        }

        private static DateTimeOffset ParseTime(string value)
        {
            return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
        }

        private static void AssertNonOperational(TabletopResult report)
        {
            Assert.That(report.SchemaVersion, Is.EqualTo(1));
            Assert.That(report.Synthetic, Is.True);
            Assert.That(report.FoundationReviewRequired, Is.True);
            Assert.That(report.OperationalExerciseCompleted, Is.False);
        }

        private static readonly TestCaseData[] s_clockCases =
        [
            new("vulnerability", "UTC", "2028-01-31T08:00:00Z", "2028-02-14T08:00:00Z"),
            new("incident", "UTC", "2028-01-31T08:00:00Z", "2028-02-29T08:00:00Z"),
            new("incident", "UTC", "2027-01-31T08:00:00Z", "2027-02-28T08:00:00Z"),
            new("incident", "Europe/Berlin", "2028-03-15T12:00:00+01:00", "2028-04-15T12:00:00+02:00")
        ];

        private static readonly TestCaseData[] s_scopeCases =
        [
            new("vulnerability", "no", "yes", false),
            new("vulnerability", "yes", "no", false),
            new("vulnerability", "unknown", "yes", true),
            new("incident", "yes", "unknown", true),
            new("incident", "unknown", "no", false),
            new("incident", "no", "unknown", false)
        ];

        private static readonly string[] s_requiredReportingRecords =
            ["scope-and-awareness-facts", "independent-stage-records", "foundation-legal-review"];
    }
}
