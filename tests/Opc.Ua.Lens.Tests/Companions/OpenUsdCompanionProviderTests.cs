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
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.OpenUsd.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions;

[TestFixture]
public sealed class OpenUsdCompanionProviderTests
{
    [Test]
    public async Task DefaultProviderDiscoversAnActualRepresentationThroughTheConnectorAsync()
    {
        var session = new CellProviderTestSession(ModelUri);
        var facility = new NodeId("openusd-facility", session.NamespaceIndex);
        var registry = new NodeId("representations", session.NamespaceIndex);
        session.Browses[Opc.Ua.ObjectIds.Server] =
            [session.Reference(facility, "OpenUSD", Opc.Ua.ObjectTypeIds.BaseObjectType)];
        session.Browses[facility] = [session.Reference(registry, "Representations", Opc.Ua.ObjectTypeIds.FolderType)];
        session.Browses[registry] =
            [session.Reference(s_representation, "Robot", new NodeId(1003, session.NamespaceIndex))];
        session.AddValue(s_representation, "PrimPath", Variant.From("/World/Robot"));
        session.Values[(s_representation, Attributes.BrowseName)] =
            new DataValue(Variant.From(new QualifiedName("Robot")));
        var provider = new OpenUsdCompanionProvider();

        ArrayOf<CompanionTarget> targets = await provider.DiscoverAsync(session.Context, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(targets, Has.Count.EqualTo(1));
        Assert.That(targets[0].NodeId, Is.EqualTo(s_representation));
        Assert.That(targets[0].DisplayName, Is.EqualTo("Robot"));
        Assert.That(targets[0].TypeName, Is.EqualTo("OpenUsdRepresentation"));
        Assert.That(provider.Descriptor.Maturity, Does.Contain("Draft"));
        session.VerifyNoMutationOrSessionOwnership();
    }

    [Test]
    public async Task InspectionReadsOnlyMetadataAndCreatesFailClosedConnectorOptionsAsync()
    {
        var session = new CellProviderTestSession(ModelUri);
        var reader = new TestReader();
        var exporter = new TestExporter();
        OpenUsdConnectorOptions? options = null;
        CompanionContext? context = null;
        var provider = new OpenUsdCompanionProvider((operationContext, operationOptions) =>
        {
            context = operationContext;
            options = operationOptions;
            return reader;
        }, exporter);

        CompanionInspection inspection = await provider.InspectAsync(session.Context, s_target, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(context, Is.SameAs(session.Context));
        Assert.That(options, Is.Not.Null);
        Assert.That(options!.EnableCommands, Is.False);
        Assert.That(options.RemoteSessionFactory, Is.Null);
        Assert.That(options.RequireAssetDigests, Is.True);
        Assert.That(options.MaxAssetBytes, Is.EqualTo(8 * 1024 * 1024));
        Assert.That(options.MaxTotalAssetBytes, Is.EqualTo(32L * 1024 * 1024));
        Assert.That(reader.AssetMetadataReads, Is.EqualTo(1));
        Assert.That(reader.RequestedStage, Is.EqualTo(s_stage));
        Assert.That(reader.FileReads, Is.Zero);
        Assert.That(reader.Disposals, Is.EqualTo(1));
        Assert.That(exporter.Writes, Is.Zero);
        Assert.That(inspection.Operations[2].Id, Is.EqualTo("export"));
        Assert.That(inspection.Operations[2].Safety, Is.EqualTo(CompanionOperationSafety.LocalFile));
        Assert.That(inspection.Summary, Does.Contain("no served file was opened").IgnoreCase);
        session.VerifyNoMutationOrSessionOwnership();
    }

    [Test]
    public async Task SnapshotUsesTheConnectorsConversionAndIgnoresNonTelemetryBindingsAsync()
    {
        var session = new CellProviderTestSession(ModelUri);
        var reader = new TestReader();
        OpenUsdConnector.RepresentationInfo representation = reader.Representations[0];
        representation.Bindings.Add(new OpenUsdConnector.BindingInfo
        {
            SourceNodeId = s_source,
            PrimPath = "/World/Robot",
            PropertyName = "temperature",
            Kind = OpenUsdRenderTargetKind.Custom,
            Scale = 2,
            Offset = 1
        });
        foreach (OpenUsdIntentProfile intent in new[]
        {
            OpenUsdIntentProfile.UsdToUaCommand,
            OpenUsdIntentProfile.UaHistoryToUsd,
            OpenUsdIntentProfile.UaAlarmToUsd
        })
        {
            representation.Bindings.Add(new OpenUsdConnector.BindingInfo { Intent = intent });
        }
        representation.Bindings.Add(new OpenUsdConnector.BindingInfo { Enabled = false });
        var provider = new OpenUsdCompanionProvider((_, _) => reader, new TestExporter());

        CompanionOperationResult result = await provider.ExecuteAsync(
            session.Context, s_target, "snapshot", null, CancellationToken.None).ConfigureAwait(false);

        Assert.That(reader.ReadNodes, Is.EqualTo(new[] { s_source }));
        Assert.That(reader.FileReads, Is.Zero);
        Assert.That(reader.Disposals, Is.EqualTo(1));
        Assert.That(result.Values, Has.Count.EqualTo(1));
        Assert.That(result.Values[0].Name, Is.EqualTo("/World/Robot.temperature"));
        Assert.That(result.Values[0].Value.TryGetValue(out double value), Is.True);
        Assert.That(value, Is.EqualTo(7d));
        Assert.That(result.Summary, Does.Contain("1 telemetry").And.Contain("no stage or subscription"));
    }

    [Test]
    public async Task AssetVerificationChecksEveryDigestAndTheSelectedRootWithoutWritingAsync()
    {
        var session = new CellProviderTestSession(ModelUri);
        var reader = new TestReader();
        var exporter = new TestExporter();
        var provider = new OpenUsdCompanionProvider((_, _) => reader, exporter);

        CompanionOperationResult result = await provider.ExecuteAsync(
            session.Context, s_target, "verify-assets", null, CancellationToken.None).ConfigureAwait(false);

        Assert.That(reader.RequestedStage, Is.EqualTo(s_stage));
        Assert.That(reader.FileReads, Is.EqualTo(1));
        Assert.That(reader.Disposals, Is.EqualTo(1));
        Assert.That(exporter.Writes, Is.Zero);
        Assert.That(result.Summary, Does.Contain("Verified 1"));
        Assert.That(CellProviderTestSession.Field(result.Values, "Verified bytes")
            .TryGetValue(out long bytes), Is.True);
        Assert.That(bytes, Is.EqualTo(s_content.Length));
    }

    [Test]
    public async Task ExportForwardsOnlyVerifiedBytesAndTheExplicitLocalDestinationAsync()
    {
        var session = new CellProviderTestSession(ModelUri);
        var reader = new TestReader();
        var exporter = new TestExporter();
        var provider = new OpenUsdCompanionProvider((_, _) => reader, exporter);
        string destination = NewDestination();

        CompanionOperationResult result = await provider.ExecuteAsync(
            session.Context, s_target, "export", destination, CancellationToken.None).ConfigureAwait(false);

        Assert.That(exporter.Writes, Is.EqualTo(1));
        Assert.That(exporter.Destination, Is.EqualTo(destination));
        Assert.That(exporter.RootLayer, Is.EqualTo("robot.usda"));
        Assert.That(exporter.Assets, Has.Count.EqualTo(1));
        Assert.That(exporter.Assets[0].Identifier, Is.EqualTo("robot.usda"));
        Assert.That(exporter.Assets[0].Content, Is.EqualTo(s_content));
        Assert.That(result.Summary, Does.Contain("Exported 1 digest-verified"));
        Assert.That(reader.Disposals, Is.EqualTo(1));
        Assert.That(Directory.Exists(destination), Is.False, "The injected exporter performs no filesystem work.");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("relative-directory")]
    [TestCase(@"\\server\share\export")]
    [TestCase("https://external.example/export")]
    public void InvalidExportPathsAreRejectedBeforeAnyServerWork(string? path)
    {
        var session = new CellProviderTestSession(ModelUri);
        int created = 0;
        var provider = new OpenUsdCompanionProvider((_, _) =>
        {
            created++;
            return new TestReader();
        }, new TestExporter());

        Assert.That(
            async () => await provider.ExecuteAsync(
                session.Context, s_target, "export", path, CancellationToken.None).ConfigureAwait(false),
            Throws.ArgumentException);
        Assert.That(created, Is.Zero);
        session.VerifyNoMutationOrSessionOwnership();
    }

    [Test]
    public void InvalidAssetDigestNeverReachesTheExporter()
    {
        var session = new CellProviderTestSession(ModelUri);
        var reader = new TestReader
        {
            Assets = [Asset() with { Digest = new ByteString(new byte[32]) }]
        };
        var exporter = new TestExporter();
        var provider = new OpenUsdCompanionProvider((_, _) => reader, exporter);

        Assert.That(
            async () => await provider.ExecuteAsync(
                session.Context, s_target, "export", NewDestination(), CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>());
        Assert.That(reader.FileReads, Is.EqualTo(1));
        Assert.That(reader.Disposals, Is.EqualTo(1));
        Assert.That(exporter.Writes, Is.Zero);
    }

    [Test]
    public void MissingRootDigestFailsBeforeAnyAssetDownload()
    {
        var session = new CellProviderTestSession(ModelUri);
        var reader = new TestReader();
        reader.Representations[0].RootLayerDigest = default;
        var provider = new OpenUsdCompanionProvider((_, _) => reader, new TestExporter());

        Assert.That(
            async () => await provider.ExecuteAsync(
                session.Context, s_target, "verify-assets", null, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>());
        Assert.That(reader.FileReads, Is.Zero);
        Assert.That(reader.AssetMetadataReads, Is.Zero);
        Assert.That(reader.Disposals, Is.EqualTo(1));
    }

    [Test]
    public void CorrectAssetDigestCannotMaskADifferentAdvertisedStageDigest()
    {
        var session = new CellProviderTestSession(ModelUri);
        var reader = new TestReader();
        reader.Representations[0].RootLayerDigest = new ByteString(SHA256.HashData("different root"u8));
        var provider = new OpenUsdCompanionProvider((_, _) => reader, new TestExporter());

        Assert.That(
            async () => await provider.ExecuteAsync(
                session.Context, s_target, "verify-assets", null, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadSecurityChecksFailed));
        Assert.That(reader.FileReads, Is.EqualTo(1));
        Assert.That(reader.Disposals, Is.EqualTo(1));
    }

    [Test]
    public void AggregateAssetBytesAreBoundedBeforeAnyFileIsOpened()
    {
        var session = new CellProviderTestSession(ModelUri);
        var reader = new TestReader
        {
            Assets =
            [
                .. Enumerable.Range(0, 5).Select(index => Asset() with
                {
                    Identifier = index == 0 ? "robot.usda" : $"part-{index}.usda",
                    Size = OpenUsdCompanionProvider.MaxAssetBytes
                })
            ]
        };
        var provider = new OpenUsdCompanionProvider((_, _) => reader, new TestExporter());

        Assert.That(
            async () => await provider.ExecuteAsync(
                session.Context, s_target, "verify-assets", null, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        Assert.That(reader.FileReads, Is.Zero);
        Assert.That(reader.Disposals, Is.EqualTo(1));
    }

    [Test]
    public void AssetSizeMustMatchEvenWhenTheActualDigestMatches()
    {
        var session = new CellProviderTestSession(ModelUri);
        var reader = new TestReader { Assets = [Asset() with { Size = 1 }] };
        var provider = new OpenUsdCompanionProvider((_, _) => reader, new TestExporter());

        Assert.That(
            async () => await provider.ExecuteAsync(
                session.Context, s_target, "verify-assets", null, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadSecurityChecksFailed));
        Assert.That(reader.FileReads, Is.EqualTo(1));
        Assert.That(reader.Disposals, Is.EqualTo(1));
    }

    [Test]
    public void AdvertisedAssetSizeIsCheckedBeforeOpeningTheFile()
    {
        var session = new CellProviderTestSession(ModelUri);
        var reader = new TestReader
        {
            Assets = [Asset() with { Size = (ulong)OpenUsdCompanionProvider.MaxAssetBytes + 1 }]
        };
        var provider = new OpenUsdCompanionProvider((_, _) => reader, new TestExporter());

        Assert.That(
            async () => await provider.ExecuteAsync(
                session.Context, s_target, "verify-assets", null, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>());
        Assert.That(reader.FileReads, Is.Zero);
        Assert.That(reader.Disposals, Is.EqualTo(1));
    }

    [Test]
    public void AnOversizedAssetRegistryCannotStartAnyDownloads()
    {
        var session = new CellProviderTestSession(ModelUri);
        var reader = new TestReader
        {
            Assets = [.. Enumerable.Range(0, 33).Select(index => Asset() with { Identifier = $"asset-{index}.usda" })]
        };
        var provider = new OpenUsdCompanionProvider((_, _) => reader, new TestExporter());
        Assert.That(
            async () => await provider.ExecuteAsync(
                session.Context, s_target, "verify-assets", null, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>());
        Assert.That(reader.RequestedAssetLimit, Is.EqualTo(32));
        Assert.That(reader.FileReads, Is.Zero);
    }

    [TestCase("../escape.usda")]
    [TestCase("http://external.example/root.usda")]
    [TestCase(@"C:\outside.usda")]
    [TestCase("CON.usda")]
    [TestCase("ualens-snapshot.usda")]
    [TestCase("@injected@.usda")]
    public void UntrustedAssetIdentifiersFailBeforeTheFileIsOpened(string identifier)
    {
        var session = new CellProviderTestSession(ModelUri);
        var reader = new TestReader { Assets = [Asset() with { Identifier = identifier }] };
        var provider = new OpenUsdCompanionProvider((_, _) => reader, new TestExporter());
        Assert.That(
            async () => await provider.ExecuteAsync(
                session.Context, s_target, "verify-assets", null, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>());
        Assert.That(reader.FileReads, Is.Zero);
    }

    [Test]
    public void CrossServerComponentsAreNotFederatedDuringVerification()
    {
        var session = new CellProviderTestSession(ModelUri);
        var reader = new TestReader();
        reader.Representations[0].Components.Add(new OpenUsdConnector.ComponentInfo
        {
            ComponentEndpointUrl = "opc.tcp://another-server:4840"
        });
        var provider = new OpenUsdCompanionProvider((_, _) => reader, new TestExporter());
        Assert.That(
            async () => await provider.ExecuteAsync(
                session.Context, s_target, "verify-assets", null, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>());
        Assert.That(reader.AssetMetadataReads, Is.Zero);
        Assert.That(reader.FileReads, Is.Zero);
    }

    [Test]
    public async Task CancellationDuringDownloadDisposesTheReaderAndDoesNotExportAsync()
    {
        var session = new CellProviderTestSession(ModelUri);
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ByteString>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new TestReader
        {
            ReadAsset = token =>
            {
                started.SetResult();
                return new ValueTask<ByteString>(release.Task.WaitAsync(token));
            }
        };
        var exporter = new TestExporter();
        var provider = new OpenUsdCompanionProvider((_, _) => reader, exporter);
        Task<CompanionOperationResult> operation = provider.ExecuteAsync(
            session.Context, s_target, "export", NewDestination(), cancellation.Token).AsTask();
        Task first = await Task.WhenAny(started.Task, operation).WaitAsync(TimeSpan.FromSeconds(10))
            .ConfigureAwait(false);
        if (first == operation)
        {
            await operation.ConfigureAwait(false);
        }
        Assert.That(started.Task.IsCompletedSuccessfully, Is.True, "The pending asset read must start.");
        await cancellation.CancelAsync().ConfigureAwait(false);

        await Assert.ThatAsync(() => operation, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        Assert.That(reader.Disposals, Is.EqualTo(1));
        Assert.That(exporter.Writes, Is.Zero);
    }

    [Test]
    public void DiscoveryFailureIsPropagatedAndStillDisposesTheConnectorReader()
    {
        var session = new CellProviderTestSession(ModelUri);
        var failure = new ServiceResultException(StatusCodes.BadUserAccessDenied);
        var reader = new TestReader { DiscoveryFailure = failure };
        var provider = new OpenUsdCompanionProvider((_, _) => reader, new TestExporter());
        Assert.That(
            async () => await provider.DiscoverAsync(session.Context, CancellationToken.None).ConfigureAwait(false),
            Throws.Exception.SameAs(failure));
        Assert.That(reader.Disposals, Is.EqualTo(1));
    }

    [Test]
    public void APreviouslySelectedRepresentationMustStillExist()
    {
        var session = new CellProviderTestSession(ModelUri);
        var reader = new TestReader { Representations = [] };
        var provider = new OpenUsdCompanionProvider((_, _) => reader, new TestExporter());
        Assert.That(
            async () => await provider.InspectAsync(session.Context, s_target, CancellationToken.None)
                .ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>());
        Assert.That(reader.AssetMetadataReads, Is.Zero);
        Assert.That(reader.Disposals, Is.EqualTo(1));
    }

    [Test]
    public async Task DefaultAssetReaderReleasesContinuationWhenItsBoundIsReachedAsync()
    {
        var session = new CellProviderTestSession(ModelUri);
        var folder = new NodeId("assets", session.NamespaceIndex);
        session.Children[(s_stage, "Assets")] = folder;
        ByteString continuation = ByteString.From([1, 2, 3]);
        session.Session.Setup(item => item.BrowseAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(new BrowseResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results =
                [
                    new BrowseResult
                    {
                        StatusCode = StatusCodes.Good,
                        ContinuationPoint = continuation,
                        References = [session.Reference(s_asset, "root", Opc.Ua.ObjectTypeIds.FileType)]
                    }
                ],
                DiagnosticInfos = []
            }));
        session.Session.Setup(item => item.BrowseNextAsync(
                It.IsAny<RequestHeader>(), true, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(new BrowseNextResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = [new BrowseResult { StatusCode = StatusCodes.Good }],
                DiagnosticInfos = []
            }));
        var reader = new OpenUsdCompanionReader(session.Context, OpenUsdCompanionProvider.CreateOptions());
        await using (reader.ConfigureAwait(false))
        {
            await Assert.ThatAsync(
                () => reader.ReadAssetsAsync(s_stage, 1, CancellationToken.None),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
        }
        session.Session.Verify(item => item.BrowseNextAsync(
            It.IsAny<RequestHeader>(), true,
            It.Is<ArrayOf<ByteString>>(points => points.Count == 1 && points[0] == continuation),
            It.IsAny<CancellationToken>()), Times.Once);
        session.VerifyNoMutationOrSessionOwnership();
    }

    private static OpenUsdConnector.RepresentationInfo Representation()
    {
        return new OpenUsdConnector.RepresentationInfo
        {
            NodeId = s_representation,
            StageNodeId = s_stage,
            PrimPath = "/World/Robot",
            RootLayerIdentifier = "robot.usda",
            RootLayerDigest = new ByteString(SHA256.HashData(s_content.Span)),
            DigestAlgorithm = OpenUsdDigestAlgorithm.Sha256
        };
    }

    private static OpenUsdCompanionAsset Asset()
    {
        return new OpenUsdCompanionAsset(
            s_asset, "robot.usda", OpenUsdAssetKind.RootLayer, (ulong)s_content.Length,
            new ByteString(SHA256.HashData(s_content.Span)), OpenUsdDigestAlgorithm.Sha256);
    }

    private static string NewDestination()
    {
        return OpenUsdTestPaths.NewDestination();
    }

    private sealed class TestReader : IOpenUsdCompanionReader
    {
        public ArrayOf<OpenUsdConnector.RepresentationInfo> Representations { get; init; } = [Representation()];

        public ArrayOf<OpenUsdCompanionAsset> Assets { get; init; } = [Asset()];

        public Exception? DiscoveryFailure { get; init; }

        public Func<CancellationToken, ValueTask<ByteString>>? ReadAsset { get; init; }

        public int Disposals { get; private set; }

        public int AssetMetadataReads { get; private set; }

        public int FileReads { get; private set; }

        public int RequestedAssetLimit { get; private set; }

        public NodeId RequestedStage { get; private set; }

        public List<NodeId> ReadNodes { get; } = [];

        public Task<ArrayOf<OpenUsdConnector.RepresentationInfo>> DiscoverAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DiscoveryFailure is null
                ? Task.FromResult(Representations)
                : Task.FromException<ArrayOf<OpenUsdConnector.RepresentationInfo>>(DiscoveryFailure);
        }

        public Task<string> ReadNameAsync(NodeId nodeId, CancellationToken cancellationToken)
        {
            return Task.FromResult("Robot representation");
        }

        public Task<ArrayOf<OpenUsdCompanionAsset>> ReadAssetsAsync(
            NodeId stageNodeId, int maximum, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedStage = stageNodeId;
            RequestedAssetLimit = maximum;
            AssetMetadataReads++;
            return Task.FromResult(Assets);
        }

        public Task<DataValue> ReadValueAsync(NodeId nodeId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadNodes.Add(nodeId);
            return Task.FromResult(new DataValue(Variant.From(3d)));
        }

        public ValueTask<ByteString> ReadAssetAsync(NodeId assetNodeId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileReads++;
            return ReadAsset?.Invoke(cancellationToken) ?? ValueTask.FromResult(s_content);
        }

        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestExporter : IOpenUsdCompanionExporter
    {
        public int Writes { get; private set; }

        public string? Destination { get; private set; }

        public string? RootLayer { get; private set; }

        public ArrayOf<OpenUsdVerifiedAsset> Assets { get; private set; }

        public Task WriteAsync(
            string destination,
            string rootLayerIdentifier,
            string primPath,
            ArrayOf<OpenUsdVerifiedAsset> assets,
            ArrayOf<OpenUsdCompanionBindingValue> snapshot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Writes++;
            Destination = destination;
            RootLayer = rootLayerIdentifier;
            Assets = assets;
            return Task.CompletedTask;
        }
    }

    private const string ModelUri = "http://opcfoundation.org/UA/OpenUSD/";
    private static readonly NodeId s_representation = new("representation", 2);
    private static readonly NodeId s_stage = new("stage", 2);
    private static readonly NodeId s_asset = new("asset", 2);
    private static readonly NodeId s_source = new("temperature", 2);
    private static readonly ByteString s_content = new(Encoding.UTF8.GetBytes("#usda 1.0\n\ndef Xform \"World\" {}\n"));
    private static readonly CompanionTarget s_target =
        new("openusd", s_representation, "Robot", "OpenUsdRepresentation");
}
