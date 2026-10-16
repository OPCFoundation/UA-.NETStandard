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
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Encoders.Tests
{
    /// <summary>
    /// Verifies the provisional 0.2.0 contract through the real standard encoders and generated type factory.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class AsyncServiceContractTests
    {
        /// <summary>
        /// Both mappings preserve the original request, separate exchange header and exact duration values.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void SubmissionRoundTripsDeclaredEnvelope(bool json)
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            var original = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 13 },
                MaxAge = 0,
                TimestampsToReturn = TimestampsToReturn.Both,
                NodesToRead = [new ReadValueId { NodeId = new NodeId(2258u), AttributeId = Attributes.Value }]
            };
            var request = new SubmitServiceRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 29 },
                ClientOperationId = new Uuid(new Guid("95c03745-16c4-4f97-9bf5-0948d733a5bd")),
                Request = new ExtensionObject(original),
                ExecutionAllowance = 2500.5,
                ResultRetentionTime = 5000.25,
                WaitTime = 0
            };
            byte[] encoded = json
                ? JsonEncoder.EncodeMessage(request, new byte[4096], context).AsSpan().ToArray()
                : BinaryEncoder.EncodeMessage(request, context);
            SubmitServiceRequest decoded = json
                ? JsonDecoder.DecodeMessage<SubmitServiceRequest>(encoded, context)
                : BinaryDecoder.DecodeMessage<SubmitServiceRequest>(encoded, context);

            Assert.That(decoded.TypeId, Is.EqualTo(new ExpandedNodeId(1200020u)));
            Assert.That(decoded.BinaryEncodingId, Is.EqualTo(new ExpandedNodeId(1202020u)));
            Assert.That(ObjectIds.SubmitServiceRequest_Encoding_DefaultJson, Is.EqualTo(new NodeId(1204020u)));
            Assert.That(decoded.ClientOperationId, Is.EqualTo(request.ClientOperationId));
            Assert.That(decoded.ExecutionAllowance, Is.EqualTo(2500.5));
            Assert.That(decoded.ResultRetentionTime, Is.EqualTo(5000.25));
            Assert.That(decoded.WaitTime, Is.Zero);
            Assert.That(decoded.RequestHeader.RequestHandle, Is.EqualTo(29u));
            Assert.That(decoded.Request.TryGetValue(out ReadRequest? recovered), Is.True);
            Assert.That(recovered!.RequestHeader.RequestHandle, Is.EqualTo(13u));
            Assert.That(recovered.NodesToRead[0].NodeId, Is.EqualTo(new NodeId(2258u)));
        }

        /// <summary>
        /// Independently reads the frozen binary field order rather than relying on symmetric generated codecs.
        /// </summary>
        [Test]
        public void SubmissionUsesCanonicalBinaryFieldOrder()
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            var id = new Uuid(new Guid("95c03745-16c4-4f97-9bf5-0948d733a5bd"));
            var request = new SubmitServiceRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 31 },
                ClientOperationId = id,
                Request = new ExtensionObject(new ReadRequest { RequestHeader = new RequestHeader() }),
                ExecutionAllowance = 101.5,
                ResultRetentionTime = 202.75,
                WaitTime = 3.25
            };
            byte[] bytes = BinaryEncoder.EncodeMessage(request, context);
            using var decoder = new BinaryDecoder(bytes, context);
            Assert.That(decoder.ReadNodeId(null), Is.EqualTo(new NodeId(1202020u)));
            var header = new RequestHeader();
            header.Decode(decoder);
            Assert.That(header.RequestHandle, Is.EqualTo(31u));
            Assert.That(decoder.ReadGuid(null), Is.EqualTo(id));
            Assert.That(decoder.ReadExtensionObject(null).TryGetValue(out ReadRequest? original), Is.True);
            Assert.That(original, Is.Not.Null);
            Assert.That(decoder.ReadDouble(null), Is.EqualTo(101.5));
            Assert.That(decoder.ReadDouble(null), Is.EqualTo(202.75));
            Assert.That(decoder.ReadDouble(null), Is.EqualTo(3.25));
            Assert.That(decoder.Position, Is.EqualTo(bytes.Length));
        }

        /// <summary>
        /// An available ServiceFault remains the original outcome, independent of a successful outer lookup.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CollectionPreservesCompleteOriginalServiceFault(bool json)
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            var original = new ServiceFault
            {
                ResponseHeader = new ResponseHeader
                {
                    RequestHandle = 13,
                    ServiceResult = StatusCodes.BadUserAccessDenied,
                    StringTable = ["original diagnostic"],
                    ServiceDiagnostics = new DiagnosticInfo { AdditionalInfo = "synthetic failure" }
                }
            };
            var response = new GetServiceResultResponse
            {
                ResponseHeader = new ResponseHeader { RequestHandle = 29 },
                Operation = new ServiceOperationInfoDataType
                {
                    ClientOperationId = new Uuid(new Guid("95c03745-16c4-4f97-9bf5-0948d733a5bd")),
                    ServerToken = ByteString.From(new byte[32]),
                    ServiceId = DataTypeIds.CallRequest,
                    State = ServiceOperationState.OutcomeAvailable,
                    OutcomeKind = ServiceOperationOutcome.ServiceResponse,
                    OutcomeStatus = StatusCodes.BadUserAccessDenied,
                    ExecutionAllowance = 2500,
                    ResultRetentionTime = 5000,
                    ResultRemaining = 4000
                },
                Result = new ExtensionObject(original)
            };
            byte[] encoded = json
                ? JsonEncoder.EncodeMessage(response, new byte[4096], context).AsSpan().ToArray()
                : BinaryEncoder.EncodeMessage(response, context);
            GetServiceResultResponse decoded = json
                ? JsonDecoder.DecodeMessage<GetServiceResultResponse>(encoded, context)
                : BinaryDecoder.DecodeMessage<GetServiceResultResponse>(encoded, context);
            Assert.That(decoded.TypeId, Is.EqualTo(new ExpandedNodeId(1200023u)));
            Assert.That(decoded.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(decoded.ResponseHeader.RequestHandle, Is.EqualTo(29u));
            Assert.That(decoded.Operation.OutcomeStatus, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(decoded.Operation.ResultRemaining, Is.EqualTo(4000));
            Assert.That(decoded.Result.TryGetValue(out ServiceFault? result), Is.True);
            Assert.That(result!.ResponseHeader.RequestHandle, Is.EqualTo(13u));
            Assert.That(result.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(result.ResponseHeader.StringTable, Is.EqualTo(original.ResponseHeader.StringTable));
            Assert.That(result.ResponseHeader.ServiceDiagnostics, Is.Not.Null);
            Assert.That(result.ResponseHeader.ServiceDiagnostics!.AdditionalInfo, Is.EqualTo("synthetic failure"));
        }

        /// <summary>
        /// The three control actions and their dispositions have the reviewed numeric values and declared headers.
        /// </summary>
        [TestCase(ServiceOperationControlAction.Cancel, 1)]
        [TestCase(ServiceOperationControlAction.Acknowledge, 2)]
        [TestCase(ServiceOperationControlAction.Abandon, 3)]
        public void ControlUsesCanonicalActionValues(ServiceOperationControlAction action, int wireValue)
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            var request = new ControlServiceExecutionRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 73 },
                ServerToken = ByteString.From(new byte[32]),
                Action = action
            };
            byte[] bytes = BinaryEncoder.EncodeMessage(request, context);
            using var decoder = new BinaryDecoder(bytes, context);
            Assert.That(decoder.ReadNodeId(null), Is.EqualTo(new NodeId(1202024u)));
            var header = new RequestHeader();
            header.Decode(decoder);
            Assert.That(header.RequestHandle, Is.EqualTo(73u));
            Assert.That(decoder.ReadByteString(null), Is.EqualTo(request.ServerToken));
            Assert.That(decoder.ReadInt32(null), Is.EqualTo(wireValue));
            Assert.That(decoder.Position, Is.EqualTo(bytes.Length));
            Assert.That((int)ServiceOperationControlResult.Applied, Is.EqualTo(1));
            Assert.That((int)ServiceOperationControlResult.AlreadyApplied, Is.EqualTo(2));
            Assert.That((int)ServiceOperationControlResult.NotApplicable, Is.EqualTo(3));
            Assert.That((int)ServiceOperationControlResult.UnknownOperation, Is.EqualTo(4));
        }

        /// <summary>
        /// Normal namespace-zero initialization does not install the experimental capability, audit or data model.
        /// </summary>
        [Test]
        public void StandardAddressSpaceExcludesExperimentalDefinitions()
        {
            var context = new SystemContext(NUnitTelemetryContext.Create()) { NamespaceUris = new NamespaceTable() };
            NodeStateCollection nodes = new NodeStateCollection().AddOpcUa(context);
            var pending = new Stack<NodeState>(nodes);
            var names = new HashSet<string>(StringComparer.Ordinal);
            while (pending.TryPop(out NodeState? node))
            {
                if (node.BrowseName.Name is { } name)
                {
                    names.Add(name);
                }
                var children = new List<BaseInstanceState>();
                node.GetChildren(context, children);
                foreach (BaseInstanceState child in children)
                {
                    pending.Push(child);
                }
            }
            Assert.That(names, Does.Contain("Server"));
            Assert.That(names.Any(name => name.StartsWith("AsyncService", StringComparison.Ordinal)), Is.False);
            Assert.That(names, Does.Not.Contain("ServiceOperationInfoDataType"));
            Assert.That(names, Does.Not.Contain("ServiceOperationState"));
            Assert.That(names, Does.Not.Contain("AuditServiceOperationEventType"));
        }
    }
}
