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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Schema;
using UaLens.Plugins.Models;
using UaLens.StructuredValues;

namespace UaLens.Tests.StructuredValues;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class ModelInspectorBackendTests
{
    [Test]
    public async Task BindAndReleaseDoNotReadOrDisposeThePrimarySessionAsync()
    {
        using var context = new StructuredValueTestContext();
        var backend = new SessionModelInspectorBackend(context.Service);
        await using (backend.ConfigureAwait(false))
        {
            await backend.BindAsync(context.Session.Object, CancellationToken.None).ConfigureAwait(false);
            Assert.That(backend.IsBound, Is.True);
            await backend.BindAsync(null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(backend.IsBound, Is.False);
            Assert.That(backend.Session, Is.Null);
            Assert.That(context.Reads, Is.Zero);
            context.Session.Verify(value => value.Dispose(), Times.Never);
        }
    }

    [Test]
    public async Task VariableReadReturnsTypedEvidenceAndExplicitNoSchemaAsync()
    {
        using var context = new StructuredValueTestContext();
        ConfigureVariableReads(context);
        var backend = new SessionModelInspectorBackend(context.Service);
        await using (backend.ConfigureAwait(false))
        {
            await backend.BindAsync(context.Session.Object, CancellationToken.None).ConfigureAwait(false);
            ModelInspection inspection = await backend.ReadAsync("i=2258", false, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(inspection.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(inspection.DataType, Is.EqualTo(DataTypeIds.Int32));
            Assert.That(inspection.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(inspection.Value.WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(7));
            Assert.That(inspection.CanWrite, Is.True);
            Assert.That(inspection.CanCall, Is.False);
            Assert.That(inspection.Definition, Is.Null);
            ModelSchemaPreview schema = await backend.CreateSchemaAsync(
                inspection, UaSchemaFormat.JsonCompact, CancellationToken.None).ConfigureAwait(false);
            Assert.That(schema.Available, Is.False);
            Assert.That(schema.Text, Does.Contain("No authoritative"));
        }
    }

    [Test]
    public async Task ExplicitWriteRechecksMetadataAndRejectsWrongTypesBeforeSendingAsync()
    {
        using var context = new StructuredValueTestContext();
        ConfigureVariableReads(context);
        context.Session.SetupGet(value => value.TypeTree).Returns(new TypeTable(context.MessageContext.NamespaceUris));
        ArrayOf<WriteValue> sent = default;
        context.Session.Setup(value => value.WriteAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
            .Callback((RequestHeader? _, ArrayOf<WriteValue> values, CancellationToken _) => sent = values)
            .ReturnsAsync(new WriteResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = [StatusCodes.Good]
            });
        var backend = new SessionModelInspectorBackend(context.Service);
        await using (backend.ConfigureAwait(false))
        {
            await backend.BindAsync(context.Session.Object, CancellationToken.None).ConfigureAwait(false);
            ModelInspection inspection = await backend.ReadAsync("i=2258", false, CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.ThatAsync(() => backend.WriteAsync(
                inspection, Variant.From("wrong type"), CancellationToken.None),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
            Assert.That(sent.IsNull, Is.True);
            StatusCode status = await backend.WriteAsync(inspection, Variant.From(42), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(status), Is.True);
            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0].NodeId, Is.EqualTo(inspection.NodeId));
            Assert.That(sent[0].Value.WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(42));
            Assert.That(inspection.Value.WrappedValue.TryGetValue(out int original), Is.True);
            Assert.That(original, Is.EqualTo(7));
            context.Session.Verify(value => value.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task SessionRecreationInvalidatesWriteEvidenceBeforeAnyServiceCallAsync()
    {
        using var context = new StructuredValueTestContext();
        ConfigureVariableReads(context);
        var backend = new SessionModelInspectorBackend(context.Service);
        await using (backend.ConfigureAwait(false))
        {
            await backend.BindAsync(context.Session.Object, CancellationToken.None).ConfigureAwait(false);
            ModelInspection inspection = await backend.ReadAsync("i=2258", false, CancellationToken.None)
                .ConfigureAwait(false);
            int reads = context.Reads;
            context.SessionId = new NodeId(2u);

            await Assert.ThatAsync(() => backend.WriteAsync(inspection, Variant.From(42), CancellationToken.None),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
            Assert.That(context.Reads, Is.EqualTo(reads));
            context.Session.Verify(value => value.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task MissingNestedDefinitionCannotBecomeAnOpaqueSuccessSchemaAsync()
    {
        using var context = new StructuredValueTestContext();
        var nested = new NodeId(6001u, context.NamespaceIndex);
        var definition = new StructureDefinition
        {
            Fields = [StructuredValueTestContext.Field("Nested", nested)]
        };
        var values = new Mock<IStructuredValueService>(MockBehavior.Strict);
        values.SetupGet(value => value.MessageContext).Returns(context.MessageContext);
        values.Setup(value => value.Refresh());
        values.Setup(value => value.ResolveAsync(nested, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataTypeDefinition?)null);
        var schema = new Mock<ISchemaProvider>(MockBehavior.Strict);
        var backend = new SessionModelInspectorBackend(values.Object, schema.Object);
        await using (backend.ConfigureAwait(false))
        {
            await backend.BindAsync(context.Session.Object, CancellationToken.None).ConfigureAwait(false);
            ModelInspection inspection = SchemaInspection(context, definition);
            ModelSchemaPreview result = await backend.CreateSchemaAsync(
                inspection, UaSchemaFormat.JsonCompact, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Available, Is.False);
            Assert.That(result.Text, Does.Contain("Nested"));
            schema.Verify(value => value.CreateSchema(
                It.IsAny<UaTypeDescription>(), It.IsAny<UaSchemaFormat>(), It.IsAny<UaSchemaScope>()), Times.Never);
        }
    }

    [Test]
    public async Task DefaultSchemaProviderIncludesResolvedNestedDefinitionsAsync()
    {
        using var context = new StructuredValueTestContext();
        var nested = new NodeId(6001u, context.NamespaceIndex);
        var definition = new StructureDefinition
        {
            Fields = [StructuredValueTestContext.Field("Nested", nested)]
        };
        var nestedDefinition = new StructureDefinition
        {
            Fields = [StructuredValueTestContext.Field("Number", DataTypeIds.Int32)]
        };
        var values = new Mock<IStructuredValueService>(MockBehavior.Strict);
        values.SetupGet(value => value.MessageContext).Returns(context.MessageContext);
        values.Setup(value => value.Refresh());
        values.Setup(value => value.ResolveAsync(nested, It.IsAny<CancellationToken>()))
            .ReturnsAsync(nestedDefinition);
        context.Reader = (ids, _) =>
        {
            Assert.That(ids.Count, Is.EqualTo(1));
            Assert.That(ids[0].NodeId, Is.EqualTo(nested));
            Assert.That(ids[0].AttributeId, Is.EqualTo(Attributes.BrowseName));
            return ValueTask.FromResult(Reply(Variant.From(new QualifiedName("NestedType", context.NamespaceIndex))));
        };
        var backend = new SessionModelInspectorBackend(values.Object);
        await using (backend.ConfigureAwait(false))
        {
            await backend.BindAsync(context.Session.Object, CancellationToken.None).ConfigureAwait(false);
            ModelSchemaPreview result = await backend.CreateSchemaAsync(
                SchemaInspection(context, definition), UaSchemaFormat.JsonCompact, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.Available, Is.True);
            Assert.That(result.Text, Does.Contain("NestedType"));
            Assert.That(result.Text, Does.Contain("Number"));
            Assert.That(result.MediaType, Does.Contain("json"));
            Assert.That(result.Extension, Is.EqualTo("json"));
        }
    }

    [TestCase(UaSchemaFormat.JsonCompact)]
    [TestCase(UaSchemaFormat.JsonVerbose)]
    [TestCase(UaSchemaFormat.Xsd)]
    [TestCase(UaSchemaFormat.Bsd)]
    public async Task StructureWithNumberValueGeneratesSchemaAsync(UaSchemaFormat format)
    {
        using var context = new StructuredValueTestContext();
        var definition = new StructureDefinition
        {
            BaseDataType = DataTypeIds.Structure,
            Fields =
            [
                StructuredValueTestContext.Field("NumberValue", DataTypeIds.Number),
                StructuredValueTestContext.Field("IntegerValue", DataTypeIds.Integer),
                StructuredValueTestContext.Field("UIntegerValue", DataTypeIds.UInteger)
            ]
        };
        var backend = new SessionModelInspectorBackend(context.Service);
        await using (backend.ConfigureAwait(false))
        {
            await backend.BindAsync(context.Session.Object, CancellationToken.None).ConfigureAwait(false);

            ModelSchemaPreview schema = await backend.CreateSchemaAsync(
                SchemaInspection(context, definition), format, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(schema.Available, Is.True, schema.Text);
            Assert.That(schema.Text, Does.Contain("NumberValue"));
            Assert.That(schema.Text, Does.Contain("IntegerValue"));
            Assert.That(schema.Text, Does.Contain("UIntegerValue"));
            Assert.That(context.Reads, Is.Zero, "Standard abstract numeric types do not need server definitions.");
            if (format is UaSchemaFormat.JsonCompact or UaSchemaFormat.JsonVerbose)
            {
                JsonNode document = JsonNode.Parse(schema.Text)!;
                JsonNode properties = document["$defs"]!["Root"]!["properties"]!;
                Assert.That(properties["NumberValue"]!["$ref"]!.GetValue<string>(), Is.EqualTo("#/$defs/Ua_Variant"));
                Assert.That(properties["IntegerValue"]!["$ref"]!.GetValue<string>(), Is.EqualTo("#/$defs/Ua_Variant"));
                Assert.That(properties["UIntegerValue"]!["$ref"]!.GetValue<string>(), Is.EqualTo("#/$defs/Ua_Variant"));
                Assert.That(schema.MediaType, Is.EqualTo("application/schema+json"));
                Assert.That(schema.Extension, Is.EqualTo("json"));
            }
            else
            {
                Assert.That(schema.MediaType, Is.EqualTo("application/xml"));
                Assert.That(schema.Extension, Is.EqualTo(format == UaSchemaFormat.Xsd ? "xsd" : "bsd"));
            }
        }
    }

    [TestCase(26u)]
    [TestCase(27u)]
    [TestCase(28u)]
    public async Task NonstandardNumericIdentifiersStillRequireDefinitionsAsync(uint identifier)
    {
        using var context = new StructuredValueTestContext();
        var customType = new NodeId(identifier, context.NamespaceIndex);
        var definition = new StructureDefinition
        {
            Fields = [StructuredValueTestContext.Field("CustomNumber", customType)]
        };
        context.Reader = (ids, _) =>
        {
            Assert.That(ids.Count, Is.EqualTo(1));
            Assert.That(ids[0].NodeId, Is.EqualTo(customType));
            Assert.That(ids[0].AttributeId, Is.EqualTo(Attributes.DataTypeDefinition));
            return ValueTask.FromResult(StructuredValueTestContext.Reply(
                Variant.Null, StatusCodes.BadAttributeIdInvalid));
        };
        var backend = new SessionModelInspectorBackend(context.Service);
        await using (backend.ConfigureAwait(false))
        {
            await backend.BindAsync(context.Session.Object, CancellationToken.None).ConfigureAwait(false);

            ModelSchemaPreview schema = await backend.CreateSchemaAsync(
                SchemaInspection(context, definition), UaSchemaFormat.JsonCompact, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(schema.Available, Is.False);
            Assert.That(schema.Text, Does.Contain($"'CustomNumber' has no definition for {customType}"));
            Assert.That(context.Reads, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task CanceledNestedLookupDoesNotReturnSchemaUnavailableAsync()
    {
        using var context = new StructuredValueTestContext();
        using var cancellation = new CancellationTokenSource();
        var nested = new NodeId(6001u, context.NamespaceIndex);
        var definition = new StructureDefinition
        {
            Fields = [StructuredValueTestContext.Field("Nested", nested)]
        };
        var values = new Mock<IStructuredValueService>(MockBehavior.Strict);
        values.SetupGet(value => value.MessageContext).Returns(context.MessageContext);
        values.Setup(value => value.Refresh());
        values.Setup(value => value.ResolveAsync(nested, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromResult<DataTypeDefinition?>(null);
            });
        var backend = new SessionModelInspectorBackend(values.Object);
        await using (backend.ConfigureAwait(false))
        {
            await backend.BindAsync(context.Session.Object, CancellationToken.None).ConfigureAwait(false);
            await Assert.ThatAsync(() => backend.CreateSchemaAsync(
                SchemaInspection(context, definition), UaSchemaFormat.JsonCompact, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task MatrixWriteRechecksDimensionMetadataAndRetainsTheInspectedSnapshotAsync()
    {
        using var context = new StructuredValueTestContext();
        ArrayOf<uint> dimensions = [2u, 3u];
        ConfigureVariableReads(context, 2, () => dimensions);
        context.Session.SetupGet(value => value.TypeTree).Returns(new TypeTable(context.MessageContext.NamespaceUris));
        ArrayOf<WriteValue> sent = default;
        context.Session.Setup(value => value.WriteAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
            .Callback((RequestHeader? _, ArrayOf<WriteValue> values, CancellationToken _) => sent = values)
            .ReturnsAsync(new WriteResponse { ResponseHeader = new ResponseHeader(), Results = [StatusCodes.Good] });
        var backend = new SessionModelInspectorBackend(context.Service);
        await using (backend.ConfigureAwait(false))
        {
            await backend.BindAsync(context.Session.Object, CancellationToken.None).ConfigureAwait(false);
            ModelInspection inspection = await backend.ReadAsync("i=2258", false, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(inspection.ArrayDimensions, Is.EqualTo((ArrayOf<uint>)[2u, 3u]));
            dimensions = [3u, 2u];
            Variant candidate = Variant.From(((ArrayOf<int>)[8, 9, 10]).ToMatrix([1, 3]));

            await Assert.ThatAsync(() => backend.WriteAsync(inspection, candidate, CancellationToken.None),
                Throws.TypeOf<ServiceResultException>().With.Message.Contains("dimensions changed"))
                .ConfigureAwait(false);
            Assert.That(sent.IsNull, Is.True);
            Assert.That(inspection.ArrayDimensions, Is.EqualTo((ArrayOf<uint>)[2u, 3u]));

            dimensions = [2u, 3u];
            Assert.That(await backend.WriteAsync(inspection, candidate, CancellationToken.None).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.Good));
            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0].NodeId, Is.EqualTo(inspection.NodeId));
            Assert.That(sent[0].Value.WrappedValue, Is.EqualTo(candidate));
            context.Session.Verify(value => value.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task MatrixExceedingDeclaredDimensionsIsRejectedBeforeWriteAsync()
    {
        using var context = new StructuredValueTestContext();
        ConfigureVariableReads(context, 2, () => (ArrayOf<uint>)[2u, 3u]);
        var backend = new SessionModelInspectorBackend(context.Service);
        await using (backend.ConfigureAwait(false))
        {
            await backend.BindAsync(context.Session.Object, CancellationToken.None).ConfigureAwait(false);
            ModelInspection inspection = await backend.ReadAsync("i=2258", false, CancellationToken.None)
                .ConfigureAwait(false);
            Variant oversized = Variant.From(((ArrayOf<int>)[1, 2, 3, 4, 5, 6]).ToMatrix([3, 2]));

            await Assert.ThatAsync(() => backend.WriteAsync(inspection, oversized, CancellationToken.None),
                Throws.TypeOf<ServiceResultException>().With.Message.Contains("declared maximum"))
                .ConfigureAwait(false);
            context.Session.Verify(value => value.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InvalidOrDeniedDimensionMetadataCannotBecomeUnboundedArrayEvidenceAsync(bool denied)
    {
        using var context = new StructuredValueTestContext();
        ConfigureVariableReads(context, 2);
        Func<ArrayOf<ReadValueId>, CancellationToken, ValueTask<ReadResponse>> reader = context.Reader;
        context.Reader = (ids, token) => ids[0].AttributeId == Attributes.ArrayDimensions
            ? ValueTask.FromResult(StructuredValueTestContext.Reply(
                Variant.From("invalid dimensions"), denied ? StatusCodes.BadUserAccessDenied : StatusCodes.Good))
            : reader(ids, token);
        var backend = new SessionModelInspectorBackend(context.Service);
        await using (backend.ConfigureAwait(false))
        {
            await backend.BindAsync(context.Session.Object, CancellationToken.None).ConfigureAwait(false);
            await Assert.ThatAsync(() => backend.ReadAsync("i=2258", false, CancellationToken.None),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(denied ? StatusCodes.BadUserAccessDenied : StatusCodes.BadDecodingError))
                .ConfigureAwait(false);
            context.Session.Verify(value => value.WriteAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    private static void ConfigureVariableReads(
        StructuredValueTestContext context,
        int valueRank = ValueRanks.Scalar,
        Func<ArrayOf<uint>>? dimensions = null)
    {
        context.Reader = (ids, _) => ValueTask.FromResult(ids[0].AttributeId switch
        {
            Attributes.NodeClass => Reply(
                Variant.From((int)NodeClass.Variable),
                Variant.From(new LocalizedText("Test value")),
                Variant.From(new QualifiedName("TestValue"))),
            Attributes.Value => Reply(
                valueRank == ValueRanks.Scalar
                    ? Variant.From(7)
                    : Variant.From(((ArrayOf<int>)[1, 2, 3, 4, 5, 6]).ToMatrix([2, 3])),
                Variant.From(DataTypeIds.Int32),
                Variant.From(valueRank),
                Variant.From((byte)AccessLevels.CurrentWrite)),
            Attributes.DataType => Reply(
                Variant.From(DataTypeIds.Int32),
                Variant.From(valueRank),
                Variant.From((byte)AccessLevels.CurrentWrite)),
            Attributes.ArrayDimensions => Reply(Variant.From(dimensions?.Invoke() ?? ArrayOf<uint>.Empty)),
            Attributes.BrowseName => Reply(Variant.From(new QualifiedName("Int32"))),
            Attributes.DataTypeDefinition => StructuredValueTestContext.Reply(
                Variant.Null, StatusCodes.BadAttributeIdInvalid),
            _ => throw new InvalidOperationException("Unexpected attribute request.")
        });
    }

    private static ReadResponse Reply(params Variant[] values)
    {
        var data = new DataValue[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            data[i] = new DataValue(values[i]);
        }
        return new ReadResponse { ResponseHeader = new ResponseHeader(), Results = data };
    }

    private static ModelInspection SchemaInspection(StructuredValueTestContext context, StructureDefinition definition)
    {
        var dataType = new NodeId(6000u, context.NamespaceIndex);
        return new ModelInspection(
            1, context.SessionId, context.MessageContext.NamespaceUris.ToArray(),
            NodeId.ToExpandedNodeId(dataType, context.MessageContext.NamespaceUris).ToString(),
            dataType, "Root", NodeClass.DataType, dataType, new QualifiedName("Root", context.NamespaceIndex),
            ValueRanks.Scalar, default, false, false, definition);
    }
}
