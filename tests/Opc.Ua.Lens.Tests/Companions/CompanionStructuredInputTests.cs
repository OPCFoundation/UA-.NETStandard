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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Connection;
using UaLens.Plugins.Companions;
using UaLens.Tests.StructuredValues;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class CompanionStructuredInputTests
    {
        [TestCaseSource(nameof(ScalarCases))]
        public async Task ScalarEditorAndPreparationRetainExactValues(
            BuiltInType type, string text, Variant expected)
        {
            var context = new CompanionStructuredInputTestContext();
            await using (context.ConfigureAwait(false))
            {
                CompanionInputDefinition definition = CompanionStructuredInputTestContext.Field(type);
                context.Offer([definition]);
                await context.Tasks.InitializeAsync().ConfigureAwait(false);
                int changes = 0;
                var editor = new CompanionInputEditor(definition, () => changes++) { Text = text };
                int beforeCapture = changes;

                CompanionValue captured = editor.Capture();
                CompanionOperationDraft draft = await context.PrepareAsync([captured]).ConfigureAwait(false);

                Assert.That(editor.IsText, Is.True);
                Assert.That(editor.RequiresEditor, Is.False);
                Assert.That(editor.Text, Is.EqualTo(text));
                Assert.That(changes, Is.EqualTo(beforeCapture));
                Assert.That(captured.Name, Is.EqualTo("value"));
                Assert.That(captured.Value.TypeInfo.BuiltInType, Is.EqualTo(type));
                Assert.That(captured.Value.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(captured.Value, Is.EqualTo(expected));
                Assert.That(context.Tasks.CapturedInputs[0].Value, Is.EqualTo(expected));
                Assert.That(draft.Inputs[0].Value, Is.EqualTo(expected));
                Assert.That(draft.InputSchemas.Count, Is.Zero);
                context.Tasks.VerifyCalls(1, 0);
                Assert.That(context.Values.Reads, Is.Zero);
            }
        }

        [TestCase("Null")]
        [TestCase("Empty")]
        [TestCase("Singleton")]
        [TestCase("Many")]
        [TestCase("Matrix")]
        public async Task PreparationRetainsPrimitiveArrayShapeAndValues(string shape)
        {
            var context = new CompanionStructuredInputTestContext();
            await using (context.ConfigureAwait(false))
            {
                bool matrix = shape == "Matrix";
                ArrayOf<int> expected = shape switch
                {
                    "Null" => ArrayOf<int>.Null,
                    "Empty" => [],
                    "Singleton" => [int.MinValue],
                    _ => [17, -4, 90, int.MaxValue, 0, 6]
                };
                Variant value = matrix ? Variant.From(expected.ToMatrix([2, 3])) : Variant.From(expected);
                context.Offer([CompanionStructuredInputTestContext.Field(BuiltInType.Int32) with
                {
                    ValueRank = matrix ? 2 : ValueRanks.OneDimension,
                    ArrayDimensions = matrix ? [2u, 3u] : [8u]
                }]);
                await context.Tasks.InitializeAsync().ConfigureAwait(false);

                CompanionOperationDraft draft = await context.PrepareAsync([new("value", value)])
                    .ConfigureAwait(false);
                CompanionOperationResult result = await context.Tasks.Workspace.ExecuteTaskAsync(draft, false, null)
                    .ConfigureAwait(false);

                AssertIntegers(context.Tasks.CapturedInputs[0].Value, expected, matrix);
                AssertIntegers(draft.Inputs[0].Value, expected, matrix);
                Assert.That(result.Summary, Is.EqualTo("Typed operation completed."));
                Assert.That(result.Values[0].Value, Is.EqualTo(Variant.From(19u)));
                context.Tasks.VerifyCalls(1, 1);
                context.Tasks.VerifyInspections(3);
                Assert.That(context.Values.Reads, Is.Zero);
            }
        }

        [Test]
        public async Task QueuedPreparationIsolatesCallerAndProviderArrayStorage()
        {
            var context = new CompanionStructuredInputTestContext();
            await using (context.ConfigureAwait(false))
            {
                context.Offer([CompanionStructuredInputTestContext.Field(BuiltInType.Int32) with
                {
                    ValueRank = ValueRanks.OneDimension
                }]);
                await context.Tasks.InitializeAsync().ConfigureAwait(false);
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                int inspections = 0;
                context.Tasks.Provider.Setup(value => value.InspectAsync(
                        It.IsAny<CompanionContext>(), context.Tasks.Target, It.IsAny<CancellationToken>()))
                    .Returns(async (CompanionContext _, CompanionTarget _, CancellationToken token) =>
                    {
                        if (++inspections == 1)
                        {
                            entered.TrySetResult();
                            await release.Task.WaitAsync(token).ConfigureAwait(false);
                        }
                        return context.Tasks.Inspection;
                    });
                context.Tasks.PrepareHandler = (fields, _) =>
                {
                    AssertIntegers(fields[0].Value, [17, -4, 90]);
                    OverwriteFirst(fields[0].Value, -800);
                    return ValueTask.FromResult(context.Tasks.PreparedInput);
                };
                Task<CompanionInspection> inspecting = context.Tasks.Workspace.InspectAsync(context.Tasks.Target);
                Task<CompanionOperationDraft>? preparing = null;
                int[] callerStorage = [17, -4, 90];
                try
                {
                    await Task.WhenAny(entered.Task, inspecting).ConfigureAwait(false);
                    Assert.That(entered.Task.IsCompletedSuccessfully, Is.True);
                    preparing = context.PrepareAsync([new("value", Variant.From(ArrayOf.Wrapped(callerStorage)))]);
                    Assert.That(preparing.IsCompleted, Is.False);
                    callerStorage[0] = 900;
                    context.Tasks.VerifyCalls(0, 0);
                }
                finally
                {
                    release.TrySetResult();
                }
                await inspecting.ConfigureAwait(false);
                CompanionOperationDraft draft = await (preparing ??
                    throw new AssertionException("The queued preparation did not start.")).ConfigureAwait(false);

                Assert.That(callerStorage, Is.EqualTo<int[]>([900, -4, 90]));
                AssertIntegers(context.Tasks.CapturedInputs[0].Value, [-800, -4, 90]);
                AssertIntegers(draft.Inputs[0].Value, [17, -4, 90]);
                OverwriteFirst(draft.Inputs[0].Value, 700);
                AssertIntegers(draft.Inputs[0].Value, [17, -4, 90]);
                await context.Tasks.Workspace.ExecuteTaskAsync(draft, false, null).ConfigureAwait(false);
                context.Tasks.VerifyCalls(1, 1);
                Assert.That(inspections, Is.EqualTo(3));
                Assert.That(context.Values.Reads, Is.Zero);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CustomDefinitionIsReadAndPinnedUntilExecution(bool structure)
        {
            var context = new CompanionStructuredInputTestContext();
            await using (context.ConfigureAwait(false))
            {
                NodeId typeId;
                DataTypeDefinition definition;
                Variant value;
                if (structure)
                {
                    StructuredValueTestContext.NativeType type = context.Values.Register(
                        "TaskSettings", StructureType.Structure,
                        [StructuredValueTestContext.Field("Counter", DataTypeIds.Int64),
                            StructuredValueTestContext.Field("Label", DataTypeIds.String)]);
                    typeId = type.DataTypeId;
                    definition = type.Definition;
                    IEncodeable instance = type.Type.CreateInstance();
                    ((IStructure)instance)["Counter"] = Variant.From(long.MinValue);
                    ((IStructure)instance)["Label"] = Variant.From("unchanged neighbor");
                    value = Variant.FromStructure(instance);
                }
                else
                {
                    typeId = new NodeId(4200u, context.Values.NamespaceIndex);
                    definition = CompanionStructuredInputTestContext.Enumeration();
                    value = Variant.From(7);
                }
                context.Offer([context.CustomField(
                    structure ? BuiltInType.ExtensionObject : BuiltInType.Enumeration, typeId)]);
                context.ReturnDefinition(typeId, definition);
                await context.Tasks.InitializeAsync().ConfigureAwait(false);
                ByteString digest = CompanionInputContract.SchemaDigest(definition, context.Values.MessageContext);

                CompanionOperationDraft draft = await context.PrepareAsync([new("value", value)])
                    .ConfigureAwait(false);

                Assert.That(draft.InputSchemas.Count, Is.EqualTo(1));
                Assert.That(draft.InputSchemas[0].Name, Is.EqualTo("value"));
                Assert.That(draft.InputSchemas[0].Digest, Is.EqualTo(digest));
                Assert.That(digest.Length, Is.EqualTo(32));
                Assert.That(context.Values.Reads, Is.EqualTo(1));
                if (structure)
                {
                    Assert.That(draft.Inputs[0].Value.TryGetValue<Opc.Ua.Encoders.Structure>(
                        out Opc.Ua.Encoders.Structure? result, context.Values.MessageContext), Is.True);
                    Assert.That(result!["Counter"], Is.EqualTo(Variant.From(long.MinValue)));
                    Assert.That(result["Label"], Is.EqualTo(Variant.From("unchanged neighbor")));
                    Assert.That(context.Tasks.CapturedInputs[0].Value, Is.EqualTo(value));
                }
                else
                {
                    Assert.That(draft.Inputs[0].Value.TryGetValue(out int number), Is.True);
                    Assert.That(number, Is.EqualTo(7));
                    Assert.That(context.Tasks.CapturedInputs[0].Value, Is.EqualTo(Variant.From(7)));
                }
                await context.Tasks.Workspace.ExecuteTaskAsync(draft, false, null).ConfigureAwait(false);
                Assert.That(context.Values.Reads, Is.EqualTo(2));
                context.Tasks.VerifyInspections(3);
                context.Tasks.VerifyCalls(1, 1);
            }
        }

        [Test]
        public async Task NestedDefinitionChangeInvalidatesPreparationWithoutDispatch()
        {
            var context = new CompanionStructuredInputTestContext();
            await using (context.ConfigureAwait(false))
            {
                StructuredValueTestContext.NativeType child = context.Values.Register(
                    "NestedSettings", StructureType.Structure,
                    [StructuredValueTestContext.Field("Counter", DataTypeIds.Int32)]);
                StructuredValueTestContext.NativeType parent = context.Values.Register(
                    "ParentSettings", StructureType.Structure,
                    [StructuredValueTestContext.Field("Nested", child.DataTypeId)]);
                IEncodeable childValue = child.Type.CreateInstance();
                ((IStructure)childValue)["Counter"] = Variant.From(31);
                IEncodeable parentValue = parent.Type.CreateInstance();
                ((IStructure)parentValue)["Nested"] = Variant.FromStructure(childValue);
                StructureDefinition childDefinition = CoreUtils.Clone(child.Definition)!;
                context.Values.Reader = (ids, _) =>
                {
                    Assert.That(ids, Has.Count.EqualTo(1));
                    Assert.That(ids[0].AttributeId, Is.EqualTo(Attributes.DataTypeDefinition));
                    DataTypeDefinition definition = ids[0].NodeId == parent.DataTypeId
                        ? parent.Definition : ids[0].NodeId == child.DataTypeId
                            ? childDefinition : throw new AssertionException("An unrelated type was requested.");
                    return ValueTask.FromResult(StructuredValueTestContext.Reply(Variant.FromStructure(definition)));
                };
                context.Offer([context.CustomField(BuiltInType.ExtensionObject, parent.DataTypeId)]);
                await context.Tasks.InitializeAsync().ConfigureAwait(false);
                CompanionOperationDraft draft = await context.PrepareAsync(
                    [new("value", Variant.FromStructure(parentValue))]).ConfigureAwait(false);
                childDefinition.Fields[0].Description = new LocalizedText("Revised nested contract");

                await Assert.ThatAsync(() => context.Tasks.Workspace.ExecuteTaskAsync(draft, false, null),
                    Throws.InvalidOperationException).ConfigureAwait(false);

                context.Tasks.VerifyCalls(1, 0);
            }
        }

        [TestCase("Missing")]
        [TestCase("Denied")]
        [TestCase("Malformed")]
        public async Task CustomDefinitionFailuresNeverReachProvider(string failure)
        {
            var context = new CompanionStructuredInputTestContext();
            await using (context.ConfigureAwait(false))
            {
                var typeId = new NodeId(4200u, context.Values.NamespaceIndex);
                context.Offer([context.CustomField(BuiltInType.Enumeration, typeId)]);
                StatusCode expected = failure switch
                {
                    "Missing" => StatusCodes.BadDataTypeIdUnknown,
                    "Denied" => StatusCodes.BadUserAccessDenied,
                    _ => StatusCodes.BadDecodingError
                };
                context.Values.Reader = (ids, _) =>
                {
                    CompanionStructuredInputTestContext.AssertDefinitionRead(ids, typeId);
                    return ValueTask.FromResult(failure switch
                    {
                        "Missing" => StructuredValueTestContext.Reply(Variant.Null),
                        "Denied" => StructuredValueTestContext.Reply(Variant.Null, StatusCodes.BadUserAccessDenied),
                        _ => StructuredValueTestContext.Reply(Variant.From(19))
                    });
                };
                await context.Tasks.InitializeAsync().ConfigureAwait(false);

                await Assert.ThatAsync(() => context.PrepareAsync([new("value", Variant.From(7))]),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(expected))
                    .ConfigureAwait(false);

                Assert.That(context.Values.Reads, Is.EqualTo(1));
                context.Tasks.VerifyCalls(0, 0);
            }
        }

        [Test]
        public async Task CustomStructureMustMatchTheOfferedDataType()
        {
            var context = new CompanionStructuredInputTestContext();
            await using (context.ConfigureAwait(false))
            {
                ArrayOf<StructureField> fields = [StructuredValueTestContext.Field("Counter", DataTypeIds.Int32)];
                StructuredValueTestContext.NativeType offered = context.Values.Register(
                    "OfferedSettings", StructureType.Structure, fields);
                StructuredValueTestContext.NativeType other = context.Values.Register(
                    "OtherSettings", StructureType.Structure, fields);
                IEncodeable instance = other.Type.CreateInstance();
                ((IStructure)instance)["Counter"] = Variant.From(31);
                context.Offer([context.CustomField(BuiltInType.ExtensionObject, offered.DataTypeId)]);
                context.ReturnDefinition(offered.DataTypeId, offered.Definition);
                await context.Tasks.InitializeAsync().ConfigureAwait(false);

                await Assert.ThatAsync(() => context.PrepareAsync([new("value", Variant.FromStructure(instance))]),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadTypeMismatch))
                    .ConfigureAwait(false);

                Assert.That(((IStructure)instance)["Counter"], Is.EqualTo(Variant.From(31)));
                Assert.That(context.Values.Reads, Is.EqualTo(1));
                context.Tasks.VerifyCalls(0, 0);
            }
        }

        [TestCase("Accepted")]
        [TestCase("Prepared")]
        [TestCase("Direct")]
        public async Task SchemaChangeAfterAcceptanceOrPreparationRejectsWithoutDispatch(string stage)
        {
            var context = new CompanionStructuredInputTestContext();
            await using (context.ConfigureAwait(false))
            {
                bool afterPreparation = stage != "Accepted";
                var typeId = new NodeId(4200u, context.Values.NamespaceIndex);
                EnumDefinition definition = CompanionStructuredInputTestContext.Enumeration();
                CompanionInputDefinition field = context.CustomField(BuiltInType.Enumeration, typeId);
                context.Offer([field]);
                context.ReturnDefinition(typeId, definition);
                await context.Tasks.InitializeAsync().ConfigureAwait(false);
                var editor = new CompanionInputEditor(field, static () => { });
                ByteString digest = CompanionInputContract.SchemaDigest(definition, context.Values.MessageContext);
                editor.AcceptValue(Variant.From(7), context.Values.MessageContext, static () => { }, digest);
                CompanionValue input = stage == "Direct" ? new("value", Variant.From(7)) : editor.Capture();
                CompanionOperationDraft? draft = afterPreparation
                    ? await context.PrepareAsync([input]).ConfigureAwait(false)
                    : null;
                definition.Fields = [new EnumField { Name = "Reassigned", Value = 7 }];
                Assert.That(CompanionInputContract.SchemaDigest(definition, context.Values.MessageContext),
                    Is.Not.EqualTo(digest));

                if (draft is null)
                {
                    await Assert.ThatAsync(() => context.PrepareAsync([editor.Capture()]),
                        Throws.InvalidOperationException.With.Message.Contains("definition changed"))
                        .ConfigureAwait(false);
                }
                else
                {
                    Assert.That(draft.Inputs[0].InputSchemaDigest, Is.EqualTo(stage == "Direct" ? default : digest));
                    Assert.That(draft.InputSchemas[0].Digest, Is.EqualTo(digest));
                    await Assert.ThatAsync(() => context.Tasks.Workspace.ExecuteTaskAsync(draft, false, null),
                        Throws.InvalidOperationException.With.Message.Contains(
                            stage == "Direct" ? "input metadata" : "definition changed"))
                        .ConfigureAwait(false);
                    await Assert.ThatAsync(() => context.Tasks.Workspace.ExecuteTaskAsync(draft, false, null),
                        Throws.InvalidOperationException.With.Message.Contains("Prepare this operation again"))
                        .ConfigureAwait(false);
                }
                Assert.That(editor.Capture().Value, Is.EqualTo(Variant.From(7)));
                Assert.That(context.Values.Reads, Is.EqualTo(afterPreparation ? 2 : 1));
                context.Tasks.VerifyCalls(afterPreparation ? 1 : 0, 0);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CurrentStructureDefinitionMustMatchNativeFieldTypeAndRank(bool changeRank)
        {
            var context = new CompanionStructuredInputTestContext();
            await using (context.ConfigureAwait(false))
            {
                StructuredValueTestContext.NativeType type = context.Values.Register(
                    "CurrentTaskSchema", StructureType.Structure,
                    [StructuredValueTestContext.Field("Counter", DataTypeIds.Int32)]);
                IEncodeable instance = type.Type.CreateInstance();
                ((IStructure)instance)["Counter"] = Variant.From(31);
                StructureDefinition incompatible = CoreUtils.Clone(type.Definition)!;
                incompatible.Fields =
                [
                    StructuredValueTestContext.Field("Counter", changeRank ? DataTypeIds.Int32 : DataTypeIds.String,
                        rank: changeRank ? ValueRanks.OneDimension : ValueRanks.Scalar)
                ];
                context.Offer([context.CustomField(BuiltInType.ExtensionObject, type.DataTypeId)]);
                context.ReturnDefinition(type.DataTypeId, incompatible);
                await context.Tasks.InitializeAsync().ConfigureAwait(false);

                await Assert.ThatAsync(() => context.PrepareAsync([new("value", Variant.FromStructure(instance))]),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadTypeMismatch))
                    .ConfigureAwait(false);

                Assert.That(((IStructure)instance)["Counter"], Is.EqualTo(Variant.From(31)));
                Assert.That(context.Values.Reads, Is.EqualTo(1));
                context.Tasks.VerifyCalls(0, 0);
            }
        }

        [TestCase("Rank")]
        [TestCase("Type")]
        [TestCase("Dimensions")]
        public async Task ChangedOfferedInputMetadataConsumesPreparationWithoutDispatch(string change)
        {
            var context = new CompanionStructuredInputTestContext();
            await using (context.ConfigureAwait(false))
            {
                CompanionInputDefinition field = CompanionStructuredInputTestContext.Field(BuiltInType.Int32) with
                {
                    ValueRank = 1,
                    ArrayDimensions = [8u]
                };
                context.Offer([field]);
                await context.Tasks.InitializeAsync().ConfigureAwait(false);
                CompanionOperationDraft draft = await context.PrepareAsync(
                    [new("value", Variant.From([17, -4, 90]))]).ConfigureAwait(false);
                context.Offer([change switch
                {
                    "Rank" => field with { ValueRank = 2, ArrayDimensions = [2u, 4u] },
                    "Type" => field with { DataType = BuiltInType.UInt32 },
                    _ => field with { ArrayDimensions = [7u] }
                }]);

                await Assert.ThatAsync(() => context.Tasks.Workspace.ExecuteTaskAsync(draft, false, null),
                    Throws.InvalidOperationException.With.Message.Contains("operation or session changed"))
                    .ConfigureAwait(false);
                await Assert.ThatAsync(() => context.Tasks.Workspace.ExecuteTaskAsync(draft, false, null),
                    Throws.InvalidOperationException.With.Message.Contains("Prepare this operation again"))
                    .ConfigureAwait(false);

                AssertIntegers(draft.Inputs[0].Value, [17, -4, 90]);
                context.Tasks.VerifyCalls(1, 0);
                context.Tasks.VerifyInspections(3);
                Assert.That(context.Values.Reads, Is.Zero);
            }
        }

        [TestCase("LowRank")]
        [TestCase("HighRank")]
        [TestCase("DimensionCount")]
        [TestCase("ScalarDimensions")]
        [TestCase("UnsupportedType")]
        [TestCase("StructureWithoutId")]
        [TestCase("EnumWithoutId")]
        [TestCase("ForeignServer")]
        [TestCase("IndexedNamespace")]
        [TestCase("ArrayFile")]
        [TestCase("NumericFile")]
        [TestCase("ArrayMultiline")]
        [TestCase("NumericMultiline")]
        public void DefinitionRejectsNonPortableOrIncompatibleMetadata(string fault)
        {
            CompanionInputDefinition scalar = CompanionStructuredInputTestContext.Field(BuiltInType.Int32);
            CompanionInputDefinition invalid = fault switch
            {
                "LowRank" => scalar with { ValueRank = ValueRanks.ScalarOrOneDimension - 1 },
                "HighRank" => scalar with { ValueRank = 33 },
                "DimensionCount" => scalar with { ValueRank = 2, ArrayDimensions = [1u] },
                "ScalarDimensions" => scalar with { ArrayDimensions = [1u] },
                "UnsupportedType" => scalar with { DataType = BuiltInType.Variant },
                "StructureWithoutId" => scalar with { DataType = BuiltInType.ExtensionObject },
                "EnumWithoutId" => scalar with { DataType = BuiltInType.Enumeration },
                "ForeignServer" => scalar with { DataTypeId = new ExpandedNodeId(4200u, "urn:foreign", 1u) },
                "IndexedNamespace" => scalar with { DataTypeId = new ExpandedNodeId(4200u, 2) },
                "ArrayFile" => scalar with
                {
                    DataType = BuiltInType.String,
                    ValueRank = 1,
                    IsFileSource = true
                },
                "NumericFile" => scalar with { IsFileSource = true },
                "ArrayMultiline" => scalar with
                {
                    DataType = BuiltInType.String,
                    ValueRank = 1,
                    IsMultiline = true
                },
                _ => scalar with { IsMultiline = true }
            };

            Assert.That(() => CompanionInputContract.ValidateDefinition(invalid),
                Throws.InvalidOperationException.With.Message.Contains("invalid input definition"));
            Assert.That(() => CompanionInputContract.ValidateDefinition(scalar), Throws.Nothing);
            Assert.That(() => CompanionInputContract.ValidateDefinition(scalar with
            {
                ValueRank = 32,
                ArrayDimensions = ArrayOf.Wrapped(new uint[32])
            }), Throws.Nothing);
        }

        [TestCase("Rank")]
        [TestCase("Type")]
        [TestCase("Dimensions")]
        [TestCase("Namespace")]
        public async Task InvalidValuesNeverReachProvider(string fault)
        {
            var context = new CompanionStructuredInputTestContext();
            await using (context.ConfigureAwait(false))
            {
                CompanionInputDefinition field = CompanionStructuredInputTestContext.Field(BuiltInType.Int32) with
                {
                    ValueRank = 2,
                    ArrayDimensions = [2u, 3u]
                };
                Variant value = fault switch
                {
                    "Rank" => Variant.From([1, 2, 3, 4, 5, 6]),
                    "Type" => Variant.From(((ArrayOf<double>)[1, 2, 3, 4, 5, 6]).ToMatrix([2, 3])),
                    "Dimensions" => Variant.From(((ArrayOf<int>)[1, 2, 3, 4, 5, 6]).ToMatrix([3, 2])),
                    _ => Variant.From(7)
                };
                if (fault == "Namespace")
                {
                    field = CompanionStructuredInputTestContext.Field(BuiltInType.Enumeration) with
                    {
                        DataTypeId = new ExpandedNodeId(4200u, "urn:missing")
                    };
                }
                context.Offer([field]);
                await context.Tasks.InitializeAsync().ConfigureAwait(false);

                if (fault is "Dimensions" or "Namespace")
                {
                    await Assert.ThatAsync(() => context.PrepareAsync([new("value", value)]),
                        Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                            .EqualTo(fault == "Dimensions"
                                ? StatusCodes.BadOutOfRange : StatusCodes.BadDataTypeIdUnknown)).ConfigureAwait(false);
                }
                else
                {
                    await Assert.ThatAsync(() => context.PrepareAsync([new("value", value)]),
                        Throws.ArgumentException.With.Message.Contains("types or ranks")).ConfigureAwait(false);
                }
                context.Tasks.VerifyCalls(0, 0);
                Assert.That(context.Values.Reads, Is.Zero);
            }
        }

        [Test]
        public async Task DuplicateOfferedFieldsAreRejectedBeforePreparation()
        {
            var context = new CompanionStructuredInputTestContext();
            await using (context.ConfigureAwait(false))
            {
                CompanionInputDefinition field = CompanionStructuredInputTestContext.Field(BuiltInType.Int32);
                context.Offer([field, field with { DisplayName = "Duplicate" }]);

                await Assert.ThatAsync(context.Tasks.InitializeAsync,
                    Throws.InvalidOperationException.With.Message.Contains("invalid input definition"))
                    .ConfigureAwait(false);

                context.Tasks.VerifyCalls(0, 0);
                Assert.That(context.Values.Reads, Is.Zero);
            }
        }

        [TestCase("Characters")]
        [TestCase("Bytes")]
        [TestCase("Elements")]
        public async Task InputLimitsAcceptTheBoundaryAndRejectOneMore(string limit)
        {
            var context = new CompanionStructuredInputTestContext();
            await using (context.ConfigureAwait(false))
            {
                context.Values.MessageContext.MaxArrayLength = 0;
                context.Values.MessageContext.MaxStringLength = 0;
                context.Values.MessageContext.MaxByteStringLength = 0;
                ArrayOf<CompanionValue> accepted;
                ArrayOf<CompanionValue> rejected;
                if (limit == "Characters")
                {
                    context.Offer(
                    [
                        CompanionStructuredInputTestContext.Field(BuiltInType.String),
                        CompanionStructuredInputTestContext.Field(BuiltInType.String) with { Name = "second" }
                    ]);
                    accepted = [new("value", Variant.From(new string('a', 32768))),
                        new("second", Variant.From(new string('b', 32768)))];
                    rejected = [accepted[0], new("second", Variant.From(new string('b', 32769)))];
                }
                else if (limit == "Bytes")
                {
                    context.Offer(
                    [
                        CompanionStructuredInputTestContext.Field(BuiltInType.ByteString),
                        CompanionStructuredInputTestContext.Field(BuiltInType.ByteString) with { Name = "second" }
                    ]);
                    byte[] bytes = new byte[(1048576 - 10) / 2];
                    bytes[0] = 0xA5;
                    bytes[^1] = 0x7F;
                    byte[] second = new byte[bytes.Length];
                    second[0] = 0x5A;
                    second[^1] = 0x80;
                    accepted = [new("value", Variant.From(ByteString.From(bytes))),
                        new("second", Variant.From(ByteString.From(second)))];
                    rejected = [accepted[0],
                        new("second", Variant.From(ByteString.From(new byte[bytes.Length + 1])))];
                    Assert.That(DataValueCodec.EncodeVariant(
                        accepted[0].Value, EncodingFormat.Binary, context.Values.MessageContext).Length +
                        DataValueCodec.EncodeVariant(
                            accepted[1].Value, EncodingFormat.Binary, context.Values.MessageContext).Length,
                        Is.EqualTo(1048576));
                }
                else
                {
                    context.Offer(
                        [CompanionStructuredInputTestContext.Field(BuiltInType.Int32) with { ValueRank = 1 }]);
                    int[] elements = new int[65536];
                    elements[0] = 11;
                    elements[^1] = 99;
                    accepted = [new("value", Variant.From(ArrayOf.Wrapped(elements)))];
                    rejected = [new("value", Variant.From(ArrayOf.Wrapped(new int[elements.Length + 1])))];
                }
                await context.Tasks.InitializeAsync().ConfigureAwait(false);

                CompanionOperationDraft draft = await context.PrepareAsync(accepted).ConfigureAwait(false);
                Assert.That(draft.Inputs, Is.EqualTo(accepted));
                Assert.That(context.Tasks.CapturedInputs, Is.EqualTo(accepted));
                if (limit == "Elements")
                {
                    await Assert.ThatAsync(() => context.PrepareAsync(rejected),
                        Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                            .EqualTo(StatusCodes.BadEncodingLimitsExceeded)).ConfigureAwait(false);
                }
                else
                {
                    await Assert.ThatAsync(() => context.PrepareAsync(rejected), Throws.ArgumentException)
                        .ConfigureAwait(false);
                }
                await Assert.ThatAsync(() => context.Tasks.Workspace.ExecuteTaskAsync(draft, false, null),
                    Throws.InvalidOperationException.With.Message.Contains("Prepare this operation again"))
                    .ConfigureAwait(false);
                context.Tasks.VerifyCalls(1, 0);
                Assert.That(context.Values.Reads, Is.Zero);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MetadataAwaitCannotCommitAfterCancellationOrSessionChange(bool changeSession)
        {
            var context = new CompanionStructuredInputTestContext();
            await using (context.ConfigureAwait(false))
            {
                using var cancellation = new CancellationTokenSource();
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<ReadResponse>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var typeId = new NodeId(4200u, context.Values.NamespaceIndex);
                context.Offer([context.CustomField(BuiltInType.Enumeration, typeId)]);
                CancellationToken readToken = default;
                context.Values.Reader = (ids, token) =>
                {
                    CompanionStructuredInputTestContext.AssertDefinitionRead(ids, typeId);
                    readToken = token;
                    entered.TrySetResult();
                    return new ValueTask<ReadResponse>(release.Task);
                };
                await context.Tasks.InitializeAsync().ConfigureAwait(false);
                Task<CompanionOperationDraft> preparing = context.PrepareAsync(
                    [new("value", Variant.From(7))], cancellation.Token);
                try
                {
                    await Task.WhenAny(entered.Task, preparing).ConfigureAwait(false);
                    Assert.That(entered.Task.IsCompletedSuccessfully, Is.True);
                    Assert.That(readToken.CanBeCanceled, Is.True);
                    context.Tasks.VerifyCalls(0, 0);
                    if (changeSession)
                    {
                        context.Tasks.SessionId = new NodeId(202u);
                    }
                    else
                    {
                        await cancellation.CancelAsync().ConfigureAwait(false);
                        Assert.That(readToken.IsCancellationRequested, Is.True);
                    }
                }
                finally
                {
                    release.TrySetResult(StructuredValueTestContext.Reply(
                        Variant.FromStructure(CompanionStructuredInputTestContext.Enumeration())));
                }

                if (changeSession)
                {
                    await Assert.ThatAsync(() => preparing,
                        Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                            .EqualTo(StatusCodes.BadInvalidState)).ConfigureAwait(false);
                }
                else
                {
                    await Assert.ThatAsync(() => preparing, Throws.InstanceOf<OperationCanceledException>())
                        .ConfigureAwait(false);
                }
                context.Tasks.VerifyCalls(0, 0);
                Assert.That(context.Values.Reads, Is.EqualTo(1));
            }
        }

        [Test]
        public void AcceptedEditorValueAndDigestAreIndependentSnapshotsAndRejectStaleCapture()
        {
            using var context = new StructuredValueTestContext();
            int changes = 0;
            int currentChecks = 0;
            bool current = true;
            var editor = new CompanionInputEditor(
                CompanionStructuredInputTestContext.Field(BuiltInType.Int32) with { ValueRank = 1 }, () => changes++);
            int[] storage = [17, -4, 90];
            byte[] digest = [1, 2, 3, 4];
            void RequireCurrent()
            {
                currentChecks++;
                if (!current)
                {
                    throw new InvalidOperationException("The accepted session changed.");
                }
            }

            Assert.That(() => editor.Capture(), Throws.InvalidOperationException.With.Message.Contains("accept"));
            editor.AcceptValue(Variant.From(ArrayOf.Wrapped(storage)), context.MessageContext,
                RequireCurrent, ByteString.From(digest));
            storage[0] = 999;
            digest[0] = 255;
            CompanionValue captured = editor.Capture();
            OverwriteFirst(captured.Value, 500);
            OverwriteFirst(editor.GetInitialValue(), 600);
            CompanionValue snapshot = CompanionInputContract.Snapshot([editor.Capture()], context.MessageContext)[0];

            AssertIntegers(snapshot.Value, [17, -4, 90]);
            Assert.That(snapshot.InputSchemaDigest, Is.EqualTo(ByteString.From([1, 2, 3, 4])));
            Assert.That(captured.InputSchemaDigest, Is.EqualTo(ByteString.From([1, 2, 3, 4])));
            Assert.That(editor.TypedSummary, Is.EqualTo("Reviewed Int32, rank 1."));
            Assert.That(editor.IsText, Is.False);
            Assert.That(editor.IsBoolean, Is.False);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(currentChecks, Is.EqualTo(4));
            current = false;
            Assert.That(() => editor.Capture(),
                Throws.InvalidOperationException.With.Message.Contains("session changed"));
            Assert.That(editor.GetInitialValue,
                Throws.InvalidOperationException.With.Message.Contains("session changed"));
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(currentChecks, Is.EqualTo(6));
            Assert.That(context.Reads, Is.Zero);
        }

        private static IEnumerable<TestCaseData> ScalarCases()
        {
            yield return new TestCaseData(BuiltInType.UInt64, "18446744073709551615", Variant.From(ulong.MaxValue));
            yield return new TestCaseData(BuiltInType.Int64, "-9223372036854775808", Variant.From(long.MinValue));
            yield return new TestCaseData(BuiltInType.Int64, "9223372036854775807", Variant.From(long.MaxValue));
            yield return new TestCaseData(BuiltInType.ByteString, "AH+A/w==",
                Variant.From(ByteString.From([0, 127, 128, 255])));
            yield return new TestCaseData(BuiltInType.SByte, "-128", Variant.From(sbyte.MinValue));
            yield return new TestCaseData(BuiltInType.Byte, "255", Variant.From(byte.MaxValue));
            yield return new TestCaseData(BuiltInType.Int16, "-32768", Variant.From(short.MinValue));
            yield return new TestCaseData(BuiltInType.UInt16, "65535", Variant.From(ushort.MaxValue));
            yield return new TestCaseData(BuiltInType.Float, "1.25", Variant.From(1.25f));
            yield return new TestCaseData(BuiltInType.DateTime, "2026-09-12T12:30:45Z",
                Variant.From(new DateTimeUtc(new DateTime(2026, 9, 12, 12, 30, 45, DateTimeKind.Utc))));
            yield return new TestCaseData(BuiltInType.Guid, "e35c7a60-9a04-4b8e-9033-2d6f3d2275d1",
                Variant.From(new Uuid("e35c7a60-9a04-4b8e-9033-2d6f3d2275d1")));
            yield return new TestCaseData(BuiltInType.NodeId, "ns=2;i=4294967295",
                Variant.From(new NodeId(uint.MaxValue, 2)));
            yield return new TestCaseData(BuiltInType.QualifiedName, "2:Counter",
                Variant.From(new QualifiedName("Counter", 2)));
            yield return new TestCaseData(BuiltInType.LocalizedText, "retained text",
                Variant.From(new LocalizedText(null, "retained text")));
        }

        private static void AssertIntegers(Variant value, ArrayOf<int> expected, bool matrix = false)
        {
            Assert.That(value.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(value.TypeInfo.ValueRank, Is.EqualTo(matrix ? 2 : 1));
            ArrayOf<int> actual;
            if (matrix)
            {
                Assert.That(value.TryGetValue(out MatrixOf<int> cells), Is.True);
                Assert.That(cells.Dimensions, Is.EqualTo(sMatrixDimensions));
                actual = cells.ToArrayOf();
            }
            else
            {
                Assert.That(value.TryGetValue(out actual), Is.True);
            }
            Assert.That(actual.IsNull, Is.EqualTo(expected.IsNull));
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            Assert.That(actual.ToArray(), Is.EqualTo(expected.ToArray()));
        }

        private static void OverwriteFirst(Variant value, int replacement)
        {
            Assert.That(value.TryGetValue(out ArrayOf<int> array), Is.True);
            Assert.That(MemoryMarshal.TryGetArray(array.Memory, out ArraySegment<int> segment), Is.True);
            int[] storage = segment.Array ?? throw new AssertionException("Expected array-backed primitive input.");
            storage[segment.Offset] = replacement;
        }

        private static readonly int[] sMatrixDimensions = [2, 3];
    }

    internal sealed class CompanionStructuredInputTestContext : IAsyncDisposable
    {
        public CompanionStructuredInputTestContext(ITelemetryContext? telemetry = null)
        {
            Values.MessageContext.NamespaceUris.GetIndexOrAppend("urn:typed");
            Tasks = new CompanionTypedTaskTestContext(telemetry ?? Values.MessageContext.Telemetry);
            Tasks.Session.SetupGet(value => value.MessageContext).Returns(() => Values.MessageContext);
            Tasks.Session.SetupGet(value => value.NamespaceUris).Returns(() => Values.MessageContext.NamespaceUris);
            Tasks.Session.SetupGet(value => value.Factory).Returns(() => Values.MessageContext.Factory);
            Tasks.Session.Setup(value => value.ReadAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, double age, TimestampsToReturn timestamps,
                    ArrayOf<ReadValueId> ids, CancellationToken token) =>
                    Values.Session.Object.ReadAsync(header, age, timestamps, ids, token));
        }

        public StructuredValueTestContext Values { get; } = new();

        public CompanionTypedTaskTestContext Tasks { get; }

        public void Offer(ArrayOf<CompanionInputDefinition> fields)
        {
            CompanionOperation operation = Tasks.Operation with { Inputs = fields };
            Tasks.Inspection = new CompanionInspection([], [operation], "Structured input inspection.");
        }

        public CompanionInputDefinition CustomField(BuiltInType type, NodeId typeId)
        {
            return Field(type) with
            {
                DataTypeId = NodeId.ToExpandedNodeId(typeId, Values.MessageContext.NamespaceUris)
            };
        }

        public void ReturnDefinition(NodeId typeId, DataTypeDefinition definition)
        {
            Values.Reader = (ids, _) =>
            {
                AssertDefinitionRead(ids, typeId);
                return ValueTask.FromResult(StructuredValueTestContext.Reply(Variant.FromStructure(definition)));
            };
        }

        public Task<CompanionOperationDraft> PrepareAsync(
            ArrayOf<CompanionValue> inputs, CancellationToken cancellationToken = default)
        {
            return Tasks.Workspace.PrepareTaskAsync(Tasks.Target, "typed", inputs, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Tasks.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                Values.Dispose();
            }
        }

        public static CompanionInputDefinition Field(BuiltInType type)
        {
            return new CompanionInputDefinition("value", "Task value", type, "Review the typed value.");
        }

        public static EnumDefinition Enumeration()
        {
            return new EnumDefinition
            {
                Fields = [new EnumField { Name = "Idle", Value = 0 }, new EnumField { Name = "Running", Value = 7 }]
            };
        }

        public static void AssertDefinitionRead(ArrayOf<ReadValueId> ids, NodeId typeId)
        {
            Assert.That(ids.Count, Is.EqualTo(1));
            Assert.That(ids[0].NodeId, Is.EqualTo(typeId));
            Assert.That(ids[0].AttributeId, Is.EqualTo(Attributes.DataTypeDefinition));
        }
    }
}
