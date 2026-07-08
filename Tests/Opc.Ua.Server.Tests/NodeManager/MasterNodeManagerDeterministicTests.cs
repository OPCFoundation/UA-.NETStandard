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
                new NodeId(1000u),
                new NodeId("register-me", 0)
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
                    CancellationToken.None).ConfigureAwait(false),
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
                    CancellationToken.None).ConfigureAwait(false),
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
                CancellationToken.None).ConfigureAwait(false);

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
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
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
                CancellationToken.None).ConfigureAwait(false);

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
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadBrowseDirectionInvalid));
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
                    CancellationToken.None).ConfigureAwait(false),
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
                CancellationToken.None).ConfigureAwait(false);

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
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
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
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task TranslateBrowsePathsToNodeIdsAsync_EmptyBatch_ReturnsEmptyResultsAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<BrowsePathResult> results, _) = await sut.TranslateBrowsePathsToNodeIdsAsync(
                ctx,
                System.Array.Empty<BrowsePath>().ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

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
                        new RelativePathElement { TargetName = new QualifiedName("Any", 0) }
                    }.ToArrayOf()
                }
            };

            (ArrayOf<BrowsePathResult> results, _) = await sut.TranslateBrowsePathsToNodeIdsAsync(
                ctx,
                new BrowsePath[] { browsePath }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

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
                CancellationToken.None).ConfigureAwait(false);

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
                        new RelativePathElement()
                    }.ToArrayOf()
                }
            };

            (ArrayOf<BrowsePathResult> results, _) = await sut.TranslateBrowsePathsToNodeIdsAsync(
                ctx,
                new BrowsePath[] { browsePath }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

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
                    CancellationToken.None).ConfigureAwait(false),
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
                    CancellationToken.None).ConfigureAwait(false),
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
                CancellationToken.None).ConfigureAwait(false);

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
                new ReadValueId[] { new ReadValueId { AttributeId = Attributes.Value } }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

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
                CancellationToken.None).ConfigureAwait(false);

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
                CancellationToken.None).ConfigureAwait(false);

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
                    CancellationToken.None).ConfigureAwait(false),
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
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.Zero);
        }

        [Test]
        public async Task WriteAsync_NullNodeId_ReturnsBadNodeIdInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<StatusCode> results, _) = await sut.WriteAsync(
                ctx,
                new WriteValue[] { new WriteValue { AttributeId = Attributes.Value } }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

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
                CancellationToken.None).ConfigureAwait(false);

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
                    default(ExtensionObject),
                    TimestampsToReturn.Neither,
                    false,
                    System.Array.Empty<HistoryReadValueId>().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false),
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
                CancellationToken.None).ConfigureAwait(false);

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
                new HistoryReadValueId[] { new HistoryReadValueId() }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

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
                CancellationToken.None).ConfigureAwait(false);

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
                CancellationToken.None).ConfigureAwait(false);

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
                CancellationToken.None).ConfigureAwait(false);

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
                CancellationToken.None).ConfigureAwait(false);

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
                    CancellationToken.None).ConfigureAwait(false),
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
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(results.Count, Is.Zero);
        }

        [Test]
        public async Task CallAsync_NullObjectId_ReturnsBadNodeIdInvalidAsync()
        {
            using MasterNodeManager sut = CreateMasterNodeManager();
            OperationContext ctx = CreateContext();

            (ArrayOf<CallMethodResult> results, _) = await sut.CallAsync(
                ctx,
                new CallMethodRequest[] { new CallMethodRequest() }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

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
                CancellationToken.None).ConfigureAwait(false);

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
                CancellationToken.None).ConfigureAwait(false);

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
                    CancellationToken.None).ConfigureAwait(false),
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
                    CancellationToken.None).ConfigureAwait(false),
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
                    CancellationToken.None).ConfigureAwait(false),
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
                    CancellationToken.None).ConfigureAwait(false),
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
                    CancellationToken.None).ConfigureAwait(false),
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
                    CancellationToken.None).ConfigureAwait(false),
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
                    CancellationToken.None).ConfigureAwait(false),
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
                    CancellationToken.None).ConfigureAwait(false),
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
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
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
                    CancellationToken.None).ConfigureAwait(false),
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
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
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
                    CancellationToken.None).ConfigureAwait(false),
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
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
        }

        private MasterNodeManager CreateMasterNodeManager()
        {
            return new MasterNodeManager(
                m_server.CurrentInstance,
                m_fixture.Config,
                null,
                System.Array.Empty<INodeManager>());
        }

        private static OperationContext CreateContext()
        {
            var session = new Mock<ISession>();
            session.Setup(s => s.EffectiveIdentity).Returns(new Mock<IUserIdentity>().Object);
            session.Setup(s => s.PreferredLocales).Returns([]);
            return new OperationContext(
                new RequestHeader(), null!, RequestType.Read, RequestLifetime.None, session.Object);
        }
    }
}
