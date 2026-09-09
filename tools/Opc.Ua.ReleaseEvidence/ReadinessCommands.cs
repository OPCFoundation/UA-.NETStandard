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
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Json.Schema;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Validates readiness handoff structure and calculates synthetic stewardship scenarios without granting approval.
    /// </summary>
    internal sealed class ReadinessCommands
    {
        /// <summary>
        /// Creates readiness command handlers using the supplied evidence operations and review-validation clock.
        /// </summary>
        public ReadinessCommands(EvidenceFiles files, TimeProvider timeProvider)
        {
            m_files = files ?? throw new ArgumentNullException(nameof(files));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        /// <summary>
        /// Adds readiness validation and synthetic tabletop commands, optionally using an injected validation service.
        /// </summary>
        public static void AddTo(RootCommand root, ReadinessCommands? service = null)
        {
            ArgumentNullException.ThrowIfNull(root);
            ReadinessCommands handler = service ?? new ReadinessCommands(new EvidenceFiles(), TimeProvider.System);
            var validate = new Command("validate-readiness", "Validate handoff structure; never grants approval.");
            var repository = new Option<string>("--repository-root") { Required = true };
            var input = new Option<string>("--input") { Required = true };
            var output = new Option<string>("--output") { Required = true };
            validate.Options.Add(repository);
            validate.Options.Add(input);
            validate.Options.Add(output);
            validate.SetAction(async (parse, cancellationToken) =>
            {
                try
                {
                    await handler.ValidateAsync(
                        parse.GetRequiredValue(repository), parse.GetRequiredValue(input),
                        parse.GetRequiredValue(output), cancellationToken).ConfigureAwait(false);
                    return 0;
                }
                catch (Exception ex) when (IsInputError(ex))
                {
                    await Console.Error.WriteLineAsync("Invalid readiness input; no approval was established.")
                        .ConfigureAwait(false);
                    return 2;
                }
            });
            root.Subcommands.Add(validate);

            var tabletop = new Command("tabletop", "Calculate synthetic steward scenarios for Foundation review.");
            var scenario = new Option<string>("--input") { Required = true };
            var result = new Option<string>("--output") { Required = true };
            tabletop.Options.Add(scenario);
            tabletop.Options.Add(result);
            tabletop.SetAction(async (parse, cancellationToken) =>
            {
                try
                {
                    var files = new EvidenceFiles();
                    TabletopRequest request = await files.ReadModelAsync(
                        parse.GetRequiredValue(scenario), ReadinessJsonContext.Default.TabletopRequest,
                        cancellationToken).ConfigureAwait(false);
                    await files.WriteModelAsync(
                        parse.GetRequiredValue(result), Calculate(request),
                        ReadinessJsonContext.Default.TabletopResult, cancellationToken).ConfigureAwait(false);
                    return 0;
                }
                catch (Exception ex) when (IsInputError(ex))
                {
                    await Console.Error.WriteLineAsync("Invalid synthetic scenario; no operational decision was made.")
                        .ConfigureAwait(false);
                    return 2;
                }
            });
            root.Subcommands.Add(tabletop);
        }

        private async Task ValidateAsync(
            string repositoryRoot,
            string input,
            string output,
            CancellationToken cancellationToken)
        {
            EvidenceFiles files = m_files;
            string schemaPath = EvidenceFiles.Confined(repositoryRoot, ".azurepipelines/readiness-record.schema.json");
            using JsonDocument schemaDocument = await files.ReadJsonAsync(schemaPath, cancellationToken)
                .ConfigureAwait(false);
            using JsonDocument document = await files.ReadJsonAsync(input, cancellationToken).ConfigureAwait(false);
            JsonSchema schema = JsonSchema.FromText(schemaDocument.RootElement.GetRawText(), new BuildOptions
            {
                SchemaRegistry = new SchemaRegistry
                {
                    Fetch = (_, _) => throw new InvalidDataException("Unregistered offline readiness schema.")
                }
            });
            if (!schema.Evaluate(document.RootElement).IsValid)
            {
                throw new InvalidDataException("Readiness schema validation failed.");
            }
            ReadinessProgress progress = document.Deserialize(ReadinessJsonContext.Default.ReadinessProgress) ??
                throw new JsonException("Missing readiness progress.");
            string policyPath = EvidenceFiles.Confined(repositoryRoot, progress.PolicyPath);
            using JsonDocument policy = await files.ReadJsonAsync(policyPath, cancellationToken).ConfigureAwait(false);
            string digest = await files.DigestAsync(policyPath, cancellationToken).ConfigureAwait(false);
            var required = policy.RootElement.GetProperty("graduation").GetProperty("requiredChecks")
                .EnumerateArray().Select(element => element.GetString()!).ToHashSet(StringComparer.Ordinal);
            var stewardship = new HashSet<string>(StringComparer.Ordinal)
            {
                "programme-approval", "functional-assignments", "development-system-scope",
                "cooperation-retrieval", "reporting-access", "reporting-cooperation-exercises"
            };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var checks = new List<ReadinessCheck>();
            DateTimeOffset now = m_timeProvider.GetUtcNow();
            foreach (ReadinessItem record in progress.Records)
            {
                if (!seen.Add(record.Id) ||
                    !(record.Category == "graduation" ? required : stewardship).Remove(record.Id))
                {
                    throw new InvalidDataException("Unknown, duplicate or incorrectly categorized readiness check.");
                }
                if (record.Category == "graduation" && (record.Groups.Count == 0 || record.Destinations.Count == 0))
                {
                    throw new InvalidDataException("Graduation records require group and destination scope.");
                }
                string state = record.Status;
                if (record.ReviewedAt != null)
                {
                    _ = ParseTime(record.ReviewedAt);
                }
                if (record.RevalidateAfter != null)
                {
                    _ = ParseTime(record.RevalidateAfter);
                }
                if (record.Status == "submitted")
                {
                    if (record.PolicyDigest == null || record.ControllerSha == null ||
                        record.EvidenceRecord == null || record.ApprovalRecord == null ||
                        record.ReviewerAuthorityRecord == null || record.PrimaryAssignmentRecord == null ||
                        record.DeputyAssignmentRecord == null ||
                        record.ReviewedAt == null || record.RevalidateAfter == null)
                    {
                        throw new InvalidDataException("Submitted records require attributable, scoped references.");
                    }
                    DateTimeOffset reviewed = ParseTime(record.ReviewedAt);
                    DateTimeOffset expires = ParseTime(record.RevalidateAfter);
                    if (reviewed > now || expires <= reviewed)
                    {
                        throw new InvalidDataException("Invalid readiness review interval.");
                    }
                    state = record.PolicyDigest != digest || expires <= now ? "stale" : "authentication-required";
                }
                checks.Add(new ReadinessCheck(record.Id, state));
            }
            if (required.Count != 0 || stewardship.Count != 0)
            {
                throw new InvalidDataException("The progress record omits required handoffs.");
            }
            await files.WriteModelAsync(
                output, new ReadinessValidation(1, "pending", true, false, digest, checks),
                ReadinessJsonContext.Default.ReadinessValidation, cancellationToken).ConfigureAwait(false);
        }

        private static TabletopResult Calculate(TabletopRequest request)
        {
            if (request.SchemaVersion != 1 || !request.Synthetic ||
                request.Route is not ("vulnerability" or "incident" or "voluntary" or "cooperation") ||
                !IsScopeValue(request.DevelopmentInvolvement) || !IsScopeValue(request.ActivelyExploited) ||
                !IsScopeValue(request.ProvidedDevelopmentSystem) || !IsScopeValue(request.ProductSecurityImpact))
            {
                throw new InvalidDataException("Unsupported or non-synthetic scenario.");
            }
            TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(request.CalendarTimeZone);
            if (request.Route is "voluntary" or "cooperation")
            {
                if (request.Awareness != null ||
                    request.RemedyAvailable != null ||
                    request.IncidentNotification != null)
                {
                    throw new InvalidDataException("This route does not define these mandatory reporting clocks.");
                }
                return new TabletopResult(
                    1, true, request.Route, "not-triggered-by-this-route",
                    "no-universal-deadline-assumed", timeZone.Id, null, null, null, true, false,
                    request.Route == "cooperation"
                        ? ["authenticated-request", "approved-policy-revision", "retrieval-language-delivery"]
                        : ["voluntary-scope-decision", "current-secure-reporting-route"]);
            }
            DateTimeOffset awareness = ParseTime(request.Awareness);
            string scope = request.Route == "vulnerability"
                ? AssessScope(request.DevelopmentInvolvement, request.ActivelyExploited)
                : AssessScope(request.ProvidedDevelopmentSystem, request.ProductSecurityImpact);
            if ((request.Route == "vulnerability" && request.IncidentNotification != null) ||
                (request.Route == "incident" && request.RemedyAvailable != null))
            {
                throw new InvalidDataException("Final-report events belong to different reporting routes.");
            }
            string? final = null;
            string basis = "24-and-72-elapsed-hours-from-awareness;14-elapsed-days-from-remedy";
            if (request.RemedyAvailable != null)
            {
                final = Format(ParseTime(request.RemedyAvailable).ToUniversalTime().AddDays(14));
            }
            if (request.Route == "incident")
            {
                basis = "24-and-72-elapsed-hours-from-awareness;calendar-month-in-declared-zone;end-of-month-clamp";
                if (request.IncidentNotification != null)
                {
                    DateTimeOffset notification = ParseTime(request.IncidentNotification);
                    if (notification < awareness)
                    {
                        throw new InvalidDataException("Incident notification precedes awareness.");
                    }
                    DateTime local = DateTime.SpecifyKind(
                        TimeZoneInfo.ConvertTime(notification, timeZone).DateTime.AddMonths(1),
                        DateTimeKind.Unspecified);
                    if (timeZone.IsAmbiguousTime(local) || timeZone.IsInvalidTime(local))
                    {
                        throw new InvalidDataException("Calendar deadline needs an explicit DST interpretation.");
                    }
                    final = Format(new DateTimeOffset(local, timeZone.GetUtcOffset(local)));
                }
            }
            bool potentialClock = scope != "mandatory-scope-not-established";
            return new TabletopResult(
                1, true, scope,
                request.Route != "incident" || !potentialClock ? "not-triggered-by-this-route" :
                    scope == "mandatory-scope-established"
                        ? "timely-incident-user-information" : "scope-review-required",
                basis,
                timeZone.Id,
                potentialClock ? Format(awareness.ToUniversalTime().AddHours(24)) : null,
                potentialClock ? Format(awareness.ToUniversalTime().AddHours(72)) : null,
                potentialClock ? final : null,
                true, false,
                ["scope-and-awareness-facts", "independent-stage-records", "foundation-legal-review"]);
        }

        private static string AssessScope(string first, string second)
        {
            if (first == "no" || second == "no")
            {
                return "mandatory-scope-not-established";
            }
            return first == "yes" && second == "yes" ? "mandatory-scope-established" : "scope-review-required";
        }

        private static bool IsScopeValue(string value)
        {
            return value is "yes" or "no" or "unknown";
        }

        private static DateTimeOffset ParseTime(string? value)
        {
            if (!DateTimeOffset.TryParseExact(
                value,
                ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
                    "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset timestamp))
            {
                throw new InvalidDataException("Timestamps require an explicit ISO 8601 offset.");
            }
            return timestamp;
        }

        private static string Format(DateTimeOffset value)
        {
            return value.ToString("O", CultureInfo.InvariantCulture);
        }

        private static bool IsInputError(Exception exception)
        {
            return exception is IOException or InvalidDataException or JsonException or ArgumentException or
                InvalidOperationException or
                UnauthorizedAccessException or TimeZoneNotFoundException or InvalidTimeZoneException;
        }

        private readonly EvidenceFiles m_files;
        private readonly TimeProvider m_timeProvider;
    }
}
