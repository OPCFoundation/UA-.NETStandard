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
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.StructuredValues;
using UaLens.Tests.StructuredValues;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class CompanionInputSchemaGraphTests
    {
        [TestCase(128)]
        [TestCase(129)]
        [TestCase(130)]
        public async Task DefinitionCountStopsBeforeReadingBeyondTheExactBound(int count)
        {
            using var context = new StructuredValueTestContext();
            var definitions = new Dictionary<NodeId, DataTypeDefinition>();
            for (int index = 0; index < count; index++)
            {
                definitions.Add(new NodeId((uint)index + 1000, context.NamespaceIndex),
                    Definition(index + 1 == count ? DataTypeIds.Int32 :
                        new NodeId((uint)index + 1001, context.NamespaceIndex)));
            }
            Mock<IStructuredValueService> values = Service(context, definitions);
            var root = new NodeId(1000u, context.NamespaceIndex);

            if (count == 128)
            {
                CompanionInputSchemaGraph graph = await CompanionInputSchemaGraph.ResolveAsync(
                    root, definitions[root], values.Object, CancellationToken.None).ConfigureAwait(false);
                Assert.That(graph.GetDigest().Length, Is.EqualTo(32));
            }
            else
            {
                await Assert.ThatAsync(() => CompanionInputSchemaGraph.ResolveAsync(
                    root, definitions[root], values.Object, CancellationToken.None),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadEncodingLimitsExceeded)).ConfigureAwait(false);
            }
            values.Verify(value => value.ResolveAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()),
                Times.Exactly(127));
        }

        [Test]
        public async Task CyclicAndRepeatedReferencesAreResolvedOnce()
        {
            using var context = new StructuredValueTestContext();
            var root = new NodeId(1000u, context.NamespaceIndex);
            var child = new NodeId(1001u, context.NamespaceIndex);
            StructureDefinition definition = Definition(child);
            definition.Fields = [definition.Fields[0], StructuredValueTestContext.Field("Again", child)];
            var definitions = new Dictionary<NodeId, DataTypeDefinition>
            {
                [root] = definition,
                [child] = Definition(root)
            };
            Mock<IStructuredValueService> values = Service(context, definitions);

            CompanionInputSchemaGraph graph = await CompanionInputSchemaGraph.ResolveAsync(
                root, definition, values.Object, CancellationToken.None).ConfigureAwait(false);

            Assert.That(graph.GetDigest().Length, Is.EqualTo(32));
            values.Verify(value => value.ResolveAsync(child, It.IsAny<CancellationToken>()), Times.Once);
            values.Verify(value => value.ResolveAsync(root, It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        public async Task DefinitionBytesUseAnAggregateLimitInsteadOfASeparateLimitPerType(int excess)
        {
            using var context = new StructuredValueTestContext();
            context.MessageContext.MaxStringLength = 2 * 1024 * 1024;
            var root = new NodeId(1000u, context.NamespaceIndex);
            var child = new NodeId(1001u, context.NamespaceIndex);
            StructureDefinition parent = Definition(child);
            StructureDefinition nested = Definition(DataTypeIds.Int32);
            parent.Fields[0].Description = new LocalizedText("x");
            nested.Fields[0].Description = new LocalizedText("x");
            int overhead = EncodedLength(parent, context.MessageContext) +
                EncodedLength(nested, context.MessageContext) -
                2;
            int characters = 1048576 + excess - overhead;
            parent.Fields[0].Description = new LocalizedText(new string('p', characters / 2));
            nested.Fields[0].Description = new LocalizedText(new string('n', characters - (characters / 2)));
            Assert.That(EncodedLength(parent, context.MessageContext) + EncodedLength(nested, context.MessageContext),
                Is.EqualTo(1048576 + excess));
            Mock<IStructuredValueService> values = Service(context,
                new Dictionary<NodeId, DataTypeDefinition> { [root] = parent, [child] = nested });

            if (excess <= 0)
            {
                CompanionInputSchemaGraph graph = await CompanionInputSchemaGraph.ResolveAsync(
                    root, parent, values.Object, CancellationToken.None).ConfigureAwait(false);
                Assert.That(graph.GetDigest().Length, Is.EqualTo(32));
            }
            else
            {
                await Assert.ThatAsync(() => CompanionInputSchemaGraph.ResolveAsync(
                    root, parent, values.Object, CancellationToken.None), Throws.TypeOf<ServiceResultException>()
                        .With.Property("StatusCode").EqualTo(StatusCodes.BadEncodingLimitsExceeded))
                            .ConfigureAwait(false);
            }
            values.Verify(value => value.ResolveAsync(child, It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConcreteTypesEmbeddedInVariantFieldsParticipateInTheFingerprint(bool embedded)
        {
            var context = new CompanionStructuredInputTestContext();
            await using (context.ConfigureAwait(false))
            {
                StructuredValueTestContext.NativeType child = context.Values.Register("Child", StructureType.Structure,
                    [StructuredValueTestContext.Field("Counter", DataTypeIds.Int32)]);
                StructuredValueTestContext.NativeType root = context.Values.Register("Root", StructureType.Structure,
                    [StructuredValueTestContext.Field(
                        "Nested", embedded ? DataTypeIds.BaseDataType : child.DataTypeId)]);
                IEncodeable childValue = child.Type.CreateInstance();
                ((IStructure)childValue)["Counter"] = Variant.From(31);
                IEncodeable rootValue = root.Type.CreateInstance();
                ((IStructure)rootValue)["Nested"] = Variant.FromStructure(childValue);
                StructureDefinition nested = CoreUtils.Clone(child.Definition)!;
                context.Values.Reader = (ids, _) => ValueTask.FromResult(StructuredValueTestContext.Reply(
                    Variant.FromStructure(ids[0].NodeId == root.DataTypeId ? root.Definition : nested)));
                context.Offer([context.CustomField(BuiltInType.ExtensionObject, root.DataTypeId)]);
                await context.Tasks.InitializeAsync().ConfigureAwait(false);
                CompanionOperationDraft draft = await context.PrepareAsync(
                    [new("value", Variant.FromStructure(rootValue))]).ConfigureAwait(false);
                nested.Fields[0].Description = new LocalizedText("changed child definition");

                await Assert.ThatAsync(() => context.Tasks.Workspace.ExecuteTaskAsync(draft, false, null),
                    Throws.InvalidOperationException).ConfigureAwait(false);

                Assert.That(context.Values.Reads, Is.EqualTo(4));
                context.Tasks.VerifyCalls(1, 0);
            }
        }

        [TestCase("type")]
        [TestCase("rank")]
        [TestCase("denied")]
        [TestCase("missing")]
        public async Task UnusableNestedDefinitionsNeverReachDomainPreparation(string fault)
        {
            var context = new CompanionStructuredInputTestContext();
            await using (context.ConfigureAwait(false))
            {
                StructuredValueTestContext.NativeType child = context.Values.Register("Child", StructureType.Structure,
                    [StructuredValueTestContext.Field("Counter", DataTypeIds.Int32)]);
                StructuredValueTestContext.NativeType root = context.Values.Register("Root", StructureType.Structure,
                    [StructuredValueTestContext.Field("Nested", child.DataTypeId)]);
                IEncodeable childValue = child.Type.CreateInstance();
                ((IStructure)childValue)["Counter"] = Variant.From(31);
                IEncodeable rootValue = root.Type.CreateInstance();
                ((IStructure)rootValue)["Nested"] = Variant.FromStructure(childValue);
                StructureDefinition nested = CoreUtils.Clone(child.Definition)!;
                if (fault == "type")
                {
                    nested.Fields[0].DataType = DataTypeIds.String;
                }
                if (fault == "rank")
                {
                    nested.Fields[0].ValueRank = ValueRanks.OneDimension;
                }
                context.Values.Reader = (ids, _) => ValueTask.FromResult(
                    ids[0].NodeId == root.DataTypeId
                        ? StructuredValueTestContext.Reply(Variant.FromStructure(root.Definition))
                        : fault == "denied"
                            ? StructuredValueTestContext.Reply(default, StatusCodes.BadUserAccessDenied)
                            : StructuredValueTestContext.Reply(
                                fault == "missing" ? Variant.Null : Variant.FromStructure(nested)));
                context.Offer([context.CustomField(BuiltInType.ExtensionObject, root.DataTypeId)]);
                await context.Tasks.InitializeAsync().ConfigureAwait(false);
                StatusCode expected = fault switch
                {
                    "denied" => StatusCodes.BadUserAccessDenied,
                    "missing" => StatusCodes.BadDataTypeIdUnknown,
                    _ => StatusCodes.BadTypeMismatch
                };

                await Assert.ThatAsync(() => context.PrepareAsync([new("value", Variant.FromStructure(rootValue))]),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(expected))
                    .ConfigureAwait(false);

                context.Tasks.VerifyCalls(0, 0);
            }
        }

        [TestCase(63)]
        [TestCase(64)]
        [TestCase(65)]
        public async Task NestedValueDepthHasAnExactIndependentLimit(int depth)
        {
            using var context = new StructuredValueTestContext();
            context.MessageContext.MaxEncodingNestingLevels = 256;
            var definitions = new Dictionary<NodeId, DataTypeDefinition>();
            StructuredValueTestContext.NativeType type = context.Register("Leaf", StructureType.Structure,
                [StructuredValueTestContext.Field("Counter", DataTypeIds.Int32)]);
            IEncodeable instance = type.Type.CreateInstance();
            ((IStructure)instance)["Counter"] = Variant.From(31);
            definitions.Add(type.DataTypeId, type.Definition);
            for (int index = 1; index < depth; index++)
            {
                StructuredValueTestContext.NativeType parent = context.Register(
                    "Parent" + index, StructureType.Structure,
                    [StructuredValueTestContext.Field("Nested", type.DataTypeId)]);
                IEncodeable value = parent.Type.CreateInstance();
                ((IStructure)value)["Nested"] = Variant.FromStructure(instance);
                definitions.Add(parent.DataTypeId, parent.Definition);
                type = parent;
                instance = value;
            }
            context.Reader = (ids, _) => ValueTask.FromResult(StructuredValueTestContext.Reply(
                Variant.FromStructure(definitions[ids[0].NodeId])));
            CompanionInputSchemaGraph graph = await CompanionInputSchemaGraph.ResolveAsync(
                type.DataTypeId, type.Definition, context.Service, CancellationToken.None).ConfigureAwait(false);

            if (depth <= 64)
            {
                await graph.ValidateAsync(type.DataTypeId, Variant.FromStructure(instance),
                    ValueRanks.Scalar, [], CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(() => graph.ValidateAsync(type.DataTypeId, Variant.FromStructure(instance),
                    ValueRanks.Scalar, [], CancellationToken.None),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadEncodingLimitsExceeded)).ConfigureAwait(false);
            }
            Assert.That(context.Reads, Is.EqualTo(depth - 1));
        }

        [Test]
        public async Task CancellationDuringTheLastEnumDependencyCannotReturnASchema()
        {
            using var context = new StructuredValueTestContext();
            var root = new NodeId(1000u, context.NamespaceIndex);
            var child = new NodeId(1001u, context.NamespaceIndex);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reply = new TaskCompletionSource<DataTypeDefinition?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var values = new Mock<IStructuredValueService>(MockBehavior.Strict);
            values.SetupGet(value => value.MessageContext).Returns(context.MessageContext);
            values.Setup(value => value.ResolveAsync(child, It.IsAny<CancellationToken>())).Returns(() =>
            {
                entered.SetResult();
                return reply.Task;
            });
            using var cancellation = new CancellationTokenSource();
            Task<CompanionInputSchemaGraph> pending = CompanionInputSchemaGraph.ResolveAsync(
                root, Definition(child), values.Object, cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            reply.SetResult(CompanionStructuredInputTestContext.Enumeration());

            await Assert.ThatAsync(() => pending.WaitAsync(TimeSpan.FromSeconds(10)),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        }

        [Test]
        public async Task CapturedDefinitionsAreIndependentOfTheResolversMutableObjects()
        {
            using var context = new StructuredValueTestContext();
            StructuredValueTestContext.NativeType type = context.Register("Settings", StructureType.Structure,
                [StructuredValueTestContext.Field("Counter", DataTypeIds.Int32)]);
            IEncodeable instance = type.Type.CreateInstance();
            ((IStructure)instance)["Counter"] = Variant.From(31);
            StructureDefinition definition = CoreUtils.Clone(type.Definition)!;
            CompanionInputSchemaGraph graph = await CompanionInputSchemaGraph.ResolveAsync(
                type.DataTypeId, definition, context.Service, CancellationToken.None).ConfigureAwait(false);
            ByteString digest = graph.GetDigest();
            definition.Fields[0].Name = "ChangedByResolver";

            await graph.ValidateAsync(type.DataTypeId, Variant.FromStructure(instance),
                ValueRanks.Scalar, [], CancellationToken.None).ConfigureAwait(false);

            Assert.That(graph.GetDigest(), Is.EqualTo(digest));
            Assert.That(CompanionInputContract.SchemaDigest(definition, context.MessageContext),
                Is.Not.EqualTo(digest));
            Assert.That(context.Reads, Is.Zero);
        }

        private static StructureDefinition Definition(NodeId child)
        {
            return new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields = [StructuredValueTestContext.Field("Child", child)]
            };
        }

        private static Mock<IStructuredValueService> Service(
            StructuredValueTestContext context, Dictionary<NodeId, DataTypeDefinition> definitions)
        {
            var values = new Mock<IStructuredValueService>(MockBehavior.Strict);
            values.SetupGet(value => value.MessageContext).Returns(context.MessageContext);
            values.Setup(value => value.ResolveAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId id, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return Task.FromResult<DataTypeDefinition?>(definitions[id]);
                });
            return values;
        }

        private static int EncodedLength(DataTypeDefinition definition, IServiceMessageContext context)
        {
            using var encoder = new BinaryEncoder(context);
            encoder.WriteVariant(null, Variant.FromStructure(definition));
            return encoder.CloseAndReturnBuffer()!.Length;
        }
    }
}
