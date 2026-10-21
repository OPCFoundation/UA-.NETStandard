/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root.
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.AutoID.Server;
using Opc.Ua.Server;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// Exercises bound RFID handlers and their actual event projection without a transport.
    /// </summary>
    [TestFixture]
    [Category("SecurityRegression")]
    public sealed class AutoIdAccessEventSecurityTests
    {
        /// <summary>
        /// A credential-bank operation preserves the caller's result, but never broadcasts its data.
        /// </summary>
        [TestCase(0u)]
        [TestCase(3u)]
        [TestCase(4u)]
        [TestCase(7u)]
        public async Task SecurityR14ReservedBankReadAndWriteNeverPublishCredentialsAsync(uint offset)
        {
            await ExerciseAsync("EPC", 0, offset, true, true, true,
                AutoIdOperationStatusEnumeration.SUCCESS, false).ConfigureAwait(false);
        }

        /// <summary>
        /// Explicitly classified ordinary memory retains its telemetry and provider arguments.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task SecurityR14SafeTagDataRetainsAuthorizedResultAndTelemetryAsync(int region)
        {
            await ExerciseAsync("EPC", checked((ushort)region), 19, true, true, true,
                AutoIdOperationStatusEnumeration.SUCCESS, true).ConfigureAwait(false);
        }

        /// <summary>
        /// Unknown classifications, credential overlap, empty data and disabled publication fail safe.
        /// </summary>
        [TestCase("EPC", 0, 8u, true, true, true, AutoIdOperationStatusEnumeration.SUCCESS, true)]
        [TestCase("EPC", 0, 7u, true, true, true, AutoIdOperationStatusEnumeration.SUCCESS, false)]
        [TestCase("EPC", 0, 0u, true, true, true, AutoIdOperationStatusEnumeration.SUCCESS, false)]
        [TestCase("custom", 3, 0u, false, true, true, AutoIdOperationStatusEnumeration.SUCCESS, false)]
        [TestCase("EPC", 3, 0u, true, false, true, AutoIdOperationStatusEnumeration.SUCCESS, false)]
        [TestCase("EPC", 3, uint.MaxValue, true, true, true, AutoIdOperationStatusEnumeration.SUCCESS, false)]
        [TestCase("EPC", 3, 0u, true, true, false, AutoIdOperationStatusEnumeration.SUCCESS, true)]
        [TestCase("EPC", 3, 0u, true, true, true, AutoIdOperationStatusEnumeration.NO_IDENTIFIER, false)]
        [TestCase("EPC", 3, 0u, true, true, true, AutoIdOperationStatusEnumeration.PASSWORD_ERROR, false)]
        public async Task SecurityR14CredentialRangeOverlapAndUnknownClassificationFailSafeAsync(
            string codeType, int region, uint offset, bool classified, bool hasPolicy, bool publish,
            AutoIdOperationStatusEnumeration status, bool includeData)
        {
            await ExerciseAsync(
                codeType, checked((ushort)region), offset, classified, hasPolicy, publish, status, includeData)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Empty successful reads and classifier faults cannot leak data or change the direct result.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SecurityR14EmptyDataAndClassificationFaultPreserveMethodOutcomeAsync(bool fault)
        {
            await ExerciseAsync("EPC", 3, 0, true, true, true,
                AutoIdOperationStatusEnumeration.SUCCESS, false, empty: !fault, fault: fault).ConfigureAwait(false);
        }

        /// <summary>
        /// Binds real method callbacks and checks direct and broadcast outputs separately.
        /// </summary>
        private static async Task ExerciseAsync(
            string codeType, ushort region, uint offset, bool classified, bool hasPolicy, bool publish,
            AutoIdOperationStatusEnumeration status, bool includeData, bool empty = false, bool fault = false)
        {
            SystemContext context = AutoIdTestContext.CreateSystemContext();
            var server = new Mock<IServerInternal>();
            var master = new Mock<IMasterNodeManager>();
            master.SetupGet(value => value.AsyncNodeManagers).Returns([]);
            server.SetupGet(value => value.NamespaceUris).Returns(context.NamespaceUris);
            server.SetupGet(value => value.ServerUris).Returns(new StringTable());
            server.SetupGet(value => value.Factory).Returns(context.EncodeableFactory);
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(context.NamespaceUris));
            server.SetupGet(value => value.Telemetry).Returns(context.Telemetry);
            server.SetupGet(value => value.NodeManager).Returns(master.Object);
            server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            await using var owner = new AutoIdNodeManager(server.Object, new ApplicationConfiguration
            {
                ApplicationUri = "urn:autoid-security:instances",
                ServerConfiguration = new ServerConfiguration()
            }, new AutoIdServerOptions { PublishAccessEvents = publish });
            var provider = new Mock<IAutoIdRfidProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.Kind).Returns(AutoIdReaderKind.Rfid);
            provider.SetupGet(value => value.SupportedOperations).Returns(AutoIdRfidOperations.All);
            provider.Setup(value => value.AttachAsync(
                It.IsAny<IAutoIdReaderSink>(), It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            provider.Setup(value => value.DetachAsync(It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            if (hasPolicy)
            {
                Mock<IAutoIdRfidAccessEventPolicy> policy = provider.As<IAutoIdRfidAccessEventPolicy>();
                if (fault)
                {
                    policy.Setup(value => value.CanPublishData(codeType, region, offset, 3))
                        .Throws(new InvalidOperationException("Unclassified memory."));
                }
                else
                {
                    policy.Setup(value => value.CanPublishData(codeType, region, offset, 3)).Returns(classified);
                }
            }
            ScanData identifier = AutoIdTestData.Epc(ByteString.From([17, 18]));
            ByteString password = ByteString.From([91, 92, 93, 94]);
            ByteString bytes = empty ? ByteString.Empty : ByteString.From([1, 2, 3]);
            ByteString returned = status == AutoIdOperationStatusEnumeration.SUCCESS ? bytes : ByteString.Empty;
            provider.Setup(value => value.ReadTagAsync(
                identifier, codeType, region, offset, 3, password, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AutoIdTagReadOutcome>(new AutoIdTagReadOutcome(returned, status)));
            provider.Setup(value => value.WriteTagAsync(
                identifier, codeType, region, offset, bytes, password, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AutoIdOperationStatusEnumeration>(status));
            provider.Setup(value => value.SetTagPasswordAsync(
                identifier, codeType, RfidPasswordTypeEnumeration.Access, password, bytes,
                It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AutoIdOperationStatusEnumeration>(AutoIdOperationStatusEnumeration.SUCCESS));
            ISystemContext system = owner.SystemContext;
            RfidReaderDeviceState device = system.CreateInstanceOfRfidReaderDeviceType(
                null!, new QualifiedName("Reader", owner.InstanceNamespaceIndex));
            device.AddReadTag(system).AddWriteTag(system).AddSetTagPassword(system);
            var events = new EventCapture(device);
            await using var reader = new AutoIdReader(owner.Runtime, device, provider.Object, false, false);
            reader.Bind();
            await reader.AttachAsync(CancellationToken.None).ConfigureAwait(false);

            ReadTagMethodStateResult read = await device.ReadTag!.OnCallAsync!(
                system, device.ReadTag, device.NodeId, identifier, codeType, region, offset, 3,
                password, CancellationToken.None).ConfigureAwait(false);
            WriteTagMethodStateResult write = await device.WriteTag!.OnCallAsync!(
                system, device.WriteTag, device.NodeId, identifier, codeType, region, offset, bytes,
                password, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(read.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(read.ResultData, Is.EqualTo(returned));
                Assert.That(read.Status, Is.EqualTo(status));
                Assert.That(write.Status, Is.EqualTo(status));
            });
            RfidAccessEventState[] observed = events.Of<RfidAccessEventState>().ToArray();
            if (!publish || !AutoIdReader.TagWasAccessed(status))
            {
                Assert.That(observed, Is.Empty);
            }
            else
            {
                Assert.That(observed.Select(value => value.Command!.Value), Is.EqualTo(s_accessCommands));
                foreach (RfidAccessEventState item in observed)
                {
                    RfidAccessResult access = item.AccessResult!.Value[0];
                    Assert.Multiple(() =>
                    {
                        Assert.That(access.Identifier, Is.EqualTo(identifier));
                        Assert.That(access.CodeType, Is.EqualTo(codeType));
                        Assert.That(access.Timestamp, Is.Not.Default);
                        Assert.That(access.EncodingMask & (uint)RfidAccessResultFields.Timestamp, Is.Not.Zero);
                        Assert.That(access.EncodingMask & (uint)RfidAccessResultFields.RWData,
                            Is.EqualTo(includeData ? (uint)RfidAccessResultFields.RWData : 0u));
                        Assert.That(access.EncodingMask & (uint)RfidAccessResultFields.CodeTypeRWData,
                            Is.EqualTo(includeData ? (uint)RfidAccessResultFields.CodeTypeRWData : 0u));
                    });
                    if (includeData)
                    {
                        Assert.That(access.RWData.ByteString, Is.EqualTo(bytes));
                        Assert.That(access.CodeTypeRWData, Is.EqualTo("RAW:BYTES"));
                    }
                    else
                    {
                        Assert.That(access.RWData?.ByteString.IsEmpty ?? true, Is.True);
                    }
                }
            }
            provider.Verify(value => value.ReadTagAsync(
                identifier, codeType, region, offset, 3, password, It.IsAny<CancellationToken>()), Times.Once);
            provider.Verify(value => value.WriteTagAsync(
                identifier, codeType, region, offset, bytes, password, It.IsAny<CancellationToken>()), Times.Once);

            SetTagPasswordMethodStateResult changed = await device.SetTagPassword!.OnCallAsync!(
                system, device.SetTagPassword, device.NodeId, identifier, codeType,
                RfidPasswordTypeEnumeration.Access, password, bytes, CancellationToken.None).ConfigureAwait(false);
            Assert.That(changed.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
            if (publish)
            {
                RfidAccessEventState passwordEvent = events.Of<RfidAccessEventState>().Last();
                Assert.That(passwordEvent.Command!.Value, Is.EqualTo("SetTagPassword"));
                Assert.That(passwordEvent.AccessResult!.Value[0].EncodingMask &
                    (uint)(RfidAccessResultFields.RWData | RfidAccessResultFields.CodeTypeRWData), Is.Zero);
            }
            else
            {
                Assert.That(events.Events, Is.Empty);
            }
            provider.Verify(value => value.SetTagPasswordAsync(
                identifier, codeType, RfidPasswordTypeEnumeration.Access, password, bytes,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>The expected direct-access event order.</summary>
        private static readonly string[] s_accessCommands = ["ReadTag", "WriteTag"];
    }
}
