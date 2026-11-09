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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Gpos;
using Opc.Ua.Positioning.Client;

namespace Opc.Ua.Positioning.Tests
{
    /// <summary>
    /// Protocol-level regression tests for opt-in strict Positioning reads.
    /// </summary>
    [TestFixture]
    [Category("Positioning")]
    public sealed partial class PositioningStrictReadTests
    {
        [Test]
        public void OptionsDefaultsArePositiveAndLegacyFrameDepthIsUnchanged()
        {
            var options = new PositioningClientReadOptions();
            Mock<ISession> session = CreateSession();
            var legacy = new PositioningClientOperations(session.Object, Mock.Of<ITelemetryContext>());
            Assert.Multiple(
                () =>
                {
                    Assert.That(options.MaximumBrowseReferences, Is.EqualTo(1024));
                    Assert.That(options.MaximumBrowsePages, Is.EqualTo(128));
                    Assert.That(options.MaximumFrameChainLength, Is.EqualTo(128));
                    Assert.That(options.MaximumReadArrayItems, Is.EqualTo(1024));
                    Assert.That(legacy.Strict, Is.False);
                    Assert.That(legacy.MaximumFrameChainLength, Is.EqualTo(1024));
                    Assert.That(Operations(session).Strict, Is.True);
                    Assert.That(Operations(session).MaximumFrameChainLength, Is.EqualTo(128));
                });
        }

        [TestCase("references", 0)]
        [TestCase("references", -1)]
        [TestCase("pages", 0)]
        [TestCase("pages", -1)]
        [TestCase("frames", 0)]
        [TestCase("frames", -1)]
        [TestCase("array", 0)]
        [TestCase("array", -1)]
        public void BothConstructorsAndHostingRejectEachNonpositiveLimit(string limit, int value)
        {
            PositioningClientReadOptions options = limit switch
            {
                "references" => new() { MaximumBrowseReferences = value },
                "pages" => new() { MaximumBrowsePages = value },
                "frames" => new() { MaximumFrameChainLength = value },
                _ => new() { MaximumReadArrayItems = value }
            };
            Mock<ISession> session = CreateSession();
            ITelemetryContext telemetry = Mock.Of<ITelemetryContext>();
            IOpcUaClientBuilder builder = new ServiceCollection().AddOpcUa().AddClient(_ => { });
            Assert.Multiple(
                () =>
                {
                    Assert.That(
                        () => new RelativeSpatialLocationClient(session.Object, telemetry, options),
                        Throws.TypeOf<ArgumentOutOfRangeException>());
                    Assert.That(
                        () => new GlobalPositioningClient(session.Object, telemetry, options),
                        Throws.TypeOf<ArgumentOutOfRangeException>());
                    Assert.That(
                        () => builder.AddPositioningClient(options),
                        Throws.TypeOf<ArgumentOutOfRangeException>());
                });
        }

        [Test]
        public void BothConstructorsRejectNullOptionsAndBorrowTheirSession()
        {
            Mock<ISession> session = CreateSession();
            ITelemetryContext telemetry = Mock.Of<ITelemetryContext>();
            var options = new PositioningClientReadOptions();
            var relative = new RelativeSpatialLocationClient(session.Object, telemetry, options);
            var global = new GlobalPositioningClient(session.Object, telemetry, options);
            Assert.Multiple(
                () =>
                {
                    Assert.That(relative.Session, Is.SameAs(session.Object));
                    Assert.That(global.Session, Is.SameAs(session.Object));
                    Assert.That(relative.Telemetry, Is.SameAs(telemetry));
                    Assert.That(global.Telemetry, Is.SameAs(telemetry));
                    Assert.That(
                        () => new RelativeSpatialLocationClient(session.Object, telemetry, null!),
                        Throws.TypeOf<ArgumentNullException>());
                    Assert.That(
                        () => new GlobalPositioningClient(session.Object, telemetry, null!),
                        Throws.TypeOf<ArgumentNullException>());
                    Assert.That(
                        () => new RelativeSpatialLocationClient(null!, telemetry, options),
                        Throws.TypeOf<ArgumentNullException>());
                    Assert.That(
                        () => new GlobalPositioningClient(null!, telemetry, options),
                        Throws.TypeOf<ArgumentNullException>());
                    Assert.That(
                        () => new RelativeSpatialLocationClient(session.Object, null!, options),
                        Throws.TypeOf<ArgumentNullException>());
                    Assert.That(
                        () => new GlobalPositioningClient(session.Object, null!, options),
                        Throws.TypeOf<ArgumentNullException>());
                });
            VerifyBorrowed(session);
        }

        [Test]
        public void HostingAcceptsStrictOptionsAndRegistersBothFactories()
        {
            var services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(_ => { });
            Assert.That(builder.AddPositioningClient(new PositioningClientReadOptions()), Is.SameAs(builder));
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Multiple(
                () =>
                {
                    Assert.That(
                        provider.GetRequiredService<Func<CancellationToken, Task<RelativeSpatialLocationClient>>>(),
                        Is.Not.Null);
                    Assert.That(
                        provider.GetRequiredService<Func<CancellationToken, Task<GlobalPositioningClient>>>(),
                        Is.Not.Null);
                    Assert.That(() => builder.AddPositioningClient(null!), Throws.TypeOf<ArgumentNullException>());
                    Assert.That(
                        () => OpcUaPositioningClientBuilderExtensions.AddPositioningClient(
                            null!,
                            new PositioningClientReadOptions()),
                        Throws.TypeOf<ArgumentNullException>());
                });
        }

        private static IEnumerable<TestCaseData> NonGoodStatuses()
        {
            yield return new TestCaseData(StatusCodes.BadUserAccessDenied);
            yield return new TestCaseData(StatusCodes.Uncertain);
            yield return new TestCaseData(StatusCodes.BadNodeIdUnknown);
        }

        private static Mock<ISession> CreateSession(bool includeModels = true)
        {
            var session = new Mock<ISession>(MockBehavior.Strict);
            ServiceMessageContext context = ServiceMessageContext.Create(null);
            if (includeModels)
            {
                context.NamespaceUris.GetIndexOrAppend(Rsl.Namespaces.RSL);
                context.NamespaceUris.GetIndexOrAppend(Gpos.Namespaces.GPOS);
            }
            context.NamespaceUris.GetIndexOrAppend(kInstanceNamespace);
            session.SetupGet(s => s.NamespaceUris).Returns(context.NamespaceUris);
            session.SetupGet(s => s.MessageContext).Returns(context);
            session.SetupGet(s => s.Factory).Returns(context.Factory);
            session.SetupGet(s => s.OperationLimits).Returns(new OperationLimits());
            session.SetupGet(s => s.ServerCapabilities).Returns(new ServerCapabilities());
            session.SetupGet(s => s.ContinuationPointPolicy).Returns(ContinuationPointPolicy.Default);
            var cache = new Mock<INodeCache>();
            cache.Setup(
                c => c.IsTypeOfAsync(
                    It.IsAny<ExpandedNodeId>(),
                    It.IsAny<ExpandedNodeId>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    (ExpandedNodeId actual, ExpandedNodeId expected, CancellationToken _) =>
                        new ValueTask<bool>(actual == expected));
            session.SetupGet(s => s.NodeCache).Returns(cache.Object);
            return session;
        }

        private static PositioningClientOperations Operations(
            Mock<ISession> session,
            PositioningClientReadOptions? options = null)
        {
            return new PositioningClientOperations(
                session.Object,
                Mock.Of<ITelemetryContext>(),
                options ?? new PositioningClientReadOptions());
        }

        private static RelativeSpatialLocationClient Relative(
            Mock<ISession> session,
            PositioningClientReadOptions? options = null)
        {
            return new RelativeSpatialLocationClient(
                session.Object,
                Mock.Of<ITelemetryContext>(),
                options ?? new PositioningClientReadOptions());
        }

        private static GlobalPositioningClient Global(
            Mock<ISession> session,
            PositioningClientReadOptions? options = null)
        {
            return new GlobalPositioningClient(
                session.Object,
                Mock.Of<ITelemetryContext>(),
                options ?? new PositioningClientReadOptions());
        }

        private static ReferenceDescription Reference(string name = "frame")
        {
            return new ReferenceDescription
            {
                NodeId = new NodeId(name, 3),
                TypeDefinition = Rsl.VariableTypeIds.SpatialLocationType,
                BrowseName = new QualifiedName(name, 3),
                DisplayName = new LocalizedText(name),
                NodeClass = NodeClass.Variable
            };
        }

        private static BrowsePathResult Resolved(NodeId nodeId)
        {
            return new BrowsePathResult
            {
                Targets = [new BrowsePathTarget { TargetId = nodeId, RemainingPathIndex = uint.MaxValue }]
            };
        }

        private static void SetupTranslate(
            Mock<ISession> session,
            Func<BrowsePath, BrowsePathResult> result)
        {
            session.Setup(
                s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    (RequestHeader? _, ArrayOf<BrowsePath> paths, CancellationToken _) =>
                    {
                        Assert.That(paths.Count, Is.EqualTo(1));
                        return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                            new TranslateBrowsePathsToNodeIdsResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = [result(paths[0])]
                            });
                    });
        }

        private static void SetupRead(
            Mock<ISession> session,
            Func<NodeId, DataValue> result,
            Action<ArrayOf<ReadValueId>>? inspect = null)
        {
            session.Setup(
                s => s.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    0,
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    (
                        RequestHeader? _,
                        double _,
                        TimestampsToReturn _,
                        ArrayOf<ReadValueId> reads,
                        CancellationToken _) =>
                    {
                        inspect?.Invoke(reads);
                        var values = new DataValue[reads.Count];
                        for (int ii = 0; ii < reads.Count; ii++)
                        {
                            Assert.That(reads[ii].AttributeId, Is.EqualTo(Attributes.Value));
                            values[ii] = result(reads[ii].NodeId);
                        }
                        return new ValueTask<ReadResponse>(
                            new ReadResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = values.ToArrayOf()
                            });
                    });
        }

        private static void SetupBrowse(Mock<ISession> session, Func<BrowseResult> result)
        {
            session.Setup(
                s => s.BrowseAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ViewDescription?>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    (
                        RequestHeader? _,
                        ViewDescription? _,
                        uint maximum,
                        ArrayOf<BrowseDescription> requests,
                        CancellationToken _) =>
                    {
                        Assert.That(requests.Count, Is.EqualTo(1));
                        Assert.That(maximum, Is.InRange(1u, 64u));
                        return new ValueTask<BrowseResponse>(
                            new BrowseResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = [result()]
                            });
                    });
        }

        private static void SetupNext(
            Mock<ISession> session,
            Func<bool, ByteString, CancellationToken, BrowseResult> result)
        {
            session.Setup(
                s => s.BrowseNextAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    (RequestHeader? _, bool release, ArrayOf<ByteString> points, CancellationToken token) =>
                    {
                        Assert.That(points.Count, Is.EqualTo(1));
                        return new ValueTask<BrowseNextResponse>(
                            new BrowseNextResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = [result(release, points[0], token)]
                            });
                    });
        }

        private static void SetupRelease(
            Mock<ISession> session,
            List<ByteString> released,
            bool emptyResults = false)
        {
            session.Setup(
                s => s.BrowseNextAsync(
                    It.IsAny<RequestHeader?>(),
                    true,
                    It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    (RequestHeader? _, bool _, ArrayOf<ByteString> points, CancellationToken token) =>
                    {
                        Assert.That(points.Count, Is.EqualTo(1));
                        Assert.That(token.CanBeCanceled, Is.True);
                        Assert.That(token.IsCancellationRequested, Is.False);
                        released.Add(points[0]);
                        return new ValueTask<BrowseNextResponse>(
                            new BrowseNextResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = emptyResults ? [] : [new BrowseResult()]
                            });
                    });
        }

        private static ThreeDFrame Frame(double x = 1, double roll = 0)
        {
            return new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { X = x },
                Orientation = new ThreeDOrientation { A = roll }
            };
        }

        private static GlobalPositionDataType Position(double longitude = 8, double latitude = 47)
        {
            return new GlobalPositionDataType { Longitude = longitude, Latitude = latitude };
        }

        private static DataValue Structure(IEncodeable value, StatusCode status = default)
        {
            return new DataValue(Variant.From(new ExtensionObject(value)), status, s_timestamp, s_timestamp);
        }

        private static void SetupFrame(
            Mock<ISession> session,
            StatusCode frameStatus = default,
            StatusCode baseStatus = default)
        {
            SetupTranslate(session, _ => Resolved(s_base));
            SetupRead(
                session,
                node => node == s_frame
                    ? Structure(Frame(), frameStatus)
                    : new DataValue(new Variant(NodeId.Null), baseStatus));
        }

        private static void SetupGlobalMetadata(Mock<ISession> session)
        {
            SetupTranslate(
                session,
                path => Resolved(
                    path.RelativePath.Elements[0].TargetName.Name switch
                    {
                        "SourceId" => s_source,
                        "CoordinateReferenceSystem" => s_crs,
                        "Position" => s_position,
                        "GroundControlPoints" => s_gcps,
                        _ => throw new AssertionException("Unexpected metadata path.")
                    }));
        }

        private static DataValue GlobalValue(NodeId node)
        {
            if (node == s_source)
            {
                return new DataValue(new Variant(s_zone));
            }
            if (node == s_crs)
            {
                return new DataValue(new Variant(4326u));
            }
            if (node == s_location)
            {
                return Structure(new GlobalLocationDataType { Position = Position() });
            }
            return Structure(Position());
        }

        private static async Task AssertStatusAsync(Func<Task> operation, StatusCode expected)
        {
            await Assert.ThatAsync(
                operation,
                Throws.InstanceOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(expected)).ConfigureAwait(false);
        }

        private static async Task<T> FailureAsync<T>(Func<Task> operation)
            where T : Exception
        {
            Exception? failure = null;
            try
            {
                await operation().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            Assert.That(failure, Is.TypeOf<T>());
            return (T)failure!;
        }

        private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> values)
        {
            var result = new List<T>();
            await foreach (T value in values.ConfigureAwait(false))
            {
                result.Add(value);
            }
            return result;
        }

        private static void VerifyBorrowed(Mock<ISession> session)
        {
            session.Verify(s => s.Dispose(), Times.Never);
            session.Verify(
                s => s.WriteAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<WriteValue>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            session.Verify(
                s => s.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        private static void VerifyNoRead(Mock<ISession> session)
        {
            session.Verify(
                s => s.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        private const string kInstanceNamespace = "urn:positioning:strict:instances";
        private static readonly NodeId s_frame = new("frame", 3);
        private static readonly NodeId s_base = new("base", 3);
        private static readonly NodeId s_position = new("position", 3);
        private static readonly NodeId s_location = new("location", 3);
        private static readonly NodeId s_source = new("source", 3);
        private static readonly NodeId s_crs = new("crs", 3);
        private static readonly NodeId s_zone = new("zone", 3);
        private static readonly NodeId s_gcps = new("gcps", 3);
        private static readonly DateTimeUtc s_timestamp = new(new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc));
    }
}
