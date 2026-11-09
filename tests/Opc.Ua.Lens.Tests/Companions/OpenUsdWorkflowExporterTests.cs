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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.OpenUsd.Client;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class OpenUsdWorkflowExporterTests
    {
        [Test]
        public void HistoryAuthoringUsesSourceTimeAndDisposesItsBatch()
        {
            var fixture = new OpenUsdWorkflowTestContext();
            OpenUsdWorkflowSample sample = Sample(fixture);
            var sourceTime = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
            Assert.That(sample.SourceValue.SourceTimestamp.ToDateTime(), Is.EqualTo(sourceTime));
            Assert.That(sample.SourceValue.ServerTimestamp.ToDateTime(), Is.EqualTo(sourceTime.AddSeconds(1)));
            var batch = new Mock<IDisposable>(MockBehavior.Strict);
            batch.Setup(value => value.Dispose());
            var sink = new Mock<IUsdSink>(MockBehavior.Strict);
            sink.Setup(value => value.BeginBatch()).Returns(batch.Object);
            sink.Setup(value => value.SetTimeSample("/World/Cell", "temperature", sourceTime, Variant.From(7d)));

            OpenUsdWorkflowTasks.Author(sink.Object, [sample], history: true);

            sink.Verify(value => value.BeginBatch(), Times.Once);
            sink.Verify(value => value.SetTimeSample(
                "/World/Cell", "temperature", sourceTime, Variant.From(7d)), Times.Once);
            batch.Verify(value => value.Dispose(), Times.Once);
            sink.VerifyNoOtherCalls();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task WritesOnlyLocalValuesAndExactPortableProvenance(bool history)
        {
            var fixture = new OpenUsdWorkflowTestContext();
            string destination = OpenUsdTestPaths.NewDestination();
            OpenUsdWorkflowTaskInput task = TaskInput(fixture, destination, history);
            OpenUsdWorkflowSample sample = Sample(fixture);
            if (history)
            {
                sample = sample with { SequenceNumber = 0, PublishTime = default };
            }
            try
            {
                await new OpenUsdWorkflowExporter().WriteAsync(
                    fixture.Context, task, [], [], [sample], CancellationToken.None).ConfigureAwait(false);

                Assert.That(Directory.GetFiles(destination).Select(Path.GetFileName).Order(),
                    Is.EqualTo(sFiles));
                string layer = await File.ReadAllTextAsync(Path.Combine(destination, "values.usda"))
                    .ConfigureAwait(false);
                Assert.That(layer, Does.StartWith("#usda 1.0"));
                Assert.That(layer, Does.Contain("7.0000"));
                Assert.That(layer, Does.Contain(history ? "temperature.timeSamples" : "double temperature ="));
                Assert.That(layer, Does.Not.Contain("@").And.Not.Contain("references").And.Not.Contain("payload"));
                using var document = JsonDocument.Parse(
                    await File.ReadAllTextAsync(Path.Combine(destination, "evidence.json")).ConfigureAwait(false));
                JsonElement root = document.RootElement;
                Assert.That(root.GetProperty("containsRemoteAssets").GetBoolean(), Is.False);
                Assert.That(root.GetProperty("correlationId").GetGuid(), Is.EqualTo(task.CorrelationId));
                Assert.That(root.GetProperty("sampleCount").GetInt32(), Is.EqualTo(1));
                Assert.That(root.GetProperty("samples").GetArrayLength(), Is.EqualTo(1));
                JsonElement row = root.GetProperty("samples")[0];
                Assert.That(row.GetProperty("origin").GetString(), Is.EqualTo("primary"));
                Assert.That(row.GetProperty("sourceId").GetString(), Is.EqualTo(sample.Source.ToString()));
                Assert.That(sample.Source.NamespaceUri, Is.EqualTo(OpenUsdWorkflowTestContext.ModelUri));
                Assert.That(row.GetProperty("bindingId").GetGuid(), Is.EqualTo(sample.BindingId));
                Assert.That(row.GetProperty("primPath").GetString(), Is.EqualTo(sample.PrimPath));
                Assert.That(row.GetProperty("propertyName").GetString(), Is.EqualTo(sample.PropertyName));
                byte[] encoded = row.GetProperty("encodedValue").GetBytesFromBase64();
                Assert.That(encoded, Is.EqualTo(sample.EncodedValue.ToArray()));
                Assert.That(row.GetProperty("valueSha256").GetString(),
                    Is.EqualTo(Convert.ToHexString(SHA256.HashData(encoded))));
                using var decoder = new BinaryDecoder(encoded, fixture.Context.Session.MessageContext);
                Assert.That(decoder.ReadDataValue(null), Is.EqualTo(sample.SourceValue));
                Assert.That(decoder.ReadVariant(null), Is.EqualTo(sample.Value));
                Assert.That(decoder.Position, Is.EqualTo(encoded.Length));
                if (history)
                {
                    Assert.That(row.GetProperty("sequenceNumber").ValueKind, Is.EqualTo(JsonValueKind.Null));
                    Assert.That(row.GetProperty("publishTimeUtc").ValueKind, Is.EqualTo(JsonValueKind.Null));
                }
                else
                {
                    Assert.That(row.GetProperty("sequenceNumber").GetUInt32(), Is.EqualTo(71));
                    Assert.That(row.GetProperty("publishTimeUtc").GetDateTime(), Is.EqualTo(sample.PublishTime));
                }
            }
            finally
            {
                DeleteOutput(destination);
            }
        }

        [Test]
        public async Task ExistingDestinationIsNeverOverwritten()
        {
            var fixture = new OpenUsdWorkflowTestContext();
            string destination = OpenUsdTestPaths.NewDestination();
            Directory.CreateDirectory(destination);
            string sentinel = Path.Combine(destination, "sentinel.txt");
            await File.WriteAllTextAsync(sentinel, "original").ConfigureAwait(false);
            try
            {
                await Assert.ThatAsync(() => new OpenUsdWorkflowExporter().WriteAsync(
                    fixture.Context, TaskInput(fixture, destination), [], [], [Sample(fixture)],
                    CancellationToken.None),
                    Throws.TypeOf<IOException>()).ConfigureAwait(false);

                Assert.That(await File.ReadAllTextAsync(sentinel).ConfigureAwait(false), Is.EqualTo("original"));
                Assert.That(Directory.GetFiles(destination), Has.Length.EqualTo(1));
            }
            finally
            {
                File.Delete(sentinel);
                Directory.Delete(destination);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedOrCanceledSinkCleansOnlyItsCreatedOutput(bool cancel)
        {
            var fixture = new OpenUsdWorkflowTestContext();
            using var cancellation = new CancellationTokenSource();
            string destination = OpenUsdTestPaths.NewDestination();
            string? staging = null;
            var failure = new InvalidOperationException("configured sink failed");
            var sink = new Mock<IUsdSink>(MockBehavior.Strict);
            sink.Setup(value => value.BeginBatch()).Returns(() =>
            {
                if (!cancel)
                {
                    throw failure;
                }
                cancellation.Cancel();
                return Mock.Of<IDisposable>();
            });
            sink.Setup(value => value.SetAttribute(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Variant>()));
            var exporter = new OpenUsdWorkflowExporter(path =>
            {
                staging = Path.GetDirectoryName(path);
                return sink.Object;
            });

            if (cancel)
            {
                await Assert.ThatAsync(() => exporter.WriteAsync(
                    fixture.Context, TaskInput(fixture, destination), [], [], [Sample(fixture)], cancellation.Token),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(() => exporter.WriteAsync(
                    fixture.Context, TaskInput(fixture, destination), [], [], [Sample(fixture)], cancellation.Token),
                    Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            }
            Assert.That(staging, Is.Not.Null);
            Assert.That(Directory.Exists(staging), Is.False);
            Assert.That(Directory.Exists(destination), Is.False);
        }

        [Test]
        public async Task UnexpectedFilesAreRetainedAndCleanupFailurePreservesTheOriginalError()
        {
            var fixture = new OpenUsdWorkflowTestContext();
            string destination = OpenUsdTestPaths.NewDestination();
            string? staging = null;
            string? foreign = null;
            var failure = new InvalidOperationException("sink failure");
            var exporter = new OpenUsdWorkflowExporter(path =>
            {
                staging = Path.GetDirectoryName(path)!;
                foreign = Path.Combine(staging, "foreign");
                Directory.CreateDirectory(foreign);
                throw failure;
            });
            try
            {
                AggregateException? observed = null;
                try
                {
                    await exporter.WriteAsync(fixture.Context, TaskInput(fixture, destination),
                        [], [], [Sample(fixture)], CancellationToken.None).ConfigureAwait(false);
                }
                catch (AggregateException error)
                {
                    observed = error;
                }
                Assert.That(observed, Is.Not.Null);
                Assert.That(observed!.InnerExceptions, Has.Count.EqualTo(2));
                Assert.That(observed.InnerExceptions[0], Is.SameAs(failure));
                Assert.That(observed.InnerExceptions[1], Is.InstanceOf<IOException>());
                Assert.That(Directory.Exists(foreign), Is.True);
                Assert.That(File.Exists(Path.Combine(staging!, "values.usda")), Is.False);
                Assert.That(Directory.Exists(destination), Is.False);
            }
            finally
            {
                if (foreign is not null)
                {
                    Directory.Delete(foreign);
                }
                if (staging is not null)
                {
                    Directory.Delete(staging);
                }
            }
        }

        [TestCase("null-source")]
        [TestCase("bad-status")]
        [TestCase("missing-time")]
        [TestCase("unconfigured-origin")]
        [TestCase("missing-encoding")]
        [TestCase("mismatched-encoding")]
        public async Task InvalidProvenanceNeverCreatesOutput(string fault)
        {
            var fixture = new OpenUsdWorkflowTestContext();
            string destination = OpenUsdTestPaths.NewDestination();
            OpenUsdWorkflowSample sample = Sample(fixture);
            sample = fault switch
            {
                "null-source" => sample with { Source = ExpandedNodeId.Null },
                "bad-status" => sample with { SourceValue = DataValue.FromStatusCode(StatusCodes.BadNoData) },
                "missing-time" => sample with { SourceValue = new DataValue(sample.SourceValue.WrappedValue) },
                "unconfigured-origin" => sample with { Origin = "unconfigured" },
                "missing-encoding" => sample with { EncodedValue = ByteString.Empty },
                "mismatched-encoding" => sample with { Value = Variant.From(9d) },
                _ => throw new ArgumentOutOfRangeException(nameof(fault))
            };

            await Assert.ThatAsync(() => new OpenUsdWorkflowExporter().WriteAsync(
                fixture.Context, TaskInput(fixture, destination), [], [], [sample], CancellationToken.None),
                Throws.ArgumentException).ConfigureAwait(false);

            Assert.That(Directory.Exists(destination), Is.False);
        }

        private static OpenUsdWorkflowTaskInput TaskInput(
            OpenUsdWorkflowTestContext fixture, string destination, bool history = false)
        {
            return new OpenUsdWorkflowTaskInput(fixture.Context, fixture.Target,
                history ? "export-history" : "export-composition",
                OpenUsdWorkflowMetadata.Digest(fixture.Context, fixture.Representation), 16,
                TimeSpan.Zero, default, default, destination, "Local values");
        }

        private static OpenUsdWorkflowSample Sample(OpenUsdWorkflowTestContext fixture)
        {
            DataValue source = OpenUsdWorkflowTestContext.Value(Variant.From(3d));
            var converted = Variant.From(7d);
            return new OpenUsdWorkflowSample("primary",
                NodeId.ToExpandedNodeId(fixture.Binding.SourceNodeId, fixture.Server.NamespaceUris),
                fixture.Binding.BindingDefinitionId, "/World/Cell", "temperature", source, converted,
                OpenUsdWorkflowMetadata.EncodeSample(fixture.Context, source, converted), 71,
                OpenUsdWorkflowTestContext.Start.Add(TimeSpan.FromSeconds(2)).ToDateTime());
        }

        private static void DeleteOutput(string directory)
        {
            if (Directory.Exists(directory))
            {
                File.Delete(Path.Combine(directory, "values.usda"));
                File.Delete(Path.Combine(directory, "evidence.json"));
                Directory.Delete(directory);
            }
        }

        private static readonly string[] sFiles = ["evidence.json", "values.usda"];
    }
}
