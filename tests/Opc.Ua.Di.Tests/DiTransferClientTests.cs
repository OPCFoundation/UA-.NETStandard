// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Di.Client;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Tests for <see cref="DiTransferClient"/> — the client-side
    /// TransferServicesType wrapper.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Transfer")]
    public sealed class DiTransferClientTests
    {
        private static readonly NodeId kTransferServicesId =
            new("TransferServices", 2);

        private static Mock<ISession> CreateSessionMock()
        {
            var mock = new Mock<ISession>();
            var nsTable = new NamespaceTable();
            nsTable.GetIndexOrAppend(global::Opc.Ua.Di.Namespaces.OpcUaDi);
            mock.SetupGet(s => s.NamespaceUris).Returns(nsTable);
            // The source-generated TransferServicesTypeClient proxy
            // reads Session.MessageContext.NamespaceUris (not
            // Session.NamespaceUris) to resolve method ExpandedNodeIds;
            // supply a stub context backed by the same table.
            var ctxMock = new Mock<IServiceMessageContext>();
            ctxMock.SetupGet(c => c.NamespaceUris).Returns(nsTable);
            mock.SetupGet(s => s.MessageContext).Returns(ctxMock.Object);
            return mock;
        }

        private static ITelemetryContext NullTelemetry()
        {
            return new Mock<ITelemetryContext>().Object;
        }

        [Test]
        public void ConstructorRejectsNullSession()
        {
            Assert.Throws<ArgumentNullException>(
                () => new DiTransferClient(null!, kTransferServicesId, NullTelemetry()));
        }

        [Test]
        public void ConstructorRejectsNullNodeId()
        {
            Mock<ISession> session = CreateSessionMock();
            Assert.Throws<ArgumentException>(
                () => new DiTransferClient(session.Object, NodeId.Null, NullTelemetry()));
        }

        [Test]
        public void ConstructorRejectsNullTelemetry()
        {
            Mock<ISession> session = CreateSessionMock();
            Assert.Throws<ArgumentNullException>(
                () => new DiTransferClient(session.Object, kTransferServicesId, null!));
        }

        [Test]
        public async Task TransferToDeviceReturnsTransferIdFromServer()
        {
            Mock<ISession> session = CreateSessionMock();
            SetupCallReturnsInt(session, transferId: 42);

            var client = new DiTransferClient(
                session.Object, kTransferServicesId, NullTelemetry());

            int id = await client.TransferToDeviceAsync().ConfigureAwait(false);
            Assert.That(id, Is.EqualTo(42));
        }

        [Test]
        public async Task TransferFromDeviceReturnsTransferIdFromServer()
        {
            Mock<ISession> session = CreateSessionMock();
            SetupCallReturnsInt(session, transferId: 7);

            var client = new DiTransferClient(
                session.Object, kTransferServicesId, NullTelemetry());

            int id = await client.TransferFromDeviceAsync().ConfigureAwait(false);
            Assert.That(id, Is.EqualTo(7));
        }

        [Test]
        public async Task TransferToDeviceThrowsOnBadStatus()
        {
            Mock<ISession> session = CreateSessionMock();
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results = new CallMethodResult[]
                    {
                        new() {
                            StatusCode = StatusCodes.BadInvalidArgument,
                            OutputArguments = global::Opc.Ua.ArrayOf.Empty<Variant>()
                        }
                    }.ToArrayOf()
                });

            var client = new DiTransferClient(
                session.Object, kTransferServicesId, NullTelemetry());

            await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.TransferToDeviceAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task StreamAsyncYieldsEntriesUntilEndOfResults()
        {
            Mock<ISession> session = CreateSessionMock();

            int call = 0;
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    call++;
                    if (call == 1)
                    {
                        var data1 = new TransferResultDataDataType
                        {
                            SequenceNumber = 2,
                            EndOfResults = false,
                            ParameterDefs = new[]
                            {
                                new ParameterResultDataType
                                {
                                    StatusCode = StatusCodes.Good
                                },
                                new ParameterResultDataType
                                {
                                    StatusCode = StatusCodes.Good
                                }
                            }.ToArrayOf()
                        };
                        return CallResponseFor(data1);
                    }
                    var data2 = new TransferResultDataDataType
                    {
                        SequenceNumber = 3,
                        EndOfResults = true,
                        ParameterDefs = new[]
                        {
                            new ParameterResultDataType
                            {
                                StatusCode = StatusCodes.BadInternalError
                            }
                        }.ToArrayOf()
                    };
                    return CallResponseFor(data2);
                });

            var client = new DiTransferClient(
                session.Object, kTransferServicesId, NullTelemetry());

            var entries = new List<ParameterFetchEntry>();
            await foreach (ParameterFetchEntry e in client.StreamAsync(transferId: 1))
            {
                entries.Add(e);
            }

            Assert.That(entries, Has.Count.EqualTo(3));
            Assert.That(StatusCode.IsGood(entries[0].StatusCode));
            Assert.That(StatusCode.IsGood(entries[1].StatusCode));
            Assert.That(entries[2].StatusCode,
                Is.EqualTo(StatusCodes.BadInternalError));
            Assert.That(call, Is.EqualTo(2));
        }

        [Test]
        public async Task StreamAsyncThrowsOnErrorChunk()
        {
            Mock<ISession> session = CreateSessionMock();
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    var err = new TransferResultErrorDataType
                    {
                        Status = (int)(uint)StatusCodes.BadOutOfMemory
                    };
                    return CallResponseFor(err);
                });

            var client = new DiTransferClient(
                session.Object, kTransferServicesId, NullTelemetry());

            ServiceResultException ex = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await foreach (ParameterFetchEntry _ in client.StreamAsync(transferId: 1))
                {
                }
            }))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadOutOfMemory));
        }

        private static void SetupCallReturnsInt(Mock<ISession> session, int transferId)
        {
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results = new CallMethodResult[]
                    {
                        new() {
                            StatusCode = StatusCodes.Good,
                            // TransferToDevice / TransferFromDevice in OPC
                            // 10000-100 §10.4 return two output arguments:
                            // (transferID, initTransferStatus). The
                            // generated proxy method materialises both.
                            OutputArguments =
                                new Variant[]
                                {
                                    new(transferId),
                                    new(0)
                                }.ToArrayOf()
                        }
                    }.ToArrayOf()
                });
        }

        private static CallResponse CallResponseFor(IEncodeable payload)
        {
            var ext = new ExtensionObject(payload);
            return new CallResponse
            {
                Results = new CallMethodResult[]
                {
                    new() {
                        StatusCode = StatusCodes.Good,
                        OutputArguments =
                            new Variant[] { new(ext) }.ToArrayOf()
                    }
                }.ToArrayOf()
            };
        }
    }
}
