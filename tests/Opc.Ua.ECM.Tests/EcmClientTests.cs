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
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.ECM.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.ECM.Tests
{
    /// <summary>
    /// Exercises the actual generated method wrappers through a borrowed session.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class EcmClientTests
    {
        /// <summary>
        /// Rejects missing discovery dependencies before returning an enumerable or accessing the session.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void DiscoveryValidatesDependenciesBeforeEnumeration(bool wolOnly, bool missingTelemetry)
        {
            var session = new Mock<ISession>(MockBehavior.Strict);
            ISession source = missingTelemetry ? session.Object : null!;
            ITelemetryContext telemetry = missingTelemetry ? null! : NUnitTelemetryContext.Create();

            ArgumentNullException error = Assert.Throws<ArgumentNullException>(() =>
            {
                if (wolOnly)
                {
                    _ = EcmWolClient.DiscoverAsync(source, new NodeId("devices", 4), telemetry);
                }
                else
                {
                    _ = EcmClient.DiscoverAsync(source, new NodeId("devices", 4), telemetry);
                }
            })!;

            Assert.That(error.ParamName, Is.EqualTo(missingTelemetry ? "telemetry" : "session"));
            session.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Proves discovery uses current namespace mappings and vendor type inheritance rather than BrowseNames.
        /// </summary>
        [Test]
        public async Task DiscoveryFindsVendorStandbyAndWolSubtypesOnceWithChangedNamespaceIndexes()
        {
            Mock<ISession> session = CreateVariableSession(reorderedNamespaces: true);
            session.Object.NamespaceUris.Append("urn:ecm-tests:vendor");
            ushort vendorNamespace = session.Object.NamespaceUris.GetIndexOrAppend("urn:ecm-tests:vendor");
            NodeId parent = new("devices", vendorNamespace);
            NodeId standby = new("standby", vendorNamespace);
            NodeId wol = new("wol", vendorNamespace);
            NodeId vendorStandbyType = new("standbyType", vendorNamespace);
            NodeId vendorWolType = new("wolType", vendorNamespace);
            NodeId expectedStandby = ExpandedNodeId.ToNodeId(
                ObjectTypeIds.EnergyStandbyManagementType, session.Object.NamespaceUris);
            NodeId expectedWol = ExpandedNodeId.ToNodeId(
                ObjectTypeIds.EnergyDevicePowerOffType, session.Object.NamespaceUris);
            var cache = new Mock<INodeCache>(MockBehavior.Strict);
            cache.Setup(value => value.IsTypeOfAsync(vendorStandbyType, expectedStandby,
                It.IsAny<CancellationToken>())).Returns(new ValueTask<bool>(true));
            cache.Setup(value => value.IsTypeOfAsync(vendorWolType, expectedWol,
                It.IsAny<CancellationToken>())).Returns(new ValueTask<bool>(true));
            session.SetupGet(value => value.NodeCache).Returns(cache.Object);
            ReferenceDescription standbyReference = new()
            {
                NodeId = standby,
                TypeDefinition = vendorStandbyType,
                BrowseName = new QualifiedName("VendorPower", vendorNamespace),
                NodeClass = NodeClass.Object,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent,
                IsForward = true
            };
            ReferenceDescription wolReference = new()
            {
                NodeId = wol,
                TypeDefinition = vendorWolType,
                BrowseName = new QualifiedName("VendorSleep", vendorNamespace),
                NodeClass = NodeClass.Object,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent,
                IsForward = true
            };
            session.Setup(value => value.BrowseAsync(null, null, 0,
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ViewDescription _, uint _, ArrayOf<BrowseDescription> nodes,
                    CancellationToken _) => new ValueTask<BrowseResponse>(new BrowseResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new BrowseResult
                            {
                                References = nodes[0].NodeId == parent
                                    ? [standbyReference, standbyReference]
                                    : [wolReference]
                            }
                        ],
                        DiagnosticInfos = []
                    }));
            var discovered = new List<EcmClient>();

            await foreach (EcmClient client in EcmClient.DiscoverAsync(
                session.Object, parent, NUnitTelemetryContext.Create()).ConfigureAwait(false))
            {
                discovered.Add(client);
            }

            Assert.That(discovered, Has.Count.EqualTo(1));
            Assert.That(discovered[0].Standby.ObjectId, Is.EqualTo(standby));
            Assert.That(discovered[0].PowerOff, Is.Not.Null);
            Assert.That(discovered[0].PowerOff!.ObjectId, Is.EqualTo(wol));
            cache.Verify(value => value.IsTypeOfAsync(vendorStandbyType, expectedStandby,
                It.IsAny<CancellationToken>()), Times.Once);
            cache.Verify(value => value.IsTypeOfAsync(vendorWolType, expectedWol,
                It.IsAny<CancellationToken>()), Times.Once);
            session.Verify(value => value.CallAsync(null, It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Proves the typed PauseTime write retains its operation status instead of reporting rejection as acceptance.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task PauseTimeWritePreservesOperationStatus(int scenario)
        {
            StatusCode status = scenario switch
            {
                0 => StatusCodes.Good,
                1 => StatusCodes.GoodClamped,
                2 => StatusCodes.Uncertain,
                _ => StatusCodes.BadUserAccessDenied
            };
            Mock<ISession> session = CreateVariableSession();
            NodeId pause = new("pause", 2);
            ExpectVariable(session, pause, BrowseNames.PauseTime);
            session.Setup(value => value.WriteAsync(null,
                It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WriteResponse>(new WriteResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [status],
                    DiagnosticInfos = []
                }));
            var client = new EcmClient(session.Object, new NodeId("standby", 2), NUnitTelemetryContext.Create());
            if (StatusCode.IsBad(status))
            {
                ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await client.Standby.PauseTimeVariable().WriteWithStatusAsync(12000).ConfigureAwait(false));
                Assert.That(error.StatusCode, Is.EqualTo(status));
            }
            else
            {
                StatusCode result = await client.Standby.PauseTimeVariable()
                    .WriteWithStatusAsync(12000).ConfigureAwait(false);
                Assert.That(result, Is.EqualTo(status));
            }
            session.Verify(value => value.WriteAsync(null,
                It.Is<ArrayOf<WriteValue>>(items => items.Count == 1 && items[0].NodeId == pause &&
                    items[0].AttributeId == Attributes.Value && items[0].Value.WrappedValue == new Variant(12000d)),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// A bad service header cannot be hidden by a successful-looking variable operation result.
        /// </summary>
        [Test]
        public async Task PauseTimeWriteRejectsBadServiceHeader()
        {
            Mock<ISession> session = CreateVariableSession();
            ExpectVariable(session, new NodeId("pause", 2), BrowseNames.PauseTime);
            session.Setup(value => value.WriteAsync(null,
                It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WriteResponse>(new WriteResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadSessionClosed },
                    Results = [StatusCodes.Good],
                    DiagnosticInfos = []
                }));
            var client = new EcmClient(session.Object, new NodeId("standby", 2), NUnitTelemetryContext.Create());

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.Standby.PauseTimeVariable().WriteWithStatusAsync(12000)))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            session.Verify(value => value.WriteAsync(null,
                It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Status-preserving writes require exactly one result and correctly sized diagnostics.
        /// </summary>
        [TestCase(0, 0)]
        [TestCase(2, 0)]
        [TestCase(1, 2)]
        public async Task PauseTimeWriteRejectsMalformedResponse(int resultCount, int diagnosticCount)
        {
            Mock<ISession> session = CreateVariableSession();
            ExpectVariable(session, new NodeId("pause", 2), BrowseNames.PauseTime);
            var results = new StatusCode[resultCount];
            var diagnostics = new DiagnosticInfo[diagnosticCount];
            for (int ii = 0; ii < diagnostics.Length; ii++)
            {
                diagnostics[ii] = new DiagnosticInfo();
            }
            session.Setup(value => value.WriteAsync(null,
                It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WriteResponse>(new WriteResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = results,
                    DiagnosticInfos = diagnostics
                }));
            var client = new EcmClient(session.Object, new NodeId("standby", 2), NUnitTelemetryContext.Create());

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.Standby.PauseTimeVariable().WriteWithStatusAsync(12000)))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            session.Verify(value => value.WriteAsync(null,
                It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Proves typed standby reads retain bad or uncertain acquisition quality and both wire timestamps.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task StandbyReadPreservesQualityAndBothTimestamps(int scenario)
        {
            StatusCode quality = scenario switch
            {
                0 => StatusCodes.Good,
                1 => StatusCodes.Uncertain,
                _ => StatusCodes.BadNoCommunication
            };
            Mock<ISession> session = CreateVariableSession();
            NodeId status = new("status", 2);
            ExpectVariable(session, status, BrowseNames.StandbyManagementStatus);
            DateTimeUtc source = (DateTimeUtc)new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc);
            DateTimeUtc server = (DateTimeUtc)new DateTime(2026, 1, 1, 0, 0, 2, DateTimeKind.Utc);
            session.Setup(value => value.ReadAsync(null, 0, TimestampsToReturn.Both,
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ReadResponse>(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new DataValue(new Variant((byte)3), quality, source, server)],
                    DiagnosticInfos = []
                }));
            var client = new EcmClient(session.Object, new NodeId("standby", 2), NUnitTelemetryContext.Create());

            var result = await client.Standby.StandbyManagementStatusVariable().ReadAsync().ConfigureAwait(false);

            Assert.That(result.HasValue, Is.True);
            Assert.That(result.Value, Is.EqualTo(3));
            Assert.That(result.DataValue.StatusCode, Is.EqualTo(quality));
            Assert.That(result.DataValue.SourceTimestamp, Is.EqualTo(source));
            Assert.That(result.DataValue.ServerTimestamp, Is.EqualTo(server));
            session.Verify(value => value.ReadAsync(null, 0, TimestampsToReturn.Both,
                It.Is<ArrayOf<ReadValueId>>(items => items.Count == 1 && items[0].NodeId == status &&
                    items[0].AttributeId == Attributes.Value), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Proves every accepted standby output and the typed input survive generated-proxy invocation.
        /// </summary>
        [Test]
        public async Task StartPauseRetainsCallStatusModeAndAllTimings()
        {
            Mock<ISessionClient> session = CreateSession(StatusCodes.Good,
                [new Variant((byte)2), new Variant(1250d), new Variant(3000d), new Variant(4000d),
                    new Variant((byte)0)]);
            var client = CreateClient(session);

            EcmControlResult result = await client.StartPauseAsync(12000).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.ReturnCode, Is.Zero);
            Assert.That(result.ModeId, Is.EqualTo(2));
            Assert.That(result.CurrentTimeToDestination, Is.EqualTo(1250));
            Assert.That(result.RegularTimeToOperate, Is.EqualTo(3000));
            Assert.That(result.TimeMinLengthOfStay, Is.EqualTo(4000));
            session.Verify(value => value.CallAsync(null,
                It.Is<ArrayOf<CallMethodRequest>>(calls =>
                    calls.Count == 1 && calls[0].ObjectId == new NodeId("standby", 2) &&
                    calls[0].InputArguments.Count == 1 && calls[0].InputArguments[0] == new Variant(12000d)),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Proves Uncertain is not converted into Good by the generated wrappers.
        /// </summary>
        [Test]
        public async Task RejectedModeRetainsUncertainAndEffectiveMode()
        {
            Mock<ISessionClient> session = CreateSession(StatusCodes.Uncertain,
                [new Variant((byte)1), new Variant(0d), new Variant(0d), new Variant(0d),
                    new Variant((byte)0x52)]);
            EcmControlResult result = await CreateClient(session).SwitchToEnergySavingModeAsync(99)
                .ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(result.ReturnCode, Is.EqualTo(0x52));
            Assert.That(result.ModeId, Is.EqualTo(1));
            Assert.That(result.CurrentTimeToDestination, Is.Zero);
            session.Verify(value => value.CallAsync(null, It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Proves rejected StartPause cannot expose an effective mode as though it had selected a destination.
        /// </summary>
        [Test]
        public async Task RejectedStartPauseWithDestinationReportsDecodingError()
        {
            Mock<ISessionClient> session = CreateSession(StatusCodes.Uncertain,
                [new Variant((byte)2), new Variant(0d), new Variant(0d), new Variant(0d), new Variant((byte)0x54)]);

            EcmControlResult result = await CreateClient(session).StartPauseAsync(12000).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(result.ReturnCode.HasValue, Is.False);
            Assert.That(result.ModeId, Is.Zero);
            session.Verify(value => value.CallAsync(null, It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Proves bad status prevents malformed output interpretation or command replay.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task BadCallDoesNotInterpretDeviceOutputsOrReplay(int scenario)
        {
            StatusCode status = scenario switch
            {
                0 => StatusCodes.BadUserAccessDenied,
                1 => StatusCodes.BadUnexpectedError,
                _ => StatusCodes.BadConnectionClosed
            };
            Mock<ISessionClient> session = CreateSession(status, [new Variant("malformed")]);
            EcmControlResult result = await CreateClient(session).StartPauseAsync(12000).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(status));
            Assert.That(result.ReturnCode.HasValue, Is.False);
            Assert.That(result.ModeId, Is.Zero);
            Assert.That(result.CurrentTimeToDestination, Is.Zero);
            session.Verify(value => value.CallAsync(null, It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            session.Verify(value => value.TranslateBrowsePathsToNodeIdsAsync(null,
                It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Proves successful transport does not turn inconsistent device status into accepted control.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InconsistentCallAndDeviceStatusReportsDecodingError(bool uncertain)
        {
            StatusCode status = uncertain ? StatusCodes.Uncertain : StatusCodes.Good;
            byte code = uncertain ? (byte)0 : (byte)0x50;
            Mock<ISessionClient> session = CreateSession(status,
                [new Variant((byte)0), new Variant(0d), new Variant(0d), new Variant(0d), new Variant(code)]);
            EcmControlResult result = await CreateClient(session).StartPauseAsync(12000).ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(result.ReturnCode.HasValue, Is.False);
        }

        /// <summary>
        /// Proves truncated output is an explicit bad result, not a successful default.
        /// </summary>
        [Test]
        public async Task MissingOutputsDoNotFabricateSuccess()
        {
            Mock<ISessionClient> session = CreateSession(StatusCodes.Good, []);
            EcmControlResult result = await CreateClient(session).StartPauseAsync(12000).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(result.Status), Is.True);
            Assert.That(result.ReturnCode.HasValue, Is.False);
        }

        /// <summary>
        /// Proves a null scalar output cannot become a successful default value during generated decoding.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public async Task NullRequiredControlOutputDoesNotFabricateSuccess(int index)
        {
            Variant[] outputs =
            [
                new Variant((byte)2), new Variant(1000d), new Variant(2000d), new Variant(3000d), new Variant((byte)0)
            ];
            outputs[index] = default;
            Mock<ISessionClient> session = CreateSession(StatusCodes.Good, outputs);

            EcmControlResult result = await CreateClient(session).StartPauseAsync(12000).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(result.Status), Is.True);
            Assert.That(result.ReturnCode.HasValue, Is.False);
            Assert.That(result.ModeId, Is.Zero);
            session.Verify(value => value.CallAsync(null, It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Proves the EndPause estimate is not confused with regular recovery or destination time.
        /// </summary>
        [Test]
        public async Task EndPauseRetainsCurrentTimeToOperate()
        {
            Mock<ISessionClient> session = CreateSession(StatusCodes.Good,
                [new Variant(2750d), new Variant((byte)0)]);
            EcmControlResult result = await CreateClient(session).EndPauseAsync().ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.ReturnCode, Is.Zero);
            Assert.That(result.CurrentTimeToOperate, Is.EqualTo(2750));
            Assert.That(result.RegularTimeToOperate, Is.Zero);
            Assert.That(result.CurrentTimeToDestination, Is.Zero);
        }

        /// <summary>
        /// Proves WOL output is routed through its separately addressed generated proxy.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SwitchOffWolRetainsReservedModeAndTimings(bool standalone)
        {
            Mock<ISessionClient> session = CreateSession(StatusCodes.Good,
                [new Variant((byte)0xFE), new Variant(1000d), new Variant(2000d), new Variant(3000d),
                    new Variant((byte)0)]);
            EcmControlResult result = standalone ?
                await new EcmWolClient(session.Object, new NodeId("wol", 2), NUnitTelemetryContext.Create())
                    .SwitchOffWolAsync().ConfigureAwait(false) :
                await new EcmClient(session.Object, new NodeId("standby", 2),
                    NUnitTelemetryContext.Create(), new NodeId("wol", 2)).SwitchOffWolAsync().ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.ReturnCode, Is.Zero);
            Assert.That(result.ModeId, Is.EqualTo(0xFE));
            Assert.That(result.CurrentTimeToDestination, Is.EqualTo(1000));
            Assert.That(result.RegularTimeToOperate, Is.EqualTo(2000));
            Assert.That(result.TimeMinLengthOfStay, Is.EqualTo(3000));
            session.Verify(value => value.CallAsync(null,
                It.Is<ArrayOf<CallMethodRequest>>(calls => calls[0].ObjectId == new NodeId("wol", 2)),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// A WOL-only client retains rejection or bad Call status and never replays the command.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task StandaloneWolPreservesRejectedAndBadCalls(int scenario)
        {
            StatusCode status = scenario switch
            {
                0 => StatusCodes.Uncertain,
                1 => StatusCodes.BadConnectionClosed,
                _ => StatusCodes.BadUserAccessDenied
            };
            Mock<ISessionClient> session = CreateSession(status,
                StatusCode.IsBad(status) ? [new Variant("uninterpretable")] :
                [new Variant((byte)0), new Variant(0d), new Variant(0d), new Variant(0d), new Variant((byte)0x54)]);
            var client = new EcmWolClient(session.Object, new NodeId("wol", 2), NUnitTelemetryContext.Create());

            EcmControlResult result = await client.SwitchOffWolAsync().ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(status));
            Assert.That(result.ReturnCode, Is.EqualTo(StatusCode.IsBad(status) ? (byte?)null : (byte)0x54));
            Assert.That(result.ModeId, Is.Zero);
            Assert.That(result.CurrentTimeToDestination, Is.Zero);
            Assert.That(result.RegularTimeToOperate, Is.Zero);
            Assert.That(result.TimeMinLengthOfStay, Is.Zero);
            session.Verify(value => value.CallAsync(null, It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Accepted WOL results must identify the reserved destination rather than an arbitrary mode.
        /// </summary>
        [TestCase((byte)0)]
        [TestCase((byte)1)]
        [TestCase((byte)0xFF)]
        public async Task StandaloneWolRejectsIncorrectAcceptedDestination(byte mode)
        {
            Mock<ISessionClient> session = CreateSession(StatusCodes.Good,
                [new Variant(mode), new Variant(1000d), new Variant(2000d), new Variant(3000d), new Variant((byte)0)]);
            var client = new EcmWolClient(session.Object, new NodeId("wol", 2), NUnitTelemetryContext.Create());

            EcmControlResult result = await client.SwitchOffWolAsync().ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(result.ReturnCode.HasValue, Is.False);
            session.Verify(value => value.CallAsync(null, It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// A rejected WOL call cannot retain a destination as though the device accepted sleep.
        /// </summary>
        [TestCase((byte)1)]
        [TestCase((byte)0xFE)]
        public async Task StandaloneWolRejectsNonzeroRejectedDestination(byte mode)
        {
            Mock<ISessionClient> session = CreateSession(StatusCodes.Uncertain,
                [new Variant(mode), new Variant(0d), new Variant(0d), new Variant(0d), new Variant((byte)0x54)]);
            var client = new EcmWolClient(session.Object, new NodeId("wol", 2), NUnitTelemetryContext.Create());

            EcmControlResult result = await client.SwitchOffWolAsync().ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(result.ReturnCode.HasValue, Is.False);
            Assert.That(result.ModeId, Is.Zero);
            session.Verify(value => value.CallAsync(null, It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// WOL-only discovery uses subtype relationships and the live namespace table without a standby object.
        /// </summary>
        [Test]
        public async Task StandaloneWolDiscoveryFindsVendorSubtypeOnce()
        {
            Mock<ISession> session = CreateVariableSession(reorderedNamespaces: true);
            ushort vendor = session.Object.NamespaceUris.GetIndexOrAppend("urn:ecm-tests:vendor-wol");
            NodeId type = new("wolType", vendor);
            NodeId wol = new("wol", vendor);
            NodeId expected = ExpandedNodeId.ToNodeId(
                ObjectTypeIds.EnergyDevicePowerOffType, session.Object.NamespaceUris);
            var cache = new Mock<INodeCache>(MockBehavior.Strict);
            cache.Setup(value => value.IsTypeOfAsync(type, expected, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(true));
            session.SetupGet(value => value.NodeCache).Returns(cache.Object);
            var reference = new ReferenceDescription
            {
                NodeId = wol,
                TypeDefinition = type,
                BrowseName = new QualifiedName("VendorSleep", vendor),
                NodeClass = NodeClass.Object,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent,
                IsForward = true
            };
            session.Setup(value => value.BrowseAsync(null, null, 0,
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<BrowseResponse>(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { References = [reference, reference] }],
                    DiagnosticInfos = []
                }));
            var discovered = new List<EcmWolClient>();

            await foreach (EcmWolClient client in EcmWolClient.DiscoverAsync(
                session.Object, new NodeId("device", vendor), NUnitTelemetryContext.Create()).ConfigureAwait(false))
            {
                discovered.Add(client);
            }

            Assert.That(discovered, Has.Count.EqualTo(1));
            Assert.That(discovered[0].PowerOff.ObjectId, Is.EqualTo(wol));
            cache.Verify(value => value.IsTypeOfAsync(type, expected, It.IsAny<CancellationToken>()), Times.Once);
            session.Verify(value => value.CallAsync(null, It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A disconnected command retains the transport status without invoking or resolving a second method.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(4, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        [TestCase(4, true)]
        public async Task TransportFailureDoesNotReplayPowerCommand(int command, bool timeout)
        {
            StatusCode status = timeout ? StatusCodes.BadTimeout : StatusCodes.BadConnectionClosed;
            Mock<ISessionClient> session = CreateSession(StatusCodes.Good, []);
            session.Setup(value => value.CallAsync(null, It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>())).Throws(new ServiceResultException(status));
            var client = new EcmClient(session.Object, new NodeId("standby", 2),
                NUnitTelemetryContext.Create(), new NodeId("wol", 2));

            EcmControlResult result = command switch
            {
                0 => await client.StartPauseAsync(10000).ConfigureAwait(false),
                1 => await client.SwitchToEnergySavingModeAsync(1).ConfigureAwait(false),
                2 => await client.EndPauseAsync().ConfigureAwait(false),
                3 => await client.SwitchOffWolAsync().ConfigureAwait(false),
                _ => await new EcmWolClient(session.Object, new NodeId("wol", 2), NUnitTelemetryContext.Create())
                    .SwitchOffWolAsync().ConfigureAwait(false)
            };

            Assert.That(result.Status, Is.EqualTo(status));
            Assert.That(result.ReturnCode.HasValue, Is.False);
            Assert.That(result.ModeId, Is.Zero);
            Assert.That(result.CurrentTimeToDestination, Is.Zero);
            Assert.That(result.CurrentTimeToOperate, Is.Zero);
            Assert.That(result.RegularTimeToOperate, Is.Zero);
            Assert.That(result.TimeMinLengthOfStay, Is.Zero);
            session.Verify(value => value.CallAsync(null, It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            session.Verify(value => value.TranslateBrowsePathsToNodeIdsAsync(null,
                It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Proves absent optional WOL capability cannot invoke a command.
        /// </summary>
        [Test]
        public async Task MissingWolReportsUnsupportedWithoutCall()
        {
            Mock<ISessionClient> session = CreateSession(StatusCodes.Good, []);
            EcmControlResult result = await CreateClient(session).SwitchOffWolAsync().ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(result.ReturnCode.HasValue, Is.False);
            session.Verify(value => value.CallAsync(null, It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Creates a typed client over a strict borrowed session.
        /// </summary>
        private static EcmClient CreateClient(Mock<ISessionClient> session)
        {
            return new EcmClient(session.Object, new NodeId("standby", 2), NUnitTelemetryContext.Create());
        }

        /// <summary>
        /// Configures the existing managed-browse infrastructure for a real ISession-shaped test peer.
        /// </summary>
        private static Mock<ISession> CreateVariableSession(bool reorderedNamespaces = false)
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            if (reorderedNamespaces)
            {
                context.NamespaceUris.Append("urn:ecm-tests:unrelated");
            }
            context.NamespaceUris.Append(Namespaces.ECM);
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(value => value.MessageContext).Returns(context);
            session.SetupGet(value => value.NamespaceUris).Returns(context.NamespaceUris);
            session.SetupGet(value => value.Factory).Returns(context.Factory);
            session.SetupGet(value => value.OperationLimits).Returns(new OperationLimits { MaxNodesPerBrowse = 16 });
            session.SetupGet(value => value.ServerCapabilities).Returns(new ServerCapabilities());
            session.SetupGet(value => value.ContinuationPointPolicy).Returns(ContinuationPointPolicy.Balanced);
            return session;
        }

        /// <summary>
        /// Supplies one uniquely qualified generated variable through ordinary Browse.
        /// </summary>
        private static void ExpectVariable(Mock<ISession> session, NodeId node, string browseName)
        {
            session.Setup(value => value.BrowseAsync(null, null, 0,
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<BrowseResponse>(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new BrowseResult
                        {
                            References =
                            [
                                new ReferenceDescription
                                {
                                    NodeId = node,
                                    BrowseName = new QualifiedName(browseName,
                                        session.Object.NamespaceUris.GetIndexOrAppend(Namespaces.ECM)),
                                    NodeClass = NodeClass.Variable,
                                    ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent,
                                    IsForward = true
                                }
                            ]
                        }
                    ]
                }));
        }

        /// <summary>
        /// Supplies a real message context and one deterministic Call response.
        /// </summary>
        private static Mock<ISessionClient> CreateSession(StatusCode status, ArrayOf<Variant> outputs)
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.NamespaceUris.Append(Namespaces.ECM);
            var session = new Mock<ISessionClient>(MockBehavior.Strict);
            session.SetupGet(value => value.MessageContext).Returns(context);
            session.Setup(value => value.CallAsync(null, It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>())).Returns(new ValueTask<CallResponse>(new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new CallMethodResult { StatusCode = status, OutputArguments = outputs }],
                    DiagnosticInfos = []
                }));
            return session;
        }
    }
}
