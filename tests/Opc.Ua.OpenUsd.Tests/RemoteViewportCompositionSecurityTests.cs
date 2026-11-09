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

#if NET10_0
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.OpenUsd.Connector.Viewer;
using Opc.Ua.OpenUsd.Scene;
using Opc.Ua.OpenUsd.Scene.Conversion;
using Opc.Ua.Tests;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Drives the real sample factories and connector dispatch without loading OpenUSD or contacting a network.
    /// </summary>
    [TestFixture]
    [Category("SecurityRegression")]
    public sealed class RemoteViewportCompositionSecurityTests
    {
        /// <summary>
        /// Both sample factories deny enabled 1:1 component composition independently of command enablement.
        /// </summary>
        [Test]
        public async Task SecurityR47BothSampleViewportsSuppressRemoteStructuralCompositionAsync(
            [Values(false, true)] bool binPicking,
            [Values(false, true)] bool commands,
            [Values(OpenUsdCompositionArc.Reference, OpenUsdCompositionArc.Instance,
                OpenUsdCompositionArc.Payload)] OpenUsdCompositionArc arc)
        {
            (_, Mock<ISession> session, _) = CreateAddressSpace(@"@\\untrusted.invalid\share\foreign.usda@", arc);
            var sink = new RecordingSink();
            await using OpenUsdConnector connector =
                CreateSampleConnector(binPicking, session.Object, sink, commands);

            List<OpenUsdConnector.RepresentationInfo> representations =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None).ConfigureAwait(false);
            OpenUsdConnector.RepresentationInfo representation = representations.Single();
            OpenUsdConnector.ComponentInfo component = representation.Components.Single();
            Assert.That(component.Enabled, Is.True);
            Assert.That(component.Cardinality, Is.EqualTo(OpenUsdCardinality.One));
            Assert.That(component.ComponentRepresentation.IsNull, Is.False);
            Assert.That(component.TargetPrimPath, Is.EqualTo("/World/Target/Component"));
            Assert.That(component.Arc, Is.EqualTo(arc));
            await ComposeAsync(connector, representation, component).ConfigureAwait(false);

            Assert.That(sink.Compositions, Is.Empty, "The recording sink represents the native composition boundary.");
            // Positive control: the same real connector path still composes into a host-controlled sink.
            await using var trusted = new OpenUsdConnector(session.Object, sink);
            await ComposeAsync(trusted, representation, component).ConfigureAwait(false);
            Assert.That(sink.Compositions, Is.EqualTo(new[]
            {
                ("/World/Target/Component", arc, component.ComponentAssetReference, true)
            }));
        }

        /// <summary>
        /// The boundary is value-only, not a destination filter that can miss escaping or transitive references.
        /// </summary>
        [Test]
        public async Task SecurityR47RemoteViewportBoundaryRejectsEveryExternalAssetFormAsync(
            [Values(false, true)] bool binPicking,
            [Values(false, true)] bool commands,
            [Values(
                @"@\\untrusted.invalid\share\foreign.usda@",
                "@//untrusted.invalid/share/foreign.usda@",
                @"@C:\outside\foreign.usda@",
                "@/outside/foreign.usda@",
                "@https://untrusted.invalid/foreign.usda@",
                "@file://untrusted.invalid/share/foreign.usda@",
                "@../foreign.usda@",
                "@./nested/../../foreign.usda@",
                "@./verified-with-external-dependencies.usda@",
                "@./verified.usda@</World/Target>")] string asset)
        {
            (_, Mock<ISession> session, _) = CreateAddressSpace(asset, OpenUsdCompositionArc.Reference);
            var sink = new RecordingSink();
            await using OpenUsdConnector connector =
                CreateSampleConnector(binPicking, session.Object, sink, commands);
            List<OpenUsdConnector.RepresentationInfo> representations =
                await connector.DiscoverAllRepresentationsAsync(CancellationToken.None).ConfigureAwait(false);
            OpenUsdConnector.RepresentationInfo representation = representations.Single();
            OpenUsdConnector.ComponentInfo component = representation.Components.Single();
            Assert.That(component.ComponentAssetReference, Is.EqualTo(asset));

            await ComposeAsync(connector, representation, component).ConfigureAwait(false);
            component.Arc = OpenUsdCompositionArc.Payload;
            await ComposeAsync(connector, representation, component).ConfigureAwait(false);
            component.Arc = OpenUsdCompositionArc.Instance;
            await ComposeAsync(connector, representation, component).ConfigureAwait(false);

            Assert.That(sink.Compositions, Is.Empty,
                "Even a local-looking layer is not resolved, so external transitive dependencies cannot be followed.");
        }

        /// <summary>
        /// Actual fetching, live-notification and history paths retain their data and trusted file authoring.
        /// </summary>
        [Test]
        public async Task SecurityR47SampleViewportsRetainLiveValuesAndVerifiedInitialStageAsync(
            [Values(false, true)] bool binPicking)
        {
            (FakeAddressSpace space, Mock<ISession> session, NodeId representationId) =
                CreateAddressSpace("@./trusted.usda@", OpenUsdCompositionArc.Reference);
            byte[] initialStage = Encoding.UTF8.GetBytes(
                "#usda 1.0\n\ndef Xform \"World\"\n{\n    def Xform \"Target\"\n    {\n    }\n}\n");
            AddVerifiedStage(space, representationId, initialStage);
            DateTime time = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
            session.Setup(s => s.HistoryReadAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ExtensionObject>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<bool>(), It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistoryReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new HistoryReadResult
                        {
                            StatusCode = StatusCodes.Good,
                            HistoryData = new ExtensionObject(new HistoryData
                            {
                                DataValues =
                                [
                                    new DataValue(Variant.From(43.0), StatusCodes.Good, DateTimeUtc.From(time))
                                ]
                            })
                        }
                    ],
                    DiagnosticInfos = []
                });
            string directory = Path.GetFullPath(
                Path.Combine("artifacts", "security-r47-" + Guid.NewGuid().ToString("N")));
            var sink = new RecordingSink();
            await using OpenUsdConnector connector =
                CreateSampleConnector(binPicking, session.Object, sink, commands: false);
            try
            {
                List<OpenUsdConnector.FetchedAsset> fetched =
                    await connector.FetchServedAssetsAsync(directory, CancellationToken.None).ConfigureAwait(false);
                Assert.That(fetched, Has.Count.EqualTo(1));
                Assert.That(fetched[0].DigestVerified, Is.True);
                Assert.That(await File.ReadAllBytesAsync(fetched[0].LocalPath).ConfigureAwait(false),
                    Is.EqualTo(initialStage));
                List<OpenUsdConnector.RepresentationInfo> representations =
                    await connector.DiscoverAllRepresentationsAsync(CancellationToken.None).ConfigureAwait(false);
                OpenUsdConnector.RepresentationInfo representation = representations.Single();
                PublishValue(connector,
                    representation.Bindings.Single(binding => binding.Intent == OpenUsdIntentProfile.UaToUsdTelemetry),
                    Variant.From(42.0));
                int samples = await connector.ReplayHistoryAsync(
                    time.AddSeconds(-1), time.AddSeconds(1), CancellationToken.None).ConfigureAwait(false);
                await ComposeAsync(connector, representation, representation.Components.Single()).ConfigureAwait(false);

                Assert.That(sink.Attributes, Is.EqualTo(new[] { ("/World/Target", "speed", Variant.From(42.0)) }));
                Assert.That(samples, Is.EqualTo(1));
                Assert.That(sink.Samples,
                    Is.EqualTo(new[] { ("/World/Target", "history", time, Variant.From(43.0)) }));
                Assert.That(sink.BatchesOpened, Is.EqualTo(2));
                Assert.That(sink.BatchesClosed, Is.EqualTo(2));
                Assert.That(sink.Compositions, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(fetched[0].LocalPath).ConfigureAwait(false),
                    Is.EqualTo(initialStage), "Live streaming must not modify the verified initial stage.");

                string filePath = Path.Combine(directory, "trusted-live.usda");
                var fileSink = new UsdFileSink(filePath);
                await using var trusted = new OpenUsdConnector(session.Object, fileSink);
                await ComposeAsync(trusted, representation, representation.Components.Single()).ConfigureAwait(false);
                UsdStage fileStage = UsdaReader.Parse(
                    await File.ReadAllTextAsync(filePath).ConfigureAwait(false), "trusted");
                UsdCompositionArc composition = fileStage.AllPrims().Single(prim => prim.Name == "Component")
                    .Composition.Single();
                Assert.That(composition.ArcKind, Is.EqualTo(UsdArcKindEnum.Reference));
                Assert.That(composition.AssetPath, Is.EqualTo("./trusted.usda"));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        /// <summary>
        /// The viewer's actual session callback also enforces the boundary for arbitrary callers.
        /// </summary>
        [Test]
        public async Task SecurityR47ViewerSessionCallbackOnlyForwardsValuesAsync()
        {
            var sink = new RecordingSink();
            DateTime time = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

            await OpenUsdViewHost.RunSessionAsync(
                (viewport, _) =>
                {
                    using IDisposable batch = viewport.BeginBatch();
                    viewport.ComposePrim("/World/Injected", OpenUsdCompositionArc.Payload,
                        @"@\\untrusted.invalid\share\foreign.usda@", active: true);
                    viewport.ComposePrim("/World/Target", OpenUsdCompositionArc.Child, null, active: false);
                    viewport.SetAttribute("/World/Target", "speed", Variant.From(42.0));
                    viewport.SetTimeSample("/World/Target", "history", time, Variant.From(43.0));
                    return Task.CompletedTask;
                },
                sink, CancellationToken.None, CancellationToken.None).ConfigureAwait(false);

            Assert.That(sink.Compositions, Is.Empty);
            Assert.That(sink.Attributes, Is.EqualTo(new[] { ("/World/Target", "speed", Variant.From(42.0)) }));
            Assert.That(sink.Samples, Is.EqualTo(new[] { ("/World/Target", "history", time, Variant.From(43.0)) }));
            Assert.That(sink.BatchesOpened, Is.EqualTo(1));
            Assert.That(sink.BatchesClosed, Is.EqualTo(1));
        }

        /// <summary>
        /// Invokes exactly the factory used by each sample's optional viewport stream.
        /// </summary>
        private static OpenUsdConnector CreateSampleConnector(
            bool binPicking, ISession session, IUsdSink sink, bool commands)
        {
            var options = new OpenUsdConnectorOptions { EnableCommands = commands };
            return binPicking
                ? BinPickingClient.Program.CreateViewportConnector(session, sink, options)
                : IntentViewerClient.Program.CreateViewportConnector(session, sink, options);
        }

        /// <summary>
        /// Supplies enabled component metadata and both live and history bindings through real discovery.
        /// </summary>
        private static (FakeAddressSpace Space, Mock<ISession> Session, NodeId Representation) CreateAddressSpace(
            string asset, OpenUsdCompositionArc arc)
        {
            var space = new FakeAddressSpace();
            NodeId facility = space.AddObject(Opc.Ua.ObjectIds.Server, "OpenUSD",
                browseNameNamespace: space.OpenUsdNamespaceIndex);
            NodeId registry = space.AddObject(facility, "Representations");
            NodeId representation = space.AddObject(registry, "Target",
                new NodeId(OpenUsdModel.RepresentationTypeId, space.OpenUsdNamespaceIndex));
            space.AddVariable(representation, "PrimPath", Variant.From("/World/Target"));
            NodeId component = space.AddObject(representation, "Component",
                new NodeId(OpenUsdModel.ComponentBindingTypeId, space.OpenUsdNamespaceIndex));
            space.AddVariable(component, "Enabled", Variant.From(true));
            space.AddVariable(component, "Cardinality", Variant.From((int)OpenUsdCardinality.One));
            space.AddVariable(component, "TargetPrimPath", Variant.From("/World/Target/Component"));
            space.AddVariable(component, "CompositionArc", Variant.From((int)arc));
            space.AddVariable(component, "ComponentRepresentation", Variant.From(space.NewNodeId()));
            space.AddVariable(component, "ComponentAssetReference", Variant.From(asset));
            NodeId source = space.AddVariable(Opc.Ua.ObjectIds.Server, "Speed", Variant.From(42.0));
            AddBinding(space, representation, source, OpenUsdModel.ValueChangeBindingTypeId, "speed");
            NodeId history = AddBinding(space, representation, source, OpenUsdModel.HistoryBindingTypeId, "history");
            space.AddVariable(history, "TimeSampled", Variant.From(true));
            return (space, FakeSession.Create(space), representation);
        }

        /// <summary>
        /// Adds a binding with explicit source and valid target to the fake remote representation.
        /// </summary>
        private static NodeId AddBinding(
            FakeAddressSpace space, NodeId representation, NodeId source, uint typeId, string property)
        {
            NodeId binding = space.AddObject(representation, property,
                new NodeId(typeId, space.OpenUsdNamespaceIndex));
            space.AddVariable(binding, "SourceNodeId", Variant.From(source));
            space.AddVariable(binding, "TargetPropertyName", Variant.From(property));
            space.AddVariable(binding, "RenderTargetKind", Variant.From((int)OpenUsdRenderTargetKind.Custom));
            return binding;
        }

        /// <summary>
        /// Serves one digest-protected initial layer using the actual Part 5 file transfer fake.
        /// </summary>
        private static void AddVerifiedStage(FakeAddressSpace space, NodeId representation, byte[] content)
        {
            NodeId stage = space.AddObject(representation, "StageObject");
            space.AddVariable(representation, "Stage", Variant.From(stage));
            space.AddVariable(stage, "RootLayerIdentifier", Variant.From("initial.usda"));
            NodeId assets = space.AddObject(stage, "Assets");
            NodeId asset = space.AddObject(assets, "Initial",
                new NodeId(OpenUsdModel.AssetTypeId, space.OpenUsdNamespaceIndex));
            space.AddVariable(asset, "AssetIdentifier", Variant.From("initial.usda"));
            space.AddVariable(asset, "AssetKind", Variant.From((int)OpenUsdAssetKind.RootLayer));
            space.AddVariable(asset, "Digest", Variant.From(new ByteString(SHA256.HashData(content))));
            space.AddVariable(asset, "DigestAlgorithm", Variant.From((int)OpenUsdDigestAlgorithm.Sha256));
            space.AddPart5File(asset, content);
        }

        /// <summary>
        /// Runs the real private composition dispatcher without creating a subscription or native renderer.
        /// </summary>
        private static async Task ComposeAsync(
            OpenUsdConnector connector,
            OpenUsdConnector.RepresentationInfo representation,
            OpenUsdConnector.ComponentInfo component)
        {
            MethodInfo method = typeof(OpenUsdConnector).GetMethod(
                "ComposeComponentAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await ((Task)method.Invoke(connector, [representation, component, CancellationToken.None])!)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Delivers a real queued monitored value to the connector's production notification handler.
        /// </summary>
        private static void PublishValue(
            OpenUsdConnector connector, OpenUsdConnector.BindingInfo binding, Variant value)
        {
            var item = new MonitoredItem(NUnitTelemetryContext.Create()) { Handle = binding };
            var notification = new MonitoredItemNotification { Value = new DataValue(value) };
            item.SaveValueInCache(notification);
            MethodInfo method = typeof(OpenUsdConnector).GetMethod(
                "OnNotification", BindingFlags.Instance | BindingFlags.NonPublic)!;
            method.Invoke(connector, [item, new MonitoredItemNotificationEventArgs(notification)]);
        }

        /// <summary>
        /// Records concrete values and every structural call before any native resolver could run.
        /// </summary>
        private sealed class RecordingSink : IUsdSink
        {
            /// <summary>
            /// Recorded current values.
            /// </summary>
            public List<(string Prim, string Property, Variant Value)> Attributes { get; } = [];

            /// <summary>
            /// Recorded historical values with their exact timestamps.
            /// </summary>
            public List<(string Prim, string Property, DateTime Time, Variant Value)> Samples { get; } = [];

            /// <summary>
            /// Every call that would reach native structural composition.
            /// </summary>
            public List<(string Prim, OpenUsdCompositionArc Arc, string? Asset, bool Active)> Compositions { get; } =
                [];

            /// <summary>
            /// Number of begun scopes.
            /// </summary>
            public int BatchesOpened { get; private set; }

            /// <summary>
            /// Number of completed scopes.
            /// </summary>
            public int BatchesClosed { get; private set; }

            /// <inheritdoc/>
            public void SetAttribute(string primPath, string propertyName, Variant value)
            {
                Attributes.Add((primPath, propertyName, value));
            }

            /// <inheritdoc/>
            public void SetTimeSample(string primPath, string propertyName, DateTime time, Variant value)
            {
                Samples.Add((primPath, propertyName, time, value));
            }

            /// <inheritdoc/>
            public void ComposePrim(string primPath, OpenUsdCompositionArc arc, string? assetReference, bool active)
            {
                Compositions.Add((primPath, arc, assetReference, active));
            }

            /// <inheritdoc/>
            public IDisposable BeginBatch()
            {
                BatchesOpened++;
                return new BatchScope(() => BatchesClosed++);
            }
        }

        /// <summary>
        /// Counts completion of one delegated batch.
        /// </summary>
        private sealed class BatchScope(Action dispose) : IDisposable
        {
            /// <inheritdoc/>
            public void Dispose()
            {
                dispose();
            }
        }
    }
}
#endif
