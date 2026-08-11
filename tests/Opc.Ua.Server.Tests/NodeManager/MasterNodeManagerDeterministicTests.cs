/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Deterministic, offline unit tests for the residual dispatcher surface of
    /// <see cref="MasterNodeManager"/> that is reachable without a live client
    /// session or transport: constructor guards, RegisterNodes pass-through,
    /// Browse / BrowseNext / TranslateBrowsePaths / Read / Write / HistoryRead /
    /// HistoryUpdate / Call per-item validation and routing, and the
    /// monitored-item argument guards and dispatch. The per-item validation
    /// StatusCodes for AddNodes / DeleteNodes / AddReferences / DeleteReferences
    /// are intentionally not retested here (covered by
    /// MasterNodeManagerNodeManagementTests).
    /// </summary>
    [TestFixture]
    [Category("MasterNodeManager")]
    [Category("MasterNodeManagerDeterministic")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class MasterNodeManagerDeterministicTests
    {
        private ServerFixture<StandardServer> m_fixture = null!;
        private StandardServer m_server = null!;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        [Test]
        public void Constructor_NullServer_ThrowsArgumentNullException()
        {
            Assert.That(
                () => new MasterNodeManager(
                    null!,
                    m_fixture.Config,
                    null,
                    System.Array.Empty<INodeManager>()),
                Throws.TypeOf<System.ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("server"));
        }

        [Test]
        public void Constructor_NullConfiguration_ThrowsArgumentNullException()
        {
            Assert.That(
                () => new MasterNodeManager(
                    m_server.CurrentInstance,
                    null!,
                    null,
                    System.Array.Empty<INodeManager>()),
                Throws.TypeOf<System.ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("configuration"));
        }

        [Test]
        public void Constructor_NoAdditionalManagers_RegistersConfigurationAndCore()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();

            Assert.That(sut.AsyncNodeManagers, Has.Count.EqualTo(2));
            Assert.That(sut.NodeManagers, Has.Count.EqualTo(2));
        }

        [Test]
        public void RegisterNodes_UnknownNodeIds_ReturnsInputNodeIds()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            ArrayOf<NodeId> input = new NodeId[]
            {
                new(1000u),
                new("register-me", 0)
            }.ToArrayOf();

            sut.RegisterNodes(ctx, input, out ArrayOf<NodeId> registered);

            Assert.That(registered.Count, Is.EqualTo(2));
            Assert.That(registered[0], Is.EqualTo(new NodeId(1000u)));
            Assert.That(registered[1], Is.EqualTo(new NodeId("register-me", 0)));
        }

        [Test]
        public void BrowseAsync_NullContext_ThrowsArgumentNullException()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();

            Assert.That(
                async () => await sut.BrowseAsync(
                    null!,
                    new ViewDescription(),
                    0u,
                    System.Array.Empty<BrowseDescription>().ToArrayOf(),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("context"));
        }

        [Test]
        public void BrowseAsync_UnknownViewId_ThrowsBadViewIdUnknown()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var view = new ViewDescription { ViewId = new NodeId(99999u) };

            Assert.That(
                async () => await sut.BrowseAsync(
                    ctx,
                    view,
                    0u,
                    System.Array.Empty<BrowseDescription>().ToArrayOf(),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadViewIdUnknown));
        }

        [Test]
        public async Task BrowseAsync_EmptyBatch_ReturnsEmptyResultsAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<BrowseResult> results, _) = await sut.BrowseAsync(
                ctx,
                new ViewDescription(),
                0u,
                System.Array.Empty<BrowseDescription>().ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.Zero);
        }

        [Test]
        public async Task BrowseAsync_UnknownNode_ReturnsBadNodeIdUnknownAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var nodeToBrowse = new BrowseDescription
            {
                NodeId = new NodeId(99999u),
                BrowseDirection = BrowseDirection.Forward
            };

            (ArrayOf<BrowseResult> results, _) = await sut.BrowseAsync(
                ctx,
                new ViewDescription(),
                0u,
                new BrowseDescription[] { nodeToBrowse }.ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(results[0].ContinuationPoint.IsNull, Is.True);
        }

        [Test]
        public async Task BrowseAsyncCompletedBrowseReturnsNullContinuationPointAsync()
        {
            IMasterNodeManager sut = m_server.CurrentInstance.NodeManager;
            OperationContext ctx = CreateContext();

            var nodeToBrowse = new BrowseDescription
            {
                NodeId = ObjectIds.ViewsFolder,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                ResultMask = (uint)BrowseResultMask.All
            };

            (ArrayOf<BrowseResult> results, _) = await sut.BrowseAsync(
                ctx,
                new ViewDescription(),
                0u,
                new BrowseDescription[] { nodeToBrowse }.ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(results[0].ContinuationPoint.IsNull, Is.True);
        }

        [Test]
        public async Task BrowseAsync_UnknownReferenceType_ReturnsBadReferenceTypeIdInvalidAsync()
        {
            IMasterNodeManager sut = m_server.CurrentInstance.NodeManager;
            OperationContext ctx = CreateContext();

            var nodeToBrowse = new BrowseDescription
            {
                NodeId = ObjectIds.ObjectsFolder,
                ReferenceTypeId = new NodeId(88888u),
                BrowseDirection = BrowseDirection.Forward
            };

            (ArrayOf<BrowseResult> results, _) = await sut.BrowseAsync(
                ctx,
                new ViewDescription(),
                0u,
                new BrowseDescription[] { nodeToBrowse }.ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadReferenceTypeIdInvalid));
        }

        [Test]
        public async Task BrowseAsync_InvalidBrowseDirection_ReturnsBadBrowseDirectionInvalidAsync()
        {
            IMasterNodeManager sut = m_server.CurrentInstance.NodeManager;
            OperationContext ctx = CreateContext();

            var nodeToBrowse = new BrowseDescription
            {
                NodeId = ObjectIds.ObjectsFolder,
                BrowseDirection = (BrowseDirection)99
            };

            (ArrayOf<BrowseResult> results, _) = await sut.BrowseAsync(
                ctx,
                new ViewDescription(),
                0u,
                new BrowseDescription[] { nodeToBrowse }.ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadBrowseDirectionInvalid));
        }

        [Test]
        public async Task BrowseAsync_WhenNoContinuationPointCanBeAssigned_ReturnsBadNoContinuationPointsWithoutContinuationPointAsync()
        {
            MasterNodeManager sut = (MasterNodeManager)m_server.CurrentInstance.NodeManager;
            OperationContext ctx = CreateContextWithContinuationStore();
            uint originalLimit = GetMaxBrowseContinuationPointsPerBrowse(sut);

            try
            {
                SetMaxBrowseContinuationPointsPerBrowse(sut, 0u);

                var nodeToBrowse = new BrowseDescription
                {
                    NodeId = ObjectIds.RootFolder,
                    BrowseDirection = BrowseDirection.Forward
                };

                (ArrayOf<BrowseResult> results, _) = await sut.BrowseAsync(
                    ctx,
                    new ViewDescription(),
                    1u,
                    new BrowseDescription[] { nodeToBrowse }.ToArrayOf(),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);

                Assert.That(results.Count, Is.EqualTo(1));
                Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadNoContinuationPoints));
                Assert.That(results[0].ContinuationPoint.IsEmpty, Is.True);
            }
            finally
            {
                SetMaxBrowseContinuationPointsPerBrowse(sut, originalLimit);
            }
        }

        [Test]
        public void BrowseNextAsync_NullContext_ThrowsArgumentNullException()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();

            Assert.That(
                async () => await sut.BrowseNextAsync(
                    null!,
                    false,
                    System.Array.Empty<ByteString>().ToArrayOf(),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("context"));
        }

        [Test]
        public async Task BrowseNextAsync_EmptyBatch_ReturnsEmptyResultsAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<BrowseResult> results, _) = await sut.BrowseNextAsync(
                ctx,
                false,
                System.Array.Empty<ByteString>().ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.Zero);
        }

        [Test]
        public async Task BrowseNextAsync_InvalidContinuationPoint_ReturnsBadContinuationPointInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            ArrayOf<ByteString> continuationPoints = new ByteString[]
            {
                new byte[] { 1, 2, 3, 4 }.ToByteString()
            }.ToArrayOf();

            (ArrayOf<BrowseResult> results, _) = await sut.BrowseNextAsync(
                ctx,
                false,
                continuationPoints,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
            Assert.That(results[0].ContinuationPoint.IsNull, Is.True);
        }

        [Test]
        public async Task BrowseNextAsync_ReleaseInvalidContinuationPoint_ReturnsGoodAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            ArrayOf<ByteString> continuationPoints = new ByteString[]
            {
                new byte[] { 1, 2, 3, 4 }.ToByteString()
            }.ToArrayOf();

            (ArrayOf<BrowseResult> results, _) = await sut.BrowseNextAsync(
                ctx,
                true,
                continuationPoints,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(results[0].ContinuationPoint.IsNull, Is.True);
        }

        [Test]
        public async Task BrowseNextAsync_WhenNoContinuationPointCanBeAssigned_ReturnsBadNoContinuationPointsWithoutContinuationPointAsync()
        {
            MasterNodeManager sut = (MasterNodeManager)m_server.CurrentInstance.NodeManager;
            OperationContext ctx = CreateContextWithContinuationStore();
            uint originalLimit = GetMaxBrowseContinuationPointsPerBrowse(sut);

            try
            {
                var nodeToBrowse = new BrowseDescription
                {
                    NodeId = ObjectIds.RootFolder,
                    BrowseDirection = BrowseDirection.Forward
                };

                (ArrayOf<BrowseResult> firstResults, _) = await sut.BrowseAsync(
                    ctx,
                    new ViewDescription(),
                    1u,
                    new BrowseDescription[] { nodeToBrowse }.ToArrayOf(),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);

                Assert.That(firstResults.Count, Is.EqualTo(1));
                Assert.That(firstResults[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(firstResults[0].ContinuationPoint.IsEmpty, Is.False);

                (ArrayOf<BrowseResult> nextResults, _) = await sut.BrowseNextAsync(
                    ctx,
                    false,
                    new ByteString[] { firstResults[0].ContinuationPoint }.ToArrayOf(),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);

                Assert.That(nextResults.Count, Is.EqualTo(1));
                Assert.That(nextResults[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(nextResults[0].ContinuationPoint.IsEmpty, Is.False);

                SetMaxBrowseContinuationPointsPerBrowse(sut, 0u);

                (ArrayOf<BrowseResult> finalResults, _) = await sut.BrowseNextAsync(
                    ctx,
                    false,
                    new ByteString[] { nextResults[0].ContinuationPoint }.ToArrayOf(),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);

                Assert.That(finalResults.Count, Is.EqualTo(1));
                Assert.That(finalResults[0].StatusCode, Is.EqualTo(StatusCodes.BadNoContinuationPoints));
                Assert.That(finalResults[0].ContinuationPoint.IsEmpty, Is.True);
            }
            finally
            {
                SetMaxBrowseContinuationPointsPerBrowse(sut, originalLimit);
            }
        }

        [Test]
        public async Task TranslateBrowsePathsToNodeIdsAsync_EmptyBatch_ReturnsEmptyResultsAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<BrowsePathResult> results, _) = await sut.TranslateBrowsePathsToNodeIdsAsync(
                ctx,
                System.Array.Empty<BrowsePath>().ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.Zero);
        }

        [Test]
        public async Task TranslateBrowsePathsToNodeIdsAsync_UnknownStartingNode_ReturnsBadNodeIdUnknownAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var browsePath = new BrowsePath
            {
                StartingNode = new NodeId(99999u),
                RelativePath = new RelativePath
                {
                    Elements = new RelativePathElement[]
                    {
                        new() { TargetName = new QualifiedName("Any", 0) }
                    }.ToArrayOf()
                }
            };

            (ArrayOf<BrowsePathResult> results, _) = await sut.TranslateBrowsePathsToNodeIdsAsync(
                ctx,
                new BrowsePath[] { browsePath }.ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task TranslateBrowsePathsToNodeIdsAsync_EmptyRelativePath_ReturnsBadNothingToDoAsync()
        {
            IMasterNodeManager sut = m_server.CurrentInstance.NodeManager;
            OperationContext ctx = CreateContext();

            var browsePath = new BrowsePath
            {
                StartingNode = ObjectIds.ObjectsFolder,
                RelativePath = new RelativePath()
            };

            (ArrayOf<BrowsePathResult> results, _) = await sut.TranslateBrowsePathsToNodeIdsAsync(
                ctx,
                new BrowsePath[] { browsePath }.ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task TranslateBrowsePathsToNodeIdsAsync_NullTargetName_ReturnsBadBrowseNameInvalidAsync()
        {
            IMasterNodeManager sut = m_server.CurrentInstance.NodeManager;
            OperationContext ctx = CreateContext();

            var browsePath = new BrowsePath
            {
                StartingNode = ObjectIds.ObjectsFolder,
                RelativePath = new RelativePath
                {
                    Elements = new RelativePathElement[]
                    {
                        new()
                    }.ToArrayOf()
                }
            };

            (ArrayOf<BrowsePathResult> results, _) = await sut.TranslateBrowsePathsToNodeIdsAsync(
                ctx,
                new BrowsePath[] { browsePath }.ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
        }

        [Test]
        public void ReadAsync_NegativeMaxAge_ThrowsBadMaxAgeInvalid()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            Assert.That(
                async () => await sut.ReadAsync(
                    ctx,
                    -1.0,
                    TimestampsToReturn.Neither,
                    System.Array.Empty<ReadValueId>().ToArrayOf(),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadMaxAgeInvalid));
        }

        [Test]
        public void ReadAsync_InvalidTimestampsToReturn_ThrowsBadTimestampsToReturnInvalid()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            Assert.That(
                async () => await sut.ReadAsync(
                    ctx,
                    0.0,
                    (TimestampsToReturn)99,
                    System.Array.Empty<ReadValueId>().ToArrayOf(),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTimestampsToReturnInvalid));
        }

        [Test]
        public async Task ReadAsync_EmptyBatch_ReturnsEmptyResultsAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<DataValue> values, _) = await sut.ReadAsync(
                ctx,
                0.0,
                TimestampsToReturn.Neither,
                System.Array.Empty<ReadValueId>().ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(values.Count, Is.Zero);
        }

        [Test]
        public async Task ReadAsync_NullNodeId_ReturnsBadNodeIdInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<DataValue> values, _) = await sut.ReadAsync(
                ctx,
                0.0,
                TimestampsToReturn.Neither,
                new ReadValueId[] { new() { AttributeId = Attributes.Value } }.ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(values[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public async Task ReadAsync_InvalidAttributeId_ReturnsBadAttributeIdInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var readValueId = new ReadValueId { NodeId = ObjectIds.Server, AttributeId = 0 };

            (ArrayOf<DataValue> values, _) = await sut.ReadAsync(
                ctx,
                0.0,
                TimestampsToReturn.Neither,
                new ReadValueId[] { readValueId }.ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(values[0].StatusCode, Is.EqualTo(StatusCodes.BadAttributeIdInvalid));
        }

        [Test]
        public async Task ReadAsync_UnknownNode_ReturnsBadNodeIdUnknownAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var readValueId = new ReadValueId { NodeId = new NodeId(99999u), AttributeId = Attributes.Value };

            (ArrayOf<DataValue> values, _) = await sut.ReadAsync(
                ctx,
                0.0,
                TimestampsToReturn.Neither,
                new ReadValueId[] { readValueId }.ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(values[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void WriteAsync_NullContext_ThrowsArgumentNullException()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();

            Assert.That(
                async () => await sut.WriteAsync(
                    null!,
                    System.Array.Empty<WriteValue>().ToArrayOf(),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("context"));
        }

        [Test]
        public async Task WriteAsync_EmptyBatch_ReturnsEmptyResultsAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<StatusCode> results, _) = await sut.WriteAsync(
                ctx,
                System.Array.Empty<WriteValue>().ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.Zero);
        }

        [Test]
        public async Task WriteAsync_NullNodeId_ReturnsBadNodeIdInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<StatusCode> results, _) = await sut.WriteAsync(
                ctx,
                new WriteValue[] { new() { AttributeId = Attributes.Value } }.ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public async Task WriteAsync_UnknownNode_ReturnsBadNodeIdUnknownAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var writeValue = new WriteValue
            {
                NodeId = new NodeId(99999u),
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(123))
            };

            (ArrayOf<StatusCode> results, _) = await sut.WriteAsync(
                ctx,
                new WriteValue[] { writeValue }.ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0], Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void HistoryReadAsync_NullDetails_ThrowsBadHistoryOperationInvalid()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            Assert.That(
                async () => await sut.HistoryReadAsync(
                    ctx,
                    default,
                    TimestampsToReturn.Neither,
                    false,
                    System.Array.Empty<HistoryReadValueId>().ToArrayOf(),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadHistoryOperationInvalid));
        }

        [Test]
        public async Task HistoryReadAsync_EmptyBatch_ReturnsEmptyResultsAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<HistoryReadResult> results, _) = await sut.HistoryReadAsync(
                ctx,
                new ExtensionObject(new ReadRawModifiedDetails()),
                TimestampsToReturn.Neither,
                false,
                System.Array.Empty<HistoryReadValueId>().ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.Zero);
        }

        [Test]
        public async Task HistoryReadAsync_NullNodeId_ReturnsBadNodeIdInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<HistoryReadResult> results, _) = await sut.HistoryReadAsync(
                ctx,
                new ExtensionObject(new ReadRawModifiedDetails()),
                TimestampsToReturn.Neither,
                false,
                new HistoryReadValueId[] { new() }.ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public async Task HistoryReadAsync_UnknownNode_ReturnsBadNodeIdUnknownAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var historyReadValueId = new HistoryReadValueId { NodeId = new NodeId(99999u) };

            (ArrayOf<HistoryReadResult> results, _) = await sut.HistoryReadAsync(
                ctx,
                new ExtensionObject(new ReadRawModifiedDetails()),
                TimestampsToReturn.Neither,
                false,
                new HistoryReadValueId[] { historyReadValueId }.ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task HistoryUpdateAsync_EmptyBatch_ReturnsEmptyResultsAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<HistoryUpdateResult> results, _) = await sut.HistoryUpdateAsync(
                ctx,
                System.Array.Empty<ExtensionObject>().ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.Zero);
        }

        [Test]
        public async Task HistoryUpdateAsync_NullNodeId_ReturnsBadNodeIdInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var details = new ExtensionObject(new UpdateDataDetails
            {
                PerformInsertReplace = PerformUpdateType.Insert
            });

            (ArrayOf<HistoryUpdateResult> results, _) = await sut.HistoryUpdateAsync(
                ctx,
                new ExtensionObject[] { details }.ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public async Task HistoryUpdateAsync_UnknownNode_ReturnsBadNodeIdUnknownAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var details = new ExtensionObject(new UpdateDataDetails
            {
                NodeId = new NodeId(99999u),
                PerformInsertReplace = PerformUpdateType.Insert
            });

            (ArrayOf<HistoryUpdateResult> results, _) = await sut.HistoryUpdateAsync(
                ctx,
                new ExtensionObject[] { details }.ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void CallAsync_NullContext_ThrowsArgumentNullException()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();

            Assert.That(
                async () => await sut.CallAsync(
                    null!,
                    System.Array.Empty<CallMethodRequest>().ToArrayOf(),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("context"));
        }

        [Test]
        public async Task CallAsync_EmptyBatch_ReturnsEmptyResultsAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<CallMethodResult> results, _) = await sut.CallAsync(
                ctx,
                System.Array.Empty<CallMethodRequest>().ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.Zero);
        }

        [Test]
        public async Task CallAsync_NullObjectId_ReturnsBadNodeIdInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<CallMethodResult> results, _) = await sut.CallAsync(
                ctx,
                new CallMethodRequest[] { new() }.ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public async Task CallAsync_NullMethodId_ReturnsBadMethodInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var request = new CallMethodRequest { ObjectId = ObjectIds.Server };

            (ArrayOf<CallMethodResult> results, _) = await sut.CallAsync(
                ctx,
                new CallMethodRequest[] { request }.ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadMethodInvalid));
        }

        [Test]
        public async Task CallAsync_UnknownObject_ReturnsBadNodeIdUnknownAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var request = new CallMethodRequest
            {
                ObjectId = new NodeId(99999u),
                MethodId = new NodeId(99998u)
            };

            (ArrayOf<CallMethodResult> results, _) = await sut.CallAsync(
                ctx,
                new CallMethodRequest[] { request }.ToArrayOf(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void CreateMonitoredItemsAsync_NullContext_ThrowsArgumentNullException()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();

            Assert.That(
                async () => await sut.CreateMonitoredItemsAsync(
                    null!,
                    1u,
                    0.0,
                    TimestampsToReturn.Both,
                    System.Array.Empty<MonitoredItemCreateRequest>().ToArrayOf(),
                    [],
                    [],
                    [],
                    false,
                    cancellationToken: CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("context"));
        }

        [Test]
        public void CreateMonitoredItemsAsync_NullErrors_ThrowsArgumentNullException()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            Assert.That(
                async () => await sut.CreateMonitoredItemsAsync(
                    ctx,
                    1u,
                    0.0,
                    TimestampsToReturn.Both,
                    System.Array.Empty<MonitoredItemCreateRequest>().ToArrayOf(),
                    null!,
                    [],
                    [],
                    false,
                    cancellationToken: CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("errors"));
        }

        [Test]
        public void CreateMonitoredItemsAsync_NullMonitoredItems_ThrowsArgumentNullException()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            Assert.That(
                async () => await sut.CreateMonitoredItemsAsync(
                    ctx,
                    1u,
                    0.0,
                    TimestampsToReturn.Both,
                    System.Array.Empty<MonitoredItemCreateRequest>().ToArrayOf(),
                    [],
                    [],
                    null!,
                    false,
                    cancellationToken: CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("monitoredItems"));
        }

        [Test]
        public void CreateMonitoredItemsAsync_NegativePublishingInterval_ThrowsArgumentOutOfRangeException()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            Assert.That(
                async () => await sut.CreateMonitoredItemsAsync(
                    ctx,
                    1u,
                    -1.0,
                    TimestampsToReturn.Both,
                    System.Array.Empty<MonitoredItemCreateRequest>().ToArrayOf(),
                    [],
                    [],
                    [],
                    false,
                    cancellationToken: CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentOutOfRangeException>()
                    .With.Property("ParamName").EqualTo("publishingInterval"));
        }

        [Test]
        public void CreateMonitoredItemsAsync_InvalidTimestampsToReturn_ThrowsBadTimestampsToReturnInvalid()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            Assert.That(
                async () => await sut.CreateMonitoredItemsAsync(
                    ctx,
                    1u,
                    0.0,
                    (TimestampsToReturn)99,
                    System.Array.Empty<MonitoredItemCreateRequest>().ToArrayOf(),
                    [],
                    [],
                    [],
                    false,
                    cancellationToken: CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTimestampsToReturnInvalid));
        }

        [Test]
        public void ModifyMonitoredItemsAsync_NullContext_ThrowsArgumentNullException()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();

            Assert.That(
                async () => await sut.ModifyMonitoredItemsAsync(
                    null!,
                    TimestampsToReturn.Both,
                    [],
                    System.Array.Empty<MonitoredItemModifyRequest>().ToArrayOf(),
                    [],
                    [],
                    cancellationToken: CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("context"));
        }

        [Test]
        public void ModifyMonitoredItemsAsync_InvalidTimestampsToReturn_ThrowsBadTimestampsToReturnInvalid()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            Assert.That(
                async () => await sut.ModifyMonitoredItemsAsync(
                    ctx,
                    (TimestampsToReturn)99,
                    [],
                    System.Array.Empty<MonitoredItemModifyRequest>().ToArrayOf(),
                    [],
                    [],
                    cancellationToken: CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTimestampsToReturnInvalid));
        }

        [Test]
        public void DeleteMonitoredItemsAsync_NullContext_ThrowsArgumentNullException()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();

            Assert.That(
                async () => await sut.DeleteMonitoredItemsAsync(
                    null!,
                    1u,
                    [],
                    [],
                    cancellationToken: CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("context"));
        }

        [Test]
        public async Task DeleteMonitoredItemsAsync_UnknownItem_ReturnsBadMonitoredItemIdInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            IMonitoredItem item = new Mock<IMonitoredItem>().Object;
            var itemsToDelete = new List<IMonitoredItem> { item };
            var errors = new List<ServiceResult> { ServiceResult.Good };

            await sut.DeleteMonitoredItemsAsync(
                ctx,
                1u,
                itemsToDelete,
                errors,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
        }

        [Test]
        public async Task DeleteMonitoredItemsAsyncDetachedItemReturnsGoodAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            using MonitoredItem item = CreateDetachedMonitoredItem();
            var itemsToDelete = new List<IMonitoredItem> { item };
            var errors = new List<ServiceResult> { ServiceResult.Good };

            await sut.DeleteMonitoredItemsAsync(
                CreateContext(),
                1u,
                itemsToDelete,
                errors,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task ModifyMonitoredItemsAsyncDetachedItemReturnsBadNodeIdUnknownAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            using MonitoredItem item = CreateDetachedMonitoredItem();
            var request = new MonitoredItemModifyRequest
            {
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = item.ClientHandle,
                    SamplingInterval = item.SamplingInterval,
                    QueueSize = item.QueueSize,
                    DiscardOldest = true
                }
            };
            var errors = new List<ServiceResult> { ServiceResult.Good };
            var filterResults = new List<MonitoringFilterResult> { null! };

            await sut.ModifyMonitoredItemsAsync(
                CreateContext(),
                TimestampsToReturn.Both,
                [item],
                new[] { request }.ToArrayOf(),
                errors,
                filterResults,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(request.Processed, Is.True);
        }

        [Test]
        public void SetMonitoringModeAsync_NullContext_ThrowsArgumentNullException()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();

            Assert.That(
                async () => await sut.SetMonitoringModeAsync(
                    null!,
                    MonitoringMode.Reporting,
                    [],
                    [],
                    cancellationToken: CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("context"));
        }

        [Test]
        public async Task SetMonitoringModeAsync_UnknownItem_ReturnsBadMonitoredItemIdInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            IMonitoredItem item = new Mock<IMonitoredItem>().Object;
            var itemsToModify = new List<IMonitoredItem> { item };
            var errors = new List<ServiceResult> { ServiceResult.Good };

            await sut.SetMonitoringModeAsync(
                ctx,
                MonitoringMode.Reporting,
                itemsToModify,
                errors,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
        }

        [Test]
        public async Task SetMonitoringModeAsyncDetachedItemUpdatesLocallyAndQueuesBadAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            using MonitoredItem item = CreateDetachedMonitoredItem(MonitoringMode.Disabled);
            var errors = new List<ServiceResult> { ServiceResult.Good };

            await sut.SetMonitoringModeAsync(
                CreateContext(),
                MonitoringMode.Reporting,
                [item],
                errors,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(item.MonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
            Assert.That(Publish(item).Peek().Value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void TransferMonitoredItemsAsync_NullContext_ThrowsArgumentNullException()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();

            Assert.That(
                async () => await sut.TransferMonitoredItemsAsync(
                    null!,
                    false,
                    [],
                    [],
                    new MonitoredItemTransferOptions(),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<System.ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("context"));
        }

        [Test]
        public async Task TransferMonitoredItemsAsync_NullMonitoredItem_ReturnsBadMonitoredItemIdInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            var monitoredItems = new List<IMonitoredItem> { null! };
            var errors = new List<ServiceResult> { ServiceResult.Good };

            await sut.TransferMonitoredItemsAsync(
                ctx,
                false,
                monitoredItems,
                errors,
                new MonitoredItemTransferOptions(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
        }

        [Test]
        public async Task TransferMonitoredItemsAsyncDetachedItemSucceedsAndQueuesBadAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            using MonitoredItem item = CreateDetachedMonitoredItem();
            var errors = new List<ServiceResult> { ServiceResult.Good };

            await sut.TransferMonitoredItemsAsync(
                CreateContext(),
                true,
                [item],
                errors,
                new MonitoredItemTransferOptions(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(Publish(item).Peek().Value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }


        [Test]
        public async Task TransferMonitoredItemsAsyncOwnerFailureLogsAndAllowsRetryAsync()
        {
            var sourceSession = new Mock<ISession>();
            sourceSession.SetupGet(session => session.Id).Returns(new NodeId(Guid.NewGuid()));
            sourceSession.SetupGet(session => session.EffectiveIdentity)
                .Returns(new Mock<IUserIdentity>().Object);
            sourceSession.SetupGet(session => session.PreferredLocales).Returns([]);
            var destinationSession = new Mock<ISession>();
            destinationSession.SetupGet(session => session.Id).Returns(new NodeId(Guid.NewGuid()));
            destinationSession.SetupGet(session => session.EffectiveIdentity)
                .Returns(new Mock<IUserIdentity>().Object);
            destinationSession.SetupGet(session => session.PreferredLocales).Returns([]);
            var owner = new Mock<IAsyncNodeManager>();
            var laterOwner = new Mock<IAsyncNodeManager>();
            ISession observedOwner = sourceSession.Object;
            ISession observedLaterOwner = sourceSession.Object;
            int ownerForwardCalls = 0;
            int laterForwardCalls = 0;
            bool failNextDestinationCallback = true;

            owner.Setup(nodeManager => nodeManager.TransferMonitoredItemsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<bool>(),
                    It.IsAny<IList<IMonitoredItem>>(),
                    It.IsAny<IList<bool>>(),
                    It.IsAny<IList<ServiceResult>>(),
                    It.IsAny<MonitoredItemTransferOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<OperationContext, bool, IList<IMonitoredItem>, IList<bool>, IList<ServiceResult>, MonitoredItemTransferOptions, CancellationToken>(
                    (context, sendInitialValues, monitoredItems, processedItems, errors, transferOptions, _) =>
                    {
                        for (int ii = 0; ii < monitoredItems.Count; ii++)
                        {
                            if (!processedItems[ii] &&
                                ReferenceEquals(monitoredItems[ii].NodeManager, owner.Object))
                            {
                                processedItems[ii] = true;
                                errors[ii] = ServiceResult.Good;
                                if (sendInitialValues &&
                                    !transferOptions.DeferInitialValues)
                                {
                                    monitoredItems[ii].SetupResendDataTrigger();
                                }
                            }
                        }
                        observedOwner = context.Session;
                        ownerForwardCalls++;
                    })
                .Returns(default(ValueTask));
            laterOwner.Setup(nodeManager => nodeManager.TransferMonitoredItemsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<bool>(),
                    It.IsAny<IList<IMonitoredItem>>(),
                    It.IsAny<IList<bool>>(),
                    It.IsAny<IList<ServiceResult>>(),
                    It.IsAny<MonitoredItemTransferOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<OperationContext, bool, IList<IMonitoredItem>, IList<bool>, IList<ServiceResult>, MonitoredItemTransferOptions, CancellationToken>(
                    (context, sendInitialValues, monitoredItems, processedItems, errors, transferOptions, _) =>
                    {
                        for (int ii = 0; ii < monitoredItems.Count; ii++)
                        {
                            if (!processedItems[ii] &&
                                ReferenceEquals(monitoredItems[ii].NodeManager, laterOwner.Object))
                            {
                                processedItems[ii] = true;
                                errors[ii] = ServiceResult.Good;
                                if (sendInitialValues &&
                                    !transferOptions.DeferInitialValues)
                                {
                                    monitoredItems[ii].SetupResendDataTrigger();
                                }
                            }
                        }
                        observedLaterOwner = context.Session;
                        laterForwardCalls++;
                        if (ReferenceEquals(context.Session, destinationSession.Object) &&
                            failNextDestinationCallback)
                        {
                            failNextDestinationCallback = false;
                            throw new InvalidOperationException("Later owner failed.");
                        }
                    })
                .Returns(default(ValueTask));

            using var sut = new MasterNodeManager(
                m_server.CurrentInstance,
                m_fixture.Config,
                null,
                owner.Object,
                laterOwner.Object);
            using var item = CreateMonitoredItem(owner.Object, sourceSession.Object, 2, 3, 42);
            using var laterItem = CreateMonitoredItem(laterOwner.Object, sourceSession.Object, 4, 5, 84);

            var failedErrors = new List<ServiceResult> { null!, null! };
            await sut.TransferMonitoredItemsAsync(
                new OperationContext(destinationSession.Object, DiagnosticsMasks.None),
                true,
                [item, laterItem],
                failedErrors,
                new MonitoredItemTransferOptions(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(observedOwner, Is.SameAs(destinationSession.Object));
                Assert.That(observedLaterOwner, Is.SameAs(destinationSession.Object));
                Assert.That(ownerForwardCalls, Is.EqualTo(1));
                Assert.That(laterForwardCalls, Is.EqualTo(1));
                Assert.That(failedErrors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(failedErrors[1].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
                Assert.That(item.IsResendData, Is.True);
                Assert.That(laterItem.IsResendData, Is.False);
            });
            _ = Publish(item);

            var retryErrors = new List<ServiceResult> { null!, null! };
            await sut.TransferMonitoredItemsAsync(
                new OperationContext(destinationSession.Object, DiagnosticsMasks.None),
                false,
                [item, laterItem],
                retryErrors,
                new MonitoredItemTransferOptions(),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(observedOwner, Is.SameAs(destinationSession.Object));
                Assert.That(observedLaterOwner, Is.SameAs(destinationSession.Object));
                Assert.That(item.IsResendData, Is.False);
                Assert.That(laterItem.IsResendData, Is.False);
                Assert.That(Publish(item), Is.Empty);
                Assert.That(Publish(laterItem), Is.Empty);
            });
        }

        [Test]
        public async Task TransferMonitoredItemsAsyncFlowsExplicitOptionsToEachOwnerAsync()
        {
            var destinationSession = new Mock<ISession>();
            destinationSession.SetupGet(session => session.Id).Returns(new NodeId(Guid.NewGuid()));
            destinationSession.SetupGet(session => session.EffectiveIdentity)
                .Returns(new Mock<IUserIdentity>().Object);
            destinationSession.SetupGet(session => session.PreferredLocales).Returns([]);
            OperationContext destinationContext = new(destinationSession.Object, DiagnosticsMasks.None);
            var sourceSession = new Mock<ISession>();
            sourceSession.SetupGet(session => session.EffectiveIdentity)
                .Returns(new Mock<IUserIdentity>().Object);
            sourceSession.SetupGet(session => session.PreferredLocales).Returns([]);

            var firstOwner = new Mock<IAsyncNodeManager>();
            var secondOwner = new Mock<IAsyncNodeManager>();
            OperationContext? firstContext = null;
            OperationContext? secondContext = null;
            MonitoredItemTransferOptions? firstOptions = null;
            MonitoredItemTransferOptions? secondOptions = null;

            firstOwner.Setup(nodeManager => nodeManager.TransferMonitoredItemsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<bool>(),
                    It.IsAny<IList<IMonitoredItem>>(),
                    It.IsAny<IList<bool>>(),
                    It.IsAny<IList<ServiceResult>>(),
                    It.IsAny<MonitoredItemTransferOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<OperationContext, bool, IList<IMonitoredItem>, IList<bool>, IList<ServiceResult>,
                    MonitoredItemTransferOptions, CancellationToken>(
                    (context, _, monitoredItems, processedItems, errors, transferOptions, _) =>
                    {
                        firstContext = context;
                        firstOptions = transferOptions;
                        for (int ii = 0; ii < monitoredItems.Count; ii++)
                        {
                            if (!processedItems[ii] &&
                                ReferenceEquals(monitoredItems[ii].NodeManager, firstOwner.Object))
                            {
                                processedItems[ii] = true;
                                errors[ii] = ServiceResult.Good;
                            }
                        }
                    })
                .Returns(default(ValueTask));

            secondOwner.Setup(nodeManager => nodeManager.TransferMonitoredItemsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<bool>(),
                    It.IsAny<IList<IMonitoredItem>>(),
                    It.IsAny<IList<bool>>(),
                    It.IsAny<IList<ServiceResult>>(),
                    It.IsAny<MonitoredItemTransferOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<OperationContext, bool, IList<IMonitoredItem>, IList<bool>, IList<ServiceResult>,
                    MonitoredItemTransferOptions, CancellationToken>(
                    (context, _, monitoredItems, processedItems, errors, transferOptions, _) =>
                    {
                        secondContext = context;
                        secondOptions = transferOptions;
                        for (int ii = 0; ii < monitoredItems.Count; ii++)
                        {
                            if (!processedItems[ii] &&
                                ReferenceEquals(monitoredItems[ii].NodeManager, secondOwner.Object))
                            {
                                processedItems[ii] = true;
                                errors[ii] = ServiceResult.Good;
                            }
                        }
                    })
                .Returns(default(ValueTask));

            using var sut = new MasterNodeManager(
                m_server.CurrentInstance,
                m_fixture.Config,
                null,
                firstOwner.Object,
                secondOwner.Object);
            using var firstItem = CreateMonitoredItem(firstOwner.Object, sourceSession.Object, 6, 7, 126);
            using var secondItem = CreateMonitoredItem(secondOwner.Object, sourceSession.Object, 8, 9, 168);
            var errors = new List<ServiceResult> { null!, null! };

            await sut.TransferMonitoredItemsAsync(
                destinationContext,
                true,
                [firstItem, secondItem],
                errors,
                new MonitoredItemTransferOptions(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(firstContext, Is.SameAs(destinationContext));
                Assert.That(secondContext, Is.SameAs(destinationContext));
                Assert.That(firstOptions, Is.Not.Null);
                Assert.That(secondOptions, Is.Not.Null);
                Assert.That(firstOptions!.DeferInitialValues, Is.True);
                Assert.That(secondOptions!.DeferInitialValues, Is.True);
                Assert.That(firstOptions, Is.SameAs(secondOptions));
                Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(errors[1].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(Publish(firstItem), Has.Count.EqualTo(1));
                Assert.That(Publish(secondItem), Has.Count.EqualTo(1));
            });
        }

        private MasterNodeManager CreateMasterNodeManager()
        {
            return new MasterNodeManager(
                m_server.CurrentInstance,
                m_fixture.Config,
                null,
                System.Array.Empty<INodeManager>());
        }

        private MonitoredItem CreateDetachedMonitoredItem(
            MonitoringMode monitoringMode = MonitoringMode.Reporting)
        {
            var monitoredItem = new MonitoredItem(
                m_server.CurrentInstance,
                new Mock<IAsyncNodeManager>().Object,
                new object(),
                subscriptionId: 1,
                id: 2,
                itemToMonitor: new ReadValueId
                {
                    NodeId = new NodeId("Detached", 2),
                    AttributeId = Attributes.Value
                },
                diagnosticsMasks: DiagnosticsMasks.None,
                timestampsToReturn: TimestampsToReturn.Both,
                monitoringMode,
                clientHandle: 3,
                originalFilter: null,
                filterToUse: null,
                range: null,
                samplingInterval: 1000,
                queueSize: 1,
                discardOldest: true,
                sourceSamplingInterval: 1000);
            ((IDetachableMonitoredItem)monitoredItem).Detach(m_server.CurrentInstance);
            return monitoredItem;
        }


        private MonitoredItem CreateMonitoredItem(
            IAsyncNodeManager nodeManager,
            ISession session,
            uint id,
            uint clientHandle,
            int value)
        {
            var monitoredItem = new MonitoredItem(
                m_server.CurrentInstance,
                nodeManager,
                new object(),
                subscriptionId: 1,
                id: id,
                itemToMonitor: new ReadValueId
                {
                    NodeId = new NodeId($"Transactional{id}", 2),
                    AttributeId = Attributes.Value
                },
                diagnosticsMasks: DiagnosticsMasks.None,
                timestampsToReturn: TimestampsToReturn.Both,
                monitoringMode: MonitoringMode.Reporting,
                clientHandle: clientHandle,
                originalFilter: null,
                filterToUse: null,
                range: null,
                samplingInterval: 1000,
                queueSize: 1,
                discardOldest: true,
                sourceSamplingInterval: 1000);
            var subscription = new Mock<ISubscription>();
            subscription.SetupGet(value => value.Session).Returns(session);
            monitoredItem.SubscriptionCallback = subscription.Object;
            monitoredItem.QueueValue(new DataValue(new Variant(value)), null);
            Assert.That(Publish(monitoredItem), Has.Count.EqualTo(1));
            return monitoredItem;
        }

        private Queue<MonitoredItemNotification> Publish(MonitoredItem item)
        {
            var notifications = new Queue<MonitoredItemNotification>();
            var diagnostics = new Queue<DiagnosticInfo>();
            item.Publish(
                new OperationContext(item),
                notifications,
                diagnostics,
                10,
                m_server.CurrentInstance.Telemetry.CreateLogger<MasterNodeManagerDeterministicTests>());
            return notifications;
        }

        private static OperationContext CreateContext()
        {
            var session = new Mock<ISession>();
            session.Setup(s => s.EffectiveIdentity).Returns(new Mock<IUserIdentity>().Object);
            session.Setup(s => s.PreferredLocales).Returns([]);
            session.Setup(s => s.ContinuationPoints).Returns(
                new SessionContinuationPoints(
                    () => NodeId.Null, maxBrowse: 10, maxHistory: 10, store: null));
            return new OperationContext(
                new RequestHeader(), null!, RequestType.Read, RequestLifetime.None, session.Object);
        }

        private static OperationContext CreateContextWithContinuationStore()
        {
            var session = new Mock<ISession>();
            session.Setup(s => s.EffectiveIdentity).Returns(new Mock<IUserIdentity>().Object);
            session.Setup(s => s.PreferredLocales).Returns([]);

            // The real holder, not a stand-in dictionary, so BrowseNext meets the same
            // lookup and eviction rules a live session applies.
            var continuationPoints = new SessionContinuationPoints(
                () => NodeId.Null, maxBrowse: 10, maxHistory: 10, store: null);
            session.Setup(s => s.ContinuationPoints).Returns(continuationPoints);

            return new OperationContext(
                new RequestHeader(), null!, RequestType.Read, RequestLifetime.None, session.Object);
        }

        private static void SetMaxBrowseContinuationPointsPerBrowse(MasterNodeManager sut, uint value)
        {
            var field = typeof(MasterNodeManager).GetField(
                "m_maxContinuationPointsPerBrowse",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            field!.SetValue(sut, value);
        }

        private static uint GetMaxBrowseContinuationPointsPerBrowse(MasterNodeManager sut)
        {
            var field = typeof(MasterNodeManager).GetField(
                "m_maxContinuationPointsPerBrowse",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (uint)field!.GetValue(sut)!;
        }

        private static string ToContinuationPointKey(ByteString continuationPoint)
        {
            return System.Convert.ToBase64String(continuationPoint.ToArray());
        }
    }
}
