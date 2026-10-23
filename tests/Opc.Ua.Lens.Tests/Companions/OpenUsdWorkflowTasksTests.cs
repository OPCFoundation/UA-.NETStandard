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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.OpenUsd.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using static UaLens.Tests.Companions.CellProviderTestSession;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class OpenUsdWorkflowTasksTests
    {
        [Test]
        public async Task ReadPreservesIndependentSourceAndConvertedValues()
        {
            var fixture = new OpenUsdWorkflowTestContext();
            fixture.Binding.Kind = OpenUsdRenderTargetKind.Translation;
            fixture.Binding.PropertyName = "xformOp:translate";
            var storage = new ThreeDCartesianCoordinates { X = 1, Y = 2, Z = 3 };
            fixture.ReadValue = (_, _) =>
                Task.FromResult(OpenUsdWorkflowTestContext.Value(Variant.FromStructure(storage)));
            OpenUsdWorkflowTaskInput task = await fixture.PrepareAsync().ConfigureAwait(false);

            CompanionOperationResult result = await fixture.ExecuteAsync(task).ConfigureAwait(false);
            storage.X = 999;

            Assert.That(Field(result.Values, "Sample 1 source value").TryGetValue(out DataValue source), Is.True);
            Assert.That(source.WrappedValue.TryGetValue<ThreeDCartesianCoordinates>(
                out ThreeDCartesianCoordinates? original, fixture.Context.Session.MessageContext), Is.True);
            Assert.That(original!.X, Is.EqualTo(1));
            Assert.That(original.Y, Is.EqualTo(2));
            Assert.That(original.Z, Is.EqualTo(3));
            Assert.That(source.SourceTimestamp, Is.EqualTo(OpenUsdWorkflowTestContext.Start));
            Assert.That(source.ServerTimestamp,
                Is.EqualTo(OpenUsdWorkflowTestContext.Start.Add(TimeSpan.FromSeconds(1))));
            Assert.That(source.SourcePicoseconds, Is.EqualTo(123));
            Assert.That(source.ServerPicoseconds, Is.EqualTo(456));
            Assert.That(Field(result.Values, "Sample 1 converted").TryGetValue(out ArrayOf<double> converted), Is.True);
            Assert.That(converted.ToArray(), Is.EqualTo(sConverted));
            Assert.That(fixture.ReadNodes, Is.EqualTo<NodeId[]>([fixture.Binding.SourceNodeId]));
            Assert.That(fixture.ReaderDisposals, Is.EqualTo(2));
            Assert.That(fixture.Exports, Is.Zero);
            fixture.Server.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase("missing")]
        [TestCase("extra")]
        [TestCase("name")]
        [TestCase("type")]
        [TestCase("array")]
        [TestCase("zero-seconds")]
        [TestCase("too-many-seconds")]
        [TestCase("zero-samples")]
        [TestCase("too-many-samples")]
        public async Task InvalidInputsDoNotAcquireAReader(string fault)
        {
            var fixture = new OpenUsdWorkflowTestContext();
            ArrayOf<CompanionValue> inputs =
                [new("seconds", Variant.From(1u)), new("maxSamples", Variant.From(2u))];
            inputs = fault switch
            {
                "missing" => [inputs[0]],
                "extra" => [.. inputs, inputs[0]],
                "name" => [inputs[1], inputs[0]],
                "type" => [new("seconds", Variant.From(1)), inputs[1]],
                "array" => [new("seconds", Variant.From(ArrayOf.Wrapped([1u]))), inputs[1]],
                "zero-seconds" => [new("seconds", Variant.From(0u)), inputs[1]],
                "too-many-seconds" => [new("seconds", Variant.From(16u)), inputs[1]],
                "zero-samples" => [inputs[0], new("maxSamples", Variant.From(0u))],
                "too-many-samples" => [inputs[0], new("maxSamples", Variant.From(129u))],
                _ => throw new ArgumentOutOfRangeException(nameof(fault))
            };

            await Assert.ThatAsync(() => fixture.PrepareAsync("observe-bindings", inputs),
                Throws.ArgumentException).ConfigureAwait(false);

            Assert.That(fixture.Discoveries, Is.Zero);
            Assert.That(fixture.ReadNodes, Is.Empty);
            Assert.That(fixture.Exports, Is.Zero);
        }

        [TestCase("disconnected")]
        [TestCase("session")]
        [TestCase("identity")]
        [TestCase("endpoint")]
        [TestCase("policy")]
        [TestCase("mode")]
        [TestCase("application")]
        [TestCase("namespaces")]
        public async Task ChangedSourceInvalidatesPreparedWork(string fault)
        {
            var fixture = new OpenUsdWorkflowTestContext();
            OpenUsdWorkflowTaskInput task = await fixture.PrepareAsync().ConfigureAwait(false);
            switch (fault)
            {
                case "disconnected":
                    fixture.Connected = false;
                    break;
                case "session":
                    fixture.SessionId = new NodeId("replacement-session", fixture.Server.NamespaceIndex);
                    break;
                case "identity":
                    fixture.Identity = new UserIdentity();
                    break;
                case "endpoint":
                    fixture.Endpoint.EndpointUrl += "/changed";
                    break;
                case "policy":
                    fixture.Endpoint.SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss;
                    break;
                case "mode":
                    fixture.Endpoint.SecurityMode = MessageSecurityMode.Sign;
                    break;
                case "application":
                    fixture.Endpoint.Server.ApplicationUri = "urn:changed";
                    break;
                case "namespaces":
                    fixture.Server.NamespaceUris.GetIndexOrAppend("urn:additional");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }

            await Assert.ThatAsync(() => fixture.ExecuteAsync(task),
                Throws.InvalidOperationException).ConfigureAwait(false);

            Assert.That(fixture.Discoveries, Is.EqualTo(1));
            Assert.That(fixture.ReadNodes, Is.Empty);
            Assert.That(fixture.Exports, Is.Zero);
        }

        [TestCase("scale")]
        [TestCase("source")]
        [TestCase("property")]
        [TestCase("intent")]
        [TestCase("enabled")]
        [TestCase("asset-digest")]
        public async Task ChangedBindingMetadataIsRejectedBeforeReadingValues(string fault)
        {
            var fixture = new OpenUsdWorkflowTestContext();
            OpenUsdWorkflowTaskInput task = await fixture.PrepareAsync().ConfigureAwait(false);
            switch (fault)
            {
                case "scale":
                    fixture.Binding.Scale = 3;
                    break;
                case "source":
                    fixture.Binding.SourceNodeId = new NodeId("different-source", fixture.Server.NamespaceIndex);
                    break;
                case "property":
                    fixture.Binding.PropertyName = "different";
                    break;
                case "intent":
                    fixture.Binding.Intent = OpenUsdIntentProfile.UsdToUaCommand;
                    break;
                case "enabled":
                    fixture.Binding.Enabled = false;
                    break;
                case "asset-digest":
                    fixture.Representation.RootLayerDigest = ByteString.From([9]);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }

            await Assert.ThatAsync(() => fixture.ExecuteAsync(task),
                Throws.InvalidOperationException.With.Message.Contains("metadata changed")).ConfigureAwait(false);

            Assert.That(fixture.ReadNodes, Is.Empty);
            Assert.That(fixture.Exports, Is.Zero);
        }

        [TestCase("duplicate")]
        [TestCase("reverse")]
        [TestCase("before")]
        [TestCase("end")]
        [TestCase("excess")]
        public async Task HistoryRejectsInvalidOrderingAndBounds(string fault)
        {
            var fixture = new OpenUsdWorkflowTestContext();
            fixture.Binding.Intent = OpenUsdIntentProfile.UaHistoryToUsd;
            fixture.Binding.TimeSampled = true;
            DataValue first = OpenUsdWorkflowTestContext.Value(Variant.From(1d));
            DataValue next = OpenUsdWorkflowTestContext.Value(Variant.From(2d), fault switch
            {
                "duplicate" => 0,
                "reverse" => -1,
                "before" => -2,
                "end" => 3600,
                "excess" => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(fault))
            });
            var entries = new CellProviderTestEntries<DataValue>([first, next]);
            fixture.History = entries;
            OpenUsdWorkflowTaskInput task = await fixture.PrepareAsync("export-history",
                OpenUsdWorkflowTestContext.HistoryInputs(fault == "excess" ? 1 : 2, OpenUsdTestPaths.NewDestination()))
                .ConfigureAwait(false);

            await Assert.ThatAsync(() => fixture.ExecuteAsync(task),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(
                    fault == "excess" ? StatusCodes.BadEncodingLimitsExceeded : StatusCodes.BadInvalidTimestamp))
                .ConfigureAwait(false);

            Assert.That(entries.Disposals, Is.EqualTo(1));
            Assert.That(fixture.Exports, Is.Zero);
            Assert.That(fixture.ReadNodes, Is.Empty);
        }

        [Test]
        public async Task HistoryCapturesExactSamplesAndRevalidatesBeforeExport()
        {
            var fixture = new OpenUsdWorkflowTestContext();
            fixture.Binding.Intent = OpenUsdIntentProfile.UaHistoryToUsd;
            fixture.Binding.TimeSampled = true;
            var entries = new CellProviderTestEntries<DataValue>(
                [OpenUsdWorkflowTestContext.Value(Variant.From(3d)),
                    OpenUsdWorkflowTestContext.Value(Variant.From(4d), 1)]);
            fixture.History = entries;
            OpenUsdWorkflowTaskInput task = await fixture.PrepareAsync(
                "export-history", OpenUsdWorkflowTestContext.HistoryInputs(2, OpenUsdTestPaths.NewDestination()))
                .ConfigureAwait(false);

            CompanionOperationResult result = await fixture.ExecuteAsync(task).ConfigureAwait(false);

            Assert.That(fixture.Exports, Is.EqualTo(1));
            Assert.That(fixture.ExportedSamples, Has.Count.EqualTo(2));
            Assert.That(fixture.ExportedSamples[0].Value.TryGetValue(out double first), Is.True);
            Assert.That(fixture.ExportedSamples[1].Value.TryGetValue(out double second), Is.True);
            Assert.That(first, Is.EqualTo(7d));
            Assert.That(second, Is.EqualTo(9d));
            Assert.That(fixture.Discoveries, Is.EqualTo(3));
            Assert.That(entries.Disposals, Is.EqualTo(1));
            Assert.That(result.Summary, Does.Contain("source-time").IgnoreCase);
            fixture.Server.VerifyNoMutationOrSessionOwnership();
        }

        [Test]
        public async Task MetadataChangedDuringHistoryReadPreventsExport()
        {
            var fixture = new OpenUsdWorkflowTestContext();
            fixture.Binding.Intent = OpenUsdIntentProfile.UaHistoryToUsd;
            fixture.Binding.TimeSampled = true;
            fixture.History = new CellProviderTestEntries<DataValue>(
                [OpenUsdWorkflowTestContext.Value(Variant.From(3d))])
            {
                OnMove = () => fixture.Binding.Offset = 10
            };
            OpenUsdWorkflowTaskInput task = await fixture.PrepareAsync(
                "export-history", OpenUsdWorkflowTestContext.HistoryInputs(2, OpenUsdTestPaths.NewDestination()))
                .ConfigureAwait(false);

            await Assert.ThatAsync(() => fixture.ExecuteAsync(task),
                Throws.InvalidOperationException).ConfigureAwait(false);

            Assert.That(fixture.Exports, Is.Zero);
            Assert.That(fixture.ReaderDisposals, Is.EqualTo(2));
        }

        [Test]
        public async Task ObservationStopsAndDisposesAtTheExactSampleLimit()
        {
            var fixture = new OpenUsdWorkflowTestContext();
            var entries = new CellProviderTestEntries<OpenUsdWorkflowChange>(
                [.. Enumerable.Range(0, 4).Select(index => new OpenUsdWorkflowChange(fixture.Binding.SourceNodeId,
                    OpenUsdWorkflowTestContext.Value(Variant.From((double)index), index)))]);
            fixture.Changes = entries;
            OpenUsdWorkflowTaskInput task = await fixture.PrepareAsync("observe-bindings",
                [new("seconds", Variant.From(1u)), new("maxSamples", Variant.From(2u))]).ConfigureAwait(false);

            CompanionOperationResult result = await fixture.ExecuteAsync(task).ConfigureAwait(false);

            Assert.That(entries.Visited, Is.EqualTo(2));
            Assert.That(entries.Disposals, Is.EqualTo(1));
            Assert.That(Field(result.Values, "Sample count").TryGetValue(out int count), Is.True);
            Assert.That(count, Is.EqualTo(2));
            Assert.That(result.Summary, Does.Contain("Sample limit reached"));
            Assert.That(fixture.Exports, Is.Zero);
        }

        [Test]
        public async Task CommandPreflightDoesNotAcquireValuesAssetsOrExecute()
        {
            var fixture = new OpenUsdWorkflowTestContext();
            fixture.Binding.Intent = OpenUsdIntentProfile.UsdToUaCommand;
            fixture.Binding.CommandTargetNodeId = new NodeId("target", fixture.Server.NamespaceIndex);
            fixture.Binding.CommandMethodId = new NodeId("method", fixture.Server.NamespaceIndex);
            OpenUsdWorkflowTaskInput task = await fixture.PrepareAsync("command-preflight").ConfigureAwait(false);

            CompanionOperationResult result = await fixture.ExecuteAsync(task).ConfigureAwait(false);

            Assert.That(Field(result.Values, "Command 1 method").TryGetValue(out NodeId method), Is.True);
            Assert.That(method, Is.EqualTo(fixture.Binding.CommandMethodId));
            Assert.That(result.Summary, Does.Contain("not invoked").And.Contain("not command authorization"));
            Assert.That(fixture.ReadNodes, Is.Empty);
            fixture.Reader.Verify(reader => reader.ReadAssetAsync(
                It.IsAny<NodeId>(), It.IsAny<CancellationToken>()), Times.Never);
            fixture.Server.VerifyNoMutationOrSessionOwnership();
        }

        private static readonly double[] sConverted = [3d, 5d, 7d];
    }
}
