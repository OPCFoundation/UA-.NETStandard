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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.Di;
using Opc.Ua.Fx.Ac;
using Opc.Ua.Fx.Cm;
using Opc.Ua.Fx.Data;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Server.Tests
{
    /// <summary>
    /// Exercises actual MethodState validation and asynchronous reservation/consumption bindings.
    /// </summary>
    [TestFixture]
    public sealed class PubSubCommunicationIdMethodTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task RealReserveIdsCallHasStandardUInt16OutputsAndArity(bool standard)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState reserve = standard ? harness.Root.PubSubConfiguration!.ReserveIds! :
                harness.DynamicMethod("pubsub:configuration", BrowseNames.ReserveIds);
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                reserve, harness.Context, ReserveArguments()).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(reserve.InputArguments!.Value.Count, Is.EqualTo(3));
            Assert.That(reserve.OutputArguments!.Value.Count, Is.EqualTo(3));
            Assert.That(reserve.BrowseName.NamespaceIndex, Is.Zero);
            Assert.That(outputs, Has.Count.EqualTo(3));
            Assert.That(outputs[0].TryGetValue(out ulong publisher), Is.True);
            Assert.That(publisher, Is.EqualTo(7UL));
            Assert.That(outputs[1].TryGetValue(out ArrayOf<ushort> groups), Is.True);
            Assert.That(outputs[2].TryGetValue(out ArrayOf<ushort> writers), Is.True);
            Assert.That(groups, Is.EqualTo(new ushort[] { 0x8000 }));
            Assert.That(writers, Is.EqualTo(new ushort[] { 0x8000 }));
        }

        [Test]
        public async Task RealReserveIdsCallAwaitsDelayedPolicyAndSessionCloseCannotResurrect()
        {
            var delayed = new TaskCompletionSource<Variant>(TaskCreationOptions.RunContinuationsAsynchronously);
            var policy = new Mock<IPubSubDefaultPublisherIdPolicy>();
            policy.Setup(p => p.ResolveAsync(Profiles.PubSubUdpUadpTransport, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Variant>(delayed.Task));
            await using var harness = new Harness(policy.Object);
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.Root.PubSubConfiguration!.ReserveIds!;
            Task<(ServiceResult Result, List<Variant> Outputs)> pending =
                harness.CallAsync(method, harness.Context, ReserveArguments());
            Assert.That(pending.IsCompleted, Is.False);
            var session = new Mock<ISession>();
            session.SetupGet(s => s.Id).Returns(new NodeId(123u));
            harness.Sessions.Raise(s => s.SessionClosing += null!, session.Object, default(SessionEventReason));
            delayed.SetResult(Variant.From(7UL));
            (ServiceResult result, List<Variant> outputs) = await pending.ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(outputs, Is.Empty);
            harness.Context.SessionId = new NodeId(456u);
            (ServiceResult nextResult, List<Variant> next) =
                await harness.CallAsync(method, harness.Context, ReserveArguments()).ConfigureAwait(false);
            Assert.That(nextResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(next[1].TryGetValue(out ArrayOf<ushort> groups), Is.True);
            Assert.That(groups[0], Is.EqualTo((ushort)0x8000));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RealReserveIdsCallDeniesUnauthorizedAndSessionlessCallers(bool sessionless)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            if (sessionless)
            {
                harness.Context.SessionId = null;
            }
            else
            {
                harness.Context.UserIdentity = new Mock<IUserIdentity>().Object;
            }
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                harness.Root.PubSubConfiguration!.ReserveIds!, harness.Context, ReserveArguments())
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(outputs, Is.Empty);
        }

        [Test]
        public async Task RealReserveIdsCallRejectsUnsupportedScopeAndBadInputTypes()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.Root.PubSubConfiguration!.ReserveIds!;
            (ServiceResult unsupported, List<Variant> outputs) = await harness.CallAsync(method, harness.Context,
                [Variant.From("urn:unsupported"), Variant.From((ushort)1), Variant.From((ushort)1)])
                .ConfigureAwait(false);
            Assert.That(unsupported.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(outputs, Is.Empty);
            var errors = new List<ServiceResult>();
            var invalidOutputs = new List<Variant>();
            _ = await method.CallAsync(harness.Context, method.Parent!.NodeId,
                [Variant.From(Profiles.PubSubUdpUadpTransport), Variant.From("invalid"), Variant.From((ushort)1)],
                errors, invalidOutputs).ConfigureAwait(false);
            Assert.That(errors[1].StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(invalidOutputs, Is.Empty);
        }

        [Test]
        public async Task RealReserveIdsCancellationReturnsCancelledStatusWithoutLeakingIds()
        {
            var delayed = new TaskCompletionSource<Variant>(TaskCreationOptions.RunContinuationsAsynchronously);
            var policy = new Mock<IPubSubDefaultPublisherIdPolicy>();
            policy.Setup(p => p.ResolveAsync(Profiles.PubSubUdpUadpTransport, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Variant>(delayed.Task));
            await using var harness = new Harness(policy.Object);
            await harness.InitializeAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            MethodState reserve = harness.Root.PubSubConfiguration!.ReserveIds!;
            Task<(ServiceResult Result, List<Variant> Outputs)> pending =
                harness.CallAsync(reserve, harness.Context, ReserveArguments(), cancellation.Token);
            Assert.That(pending.IsCompleted, Is.False);
            cancellation.Cancel();
            delayed.SetResult(Variant.From(7UL));
            (ServiceResult result, List<Variant> outputs) = await pending.ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(outputs, Is.Empty);
            (ServiceResult nextResult, List<Variant> next) =
                await harness.CallAsync(reserve, harness.Context, ReserveArguments()).ConfigureAwait(false);
            Assert.That(nextResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(next[1].TryGetValue(out ArrayOf<ushort> groups), Is.True);
            Assert.That(groups[0], Is.EqualTo((ushort)0x8000));
        }

        [Test]
        public async Task RealCloseAndUpdateImportsOnlyExplicitlyReferencedBinaryElements()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            var incoming = new PubSubConfiguration2DataType
            {
                Enabled = true,
                Connections =
                [
                    CreateConnection(),
                    new PubSubConnectionDataType { Name = "ignored", TransportProfileUri = "urn:unsupported" }
                ]
            };
            uint handle = await UploadFileAsync(harness, EncodeFile(harness, incoming)).ConfigureAwait(false);
            MethodState update = harness.DynamicMethod("pubsub:configuration", BrowseNames.CloseAndUpdate);
            Assert.That(update.InputArguments!.Value.Count, Is.EqualTo(3));
            Assert.That(update.OutputArguments!.Value.Count, Is.EqualTo(4));
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(update, harness.Context,
                [Variant.From(handle), Variant.From(true), ConnectionAddReferences()]).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(outputs, Has.Count.EqualTo(4));
            Assert.That(outputs[0].TryGetValue(out bool applied), Is.True);
            Assert.That(applied, Is.True);
            Assert.That(outputs[1].TryGetValue(out ArrayOf<StatusCode> statuses), Is.True);
            Assert.That(statuses.Count, Is.EqualTo(1));
            Assert.That(statuses[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(outputs[2].TryGetValue(out ArrayOf<ExtensionObject> values), Is.True);
            Assert.That(values.IsEmpty, Is.True, "Explicit names and identifiers must not be reported as assigned.");
            Assert.That(outputs[3].TryGetValue(out ArrayOf<NodeId> objects), Is.True);
            Assert.That(objects.IsEmpty, Is.True);
            PubSubConfigurationDataType configuration = harness.Application.GetConfiguration();
            Assert.That(configuration.Connections.Count, Is.EqualTo(1));
            Assert.That(configuration.Connections[0].Name, Is.EqualTo("connection"));
            Assert.That(configuration.PublishedDataSets[0].Name, Is.EqualTo("pds"));
            Assert.That(configuration.Enabled, Is.False);
            (ServiceResult closed, _) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Close), harness.Context,
                [Variant.From(handle)]).ConfigureAwait(false);
            Assert.That(closed.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task BinaryConfigurationImportRemapsKnownNamespaceIndices()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            ushort first = harness.Namespaces.GetIndexOrAppend("urn:configuration:first");
            ushort second = harness.Namespaces.GetIndexOrAppend("urn:configuration:second");
            var fileNamespaces = new NamespaceTable();
            fileNamespaces.Append("urn:configuration:second");
            fileNamespaces.Append("urn:configuration:first");
            foreach (string uri in harness.Namespaces.ToArray().Skip(1))
            {
                _ = fileNamespaces.GetIndexOrAppend(uri);
            }
            PubSubConnectionDataType connection = CreateConnection();
            connection.ConnectionProperties =
            [
                new KeyValuePair
                {
                    Key = new QualifiedName("source", 1),
                    Value = Variant.From(new NodeId(42u, 2))
                }
            ];
            uint handle = await UploadFileAsync(harness, EncodeFile(harness,
                new PubSubConfiguration2DataType { Connections = [connection] }, fileNamespaces))
                .ConfigureAwait(false);
            (ServiceResult result, _) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.CloseAndUpdate), harness.Context,
                [Variant.From(handle), Variant.From(true), ConnectionAddReferences()]).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            KeyValuePair property = harness.Application.GetConfiguration().Connections[0].ConnectionProperties[0];
            Assert.That(property.Key.NamespaceIndex, Is.EqualTo(second));
            Assert.That(property.Value.TryGetValue(out NodeId node), Is.True);
            Assert.That(node, Is.EqualTo(new NodeId(42u, first)));
        }

        [TestCase("wrong-envelope", "BadTypeMismatch")]
        [TestCase("wrong-body", "BadTypeMismatch")]
        [TestCase("trailing-bytes", "BadTypeMismatch")]
        [TestCase("unknown-namespace", "BadNodeIdInvalid")]
        [TestCase("duplicate-namespace", "BadNodeIdInvalid")]
        public async Task BinaryConfigurationImportRejectsInvalidFilesWithoutMutation(
            string defect, string expectedName)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            var file = new UABinaryFileDataType
            {
                Body = Variant.From(new ExtensionObject(new PubSubConfiguration2DataType
                {
                    Connections = [CreateConnection()]
                }))
            };
            ExtensionObject envelope = new(file);
            switch (defect)
            {
                case "wrong-envelope":
                    envelope = new ExtensionObject(new PubSubConfiguration2DataType());
                    break;
                case "wrong-body":
                    file.Body = Variant.From("not a configuration");
                    break;
                case "unknown-namespace":
                    file.Namespaces = ["urn:configuration:unknown"];
                    break;
                case "duplicate-namespace":
                    string namespaceUri = harness.Namespaces.GetString(1)!;
                    file.Namespaces = [namespaceUri, namespaceUri];
                    break;
            }
            using var encoder = new BinaryEncoder(ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
            encoder.WriteExtensionObject(null, envelope);
            byte[] encoded = encoder.CloseAndReturnBuffer()!;
            if (defect == "trailing-bytes")
            {
                encoded = [.. encoded, 0];
            }
            uint handle = await UploadFileAsync(harness, encoded).ConfigureAwait(false);
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.CloseAndUpdate), harness.Context,
                [Variant.From(handle), Variant.From(true), ConnectionAddReferences()]).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(ExpectedStatus(expectedName)));
            Assert.That(outputs, Is.Empty);
            Assert.That(harness.Application.GetConfiguration().Connections.Count, Is.Zero);
        }

        private static byte[] EncodeFile(
            Harness harness, PubSubConfiguration2DataType configuration, NamespaceTable? namespaces = null)
        {
            namespaces ??= harness.Namespaces;
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.NamespaceUris = namespaces;
            var file = new UABinaryFileDataType
            {
                Namespaces = [.. namespaces.ToArray().Skip(1)],
                Body = Variant.From(new ExtensionObject(configuration))
            };
            using var encoder = new BinaryEncoder(context);
            encoder.WriteExtensionObject(null, new ExtensionObject(file));
            return encoder.CloseAndReturnBuffer()!;
        }

        private static Variant ConnectionAddReferences()
        {
            return Variant.From(new ArrayOf<ExtensionObject>(new ExtensionObject[]
            {
                new ExtensionObject(new PubSubConfigurationRefDataType
                {
                    ConfigurationMask = (uint)(PubSubConfigurationRefMask.ElementAdd |
                        PubSubConfigurationRefMask.ReferenceConnection)
                })
            }));
        }

        private static StatusCode ExpectedStatus(string name)
        {
            return name switch
            {
                "Good" => StatusCodes.Good,
                "BadTypeMismatch" => StatusCodes.BadTypeMismatch,
                "BadNodeIdInvalid" => StatusCodes.BadNodeIdInvalid,
                "BadNotWritable" => StatusCodes.BadNotWritable,
                "BadNotReadable" => StatusCodes.BadNotReadable,
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
        }

        private static async Task<uint> UploadFileAsync(Harness harness, byte[] bytes)
        {
            uint handle = await OpenFileAsync(harness, 6).ConfigureAwait(false);
            (ServiceResult result, _) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Write), harness.Context,
                [Variant.From(handle), Variant.From(ByteString.From(bytes.AsSpan()))]).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            return handle;
        }

        [Test]
        public async Task VendorSetConfigurationReplacesConfigurationThroughItsDeclaredContract()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState setter = harness.DynamicMethod("pubsub:configuration", "SetConfiguration");
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(setter, harness.Context,
                [Variant.From(new ExtensionObject(new PubSubConfigurationDataType
                {
                    Connections = [CreateConnection()]
                }))]).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(outputs, Has.Count.EqualTo(1));
            Assert.That(outputs[0].TryGetValue(out ArrayOf<StatusCode> statuses), Is.True);
            Assert.That(statuses.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(statuses[0]), Is.True);
            Assert.That(setter.InputArguments!.Value[0].DataType,
                Is.EqualTo(DataTypeIds.PubSubConfigurationDataType));
            Assert.That(harness.Application.GetConfiguration().Connections[0].Name, Is.EqualTo("connection"));
        }

        [Test]
        public async Task SubscriberMethodsUseStandardContractsForActualAddAndRemoveCalls()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            await harness.Application.AddConnectionAsync(CreateConnection()).ConfigureAwait(false);
            MethodState addGroup = harness.DynamicMethod("pubsub:connection:connection", BrowseNames.AddReaderGroup);
            (ServiceResult grouped, List<Variant> groups) = await harness.CallAsync(
                addGroup, harness.Context,
                [Variant.From(new ExtensionObject(new ReaderGroupDataType { Name = "readers" }))])
                .ConfigureAwait(false);
            Assert.That(grouped.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(groups[0].TryGetValue(out NodeId groupId), Is.True);
            MethodState addReader = harness.DynamicMethod(
                "pubsub:reader-group:connection:readers", BrowseNames.AddDataSetReader);
            (ServiceResult added, List<Variant> readers) = await harness.CallAsync(addReader, harness.Context,
                [Variant.From(new ExtensionObject(new DataSetReaderDataType
                {
                    Name = "reader",
                    PublisherId = Variant.From(7UL),
                    DataSetWriterId = 1,
                    MessageReceiveTimeout = 1000,
                    SubscribedDataSet = new ExtensionObject(new TargetVariablesDataType())
                }))]).ConfigureAwait(false);
            Assert.That(added.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(readers[0].TryGetValue(out NodeId readerId), Is.True);
            MethodState removeReader = harness.DynamicMethod(
                "pubsub:reader-group:connection:readers", BrowseNames.RemoveDataSetReader);
            (ServiceResult removed, List<Variant> removedOutputs) = await harness.CallAsync(
                removeReader, harness.Context, [Variant.From(readerId)]).ConfigureAwait(false);
            Assert.That(removed.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(removedOutputs, Is.Empty);
            MethodState removeGroup = harness.DynamicMethod("pubsub:connection:connection", BrowseNames.RemoveGroup);
            (ServiceResult ungrouped, _) = await harness.CallAsync(removeGroup, harness.Context,
                [Variant.From(groupId)]).ConfigureAwait(false);
            Assert.That(ungrouped.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Application.GetConfiguration().Connections[0].ReaderGroups.Count, Is.Zero);
            Assert.That(addGroup.BrowseName.NamespaceIndex, Is.Zero);
            Assert.That(addReader.BrowseName.NamespaceIndex, Is.Zero);
            Assert.That(removeReader.BrowseName.NamespaceIndex, Is.Zero);
            Assert.That(removeGroup.BrowseName.NamespaceIndex, Is.Zero);
        }

        [Test]
        public async Task RemovingAWriterThroughActualMethodMakesItsConsumedIdReservableAgain()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            PubSubConnectionDataType connection = CreateConnection();
            connection.WriterGroups =
            [
                new WriterGroupDataType
                {
                    Name = "group",
                    WriterGroupId = 0x8000,
                    PublishingInterval = 1000,
                    DataSetWriters =
                    [
                        new DataSetWriterDataType { Name = "writer", DataSetName = "pds", DataSetWriterId = 0x8000 }
                    ]
                }
            ];
            await harness.Application.AddConnectionAsync(connection).ConfigureAwait(false);
            MethodState remove = harness.DynamicMethod(
                "pubsub:writer-group:connection:group", BrowseNames.RemoveDataSetWriter);
            var writerId = new NodeId("pubsub:writer:connection:group:writer",
                harness.Manager.AddressSpaceNamespaceIndex);
            (ServiceResult removed, List<Variant> outputs) = await harness.CallAsync(
                remove, harness.Context, [Variant.From(writerId)]).ConfigureAwait(false);
            Assert.That(removed.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(outputs, Is.Empty);
            Assert.That(harness.Application.GetConfiguration().Connections[0].WriterGroups[0].DataSetWriters.Count,
                Is.Zero);
            (ServiceResult reserved, List<Variant> ids) = await harness.CallAsync(
                harness.Root.PubSubConfiguration!.ReserveIds!, harness.Context, ReserveArguments())
                .ConfigureAwait(false);
            Assert.That(reserved.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(ids[1].TryGetValue(out ArrayOf<ushort> groupIds), Is.True);
            Assert.That(ids[2].TryGetValue(out ArrayOf<ushort> writerIds), Is.True);
            Assert.That(groupIds[0], Is.EqualTo((ushort)0x8001));
            Assert.That(writerIds[0], Is.EqualTo((ushort)0x8000));
        }

        [Test]
        public async Task ConfigurationFileReadUsesBinaryExtensionObjectAndPreservesTheConfiguration()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            await harness.Application.AddConnectionAsync(CreateConnection()).ConfigureAwait(false);
            uint handle = await OpenFileAsync(harness, 1).ConfigureAwait(false);
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Read), harness.Context,
                [Variant.From(handle), Variant.From(65536)]).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(outputs[0].TryGetValue(out ByteString bytes), Is.True);
            using var decoder = new BinaryDecoder(
                bytes.ToArray(), ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
            ExtensionObject encoded = decoder.ReadExtensionObject(null);
            Assert.That(encoded.TryGetValue(out UABinaryFileDataType? file), Is.True);
            Assert.That(file, Is.Not.Null);
            Assert.That(file!.Body.TryGetValue(out ExtensionObject body), Is.True);
            Assert.That(body.TryGetValue(out PubSubConfiguration2DataType? configuration), Is.True);
            Assert.That(configuration!.Connections[0].Name, Is.EqualTo("connection"));
            Assert.That(decoder.Position, Is.EqualTo(bytes.Length));
        }

        [TestCase(BrowseNames.Read)]
        [TestCase(BrowseNames.Write)]
        [TestCase(BrowseNames.Close)]
        [TestCase(BrowseNames.GetPosition)]
        [TestCase(BrowseNames.SetPosition)]
        public async Task ConfigurationFileHandlesCannotBeUsedByAnotherSession(string operation)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            uint handle = await OpenFileAsync(harness, 3).ConfigureAwait(false);
            harness.Context.SessionId = new NodeId(456u);
            ArrayOf<Variant> arguments = operation switch
            {
                BrowseNames.Read => [Variant.From(handle), Variant.From(1)],
                BrowseNames.Write => [Variant.From(handle), Variant.From(ByteString.From(new byte[] { 1 }))],
                BrowseNames.SetPosition => [Variant.From(handle), Variant.From(0UL)],
                _ => [Variant.From(handle)]
            };
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", operation), harness.Context, arguments)
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(outputs, Is.Empty);
            harness.Context.SessionId = new NodeId(123u);
            (ServiceResult owner, _) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Close), harness.Context,
                [Variant.From(handle)]).ConfigureAwait(false);
            Assert.That(owner.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task ClosingSessionDiscardsItsConfigurationFilesAndPreventsReopening()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            uint handle = await OpenFileAsync(harness, 6).ConfigureAwait(false);
            var session = new Mock<ISession>();
            session.SetupGet(s => s.Id).Returns(new NodeId(123u));
            harness.Sessions.Raise(s => s.SessionClosing += null!, session.Object, default(SessionEventReason));
            (ServiceResult read, _) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Read), harness.Context,
                [Variant.From(handle), Variant.From(1)]).ConfigureAwait(false);
            Assert.That(read.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            (ServiceResult reopen, _) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Open), harness.Context,
                [Variant.From((byte)6)]).ConfigureAwait(false);
            Assert.That(reopen.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task ConfigurationFileBoundsRejectExcessWithoutDamagingRetainedBytes()
        {
            await using var harness = new Harness(options: new PubSubServerOptions
            {
                MaxConfigurationFileSize = 4096,
                MaxConfigurationFileHandles = 1
            });
            await harness.InitializeAsync().ConfigureAwait(false);
            uint handle = await OpenFileAsync(harness, 3).ConfigureAwait(false);
            (ServiceResult unavailable, _) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Open), harness.Context,
                [Variant.From((byte)6)]).ConfigureAwait(false);
            Assert.That(unavailable.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            MethodState write = harness.DynamicMethod("pubsub:configuration", BrowseNames.Write);
            byte[] retained = new byte[4096];
            retained[0] = 1;
            retained[^1] = 4;
            (ServiceResult exact, _) = await harness.CallAsync(write, harness.Context,
                [Variant.From(handle), Variant.From(ByteString.From(retained.AsSpan()))])
                .ConfigureAwait(false);
            Assert.That(exact.StatusCode, Is.EqualTo(StatusCodes.Good));
            (ServiceResult excess, _) = await harness.CallAsync(write, harness.Context,
                [Variant.From(handle), Variant.From(ByteString.From(new byte[] { 5 }))]).ConfigureAwait(false);
            Assert.That(excess.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            MethodState position = harness.DynamicMethod("pubsub:configuration", BrowseNames.GetPosition);
            (ServiceResult positioned, List<Variant> positions) =
                await harness.CallAsync(position, harness.Context, [Variant.From(handle)]).ConfigureAwait(false);
            Assert.That(positioned.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(positions[0].TryGetValue(out ulong offset), Is.True);
            Assert.That(offset, Is.EqualTo(4096UL));
            (ServiceResult reset, _) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.SetPosition), harness.Context,
                [Variant.From(handle), Variant.From(0UL)]).ConfigureAwait(false);
            Assert.That(reset.StatusCode, Is.EqualTo(StatusCodes.Good));
            (ServiceResult read, List<Variant> data) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Read), harness.Context,
                [Variant.From(handle), Variant.From(4097)]).ConfigureAwait(false);
            Assert.That(read.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(data[0].TryGetValue(out ByteString bytes), Is.True);
            Assert.That(bytes.ToArray(), Is.EqualTo(retained));
        }

        [TestCase((byte)0)]
        [TestCase((byte)2)]
        [TestCase((byte)4)]
        [TestCase((byte)7)]
        [TestCase((byte)8)]
        [TestCase((byte)10)]
        [TestCase((byte)14)]
        [TestCase((byte)16)]
        public async Task ConfigurationFileOpenRejectsInvalidModes(byte mode)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Open), harness.Context,
                [Variant.From(mode)]).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(outputs, Is.Empty);
        }

        [Test]
        public async Task ConfigurationFileOpenAwaitsAllocatorAndDoesNotResurrectAClosedSession()
        {
            var allocated = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allocator = new Mock<IPubSubIdAllocator>();
            allocator.Setup(a => a.AllocateFileHandleAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<uint>(allocated.Task));
            await using var harness = new Harness(allocator: allocator.Object);
            await harness.InitializeAsync().ConfigureAwait(false);
            Task<(ServiceResult Result, List<Variant> Outputs)> pending = harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Open), harness.Context,
                [Variant.From((byte)6)]);
            Assert.That(pending.IsCompleted, Is.False);
            var session = new Mock<ISession>();
            session.SetupGet(s => s.Id).Returns(new NodeId(123u));
            harness.Sessions.Raise(s => s.SessionClosing += null!, session.Object, default(SessionEventReason));
            allocated.SetResult(5u);
            (ServiceResult result, List<Variant> outputs) = await pending.ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(outputs, Is.Empty);
            harness.Context.SessionId = new NodeId(456u);
            uint handle = await OpenFileAsync(harness, 6).ConfigureAwait(false);
            Assert.That(handle, Is.EqualTo(5u));
        }

        [Test]
        public async Task ConfigurationFileOpenCannotPublishAHandleAfterManagerDisposal()
        {
            var allocated = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allocator = new Mock<IPubSubIdAllocator>();
            allocator.Setup(a => a.AllocateFileHandleAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<uint>(allocated.Task));
            await using var harness = new Harness(allocator: allocator.Object);
            await harness.InitializeAsync().ConfigureAwait(false);
            Task<(ServiceResult Result, List<Variant> Outputs)> pending = harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Open), harness.Context,
                [Variant.From((byte)6)]);
            Assert.That(pending.IsCompleted, Is.False);
            harness.Manager.Dispose();
            allocated.SetResult(5u);
            (ServiceResult result, List<Variant> outputs) = await pending.ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(outputs, Is.Empty);
        }

        [TestCase((byte)1, (byte)1, "Good")]
        [TestCase((byte)1, (byte)3, "BadNotWritable")]
        [TestCase((byte)1, (byte)6, "BadNotWritable")]
        [TestCase((byte)3, (byte)1, "BadNotReadable")]
        [TestCase((byte)3, (byte)3, "BadNotWritable")]
        [TestCase((byte)3, (byte)6, "BadNotWritable")]
        [TestCase((byte)6, (byte)1, "BadNotReadable")]
        [TestCase((byte)6, (byte)3, "BadNotWritable")]
        [TestCase((byte)6, (byte)6, "BadNotWritable")]
        public async Task ConfigurationFileWriteAccessIsExclusiveAcrossSessions(
            byte initialMode, byte nextMode, string expectedName)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            uint first = await OpenFileAsync(harness, initialMode).ConfigureAwait(false);
            harness.Context.SessionId = new NodeId(456u);
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Open), harness.Context,
                [Variant.From(nextMode)]).ConfigureAwait(false);
            StatusCode expectedStatus = ExpectedStatus(expectedName);
            Assert.That(result.StatusCode, Is.EqualTo(expectedStatus));
            Assert.That(outputs, Has.Count.EqualTo(StatusCode.IsGood(expectedStatus) ? 1 : 0));
            if (StatusCode.IsBad(expectedStatus))
            {
                harness.Context.SessionId = new NodeId(123u);
                (ServiceResult closed, _) = await harness.CallAsync(
                    harness.DynamicMethod("pubsub:configuration", BrowseNames.Close), harness.Context,
                    [Variant.From(first)]).ConfigureAwait(false);
                Assert.That(closed.StatusCode, Is.EqualTo(StatusCodes.Good));
                harness.Context.SessionId = new NodeId(456u);
                _ = await OpenFileAsync(harness, nextMode).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConfigurationFileReadHandlesAreBoundedAndReleasedOnClose()
        {
            await using var harness = new Harness(options: new PubSubServerOptions
            {
                MaxConfigurationFileHandles = 1
            });
            await harness.InitializeAsync().ConfigureAwait(false);
            uint first = await OpenFileAsync(harness, 1).ConfigureAwait(false);
            (ServiceResult limited, List<Variant> outputs) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Open), harness.Context,
                [Variant.From((byte)1)]).ConfigureAwait(false);
            Assert.That(limited.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            Assert.That(outputs, Is.Empty);
            (ServiceResult closed, _) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Close), harness.Context,
                [Variant.From(first)]).ConfigureAwait(false);
            Assert.That(closed.StatusCode, Is.EqualTo(StatusCodes.Good));
            _ = await OpenFileAsync(harness, 1).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConfigurationFileAccessRequiresAnAuthorizedActualSession(bool sessionless)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            uint handle = await OpenFileAsync(harness, 1).ConfigureAwait(false);
            if (sessionless)
            {
                harness.Context.SessionId = null;
            }
            else
            {
                harness.Context.UserIdentity = new Mock<IUserIdentity>().Object;
            }
            (ServiceResult opened, List<Variant> openOutputs) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Open), harness.Context,
                [Variant.From((byte)1)]).ConfigureAwait(false);
            Assert.That(opened.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(openOutputs, Is.Empty);
            (ServiceResult read, List<Variant> readOutputs) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Read), harness.Context,
                [Variant.From(handle), Variant.From(1)]).ConfigureAwait(false);
            Assert.That(read.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(readOutputs, Is.Empty);
        }

        [Test]
        public async Task EraseExistingConfigurationFileCannotBeReadAndCloseDiscardsItsBytes()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            uint handle = await UploadFileAsync(harness, [1, 2, 3]).ConfigureAwait(false);
            (ServiceResult read, List<Variant> outputs) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Read), harness.Context,
                [Variant.From(handle), Variant.From(3)]).ConfigureAwait(false);
            Assert.That(read.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(outputs, Is.Empty);
            (ServiceResult closed, _) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Close), harness.Context,
                [Variant.From(handle)]).ConfigureAwait(false);
            Assert.That(closed.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Application.GetConfiguration().Connections.Count, Is.Zero);
            uint next = await OpenFileAsync(harness, 1).ConfigureAwait(false);
            (ServiceResult exported, List<Variant> data) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Read), harness.Context,
                [Variant.From(next), Variant.From(65536)]).ConfigureAwait(false);
            Assert.That(exported.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(data[0].TryGetValue(out ByteString bytes), Is.True);
            using var decoder = new BinaryDecoder(
                bytes.ToArray(), ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
            Assert.That(decoder.ReadExtensionObject(null).TryGetValue(out UABinaryFileDataType? file), Is.True);
            Assert.That(file!.Body.TryGetValue(out ExtensionObject body), Is.True);
            Assert.That(body.TryGetValue(out PubSubConfiguration2DataType? configuration), Is.True);
            Assert.That(configuration!.PublishedDataSets[0].Name, Is.EqualTo("pds"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConfigurationFileNodesExposeGeneratedPropertiesAndActualOpenCount(bool standard)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            PubSubConfigurationState file = standard ? harness.Root.PubSubConfiguration! :
                harness.Manager.FindPredefinedNode<PubSubConfigurationState>(
                    new NodeId("pubsub:configuration", harness.Manager.AddressSpaceNamespaceIndex));
            Assert.That(file.TypeDefinitionId, Is.EqualTo(ObjectTypeIds.PubSubConfigurationType));
            Assert.That(file.MimeType!.Value, Is.EqualTo("application/opcua+uabinary"));
            Assert.That(ReadVariable(harness, file.OpenCount!).TryGetValue(out ushort before), Is.True);
            Assert.That(before, Is.Zero);
            Assert.That(ReadVariable(harness, file.Size!).TryGetValue(out ulong size), Is.True);
            Assert.That(size, Is.GreaterThan(0UL));
            _ = await OpenFileAsync(harness, 1).ConfigureAwait(false);
            Assert.That(ReadVariable(harness, file.OpenCount!).TryGetValue(out ushort after), Is.True);
            Assert.That(after, Is.EqualTo((ushort)1));
            Assert.That(ReadVariable(harness, file.UserWritable!).TryGetValue(out bool allowed), Is.True);
            Assert.That(allowed, Is.True);
            harness.Context.UserIdentity = new Mock<IUserIdentity>().Object;
            Assert.That(ReadVariable(harness, file.UserWritable!).TryGetValue(out bool denied), Is.True);
            Assert.That(denied, Is.False);
        }

        [Test]
        public async Task ReadWriteConfigurationFileStartsWithCurrentBinaryAndPositionClampsToEnd()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            uint handle = await OpenFileAsync(harness, 3).ConfigureAwait(false);
            (ServiceResult read, List<Variant> outputs) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Read), harness.Context,
                [Variant.From(handle), Variant.From(65536)]).ConfigureAwait(false);
            Assert.That(read.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(outputs[0].TryGetValue(out ByteString bytes), Is.True);
            Assert.That(bytes.Length, Is.GreaterThan(0));
            (ServiceResult positioned, _) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.SetPosition), harness.Context,
                [Variant.From(handle), Variant.From(ulong.MaxValue)]).ConfigureAwait(false);
            Assert.That(positioned.StatusCode, Is.EqualTo(StatusCodes.Good));
            (ServiceResult located, List<Variant> position) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.GetPosition), harness.Context,
                [Variant.From(handle)]).ConfigureAwait(false);
            Assert.That(located.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(position[0].TryGetValue(out ulong offset), Is.True);
            Assert.That(offset, Is.EqualTo((ulong)bytes.Length));
            (ServiceResult eof, List<Variant> empty) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Read), harness.Context,
                [Variant.From(handle), Variant.From(1)]).ConfigureAwait(false);
            Assert.That(eof.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(empty[0].TryGetValue(out ByteString remainder), Is.True);
            Assert.That(remainder.Length, Is.Zero);
            (ServiceResult invalid, List<Variant> invalidOutputs) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Read), harness.Context,
                [Variant.From(handle), Variant.From(-1)]).ConfigureAwait(false);
            Assert.That(invalid.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(invalidOutputs, Is.Empty);
        }

        private static Variant ReadVariable(Harness harness, BaseVariableState variable)
        {
            var value = new DataValue();
            ServiceResult result = variable.ReadAttribute(
                harness.Context, Attributes.Value, default, default, ref value);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            return value.WrappedValue;
        }

        [Test]
        public async Task RemovalMethodCannotDeleteAnotherParentsWriter()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            PubSubConnectionDataType connection = CreateConnection();
            connection.WriterGroups =
            [
                new WriterGroupDataType { Name = "first", WriterGroupId = 1, PublishingInterval = 1000 },
                new WriterGroupDataType
                {
                    Name = "second",
                    WriterGroupId = 2,
                    PublishingInterval = 1000,
                    DataSetWriters =
                    [
                        new DataSetWriterDataType { Name = "writer", DataSetName = "pds", DataSetWriterId = 1 }
                    ]
                }
            ];
            await harness.Application.AddConnectionAsync(connection).ConfigureAwait(false);
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:writer-group:connection:first", BrowseNames.RemoveDataSetWriter),
                harness.Context,
                [Variant.From(new NodeId("pubsub:writer:connection:second:writer",
                    harness.Manager.AddressSpaceNamespaceIndex))]).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(outputs, Is.Empty);
            Assert.That(harness.Application.GetConfiguration().Connections[0].WriterGroups[1].DataSetWriters.Count,
                Is.EqualTo(1));
        }

        private static async Task<uint> OpenFileAsync(Harness harness, byte mode)
        {
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                harness.DynamicMethod("pubsub:configuration", BrowseNames.Open), harness.Context,
                [Variant.From(mode)]).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(outputs[0].TryGetValue(out uint handle), Is.True);
            return handle;
        }

        private static PubSubConnectionDataType CreateConnection()
        {
            return new PubSubConnectionDataType
            {
                Name = "connection",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                PublisherId = Variant.From(7UL),
                Address = new ExtensionObject(new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
        }

        [Test]
        public async Task RealStandardAndDynamicConsumersUseTheSameSessionReservations()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            (ServiceResult reserved, _) = await harness.CallAsync(
                harness.Root.PubSubConfiguration!.ReserveIds!, harness.Context, ReserveArguments())
                .ConfigureAwait(false);
            Assert.That(reserved.StatusCode, Is.EqualTo(StatusCodes.Good));
            var connection = new PubSubConnectionDataType
            {
                Name = "connection",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                PublisherId = Variant.From(7UL),
                Address = new ExtensionObject(new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            (ServiceResult added, List<Variant> connectionOutputs) = await harness.CallAsync(
                harness.Root.AddConnection!, harness.Context, [Variant.From(new ExtensionObject(connection))])
                .ConfigureAwait(false);
            Assert.That(added.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(connectionOutputs, Has.Count.EqualTo(1));
            MethodState addGroup = harness.DynamicMethod("pubsub:connection:connection", BrowseNames.AddWriterGroup);
            var group = new WriterGroupDataType
            {
                Name = "group",
                WriterGroupId = 0x8000,
                PublishingInterval = 1000
            };
            harness.Context.SessionId = new NodeId(456u);
            (ServiceResult foreign, _) = await harness.CallAsync(
                addGroup, harness.Context, [Variant.From(new ExtensionObject(group))]).ConfigureAwait(false);
            Assert.That(foreign.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            harness.Context.SessionId = new NodeId(123u);
            (ServiceResult grouped, List<Variant> groupOutputs) = await harness.CallAsync(
                addGroup, harness.Context, [Variant.From(new ExtensionObject(group))]).ConfigureAwait(false);
            Assert.That(grouped.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(groupOutputs, Has.Count.EqualTo(1));
            MethodState addWriter = harness.DynamicMethod(
                "pubsub:writer-group:connection:group", BrowseNames.AddDataSetWriter);
            var writer = new DataSetWriterDataType { Name = "writer", DataSetWriterId = 0x8000, DataSetName = "pds" };
            (ServiceResult written, List<Variant> writerOutputs) = await harness.CallAsync(
                addWriter, harness.Context, [Variant.From(new ExtensionObject(writer))]).ConfigureAwait(false);
            Assert.That(written.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(writerOutputs, Has.Count.EqualTo(1));
            PubSubConfigurationDataType configuration = harness.Application.GetConfiguration();
            Assert.That(configuration.Connections[0].WriterGroups[0].WriterGroupId, Is.EqualTo((ushort)0x8000));
            Assert.That(configuration.Connections[0].WriterGroups[0].DataSetWriters[0].DataSetWriterId,
                Is.EqualTo((ushort)0x8000));
        }

        private static ArrayOf<Variant> ReserveArguments()
        {
            return
            [
                Variant.From(Profiles.PubSubUdpUadpTransport), Variant.From((ushort)1), Variant.From((ushort)1)
            ];
        }

        [Test]
        public async Task FxReservationMethodUsesActualSessionTransactionAndTypedOutputs()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxReserveArguments(FxRequest())).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(outputs, Has.Count.EqualTo(4));
            PubSubReserveCommunicationIdsResult2DataType reserved = FxResults(harness, outputs)[0];
            Assert.That(outputs[0].TryGetValue(out ArrayOf<AssetVerificationResultDataType> assets,
                harness.Context.AsMessageContext()), Is.True);
            Assert.That(assets.IsEmpty, Is.True);
            Assert.That(outputs[1].TryGetValue(out ArrayOf<ConnectionEndpointConfigurationResultDataType> endpoints,
                harness.Context.AsMessageContext()), Is.True);
            Assert.That(endpoints.IsEmpty, Is.True);
            Assert.That(outputs[3].TryGetValue(out ArrayOf<CommunicationConfigurationResultDataType> configurations,
                harness.Context.AsMessageContext()), Is.True);
            Assert.That(configurations.IsEmpty, Is.True);
            Assert.That(reserved.Result, Is.EqualTo(StatusCodes.Good));
            Assert.That(reserved.DefaultPublisherId.TryGetValue(out ulong publisher), Is.True);
            Assert.That(publisher, Is.EqualTo(7UL));
            Assert.That(reserved.WriterGroupIds, Is.EqualTo(new ushort[] { 0x8000 }));
            Assert.That(reserved.DataSetWriterIds, Is.EqualTo(new ushort[] { 0x8000 }));
            Assert.That(reserved.TransportSpecificInfo.IsNull, Is.True);
            harness.Context.SessionId = new NodeId(456u);
            var connection = CreateConnection();
            connection.WriterGroups =
                [new WriterGroupDataType { Name = "group", WriterGroupId = 0x8000, PublishingInterval = 1000 }];
            (ServiceResult denied, _) = await harness.CallAsync(
                harness.Root.AddConnection!, harness.Context, [Variant.FromStructure(connection)])
                .ConfigureAwait(false);
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            harness.Context.SessionId = new NodeId(123u);
            (ServiceResult consumed, _) = await harness.CallAsync(
                harness.Root.AddConnection!, harness.Context, [Variant.FromStructure(connection)])
                .ConfigureAwait(false);
            Assert.That(consumed.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Application.GetConfiguration().Connections[0].WriterGroups[0].WriterGroupId,
                Is.EqualTo((ushort)0x8000));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FxReservationMethodRequiresAnActualAuthorizedSession(bool sessionless)
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            if (sessionless)
            {
                harness.Context.SessionId = null;
            }
            else
            {
                harness.Context.UserIdentity = new Mock<IUserIdentity>().Object;
            }
            (ServiceResult result, _) = await harness.CallAsync(
                method, harness.Context, FxReserveArguments(FxRequest())).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [TestCase(0u)]
        [TestCase(512u)]
        [TestCase(256u)]
        [TestCase(320u)]
        public async Task FxReservationMethodNeverTreatsUnsupportedCommandsAsSuccess(uint mask)
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ArrayOf<Variant> args = FxReserveArguments(FxRequest());
            Variant[] changed = args.ToArray()!;
            changed[0] = Variant.From(mask);
            (ServiceResult result, _) = await harness.CallAsync(
                method, harness.Context, changed).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(mask is 0 or 1 or 512
                ? StatusCodes.BadInvalidArgument : StatusCodes.BadInvalidState));
            (ServiceResult next, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxReserveArguments(FxRequest())).ConfigureAwait(false);
            Assert.That(next.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(FxResults(harness, outputs)[0].WriterGroupIds[0], Is.EqualTo((ushort)0x8000));
        }

        [TestCase("opc.tcp://localhost:4840/uafx")]
        [TestCase("opc.wss://localhost:443/uafx?binding=1")]
        public async Task FxReservationMethodReturnsListeningUrlAsAStringVariant(string url)
        {
            var policy = new PubSubDefaultPublisherIdPolicy(
                [new PubSubDefaultPublisherId(DataChannelProfiles.PubSubUasc, Variant.From(19u))]);
            await using var harness = new Harness(policy, new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx(_ => new ValueTask<string?>(url));
            PubSubReserveCommunicationIds2DataType request = FxRequest();
            request.TransportProfileUri = DataChannelProfiles.PubSubUasc;
            request.RequestTransportSpecificInfo = true;
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxReserveArguments(request)).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            PubSubReserveCommunicationIdsResult2DataType reserved = FxResults(harness, outputs)[0];
            Assert.That(reserved.Result, Is.EqualTo(StatusCodes.Good));
            Assert.That(reserved.TransportSpecificInfo.TryGetValue(out string? address), Is.True);
            Assert.That(address, Is.EqualTo(url));
            Assert.That(reserved.DefaultPublisherId.TryGetValue(out uint publisher), Is.True);
            Assert.That(publisher, Is.EqualTo(19u));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("opc.udp://localhost:4840")]
        [TestCase("opc.quic://localhost:4840")]
        [TestCase("opc.tcp://localhost:0")]
        [TestCase("opc.tcp://user:password@localhost:4840")]
        [TestCase("opc.tcp://localhost:4840/#fragment")]
        public async Task FxReservationMethodRefusesUnavailableOrInvalidListeningInfoBeforeAllocating(string? url)
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx(_ => new ValueTask<string?>(url));
            PubSubReserveCommunicationIds2DataType request = FxRequest();
            request.TransportProfileUri = DataChannelProfiles.PubSubUasc;
            request.RequestTransportSpecificInfo = true;
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxReserveArguments(request, FxRequest())).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            PubSubReserveCommunicationIdsResult2DataType[] reserved = FxResults(harness, outputs);
            Assert.That(reserved[0].Result, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(reserved[0].WriterGroupIds.IsEmpty, Is.True);
            Assert.That(reserved[0].TransportSpecificInfo.IsNull, Is.True);
            Assert.That(reserved[1].Result, Is.EqualTo(StatusCodes.BadNothingToDo));
            (ServiceResult next, List<Variant> nextOutputs) = await harness.CallAsync(
                method, harness.Context, FxReserveArguments(FxRequest())).ConfigureAwait(false);
            Assert.That(next.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(FxResults(harness, nextOutputs)[0].WriterGroupIds[0], Is.EqualTo((ushort)0x8000));
        }

        [Test]
        public async Task FxReservationAbortRetainsEarlierIdsAndDoesNotRunLaterRequests()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            PubSubReserveCommunicationIds2DataType unsupported = FxRequest();
            unsupported.TransportProfileUri = "urn:unsupported";
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxReserveArguments(FxRequest(), unsupported, FxRequest()))
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            PubSubReserveCommunicationIdsResult2DataType[] reserved = FxResults(harness, outputs);
            Assert.That(reserved.Select(value => value.Result),
                Is.EqualTo(new StatusCode[]
                {
                    StatusCodes.Good, StatusCodes.BadNotSupported, StatusCodes.BadNothingToDo
                }));
            (ServiceResult next, List<Variant> nextOutputs) = await harness.CallAsync(
                method, harness.Context, FxReserveArguments(FxRequest())).ConfigureAwait(false);
            Assert.That(next.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(FxResults(harness, nextOutputs)[0].WriterGroupIds[0], Is.EqualTo((ushort)0x8001));
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Id).Returns(new NodeId(123u));
            harness.Sessions.Raise(value => value.SessionClosing += null!,
                session.Object, default(SessionEventReason));
            harness.Context.SessionId = new NodeId(456u);
            (ServiceResult released, List<Variant> releasedOutputs) = await harness.CallAsync(
                method, harness.Context, FxReserveArguments(FxRequest())).ConfigureAwait(false);
            Assert.That(released.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(FxResults(harness, releasedOutputs)[0].WriterGroupIds[0], Is.EqualTo((ushort)0x8000));
        }

        [TestCase(0)]
        [TestCase(2)]
        [TestCase(128)]
        [TestCase(129)]
        public async Task FxReservationBatchBoundaryHasNoPartialAdmission(int count)
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ReserveCommunicationIdsDataType[] requests = Enumerable.Range(0, count)
                .Select(_ => (ReserveCommunicationIdsDataType)FxRequest()).ToArray();
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxReserveArguments(requests)).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(count switch
            {
                0 => StatusCodes.BadInvalidArgument,
                129 => StatusCodes.BadTooManyOperations,
                _ => StatusCodes.Good
            }));
            if (count is > 0 and <= 128)
            {
                PubSubReserveCommunicationIdsResult2DataType[] reserved = FxResults(harness, outputs);
                Assert.That(reserved, Has.Length.EqualTo(count));
                Assert.That(reserved.Select(value => value.Result), Is.All.EqualTo(StatusCodes.Good));
                Assert.That(reserved.Select(value => value.WriterGroupIds[0]),
                    Is.EqualTo(Enumerable.Range(0x8000, count).Select(value => (ushort)value)));
            }
            else
            {
                (ServiceResult next, List<Variant> nextOutputs) = await harness.CallAsync(
                    method, harness.Context, FxReserveArguments(FxRequest())).ConfigureAwait(false);
                Assert.That(next.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(FxResults(harness, nextOutputs)[0].WriterGroupIds[0], Is.EqualTo((ushort)0x8000));
            }
        }

        [TestCase(4096)]
        [TestCase(4097)]
        public async Task FxReservationListeningUrlLimitCountsUtf8Bytes(int bytes)
        {
            const string prefix = "opc.tcp://localhost:4840/";
            string url = prefix + new string('\u00e9', (bytes - prefix.Length) / 2) +
                new string('a', (bytes - prefix.Length) % 2);
            Assert.That(System.Text.Encoding.UTF8.GetByteCount(url), Is.EqualTo(bytes));
            var policy = new PubSubDefaultPublisherIdPolicy(
                [new PubSubDefaultPublisherId(DataChannelProfiles.PubSubUasc, Variant.From(19u))]);
            await using var harness = new Harness(policy, new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx(_ => new ValueTask<string?>(url));
            PubSubReserveCommunicationIds2DataType request = FxRequest();
            request.TransportProfileUri = DataChannelProfiles.PubSubUasc;
            request.RequestTransportSpecificInfo = true;
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxReserveArguments(request)).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(bytes == 4096 ? StatusCodes.Good : StatusCodes.Uncertain));
            PubSubReserveCommunicationIdsResult2DataType reserved = FxResults(harness, outputs)[0];
            Assert.That(reserved.Result, Is.EqualTo(bytes == 4096 ? StatusCodes.Good : StatusCodes.BadNotSupported));
            if (bytes == 4096)
            {
                Assert.That(reserved.TransportSpecificInfo.TryGetValue(out string? actual), Is.True);
                Assert.That(actual, Is.EqualTo(url));
            }
            else
            {
                Assert.That(reserved.TransportSpecificInfo.IsNull, Is.True);
                Assert.That(reserved.WriterGroupIds.IsEmpty, Is.True);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FxReservationDelayedEndpointCannotResurrectClosedOrCancelledReservations(bool cancel)
        {
            var policy = new PubSubDefaultPublisherIdPolicy(
            [
                new PubSubDefaultPublisherId(DataChannelProfiles.PubSubUasc, Variant.From(19u)),
                new PubSubDefaultPublisherId(Profiles.PubSubUdpUadpTransport, Variant.From(7UL))
            ]);
            await using var harness = new Harness(policy, new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var endpoint = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation = new CancellationTokenSource();
            MethodState method = harness.BindFx(_ =>
            {
                entered.SetResult(true);
                return new ValueTask<string?>(endpoint.Task);
            });
            PubSubReserveCommunicationIds2DataType request = FxRequest();
            request.TransportProfileUri = DataChannelProfiles.PubSubUasc;
            request.RequestTransportSpecificInfo = true;
            Task<(ServiceResult Result, List<Variant> Outputs)> pending = harness.CallAsync(
                method, harness.Context, FxReserveArguments(FxRequest(), request, FxRequest()), cancellation.Token);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(pending.IsCompleted, Is.False);
                if (cancel)
                {
                    cancellation.Cancel();
                }
                else
                {
                    var session = new Mock<ISession>();
                    session.SetupGet(value => value.Id).Returns(new NodeId(123u));
                    harness.Sessions.Raise(value => value.SessionClosing += null!,
                        session.Object, default(SessionEventReason));
                }
            }
            finally
            {
                endpoint.TrySetResult("opc.tcp://localhost:4840/");
            }
            (ServiceResult result, List<Variant> outputs) = await pending.ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            PubSubReserveCommunicationIdsResult2DataType[] reserved = FxResults(harness, outputs);
            Assert.That(reserved[0].Result, Is.EqualTo(StatusCodes.Good));
            Assert.That(reserved[1].Result, Is.EqualTo(cancel
                ? StatusCodes.BadRequestCancelledByClient : StatusCodes.BadUserAccessDenied));
            Assert.That(reserved[1].WriterGroupIds.IsEmpty, Is.True);
            Assert.That(reserved[2].Result, Is.EqualTo(StatusCodes.BadNothingToDo));
            harness.Context.SessionId = new NodeId(cancel ? 123u : 456u);
            (ServiceResult next, List<Variant> nextOutputs) = await harness.CallAsync(
                method, harness.Context, FxReserveArguments(FxRequest())).ConfigureAwait(false);
            Assert.That(next.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(FxResults(harness, nextOutputs)[0].WriterGroupIds[0],
                Is.EqualTo(cancel ? (ushort)0x8001 : (ushort)0x8000));
        }

        [Test]
        public async Task FxReservationBindingPreservesExistingHandlerAndHostObjectIdentity()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            EstablishConnectionsMethodState method = harness.BindFx();
            var component = (AutomationComponentState)method.Parent!;
            var original = method.OnCallAsync;
            var binding = new PubSubFxReservationMethods(harness.Application, new PubSubServerOptions());
            Assert.That(() => binding.Bind(component), Throws.InvalidOperationException);
            Assert.That(method.OnCallAsync, Is.SameAs(original));
            var errors = new List<ServiceResult>();
            ServiceResult result = await method.CallAsync(harness.Context, new NodeId("foreign", 1),
                FxReserveArguments(FxRequest()), errors, new List<Variant>()).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            (ServiceResult next, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxReserveArguments(FxRequest())).ConfigureAwait(false);
            Assert.That(next.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(FxResults(harness, outputs)[0].WriterGroupIds[0], Is.EqualTo((ushort)0x8000));
        }

        [Test]
        public async Task FxReservationDisabledConfigurationDeniesWithoutAllocating()
        {
            var options = new PubSubServerOptions { ExposeFxModel = true, ExposeConfigurationMethods = false };
            await using var harness = new Harness(options: options);
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            (ServiceResult result, _) = await harness.CallAsync(
                method, harness.Context, FxReserveArguments(FxRequest())).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            options.ExposeConfigurationMethods = true;
            (ServiceResult next, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxReserveArguments(FxRequest())).ConfigureAwait(false);
            Assert.That(next.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(FxResults(harness, outputs)[0].WriterGroupIds[0], Is.EqualTo((ushort)0x8000));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public async Task FxReservationRejectsOtherCommandInputsBeforeMutation(int index)
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            Variant[] arguments = FxReserveArguments(FxRequest()).ToArray()!;
            arguments[index] = index switch
            {
                1 => Variant.FromStructure<AssetVerificationDataType>([new AssetVerificationDataType()]),
                2 => Variant.FromStructure<ConnectionEndpointConfigurationDataType>(
                    [new ConnectionEndpointConfigurationDataType()]),
                _ => Variant.FromStructure<CommunicationConfigurationDataType>(
                    [new CommunicationConfigurationDataType()])
            };
            (ServiceResult result, _) = await harness.CallAsync(
                method, harness.Context, arguments).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            (ServiceResult next, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxReserveArguments(FxRequest())).ConfigureAwait(false);
            Assert.That(next.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(FxResults(harness, outputs)[0].WriterGroupIds[0], Is.EqualTo((ushort)0x8000));
        }

        [TestCase(AssetVerificationResultEnum.Match, AssetVerificationResultEnum.Match, true)]
        [TestCase(AssetVerificationResultEnum.Compatible, AssetVerificationResultEnum.Compatible, true)]
        [TestCase(AssetVerificationResultEnum.Match, AssetVerificationResultEnum.Match, false)]
        public async Task FxVerifyAssetForwardsArgumentsAndHonorsCommandMask(
            AssetVerificationResultEnum expectedResult,
            AssetVerificationResultEnum actualResult,
            bool includeReservations)
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            NodeId assetId = new("asset",
                (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc));
            harness.AddVerifiableAsset(assetId, actualResult);
            var assetVerification = new AssetVerificationDataType
            {
                AssetToVerify = assetId,
                VerificationMode = AssetVerificationModeEnum.AssetIdentityAndCompatibility,
                ExpectedVerificationResult = expectedResult,
                ExpectedVerificationVariables = [],
                ExpectedAdditionalVerificationVariables = []
            };

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    includeReservations
                        ? (uint)FxCommandMask.VerifyAssetCmd | (uint)FxCommandMask.ReserveCommunicationIdsCmd
                        : (uint)FxCommandMask.VerifyAssetCmd,
                    [assetVerification],
                    includeReservations ? [FxRequest()] : Array.Empty<ReserveCommunicationIdsDataType>()))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.VerifyAssetCalls, Is.EqualTo(1));
            Assert.That(harness.LastVerifyAssetInputs.Count, Is.EqualTo(3));
            Assert.That(harness.LastVerifyAssetInputs[0], Is.EqualTo(
                Variant.From((int)AssetVerificationModeEnum.AssetIdentityAndCompatibility)));
            Assert.That(harness.LastVerifyAssetInputs[1].TryGetValue(
                out ArrayOf<KeyValuePair> expectedVariables, harness.Context.AsMessageContext()), Is.True);
            Assert.That(expectedVariables.IsEmpty, Is.True);
            Assert.That(harness.LastVerifyAssetInputs[2].TryGetValue(
                out ArrayOf<NodeIdValuePair> expectedAdditionalVariables, harness.Context.AsMessageContext()),
                Is.True);
            Assert.That(expectedAdditionalVariables.IsEmpty, Is.True);
            AssetVerificationResultDataType[] verificationResults = FxAssetResults(harness, outputs);
            Assert.That(verificationResults, Has.Length.EqualTo(1));
            Assert.That(verificationResults[0].VerificationStatus, Is.EqualTo(StatusCodes.Good));
            Assert.That(verificationResults[0].VerificationResult, Is.EqualTo(actualResult));
            if (includeReservations)
            {
                Assert.That(FxResults(harness, outputs)[0].WriterGroupIds, Is.EqualTo(new ushort[] { 0x8000 }));
            }
            else
            {
                Assert.That(FxResults(harness, outputs), Is.Empty);
                (ServiceResult next, List<Variant> nextOutputs) = await harness.CallAsync(
                    method, harness.Context, FxReserveArguments(FxRequest())).ConfigureAwait(false);
                Assert.That(next.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(FxResults(harness, nextOutputs)[0].WriterGroupIds, Is.EqualTo(new ushort[] { 0x8000 }));
            }
        }

        [TestCase(AssetVerificationResultEnum.Compatible, AssetVerificationResultEnum.NotSet, true)]
        [TestCase(AssetVerificationResultEnum.Compatible, AssetVerificationResultEnum.Mismatch, true)]
        [TestCase(AssetVerificationResultEnum.Match, AssetVerificationResultEnum.Compatible, true)]
        [TestCase(AssetVerificationResultEnum.Compatible, AssetVerificationResultEnum.Compatible, false)]
        [TestCase(AssetVerificationResultEnum.Compatible, AssetVerificationResultEnum.Match, false)]
        [TestCase(AssetVerificationResultEnum.NotSet, AssetVerificationResultEnum.Match, false)]
        public async Task FxVerifyAssetAbortsForNormativeResultCombinations(
            AssetVerificationResultEnum expectedResult,
            AssetVerificationResultEnum actualResult,
            bool shouldAbort)
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            NodeId assetId = new("asset", namespaceIndex);
            harness.AddVerifiableAsset(assetId, actualResult);
            var asset = new AssetVerificationDataType
            {
                AssetToVerify = assetId,
                ExpectedVerificationResult = expectedResult
            };

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyAssetCmd | (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                    [asset],
                    FxRequest())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(
                shouldAbort ? StatusCodes.Uncertain : StatusCodes.Good));
            AssetVerificationResultDataType[] verificationResults = FxAssetResults(harness, outputs);
            Assert.That(verificationResults, Has.Length.EqualTo(1));
            Assert.That(verificationResults[0].VerificationStatus, Is.EqualTo(StatusCodes.Good));
            Assert.That(verificationResults[0].VerificationResult, Is.EqualTo(actualResult));
            PubSubReserveCommunicationIdsResult2DataType reservation = FxResults(harness, outputs)[0];
            Assert.That(reservation.Result, Is.EqualTo(
                shouldAbort ? StatusCodes.BadNothingToDo : StatusCodes.Good));
        }

        [TestCase((AssetVerificationModeEnum)3, AssetVerificationResultEnum.Match)]
        [TestCase(AssetVerificationModeEnum.AssetCompatibility, (AssetVerificationResultEnum)4)]
        public async Task FxVerifyAssetRejectsUndefinedEnumArguments(
            AssetVerificationModeEnum verificationMode,
            AssetVerificationResultEnum expectedResult)
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            NodeId assetId = new("asset", namespaceIndex);
            _ = harness.AddVerifiableAsset(assetId, AssetVerificationResultEnum.Match);
            var asset = new AssetVerificationDataType
            {
                AssetToVerify = assetId,
                VerificationMode = verificationMode,
                ExpectedVerificationResult = expectedResult
            };

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyAssetCmd | (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                    [asset],
                    FxRequest())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(harness.VerifyAssetCalls, Is.Zero);
            Assert.That(outputs, Is.Empty);
        }

        [TestCase(AssetVerificationResultEnum.NotSet)]
        [TestCase(AssetVerificationResultEnum.Compatible)]
        [TestCase(AssetVerificationResultEnum.Mismatch)]
        public async Task FxVerifyAssetUnexpectedResultProcessesAllAssetsAndAbortsReservationCommand(
            AssetVerificationResultEnum unexpectedResult)
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            NodeId mismatchAssetId = new("mismatch", namespaceIndex);
            NodeId matchAssetId = new("match", namespaceIndex);
            harness.AddVerifiableAsset(mismatchAssetId, unexpectedResult);
            harness.AddVerifiableAsset(matchAssetId, AssetVerificationResultEnum.Match);
            AssetVerificationDataType[] assets =
            [
                new()
                {
                    AssetToVerify = mismatchAssetId,
                    VerificationMode = AssetVerificationModeEnum.AssetCompatibility,
                    ExpectedVerificationResult = AssetVerificationResultEnum.Match
                },
                new()
                {
                    AssetToVerify = matchAssetId,
                    VerificationMode = AssetVerificationModeEnum.AssetCompatibility,
                    ExpectedVerificationResult = AssetVerificationResultEnum.Match
                }
            ];

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyAssetCmd | (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                    assets, FxRequest())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(harness.VerifyAssetCalls, Is.EqualTo(2));
            AssetVerificationResultDataType[] verificationResults = FxAssetResults(harness, outputs);
            Assert.That(verificationResults.Select(value => value.VerificationResult),
                Is.EqualTo(new[]
                {
                    unexpectedResult,
                    AssetVerificationResultEnum.Match
                }));
            Assert.That(FxResults(harness, outputs)[0].Result, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task FxVerifyAssetUnknownTargetReturnsStatusAndAbortsReservationCommand()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            var assetVerification = new AssetVerificationDataType
            {
                AssetToVerify = new NodeId("missing",
                    (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc)),
                VerificationMode = AssetVerificationModeEnum.AssetCompatibility,
                ExpectedVerificationResult = AssetVerificationResultEnum.Match
            };

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyAssetCmd | (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                    [assetVerification], FxRequest())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(harness.VerifyAssetCalls, Is.Zero);
            AssetVerificationResultDataType verificationResult = FxAssetResults(harness, outputs)[0];
            Assert.That(verificationResult.VerificationStatus, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(verificationResult.VerificationResult, Is.EqualTo(AssetVerificationResultEnum.NotSet));
            Assert.That(FxResults(harness, outputs)[0].Result, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task FxVerifyAssetRejectsNodeThatIsNotAnAsset()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            NodeId nodeId = new("not-an-asset", namespaceIndex);
            var node = new BaseObjectState(null)
            {
                NodeId = nodeId,
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            harness.Master.Setup(master => master.FindNodeInAddressSpaceAsync(nodeId, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeState?>(node));
            var assetVerification = new AssetVerificationDataType
            {
                AssetToVerify = nodeId,
                VerificationMode = AssetVerificationModeEnum.AssetCompatibility,
                ExpectedVerificationResult = AssetVerificationResultEnum.Match
            };

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyAssetCmd | (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                    [assetVerification], FxRequest())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(harness.VerifyAssetCalls, Is.Zero);
            Assert.That(FxAssetResults(harness, outputs)[0].VerificationStatus,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(FxResults(harness, outputs)[0].Result, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task FxVerifyAssetMethodErrorIsReturnedAndAbortsReservationCommand()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            NodeId assetId = new("asset",
                (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc));
            harness.AddVerifiableAsset(assetId, AssetVerificationResultEnum.Match, StatusCodes.BadInternalError);
            var assetVerification = new AssetVerificationDataType
            {
                AssetToVerify = assetId,
                VerificationMode = AssetVerificationModeEnum.AssetCompatibility,
                ExpectedVerificationResult = AssetVerificationResultEnum.Match
            };

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyAssetCmd | (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                    [assetVerification], FxRequest())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            AssetVerificationResultDataType verificationResult = FxAssetResults(harness, outputs)[0];
            Assert.That(verificationResult.VerificationStatus, Is.EqualTo(StatusCodes.BadInternalError));
            Assert.That(verificationResult.VerificationResult, Is.EqualTo(AssetVerificationResultEnum.NotSet));
            Assert.That(FxResults(harness, outputs)[0].Result, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task FxVerifyAssetThrownMethodErrorPreservesResultsAndContinuesAssetArray()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            NodeId failedId = new("failed", namespaceIndex);
            NodeId succeedingId = new("succeeding", namespaceIndex);
            BaseObjectState failedAsset = harness.AddVerifiableAsset(
                failedId, AssetVerificationResultEnum.Match);
            _ = harness.AddVerifiableAsset(succeedingId, AssetVerificationResultEnum.Match);
            MethodState verifyAsset = failedAsset.FindChild(
                harness.Context, new QualifiedName("VerifyAsset", namespaceIndex)) as MethodState ??
                throw new InvalidOperationException("The VerifyAsset Method was not created.");
            verifyAsset.OnCallMethod2Async = (_, _, _, _, _, _) =>
                throw new ServiceResultException(StatusCodes.BadInternalError);
            var assets = new[]
            {
                new AssetVerificationDataType
                {
                    AssetToVerify = failedId,
                    ExpectedVerificationResult = AssetVerificationResultEnum.Match
                },
                new AssetVerificationDataType
                {
                    AssetToVerify = succeedingId,
                    ExpectedVerificationResult = AssetVerificationResultEnum.Match
                }
            };

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyAssetCmd | (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                    assets,
                    FxRequest())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(harness.VerifyAssetCalls, Is.EqualTo(1));
            AssetVerificationResultDataType[] assetResults = FxAssetResults(harness, outputs);
            Assert.That(assetResults, Has.Length.EqualTo(2));
            Assert.That(assetResults[0].VerificationStatus, Is.EqualTo(StatusCodes.BadInternalError));
            Assert.That(assetResults[0].VerificationResult, Is.EqualTo(AssetVerificationResultEnum.NotSet));
            Assert.That(assetResults[1].VerificationStatus, Is.EqualTo(StatusCodes.Good));
            Assert.That(assetResults[1].VerificationResult, Is.EqualTo(AssetVerificationResultEnum.Match));
            Assert.That(FxResults(harness, outputs)[0].Result, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task FxVerifyAssetArgumentValidationErrorIsReturnedAndAbortsReservationCommand()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            NodeId assetId = new("asset", namespaceIndex);
            BaseObjectState asset = harness.AddVerifiableAsset(assetId, AssetVerificationResultEnum.Match);
            MethodState verifyAsset = asset.FindChild(
                harness.Context, new QualifiedName("VerifyAsset", namespaceIndex)) as MethodState ??
                throw new InvalidOperationException("The asset VerifyAsset method was not created.");
            PropertyState<ArrayOf<Argument>> inputArgumentsProperty = verifyAsset.InputArguments ??
                throw new InvalidOperationException("The VerifyAsset input arguments were not created.");
            Argument?[] inputArguments = inputArgumentsProperty.Value.ToArray() ??
                throw new InvalidOperationException("The VerifyAsset input argument array was not created.");
            Argument verificationMode = inputArguments[0] ??
                throw new InvalidOperationException("The VerifyAsset mode argument was not created.");
            verificationMode.DataType = DataTypeIds.String;
            Assert.That(verifyAsset.InputArguments!.Value[0].DataType, Is.EqualTo(DataTypeIds.String));
            var assetVerification = new AssetVerificationDataType
            {
                AssetToVerify = assetId,
                VerificationMode = AssetVerificationModeEnum.AssetCompatibility,
                ExpectedVerificationResult = AssetVerificationResultEnum.Match
            };

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyAssetCmd | (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                    [assetVerification], FxRequest())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(FxAssetResults(harness, outputs)[0].VerificationStatus,
                Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(FxResults(harness, outputs)[0].Result, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task FxVerifyAssetInvalidOutputReturnsTypeMismatchAndAbortsReservationCommand()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            NodeId assetId = new("asset", namespaceIndex);
            BaseObjectState asset = harness.AddVerifiableAsset(assetId, AssetVerificationResultEnum.Match);
            MethodState verifyAsset = asset.FindChild(
                harness.Context, new QualifiedName("VerifyAsset", namespaceIndex)) as MethodState ??
                throw new InvalidOperationException("The asset VerifyAsset method was not created.");
            verifyAsset.OnCallMethod2Async = (_, _, _, _, output, _) =>
            {
                output[0] = Variant.From("invalid result");
                output[1] = Variant.From(ArrayOf<StatusCode>.Empty);
                output[2] = Variant.From(ArrayOf<StatusCode>.Empty);
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            };
            var assetVerification = new AssetVerificationDataType
            {
                AssetToVerify = assetId,
                VerificationMode = AssetVerificationModeEnum.AssetCompatibility,
                ExpectedVerificationResult = AssetVerificationResultEnum.Match
            };

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyAssetCmd | (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                    [assetVerification], FxRequest())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            AssetVerificationResultDataType verificationResult = FxAssetResults(harness, outputs)[0];
            Assert.That(verificationResult.VerificationStatus, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(verificationResult.VerificationResult, Is.EqualTo(AssetVerificationResultEnum.NotSet));
            Assert.That(FxResults(harness, outputs)[0].Result, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task FxVerifyFunctionalEntityForwardsExpectedVariablesBeforeReservation()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            NodeId functionalEntityId = new("functional-entity", namespaceIndex);
            MethodState verify = harness.AddVerifiableFunctionalEntity(
                functionalEntityId, FunctionalEntityVerificationResultEnum.Match);
            NodeIdValuePair expectedVariable = ExpectedVerificationVariable("expected", namespaceIndex);
            ConnectionEndpointConfigurationDataType endpoint =
                FxEndpoint(functionalEntityId, [expectedVariable]);

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyFunctionalEntityCmd |
                    (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                    ArrayOf<AssetVerificationDataType>.Empty,
                    [endpoint],
                    FxRequest())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.VerifyFunctionalEntityCalls, Is.EqualTo(1));
            Assert.That(harness.LastVerifyFunctionalEntityInputs.Count, Is.EqualTo(1));
            Assert.That(harness.LastVerifyFunctionalEntityInputs[0].TryGetValue(
                out ArrayOf<NodeIdValuePair> passedVariables, harness.Context.AsMessageContext()), Is.True);
            Assert.That(passedVariables, Is.EqualTo(new[] { expectedVariable }));
            Assert.That(verify.Parent!.NodeId, Is.EqualTo(functionalEntityId));
            ConnectionEndpointConfigurationResultDataType endpointResult = FxEndpointResults(harness, outputs)[0];
            Assert.That(endpointResult.ConnectionEndpointId, Is.EqualTo(endpoint.ConnectionEndpoint.Node));
            Assert.That(endpointResult.FunctionalEntityNodeResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(endpointResult.VerificationStatus, Is.EqualTo(StatusCodes.Good));
            Assert.That(endpointResult.VerificationResult,
                Is.EqualTo(FunctionalEntityVerificationResultEnum.Match));
            Assert.That(endpointResult.VerificationVariablesErrors.IsEmpty, Is.True);
            Assert.That(FxResults(harness, outputs)[0].Result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task FxVerifyFunctionalEntityEmptyVariablesReportsNoDataWithoutCallingMethod()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            NodeId functionalEntityId = new("functional-entity", namespaceIndex);
            _ = harness.AddVerifiableFunctionalEntity(
                functionalEntityId, FunctionalEntityVerificationResultEnum.Match);
            var endpoints = new[]
            {
                FxEndpoint(functionalEntityId, ArrayOf<NodeIdValuePair>.Empty),
                FxEndpoint(functionalEntityId, [ExpectedVerificationVariable("expected", namespaceIndex)])
            };

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyFunctionalEntityCmd,
                    ArrayOf<AssetVerificationDataType>.Empty,
                    endpoints)).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.VerifyFunctionalEntityCalls, Is.EqualTo(1));
            ConnectionEndpointConfigurationResultDataType[] endpointResults = FxEndpointResults(harness, outputs);
            Assert.That(endpointResults, Has.Length.EqualTo(2));
            Assert.That(endpointResults[0].FunctionalEntityNodeResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(endpointResults[0].VerificationStatus, Is.EqualTo(StatusCodes.GoodNoData));
            Assert.That(endpointResults[0].VerificationResult,
                Is.EqualTo(FunctionalEntityVerificationResultEnum.NotSet));
            Assert.That(endpointResults[0].VerificationVariablesErrors.IsEmpty, Is.True);
            Assert.That(endpointResults[1].VerificationResult,
                Is.EqualTo(FunctionalEntityVerificationResultEnum.Match));
        }

        [Test]
        public async Task FxVerifyFunctionalEntityMissingTargetReturnsMismatchAndAbortsReservation()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            ConnectionEndpointConfigurationDataType endpoint = FxEndpoint(
                new NodeId("missing", namespaceIndex),
                [ExpectedVerificationVariable("expected", namespaceIndex)]);

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyFunctionalEntityCmd |
                    (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                    ArrayOf<AssetVerificationDataType>.Empty,
                    [endpoint],
                    FxRequest())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            ConnectionEndpointConfigurationResultDataType endpointResult = FxEndpointResults(harness, outputs)[0];
            Assert.That(endpointResult.FunctionalEntityNodeResult, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(endpointResult.VerificationResult,
                Is.EqualTo(FunctionalEntityVerificationResultEnum.Mismatch));
            Assert.That(endpointResult.VerificationVariablesErrors.IsEmpty, Is.True);
            Assert.That(FxResults(harness, outputs)[0].Result, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task FxAssetFailureSkipsFunctionalEntityVerificationAndReportsNothingToDo()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            NodeId assetId = new("mismatch", namespaceIndex);
            _ = harness.AddVerifiableAsset(assetId, AssetVerificationResultEnum.Mismatch);
            NodeId functionalEntityId = new("functional-entity", namespaceIndex);
            _ = harness.AddVerifiableFunctionalEntity(
                functionalEntityId, FunctionalEntityVerificationResultEnum.Match);
            var asset = new AssetVerificationDataType
            {
                AssetToVerify = assetId,
                ExpectedVerificationResult = AssetVerificationResultEnum.Match
            };
            ConnectionEndpointConfigurationDataType endpoint = FxEndpoint(
                functionalEntityId, [ExpectedVerificationVariable("expected", namespaceIndex)]);

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyAssetCmd |
                    (uint)FxCommandMask.VerifyFunctionalEntityCmd |
                    (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                    [asset],
                    [endpoint],
                    FxRequest())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(harness.VerifyFunctionalEntityCalls, Is.Zero);
            ConnectionEndpointConfigurationResultDataType endpointResult = FxEndpointResults(harness, outputs)[0];
            Assert.That(endpointResult.FunctionalEntityNodeResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(endpointResult.VerificationStatus, Is.EqualTo(StatusCodes.BadNothingToDo));
            Assert.That(endpointResult.VerificationResult,
                Is.EqualTo(FunctionalEntityVerificationResultEnum.NotSet));
            Assert.That(FxResults(harness, outputs)[0].Result, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FxVerifyFunctionalEntityRejectsWrongTypeOrForeignTarget(bool foreign)
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            NodeId functionalEntityId = new("functional-entity", namespaceIndex);
            ConnectionEndpointConfigurationDataType endpoint = FxEndpoint(
                functionalEntityId, [ExpectedVerificationVariable("expected", namespaceIndex)]);
            if (foreign)
            {
                _ = harness.AddVerifiableFunctionalEntity(
                    functionalEntityId, FunctionalEntityVerificationResultEnum.Match, related: false);
            }
            else
            {
                var wrongType = new BaseObjectState(null)
                {
                    NodeId = functionalEntityId,
                    TypeDefinitionId = ObjectTypeIds.BaseObjectType
                };
                harness.Master.Setup(master => master.FindNodeInAddressSpaceAsync(
                    functionalEntityId, It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<NodeState?>(wrongType));
            }

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyFunctionalEntityCmd |
                    (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                    ArrayOf<AssetVerificationDataType>.Empty,
                    [endpoint],
                    FxRequest())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            ConnectionEndpointConfigurationResultDataType endpointResult = FxEndpointResults(harness, outputs)[0];
            Assert.That(endpointResult.FunctionalEntityNodeResult, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(endpointResult.VerificationResult,
                Is.EqualTo(FunctionalEntityVerificationResultEnum.Mismatch));
            Assert.That(harness.VerifyFunctionalEntityCalls, Is.Zero);
            Assert.That(FxResults(harness, outputs)[0].Result, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task FxVerifyFunctionalEntityProcessesAllEndpointsAndAbortsLaterReservationsOnMismatch()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            NodeId firstId = new("first", namespaceIndex);
            NodeId secondId = new("second", namespaceIndex);
            _ = harness.AddVerifiableFunctionalEntity(firstId, FunctionalEntityVerificationResultEnum.Mismatch);
            _ = harness.AddVerifiableFunctionalEntity(secondId, FunctionalEntityVerificationResultEnum.Match);
            var endpoints = new[]
            {
                FxEndpoint(firstId, [ExpectedVerificationVariable("first-variable", namespaceIndex)]),
                FxEndpoint(secondId, [ExpectedVerificationVariable("second-variable", namespaceIndex)])
            };

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyFunctionalEntityCmd |
                    (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                    ArrayOf<AssetVerificationDataType>.Empty,
                    endpoints,
                    FxRequest())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(harness.VerifyFunctionalEntityCalls, Is.EqualTo(2));
            Assert.That(FxEndpointResults(harness, outputs).Select(value => value.VerificationResult),
                Is.EqualTo(new[]
                {
                    FunctionalEntityVerificationResultEnum.Mismatch,
                    FunctionalEntityVerificationResultEnum.Match
                }));
            Assert.That(FxResults(harness, outputs)[0].Result, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task FxVerifyFunctionalEntityMethodErrorIsReportedAndAbortsReservations()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            NodeId functionalEntityId = new("functional-entity", namespaceIndex);
            _ = harness.AddVerifiableFunctionalEntity(
                functionalEntityId,
                FunctionalEntityVerificationResultEnum.Match,
                verificationStatus: StatusCodes.BadInternalError);
            ConnectionEndpointConfigurationDataType endpoint = FxEndpoint(
                functionalEntityId, [ExpectedVerificationVariable("expected", namespaceIndex)]);

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyFunctionalEntityCmd |
                    (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                    ArrayOf<AssetVerificationDataType>.Empty,
                    [endpoint],
                    FxRequest())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            ConnectionEndpointConfigurationResultDataType endpointResult = FxEndpointResults(harness, outputs)[0];
            Assert.That(endpointResult.FunctionalEntityNodeResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(endpointResult.VerificationStatus, Is.EqualTo(StatusCodes.BadInternalError));
            Assert.That(endpointResult.VerificationResult,
                Is.EqualTo(FunctionalEntityVerificationResultEnum.NotSet));
            Assert.That(FxResults(harness, outputs)[0].Result, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task FxVerifyFunctionalEntityThrownMethodErrorPreservesResultsAndContinuesEndpointArray()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            NodeId failedId = new("failed", namespaceIndex);
            NodeId succeedingId = new("succeeding", namespaceIndex);
            MethodState failedVerify = harness.AddVerifiableFunctionalEntity(
                failedId, FunctionalEntityVerificationResultEnum.Match);
            _ = harness.AddVerifiableFunctionalEntity(
                succeedingId, FunctionalEntityVerificationResultEnum.Match);
            failedVerify.OnCallMethod2Async = (_, _, _, _, _, _) =>
                throw new ServiceResultException(StatusCodes.BadInternalError);
            var endpoints = new[]
            {
                FxEndpoint(failedId, [ExpectedVerificationVariable("failed-variable", namespaceIndex)]),
                FxEndpoint(succeedingId, [ExpectedVerificationVariable("succeeding-variable", namespaceIndex)])
            };

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyFunctionalEntityCmd |
                    (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                    ArrayOf<AssetVerificationDataType>.Empty,
                    endpoints,
                    FxRequest())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(harness.VerifyFunctionalEntityCalls, Is.EqualTo(1));
            ConnectionEndpointConfigurationResultDataType[] endpointResults = FxEndpointResults(harness, outputs);
            Assert.That(endpointResults[0].FunctionalEntityNodeResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(endpointResults[0].VerificationStatus, Is.EqualTo(StatusCodes.BadInternalError));
            Assert.That(endpointResults[0].VerificationResult,
                Is.EqualTo(FunctionalEntityVerificationResultEnum.NotSet));
            Assert.That(endpointResults[1].VerificationStatus, Is.EqualTo(StatusCodes.Good));
            Assert.That(endpointResults[1].VerificationResult,
                Is.EqualTo(FunctionalEntityVerificationResultEnum.Match));
            Assert.That(FxResults(harness, outputs)[0].Result, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task FxVerifyFunctionalEntityNotSetWithErrorsAbortsReservations()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            NodeId functionalEntityId = new("functional-entity", namespaceIndex);
            _ = harness.AddVerifiableFunctionalEntity(
                functionalEntityId,
                FunctionalEntityVerificationResultEnum.NotSet,
                verificationErrors: [StatusCodes.BadTypeMismatch]);
            ConnectionEndpointConfigurationDataType endpoint = FxEndpoint(
                functionalEntityId, [ExpectedVerificationVariable("expected", namespaceIndex)]);

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyFunctionalEntityCmd |
                    (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                    ArrayOf<AssetVerificationDataType>.Empty,
                    [endpoint],
                    FxRequest())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            ConnectionEndpointConfigurationResultDataType endpointResult = FxEndpointResults(harness, outputs)[0];
            Assert.That(endpointResult.VerificationResult,
                Is.EqualTo(FunctionalEntityVerificationResultEnum.NotSet));
            Assert.That(endpointResult.VerificationVariablesErrors, Is.EqualTo(new[] { StatusCodes.BadTypeMismatch }));
            Assert.That(FxResults(harness, outputs)[0].Result, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task FxVerifyFunctionalEntityArgumentValidationErrorIsReported()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            NodeId functionalEntityId = new("functional-entity", namespaceIndex);
            MethodState verify = harness.AddVerifiableFunctionalEntity(
                functionalEntityId, FunctionalEntityVerificationResultEnum.Match);
            Argument?[] inputArguments = verify.InputArguments!.Value.ToArray() ??
                throw new InvalidOperationException("The Verify input argument array was not created.");
            Argument expectedVariables = inputArguments[0] ??
                throw new InvalidOperationException("The Verify input argument was not created.");
            expectedVariables.DataType = DataTypeIds.String;
            ConnectionEndpointConfigurationDataType endpoint = FxEndpoint(
                functionalEntityId, [ExpectedVerificationVariable("expected", namespaceIndex)]);

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyFunctionalEntityCmd |
                    (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                    ArrayOf<AssetVerificationDataType>.Empty,
                    [endpoint],
                    FxRequest())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(harness.VerifyFunctionalEntityCalls, Is.Zero);
            ConnectionEndpointConfigurationResultDataType endpointResult = FxEndpointResults(harness, outputs)[0];
            Assert.That(endpointResult.FunctionalEntityNodeResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(endpointResult.VerificationStatus, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(FxResults(harness, outputs)[0].Result, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task FxVerifyFunctionalEntityInvalidOutputReturnsTypeMismatch()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            NodeId functionalEntityId = new("functional-entity", namespaceIndex);
            MethodState verify = harness.AddVerifiableFunctionalEntity(
                functionalEntityId, FunctionalEntityVerificationResultEnum.Match);
            verify.OnCallMethod2Async = (_, _, _, _, outputs, _) =>
            {
                outputs[0] = Variant.From("invalid result");
                outputs[1] = Variant.From(ArrayOf<StatusCode>.Empty);
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            };
            ConnectionEndpointConfigurationDataType endpoint = FxEndpoint(
                functionalEntityId, [ExpectedVerificationVariable("expected", namespaceIndex)]);

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyFunctionalEntityCmd |
                    (uint)FxCommandMask.ReserveCommunicationIdsCmd,
                    ArrayOf<AssetVerificationDataType>.Empty,
                    [endpoint],
                    FxRequest())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            ConnectionEndpointConfigurationResultDataType endpointResult = FxEndpointResults(harness, outputs)[0];
            Assert.That(endpointResult.VerificationStatus, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(endpointResult.VerificationResult,
                Is.EqualTo(FunctionalEntityVerificationResultEnum.NotSet));
            Assert.That(FxResults(harness, outputs)[0].Result, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task FxVerifyFunctionalEntityRequiresAtLeastOneExpectedVariable()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            var endpoint = FxEndpoint(
                new NodeId("functional-entity", namespaceIndex), ArrayOf<NodeIdValuePair>.Empty);

            (ServiceResult result, _) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyFunctionalEntityCmd,
                    ArrayOf<AssetVerificationDataType>.Empty,
                    [endpoint])).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(harness.VerifyFunctionalEntityCalls, Is.Zero);
        }

        [TestCase("connection-endpoint")]
        [TestCase("control-groups")]
        [TestCase("configuration-data")]
        [TestCase("communication-links")]
        public async Task FxVerifyFunctionalEntityRejectsInputsForUnsetCommands(string invalidInput)
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            ushort namespaceIndex = (ushort)harness.Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
            ConnectionEndpointConfigurationDataType endpoint = FxEndpoint(
                new NodeId("functional-entity", namespaceIndex),
                [ExpectedVerificationVariable("expected", namespaceIndex)]);
            switch (invalidInput)
            {
                case "connection-endpoint":
                    endpoint.ConnectionEndpoint = new ConnectionEndpointDefinitionDataType();
                    break;
                case "control-groups":
                    endpoint.ControlGroups = [new NodeId(42u)];
                    break;
                case "configuration-data":
                    endpoint.ConfigurationData = [ExpectedVerificationVariable("key", namespaceIndex)];
                    break;
                case "communication-links":
                    endpoint.CommunicationLinks = new PubSubCommunicationLinkConfigurationDataType();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(invalidInput));
            }

            (ServiceResult result, _) = await harness.CallAsync(
                method, harness.Context, FxArguments(
                    (uint)FxCommandMask.VerifyFunctionalEntityCmd,
                    ArrayOf<AssetVerificationDataType>.Empty,
                    [endpoint])).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(harness.VerifyFunctionalEntityCalls, Is.Zero);
        }

        [Test]
        public async Task FxReservationRejectsOlderRequestStructureRatherThanDroppingTransportInfo()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            var older = new PubSubReserveCommunicationIdsDataType
            {
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                NumReqWriterGroupIds = 1
            };
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxReserveArguments(older, FxRequest())).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            PubSubReserveCommunicationIdsResult2DataType[] reserved = FxResults(harness, outputs);
            Assert.That(reserved[0].Result, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(reserved[1].Result, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FxReservationDoesNotInventTransportInfoWithoutAListener(bool uasc)
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            MethodState method = harness.BindFx();
            PubSubReserveCommunicationIds2DataType request = FxRequest();
            request.RequestTransportSpecificInfo = true;
            if (uasc)
            {
                request.TransportProfileUri = DataChannelProfiles.PubSubUasc;
            }
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, FxReserveArguments(request)).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            PubSubReserveCommunicationIdsResult2DataType reserved = FxResults(harness, outputs)[0];
            Assert.That(reserved.Result, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(reserved.TransportSpecificInfo.IsNull, Is.True);
            Assert.That(reserved.WriterGroupIds.IsEmpty, Is.True);
        }

        [Test]
        public async Task FxReservationValidatesApplicationOptionsAndHostMethodBeforeBinding()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            Assert.That(() => new PubSubFxReservationMethods(null!, new PubSubServerOptions()),
                Throws.ArgumentNullException);
            Assert.That(() => new PubSubFxReservationMethods(new Mock<IPubSubApplication>().Object,
                new PubSubServerOptions()), Throws.ArgumentException);
            Assert.That(() => new PubSubFxReservationMethods(harness.Application, null!),
                Throws.ArgumentNullException);
            var binding = new PubSubFxReservationMethods(harness.Application, new PubSubServerOptions());
            Assert.That(() => binding.Bind(null!), Throws.ArgumentNullException);
            Assert.That(() => binding.Bind(new AutomationComponentState(null)), Throws.ArgumentException);
            var component = new AutomationComponentState(null) { NodeId = new NodeId("component", 1) };
            Assert.That(() => binding.Bind(component), Throws.ArgumentException);
            component.EstablishConnections = new EstablishConnectionsMethodState(component);
            Assert.That(() => binding.Bind(component), Throws.ArgumentException);
            EstablishConnectionsMethodState initialized = harness.BindFx();
            component.EstablishConnections = initialized;
            Assert.That(() => binding.Bind(component), Throws.ArgumentException);
            initialized.OnCallAsync = null;
            initialized.OnCallMethod2Async = (_, _, _, _, _, _) => new ValueTask<ServiceResult>(ServiceResult.Good);
            Assert.That(() => binding.Bind((AutomationComponentState)initialized.Parent!),
                Throws.InvalidOperationException);
            Assert.That(initialized.OnCallMethod2Async, Is.Not.Null);
            Assert.That(initialized.OnCallAsync, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FxCloseConnectionsWithoutLifecycleRemainsUnbound(bool remove)
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            AutomationComponentState component = harness.CreateFxComponentWithCloseConnections();
            CloseConnectionsMethodState method = component.CloseConnections!;

            new PubSubFxReservationMethods(harness.Application,
                new PubSubServerOptions { ExposeFxModel = true }).Bind(component);

            Assert.That(component.NodeId, Is.EqualTo(new NodeId("host:component", 1)));
            Assert.That(component.EstablishConnections!.OnCallAsync, Is.Not.Null,
                "The existing reservation binding must still be installed.");
            Assert.That(method.OnCall, Is.Null);
            Assert.That(method.OnCallAsync, Is.Null);
            Assert.That(method.OnCallMethod, Is.Null);
            Assert.That(method.OnCallMethod2, Is.Null);
            Assert.That(method.OnCallMethodWithResultAsync, Is.Null);
            Assert.That(method.OnCallMethod2Async, Is.Null);
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context,
                [Variant.From(new ArrayOf<NodeId>(new[] { new NodeId("endpoint", 1) })), Variant.From(remove)])
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotImplemented));
            Assert.That(outputs, Is.Empty, "An absent lifecycle must not synthesize per-endpoint success.");
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task FxCloseConnectionsWithoutLifecyclePreservesHostHandler(bool asynchronous, bool remove)
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            AutomationComponentState component = harness.CreateFxComponentWithCloseConnections();
            CloseConnectionsMethodState method = component.CloseConnections!;
            ArrayOf<NodeId> endpoints =
                [new NodeId("endpoint:z", 1), new NodeId("endpoint:a", 1), new NodeId("endpoint:z", 1)];
            ArrayOf<StatusCode> expected =
                [StatusCodes.Good, StatusCodes.BadNodeIdUnknown, StatusCodes.BadInvalidState];
            using var cancellation = new CancellationTokenSource();
            int calls = 0;
            void VerifyHostCall(ISystemContext context, MethodState calledMethod, NodeId objectId,
                ArrayOf<NodeId> actualEndpoints, bool actualRemove)
            {
                calls++;
                Assert.That(context, Is.SameAs(harness.Context));
                Assert.That(((ServerSystemContext)context).SessionId, Is.EqualTo(new NodeId(123u)));
                Assert.That(calledMethod, Is.SameAs(method));
                Assert.That(objectId, Is.EqualTo(new NodeId("host:component", 1)));
                Assert.That(actualEndpoints, Is.EqualTo(endpoints),
                    "The generated dispatch must preserve order and duplicate endpoint IDs.");
                Assert.That(actualRemove, Is.EqualTo(remove));
            }
            var asyncHandler = new Mock<CloseConnectionsMethodStateMethodAsyncCallHandler>();
            asyncHandler.Setup(handler => handler(
                It.IsAny<ISystemContext>(), It.IsAny<MethodState>(), It.IsAny<NodeId>(),
                It.IsAny<ArrayOf<NodeId>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns((ISystemContext context, MethodState calledMethod, NodeId objectId,
                    ArrayOf<NodeId> actualEndpoints, bool actualRemove, CancellationToken token) =>
                {
                    VerifyHostCall(context, calledMethod, objectId, actualEndpoints, actualRemove);
                    Assert.That(token, Is.EqualTo(cancellation.Token));
                    return new ValueTask<CloseConnectionsMethodStateResult>(
                        new CloseConnectionsMethodStateResult { ServiceResult = ServiceResult.Good, ResultsOut = expected });
                });
            CloseConnectionsMethodStateMethodCallHandler syncHandler =
                (ISystemContext context, MethodState calledMethod, NodeId objectId,
                    ArrayOf<NodeId> actualEndpoints, bool actualRemove, ref ArrayOf<StatusCode> results) =>
                {
                    VerifyHostCall(context, calledMethod, objectId, actualEndpoints, actualRemove);
                    results = expected;
                    return ServiceResult.Good;
                };
            if (asynchronous)
            {
                method.OnCallAsync = asyncHandler.Object;
            }
            else
            {
                method.OnCall = syncHandler;
            }

            new PubSubFxReservationMethods(harness.Application,
                new PubSubServerOptions { ExposeFxModel = true }).Bind(component);

            Assert.That(method.OnCallAsync, asynchronous ? Is.SameAs(asyncHandler.Object) : Is.Null);
            Assert.That(method.OnCall, asynchronous ? Is.Null : Is.SameAs(syncHandler));
            Assert.That(component.EstablishConnections!.OnCallAsync, Is.Not.Null);
            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, [Variant.From(endpoints), Variant.From(remove)], cancellation.Token)
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(outputs, Has.Count.EqualTo(1));
            Assert.That(outputs[0].TryGetValue(out ArrayOf<StatusCode> statuses), Is.True);
            Assert.That(statuses, Is.EqualTo(expected));
            asyncHandler.Verify(handler => handler(
                It.IsAny<ISystemContext>(), It.IsAny<MethodState>(), It.IsAny<NodeId>(),
                It.IsAny<ArrayOf<NodeId>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
                asynchronous ? Times.Once() : Times.Never());
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public async Task FxCloseConnectionsDoesNotReplaceAnyExistingHandlerWhenLifecycleIsSupplied(
            int handlerKind)
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            AutomationComponentState component = harness.CreateFxComponentWithCloseConnections();
            CloseConnectionsMethodState method = component.CloseConnections!;
            object existingHandler;
            switch (handlerKind)
            {
                case 0:
                    existingHandler = (CloseConnectionsMethodStateMethodCallHandler) (
                        (ISystemContext context, MethodState calledMethod, NodeId objectId,
                            ArrayOf<NodeId> endpoints, bool remove, ref ArrayOf<StatusCode> results) =>
                        {
                            results = [];
                            return ServiceResult.Good;
                        });
                    method.OnCall = (CloseConnectionsMethodStateMethodCallHandler)existingHandler;
                    break;
                case 1:
                    existingHandler = (CloseConnectionsMethodStateMethodAsyncCallHandler)(
                        (ISystemContext context, MethodState calledMethod, NodeId objectId,
                            ArrayOf<NodeId> endpoints, bool remove, CancellationToken cancellationToken) =>
                            new ValueTask<CloseConnectionsMethodStateResult>(new CloseConnectionsMethodStateResult
                            {
                                ServiceResult = ServiceResult.Good,
                                ResultsOut = []
                            }));
                    method.OnCallAsync = (CloseConnectionsMethodStateMethodAsyncCallHandler)existingHandler;
                    break;
                case 2:
                    existingHandler = (GenericMethodCalledEventHandler)(
                        (ISystemContext context, MethodState calledMethod, ArrayOf<Variant> inputs,
                            List<Variant> outputs) => ServiceResult.Good);
                    method.OnCallMethod = (GenericMethodCalledEventHandler)existingHandler;
                    break;
                case 3:
                    existingHandler = (GenericMethodCalledEventHandler2)(
                        (ISystemContext context, MethodState calledMethod, NodeId objectId,
                            ArrayOf<Variant> inputs, List<Variant> outputs) => ServiceResult.Good);
                    method.OnCallMethod2 = (GenericMethodCalledEventHandler2)existingHandler;
                    break;
                case 4:
                    existingHandler = (GenericMethodCalledEventHandler2Async)(
                        (ISystemContext context, MethodState calledMethod, NodeId objectId,
                            ArrayOf<Variant> inputs, List<Variant> outputs, CancellationToken cancellationToken) =>
                            new ValueTask<ServiceResult>(ServiceResult.Good));
                    method.OnCallMethod2Async = (GenericMethodCalledEventHandler2Async)existingHandler;
                    break;
                case 5:
                    existingHandler = (MethodCalledWithResultEventHandlerAsync)(
                        (ISystemContext context, MethodState calledMethod, NodeId objectId,
                            ArrayOf<Variant> inputs, CancellationToken cancellationToken) =>
                            new ValueTask<MethodInvocationResult>(new MethodInvocationResult(ServiceResult.Good)));
                    method.OnCallMethodWithResultAsync = (MethodCalledWithResultEventHandlerAsync)existingHandler;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(handlerKind));
            }
            var lifecycle = new Mock<IPubSubFxConnectionLifecycle>();

            Assert.That(() => new PubSubFxReservationMethods(harness.Application,
                new PubSubServerOptions { ExposeFxModel = true }, lifecycle.Object).Bind(component),
                Throws.InvalidOperationException);

            switch (handlerKind)
            {
                case 0:
                    Assert.That(method.OnCall, Is.SameAs(existingHandler));
                    break;
                case 1:
                    Assert.That(method.OnCallAsync, Is.SameAs(existingHandler));
                    break;
                case 2:
                    Assert.That(method.OnCallMethod, Is.SameAs(existingHandler));
                    break;
                case 3:
                    Assert.That(method.OnCallMethod2, Is.SameAs(existingHandler));
                    break;
                case 4:
                    Assert.That(method.OnCallMethod2Async, Is.SameAs(existingHandler));
                    break;
                case 5:
                    Assert.That(method.OnCallMethodWithResultAsync, Is.SameAs(existingHandler));
                    break;
            }
            Assert.That(component.EstablishConnections!.OnCallAsync, Is.Null,
                "Binding must not partially install EstablishConnections when CloseConnections conflicts.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FxCloseConnectionsUsesOneHostTransactionAndReturnsOrderedResults(bool remove)
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            AutomationComponentState component = harness.CreateFxComponentWithCloseConnections();
            CloseConnectionsMethodState method = component.CloseConnections!;
            ArrayOf<NodeId> endpoints =
                [new NodeId("endpoint:z", 1), new NodeId("endpoint:a", 1), new NodeId("endpoint:z", 1)];
            ArrayOf<StatusCode> expected =
                [StatusCodes.Good, StatusCodes.BadNodeIdUnknown, StatusCodes.BadInvalidState];
            var lifecycle = new Mock<IPubSubFxConnectionLifecycle>();
            lifecycle.Setup(service => service.CloseConnectionsAsync(
                It.IsAny<ISystemContext>(), It.IsAny<AutomationComponentState>(), It.IsAny<ArrayOf<NodeId>>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns((ISystemContext context, AutomationComponentState actualComponent,
                   ArrayOf<NodeId> actualEndpoints, bool actualRemove, CancellationToken cancellationToken) =>
                {
                   Assert.That(context, Is.SameAs(harness.Context));
                   Assert.That(((ServerSystemContext)context).SessionId, Is.EqualTo(new NodeId(123u)));
                   Assert.That(actualComponent, Is.SameAs(component));
                   Assert.That(actualEndpoints, Is.EqualTo(endpoints));
                   Assert.That(actualRemove, Is.EqualTo(remove));
                   Assert.That(cancellationToken, Is.EqualTo(CancellationToken.None));
                   return new ValueTask<ArrayOf<StatusCode>>(expected);
                });
            new PubSubFxReservationMethods(harness.Application,
                new PubSubServerOptions { ExposeFxModel = true }, lifecycle.Object).Bind(component);

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, [Variant.From(endpoints), Variant.From(remove)]).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(outputs, Has.Count.EqualTo(1));
            Assert.That(outputs[0].TryGetValue(out ArrayOf<StatusCode> actualResults), Is.True);
            Assert.That(actualResults, Is.EqualTo(expected));
            lifecycle.Verify(service => service.CloseConnectionsAsync(
                It.IsAny<ISystemContext>(), It.IsAny<AutomationComponentState>(), It.IsAny<ArrayOf<NodeId>>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task FxCloseConnectionsRejectsWrongObjectIdBeforeCallingLifecycle()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            AutomationComponentState component = harness.CreateFxComponentWithCloseConnections();
            CloseConnectionsMethodState method = component.CloseConnections!;
            var lifecycle = new Mock<IPubSubFxConnectionLifecycle>();
            new PubSubFxReservationMethods(harness.Application,
                new PubSubServerOptions { ExposeFxModel = true }, lifecycle.Object).Bind(component);
            var outputs = new List<Variant>();
            var errors = new List<ServiceResult>();

            ServiceResult result = await method.CallAsync(
                harness.Context, new NodeId("forged:component", 1),
                [Variant.From(new ArrayOf<NodeId>(new[] { new NodeId("endpoint", 1) })), Variant.From(false)],
                errors, outputs).ConfigureAwait(false);

            Assert.That(errors.TrueForAll(ServiceResult.IsGood), Is.True);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(outputs, Is.Empty);
            lifecycle.Verify(service => service.CloseConnectionsAsync(
                It.IsAny<ISystemContext>(), It.IsAny<AutomationComponentState>(), It.IsAny<ArrayOf<NodeId>>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FxCloseConnectionsRequiresAuthorizedSession(bool sessionless)
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            AutomationComponentState component = harness.CreateFxComponentWithCloseConnections();
            CloseConnectionsMethodState method = component.CloseConnections!;
            var lifecycle = new Mock<IPubSubFxConnectionLifecycle>();
            new PubSubFxReservationMethods(harness.Application,
                new PubSubServerOptions { ExposeFxModel = true }, lifecycle.Object).Bind(component);
            if (sessionless)
            {
                harness.Context.SessionId = null;
            }
            else
            {
                var identity = new Mock<IUserIdentity>();
                identity.SetupGet(user => user.GrantedRoleIds).Returns([]);
                harness.Context.UserIdentity = identity.Object;
            }

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, [Variant.From(new ArrayOf<NodeId>(
                    new[] { new NodeId("endpoint", 1) })), Variant.From(false)]).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(outputs, Is.Empty);
            lifecycle.Verify(service => service.CloseConnectionsAsync(
                It.IsAny<ISystemContext>(), It.IsAny<AutomationComponentState>(), It.IsAny<ArrayOf<NodeId>>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task FxCloseConnectionsIsDeniedWhenConfigurationMethodsAreDisabled()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            AutomationComponentState component = harness.CreateFxComponentWithCloseConnections();
            CloseConnectionsMethodState method = component.CloseConnections!;
            var lifecycle = new Mock<IPubSubFxConnectionLifecycle>();
            new PubSubFxReservationMethods(harness.Application,
                new PubSubServerOptions
                {
                    ExposeFxModel = true,
                    ExposeConfigurationMethods = false
                }, lifecycle.Object).Bind(component);

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, [Variant.From(ArrayOf<NodeId>.Empty), Variant.From(false)])
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(outputs, Is.Empty);
            lifecycle.Verify(service => service.CloseConnectionsAsync(
                It.IsAny<ISystemContext>(), It.IsAny<AutomationComponentState>(), It.IsAny<ArrayOf<NodeId>>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task FxCloseConnectionsMapsHostServiceFailureWithoutEndpointSuccess()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            AutomationComponentState component = harness.CreateFxComponentWithCloseConnections();
            CloseConnectionsMethodState method = component.CloseConnections!;
            var lifecycle = new Mock<IPubSubFxConnectionLifecycle>();
            lifecycle.Setup(service => service.CloseConnectionsAsync(
                It.IsAny<ISystemContext>(), It.IsAny<AutomationComponentState>(), It.IsAny<ArrayOf<NodeId>>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadInvalidState));
            new PubSubFxReservationMethods(harness.Application,
                new PubSubServerOptions { ExposeFxModel = true }, lifecycle.Object).Bind(component);

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, [Variant.From(new ArrayOf<NodeId>(
                    new[] { new NodeId("endpoint", 1) })), Variant.From(true)]).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(outputs, Is.Empty);
        }

        [Test]
        public async Task FxCloseConnectionsRejectsMalformedHostResultCount()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            AutomationComponentState component = harness.CreateFxComponentWithCloseConnections();
            CloseConnectionsMethodState method = component.CloseConnections!;
            var lifecycle = new Mock<IPubSubFxConnectionLifecycle>();
            lifecycle.Setup(service => service.CloseConnectionsAsync(
                It.IsAny<ISystemContext>(), It.IsAny<AutomationComponentState>(), It.IsAny<ArrayOf<NodeId>>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<StatusCode>>([StatusCodes.Good]));
            new PubSubFxReservationMethods(harness.Application,
                new PubSubServerOptions { ExposeFxModel = true }, lifecycle.Object).Bind(component);

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, [Variant.From(new ArrayOf<NodeId>(
                    new[] { new NodeId("endpoint:first", 1), new NodeId("endpoint:second", 1) })),
                    Variant.From(false)]).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(outputs, Is.Empty);
        }

        [Test]
        public async Task FxCloseConnectionsRejectsNullHostResultArray()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            AutomationComponentState component = harness.CreateFxComponentWithCloseConnections();
            CloseConnectionsMethodState method = component.CloseConnections!;
            var lifecycle = new Mock<IPubSubFxConnectionLifecycle>();
            lifecycle.Setup(service => service.CloseConnectionsAsync(
                It.IsAny<ISystemContext>(), It.IsAny<AutomationComponentState>(), It.IsAny<ArrayOf<NodeId>>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<StatusCode>>(ArrayOf<StatusCode>.Null));
            new PubSubFxReservationMethods(harness.Application,
                new PubSubServerOptions { ExposeFxModel = true }, lifecycle.Object).Bind(component);

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, [Variant.From(new ArrayOf<NodeId>(
                    new[] { new NodeId("endpoint", 1) })), Variant.From(false)]).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(outputs, Is.Empty);
        }

        [Test]
        public async Task FxCloseConnectionsPassesAnEmptyEndpointArrayAsOneTransaction()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            AutomationComponentState component = harness.CreateFxComponentWithCloseConnections();
            CloseConnectionsMethodState method = component.CloseConnections!;
            var lifecycle = new Mock<IPubSubFxConnectionLifecycle>();
            lifecycle.Setup(service => service.CloseConnectionsAsync(
                It.IsAny<ISystemContext>(), It.IsAny<AutomationComponentState>(), It.IsAny<ArrayOf<NodeId>>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns((ISystemContext _, AutomationComponentState _, ArrayOf<NodeId> endpoints, bool _,
                    CancellationToken _) =>
                {
                    Assert.That(endpoints, Is.Empty);
                    return new ValueTask<ArrayOf<StatusCode>>(ArrayOf<StatusCode>.Empty);
                });
            new PubSubFxReservationMethods(harness.Application,
                new PubSubServerOptions { ExposeFxModel = true }, lifecycle.Object).Bind(component);

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, [Variant.From(ArrayOf<NodeId>.Empty), Variant.From(false)])
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(outputs, Has.Count.EqualTo(1));
            Assert.That(outputs[0].TryGetValue(out ArrayOf<StatusCode> actualResults), Is.True);
            Assert.That(actualResults.IsNull, Is.False);
            Assert.That(actualResults, Is.Empty);
            lifecycle.Verify(service => service.CloseConnectionsAsync(
                It.IsAny<ISystemContext>(), It.IsAny<AutomationComponentState>(), It.IsAny<ArrayOf<NodeId>>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task FxCloseConnectionsPropagatesCancellationAndDoesNotReturnSuccess()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            AutomationComponentState component = harness.CreateFxComponentWithCloseConnections();
            CloseConnectionsMethodState method = component.CloseConnections!;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var lifecycle = new Mock<IPubSubFxConnectionLifecycle>();
            lifecycle.Setup(service => service.CloseConnectionsAsync(
                It.IsAny<ISystemContext>(), It.IsAny<AutomationComponentState>(), It.IsAny<ArrayOf<NodeId>>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns((ISystemContext _, AutomationComponentState _, ArrayOf<NodeId> _, bool _,
                   CancellationToken cancellationToken) => WaitForCancellationAsync(entered, cancellationToken));
            new PubSubFxReservationMethods(harness.Application,
                new PubSubServerOptions { ExposeFxModel = true }, lifecycle.Object).Bind(component);
            using var cancellation = new CancellationTokenSource();
            Task<(ServiceResult Result, List<Variant> Outputs)> pending = harness.CallAsync(
                method, harness.Context, [Variant.From(new ArrayOf<NodeId>(
                    new[] { new NodeId("endpoint", 1) })), Variant.From(false)], cancellation.Token);
            await entered.Task.ConfigureAwait(false);
            cancellation.Cancel();

            (ServiceResult result, List<Variant> outputs) = await pending.ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(outputs, Is.Empty);
        }

        [Test]
        public async Task FxCloseConnectionsRejectsNullEndpointArrayBeforeCallingLifecycle()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            AutomationComponentState component = harness.CreateFxComponentWithCloseConnections();
            CloseConnectionsMethodState method = component.CloseConnections!;
            var lifecycle = new Mock<IPubSubFxConnectionLifecycle>();
            new PubSubFxReservationMethods(harness.Application,
                new PubSubServerOptions { ExposeFxModel = true }, lifecycle.Object).Bind(component);

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, [Variant.From(ArrayOf<NodeId>.Null), Variant.From(false)])
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(outputs, Is.Empty);
            lifecycle.Verify(service => service.CloseConnectionsAsync(
                It.IsAny<ISystemContext>(), It.IsAny<AutomationComponentState>(), It.IsAny<ArrayOf<NodeId>>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task FxCloseConnectionsHonorsPreCancelledTokenWithoutCallingLifecycle()
        {
            await using var harness = new Harness(options: new PubSubServerOptions { ExposeFxModel = true });
            await harness.InitializeAsync().ConfigureAwait(false);
            AutomationComponentState component = harness.CreateFxComponentWithCloseConnections();
            CloseConnectionsMethodState method = component.CloseConnections!;
            var lifecycle = new Mock<IPubSubFxConnectionLifecycle>();
            new PubSubFxReservationMethods(harness.Application,
                new PubSubServerOptions { ExposeFxModel = true }, lifecycle.Object).Bind(component);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            (ServiceResult result, List<Variant> outputs) = await harness.CallAsync(
                method, harness.Context, [Variant.From(new ArrayOf<NodeId>(
                    new[] { new NodeId("endpoint", 1) })), Variant.From(false)], cancellation.Token)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(outputs, Is.Empty);
            lifecycle.Verify(service => service.CloseConnectionsAsync(
                It.IsAny<ISystemContext>(), It.IsAny<AutomationComponentState>(), It.IsAny<ArrayOf<NodeId>>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        private static async ValueTask<ArrayOf<StatusCode>> WaitForCancellationAsync(
            TaskCompletionSource<bool> entered,
            CancellationToken cancellationToken)
        {
            entered.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return [];
        }

        private static PubSubReserveCommunicationIds2DataType FxRequest()
        {
            return new PubSubReserveCommunicationIds2DataType
            {
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                NumReqWriterGroupIds = 1,
                NumReqDataSetWriterIds = 1
            };
        }

        private static ArrayOf<Variant> FxArguments(
            uint commandMask,
            ArrayOf<AssetVerificationDataType> assets,
            params ReserveCommunicationIdsDataType[] reservations)
        {
            return FxArguments(commandMask, assets, ArrayOf.Empty<ConnectionEndpointConfigurationDataType>(),
                reservations);
        }

        private static ArrayOf<Variant> FxArguments(
            uint commandMask,
            ArrayOf<AssetVerificationDataType> assets,
            ArrayOf<ConnectionEndpointConfigurationDataType> endpoints,
            params ReserveCommunicationIdsDataType[] reservations)
        {
            return
            [
                Variant.From(commandMask),
                Variant.FromStructure(assets),
                Variant.FromStructure(endpoints),
                Variant.FromStructure(new ArrayOf<ReserveCommunicationIdsDataType>(reservations)),
                Variant.FromStructure(ArrayOf.Empty<CommunicationConfigurationDataType>())
            ];
        }

        private static ArrayOf<Variant> FxReserveArguments(params ReserveCommunicationIdsDataType[] requests)
        {
            return
            [
                Variant.From((uint)FxCommandMask.ReserveCommunicationIdsCmd),
                Variant.FromStructure(ArrayOf.Empty<AssetVerificationDataType>()),
                Variant.FromStructure(ArrayOf.Empty<ConnectionEndpointConfigurationDataType>()),
                Variant.FromStructure(new ArrayOf<ReserveCommunicationIdsDataType>(requests)),
                Variant.FromStructure(ArrayOf.Empty<CommunicationConfigurationDataType>())
            ];
        }

        private static PubSubReserveCommunicationIdsResult2DataType[] FxResults(
            Harness harness, List<Variant> outputs)
        {
            Assert.That(outputs[2].TryGetValue(out ArrayOf<ReserveCommunicationIdsResultDataType> results,
                harness.Context.AsMessageContext()), Is.True);
            return results.ToArray()!.Select(result =>
            {
                Assert.That(result, Is.TypeOf<PubSubReserveCommunicationIdsResult2DataType>());
                return (PubSubReserveCommunicationIdsResult2DataType)result;
            }).ToArray();
        }

        private static AssetVerificationResultDataType[] FxAssetResults(Harness harness, List<Variant> outputs)
        {
            Assert.That(outputs[0].TryGetValue(out ArrayOf<AssetVerificationResultDataType> results,
                harness.Context.AsMessageContext()), Is.True);
            return results.ToArray()!;
        }

        private static ConnectionEndpointConfigurationResultDataType[] FxEndpointResults(
            Harness harness,
            List<Variant> outputs)
        {
            Assert.That(outputs[1].TryGetValue(
                out ArrayOf<ConnectionEndpointConfigurationResultDataType> results,
                harness.Context.AsMessageContext()), Is.True);
            return results.ToArray()!;
        }

        private static NodeIdValuePair ExpectedVerificationVariable(string identifier, ushort namespaceIndex)
        {
            return new NodeIdValuePair
            {
                Key = new NodeIdArray
                {
                    Node = new NodeId(identifier, namespaceIndex),
                    ArrayIndex = []
                },
                Value = Variant.Null
            };
        }

        private static ConnectionEndpointConfigurationDataType FxEndpoint(
            NodeId functionalEntityId,
            ArrayOf<NodeIdValuePair> expectedVariables)
        {
            return new ConnectionEndpointConfigurationDataType
            {
                FunctionalEntityNode = functionalEntityId,
                ConnectionEndpoint = new ConnectionEndpointDefinitionDataType
                {
                    SwitchField = ConnectionEndpointDefinitionDataTypeFields.Node,
                    Node = new NodeId($"{functionalEntityId}:endpoint", functionalEntityId.NamespaceIndex)
                },
                ExpectedVerificationVariables = expectedVariables
            };
        }

        internal sealed class Harness : IAsyncDisposable
        {
            public Harness(
                IPubSubDefaultPublisherIdPolicy? policy = null,
                PubSubServerOptions? options = null,
                IPubSubIdAllocator? allocator = null,
                Func<IPubSubTransport>? transportFactory = null)
            {
                m_options = options ?? new PubSubServerOptions();
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                m_reservations = new InMemoryPubSubCommunicationIdReservations(policy ??
                    new PubSubDefaultPublisherIdPolicy(
                    [
                        new PubSubDefaultPublisherId(Profiles.PubSubUdpUadpTransport, Variant.From(7UL))
                    ]));
                var factory = new Mock<IPubSubTransportFactory>();
                factory.SetupGet(f => f.TransportProfileUri).Returns(Profiles.PubSubUdpUadpTransport);
                factory.Setup(f => f.Create(It.IsAny<PubSubConnectionDataType>(), It.IsAny<ITelemetryContext>(),
                    It.IsAny<TimeProvider>())).Returns(() =>
                        transportFactory?.Invoke() ?? new Mock<IPubSubTransport>().Object);
                Application = new PubSubApplicationBuilder(telemetry)
                    .UseConfiguration(new PubSubConfigurationDataType
                    {
                        PublishedDataSets = [new PublishedDataSetDataType { Name = "pds" }]
                    })
                    .WithCommunicationIdReservations(m_reservations)
                    .UseAllStandardEncoders()
                    .AddTransportFactory(factory.Object)
                    .Build();
                var server = new Mock<IServerInternal>();
                var namespaces = new NamespaceTable();
                Namespaces = namespaces;
                var types = new TypeTable(namespaces);
                types.AddSubtype(DataTypeIds.BaseDataType, NodeId.Null);
                types.AddSubtype(DataTypeIds.Structure, DataTypeIds.BaseDataType);
                types.AddSubtype(DataTypeIds.PubSubConnectionDataType, DataTypeIds.Structure);
                types.AddSubtype(DataTypeIds.WriterGroupDataType, DataTypeIds.Structure);
                types.AddSubtype(DataTypeIds.DataSetWriterDataType, DataTypeIds.Structure);
                types.AddSubtype(DataTypeIds.ReaderGroupDataType, DataTypeIds.Structure);
                types.AddSubtype(DataTypeIds.DataSetReaderDataType, DataTypeIds.Structure);
                types.AddSubtype(DataTypeIds.PubSubConfigurationDataType, DataTypeIds.Structure);
                types.AddSubtype(DataTypeIds.PubSubConfigurationRefDataType, DataTypeIds.Structure);
                server.SetupGet(s => s.NamespaceUris).Returns(namespaces);
                server.SetupGet(s => s.ServerUris).Returns(new StringTable());
                server.SetupGet(s => s.Factory).Returns(EncodeableFactory.Create());
                server.SetupGet(s => s.TypeTree).Returns(types);
                server.SetupGet(s => s.Telemetry).Returns(telemetry);
                Sessions = new Mock<ISessionManager>();
                server.SetupGet(s => s.SessionManager).Returns(Sessions.Object);
                m_queues = new MonitoredItemQueueFactory(telemetry);
                server.SetupGet(s => s.MonitoredItemQueueFactory).Returns(m_queues);
                Master = new Mock<IMasterNodeManager>();
                Master.SetupGet(m => m.ConfigurationNodeManager).Returns(new Mock<IConfigurationNodeManager>().Object);
                server.SetupGet(s => s.NodeManager).Returns(Master.Object);
                Context = new ServerSystemContext(server.Object) { SessionId = new NodeId(123u) };
                var identity = new Mock<IUserIdentity>();
                identity.SetupGet(i => i.GrantedRoleIds).Returns([ObjectIds.WellKnownRole_ConfigureAdmin]);
                Context.UserIdentity = identity.Object;
                server.SetupGet(s => s.DefaultSystemContext).Returns(Context);
                if (options?.ExposeFxModel == true)
                {
                    namespaces.GetIndexOrAppend(Opc.Ua.Di.Namespaces.OpcUaDi);
                    namespaces.GetIndexOrAppend(Opc.Ua.Fx.Data.Namespaces.FxData);
                    namespaces.GetIndexOrAppend(Opc.Ua.Fx.Ac.Namespaces.FxAc);
                    namespaces.GetIndexOrAppend(Opc.Ua.Fx.Cm.Namespaces.FxCm);
                    var modelNodes = new NodeStateCollection();
                    modelNodes.AddOpcUaDi(Context).AddOpcUaFxData(Context)
                        .AddOpcUaFxAc(Context).AddOpcUaFxCm(Context);
                    HashSet<NodeId> modelTypeIds = modelNodes.OfType<BaseTypeState>()
                        .Select(type => type.NodeId).ToHashSet();
                    foreach (NodeId superTypeId in modelNodes.OfType<BaseTypeState>()
                        .Select(type => type.SuperTypeId)
                        .Where(id => !id.IsNull && !modelTypeIds.Contains(id)).Distinct())
                    {
                        if (!types.IsKnown(superTypeId))
                        {
                            types.AddSubtype(superTypeId, NodeId.Null);
                        }
                    }
                }
                Root = new PublishSubscribeState(null);
                Root.Create(Context, ObjectIds.PublishSubscribe, new QualifiedName(BrowseNames.PublishSubscribe),
                    new LocalizedText(BrowseNames.PublishSubscribe), true);
                var diagnostics = new Mock<IDiagnosticsNodeManager>();
                diagnostics.Setup(d => d.FindPredefinedNode<BaseObjectState>(ObjectIds.PublishSubscribe))
                    .Returns(Root);
                diagnostics.Setup(d => d.FindPredefinedNode<MethodState>(new NodeId(17366u)))
                    .Returns(() => Root.AddConnection!);
                server.SetupGet(s => s.DiagnosticsNodeManager).Returns(diagnostics.Object);
                Manager = new PubSubNodeManager(server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    Application, null, m_options, telemetry, idAllocator: allocator);
            }

            public IPubSubApplication Application { get; }
            public ServerSystemContext Context { get; }
            public PublishSubscribeState Root { get; }
            public PubSubNodeManager Manager { get; }
            public Mock<ISessionManager> Sessions { get; }
            public NamespaceTable Namespaces { get; }
            public Mock<IMasterNodeManager> Master { get; }
            public AutomationComponentState? Component { get; private set; }
            public int VerifyAssetCalls { get; private set; }
            public ArrayOf<Variant> LastVerifyAssetInputs { get; private set; }
            public int VerifyFunctionalEntityCalls { get; private set; }
            public ArrayOf<Variant> LastVerifyFunctionalEntityInputs { get; private set; }

            public ValueTask InitializeAsync()
            {
                return Manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>());
            }

            public EstablishConnectionsMethodState BindFx(
                Func<CancellationToken, ValueTask<string?>>? listeningEndpoint = null)
            {
                var component = new AutomationComponentState(null) { NodeId = new NodeId("ac", 1) };
                Component = component;
                var method = new EstablishConnectionsMethodState(component);
                component.EstablishConnections = method;
                method.Create(Context, new NodeId("ac:establish", 1),
                    new QualifiedName("EstablishConnections"), new LocalizedText("EstablishConnections"), true);
                var binding = new PubSubFxReservationMethods(Application, m_options);
                binding.Bind(component, listeningEndpoint);
                return method;
            }

            public AutomationComponentState CreateFxComponentWithCloseConnections()
            {
                var component = new AutomationComponentState(null) { NodeId = new NodeId("host:component", 1) };
                var establish = new EstablishConnectionsMethodState(component);
                component.EstablishConnections = establish;
                establish.Create(Context, new NodeId("host:establish", 1),
                    new QualifiedName("EstablishConnections"), new LocalizedText("EstablishConnections"), true);
                var close = new CloseConnectionsMethodState(component);
                component.CloseConnections = close;
                close.Create(Context, new NodeId("host:close", 1),
                    new QualifiedName("CloseConnections"), new LocalizedText("CloseConnections"), true);
                return component;
            }

            public MethodState DynamicMethod(string parent, string name)
            {
                return Manager.FindPredefinedNode<MethodState>(
                    new NodeId($"{parent}:{name}", Manager.AddressSpaceNamespaceIndex));
            }

            public BaseObjectState AddVerifiableAsset(
                NodeId assetId,
                AssetVerificationResultEnum verificationResult,
                StatusCode verificationStatus = default)
            {
                var asset = new BaseObjectState(null)
                {
                    NodeId = assetId,
                    BrowseName = new QualifiedName("asset", assetId.NamespaceIndex),
                    TypeDefinitionId = ExpandedNodeId.ToNodeId(
                        Opc.Ua.Fx.Ac.ObjectTypeIds.IAssetRevisionType, Namespaces)
                };
                var verifyAsset = new MethodState(asset)
                {
                    NodeId = new NodeId($"{assetId}:VerifyAsset", assetId.NamespaceIndex),
                    BrowseName = new QualifiedName("VerifyAsset",
                        (ushort)Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc)),
                    Executable = true,
                    UserExecutable = true
                };
                verifyAsset.InputArguments = new PropertyState<ArrayOf<Argument>>
                    .Implementation<StructureBuilder<Argument>>(verifyAsset)
                {
                    Value =
                    [
                        new Argument
                        {
                            Name = "VerificationMode",
                            DataType = DataTypeIds.BaseDataType,
                            ValueRank = ValueRanks.Scalar
                        },
                        new Argument
                        {
                            Name = "ExpectedVerificationVariables",
                            DataType = DataTypeIds.BaseDataType,
                            ValueRank = ValueRanks.Any
                        },
                        new Argument
                        {
                            Name = "ExpectedAdditionalVerificationVariables",
                            DataType = DataTypeIds.BaseDataType,
                            ValueRank = ValueRanks.Any
                        }
                    ]
                };
                verifyAsset.OutputArguments = new PropertyState<ArrayOf<Argument>>
                    .Implementation<StructureBuilder<Argument>>(verifyAsset)
                {
                    Value =
                    [
                        new Argument
                        {
                            Name = "VerificationResult",
                            DataType = DataTypeIds.BaseDataType,
                            ValueRank = ValueRanks.Scalar
                        },
                        new Argument
                        {
                            Name = "VerificationVariablesErrors",
                            DataType = DataTypeIds.BaseDataType,
                            ValueRank = ValueRanks.Any
                        },
                        new Argument
                        {
                            Name = "VerificationAdditionalVariablesErrors",
                            DataType = DataTypeIds.BaseDataType,
                            ValueRank = ValueRanks.Any
                        }
                    ]
                };
                verifyAsset.OnCallMethod2Async = (_, _, _, inputs, output, _) =>
                {
                    VerifyAssetCalls++;
                    LastVerifyAssetInputs = inputs;
                    if (!StatusCode.IsBad(verificationStatus))
                    {
                        output[0] = Variant.From((int)verificationResult);
                        output[1] = Variant.From(ArrayOf<StatusCode>.Empty);
                        output[2] = Variant.From(ArrayOf<StatusCode>.Empty);
                        Assert.That(output[0].TryGetValue(out int resultCode), Is.True);
                        Assert.That(resultCode, Is.EqualTo((int)verificationResult));
                        Assert.That(output[1].TryGetValue(out ArrayOf<StatusCode> _), Is.True);
                        Assert.That(output[2].TryGetValue(out ArrayOf<StatusCode> _), Is.True);
                    }
                    return new ValueTask<ServiceResult>(verificationStatus);
                };
                asset.AddChild(verifyAsset);
                Master.Setup(master => master.FindNodeInAddressSpaceAsync(assetId, It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<NodeState?>(asset));
                return asset;
            }

            public MethodState AddVerifiableFunctionalEntity(
                NodeId functionalEntityId,
                FunctionalEntityVerificationResultEnum verificationResult,
                StatusCode verificationStatus = default,
                ArrayOf<StatusCode> verificationErrors = default,
                bool related = true)
            {
                AutomationComponentState component = Component ??
                    throw new InvalidOperationException("The AutomationComponent was not bound.");
                ushort namespaceIndex = (ushort)Namespaces.GetIndex(Opc.Ua.Fx.Ac.Namespaces.FxAc);
                var assetFolder = new FolderState(component)
                {
                    NodeId = new NodeId($"{functionalEntityId}:assets", namespaceIndex),
                    BrowseName = new QualifiedName("Assets", namespaceIndex)
                };
                var asset = new BaseObjectState(assetFolder)
                {
                    NodeId = new NodeId($"{functionalEntityId}:asset", namespaceIndex),
                    BrowseName = new QualifiedName("asset", namespaceIndex),
                    TypeDefinitionId = ExpandedNodeId.ToNodeId(
                        Opc.Ua.Fx.Ac.ObjectTypeIds.IAssetRevisionType, Namespaces)
                };
                var functionalEntity = new BaseObjectState(null)
                {
                    NodeId = functionalEntityId,
                    BrowseName = new QualifiedName("functional-entity", namespaceIndex),
                    TypeDefinitionId = ExpandedNodeId.ToNodeId(
                        Opc.Ua.Fx.Ac.ObjectTypeIds.FunctionalEntityType, Namespaces)
                };
                if (related)
                {
                    functionalEntity.AddReference(ReferenceTypeIds.IsHostedBy, false, asset.NodeId);
                }
                var verify = new MethodState(functionalEntity)
                {
                    NodeId = new NodeId($"{functionalEntityId}:Verify", namespaceIndex),
                    BrowseName = new QualifiedName("Verify", namespaceIndex),
                    Executable = true,
                    UserExecutable = true,
                    InputArguments = new PropertyState<ArrayOf<Argument>>
                        .Implementation<StructureBuilder<Argument>>(functionalEntity)
                    {
                        Value =
                        [
                            new Argument
                            {
                                Name = "ExpectedVerificationVariables",
                                DataType = DataTypeIds.BaseDataType,
                                ValueRank = ValueRanks.Any
                            }
                        ]
                    },
                    OutputArguments = new PropertyState<ArrayOf<Argument>>
                        .Implementation<StructureBuilder<Argument>>(functionalEntity)
                    {
                        Value =
                        [
                            new Argument
                            {
                                Name = "VerificationResult",
                                DataType = DataTypeIds.BaseDataType,
                                ValueRank = ValueRanks.Scalar
                            },
                            new Argument
                            {
                                Name = "VerificationVariablesErrors",
                                DataType = DataTypeIds.BaseDataType,
                                ValueRank = ValueRanks.Any
                            }
                        ]
                    }
                };
                verify.OnCallMethod2Async = (_, _, _, inputs, outputs, _) =>
                {
                    VerifyFunctionalEntityCalls++;
                    LastVerifyFunctionalEntityInputs = inputs;
                    if (!StatusCode.IsBad(verificationStatus))
                    {
                        outputs[0] = Variant.From((int)verificationResult);
                        outputs[1] = Variant.From(verificationErrors);
                    }
                    return new ValueTask<ServiceResult>(verificationStatus);
                };
                functionalEntity.AddChild(verify);
                Master.Setup(master => master.FindNodeInAddressSpaceAsync(
                    functionalEntityId, It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<NodeState?>(functionalEntity));
                Master.Setup(master => master.FindNodeInAddressSpaceAsync(
                    asset.NodeId, It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<NodeState?>(asset));
                return verify;
            }

            public Task<(ServiceResult Result, List<Variant> Outputs)> CallAsync(
                MethodState method, ISystemContext context, ArrayOf<Variant> inputs,
                CancellationToken cancellationToken = default)
            {
                return CallCoreAsync(method, context, inputs, cancellationToken);
            }

            private async Task<(ServiceResult Result, List<Variant> Outputs)> CallCoreAsync(
                MethodState method, ISystemContext context, ArrayOf<Variant> inputs,
                CancellationToken cancellationToken)
            {
                var outputs = new List<Variant>();
                var errors = new List<ServiceResult>();
                ServiceResult result = await method.CallAsync(context,
                    method.Parent?.NodeId ?? Root.NodeId, inputs, errors, outputs, cancellationToken)
                    .ConfigureAwait(false);
                Assert.That(errors.TrueForAll(ServiceResult.IsGood), Is.True,
                    "The real MethodState call must pass argument validation before testing the handler.");
                return (result, outputs);
            }

            public async ValueTask DisposeAsync()
            {
                Manager.Dispose();
                await Application.DisposeAsync().ConfigureAwait(false);
                m_reservations.Dispose();
                m_queues.Dispose();
            }

            private readonly InMemoryPubSubCommunicationIdReservations m_reservations;
            private readonly MonitoredItemQueueFactory m_queues;
            private readonly PubSubServerOptions m_options;
        }
    }
}
