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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.OpenUsd.Client;
using Opc.Ua.OpenUsd.Connector.Viewer;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    [Platform("Win,Linux")]
    [Category("LensOpenUsdNative")]
    [NonParallelizable]
    public sealed class OpenUsdViewportWindowTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task EmbeddedDocumentRendersLiveTelemetryPickingResizeAndSimulatedVisionThenDrainsAsync(
            bool registerAfterDefaults)
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    context.Messages.NamespaceUris.GetIndexOrAppend("http://opcfoundation.org/UA/OpenUSD/");
                    await context.ConnectAsync().ConfigureAwait(true);
                    var fixture = new SceneFixture();
                    string root = Path.GetFullPath(Path.Combine(
                        "TestResults", "viewport", Guid.NewGuid().ToString("N")));
                    var cameras = new CameraSource();
                    OpenUsdViewportSurface? native = null;
                    var cache = new OpenUsdViewportAssetCache(root);
                    var factory = new OpenUsdViewportFactory(fixture, cache,
                        telemetry => native = new OpenUsdViewportSurface(telemetry));
                    ServiceProvider services = CreateServices(
                        fixture, cache, cameras, factory, registerAfterDefaults);
                    await using var serviceLifetime = services.ConfigureAwait(true);
                    var document = services.GetRequiredService<CompanionPluginFactory>().Create(context.Host);
                    await using (document.ConfigureAwait(true))
                    {
                        Control view = ((IPlugin)document).View!;
                        DesktopInteraction.Owner.Width = 1120;
                        DesktopInteraction.Owner.Height = 920;
                        DesktopInteraction.Owner.Content = view;
                        DesktopInteraction.Owner.UpdateLayout();
                        Assert.That(document.ViewportSurface, Is.Null);
                        Assert.That(native, Is.Null, "Opening a document cannot construct its native renderer.");
                        Assert.That(Directory.Exists(root), Is.False);
                        await document.OnConnectionStateChangedAsync(default).ConfigureAwait(true);
                        await document.DiscoverCommand.ExecuteAsync(null).ConfigureAwait(true);
                        await document.InspectCommand.ExecuteAsync(null).ConfigureAwait(true);
                        document.ViewportCameraPath = "/World/Camera";
                        Assert.That(document.StartViewportCommand.CanExecute(null), Is.True);
                        Button start = view.GetLogicalDescendants().OfType<Button>().Single(button =>
                            AutomationProperties.GetName(button) == "Start embedded OpenUSD viewport");
                        Assert.That(start.IsEffectivelyVisible, Is.True);
                        await document.StartViewportCommand.ExecuteAsync(null).ConfigureAwait(true);
                        Assert.That(document.IsViewportRunning, Is.True, document.Status);
                        Assert.That(native, Is.Not.Null);
                        OpenUsdViewportSurface surface = native!;
                        UsdViewportStatistics initial = surface.Statistics;
                        Assert.That(initial.RenderedFrames, Is.GreaterThan(0));
                        Assert.That(initial.DrawCount, Is.GreaterThan(0));
                        Assert.That(initial.MeshCount, Is.EqualTo(2));
                        Assert.That(initial.ActiveFrames, Is.InRange(2, 3));
                        await document.CaptureViewportFrameCommand.ExecuteAsync(null).ConfigureAwait(true);
                        Assert.That(document.RenderedVisionFrame, Is.Not.Null,
                            document.Status + "\n" + string.Join("\n",
                                context.Desktop.Telemetry.Buffer.SnapshotList().Select(static entry => entry.Message)));
                        string before = document.ViewportStatus;
                        await fixture.Changes.Writer.WriteAsync(new OpenUsdWorkflowChange(
                            SceneFixture.Source, new DataValue(Variant.From(40d)))).ConfigureAwait(true);
                        bool updated = false;
                        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        while (!updated)
                        {
                            await Task.Delay(100, deadline.Token).ConfigureAwait(true);
                            await document.CaptureViewportFrameCommand.ExecuteAsync(null).ConfigureAwait(true);
                            updated = document.ViewportStatus != before;
                        }
                        Assert.That(document.ViewportStatus, Does.Contain("PNG SHA-256"));
                        Assert.That(fixture.Opens, Is.EqualTo(1),
                            "Live rendering never re-exports or reloads a scene.");

                        while (document.SelectedUsdPrim.Length == 0)
                        {
                            for (int x = 2; x <= 8 && document.SelectedUsdPrim.Length == 0; x++)
                            {
                                UsdViewportStatistics current = surface.Statistics;
                                await document.PickViewportAsync(current.Width * x / 10, current.Height / 2)
                                    .ConfigureAwait(true);
                            }
                            await Task.Delay(20, deadline.Token).ConfigureAwait(true);
                        }
                        Assert.That(document.SelectedUsdPrim, Is.AnyOf("/World/Anchor", "/World/Block"),
                            document.Status + "; " + document.ViewportStatus);
                        Assert.That(document.ViewportStatus, Does.Contain("Selection only"));
                        DesktopInteraction.Owner.Width = 980;
                        DesktopInteraction.Owner.UpdateLayout();
                        while (surface.Statistics.Width == initial.Width)
                        {
                            await Task.Delay(100, deadline.Token).ConfigureAwait(true);
                        }
                        Assert.That(surface.Statistics.Width, Is.Not.EqualTo(initial.Width));
                        await document.DiscoverSimulatedCamerasCommand.ExecuteAsync(null).ConfigureAwait(true);
                        Assert.That(document.SimulatedCameras, Has.Count.EqualTo(1));
                        await document.RenderSimulatedVisionCommand.ExecuteAsync(null).ConfigureAwait(true);
                        Assert.That(document.RenderedVisionFrame, Is.Not.Null, document.Status);
                        Assert.That(document.ViewportStatus,
                            Does.Contain("Local simulated Vision").And.Contain("not a server acquisition"));
                        Assert.That(cameras.Validations, Is.EqualTo(2),
                            "Revalidate the sensor both before and after capture.");
                        await document.StopViewportCommand.ExecuteAsync(null).ConfigureAwait(true);
                        Assert.That(document.ViewportSurface, Is.Null);
                        Assert.That(document.RenderedVisionFrame, Is.Null);
                        Assert.That(document.IsViewportRunning, Is.False);
                        Assert.That(surface.Statistics.ActiveFrames, Is.Zero);
                        Assert.That(surface.Statistics.ActiveGenerations, Is.Zero);
                        Assert.That(fixture.StreamDisposals, Is.EqualTo(1));
                        Assert.That(fixture.ReaderDisposals, Is.EqualTo(1));
                        Assert.That(Directory.EnumerateFileSystemEntries(root), Is.Empty);
                        context.Session.Verify(session => session.CallAsync(
                            It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                            It.IsAny<CancellationToken>()), Times.Never);
                        context.Session.Verify(session => session.WriteAsync(
                            It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(),
                            It.IsAny<CancellationToken>()), Times.Never);
                        Assert.That(context.Connected, Is.True, "The document never owns the primary connection.");
                    }
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: false);
                    }
                }
            }).ConfigureAwait(false);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task DefaultViewportUsesConfiguredProviderAndSharedCacheWithoutBypassingInspectionOnlyProvidersAsync(
            bool supportsViewport, bool dedicated)
        {
            await AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new ConnectedProtocolContext();
                await using (context.ConfigureAwait(true))
                {
                    context.Messages.NamespaceUris.GetIndexOrAppend("http://opcfoundation.org/UA/OpenUSD/");
                    await context.ConnectAsync().ConfigureAwait(true);
                    var fixture = new SceneFixture();
                    var selected = new Mock<ICompanionProvider>(MockBehavior.Strict);
                    selected.SetupGet(value => value.Descriptor).Returns(fixture.Descriptor);
                    selected.Setup(value => value.DiscoverAsync(
                        It.IsAny<CompanionContext>(), It.IsAny<CancellationToken>()))
                        .Returns((CompanionContext owner, CancellationToken token) =>
                            fixture.DiscoverAsync(owner, token));
                    selected.Setup(value => value.InspectAsync(
                        It.IsAny<CompanionContext>(), It.IsAny<CompanionTarget>(), It.IsAny<CancellationToken>()))
                        .Returns((CompanionContext owner, CompanionTarget target, CancellationToken token) =>
                            fixture.InspectAsync(owner, target, token));
                    if (supportsViewport)
                    {
                        selected.As<IOpenUsdViewportSourceFactory>().Setup(value => value.OpenViewportAsync(
                            It.IsAny<CompanionContext>(), It.IsAny<CompanionTarget>(), It.IsAny<CancellationToken>()))
                            .Returns((CompanionContext owner, CompanionTarget target, CancellationToken token) =>
                                fixture.OpenViewportAsync(owner, target, token));
                    }
                    var cache = new Mock<IOpenUsdViewportAssetCache>(MockBehavior.Strict);
                    cache.Setup(value => value.AcquireAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ArrayOf<OpenUsdVerifiedAsset>>(),
                        It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("Owned cache unavailable."));
                    var cameras = new Mock<IRenderedVisionCameraSource>(MockBehavior.Strict);
                    ServiceProvider services = CreateServices(selected.Object, cache.Object, cameras.Object);
                    await using var serviceLifetime = services.ConfigureAwait(true);
                    CompanionPluginFactory factory = services.GetRequiredService<CompanionPluginFactory>();
                    CompanionPlugin document = dedicated
                        ? factory.CreateSpecification(context.Host, PluginKind.AssetManagement, selected.Object)
                        : factory.Create(context.Host);
                    await using (document.ConfigureAwait(true))
                    {
                        Assert.That(services.GetRequiredService<IOpenUsdViewportAssetCache>(), Is.SameAs(cache.Object));
                        Assert.That(services.GetRequiredService<IRenderedVisionCameraSource>(), Is.SameAs(cameras.Object));
                        Assert.That(fixture.Opens, Is.Zero);
                        cache.VerifyNoOtherCalls();
                        await document.OnConnectionStateChangedAsync(default).ConfigureAwait(true);
                        await document.DiscoverCommand.ExecuteAsync(null).ConfigureAwait(true);
                        await document.InspectCommand.ExecuteAsync(null).ConfigureAwait(true);
                        Assert.That(document.StartViewportCommand.CanExecute(null), Is.True);

                        await document.StartViewportCommand.ExecuteAsync(null).ConfigureAwait(true);

                        Assert.That(fixture.Opens, Is.EqualTo(supportsViewport ? 1 : 0));
                        Assert.That(fixture.ReaderDisposals, Is.EqualTo(supportsViewport ? 1 : 0));
                        Assert.That(document.IsViewportRunning, Is.False);
                        Assert.That(document.HasViewport, Is.False);
                        Assert.That(document.ViewportSurface, Is.Null);
                        cache.Verify(value => value.AcquireAsync(
                            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ArrayOf<OpenUsdVerifiedAsset>>(),
                            It.IsAny<CancellationToken>()), supportsViewport ? Times.Once() : Times.Never());
                        cameras.VerifyNoOtherCalls();
                        Assert.That(context.Connected, Is.True);
                    }
                }
            }).ConfigureAwait(false);
        }

        private static ServiceProvider CreateServices(
            ICompanionProvider source,
            IOpenUsdViewportAssetCache cache,
            IRenderedVisionCameraSource cameras,
            OpenUsdViewportFactory? viewport = null,
            bool registerAfterDefaults = false)
        {
            var services = new ServiceCollection();
            if (registerAfterDefaults)
            {
                services.AddUaLens();
            }
            services.AddSingleton(source);
            services.AddSingleton(cache);
            services.AddSingleton(cameras);
            if (viewport is not null)
            {
                services.AddSingleton(viewport);
            }
            services.AddUaLens();
            services.AddUaLens();
            return services.BuildServiceProvider();
        }

        private sealed class CameraSource : IRenderedVisionCameraSource
        {
            public int Validations { get; private set; }

            public Task<ArrayOf<RenderedVisionCamera>> DiscoverAsync(
                CompanionContext context, CancellationToken cancellationToken)
            {
                return Task.FromResult<ArrayOf<RenderedVisionCamera>>(
                    [new(new NodeId(91u), new NodeId(92u), "simulated-eye", "camera-frame",
                        "/server-only/never-opened.usda", "/World/Camera")]);
            }

            public Task RequireCurrentAsync(
                CompanionContext context, RenderedVisionCamera camera, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Validations++;
                Assert.That(camera.SensorId, Is.EqualTo("simulated-eye"));
                return Task.CompletedTask;
            }
        }

        private sealed class SceneFixture : ICompanionProvider, IOpenUsdViewportSourceFactory,
            IOpenUsdCompanionReader, IOpenUsdWorkflowSource
        {
            public SceneFixture()
            {
                m_representation.Bindings.Add(new OpenUsdConnector.BindingInfo
                {
                    SourceNodeId = Source,
                    PrimPath = "/World/Block",
                    PropertyName = "xformOp:rotateZ",
                    Kind = OpenUsdRenderTargetKind.Rotation
                });
            }

            public CompanionDescriptor Descriptor { get; } =
                new("openusd", "Native fixture", "http://opcfoundation.org/UA/OpenUSD/", "Test only");
            public Channel<OpenUsdWorkflowChange> Changes { get; } = Channel.CreateBounded<OpenUsdWorkflowChange>(4);
            public int Opens { get; private set; }
            public int StreamDisposals { get; private set; }
            public int ReaderDisposals { get; private set; }

            public ValueTask<ArrayOf<CompanionTarget>> DiscoverAsync(
                CompanionContext context, CancellationToken cancellationToken) =>
                ValueTask.FromResult<ArrayOf<CompanionTarget>>([s_target]);

            public ValueTask<CompanionInspection> InspectAsync(
                CompanionContext context, CompanionTarget target, CancellationToken cancellationToken) =>
                ValueTask.FromResult(new CompanionInspection([new("Stage", Variant.From(target.NodeId))], [],
                    "Read-only native fixture; no workload started."));

            public ValueTask<CompanionOperationResult> ExecuteAsync(
                CompanionContext context, CompanionTarget target, string operationId, string? input,
                CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task<OpenUsdViewportSource> OpenViewportAsync(
                CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
            {
                Opens++;
                var binding = new CompanionOperationDraft(target,
                    new CompanionOperation("viewport", "Viewport", CompanionOperationSafety.ReadOnly),
                    null, context.Session, DateTimeOffset.MaxValue);
                return Task.FromResult(new OpenUsdViewportSource(context, target, binding,
                    OpenUsdWorkflowMetadata.Digest(context, m_representation), "cell.usda", "/World",
                    [.. m_representation.Bindings], [new("cell.usda", s_content)], this, this));
            }

            public Task<ArrayOf<OpenUsdConnector.RepresentationInfo>> DiscoverAsync(
                CancellationToken cancellationToken) =>
                Task.FromResult<ArrayOf<OpenUsdConnector.RepresentationInfo>>([m_representation]);

            public Task<string> ReadNameAsync(NodeId nodeId, CancellationToken cancellationToken) =>
                Task.FromResult("Native fixture");

            public Task<ArrayOf<OpenUsdCompanionAsset>> ReadAssetsAsync(
                NodeId stageNodeId, int maximum, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<DataValue> ReadValueAsync(NodeId nodeId, CancellationToken cancellationToken) =>
                Task.FromResult(new DataValue(Variant.From(0d)));

            public ValueTask<ByteString> ReadAssetAsync(NodeId assetNodeId, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public async IAsyncEnumerable<OpenUsdWorkflowChange> ObserveAsync(
                ArrayOf<NodeId> nodes, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                try
                {
                    await foreach (OpenUsdWorkflowChange change in Changes.Reader.ReadAllAsync(cancellationToken)
                        .ConfigureAwait(false))
                    {
                        yield return change;
                    }
                }
                finally
                {
                    StreamDisposals++;
                }
            }

            public IAsyncEnumerable<DataValue> ReadHistoryAsync(
                NodeId node, DateTime start, DateTime end, uint maximum, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public ValueTask DisposeAsync()
            {
                ReaderDisposals++;
                return ValueTask.CompletedTask;
            }

            public static readonly NodeId Source = new(71u, 2);
            private static readonly CompanionTarget s_target = new("openusd", new NodeId(70u, 2),
                "Native scene", "OpenUsdRepresentation");
            private static readonly ByteString s_content = ByteString.From(Encoding.UTF8.GetBytes("""
                #usda 1.0
                (
                    defaultPrim = "World"
                    upAxis = "Y"
                    metersPerUnit = 1
                )
                def Xform "World" {
                    def Cube "Anchor" {
                        double size = 0.7
                        double3 xformOp:translate = (-0.8, 0, 0)
                        uniform token[] xformOpOrder = ["xformOp:translate"]
                        color3f[] primvars:displayColor = [(0.1, 0.3, 0.9)]
                    }
                    def Cube "Block" {
                        double size = 0.7
                        double3 xformOp:translate = (0.4, 0, 0)
                        double xformOp:rotateZ = 0
                        uniform token[] xformOpOrder = ["xformOp:translate", "xformOp:rotateZ"]
                        color3f[] primvars:displayColor = [(0.9, 0.1, 0.2)]
                    }
                    def Camera "Camera" {
                        token projection = "orthographic"
                        float horizontalAperture = 40
                        float verticalAperture = 30
                        float2 clippingRange = (0.1, 100)
                        double3 xformOp:translate = (0, 0, 8)
                        uniform token[] xformOpOrder = ["xformOp:translate"]
                    }
                }
                """));
            private readonly OpenUsdConnector.RepresentationInfo m_representation = new()
            {
                NodeId = s_target.NodeId,
                StageNodeId = new NodeId(72u, 2),
                PrimPath = "/World",
                RootLayerIdentifier = "cell.usda",
                RootLayerDigest = ByteString.From(SHA256.HashData(s_content.Span)),
                DigestAlgorithm = OpenUsdDigestAlgorithm.Sha256
            };
        }
    }
}
