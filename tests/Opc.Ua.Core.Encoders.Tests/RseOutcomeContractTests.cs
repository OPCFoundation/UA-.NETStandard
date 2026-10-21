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
    /// Verifies retained RSE outcomes and their separation from ordinary Call exchange status.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class RseOutcomeContractTests
    {
        /// <summary>
        /// Both mappings preserve the original request, separate exchange header and exact duration values.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void SubmissionRoundTripsDeclaredEnvelope(bool json)
        {
            ServiceMessageContext context = CreateContext();
            var original = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 13 },
                MaxAge = 0,
                TimestampsToReturn = TimestampsToReturn.Both,
                NodesToRead = [new ReadValueId { NodeId = new NodeId(2258u), AttributeId = Attributes.Value }]
            };
            var submission = new SubmitRequestDataType
            {
                ClientOperationId = new Uuid(new Guid("95c03745-16c4-4f97-9bf5-0948d733a5bd")),
                RecoveryMode = ServiceOperationRecoveryMode.Session,
                Request = new ExtensionObject(original),
                ExecutionAllowance = 2500.5,
                ResultRetentionTime = 5000.25,
                WaitTime = 0
            };
            var request = new CallRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 29 },
                MethodsToCall = [new CallMethodRequest { InputArguments = [Variant.FromStructure(submission)] }]
            };
            byte[] encoded = json
                ? [.. JsonEncoder.EncodeMessage(request, new byte[4096], context)]
                : BinaryEncoder.EncodeMessage(request, context);
            CallRequest outer = json
                ? JsonDecoder.DecodeMessage<CallRequest>(encoded, context)
                : BinaryDecoder.DecodeMessage<CallRequest>(encoded, context);
            Assert.That(outer.MethodsToCall[0].InputArguments[0].TryGetValue<SubmitRequestDataType>(
                out SubmitRequestDataType? decoded, context), Is.True);

            Assert.That(decoded!.TypeId, Is.EqualTo(new ExpandedNodeId(15u, RseNamespaces.Rse)));
            Assert.That(decoded.BinaryEncodingId, Is.EqualTo(new ExpandedNodeId(16u, RseNamespaces.Rse)));
            Assert.That(RseObjectIds.SubmitRequestDataType_Encoding_DefaultJson,
                Is.EqualTo(new ExpandedNodeId(18u, RseNamespaces.Rse)));
            Assert.That(decoded.ClientOperationId, Is.EqualTo(submission.ClientOperationId));
            Assert.That(decoded.ExecutionAllowance, Is.EqualTo(2500.5));
            Assert.That(decoded.ResultRetentionTime, Is.EqualTo(5000.25));
            Assert.That(decoded.WaitTime, Is.Zero);
            Assert.That(outer.RequestHeader.RequestHandle, Is.EqualTo(29u));
            Assert.That(decoded.Request.TryGetValue(out ReadRequest? recovered), Is.True);
            Assert.That(recovered!.RequestHeader.RequestHandle, Is.EqualTo(13u));
            Assert.That(recovered.NodesToRead[0].NodeId, Is.EqualTo(new NodeId(2258u)));
        }

        /// <summary>
        /// Keeps Core and RSE numeric lookup tables separate despite sharing the public code namespace.
        /// </summary>
        [Test]
        public void SharedCodeNamespaceDoesNotMergeNumericIdentifiers()
        {
            Assert.That(typeof(SubmitRequestDataType).Namespace, Is.EqualTo("Opc.Ua"));
            Assert.That(DataTypes.GetBrowseName(1), Is.EqualTo("Boolean"));
            Assert.That(RseDataTypes.GetBrowseName(1), Is.EqualTo("ServiceOperationState"));
            Assert.That(RseObjectTypeIds.RecoverableExecutionDiagnosticsType,
                Is.EqualTo(new ExpandedNodeId(49u, RseNamespaces.Rse)));
            Assert.That(RseObjectIds.RecoverableExecutionDiagnostics,
                Is.EqualTo(new ExpandedNodeId(72u, RseNamespaces.Rse)));
        }

        /// <summary>
        /// An available ServiceFault remains the original outcome, independent of a successful outer lookup.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CollectionPreservesCompleteOriginalServiceFault(bool json)
        {
            ServiceMessageContext context = CreateContext();
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
            var outcome = new GetResultResponseDataType
            {
                Operation = new ServiceOperationInfoDataType
                {
                    ClientOperationId = new Uuid(new Guid("95c03745-16c4-4f97-9bf5-0948d733a5bd")),
                    ServerToken = ByteString.From(new byte[32]),
                    ServiceId = DataTypeIds.CallRequest,
                    RecoveryMode = ServiceOperationRecoveryMode.Durable,
                    State = ServiceOperationState.OutcomeAvailable,
                    OutcomeKind = ServiceOperationOutcome.ServiceResponse,
                    OutcomeStatus = StatusCodes.BadUserAccessDenied,
                    ExecutionAllowance = 2500,
                    ResultRetentionTime = 5000,
                    ResultRemaining = 4000
                },
                Result = new ExtensionObject(original)
            };
            var response = new CallResponse
            {
                ResponseHeader = new ResponseHeader { RequestHandle = 29 },
                Results = [new CallMethodResult { OutputArguments = [Variant.FromStructure(outcome)] }]
            };
            byte[] encoded = json
                ? [.. JsonEncoder.EncodeMessage(response, new byte[4096], context)]
                : BinaryEncoder.EncodeMessage(response, context);
            CallResponse outer = json
                ? JsonDecoder.DecodeMessage<CallResponse>(encoded, context)
                : BinaryDecoder.DecodeMessage<CallResponse>(encoded, context);
            Assert.That(outer.Results[0].OutputArguments[0].TryGetValue<GetResultResponseDataType>(
                out GetResultResponseDataType? decoded, context), Is.True);
            Assert.That(decoded!.TypeId, Is.EqualTo(new ExpandedNodeId(27u, RseNamespaces.Rse)));
            Assert.That(outer.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(outer.ResponseHeader.RequestHandle, Is.EqualTo(29u));
            Assert.That(outer.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(decoded.Operation.RecoveryMode, Is.EqualTo(ServiceOperationRecoveryMode.Durable));
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
        /// Every control Method uses the same token-only argument and the declared control disposition values.
        /// </summary>
        [Test]
        public void ControlUsesCanonicalTokenAndDispositionValues()
        {
            ServiceMessageContext context = CreateContext();
            var request = new OperationControlRequestDataType
            {
                ServerToken = ByteString.From(1, 0, 255, 127)
            };
            byte[] bytes = BinaryEncoder.EncodeMessage(request, context);
            using var decoder = new BinaryDecoder(bytes, context);
            Assert.That(decoder.ReadNodeId(null),
                Is.EqualTo(new NodeId(32u, context.NamespaceUris.GetIndexOrAppend(RseNamespaces.Rse))));
            Assert.That(decoder.ReadByteString(null), Is.EqualTo(request.ServerToken));
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
            while (pending.Count != 0)
            {
                NodeState node = pending.Pop();
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
            Assert.That(names, Does.Not.Contain("RecoverableExecutionManager"));
            Assert.That(names, Does.Not.Contain("RecoverableExecutionDiagnostics"));
        }

        private static ServiceMessageContext CreateContext()
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.NamespaceUris.GetIndexOrAppend("urn:rse-codec-test:application");
            context.NamespaceUris.GetIndexOrAppend(RseNamespaces.Rse);
            context.Factory.Builder.AddOpcUaRse().Commit();
            return context;
        }
    }
}
