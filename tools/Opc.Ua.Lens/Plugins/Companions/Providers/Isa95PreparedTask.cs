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
using System.Security.Cryptography;
using Opc.Ua;
using UaLens.Connection;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed record Isa95TaskBinding(NodeId OrderReceiver, NodeId ResponseProvider, NodeId ResponseReceiver);

    /// <summary>
    /// Encoded snapshots keep mutable generated structures out of retained task state.
    /// </summary>
    internal sealed class Isa95PreparedTask : CompanionTaskInput
    {
        public Isa95PreparedTask(
            CompanionTarget target,
            bool version2,
            Isa95JobTask task,
            Isa95TaskBinding binding,
            NodeId methodId,
            string jobOrderId,
            Variant request,
            Variant comment,
            Isa95TaskObservation? observation,
            IServiceMessageContext messageContext)
        {
            Target = target;
            Version2 = version2;
            OperationId = task.Operation.Id;
            Binding = binding;
            MethodId = methodId;
            JobOrderId = jobOrderId;
            Observation = observation;
            m_request = Encode(request, messageContext);
            m_comment = Encode(comment, messageContext);
            if (m_request.Length + m_comment.Length > CompanionInputContract.MaximumEncodedBytes)
            {
                throw IndustrialCompanionAccess.Limit("The ISA-95 task input exceeds 1 MiB.");
            }
            Review = $"{(version2 ? "V2" : "V1")} {task.Operation.DisplayName}; job ID: {jobOrderId}.\n" +
                $"Order receiver: {binding.OrderReceiver}; response provider: {binding.ResponseProvider}; " +
                $"response receiver: {binding.ResponseReceiver}.\nMethod: {methodId}.\n" +
                $"Request SHA-256: {Convert.ToHexString(SHA256.HashData(m_request.Span))}.\n" +
                $"Comment SHA-256: {Convert.ToHexString(SHA256.HashData(m_comment.Span))}.\n" +
                (observation is null ? "Responses are read only when this task is run.\n" :
                    $"Observed: {observation.Summary}\nObserved SHA-256: {observation.Fingerprint}.\n") +
                "Parameter values and comments are not reproduced in this review. " +
                "The server controls execution; this task never invokes BeginExecution, Complete or Close. " +
                "An invalid declaration MethodId may resolve to its instance; " +
                "accepted or ambiguous calls are never replayed.";
        }

        public CompanionTarget Target { get; }

        public bool Version2 { get; }

        public string OperationId { get; }

        public Isa95TaskBinding Binding { get; }

        public NodeId MethodId { get; }

        public string JobOrderId { get; }

        public Isa95TaskObservation? Observation { get; }

        public override string Review { get; }

        public Variant ReadRequest(IServiceMessageContext context)
        {
            return Decode(m_request, context);
        }

        public Variant ReadComment(IServiceMessageContext context)
        {
            return Decode(m_comment, context);
        }

        internal static ByteString Encode(Variant value, IServiceMessageContext context)
        {
            var encoded = ByteString.From(DataValueCodec.EncodeVariant(value, EncodingFormat.Binary, context));
            if (encoded.Length > CompanionInputContract.MaximumEncodedBytes)
            {
                throw IndustrialCompanionAccess.Limit("The ISA-95 value exceeds 1 MiB.");
            }
            return encoded;
        }

        internal static Variant Decode(ByteString encoded, IServiceMessageContext context)
        {
            return DataValueCodec.DecodeVariant(encoded.Span.ToArray(), EncodingFormat.Binary, context);
        }

        private readonly ByteString m_request;
        private readonly ByteString m_comment;
    }

    internal sealed class Isa95TaskObservation
    {
        public Isa95TaskObservation(
            NodeId catalogId,
            bool present,
            Variant value,
            string summary,
            IServiceMessageContext context)
        {
            CatalogId = catalogId;
            Present = present;
            Summary = summary;
            m_value = Isa95PreparedTask.Encode(value, context);
            Fingerprint = Convert.ToHexString(SHA256.HashData(m_value.Span));
        }

        public NodeId CatalogId { get; }

        public bool Present { get; }

        public string Summary { get; }

        public string Fingerprint { get; }

        public bool Matches(Isa95TaskObservation other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return CatalogId == other.CatalogId && Present == other.Present && m_value == other.m_value;
        }

        public Variant ReadValue(IServiceMessageContext context)
        {
            return Isa95PreparedTask.Decode(m_value, context);
        }

        private readonly ByteString m_value;
    }
}
