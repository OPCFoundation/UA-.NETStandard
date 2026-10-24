/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Opc.Ua.Client.Tests.Stack.Client.Fakes
{
    /// <summary>
    /// Supplies deliberately versioned synthetic custom metadata and observable decoding for restart tests.
    /// </summary>
    internal sealed class RecoveryTypeTestCodec : IEncodeableType, IDataTypeDefinitionSource
    {
        public Type Type => typeof(RecoveryTypeTestValue);
        public XmlQualifiedName XmlName => new("RecoveryTypeTestValue", NamespaceUri);
        internal string? Change { get; init; }
        internal int Decodes { get; private set; }

        public IEncodeable CreateInstance()
        {
            return new RecoveryTypeTestValue
            {
                Reverse = Change == "order",
                Signed = Change == "type",
                Decoded = () => Decodes++
            };
        }

        public DataTypeDefinition GetDataTypeDefinition(NamespaceTable namespaceUris)
        {
            var first = new StructureField
            {
                Name = "First",
                DataType = Change == "type" ? DataTypeIds.Int32 : DataTypeIds.UInt32,
                ValueRank = ValueRanks.Scalar
            };
            var second = new StructureField
            {
                Name = "Second",
                DataType = DataTypeIds.UInt32,
                ValueRank = ValueRanks.Scalar
            };
            return new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                DefaultEncodingId = NodeId.Create(502u, NamespaceUri, namespaceUris),
                StructureType = Change == "schema"
                    ? StructureType.StructureWithOptionalFields : StructureType.Structure,
                Fields = Change == "order" ? [second, first] : [first, second]
            };
        }

        internal const string NamespaceUri = "urn:synthetic:namespace:1";
    }

    /// <summary>
    /// Explicitly qualifies only the synthetic test codec; other custom registrations stay unqualified.
    /// </summary>
    internal sealed class RecoveryTypeTestQualifier : IRecoverableExecutionCodecQualifier
    {
        internal ByteString Revision { get; set; } = ByteString.From("synthetic-codec-v1"u8);

        public ValueTask<ByteString> GetRevisionAsync(
            ExpandedNodeId registrationId, IType codec, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return new ValueTask<ByteString>(codec is RecoveryTypeTestCodec ? Revision : default);
        }
    }

    /// <summary>
    /// Replaces an RSE registration without being the authoritative generated singleton.
    /// </summary>
    internal sealed class RecoveryRseOverrideCodec : IEncodeableType, IDataTypeDefinitionSource
    {
        public Type Type => typeof(SubmitResponseDataType);
        public XmlQualifiedName XmlName => SubmitResponseDataTypeActivator.Instance.XmlName;

        public IEncodeable CreateInstance()
        {
            return new SubmitResponseDataType();
        }

        public DataTypeDefinition GetDataTypeDefinition(NamespaceTable namespaceUris)
        {
            return SubmitResponseDataTypeActivator.Instance.GetDataTypeDefinition(namespaceUris);
        }
    }

    /// <summary>
    /// Produces a distinguishable interpretation when order or signedness changes behind unchanged wire identifiers.
    /// </summary>
    internal sealed class RecoveryTypeTestValue : IEncodeable
    {
        public ExpandedNodeId TypeId => new(501u, RecoveryTypeTestCodec.NamespaceUri);
        public ExpandedNodeId BinaryEncodingId => new(502u, RecoveryTypeTestCodec.NamespaceUri);
        public ExpandedNodeId XmlEncodingId => new(503u, RecoveryTypeTestCodec.NamespaceUri);
        internal long First { get; set; }
        internal uint Second { get; set; }
        internal bool Reverse { get; init; }
        internal bool Signed { get; init; }
        internal Action? Decoded { get; init; }

        public void Encode(IEncoder encoder)
        {
            encoder.WriteUInt32("First", checked((uint)First));
            encoder.WriteUInt32("Second", Second);
        }

        public void Decode(IDecoder decoder)
        {
            Decoded?.Invoke();
            if (Reverse)
            {
                Second = decoder.ReadUInt32("Second");
                First = decoder.ReadUInt32("First");
            }
            else
            {
                First = Signed ? decoder.ReadInt32("First") : decoder.ReadUInt32("First");
                Second = decoder.ReadUInt32("Second");
            }
        }

        public bool IsEqual(IEncodeable? encodeable)
        {
            return encodeable is RecoveryTypeTestValue value && First == value.First && Second == value.Second;
        }

        public object Clone()
        {
            return new RecoveryTypeTestValue
            {
                First = First,
                Second = Second,
                Reverse = Reverse,
                Signed = Signed,
                Decoded = Decoded
            };
        }
    }
}
