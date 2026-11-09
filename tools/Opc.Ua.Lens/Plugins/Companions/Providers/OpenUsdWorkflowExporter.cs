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
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.OpenUsd.Client;

namespace UaLens.Plugins.Companions.Providers
{
    internal interface IOpenUsdWorkflowExporter
    {
        Task WriteAsync(
            CompanionContext context, OpenUsdWorkflowTaskInput task, ArrayOf<OpenUsdWorkflowOrigin> origins,
            ArrayOf<OpenUsdConnector.ComponentInfo> components, ArrayOf<OpenUsdWorkflowSample> samples,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Exports a bounded override layer and provenance, never remote layer references.
    /// Publishes the new directory only after both files are complete.
    /// </summary>
    internal sealed class OpenUsdWorkflowExporter : IOpenUsdWorkflowExporter
    {
        public OpenUsdWorkflowExporter(Func<string, IUsdSink>? createSink = null)
        {
            m_createSink = createSink ?? (static path => new UsdFileSink(path));
        }

        public async Task WriteAsync(
            CompanionContext context, OpenUsdWorkflowTaskInput task, ArrayOf<OpenUsdWorkflowOrigin> origins,
            ArrayOf<OpenUsdConnector.ComponentInfo> components, ArrayOf<OpenUsdWorkflowSample> samples,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(task);
            task.RequireMatch(context, task.Target, task.OperationId);
            string destination = OpenUsdCompanionExporter.ValidateDestination(task.Destination);
            if (samples.Count == 0 || components.Count != 0)
            {
                throw new ArgumentException(
                    "Export requires captured values and never accepts remote asset components.");
            }
            CellCompanionSupport.CheckCount(samples.Count, task.MaximumSamples, "Export samples");
            CellCompanionSupport.CheckCount(origins.Count, 16, "Export origins");
            int totalBytes = 0;
            foreach (OpenUsdWorkflowSample sample in samples)
            {
                OpenUsdCompanionExporter.ValidatePrimPath(sample.PrimPath);
                OpenUsdCompanionExporter.ValidatePropertyName(sample.PropertyName);
                OpenUsdWorkflowTasks.RequireConverted(sample.Value);
                if (sample.Source.IsNull ||
                    sample.Source.ServerIndex != 0 ||
                    (sample.Source.NamespaceIndex != 0 && string.IsNullOrEmpty(sample.Source.NamespaceUri)) ||
                    sample.SourceValue.IsNull ||
                    !StatusCode.IsGood(sample.SourceValue.StatusCode) ||
                    sample.SourceValue.SourceTimestamp.ToDateTime() == DateTime.MinValue ||
                    sample.EncodedValue.Length is 0 or > OpenUsdWorkflowMetadata.MaximumSampleBytes ||
                    (sample.Origin != "primary" && !origins.Contains(origin => origin.RuleId == sample.Origin)))
                {
                    throw new ArgumentException("Captured source/provenance evidence is invalid.");
                }
                if (sample.EncodedValue != OpenUsdWorkflowMetadata.EncodeSample(
                    context, sample.SourceValue, sample.Value))
                {
                    throw new ArgumentException("Captured values do not match their encoded provenance.");
                }
                totalBytes = checked(totalBytes + sample.EncodedValue.Length);
            }
            CellCompanionSupport.CheckCount(
                totalBytes, OpenUsdWorkflowMetadata.MaximumTotalSampleBytes, "Export sample bytes");
            cancellationToken.ThrowIfCancellationRequested();
            string staging = Path.Combine(Path.GetDirectoryName(destination)!,
                ".ualens-" + Guid.NewGuid().ToString("N") + ".pending");
            string layer = Path.Combine(staging, "values.usda");
            string evidence = Path.Combine(staging, "evidence.json");
            var written = new List<string>();
            if (Directory.Exists(staging) || File.Exists(staging))
            {
                throw new IOException("The private export staging path already exists.");
            }
            Directory.CreateDirectory(staging);
            using var owned = new StagingLease(staging, written);
            try
            {
                await WriteHeaderAsync(layer, written, cancellationToken).ConfigureAwait(false);
                IUsdSink sink = m_createSink(layer) ??
                    throw new InvalidOperationException("The configured output sink is unavailable.");
                OpenUsdWorkflowTasks.Author(sink, samples.ToArray()!,
                    task.OperationId is "replay-history" or "export-history");
                var stream = new FileStream(evidence, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.Asynchronous);
                written.Add(evidence);
                await using (stream.ConfigureAwait(false))
                {
                    using var writer = new Utf8JsonWriter(stream);
                    writer.WriteStartObject();
                    writer.WriteString("correlationId", task.CorrelationId);
                    writer.WriteString("metadataSha256", Convert.ToHexString(task.MetadataDigest.Span));
                    writer.WriteNumber("sampleCount", samples.Count);
                    writer.WriteBoolean("containsRemoteAssets", false);
                    writer.WriteStartArray("origins");
                    foreach (OpenUsdWorkflowOrigin origin in origins)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("id", origin.RuleId);
                        writer.WriteString("targetPrimPath", origin.TargetPrimPath);
                        writer.WriteString("metadataSha256", Convert.ToHexString(origin.MetadataDigest.Span));
                        writer.WriteString("certificateSha256", Convert.ToHexString(origin.SecurityDigest.Span));
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteStartArray("samples");
                    foreach (OpenUsdWorkflowSample sample in samples)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("origin", sample.Origin);
                        writer.WriteString("sourceId", sample.Source.ToString());
                        writer.WriteString("bindingId", sample.BindingId);
                        writer.WriteString("primPath", sample.PrimPath);
                        writer.WriteString("propertyName", sample.PropertyName);
                        if (sample.SequenceNumber == 0)
                        {
                            writer.WriteNull("sequenceNumber");
                        }
                        else
                        {
                            writer.WriteNumber("sequenceNumber", sample.SequenceNumber);
                        }
                        if (sample.PublishTime == default)
                        {
                            writer.WriteNull("publishTimeUtc");
                        }
                        else
                        {
                            writer.WriteString("publishTimeUtc", sample.PublishTime);
                        }
                        writer.WriteBase64String("encodedValue", sample.EncodedValue.Span);
                        writer.WriteString("valueSha256",
                            Convert.ToHexString(SHA256.HashData(sample.EncodedValue.Span)));
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                task.RequireMatch(context, task.Target, task.OperationId);
                _ = OpenUsdCompanionExporter.ValidateDestination(destination);
                Directory.Move(staging, destination);
                owned.Committed = true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                OperationCanceledException or ServiceResultException or InvalidOperationException or
                ArgumentException or NotSupportedException)
            {
                owned.Failure = error;
                throw;
            }
        }

        private static async Task WriteHeaderAsync(
            string path, List<string> written, CancellationToken cancellationToken)
        {
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous);
            written.Add(path);
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync("#usda 1.0\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            }
        }

        private readonly Func<string, IUsdSink> m_createSink;

        private sealed class StagingLease(string directory, List<string> written) : IDisposable
        {
            public bool Committed { get; set; }
            public Exception? Failure { get; set; }

            public void Dispose()
            {
                if (Committed)
                {
                    return;
                }
                try
                {
                    for (int index = written.Count - 1; index >= 0; index--)
                    {
                        File.Delete(written[index]);
                    }
                    Directory.Delete(directory, recursive: false);
                }
                catch (Exception cleanup) when (Failure is not null &&
                    cleanup is IOException or UnauthorizedAccessException)
                {
                    throw new AggregateException("Export failed and owned-file cleanup was incomplete.",
                        Failure, cleanup);
                }
            }
        }
    }
}
