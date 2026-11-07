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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Sync
{
    public sealed partial class XRegistrySynchronizer
    {
        private async ValueTask RecoverAsync(Pass pass, CancellationToken cancellationToken)
        {
            foreach (XRegistrySyncIntent intent in pass.State.Intents.Values.Where(intent => intent.Pending)
                .OrderBy(intent => intent.Path, StringComparer.Ordinal)
                .ThenBy(intent => intent.Id, StringComparer.Ordinal).ToArray())
            {
                if (!TakeOperation(pass, intent.Path) || !pass.ScopeValid)
                {
                    break;
                }
                if (pass.DryRun)
                {
                    pass.Records.Add(new XRegistrySyncRecord(intent.Path, XRegistrySyncRecordKind.Pending,
                        "Dry run leaves the durable intent and its outcome unchanged.")
                    { OperationId = intent.Id });
                    continue;
                }
                try
                {
                    await RecoverIntentAsync(pass, intent, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (!pass.StateCommitInProgress &&
                    XRegistrySyncInventoryReader.IsInventoryFailure(exception, cancellationToken))
                {
                    Hold(pass, intent, "Outcome/readback is unavailable: " + exception.Message);
                    await SaveAsync(pass, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async ValueTask RecoverIntentAsync(
            Pass pass,
            XRegistrySyncIntent intent,
            CancellationToken cancellationToken)
        {
            if (!await CheckScopeAsync(pass, cancellationToken).ConfigureAwait(false))
            {
                await SaveAsync(pass, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (intent.State == XRegistrySyncIntentState.Prepared && intent.Request.OperationId is not null)
            {
                XRegistryOperationOutcome outcome = await pass.Reader(intent.Destination)
                    .OutcomeAsync(intent.Request.OperationId, cancellationToken).ConfigureAwait(false);
                if (outcome.State == XRegistryOperationState.Rejected && outcome.Response is { IsSuccess: false })
                {
                    pass.State.Intents[intent.Id] = intent with
                    {
                        State = XRegistrySyncIntentState.Rejected,
                        Response = outcome.Response
                    };
                    pass.Dirty = true;
                    AddConflict(pass, intent.Path, "mutation_rejected",
                        "The operation journal confirms rejection. No operation is repeated.", intent.Id);
                    await SaveAsync(pass, cancellationToken).ConfigureAwait(false);
                    return;
                }
                if (outcome.State == XRegistryOperationState.Committed && outcome.Response is { IsSuccess: true })
                {
                    intent = intent with { State = XRegistrySyncIntentState.Responded, Response = outcome.Response };
                    pass.State.Intents[intent.Id] = intent;
                    pass.Dirty = true;
                    await SaveAsync(pass, cancellationToken).ConfigureAwait(false);
                }
            }
            if (intent.Kind == XRegistrySyncIntentKind.Create && !KnownCommitted(intent))
            {
                Hold(pass, intent,
                    "Creation/assigned-Version outcome is ambiguous; matching content cannot attribute creation.");
                await SaveAsync(pass, cancellationToken).ConfigureAwait(false);
                return;
            }
            await VerifyIntentAsync(pass, intent, sentThisPass: false, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask VerifyIntentAsync(
            Pass pass,
            XRegistrySyncIntent intent,
            bool sentThisPass,
            CancellationToken cancellationToken)
        {
            if (!await CheckScopeAsync(pass, cancellationToken).ConfigureAwait(false))
            {
                await SaveAsync(pass, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (intent.Kind == XRegistrySyncIntentKind.Delete)
            {
                await VerifyDeletionAsync(pass, intent, sentThisPass, cancellationToken).ConfigureAwait(false);
                return;
            }
            XRegistrySyncInventory origin = pass.Inventory(Other(intent.Destination));
            XRegistrySyncInventory target = pass.Inventory(intent.Destination);
            var verified = new List<XRegistrySyncBaseline>();
            bool sourceChanged = false;
            bool destinationChanged = false;
            foreach (XRegistrySyncObservation expected in intent.Source.Span.ToArray())
            {
                XRegistrySyncObservation? source = await pass.Reader(Other(intent.Destination))
                    .ReadEntityAsync(expected.Path, origin.Model!, cancellationToken).ConfigureAwait(false);
                XRegistrySyncObservation? destination = await pass.Reader(intent.Destination)
                    .ReadEntityAsync(expected.Path, target.Model!, cancellationToken).ConfigureAwait(false);
                SetObservation(origin, expected.Path, source);
                SetObservation(target, expected.Path, destination);
                bool sourceMatches = source?.Fingerprint == expected.Fingerprint;
                bool destinationMatches = destination?.Fingerprint == expected.Fingerprint;
                XRegistrySyncObservation? before = intent.DestinationBefore.Span.ToArray().FirstOrDefault(
                    observation => observation.Path == expected.Path);
                if (before is not null &&
                    before.Fingerprint != expected.Fingerprint &&
                    before.Epoch == destination?.Epoch)
                {
                    destinationMatches = false;
                }
                sourceChanged |= !sourceMatches;
                destinationChanged |= !destinationMatches;
                if (sourceMatches && destinationMatches)
                {
                    verified.Add(intent.Destination == XRegistrySyncSide.Http
                        ? new XRegistrySyncBaseline(source!, destination!, m_timeProvider.GetUtcNow())
                        : new XRegistrySyncBaseline(destination!, source!, m_timeProvider.GetUtcNow()));
                }
            }
            if (sourceChanged || destinationChanged)
            {
                if ((KnownCommitted(intent) && intent.Kind != XRegistrySyncIntentKind.Create) ||
                    (!destinationChanged && intent.Kind == XRegistrySyncIntentKind.Replace))
                {
                    pass.State.Intents[intent.Id] = intent with { State = XRegistrySyncIntentState.Conflicted };
                    pass.Dirty = true;
                    AddConflict(pass, intent.Path, "verification_changed",
                        "Readback observed a newer source/destination state. No new baseline or forced retry is made.",
                        intent.Id);
                    foreach (XRegistrySyncObservation observation in intent.Source)
                    {
                        pass.Processed.Add(observation.Path);
                    }
                }
                else
                {
                    Hold(pass, intent,
                        "Creation identity or current observations remain unverified; the operation stays pending.");
                }
                await SaveAsync(pass, cancellationToken).ConfigureAwait(false);
                return;
            }
            pass.State.Intents[intent.Id] = intent with { State = XRegistrySyncIntentState.Verified };
            pass.Dirty = true;
            foreach (XRegistrySyncBaseline baseline in verified)
            {
                EstablishBaseline(pass, baseline.OpcUa, baseline.Http);
                pass.Processed.Add(baseline.OpcUa.Path);
            }
            pass.Records.Add(new XRegistrySyncRecord(intent.Path,
                sentThisPass ? XRegistrySyncRecordKind.Applied : XRegistrySyncRecordKind.Converged,
                KnownCommitted(intent)
                    ? "The recorded response and both live readbacks are verified."
                    : "Readback proves current convergence, not attribution of an unobserved response.")
            {
                OperationId = intent.Id
            });
            await SaveAsync(pass, cancellationToken).ConfigureAwait(false);
            await RefreshAncestorsAsync(pass, intent, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask VerifyDeletionAsync(
            Pass pass,
            XRegistrySyncIntent intent,
            bool sentThisPass,
            CancellationToken cancellationToken)
        {
            if (!pass.Complete)
            {
                Hold(pass, intent, "Deletion cannot be acknowledged from an incomplete inventory.");
                await SaveAsync(pass, cancellationToken).ConfigureAwait(false);
                return;
            }
            string[] paths = [.. intent.DestinationBefore.Span.ToArray()
                .Where(entry => Within(entry.Path, intent.Request.Path))
                .Select(entry => entry.Path).Append(intent.Path).Distinct(StringComparer.Ordinal)];
            bool allAbsent = true;
            foreach (string path in paths)
            {
                XRegistrySyncObservation? native = await pass.Reader(XRegistrySyncSide.OpcUa)
                    .ReadEntityAsync(path, pass.Native.Model!, cancellationToken).ConfigureAwait(false);
                XRegistrySyncObservation? http = await pass.Reader(XRegistrySyncSide.Http)
                    .ReadEntityAsync(path, pass.Http.Model!, cancellationToken).ConfigureAwait(false);
                SetObservation(pass.Native, path, native);
                SetObservation(pass.Http, path, http);
                allAbsent &= native is null && http is null;
            }
            if (!allAbsent)
            {
                if (KnownCommitted(intent))
                {
                    pass.State.Intents[intent.Id] = intent with { State = XRegistrySyncIntentState.Conflicted };
                    pass.Dirty = true;
                    AddConflict(pass, intent.Path, "verification_changed",
                        "The deletion completed but an identity now exists. It is not deleted again.", intent.Id);
                    foreach (string path in paths)
                    {
                        pass.Processed.Add(path);
                    }
                }
                else
                {
                    Hold(pass, intent, "Deletion outcome remains ambiguous; no repeated DELETE is sent.");
                }
                await SaveAsync(pass, cancellationToken).ConfigureAwait(false);
                return;
            }
            pass.State.Intents[intent.Id] = intent with { State = XRegistrySyncIntentState.Verified };
            pass.Dirty = true;
            foreach (string path in paths)
            {
                if (pass.State.Baselines.TryGetValue(path, out XRegistrySyncBaseline? baseline))
                {
                    Tombstone(pass, path, baseline);
                }
                ResolveConflicts(pass, path);
                pass.Processed.Add(path);
            }
            pass.Records.Add(new XRegistrySyncRecord(intent.Path,
                sentThisPass ? XRegistrySyncRecordKind.Deleted : XRegistrySyncRecordKind.Converged,
                "Both complete inventories and current reads confirm absence; the tombstone is retained.")
            {
                OperationId = intent.Id
            });
            await SaveAsync(pass, cancellationToken).ConfigureAwait(false);
            await RefreshAncestorsAsync(pass, intent, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask RefreshAncestorsAsync(
            Pass pass,
            XRegistrySyncIntent intent,
            CancellationToken cancellationToken)
        {
            ArrayOf<string> segments = XRegistryPath.GetSegments(intent.Path);
            var ancestors = new List<string>();
            if (segments.Count >= 4)
            {
                ancestors.Add(XRegistrySyncModel.ResourcePath(intent.Path) + "/meta");
            }
            if (segments.Count > 2)
            {
                ancestors.Add(XRegistryPath.FromSegments([segments[0], segments[1]]));
            }
            if (segments.Count != 0)
            {
                ancestors.Add("/");
            }
            foreach (string path in ancestors)
            {
                if (intent.Kind == XRegistrySyncIntentKind.Delete && Within(path, intent.Request.Path))
                {
                    continue;
                }
                XRegistrySyncObservation? current = await pass.Reader(intent.Destination).ReadEntityAsync(
                    path, pass.Inventory(intent.Destination).Model!, cancellationToken).ConfigureAwait(false);
                SetObservation(pass.Inventory(intent.Destination), path, current);
                if (intent.Kind == XRegistrySyncIntentKind.Delete)
                {
                    XRegistrySyncSide originSide = Other(intent.Destination);
                    XRegistrySyncObservation? source = await pass.Reader(originSide).ReadEntityAsync(
                        path, pass.Inventory(originSide).Model!, cancellationToken).ConfigureAwait(false);
                    SetObservation(pass.Inventory(originSide), path, source);
                    if (current is null && source is null)
                    {
                        if (pass.State.Baselines.TryGetValue(path, out XRegistrySyncBaseline? baseline))
                        {
                            Tombstone(pass, path, baseline);
                        }
                        ResolveConflicts(pass, path);
                        pass.Processed.Add(path);
                        continue;
                    }
                    if (current is not null && source is not null && current.Fingerprint == source.Fingerprint)
                    {
                        EstablishBaseline(pass,
                            intent.Destination == XRegistrySyncSide.OpcUa ? current : source,
                            intent.Destination == XRegistrySyncSide.Http ? current : source);
                    }
                }
                if (current is null)
                {
                    pass.Inventory(intent.Destination).Complete = false;
                    pass.Records.Add(new XRegistrySyncRecord(path, XRegistrySyncRecordKind.Failure,
                        "An ancestor disappeared after mutation; no further absence-based operations are authorized."));
                }
            }
        }

        private void Hold(Pass pass, XRegistrySyncIntent intent, string detail)
        {
            AddConflict(pass, intent.Path, "outcome_unknown", detail, intent.Id);
            pass.Records.Add(new XRegistrySyncRecord(intent.Path, XRegistrySyncRecordKind.Pending, detail)
            {
                OperationId = intent.Id
            });
        }

        private static bool KnownCommitted(XRegistrySyncIntent intent)
        {
            return intent.Response is { StatusCode: 200 or 201 or 204 };
        }
    }
}
