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
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Controls;
using Moq;
using Opc.Ua;
using Opc.Ua.OpenUsd.Client;
using Opc.Ua.OpenUsd.Connector.Viewer;
using Opc.Ua.Vision.OpenUsd;
using OpenUsd.Rendering;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions
{
    /// <summary>
    /// Real bounded metadata/source/session composition with allocation-free native and asset seams.
    /// </summary>
    internal sealed class OpenUsdViewportTestContext
    {
        public OpenUsdViewportTestContext()
        {
            Workflow.Representation.RootLayerDigest = ByteString.From(SHA256.HashData(Content.Span));
            Workflow.Changes = Changes();
            var assetId = new NodeId("asset", Workflow.Server.NamespaceIndex);
            Workflow.Reader.Setup(reader => reader.ReadAssetsAsync(
                Workflow.Representation.StageNodeId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new OpenUsdCompanionAsset(assetId, "cell.usda", OpenUsdAssetKind.RootLayer,
                    (ulong)Content.Length, Workflow.Representation.RootLayerDigest, OpenUsdDigestAlgorithm.Sha256)]);
            Workflow.Reader.Setup(reader => reader.ReadAssetAsync(assetId, It.IsAny<CancellationToken>()))
                .Returns((NodeId _, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    AssetReads++;
                    return ValueTask.FromResult(Content);
                });
            Provider = new OpenUsdCompanionProvider(
                Workflow.CreateReader, new OpenUsdCompanionExporter(), _ => Workflow.Source.Object);
            Factory = new OpenUsdViewportFactory(Provider, Cache, _ =>
            {
                SurfaceCreations++;
                return Surface;
            });
        }

        public OpenUsdWorkflowTestContext Workflow { get; } = new();
        public OpenUsdCompanionProvider Provider { get; }
        public OpenUsdViewportFactory Factory { get; }
        public TestCache Cache { get; } = new();
        public TestSurface Surface { get; } = new();
        public Channel<OpenUsdWorkflowChange> Queue { get; } = Channel.CreateBounded<OpenUsdWorkflowChange>(4);
        public TaskCompletionSource StreamStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int AssetReads { get; private set; }
        public int SurfaceCreations { get; private set; }
        public int StreamDisposals { get; private set; }

        public Task<OpenUsdViewportSource> OpenSourceAsync(CancellationToken cancellationToken = default)
        {
            return Provider.OpenViewportAsync(Workflow.Context, Workflow.Target, cancellationToken);
        }

        public OpenUsdViewportSession CreateSession() => Factory.Create(Workflow.Context.Telemetry);

        private async System.Collections.Generic.IAsyncEnumerable<OpenUsdWorkflowChange> Changes(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamStarted.TrySetResult();
            try
            {
                await foreach (OpenUsdWorkflowChange change in Queue.Reader.ReadAllAsync(cancellationToken)
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

        public static readonly ByteString Content = OpenUsdViewportScenePolicyTests.Layer("cell.usda", """
            #usda 1.0
            def Xform "World" { def Cube "Cell" { double size = 1 } def Camera "Camera" {} }
            """).Content;

        internal sealed class TestCache : IOpenUsdViewportAssetCache, IOpenUsdViewportAssets
        {
            public int Acquisitions { get; private set; }
            public int Disposals { get; private set; }
            public string StagePath => "test-owned-stage.usda";

            public Task<IOpenUsdViewportAssets> AcquireAsync(
                string rootIdentifier, string primPath, ArrayOf<OpenUsdVerifiedAsset> assets,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Acquisitions++;
                return Task.FromResult<IOpenUsdViewportAssets>(this);
            }

            public ValueTask DisposeAsync()
            {
                Disposals++;
                return ValueTask.CompletedTask;
            }
        }

        internal sealed class TestSurface : IOpenUsdViewportSurface
        {
            public Control View { get; } = new();
            public TestSink Recorder { get; } = new();
            public IUsdSink Sink => Recorder;
            public string StageIdentifier { get; private set; } = string.Empty;
            public string CameraPath { get; private set; } = string.Empty;
            public string Status => "Test surface; no GPU.";
            public SceneCameraCaptureBackend Backend { get; } = new() { Name = "Test", IsAvailable = true };
            public int Starts { get; private set; }
            public int Disposals { get; private set; }
            public int Captures { get; private set; }
            public UsdViewportStatistics Statistics { get; private set; } = new(2, 1, 2, 3, 1, 1, 1, 2);
            public TaskCompletionSource CaptureStarted { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Func<CancellationToken, ValueTask<RetainedSceneFrame>> Capture { get; set; } =
                static _ => ValueTask.FromResult(Frame());
            public event EventHandler<Exception>? Failed;

            public Task StartAsync(
                string stagePath, string stageIdentifier, string? cameraPath, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Starts++;
                StageIdentifier = stageIdentifier;
                CameraPath = cameraPath ?? string.Empty;
                return Task.CompletedTask;
            }

            public Task<UsdViewportStatistics> WaitForFirstFrameAsync(CancellationToken cancellationToken) =>
                Task.FromResult(Statistics);

            public ValueTask SetCameraAsync(string? path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CameraPath = path ?? string.Empty;
                return ValueTask.CompletedTask;
            }

            public ValueTask<RenderPickResult> PickAsync(int x, int y, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var request = new RenderPickRequest(x, y, new ViewportDimensions(2, 1), 1, null,
                    RenderPickTarget.Primitive, RenderPickOptions.None);
                var item = new SelectionItem("/World/Cell", null, null, null);
                return ValueTask.FromResult(RenderPickResult.Hit(request, 1, 3, item));
            }

            public ValueTask<RetainedSceneFrame> CaptureFrameAsync(
                SceneCameraCaptureRequest request, CancellationToken cancellationToken)
            {
                Captures++;
                CaptureStarted.TrySetResult();
                return Capture(cancellationToken);
            }

            public void Fail(Exception error) => Failed?.Invoke(this, error);

            public ValueTask DisposeAsync()
            {
                Disposals++;
                Statistics = Statistics with { ActiveFrames = 0, ActiveGenerations = 0 };
                return ValueTask.CompletedTask;
            }

            public static RetainedSceneFrame Frame() =>
                new(2, 1, ByteString.From([1, 2, 3, 255, 4, 5, 6, 255]), 1, 1, 3);
        }

        internal sealed class TestSink : IUsdSink
        {
            public Channel<Variant> Values { get; } = Channel.CreateBounded<Variant>(8);
            public int Writes { get; private set; }

            public void SetAttribute(string primPath, string propertyName, Variant value)
            {
                Writes++;
                Values.Writer.TryWrite(value);
            }

            public IDisposable BeginBatch() => new Batch();

            public void ComposePrim(string primPath, OpenUsdCompositionArc arc, string? reference, bool active) =>
                throw new InvalidOperationException("Live telemetry must not create composition or command prims.");

            public void SetTimeSample(string primPath, string propertyName, DateTime time, Variant value) =>
                throw new InvalidOperationException("Live telemetry must not create an unbounded history.");

            private sealed class Batch : IDisposable
            {
                public void Dispose()
                {
                }
            }
        }
    }
}
