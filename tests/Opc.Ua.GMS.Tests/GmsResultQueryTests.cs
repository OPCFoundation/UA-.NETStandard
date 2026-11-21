// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Tests;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Exercises the actual Machinery query and metadata services through GMS-derived instances.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    [NonParallelizable]
    public sealed class GmsResultQueryTests
    {
        /// <summary>
        /// Filters and ordering use inherited metadata fields, not a second GMS-specific query language.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MetadataFilteringOrderingAndAcknowledgmentsUseSharedServices(bool derivedOperand)
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync();
            IMachineHandle<GMSState> machine = await fixture.Stage("QueryGms").WithGmsResults(
                new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()),
                out GmsResultBinding binding).BuildAsync();
            GmsResultPublisher publisher = binding.CreatePublisher(machine);
            await publisher.PublishAsync(Result("R1", "A"));
            await publisher.PublishAsync(Result("R2", "B"));
            await publisher.PublishAsync(Result("R3", "A"));
            ISession session = await fixture.ConnectAsync();
            var client = new GmsResultClient(session, machine.NodeId, NUnitTelemetryContext.Create());
            ushort ns = (ushort)session.NamespaceUris.GetIndex(Machinery.Result.Namespaces.MachineryResult);
            NodeId type = derivedOperand
                ? NodeId.Create(VariableTypes.GMSResultType, GmsModel.NamespaceUri, session.NamespaceUris)
                : NodeId.Create(Machinery.Result.VariableTypes.ResultType,
                    Machinery.Result.Namespaces.MachineryResult, session.NamespaceUris);
            var filter = new ContentFilter();
            filter.Push(FilterOperator.Equals,
                Variant.FromStructure(new SimpleAttributeOperand(type,
                [
                    new QualifiedName("ResultMetaData", ns),
                    new QualifiedName("JobId", ns)
                ])),
                new Variant("A"));
            var order = new RelativePath
            {
                Elements =
                [
                    new RelativePathElement
                    {
                        ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName("ResultMetaData", ns)
                    },
                    new RelativePathElement
                    {
                        ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName("ResultId", ns)
                    }
                ]
            };

            Assert.That((await client.QueryAsync(3, filter, [order])).ToArray(), Is.EqualTo((string[])["R1", "R3"]));
            Assert.That((await client.QueryAsync(1, filter, [order])).ToArray(), Is.EqualTo((string[])["R1"]));
            ArrayOf<int> acknowledged = await client.AcknowledgeAsync(["R1", "absent"]);
            Assert.That(acknowledged[0], Is.Zero);
            Assert.That(acknowledged[1], Is.EqualTo(unchecked((int)StatusCodes.BadNotFound.Code)));
            GmsResultDomainException absent = (await Assert.ThrowsAsync<GmsResultDomainException>(
                async () => await client.GetByIdAsync("absent")))!;
            Assert.That(absent.Error, Is.EqualTo(unchecked((int)StatusCodes.BadNotFound.Code)));
            Assert.That((await client.GetLatestAsync()).ResultMetaData!.JobId, Is.EqualTo("A"));
        }

        /// <summary>
        /// Peer sessions cannot release someone else's pinned metadata; the owner can release it normally.
        /// </summary>
        [Test]
        public async Task MetadataOwnershipAndCapacityAreEnforcedBetweenSessions()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync();
            IMachineHandle<GMSState> machine = await fixture.Stage("OwnedMetadata").WithGmsResults(
                new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()),
                out GmsResultBinding binding).BuildAsync();
            await binding.CreatePublisher(machine).PublishAsync(Result("owned", "A"));
            ISession first = await fixture.ConnectAsync();
            ISession second = await fixture.ConnectAsync();
            var owner = new GMSResultManagementTypeClient(
                first, machine.State.ResultManagement!.NodeId, NUnitTelemetryContext.Create());
            var stranger = new GMSResultManagementTypeClient(
                second, machine.State.ResultManagement.NodeId, NUnitTelemetryContext.Create());
            (uint handle, _, _) = await owner.GetLatestResultAsync(0);
            ServiceResultException denied = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await stranger.ReleaseResultHandleAsync(handle)))!;
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            ServiceResultException full = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await stranger.GetLatestResultAsync(0)))!;
            Assert.That(full.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(await owner.ReleaseResultHandleAsync(handle), Is.Zero);
            var client = new GmsResultClient(second, machine.NodeId, NUnitTelemetryContext.Create());
            Assert.That((await client.GetLatestAsync()).ResultMetaData!.ResultId, Is.EqualTo("owned"));
        }

        /// <summary>
        /// Missing optional download support still releases the metadata handle before reporting the failure.
        /// </summary>
        [Test]
        public async Task MissingTransferDoesNotLeakMetadataOrPretendToDownload()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync();
            IMachineHandle<GMSState> machine = await fixture.Stage("NoFiles").WithGmsResults(
                new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()),
                out GmsResultBinding binding,
                new GmsResultBindingOptions { EnableFileTransfer = false }).BuildAsync();
            await binding.CreatePublisher(machine).PublishAsync(Result("inline", "A"));
            ISession session = await fixture.ConnectAsync();
            var client = new GmsResultClient(session, machine.NodeId, NUnitTelemetryContext.Create());
            ServiceResultException missing = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.DownloadAsync("inline")))!;
            Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That((await client.GetLatestAsync()).ResultMetaData!.ResultId, Is.EqualTo("inline"));
        }

        /// <summary>
        /// Creates a result carrying an explicitly encoded JobId field.
        /// </summary>
        private static GmsResult Result(string id, string job)
        {
            return new GmsResult(new DataValue(Variant.FromStructure(new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType
                {
                    ResultId = id,
                    JobId = job,
                    EncodingMask = (uint)ResultMetaDataTypeFields.JobId
                },
                ResultContent = [new Variant(10.25)]
            })));
        }
    }
}
