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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Di.Client;

#nullable enable

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
            new NodeId("TransferServices", 2);

        private static Mock<ISession> CreateSessionMock()
        {
            var mock = new Mock<ISession>();
            var nsTable = new NamespaceTable();
            nsTable.GetIndexOrAppend(global::Opc.Ua.Di.Namespaces.OpcUaDi);
            mock.SetupGet(s => s.NamespaceUris).Returns(nsTable);
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

            int id = await client.TransferToDeviceAsync();
            Assert.That(id, Is.EqualTo(42));
        }

        [Test]
        public async Task TransferFromDeviceReturnsTransferIdFromServer()
        {
            Mock<ISession> session = CreateSessionMock();
            SetupCallReturnsInt(session, transferId: 7);

            var client = new DiTransferClient(
                session.Object, kTransferServicesId, NullTelemetry());

            int id = await client.TransferFromDeviceAsync();
            Assert.That(id, Is.EqualTo(7));
        }

        [Test]
        public void TransferToDeviceThrowsOnBadStatus()
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
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.BadInvalidArgument,
                            OutputArguments = global::Opc.Ua.ArrayOf.Empty<Variant>()
                        }
                    }.ToArrayOf()
                });

            var client = new DiTransferClient(
                session.Object, kTransferServicesId, NullTelemetry());

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.TransferToDeviceAsync());
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
                        var data1 = new global::Opc.Ua.Di.TransferResultDataDataType
                        {
                            SequenceNumber = 2,
                            EndOfResults = false,
                            ParameterDefs = new[]
                            {
                                new global::Opc.Ua.Di.ParameterResultDataType
                                {
                                    StatusCode = StatusCodes.Good
                                },
                                new global::Opc.Ua.Di.ParameterResultDataType
                                {
                                    StatusCode = StatusCodes.Good
                                }
                            }.ToArrayOf()
                        };
                        return CallResponseFor(data1);
                    }
                    var data2 = new global::Opc.Ua.Di.TransferResultDataDataType
                    {
                        SequenceNumber = 3,
                        EndOfResults = true,
                        ParameterDefs = new[]
                        {
                            new global::Opc.Ua.Di.ParameterResultDataType
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
                Is.EqualTo((StatusCode)StatusCodes.BadInternalError));
            Assert.That(call, Is.EqualTo(2));
        }

        [Test]
        public void StreamAsyncThrowsOnErrorChunk()
        {
            Mock<ISession> session = CreateSessionMock();
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    var err = new global::Opc.Ua.Di.TransferResultErrorDataType
                    {
                        Status = (int)(uint)StatusCodes.BadOutOfMemory
                    };
                    return CallResponseFor(err);
                });

            var client = new DiTransferClient(
                session.Object, kTransferServicesId, NullTelemetry());

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await foreach (ParameterFetchEntry _ in client.StreamAsync(transferId: 1))
                {
                }
            })!;
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
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments =
                                new Variant[] { new Variant(transferId) }.ToArrayOf()
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
                    new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments =
                            new Variant[] { new Variant(ext) }.ToArrayOf()
                    }
                }.ToArrayOf()
            };
        }
    }
}
