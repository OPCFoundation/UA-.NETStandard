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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Moq;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.StructuredValues;
using UaLens.Telemetry;

namespace UaLens.Tests.StructuredValues;

internal sealed class StructuredValueTestContext : IDisposable
{
    public StructuredValueTestContext()
    {
        var telemetry = new AppTelemetryContext(new LogRingBuffer(16));
        MessageContext = ServiceMessageContext.Create(telemetry);
        NamespaceIndex = MessageContext.NamespaceUris.GetIndexOrAppend(kNamespaceUri);
        Session.SetupGet(value => value.Connected).Returns(() => Connected);
        Session.SetupGet(value => value.SessionId).Returns(() => SessionId);
        Session.SetupGet(value => value.MessageContext).Returns(() => MessageContext);
        Session.SetupGet(value => value.NamespaceUris).Returns(() => MessageContext.NamespaceUris);
        Session.SetupGet(value => value.Factory).Returns(() => MessageContext.Factory);
        Session.SetupGet(value => value.Endpoint).Returns(() => Endpoint);
        Session.Setup(value => value.ReadAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
            It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> ids,
                CancellationToken cancellationToken) =>
            {
                Reads++;
                return Reader(ids, cancellationToken);
            });
        Service = new SessionStructuredValueService(Session.Object);
    }

    public ServiceMessageContext MessageContext { get; set; }
    public ushort NamespaceIndex { get; }
    public Mock<ISession> Session { get; } = new(MockBehavior.Strict);
    public SessionStructuredValueService Service { get; }
    public NodeId SessionId { get; set; } = new(1u);
    public bool Connected { get; set; } = true;
    public int Reads { get; private set; }
    public EndpointDescription Endpoint { get; } = new()
    {
        EndpointUrl = "opc.tcp://unit.test:4840",
        Server = new ApplicationDescription { ApplicationUri = "urn:unit:test:server" }
    };

    public Func<ArrayOf<ReadValueId>, CancellationToken, ValueTask<ReadResponse>> Reader { get; set; } =
        (_, _) => throw new InvalidOperationException("This test did not expect a server read.");

    public NativeType Register(string name, StructureType kind, ArrayOf<StructureField> fields)
    {
        var typeId = new NodeId(m_nextId++, NamespaceIndex);
        var binaryId = new NodeId(m_nextId++, NamespaceIndex);
        var xmlId = new NodeId(m_nextId++, NamespaceIndex);
        var definition = new StructureDefinition
        {
            StructureType = kind,
            DefaultEncodingId = binaryId,
            BaseDataType = DataTypeIds.Structure,
            Fields = fields
        };
        IComplexTypeFieldBuilder builder = new DefaultComplexTypeFactory()
            .Create(kNamespaceUri, NamespaceIndex)
            .AddStructuredType(new QualifiedName(name, NamespaceIndex), definition);
        builder.AddTypeIdAttribute(
            NodeId.ToExpandedNodeId(typeId, MessageContext.NamespaceUris),
            NodeId.ToExpandedNodeId(binaryId, MessageContext.NamespaceUris),
            NodeId.ToExpandedNodeId(xmlId, MessageContext.NamespaceUris));
        for (int i = 0; i < fields.Count; i++)
        {
            IType? fieldType = TypeInfo.GetSystemType(new ExpandedNodeId(fields[i].DataType), MessageContext.Factory) ??
                TypeInfo.GetSystemType(
                    NodeId.ToExpandedNodeId(fields[i].DataType, MessageContext.NamespaceUris), MessageContext.Factory);
            builder.AddField(fields[i], fieldType, i + 1, kind is
                StructureType.StructureWithSubtypedValues or StructureType.UnionWithSubtypedValues);
        }
        IEncodeableType type = builder.CreateType();
        MessageContext.Factory.Builder.AddEncodeableType(type).Commit();
        return new NativeType(typeId, definition, type);
    }

    public NativeOptionSet RegisterOptionSet(string name, ArrayOf<EnumField> fields)
    {
        var typeId = new NodeId(m_nextId++, NamespaceIndex);
        var binaryId = new NodeId(m_nextId++, NamespaceIndex);
        var xmlId = new NodeId(m_nextId++, NamespaceIndex);
        var definition = new EnumDefinition { IsOptionSet = true, Fields = fields };
        IEncodeableType type = new DefaultComplexTypeFactory()
            .Create(kNamespaceUri, NamespaceIndex)
            .AddOptionSetType(
                new QualifiedName(name, NamespaceIndex),
                NodeId.ToExpandedNodeId(typeId, MessageContext.NamespaceUris),
                NodeId.ToExpandedNodeId(binaryId, MessageContext.NamespaceUris),
                NodeId.ToExpandedNodeId(xmlId, MessageContext.NamespaceUris),
                definition);
        MessageContext.Factory.Builder.AddEncodeableType(type).Commit();
        return new NativeOptionSet(typeId, definition, type);
    }

    public void RegisterOptionSetBaseCodec()
    {
        var type = new Mock<IEncodeableType>(MockBehavior.Strict);
        type.SetupGet(value => value.Type).Returns(typeof(OptionSet));
        type.SetupGet(value => value.XmlName).Returns(new XmlQualifiedName("OptionSet", Namespaces.OpcUaXsd));
        type.Setup(value => value.CreateInstance()).Returns(() => new OptionSet());
        MessageContext.Factory.Builder.AddEncodeableType(type.Object).Commit();
    }

    public ByteString EncodeVariant(Variant value, bool raw = false)
    {
        using var encoder = new BinaryEncoder(MessageContext);
        if (raw)
        {
            encoder.WriteVariantValue(null, value);
        }
        else
        {
            encoder.WriteVariant(null, value);
        }
        return ByteString.From(encoder.CloseAndReturnBuffer());
    }

    public Variant RoundTrip(Variant value, bool raw = false)
    {
        ByteString bytes = EncodeVariant(value, raw);
        using var stream = new MemoryStream(bytes.Span.ToArray());
        using var decoder = new BinaryDecoder(stream, MessageContext);
        return raw ? decoder.ReadVariantValue(null, value.TypeInfo) : decoder.ReadVariant(null);
    }

    public void Invalidate(string change)
    {
        switch (change)
        {
            case "Refresh":
                Service.Refresh();
                break;
            case "Session":
                SessionId = new NodeId(2u);
                break;
            case "Namespaces":
                MessageContext.NamespaceUris.GetIndexOrAppend("urn:unit:test:changed");
                break;
            case "Endpoint":
                Endpoint.EndpointUrl = "opc.tcp://unit.test:4841";
                break;
            case "Server":
                Endpoint.Server.ApplicationUri = "urn:unit:test:replacement";
                break;
            case "Disconnect":
                Connected = false;
                break;
            case "Context":
                MessageContext = ServiceMessageContext.Create(MessageContext.Telemetry);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    public static StructureField Field(
        string name, NodeId dataType, bool optional = false, int rank = ValueRanks.Scalar)
    {
        return new StructureField { Name = name, DataType = dataType, IsOptional = optional, ValueRank = rank };
    }

    public static ReadResponse Reply(Variant value, StatusCode status = default)
    {
        return new ReadResponse
        {
            ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good },
            Results = [new DataValue(value).WithStatus(status)]
        };
    }

    public void Dispose()
    {
        Service.Dispose();
    }

    internal sealed record NativeType(NodeId DataTypeId, StructureDefinition Definition, IEncodeableType Type);

    internal sealed record NativeOptionSet(NodeId DataTypeId, EnumDefinition Definition, IEncodeableType Type);

    private const string kNamespaceUri = "urn:unit:test:structured";
    private uint m_nextId = 1000;
}
