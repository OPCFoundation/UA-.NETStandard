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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.GMS;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery.Result;

namespace GmsSample.Client
{
    /// <summary>
    /// Demonstrates existing result query, raw reads, calculated deviations and owned file-transfer cleanup.
    /// </summary>
    internal static class GmsResultReader
    {
        /// <summary>
        /// Uses generated navigation and typed methods without manually acquiring or releasing protocol handles.
        /// </summary>
        /// <exception cref="InvalidOperationException">The server omits mandatory result management.</exception>
        internal static async Task RunAsync(
            GmsClient client, GmsDeviceEntry entry, GmsClientArguments options, CancellationToken cancellationToken)
        {
            GmsResultLimits limits = Limits(options);
            GmsResultClient results = client.GetResults(entry.NodeId, limits);
            GMSResultManagementTypeClient management = await client.GetProxy(entry.NodeId)
                .GetResultManagementAsync(client.Telemetry, cancellationToken).ConfigureAwait(false) ??
                throw new InvalidOperationException("The GMS has no mandatory result management.");
            bool hasTransfer = await management.GetResultTransferAsync(client.Telemetry, cancellationToken)
                .ConfigureAwait(false) != null;
            Console.WriteLine($"GENERATED_PROXY resultManagement={management.ObjectId} fileTransfer={hasTransfer}");
            int queryCount = Math.Min(options.Count, 16);
            ArrayOf<string> ids = await results.QueryAsync(queryCount, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            Console.WriteLine(
                $"RESULT_QUERY {entry.BrowseName} count={ids.Count} maximum={queryCount} " +
                $"bytes={limits.Measure(Variant.From(ids), client.Session.MessageContext)} bound=131072");
            foreach (string id in ids)
            {
                Console.WriteLine($"  RESULT_ID {id}");
            }
            if (ids.IsEmpty)
            {
                Console.WriteLine("RESULTS empty: no fabricated latest measurement.");
                if (options.VerifyCleanup)
                {
                    throw new InvalidOperationException("Cleanup verification requires a stored transferable result.");
                }
            }
            else
            {
                ResultDataType latest = await results.GetLatestAsync(cancellationToken).ConfigureAwait(false);
                string latestId = latest.ResultMetaData!.ResultId!;
                ResultDataType byId = await results.GetByIdAsync(latestId, cancellationToken).ConfigureAwait(false);
                Console.WriteLine(
                    $"RESULT_READ latest={latestId} byId={byId.ResultMetaData!.ResultId} " +
                    $"bytes={limits.Measure(Variant.FromStructure(byId), client.Session.MessageContext)} " +
                    "bound=131072; " +
                    "method envelopes have no invented acquisition quality.");
                if (options.Download)
                {
                    string? downloadId = hasTransfer
                        ? await SelectDownloadAsync(results, ids, latest, cancellationToken).ConfigureAwait(false)
                        : null;
                    if (downloadId != null)
                    {
                        ByteString file = await results.DownloadAsync(downloadId, cancellationToken)
                            .ConfigureAwait(false);
                        Console.WriteLine(
                            $"DOWNLOAD {downloadId} bytes={file.Length} maximum={options.MaximumFileBytes} " +
                            "fileHandle=closed metadataHandle=released destination=in-memory");
                        if (options.VerifyCleanup)
                        {
                            await VerifyCleanupAsync(
                                client, entry, results, downloadId, file, limits, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        Console.WriteLine(
                            "OPTIONAL file transfer or transferable payload in the queried page absent.");
                        if (options.VerifyCleanup)
                        {
                            throw new InvalidOperationException(
                                "The cleanup demonstration requires an advanced sample with a transferable result.");
                        }
                    }
                }
            }
            ArrayOf<GmsResultVariableValue> published = await results.ReadPublishedResultsAsync(cancellationToken)
                .ConfigureAwait(false);
            Console.WriteLine(published.IsNull ? "OPTIONAL Results folder absent." : $"RESULT_SLOTS {published.Count}");
            foreach (GmsResultVariableValue slot in published)
            {
                Console.WriteLine($"RESULT_SLOT node={slot.NodeId}");
                GmsReader.PrintValue("Acquired envelope", slot.Value);
                GmsReader.PrintValue("UsedTools", slot.UsedTools);
            }
            ArrayOf<GmsCorrectionValues> corrections = await results.ReadCorrectionsAsync(cancellationToken)
                .ConfigureAwait(false);
            Console.WriteLine(corrections.IsNull
                ? "OPTIONAL Corrections folder absent."
                : $"CORRECTIONS count={corrections.Count}; monitoring values, not commands.");
            foreach (GmsCorrectionValues correction in corrections)
            {
                Console.WriteLine($"CORRECTION node={correction.NodeId}");
                GmsReader.PrintValue("Identifier", correction.Identifier);
                GmsReader.PrintValue("CharacteristicIdentifier", correction.CharacteristicIdentifier);
                PrintAnalog("Relative", correction.RelativeValue);
                PrintAnalog("Absolute", correction.AbsoluteValue);
                PrintAnalog("LowerControlLimit", correction.LowerControlLimit);
                PrintAnalog("UpperControlLimit", correction.UpperControlLimit);
            }
            if (options.AcknowledgeResult != null)
            {
                _ = await results.GetByIdAsync(options.AcknowledgeResult, cancellationToken).ConfigureAwait(false);
                ArrayOf<int> statuses = await results.AcknowledgeAsync(
                    [options.AcknowledgeResult], cancellationToken).ConfigureAwait(false);
                if (statuses[0] != 0)
                {
                    throw new GmsResultDomainException("AcknowledgeResults", statuses[0]);
                }
                Console.WriteLine(
                    $"ACKNOWLEDGED system={entry.BrowseName} result={options.AcknowledgeResult} " +
                    $"domainCode={statuses[0]}; " +
                    "not measurement, job, calibration or correction execution.");
            }
        }

        /// <summary>
        /// Supplies explicit independent result response, placeholder and file bounds.
        /// </summary>
        internal static GmsResultLimits Limits(GmsClientArguments options) => new()
        {
            MaximumResults = 64,
            MaximumMetadataBytes = 128 * 1024,
            MaximumFileBytes = options.MaximumFileBytes,
            MaximumCharacteristics = 1,
            MaximumCorrections = 16,
            MaximumUsedTools = 16
        };

        /// <summary>
        /// Selects an explicitly advertised file within the bounded page, not a guessed final-result identifier.
        /// </summary>
        private static async ValueTask<string?> SelectDownloadAsync(
            GmsResultClient results, ArrayOf<string> ids, ResultDataType latest, CancellationToken cancellationToken)
        {
            if (latest.ResultMetaData!.HasTransferableDataOnFile)
            {
                return latest.ResultMetaData.ResultId;
            }
            for (int index = 0; index < ids.Count; index++)
            {
                ResultDataType candidate = await results.GetByIdAsync(ids[index], cancellationToken)
                    .ConfigureAwait(false);
                if (candidate.ResultMetaData!.HasTransferableDataOnFile)
                {
                    return candidate.ResultMetaData.ResultId;
                }
            }
            return null;
        }

        /// <summary>
        /// Shows absent analog forms separately from present bad-quality measurements.
        /// </summary>
        private static void PrintAnalog(string name, GmsAnalogReading? value)
        {
            if (value == null)
            {
                Console.WriteLine($"  {name}: optional form absent.");
                return;
            }
            GmsReader.PrintValue(name, value.Value);
            GmsReader.PrintValue(name + " EngineeringUnits", value.EngineeringUnits);
        }

        /// <summary>
        /// Demonstrates bounded refusal and cancellation, then reuses the same sample's single-handle capacity.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// A required refusal, cancellation or subsequent reuse failed.
        /// </exception>
        private static async Task VerifyCleanupAsync(
            GmsClient client, GmsDeviceEntry entry, GmsResultClient results, string id, ByteString expected,
            GmsResultLimits limits, CancellationToken cancellationToken)
        {
            try
            {
                await results.GetByIdAsync("missing-" + Guid.NewGuid().ToString("N"), cancellationToken)
                    .ConfigureAwait(false);
                throw new InvalidOperationException("The missing result unexpectedly existed.");
            }
            catch (GmsResultDomainException error)
            {
                Console.WriteLine($"EXPECTED_MISSING_RESULT operation={error.Operation} domainCode={error.Error}");
            }
            if (expected.Length < 2)
            {
                throw new InvalidOperationException("Cleanup demonstration requires a multi-byte sample file.");
            }
            GmsResultClient bounded = client.GetResults(
                entry.NodeId, limits with { MaximumFileBytes = expected.Length - 1 });
            try
            {
                await bounded.DownloadAsync(id, cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException("The undersized file bound unexpectedly succeeded.");
            }
            catch (ServiceResultException error) when (error.StatusCode == StatusCodes.BadEncodingLimitsExceeded)
            {
                Console.WriteLine($"EXPECTED_FILE_REFUSAL status={error.StatusCode} maximum={expected.Length - 1}");
            }
            using (var cancelTransfer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var destination = new DownloadCancellationStream(cancelTransfer))
            {
                try
                {
                    await results.DownloadAsync(id, destination, cancelTransfer.Token).ConfigureAwait(false);
                    throw new InvalidOperationException("The transfer cancellation was not delivered.");
                }
                catch (OperationCanceledException) when (destination.AttemptedBytes > 0 &&
                    cancelTransfer.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine(
                        $"EXPECTED_DOWNLOAD_CANCELLATION afterServerReadBytes={destination.AttemptedBytes}");
                }
            }
            ByteString retried = await results.DownloadAsync(id, cancellationToken).ConfigureAwait(false);
            _ = await results.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
            if (!retried.Span.SequenceEqual(expected.Span))
            {
                throw new InvalidOperationException("The post-cleanup file changed unexpectedly.");
            }
            Console.WriteLine(
                $"HANDLE_REUSE_VERIFIED bytes={retried.Length}: download and metadata read succeeded after refusal " +
                "and mid-transfer cancellation; sample capacity=1 each, no handle-timeout wait.");
        }
    }
}
