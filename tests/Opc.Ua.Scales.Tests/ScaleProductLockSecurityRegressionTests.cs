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
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.Scales.Server;
using Opc.Ua.Scales.Server.Builders;
using Opc.Ua.Scales.Server.Runtime;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Scales.Tests
{
    /// <summary>
    /// Exercises remote product mutations against the real DI session-owner lock service without a server.
    /// </summary>
    [TestFixture]
    [Category("Scales")]
    public sealed class ScaleProductLockSecurityRegressionTests
    {
        /// <summary>
        /// Gets invalid indexed writes and their expected status codes.
        /// </summary>
        private static IEnumerable<TestCaseData> InvalidRangeCases =>
        [
            new("4:5", StatusCodes.BadIndexRangeNoData),
            new("1:2", StatusCodes.BadIndexRangeDataMismatch),
            new("0:1,0:1", StatusCodes.BadIndexRangeNoData)
        ];

        /// <summary>
        /// Gets sparse and absent enumeration tables and their expected write results.
        /// </summary>
        private static IEnumerable<TestCaseData> EnumerationTableCases =>
        [
            new(false, "sparse", 4u, StatusCodes.Good, "four"),
            new(true, "sparse", 4u, StatusCodes.Good, "four"),
            new(false, "sparse", 3u, StatusCodes.BadOutOfRange, "initial"),
            new(false, "empty", 4u, StatusCodes.BadOutOfRange, "initial"),
            new(false, "missing", 99u, StatusCodes.Good, "initial"),
            new(false, "null", 99u, StatusCodes.Good, "initial")
        ];

        /// <summary>
        /// Gets validation failures that precede enumeration policy execution.
        /// </summary>
        private static IEnumerable<TestCaseData> EnumerationValidationCases =>
        [
            new("access", StatusCodes.BadNotWritable),
            new("user", StatusCodes.BadUserAccessDenied),
            new("type", StatusCodes.BadTypeMismatch),
            new("status", StatusCodes.BadWriteNotSupported),
            new("timestamp", StatusCodes.BadWriteNotSupported),
            new("server", StatusCodes.BadWriteNotSupported),
            new("range", StatusCodes.BadIndexRangeNoData)
        ];

        /// <summary>
        /// Direct and nested writes cannot change the filling calculation while another session owns the product.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF43ProductLockProtectsValueWritesAndFillingCalculationAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            var product = (AutomaticFillingProductState)scale.ProductionPreset!.ActiveProduct!;
            TargetItemState target = product.TargetWeight!;
            AutomaticFillingController filling = scale.AutomaticFilling!;
            await harness.AcquireAsync(product).ConfigureAwait(false);

            AssertDenied(await WriteAsync(harness.Other, target, Variant.From(12.0)).ConfigureAwait(false));
            AssertDenied(await WriteAsync(harness.Other, target.PlusTolerance!, Variant.From(10.0))
                .ConfigureAwait(false));
            Assert.That(ReadDouble(target), Is.EqualTo(10));
            Assert.That(ReadDouble(target.PlusTolerance!), Is.EqualTo(1));
            Assert.That(filling.EvaluateFilling(12), Is.EqualTo(ToleranceState.Over_2));
            Assert.That(ReadDouble(filling.Scale.Deviation!), Is.EqualTo(2));

            AssertGood(await WriteAsync(harness.Owner, target, Variant.From(12.0)).ConfigureAwait(false));
            Assert.That(ReadDouble(target), Is.EqualTo(12));
            Assert.That(filling.EvaluateFilling(12), Is.EqualTo(ToleranceState.In_0));
            Assert.That(ReadDouble(filling.Scale.Deviation!), Is.Zero);

            await harness.ReleaseAsync(product).ConfigureAwait(false);
            AssertGood(await WriteAsync(harness.Other, target, Variant.From(14.0)).ConfigureAwait(false));
            Assert.That(filling.EvaluateFilling(12), Is.EqualTo(ToleranceState.Under_1));
            Assert.That(ReadDouble(filling.Scale.Deviation!), Is.EqualTo(-2));
        }

        /// <summary>
        /// A product without an active lock keeps its ordinary value-write behavior.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF43UnlockedProductKeepsOrdinaryWritesAsync(bool lockable)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct, lockable);
            var product = (AutomaticFillingProductState)scale.ProductionPreset!.ActiveProduct!;

            AssertGood(await WriteAsync(harness.Other, product.TargetWeight!, Variant.From(15.0))
                .ConfigureAwait(false));

            Assert.That(ReadDouble(product.TargetWeight!), Is.EqualTo(15));
            Assert.That(scale.AutomaticFilling!.EvaluateFilling(15), Is.EqualTo(ToleranceState.In_0));
        }

        /// <summary>
        /// A synchronous callback installed after binding is not bypassed by the native cache-writing policy.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task BoundProductAsyncWritesRejectLateSynchronousHandlersWithoutMutationAsync(bool simple)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            var product = (AutomaticFillingProductState)scale.ProductionPreset!.ActiveProduct!;
            TargetItemState target = product.TargetWeight!;
            Assert.That(target.OnWriteValueAsync, Is.Not.Null);
            int calls = 0;
            if (simple)
            {
                target.OnSimpleWriteValue = (_, _, ref _) =>
                {
                    calls++;
                    return ServiceResult.Good;
                };
            }
            else
            {
                target.OnWriteValue = (
                    ISystemContext _, NodeState _, NumericRange _, QualifiedName _,
                    ref Variant _, ref StatusCode _, ref DateTimeUtc _) =>
                {
                    calls++;
                    return ServiceResult.Good;
                };
            }
            Variant original = target.WrappedValue;
            StatusCode status = target.StatusCode;
            DateTimeUtc timestamp = target.Timestamp;

            foreach (ISystemContext context in new ISystemContext[] { harness.Context, harness.Owner, harness.Other })
            {
                ServiceResult rejected = await WriteAsync(context, target, Variant.From(15.0)).ConfigureAwait(false);
                Assert.That(rejected.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
                Assert.That(target.WrappedValue, Is.EqualTo(original));
                Assert.That(target.StatusCode, Is.EqualTo(status));
                Assert.That(target.Timestamp, Is.EqualTo(timestamp));
                Assert.That(calls, Is.Zero);
            }

            AssertGood(Write(harness.Owner, target, Variant.From(12.0)));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(ReadDouble(target), Is.EqualTo(12));

            target.OnWriteValue = null;
            target.OnSimpleWriteValue = null;
            AssertGood(await WriteAsync(harness.Owner, target, Variant.From(14.0)).ConfigureAwait(false));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(ReadDouble(target), Is.EqualTo(14));
        }

        /// <summary>
        /// A synchronous application handler present before registration is not replaced by a cache policy.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task BoundProductAsyncWritesPreserveExistingSynchronousHandlerPolicyAsync(bool simple)
        {
            using var harness = new Harness();
            int calls = 0;
            ScaleHandle scale = harness.CreateScale(
                ScaleKind.AutomaticFilling,
                (context, state) =>
                {
                    ConfigureFillingProduct(context, state);
                    TargetItemState target = ((AutomaticFillingProductState)state).TargetWeight!;
                    if (simple)
                    {
                        target.OnSimpleWriteValue = (_, _, ref _) =>
                        {
                            calls++;
                            return ServiceResult.Good;
                        };
                    }
                    else
                    {
                        target.OnWriteValue = (
                            ISystemContext _, NodeState _, NumericRange _, QualifiedName _,
                            ref Variant _, ref StatusCode _, ref DateTimeUtc _) =>
                        {
                            calls++;
                            return ServiceResult.Good;
                        };
                    }
                });
            var product = (AutomaticFillingProductState)scale.ProductionPreset!.ActiveProduct!;
            TargetItemState target = product.TargetWeight!;
            Assert.That(target.OnWriteValueAsync, Is.Null);
            Assert.That(target.OnSimpleWriteValueAsync, Is.Null);
            StatusCode status = target.StatusCode;
            DateTimeUtc timestamp = target.Timestamp;

            ServiceResult rejected = await WriteAsync(harness.Owner, target, Variant.From(12.0))
                .ConfigureAwait(false);

            Assert.That(rejected.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(calls, Is.Zero);
            Assert.That(ReadDouble(target), Is.EqualTo(10));
            Assert.That(target.StatusCode, Is.EqualTo(status));
            Assert.That(target.Timestamp, Is.EqualTo(timestamp));
            AssertGood(Write(harness.Owner, target, Variant.From(12.0)));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(ReadDouble(target), Is.EqualTo(12));
        }

        /// <summary>
        /// Native cache writes retain access and metadata validation before type checks and cache commit.
        /// </summary>
        [Test]
        public async Task StockProductNativeAsyncWritePreservesValidationOrderAndCacheAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            var product = (AutomaticFillingProductState)scale.ProductionPreset!.ActiveProduct!;
            TargetItemState target = product.TargetWeight!;
            StatusCode status = target.StatusCode;
            DateTimeUtc timestamp = target.Timestamp;
            await harness.AcquireAsync(product).ConfigureAwait(false);
            var wrongType = new DataValue(Variant.From("not a weight"));

            ServiceResult denied = await target.WriteAttributeAsync(
                harness.Other, Attributes.Value, NumericRange.Null, wrongType).ConfigureAwait(false);
            AssertDenied(denied);
            ServiceResult badTimestamp = await target.WriteAttributeAsync(
                harness.Other,
                Attributes.Value,
                NumericRange.Null,
                wrongType.WithServerTimestamp(new DateTimeUtc(2026, 1, 1, 0, 0, 1))).ConfigureAwait(false);
            Assert.That(badTimestamp.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadWriteNotSupported));
            ServiceResult invalid = await target.WriteAttributeAsync(
                harness.Owner, Attributes.Value, NumericRange.Null, wrongType).ConfigureAwait(false);
            Assert.That(invalid.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTypeMismatch));
            ServiceResult metadata = await target.WriteAttributeAsync(
                harness.Owner,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(Variant.From("not a weight"), StatusCodes.Uncertain)).ConfigureAwait(false);
            Assert.That(metadata.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadWriteNotSupported));
            byte originalAccessLevel = target.AccessLevel;
            target.AccessLevel = AccessLevels.CurrentRead;
            ServiceResult notWritable = await target.WriteAttributeAsync(
                harness.Owner, Attributes.Value, NumericRange.Null, wrongType).ConfigureAwait(false);
            Assert.That(notWritable.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotWritable));
            target.AccessLevel = originalAccessLevel;
            Assert.That(ReadDouble(target), Is.EqualTo(10));
            Assert.That(target.StatusCode, Is.EqualTo(status));
            Assert.That(target.Timestamp, Is.EqualTo(timestamp));

            AssertGood(await WriteAsync(harness.Owner, target, Variant.From(12.0)).ConfigureAwait(false));
            Assert.That(ReadDouble(target), Is.EqualTo(12));
            Assert.That(target.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
        }

        /// <summary>
        /// Newly materialised writable children use native indexed writes and preserve product-lock validation.
        /// </summary>
        [Test]
        public async Task DynamicWritableChildUsesNativeIndexedAsyncCachePolicyAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            var variable = new BaseDataVariableState(product)
            {
                NodeId = new NodeId("DynamicWritableArray", 1),
                BrowseName = new QualifiedName("DynamicWritableArray", 1),
                DataType = Ua.DataTypeIds.Double,
                ValueRank = ValueRanks.OneDimension,
                AccessLevel = AccessLevels.CurrentReadOrWrite | AccessLevels.StatusWrite | AccessLevels.TimestampWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite |
                    AccessLevels.StatusWrite | AccessLevels.TimestampWrite,
                Value = Variant.From((double[])s_initialArray.Clone()),
                StatusCode = StatusCodes.Good
            };
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            Assert.That(variable.OnWriteValueAsync, Is.Not.Null);
            await harness.AcquireAsync(product).ConfigureAwait(false);
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 2);
            var requested = new DataValue(
                Variant.From(s_slice), StatusCodes.Uncertain, timestamp);

            ServiceResult denied = await variable.WriteAttributeAsync(
                harness.Other, Attributes.Value, NumericRange.Parse("1:2"), requested).ConfigureAwait(false);
            AssertDenied(denied);
            Assert.That(variable.Value.GetDoubleArray(), Is.EqualTo(s_initialArray.ToArrayOf()));
            ServiceResult result = await variable.WriteAttributeAsync(
                harness.Owner, Attributes.Value, NumericRange.Parse("1:2"), requested).ConfigureAwait(false);

            AssertGood(result);
            Assert.That(variable.Value.GetDoubleArray(), Is.EqualTo(s_mergedArray.ToArrayOf()));
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
        }

        /// <summary>
        /// Ordinary native cache writes isolate caller arrays under the selected write-copy policy.
        /// </summary>
        [TestCase(VariableCopyPolicy.CopyOnWrite)]
        [TestCase(VariableCopyPolicy.Always)]
        public async Task NativeCachePolicyCopiesWholeAndIndexedWriteInputsAsync(VariableCopyPolicy copyPolicy)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            var variable = new BaseDataVariableState(product)
            {
                NodeId = new NodeId("CopiedArray", 1),
                BrowseName = new QualifiedName("CopiedArray", 1),
                DataType = Ua.DataTypeIds.Double,
                ValueRank = ValueRanks.OneDimension,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                CopyPolicy = copyPolicy
            };
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            double[] input = [1.0, 2.0, 3.0];
            AssertGood(await WriteAsync(harness.Owner, variable, Variant.From(input)).ConfigureAwait(false));
            input[0] = 99;
            Assert.That(variable.Value.GetDoubleArray(), Is.EqualTo(s_initialArray.ToArrayOf()));

            double[] slice = [20.0, 30.0];
            AssertGood(await variable.WriteAttributeAsync(
                harness.Owner,
                Attributes.Value,
                NumericRange.Parse("1:2"),
                new DataValue(Variant.From(slice))).ConfigureAwait(false));
            slice[0] = 99;
            Assert.That(variable.Value.GetDoubleArray(), Is.EqualTo(s_mergedArray.ToArrayOf()));
        }

        /// <summary>
        /// Exact generated implementation classes receive the same cache policy as their untyped stock forms.
        /// </summary>
        [Test]
        public async Task AuditedGenericVariableImplementationsReceiveNativeCachePolicyAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            BaseVariableState[] variables =
            [
                BaseDataVariableState<double>.With<VariantBuilder>(product),
                PropertyState<double>.With<VariantBuilder>(product),
                DataItemState<double>.With<VariantBuilder>(product),
                AnalogItemState<double>.With<VariantBuilder>(product),
                AnalogUnitRangeState<double>.With<VariantBuilder>(product),
                TargetItemState<double>.With<VariantBuilder>(product),
                MeasuredItemState<double>.With<VariantBuilder>(product)
            ];
            for (int ii = 0; ii < variables.Length; ii++)
            {
                BaseVariableState variable = variables[ii];
                variable.NodeId = new NodeId("AuditedGeneric" + ii, 1);
                variable.BrowseName = new QualifiedName("AuditedGeneric" + ii, 1);
                variable.DataType = Ua.DataTypeIds.Double;
                variable.ValueRank = ValueRanks.Scalar;
                variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.Value = Variant.From(1.0);
                product.AddChild(variable);
            }
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);

            foreach (BaseVariableState variable in variables)
            {
                Assert.That(variable.OnWriteValueAsync, Is.Not.Null);
                AssertGood(await WriteAsync(harness.Owner, variable, Variant.From(2.0)).ConfigureAwait(false));
                Assert.That(variable.Value.GetDouble(), Is.EqualTo(2.0));
            }
        }

        /// <summary>
        /// Invalid indexed writes reject without cache, metadata, change-mask or source-array mutation.
        /// </summary>
        [TestCaseSource(nameof(InvalidRangeCases))]
        public async Task NativeCachePolicyRejectsInvalidRangesWithoutMutationAsync(string range, StatusCode expected)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            double[] original = [1.0, 2.0, 3.0];
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 3);
            var variable = new BaseDataVariableState(product)
            {
                NodeId = new NodeId("RejectedArray", 1),
                BrowseName = new QualifiedName("RejectedArray", 1),
                DataType = Ua.DataTypeIds.Double,
                ValueRank = ValueRanks.OneDimension,
                AccessLevel = AccessLevels.CurrentReadOrWrite | AccessLevels.StatusWrite | AccessLevels.TimestampWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite |
                    AccessLevels.StatusWrite | AccessLevels.TimestampWrite,
                Value = Variant.From(original),
                StatusCode = StatusCodes.Uncertain,
                Timestamp = timestamp
            };
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            NodeStateChangeMasks changeMasks = variable.ChangeMasks;
            ServiceResult result = await variable.WriteAttributeAsync(
                harness.Owner,
                Attributes.Value,
                NumericRange.Parse(range),
                new DataValue(
                    Variant.From(s_invalidSlice),
                    StatusCodes.Good,
                    new DateTimeUtc(2026, 1, 1, 0, 0, 4))).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(expected));
            Assert.That(variable.Value.GetDoubleArray(), Is.EqualTo(s_initialArray.ToArrayOf()));
            Assert.That(original, Is.EqualTo(s_initialArray));
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
            Assert.That(variable.ChangeMasks, Is.EqualTo(changeMasks));
        }

        /// <summary>
        /// External subtypes retain their synchronous semantics instead of receiving stock cache acceptance.
        /// </summary>
        [Test]
        public async Task ExternalVariableOverrideRequiresExplicitNativeAsyncImplementationAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            var variable = new CustomWriteVariableState(product)
            {
                NodeId = new NodeId("CustomWrite", 1),
                BrowseName = new QualifiedName("CustomWrite", 1),
                DataType = Ua.DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                Value = Variant.From(1.0)
            };
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            Assert.That(variable.OnWriteValueAsync, Is.Null);
            Assert.That(variable.OnSimpleWriteValueAsync, Is.Null);

            ServiceResult rejected = await WriteAsync(harness.Owner, variable, Variant.From(2.0))
                .ConfigureAwait(false);
            Assert.That(rejected.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(variable.SynchronousCalls, Is.Zero);
            Assert.That(variable.Value.GetDouble(), Is.EqualTo(1.0));
            ServiceResult synchronous = Write(harness.Owner, variable, Variant.From(2.0));
            Assert.That(synchronous.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
            Assert.That(variable.SynchronousCalls, Is.EqualTo(1));
            Assert.That(variable.Value.GetDouble(), Is.EqualTo(1.0));
        }

        /// <summary>
        /// Supplies application-specific synchronous write semantics outside the audited model assemblies.
        /// </summary>
        private sealed class CustomWriteVariableState : BaseDataVariableState
        {
            /// <summary>
            /// Creates an application variable beneath its product.
            /// </summary>
            internal CustomWriteVariableState(NodeState parent)
                : base(parent)
            {
            }

            /// <summary>
            /// Gets how often the synchronous semantic override ran.
            /// </summary>
            internal int SynchronousCalls { get; private set; }

            /// <inheritdoc/>
            protected override ServiceResult WriteValueAttribute(
                ISystemContext context,
                NumericRange indexRange,
                Variant value,
                StatusCode statusCode,
                DateTimeUtc sourceTimestamp)
            {
                SynchronousCalls++;
                return StatusCodes.BadOutOfRange;
            }
        }

        /// <summary>
        /// Sparse enum validation and dependent text use the explicitly composed native policy.
        /// </summary>
        [TestCaseSource(nameof(EnumerationTableCases))]
        public async Task NativeEnumerationPolicyPreservesSparseAndMissingTablesAsync(
            bool generic, string table, uint input, StatusCode expected, string expectedText)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateNativeEnumeration(product, generic);
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 1);
            variable.StatusCode = StatusCodes.Uncertain;
            variable.Timestamp = timestamp;
            if (table == "missing")
            {
                variable.EnumValues = null;
            }
            else if (table is "empty" or "null")
            {
                variable.EnumValues!.Value = table == "empty" ? ArrayOf<EnumValueType>.Empty : default;
            }
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            Assert.That(variable.OnWriteValueAsync, Is.Not.Null);

            ServiceResult result = await WriteAsync(harness.Owner, variable, Variant.From(input))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(expected));
            Assert.That(variable.Value.GetUInt32(), Is.EqualTo(expected == StatusCodes.Good ? input : 1u));
            Assert.That(variable.ValueAsText!.Value.Text, Is.EqualTo(expectedText));
            Assert.That(variable.StatusCode,
                Is.EqualTo((StatusCode)(expected == StatusCodes.Good ? StatusCodes.Good : StatusCodes.Uncertain)));
            if (expected != StatusCodes.Good)
            {
                Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
                Assert.That(variable.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
                Assert.That(variable.ValueAsText.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            }
        }

        /// <summary>
        /// Native async enum writes reject UInt64 overflow while the legacy synchronous cache fallback is retained.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task NativeEnumerationPolicyPreservesIntegerConversionRulesAsync(
            bool overflowingUnsigned,
            bool asynchronous)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateNativeEnumeration(product);
            variable.DataType = overflowingUnsigned ? Ua.DataTypeIds.UInt64 : Ua.DataTypeIds.Int64;
            variable.EnumValues!.Value = new[]
            {
                new EnumValueType { Value = -4, DisplayName = new LocalizedText("negative") }
            }.ToArrayOf();
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            PropertyState<LocalizedText> text = variable.ValueAsText!;
            StatusCode initialTextStatus = text.StatusCode;
            DateTimeUtc initialTextTimestamp = text.Timestamp;
            Variant input = overflowingUnsigned ? Variant.From(ulong.MaxValue) : Variant.From(-4L);

            Variant initialValue = variable.Value;
            ServiceResult result = asynchronous
                ? await WriteAsync(harness.Owner, variable, input).ConfigureAwait(false)
                : Write(harness.Owner, variable, input);

            if (overflowingUnsigned)
            {
                if (asynchronous)
                {
                    Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
                    Assert.That(variable.Value, Is.EqualTo(initialValue));
                    Assert.That(variable.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
                }
                else
                {
                    AssertGood(result);
                    Assert.That(variable.Value.GetUInt64(), Is.EqualTo(ulong.MaxValue));
                    Assert.That(variable.ChangeMasks & NodeStateChangeMasks.Value,
                        Is.EqualTo(NodeStateChangeMasks.Value));
                }
                Assert.That(text.Value.Text, Is.EqualTo("initial"));
                Assert.That(text.StatusCode, Is.EqualTo(initialTextStatus));
                Assert.That(text.Timestamp, Is.EqualTo(initialTextTimestamp));
                Assert.That(text.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            }
            else
            {
                AssertGood(result);
                Assert.That(variable.Value.GetInt64(), Is.EqualTo(-4L));
                Assert.That(text.Value.Text, Is.EqualTo("negative"));
            }
        }

        /// <summary>
        /// An internal post-commit cache mutation cannot bypass validation of the accepted enumeration sample.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeEnumerationRejectsInternalPostCommitOverflowAsync(bool simple)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateNativeEnumeration(product);
            variable.DataType = Ua.DataTypeIds.UInt64;
            variable.Value = Variant.From(1UL);
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            PropertyState<LocalizedText> text = variable.ValueAsText!;
            StatusCode textStatus = text.StatusCode;
            DateTimeUtc textTimestamp = text.Timestamp;
            int calls = 0;
            ValueTask<AttributeWriteResult> WriteValue()
            {
                calls++;
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good)
                {
                    AfterOrdinaryCacheCommit = () => variable.WrappedValue = Variant.From(ulong.MaxValue)
                });
            }
            if (simple)
            {
                variable.OnSimpleWriteValueAsync = (_, _, _, _) => WriteValue();
            }
            else
            {
                variable.OnWriteValueAsync = (_, _, _, _, _) => WriteValue();
            }

            ServiceResult result = await WriteAsync(harness.Owner, variable, Variant.From(4UL)).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
            Assert.That(variable.Value.GetUInt64(), Is.EqualTo(ulong.MaxValue));
            Assert.That(variable.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(variable.Timestamp, Is.Not.EqualTo(DateTimeUtc.MinValue));
            Assert.That(variable.ChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
            Assert.That(text.Value.Text, Is.EqualTo("initial"));
            Assert.That(text.StatusCode, Is.EqualTo(textStatus));
            Assert.That(text.Timestamp, Is.EqualTo(textTimestamp));
            Assert.That(text.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// A matching value remains writable when the optional dependent text child is absent.
        /// </summary>
        [Test]
        public async Task NativeEnumerationPolicyAllowsMissingValueAsTextAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateNativeEnumeration(product);
            variable.ValueAsText = null;
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);

            AssertGood(await WriteAsync(harness.Owner, variable, Variant.From(4u)).ConfigureAwait(false));

            Assert.That(variable.Value.GetUInt32(), Is.EqualTo(4u));
            Assert.That(variable.ValueAsText, Is.Null);
        }

        /// <summary>
        /// Access, metadata and type rejection precede sparse enum validation without dependent state updates.
        /// </summary>
        [TestCaseSource(nameof(EnumerationValidationCases))]
        public async Task NativeEnumerationPolicyPreservesValidationPrecedenceAsync(
            string scenario, StatusCode expected)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateNativeEnumeration(product);
            var originalTimestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 1);
            variable.StatusCode = StatusCodes.Uncertain;
            variable.Timestamp = originalTimestamp;
            if (scenario == "user")
            {
                variable.UserAccessLevel = AccessLevels.CurrentRead;
            }
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            if (scenario == "access")
            {
                variable.AccessLevel = AccessLevels.CurrentRead;
                await variable.ClearChangeMasksAsync(harness.Context, false).ConfigureAwait(false);
            }
            // An empty enum table would reject every scalar value if the enum handler ran first.
            variable.EnumValues!.Value = ArrayOf<EnumValueType>.Empty;
            var value = new DataValue(scenario == "type" ? Variant.From("wrong") : Variant.From(3u));
            if (scenario == "status")
            {
                value = value.WithStatus(StatusCodes.Uncertain);
            }
            if (scenario == "timestamp")
            {
                value = value.WithSourceTimestamp(new DateTimeUtc(2026, 1, 1, 0, 0, 2));
            }
            if (scenario == "server")
            {
                value = value.WithServerTimestamp(new DateTimeUtc(2026, 1, 1, 0, 0, 2));
            }

            ServiceResult result = await variable.WriteAttributeAsync(
                harness.Owner,
                Attributes.Value,
                scenario == "range" ? NumericRange.Parse("0") : NumericRange.Null,
                value).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(expected));
            Assert.That(variable.Value.GetUInt32(), Is.EqualTo(1u));
            Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(variable.Timestamp, Is.EqualTo(originalTimestamp));
            Assert.That(variable.ValueAsText!.Value.Text, Is.EqualTo("initial"));
            Assert.That(variable.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(variable.ValueAsText.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// An explicit notification flush allows a child sink to await another write on the same scale.
        /// </summary>
        [Test]
        public async Task NativeEnumerationTextFlushAllowsNestedScaleWriteAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateNativeEnumeration(product);
            MultiStateValueDiscreteState nestedVariable =
                CreateNativeEnumeration(product, name: "NestedNativeEnumeration");
            product.AddChild(variable);
            product.AddChild(nestedVariable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            var nestedWriteStarted = new TaskCompletionSource<Task<ServiceResult>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var nestedWriteCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            int calls = 0;
            string? observedText = null;
            NodeStateChangeMasks observedMasks = NodeStateChangeMasks.None;
            PropertyState<LocalizedText> text = variable.ValueAsText!;
            text.OnStateChangedAsync = async (_, _, changeMasks, cancellationToken) =>
            {
                calls++;
                observedMasks |= changeMasks;
                observedText = text.Value.Text;
                Task<ServiceResult> nestedWriteTask = nestedVariable.WriteAttributeAsync(
                    harness.Owner,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(Variant.From(4u)),
                    nestedWriteCancellation.Token).AsTask();
                nestedWriteStarted.TrySetResult(nestedWriteTask);
                await nestedWriteTask.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)
                    .ConfigureAwait(false);
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)
                    .ConfigureAwait(false);
            };

            Task flush = Task.CompletedTask;
            try
            {
                AssertGood(await WriteAsync(harness.Owner, variable, Variant.From(4u))
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false));
                Assert.That(calls, Is.Zero);
                Assert.That(variable.ChangeMasks & NodeStateChangeMasks.Value,
                    Is.EqualTo(NodeStateChangeMasks.Value));
                Assert.That(text.ChangeMasks & NodeStateChangeMasks.Value,
                    Is.EqualTo(NodeStateChangeMasks.Value));
                Assert.That(text.Value.Text, Is.EqualTo("four"));
                flush = variable.ClearChangeMasksAsync(harness.Context, true).AsTask();
                Task<ServiceResult> nestedWriteTask = await nestedWriteStarted.Task
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                AssertGood(await nestedWriteTask.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false));
                Assert.That(Write(harness.Owner, variable, Variant.From(1u)).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(variable.Value.GetUInt32(), Is.EqualTo(4u));
                Assert.That(text.Value.Text, Is.EqualTo("four"));
                Assert.That(nestedVariable.Value.GetUInt32(), Is.EqualTo(4u));
                Assert.That(nestedVariable.ValueAsText!.Value.Text, Is.EqualTo("four"));
                Assert.That(flush.IsCompleted, Is.False);
                Assert.That(observedText, Is.EqualTo("four"));
            }
            finally
            {
                release.TrySetResult(true);
                await flush.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(observedMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
            Assert.That(text.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(variable.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(nestedVariable.ChangeMasks & NodeStateChangeMasks.Value,
                Is.EqualTo(NodeStateChangeMasks.Value));
            await nestedVariable.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);

            AssertGood(await WriteAsync(harness.Owner, variable, Variant.From(1u)).ConfigureAwait(false));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(text.Value.Text, Is.EqualTo("one"));
            Assert.That(variable.ChangeMasks & NodeStateChangeMasks.Value,
                Is.EqualTo(NodeStateChangeMasks.Value));
            Assert.That(text.ChangeMasks & NodeStateChangeMasks.Value,
                Is.EqualTo(NodeStateChangeMasks.Value));
            await variable.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(observedText, Is.EqualTo("one"));
            Assert.That(text.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(variable.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            await nestedVariable.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
        }

        /// <summary>
        /// A value-text sink can await a write to the same node after write admission ends.
        /// </summary>
        [Test]
        public async Task NativeEnumerationTextFlushAllowsSameNodeNestedScaleWriteAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateNativeEnumeration(product);
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            var nestedWriteStarted = new TaskCompletionSource<Task<ServiceResult>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var nestedWriteCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            int calls = 0;
            string? observedText = null;
            NodeStateChangeMasks observedMasks = NodeStateChangeMasks.None;
            StatusCode synchronousWriteStatus = StatusCodes.Good;
            PropertyState<LocalizedText> text = variable.ValueAsText!;
            text.OnStateChangedAsync = async (_, _, changeMasks, cancellationToken) =>
            {
                calls++;
                observedMasks |= changeMasks;
                observedText = text.Value.Text;
                if (calls == 1)
                {
                    Task<ServiceResult> nestedWriteTask = variable.WriteAttributeAsync(
                        harness.Owner,
                        Attributes.Value,
                        NumericRange.Null,
                        new DataValue(Variant.From(1u)),
                        nestedWriteCancellation.Token).AsTask();
                    nestedWriteStarted.TrySetResult(nestedWriteTask);
                    await nestedWriteTask.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)
                        .ConfigureAwait(false);
                    await release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    synchronousWriteStatus = Write(
                        harness.Owner, variable, Variant.From(2u)).StatusCode;
                }
            };

            Task flush = Task.CompletedTask;
            try
            {
                AssertGood(await WriteAsync(harness.Owner, variable, Variant.From(4u))
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false));
                Assert.That(calls, Is.Zero);
                Assert.That(variable.ChangeMasks & NodeStateChangeMasks.Value,
                    Is.EqualTo(NodeStateChangeMasks.Value));
                Assert.That(text.ChangeMasks & NodeStateChangeMasks.Value,
                    Is.EqualTo(NodeStateChangeMasks.Value));
                Assert.That(text.Value.Text, Is.EqualTo("four"));
                flush = variable.ClearChangeMasksAsync(harness.Context, true).AsTask();
                Task<ServiceResult> nestedWriteTask = await nestedWriteStarted.Task
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                AssertGood(await nestedWriteTask.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false));
                Assert.That(Write(harness.Owner, variable, Variant.From(1u)).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(variable.Value.GetUInt32(), Is.EqualTo(1u));
                Assert.That(text.Value.Text, Is.EqualTo("one"));
                Assert.That(flush.IsCompleted, Is.False);
                Assert.That(observedText, Is.EqualTo("four"));
            }
            finally
            {
                release.TrySetResult(true);
                await flush.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(observedMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
            Assert.That(text.ChangeMasks & NodeStateChangeMasks.Value,
                Is.EqualTo(NodeStateChangeMasks.Value));
            Assert.That(variable.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            await variable.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(synchronousWriteStatus, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(variable.Value.GetUInt32(), Is.EqualTo(1u));
            Assert.That(text.Value.Text, Is.EqualTo("one"));
            Assert.That(text.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(variable.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// An overlapping flush cannot consume a same-node deferral created by the earlier flush's callback.
        /// </summary>
        [Test]
        public async Task NativeEnumerationOverlappingFlushPreservesReentrantDeferralAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateNativeEnumeration(product);
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            var firstCallbackEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var allowNestedWrite = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var nestedWriteCompleted = new TaskCompletionSource<ServiceResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirstFlush = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            StatusCode synchronousWriteStatus = StatusCodes.Good;
            PropertyState<LocalizedText> text = variable.ValueAsText!;
            text.OnStateChangedAsync = async (_, _, _, cancellationToken) =>
            {
                calls++;
                if (calls == 1)
                {
                    firstCallbackEntered.TrySetResult(true);
                    await allowNestedWrite.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)
                        .ConfigureAwait(false);
                    ServiceResult nestedWriteResult = await variable.WriteAttributeAsync(
                        harness.Owner,
                        Attributes.Value,
                        NumericRange.Null,
                        new DataValue(Variant.From(1u)),
                        cancellationToken).AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)
                        .ConfigureAwait(false);
                    nestedWriteCompleted.TrySetResult(nestedWriteResult);
                    await releaseFirstFlush.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    synchronousWriteStatus = Write(
                        harness.Owner, variable, Variant.From(4u)).StatusCode;
                }
            };

            Task firstFlush = Task.CompletedTask;
            Task overlappingFlush = Task.CompletedTask;
            try
            {
                AssertGood(await WriteAsync(harness.Owner, variable, Variant.From(4u)).ConfigureAwait(false));
                firstFlush = variable.ClearChangeMasksAsync(harness.Context, true).AsTask();
                await firstCallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);

                overlappingFlush = variable.ClearChangeMasksAsync(harness.Context, true).AsTask();
                await overlappingFlush.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);

                allowNestedWrite.TrySetResult(true);
                AssertGood(await nestedWriteCompleted.Task.WaitAsync(TimeSpan.FromSeconds(15))
                    .ConfigureAwait(false));
                Assert.That(Write(harness.Owner, variable, Variant.From(4u)).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(variable.Value.GetUInt32(), Is.EqualTo(1u));
                Assert.That(text.Value.Text, Is.EqualTo("one"));
                Assert.That(firstFlush.IsCompleted, Is.False);
            }
            finally
            {
                allowNestedWrite.TrySetResult(true);
                releaseFirstFlush.TrySetResult(true);
                await Task.WhenAll(firstFlush, overlappingFlush)
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(text.ChangeMasks & NodeStateChangeMasks.Value,
                Is.EqualTo(NodeStateChangeMasks.Value));
            Assert.That(variable.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            await variable.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(synchronousWriteStatus, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(variable.Value.GetUInt32(), Is.EqualTo(1u));
            Assert.That(text.Value.Text, Is.EqualTo("one"));
            Assert.That(variable.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(text.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// A flush that began before a reentrant write retains notification admission until dispatch completes.
        /// </summary>
        [Test]
        public async Task NativeEnumerationOverlappingFlushRetainsNotificationAdmissionAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateNativeEnumeration(product);
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            var firstCallbackEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var allowNestedWrite = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var nestedWriteCompleted = new TaskCompletionSource<ServiceResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirstFlush = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var earlierChildEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseEarlierChild = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            StatusCode synchronousWriteStatus = StatusCodes.Good;
            PropertyState<LocalizedText> text = variable.ValueAsText!;
            text.OnStateChangedAsync = async (_, _, _, cancellationToken) =>
            {
                calls++;
                if (calls == 1)
                {
                    firstCallbackEntered.TrySetResult(true);
                    await allowNestedWrite.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)
                        .ConfigureAwait(false);
                    ServiceResult nestedWriteResult = await variable.WriteAttributeAsync(
                        harness.Owner,
                        Attributes.Value,
                        NumericRange.Null,
                        new DataValue(Variant.From(1u)),
                        cancellationToken).AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)
                        .ConfigureAwait(false);
                    nestedWriteCompleted.TrySetResult(nestedWriteResult);
                    await releaseFirstFlush.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    synchronousWriteStatus = Write(
                        harness.Owner, variable, Variant.From(4u)).StatusCode;
                }
            };
            PropertyState<ArrayOf<EnumValueType>> earlierChild = variable.EnumValues!;
            earlierChild.OnStateChangedAsync = async (_, _, _, cancellationToken) =>
            {
                earlierChildEntered.TrySetResult(true);
                await releaseEarlierChild.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)
                    .ConfigureAwait(false);
            };

            Task firstFlush = Task.CompletedTask;
            Task overlappingFlush = Task.CompletedTask;
            Task delayedFlush = Task.CompletedTask;
            try
            {
                AssertGood(await WriteAsync(harness.Owner, variable, Variant.From(4u)).ConfigureAwait(false));
                firstFlush = variable.ClearChangeMasksAsync(harness.Context, true).AsTask();
                await firstCallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);

                overlappingFlush = variable.ClearChangeMasksAsync(harness.Context, true).AsTask();
                await overlappingFlush.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);

                earlierChild.UpdateChangeMasks(NodeStateChangeMasks.Value);
                delayedFlush = variable.ClearChangeMasksAsync(harness.Context, true).AsTask();
                await earlierChildEntered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                allowNestedWrite.TrySetResult(true);
                AssertGood(await nestedWriteCompleted.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false));
                releaseFirstFlush.TrySetResult(true);
                await firstFlush.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);

                releaseEarlierChild.TrySetResult(true);
                await delayedFlush.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }
            finally
            {
                allowNestedWrite.TrySetResult(true);
                releaseFirstFlush.TrySetResult(true);
                releaseEarlierChild.TrySetResult(true);
                await Task.WhenAll(firstFlush, overlappingFlush, delayedFlush)
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }

            Assert.That(calls, Is.EqualTo(2));
            Assert.That(synchronousWriteStatus, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(variable.Value.GetUInt32(), Is.EqualTo(1u));
            Assert.That(text.Value.Text, Is.EqualTo("one"));
            Assert.That(variable.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(text.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Admission covers a parent mask created after a clean flush entered an earlier child's callback.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeEnumerationInitiallyCleanFlushProtectsParentNotificationAsync(bool queueExternalWrite)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateNativeEnumeration(product);
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            var childWriteCompleted = new TaskCompletionSource<ServiceResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseChild = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            PropertyState<ArrayOf<EnumValueType>> child = variable.EnumValues!;
            child.OnStateChangedAsync = async (_, _, _, cancellationToken) =>
            {
                ServiceResult result = await variable.WriteAttributeAsync(
                    harness.Owner,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(Variant.From(4u)),
                    cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)
                    .ConfigureAwait(false);
                childWriteCompleted.TrySetResult(result);
                await releaseChild.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)
                    .ConfigureAwait(false);
            };
            int calls = 0;
            StatusCode synchronousWriteStatus = StatusCodes.Good;
            ServiceResult nestedWriteResult = StatusCodes.BadInvalidState;
            variable.OnStateChangedAsync = async (_, _, changeMasks, cancellationToken) =>
            {
                calls++;
                Assert.That(changeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
                synchronousWriteStatus = Write(harness.Owner, variable, Variant.From(1u)).StatusCode;
                nestedWriteResult = await variable.WriteAttributeAsync(
                    harness.Owner,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(Variant.From(1u)),
                    cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)
                    .ConfigureAwait(false);
            };

            Assert.That(variable.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(variable.ValueAsText!.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            child.UpdateChangeMasks(NodeStateChangeMasks.Value);
            Task flush = variable.ClearChangeMasksAsync(harness.Context, true).AsTask();
            Task<ServiceResult>? externalWrite = null;
            try
            {
                AssertGood(await childWriteCompleted.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false));
                if (queueExternalWrite)
                {
                    externalWrite = WriteAsync(harness.Owner, variable, Variant.From(4u)).AsTask();
                    Assert.That(externalWrite.IsCompleted, Is.False);
                }
                releaseChild.TrySetResult(true);
                await flush.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                if (externalWrite != null)
                {
                    AssertGood(await externalWrite.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false));
                }
            }
            finally
            {
                releaseChild.TrySetResult(true);
                await flush.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                if (externalWrite != null)
                {
                    await externalWrite.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                }
            }

            Assert.That(calls, Is.EqualTo(1));
            AssertGood(nestedWriteResult);
            Assert.That(synchronousWriteStatus, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(variable.Value.GetUInt32(), Is.EqualTo(queueExternalWrite ? 4u : 1u));
            Assert.That(variable.ValueAsText.Value.Text, Is.EqualTo(queueExternalWrite ? "four" : "one"));
            Assert.That(variable.ChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
            Assert.That(variable.ValueAsText.ChangeMasks & NodeStateChangeMasks.Value,
                Is.EqualTo(NodeStateChangeMasks.Value));
        }

        /// <summary>
        /// A callback failure after an overlapping clear restores its mask without removing dispatch admission.
        /// </summary>
        [Test]
        public async Task NativeEnumerationOverlappingFlushFailurePreservesNotificationAdmissionAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateNativeEnumeration(product);
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failure = new InvalidOperationException("Notification callback failed.");
            PropertyState<LocalizedText> text = variable.ValueAsText!;
            int calls = 0;
            StatusCode synchronousWriteStatus = StatusCodes.Good;
            text.OnStateChangedAsync = async (_, _, changeMasks, cancellationToken) =>
            {
                calls++;
                Assert.That(changeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
                if (calls == 1)
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
                    throw failure;
                }
                synchronousWriteStatus = Write(harness.Owner, variable, Variant.From(1u)).StatusCode;
            };

            AssertGood(await WriteAsync(harness.Owner, variable, Variant.From(4u)).ConfigureAwait(false));
            Task firstFlush = variable.ClearChangeMasksAsync(harness.Context, true).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                await variable.ClearChangeMasksAsync(harness.Context, true).AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
                InvalidOperationException? observed = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await firstFlush.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(observed, Is.SameAs(failure));
            }

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(variable.Value.GetUInt32(), Is.EqualTo(4u));
            Assert.That(text.Value.Text, Is.EqualTo("four"));
            Assert.That(text.ChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
            await variable.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(synchronousWriteStatus, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(variable.Value.GetUInt32(), Is.EqualTo(4u));
            Assert.That(text.Value.Text, Is.EqualTo("four"));
            Assert.That(variable.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(text.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            AssertGood(Write(harness.Owner, variable, Variant.From(1u)));
        }

        /// <summary>
        /// Existing synchronous traversals deliver pending enum notifications inline and restore failed callbacks.
        /// </summary>
        [TestCase("AddProduct", false, false)]
        [TestCase("AddProduct", false, true)]
        [TestCase("AddProduct", true, false)]
        [TestCase("AddProduct", true, true)]
        [TestCase("Select", false, false)]
        [TestCase("Select", false, true)]
        [TestCase("Select", true, false)]
        [TestCase("Select", true, true)]
        [TestCase("UpdateEnumValue", false, false)]
        [TestCase("UpdateEnumValue", false, true)]
        [TestCase("UpdateEnumValue", true, false)]
        [TestCase("UpdateEnumValue", true, true)]
        public async Task NativeEnumerationSynchronousTraversalsPreserveInlineNotificationsAsync(
            string traversal,
            bool callbackThrows,
            bool asynchronousWrite)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ScaleProductionPreset preset = scale.ProductionPreset!;
            ProductState product = preset.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateNativeEnumeration(product);
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            PropertyState<LocalizedText> text = variable.ValueAsText!;
            if (asynchronousWrite)
            {
                AssertGood(await WriteAsync(harness.Owner, variable, Variant.From(4u)).ConfigureAwait(false));
            }
            else
            {
                variable.Value = Variant.From(4u);
                text.Value = new LocalizedText("manual");
            }
            bool returned = false;
            int calls = 0;
            int callerThread = Environment.CurrentManagedThreadId;
            var failure = new InvalidOperationException("Synchronous subscriber failed.");
            text.OnStateChanged = (_, _, masks) =>
            {
                calls++;
                Assert.That(returned, Is.False);
                Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(callerThread));
                Assert.That(masks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
                if (traversal != "UpdateEnumValue")
                {
                    Assert.That(Write(harness.Owner, variable, Variant.From(1u)).StatusCode,
                        Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                }
                if (callbackThrows)
                {
                    throw failure;
                }
            };
            void Traverse()
            {
                switch (traversal)
                {
                    case "AddProduct":
                        preset.AddProduct("C", new LocalizedText("C"),
                            state => ConfigureFillingProduct(harness.Context, state));
                        break;
                    case "Select":
                        AssertGood(preset.Select("B"));
                        break;
                    case "UpdateEnumValue":
                        AssertGood(variable.UpdateEnumValue(harness.Context, new DataValue(Variant.From(1u))));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(traversal));
                }
            }

            if (callbackThrows)
            {
                Assert.That(Assert.Throws<InvalidOperationException>(Traverse), Is.SameAs(failure));
            }
            else
            {
                Traverse();
            }
            returned = true;

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(text.ChangeMasks & NodeStateChangeMasks.Value,
                Is.EqualTo(callbackThrows ? NodeStateChangeMasks.Value : NodeStateChangeMasks.None));
            Assert.That(text.Value.Text,
                Is.EqualTo(traversal == "UpdateEnumValue" ? "one" : asynchronousWrite ? "four" : "manual"));
            if (callbackThrows)
            {
                callbackThrows = false;
                returned = false;
                await text.ClearChangeMasksAsync(harness.Context, false).ConfigureAwait(false);
                returned = true;
                Assert.That(calls, Is.EqualTo(2));
                Assert.That(text.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            }
            AssertGood(Write(harness.Owner, variable, Variant.From(4u)));
        }

        /// <summary>
        /// Synchronous preset traversal allows its awaited sink to finish a same-scale write.
        /// </summary>
        [TestCase("Select", false, false)]
        [TestCase("Select", true, false)]
        [TestCase("Select", true, true)]
        [TestCase("AddProduct", false, false)]
        [TestCase("AddProduct", true, false)]
        [TestCase("AddProduct", true, true)]
        public async Task NativeEnumerationSynchronousPresetTraversalAllowsNestedWriteAsync(
            string traversal,
            bool useScaleAdmission,
            bool throughMethod)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ScaleProductionPreset preset = scale.ProductionPreset!;
            ProductState product = preset.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateNativeEnumeration(product);
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            if (!useScaleAdmission)
            {
                variable.BindWriteOperationExecutor(new NodeWriteOperationExecutor(
                    operation => operation(),
                    (operation, token) => operation(token)));
            }
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var nestedWriteStarted = new TaskCompletionSource<Task<ServiceResult>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            variable.ValueAsText!.OnStateChangedAsync = async (_, _, _, _) =>
            {
                Task<ServiceResult> nestedWrite = variable.WriteAttributeAsync(
                    harness.Owner,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(Variant.From(1u)),
                    cancellation.Token).AsTask();
                nestedWriteStarted.TrySetResult(nestedWrite);
                await nestedWrite.ConfigureAwait(false);
            };
            AssertGood(await WriteAsync(harness.Owner, variable, Variant.From(4u)).ConfigureAwait(false));
            Task<ServiceResult> selection = Task.Run(async () =>
            {
                if (throughMethod)
                {
                    return traversal == "AddProduct"
                        ? await CallAsync(harness.Owner, preset.Preset.AddProduct!, preset.Preset.NodeId,
                            [Variant.From("C"), Variant.From("C"), Variant.From(NodeId.Null)]).ConfigureAwait(false)
                        : await CallAsync(harness.Owner, preset.Preset.SelectProduct!, preset.Preset.NodeId,
                            [Variant.From("B")]).ConfigureAwait(false);
                }
                if (traversal == "AddProduct")
                {
                    preset.AddProduct("C", new LocalizedText("C"),
                        state => ConfigureFillingProduct(harness.Context, state));
                    return ServiceResult.Good;
                }
                return preset.Select("B");
            });
            try
            {
                Task<ServiceResult> nestedWrite = await nestedWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                AssertGood(await nestedWrite.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false));
                AssertGood(await selection.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            }
            finally
            {
                cancellation.Cancel();
                try
                {
                    await selection.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Assert.That(cancellation.IsCancellationRequested, Is.True);
                }
            }
        }

        /// <summary>
        /// Awaited preset sinks can read the preset from another thread and propagate failures to the caller.
        /// </summary>
        [TestCase("AddProduct", false)]
        [TestCase("AddProduct", true)]
        [TestCase("Select", false)]
        [TestCase("Select", true)]
        [TestCase("Deselect", false)]
        [TestCase("Deselect", true)]
        [TestCase("Switch", false)]
        [TestCase("Switch", true)]
        public async Task NativeEnumerationSynchronousPresetSinkReleasesMutationLocksAsync(
            string traversal, bool callbackThrows)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ScaleProductionPreset preset = scale.ProductionPreset!;
            ProductState product = preset.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateNativeEnumeration(product);
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            AssertGood(await WriteAsync(harness.Owner, variable, Variant.From(4u)).ConfigureAwait(false));
            PropertyState<LocalizedText> text = variable.ValueAsText!;
            var failure = new InvalidOperationException("Awaited subscriber failed.");
            var order = new List<string>();
            text.OnStateChanged = (_, _, _) =>
            {
                Assert.That(Write(harness.Owner, variable, Variant.From(1u)).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                order.Add("synchronous");
            };
            text.OnStateChangedAsync = async (_, _, _, cancellationToken) =>
            {
                ProductState? found = await Task.Run(() => preset.Find("A"))
                    .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                    .ConfigureAwait(false);
                Assert.That(found, Is.SameAs(product));
                Assert.That(Write(harness.Owner, variable, Variant.From(1u)).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                order.Add("asynchronous");
                if (callbackThrows)
                {
                    throw failure;
                }
            };
            preset.CurrentProductsChanged += (_, _) =>
            {
                Assert.That(Write(harness.Owner, variable, Variant.From(1u)).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                order.Add("selection");
            };
            void Traverse()
            {
                switch (traversal)
                {
                    case "AddProduct":
                        preset.AddProduct("C", new LocalizedText("C"),
                            state => ConfigureFillingProduct(harness.Context, state));
                        break;
                    case "Select":
                        AssertGood(preset.Select("B"));
                        break;
                    case "Deselect":
                        AssertGood(preset.Deselect("A"));
                        break;
                    case "Switch":
                        AssertGood(preset.Switch("B"));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(traversal));
                }
            }

            if (callbackThrows)
            {
                Assert.That(Assert.Throws<InvalidOperationException>(Traverse), Is.SameAs(failure));
            }
            else
            {
                Traverse();
            }
            Assert.That(order, Is.EqualTo(
                callbackThrows || traversal == "AddProduct"
                    ? s_nodeNotificationOrder
                    : s_selectionNotificationOrder));
            Assert.That(text.ChangeMasks & NodeStateChangeMasks.Value,
                Is.EqualTo(callbackThrows ? NodeStateChangeMasks.Value : NodeStateChangeMasks.None));
            Assert.That(preset.Products.ContainsKey("C"), Is.EqualTo(traversal == "AddProduct"));
            Assert.That(preset.CurrentProducts.ToArray(),
                Is.EqualTo(traversal switch
                {
                    "Select" => s_selectedProducts,
                    "Deselect" => Array.Empty<string>(),
                    "Switch" => s_switchedProducts,
                    _ => s_initialProducts
                }));
            if (callbackThrows)
            {
                callbackThrows = false;
                order.Clear();
                await text.ClearChangeMasksAsync(harness.Context, false).ConfigureAwait(false);
                Assert.That(order, Is.EqualTo(s_nodeNotificationOrder));
                Assert.That(text.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            }
            AssertGood(Write(harness.Owner, variable, Variant.From(1u)));
        }

        /// <summary>
        /// Inline selection notifications admit nested async writes without exposing selection to unrelated writers.
        /// </summary>
        [Test]
        public async Task NativeEnumerationSynchronousSelectionRetainsNotificationAdmissionAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ScaleProductionPreset preset = scale.ProductionPreset!;
            ProductState product = preset.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateNativeEnumeration(product);
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            AssertGood(await WriteAsync(harness.Owner, variable, Variant.From(4u)).ConfigureAwait(false));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            PropertyState<LocalizedText> text = variable.ValueAsText!;
            var order = new List<string>();
            text.OnStateChanged = (_, _, _) => order.Add("synchronous");
            text.OnStateChangedAsync = async (_, _, _, cancellationToken) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
                AssertGood(await WriteAsync(harness.Owner, variable, Variant.From(1u)).ConfigureAwait(false));
                Assert.That(preset.CurrentProducts.ToArray(), Is.EqualTo(s_selectedProducts));
                order.Add("asynchronous");
            };
            preset.CurrentProductsChanged += (_, _) =>
            {
                Assert.That(preset.CurrentProducts.ToArray(), Is.EqualTo(s_selectedProducts));
                Assert.That(Write(harness.Owner, variable, Variant.From(4u)).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                order.Add("selection");
            };

            Task<ServiceResult> selection = Task.Run(() => preset.Select("B"));
            Task<ServiceResult>? externalWrite = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                Assert.That(selection.IsCompleted, Is.False);
                Assert.That(preset.CurrentProducts.ToArray(), Is.EqualTo(s_selectedProducts));
                externalWrite = WriteAsync(harness.Owner, variable, Variant.From(4u)).AsTask();
                Assert.That(externalWrite.IsCompleted, Is.False);
                Assert.That(Write(harness.Owner, variable, Variant.From(1u)).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(preset.Deselect("A").StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                release.TrySetResult(true);
                AssertGood(await selection.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false));
                AssertGood(await externalWrite.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false));
            }
            finally
            {
                release.TrySetResult(true);
                await selection.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                if (externalWrite != null)
                {
                    await externalWrite.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                }
            }

            Assert.That(order, Is.EqualTo(s_selectionNotificationOrder));
            Assert.That(preset.CurrentProducts.ToArray(), Is.EqualTo(s_selectedProducts));
            Assert.That(text.ChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
            AssertGood(Write(harness.Owner, variable, Variant.From(1u)));
        }

        /// <summary>
        /// The tolerance percentage counts accepted items independently of optional counter nodes and rejections.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void CheckweigherTolerancePercentageUsesAcceptedMeasurements(bool acceptedCounter, bool bandCounter)
        {
            using var harness = new Harness();
            var statistic = new CheckweigherStatisticState(null);
            statistic.AddTotalPackages(harness.Context);
            statistic.AddPercentageLowerToleranceLimit(harness.Context);
            if (acceptedCounter)
            {
                statistic.AddTotalPackagesAccepted(harness.Context);
            }
            if (bandCounter)
            {
                statistic.AddPackagesAcceptedWithLowerToleranceLimit1(harness.Context);
            }
            var recorder = new ScaleStatistics(harness.Context, statistic);
            recorder.RecordRejected(1.2, CheckweigherRejectReason.Metal);
            Assert.That(statistic.PercentageLowerToleranceLimit!.WrappedValue.GetDouble(), Is.Zero);
            recorder.RecordAccepted(0.98, belowLowerToleranceLimit1: true);
            recorder.RecordAccepted(1.0);
            recorder.RecordRejected(double.NaN, CheckweigherRejectReason.Vision);
            Assert.That(statistic.PercentageLowerToleranceLimit.WrappedValue.GetDouble(), Is.EqualTo(50.0));
            Assert.That(statistic.TotalPackages!.ItemCount!.WrappedValue.GetUInt64(), Is.EqualTo(4UL));
            if (acceptedCounter)
            {
                Assert.That(statistic.TotalPackagesAccepted!.ItemCount!.WrappedValue.GetUInt64(), Is.EqualTo(2UL));
            }
            recorder.Reset();
            Assert.That(statistic.PercentageLowerToleranceLimit.WrappedValue.GetDouble(), Is.Zero);
            recorder.RecordRejected(1.2, CheckweigherRejectReason.Metal);
            Assert.That(statistic.PercentageLowerToleranceLimit.WrappedValue.GetDouble(), Is.Zero);
            recorder.RecordAccepted(1.0);
            Assert.That(statistic.PercentageLowerToleranceLimit.WrappedValue.GetDouble(), Is.Zero);
            recorder.RecordAccepted(0.98, belowLowerToleranceLimit1: true);
            Assert.That(statistic.PercentageLowerToleranceLimit.WrappedValue.GetDouble(), Is.EqualTo(50.0));
        }

        /// <summary>
        /// Event metadata is published independently of standard or vendor numeric notification ids.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void NotificationMetadataEventsRetainParametersAndHelp(bool vendor)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ScaleEventState? reported = null;
            scale.Scale.OnReportEvent = (_, _, e) => reported = e as ScaleEventState;
            string[] parameters = ["Printer 1", "Label 2"];
            if (vendor)
            {
                scale.Notifications.RaiseVendorEvent(
                    5001, ScaleNotificationCategory.Process, "VENDOR_JAM", new LocalizedText("Jam"),
                    parameters.ToArrayOf(), "https://example.org/help", EventSeverity.High);
            }
            else
            {
                scale.Notifications.RaiseEvent(
                    ScaleNotificationId.LabelFault, new LocalizedText("Jam"),
                    parameters.ToArrayOf(), "https://example.org/help", "VENDOR_JAM", EventSeverity.High);
            }
            Assert.That(reported, Is.Not.Null);
            Assert.That(reported!.AuxParameters!.Value.ToArray(), Is.EqualTo(parameters));
            Assert.That(reported.HelpSource!.Value, Is.EqualTo("https://example.org/help"));
            Assert.That(reported.VendorNotificationId!.Value, Is.EqualTo("VENDOR_JAM"));
            Assert.That(reported.Severity!.Value, Is.EqualTo((ushort)EventSeverity.High));
            Assert.That(reported.NotificationId!.WrappedValue.GetUInt32(),
                Is.EqualTo(vendor ? 5001u : (uint)ScaleNotificationId.LabelFault));
            parameters[0] = "Changed";
            Assert.That(reported.AuxParameters.Value[0], Is.EqualTo("Printer 1"));

            scale.Notifications.RaiseEvent(ScaleNotificationId.LabelFault, new LocalizedText("No metadata"));
            Assert.That(reported!.AuxParameters, Is.Null);
            Assert.That(reported.HelpSource, Is.Null);
            Assert.That(reported.VendorNotificationId, Is.Null);
        }

        /// <summary>
        /// Alarm transitions copy optional metadata and clear omitted fields rather than retaining stale values.
        /// </summary>
        [Test]
        public void NotificationMetadataAlarmTransitionsCopyAndClearFields()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            string[] parameters = ["Power supply 1"];
            Assert.That(scale.Notifications.SetAlarm(
                ScaleNotificationId.PowerSupplyFault, true, new LocalizedText("Down"),
                parameters.ToArrayOf(), "https://example.org/power", "VENDOR_POWER"), Is.True);
            ScaleAlarmState alarm = scale.Notifications.Alarm(ScaleNotificationId.PowerSupplyFault)!;
            Assert.That(alarm.AuxParameters!.Value.ToArray(), Is.EqualTo(parameters));
            Assert.That(alarm.HelpSource!.Value, Is.EqualTo("https://example.org/power"));
            Assert.That(alarm.VendorNotificationId!.Value, Is.EqualTo("VENDOR_POWER"));
            parameters[0] = "Changed";
            Assert.That(alarm.AuxParameters.Value[0], Is.EqualTo("Power supply 1"));
            Assert.That(scale.Notifications.SetAlarm(
                ScaleNotificationId.PowerSupplyFault, true, new LocalizedText("Unchanged")), Is.False);
            Assert.That(alarm.HelpSource.Value, Is.EqualTo("https://example.org/power"));
            Assert.That(scale.Notifications.SetAlarm(
                ScaleNotificationId.PowerSupplyFault, false, new LocalizedText("Recovered")), Is.True);
            Assert.That(alarm.AuxParameters.Value.IsNull, Is.True);
            Assert.That(alarm.HelpSource.WrappedValue.IsNull, Is.True);
            Assert.That(alarm.VendorNotificationId.WrappedValue.IsNull, Is.True);
            Assert.That(scale.Notifications.SetAlarm(
                ScaleNotificationId.PowerSupplyFault, true, new LocalizedText("Down"), []), Is.True);
            Assert.That(alarm.AuxParameters.Value.IsNull, Is.False);
            Assert.That(alarm.AuxParameters.Value.Count, Is.Zero);
        }

        /// <summary>
        /// Both deviation equations retain their configured meaning across reset and synchronous or async recording.
        /// </summary>
        [TestCase(ScaleStatisticsObservationMode.Sample, false, false)]
        [TestCase(ScaleStatisticsObservationMode.Sample, false, true)]
        [TestCase(ScaleStatisticsObservationMode.Sample, true, false)]
        [TestCase(ScaleStatisticsObservationMode.Sample, true, true)]
        [TestCase(ScaleStatisticsObservationMode.FullCheck, false, false)]
        [TestCase(ScaleStatisticsObservationMode.FullCheck, false, true)]
        [TestCase(ScaleStatisticsObservationMode.FullCheck, true, false)]
        [TestCase(ScaleStatisticsObservationMode.FullCheck, true, true)]
        public async Task StatisticDeviationUsesConfiguredObservationModeAsync(
            ScaleStatisticsObservationMode mode, bool asynchronous, bool includeDeviation)
        {
            using var harness = new Harness();
            var statistic = new StatisticState(null);
            statistic.AddTotalPackages(harness.Context);
            if (includeDeviation)
            {
                statistic.TotalPackages!.AddStandardDeviation(harness.Context);
            }
            var recorder = new ScaleStatistics(harness.Context, statistic, mode);
            async Task Record(double weight)
            {
                if (asynchronous)
                {
                    await recorder.RecordAsync(weight).ConfigureAwait(false);
                }
                else
                {
                    recorder.Record(weight);
                }
            }
            void CheckDeviation(double expected, StatusCode status = default)
            {
                if (includeDeviation)
                {
                    Assert.That(statistic.TotalPackages!.StandardDeviation!.WrappedValue.GetDouble(),
                        Is.EqualTo(expected).Within(1e-10));
                    Assert.That(statistic.TotalPackages.StandardDeviation.StatusCode,
                        Is.EqualTo((StatusCode)status));
                }
                else
                {
                    Assert.That(statistic.TotalPackages!.StandardDeviation, Is.Null);
                }
            }
            Assert.That(recorder.ObservationMode, Is.EqualTo(mode));
            CheckDeviation(0, StatusCodes.BadNoData);
            recorder.Reset();
            CheckDeviation(0, StatusCodes.BadNoData);
            await Record(1).ConfigureAwait(false);
            CheckDeviation(0, mode == ScaleStatisticsObservationMode.Sample
                ? StatusCodes.BadNoData : StatusCodes.Good);
            await Record(3).ConfigureAwait(false);
            CheckDeviation(mode == ScaleStatisticsObservationMode.Sample ? Math.Sqrt(2) : 1);
            await Record(5).ConfigureAwait(false);
            CheckDeviation(mode == ScaleStatisticsObservationMode.Sample ? 2 : Math.Sqrt(8.0 / 3));
            recorder.Reset();
            CheckDeviation(0, StatusCodes.BadNoData);
            await Record(-1).ConfigureAwait(false);
            await Record(1).ConfigureAwait(false);
            CheckDeviation(mode == ScaleStatisticsObservationMode.Sample ? Math.Sqrt(2) : 1);
            Assert.That(statistic.TotalPackages!.ItemCount!.WrappedValue.GetUInt64(), Is.EqualTo(2UL));
        }

        /// <summary>
        /// Giveaway uses current nominal weight and accepted-weight moments independently of optional UA counters.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void CheckweigherGiveAwayUsesCurrentNominalWeight(bool acceptedCounter, bool includeGiveAway)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.Checkweigher, (context, state) =>
            {
                var checkweigher = (CheckweigherProductState)state;
                checkweigher.NominalWeight!.WrappedValue = Variant.From(10.0);
                checkweigher.AddStatistic(context);
            });
            var product = (CheckweigherProductState)scale.ProductionPreset!.ActiveProduct!;
            var statistic = (CheckweigherStatisticState)product.Statistic!;
            if (acceptedCounter)
            {
                statistic.AddTotalPackagesAccepted(harness.Context);
            }
            if (includeGiveAway)
            {
                statistic.AddGiveAway(harness.Context);
            }
            var recorder = new ScaleStatistics(harness.Context, statistic, ScaleStatisticsObservationMode.FullCheck);
            void CheckGiveAway(double expected)
            {
                if (includeGiveAway)
                {
                    Assert.That(statistic.GiveAway!.WrappedValue.GetDouble(), Is.EqualTo(expected).Within(1e-10));
                    Assert.That(statistic.GiveAway.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
                }
                else
                {
                    Assert.That(statistic.GiveAway, Is.Null);
                }
            }
            recorder.Reset();
            CheckGiveAway(0);
            recorder.RecordRejected(20, CheckweigherRejectReason.Metal);
            CheckGiveAway(0);
            recorder.RecordAccepted(9);
            CheckGiveAway(-1);
            recorder.RecordAccepted(11);
            CheckGiveAway(0);
            recorder.RecordAccepted(12);
            CheckGiveAway(2);
            recorder.RecordRejected(30, CheckweigherRejectReason.Metal);
            CheckGiveAway(2);
            product.NominalWeight!.WrappedValue = Variant.From(12.0);
            recorder.RecordRejected(double.NaN, CheckweigherRejectReason.Vision);
            CheckGiveAway(-4);
            if (acceptedCounter)
            {
                Assert.That(statistic.TotalPackagesAccepted!.ItemCount!.WrappedValue.GetUInt64(), Is.EqualTo(3UL));
                Assert.That(statistic.TotalPackagesAccepted.MeanValue, Is.Null);
            }
            else
            {
                Assert.That(statistic.TotalPackagesAccepted, Is.Null);
            }
            recorder.Reset();
            CheckGiveAway(0);
            recorder.RecordAccepted(13);
            CheckGiveAway(1);
            if (includeGiveAway)
            {
                product.NominalWeight.StatusCode = StatusCodes.BadOutOfService;
                recorder.RecordRejected(0, CheckweigherRejectReason.Metal);
                Assert.That(statistic.GiveAway!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadOutOfService));
                Assert.That(statistic.GiveAway.WrappedValue.GetDouble(), Is.EqualTo(1));
                product.NominalWeight.StatusCode = StatusCodes.Good;
                product.NominalWeight.WrappedValue = Variant.From("invalid");
                recorder.RecordRejected(0, CheckweigherRejectReason.Metal);
                Assert.That(statistic.GiveAway.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNoData));
                Assert.That(statistic.GiveAway.WrappedValue.GetDouble(), Is.EqualTo(1));
                product.NominalWeight.WrappedValue = Variant.From(14.0);
                product.NominalWeight.StatusCode = StatusCodes.UncertainDataSubNormal;
                recorder.RecordRejected(0, CheckweigherRejectReason.Metal);
                Assert.That(statistic.GiveAway.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.UncertainDataSubNormal));
                Assert.That(statistic.GiveAway.WrappedValue.GetDouble(), Is.EqualTo(-1));
            }
        }

        /// <summary>
        /// Direct recorders accept an explicit nominal weight and reject invalid configuration.
        /// </summary>
        [Test]
        public void StatisticRecorderValidatesModeAndExplicitNominalWeight()
        {
            using var harness = new Harness();
            var statistic = new CheckweigherStatisticState(null);
            statistic.AddGiveAway(harness.Context);
            var recorder = new ScaleStatistics(
                harness.Context, statistic, ScaleStatisticsObservationMode.FullCheck, nominalWeight: 10);
            recorder.RecordAccepted(9);
            Assert.That(statistic.GiveAway!.WrappedValue.GetDouble(), Is.EqualTo(-1));
            recorder.RecordAccepted(double.NaN);
            Assert.That(statistic.GiveAway.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNoData));
            Assert.That(statistic.GiveAway.WrappedValue.GetDouble(), Is.EqualTo(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ScaleStatistics(harness.Context, statistic, (ScaleStatisticsObservationMode)2));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ScaleStatistics(harness.Context, statistic, ScaleStatisticsObservationMode.Sample, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ScaleStatistics(
                    harness.Context, statistic, ScaleStatisticsObservationMode.Sample, double.PositiveInfinity));
            Assert.Throws<ArgumentNullException>(() =>
                new ScaleStatistics(null!, statistic, ScaleStatisticsObservationMode.Sample));
            Assert.Throws<ArgumentNullException>(() =>
                new ScaleStatistics(harness.Context, null!, ScaleStatisticsObservationMode.Sample));
        }

        /// <summary>
        /// Missing nominal weight and nonfinite results keep explicit bad quality instead of publishing a guess.
        /// </summary>
        [Test]
        public void StatisticGiveAwayRejectsMissingNominalWeightAndNonfiniteResult()
        {
            using var harness = new Harness();
            var missingNominalStatistic = new CheckweigherStatisticState(null);
            missingNominalStatistic.AddGiveAway(harness.Context);
            var missingNominal = new ScaleStatistics(
                harness.Context, missingNominalStatistic, ScaleStatisticsObservationMode.FullCheck);
            missingNominal.Reset();
            missingNominal.RecordAccepted(1);
            Assert.That(missingNominalStatistic.GiveAway!.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNoData));
            Assert.That(missingNominalStatistic.GiveAway.WrappedValue.GetDouble(), Is.Zero);

            var statistic = new CheckweigherStatisticState(null);
            statistic.AddGiveAway(harness.Context);
            var recorder = new ScaleStatistics(
                harness.Context, statistic, ScaleStatisticsObservationMode.FullCheck, nominalWeight: 0);
            recorder.RecordAccepted(double.MaxValue);
            Assert.That(statistic.GiveAway!.WrappedValue.GetDouble(), Is.EqualTo(double.MaxValue));
            recorder.RecordAccepted(double.MaxValue);
            Assert.That(statistic.GiveAway.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
            Assert.That(statistic.GiveAway.WrappedValue.GetDouble(), Is.EqualTo(double.MaxValue));
            recorder.Reset();
            Assert.That(statistic.GiveAway.WrappedValue.GetDouble(), Is.Zero);
            Assert.That(statistic.GiveAway.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
        }

        /// <summary>
        /// Every optional counter percentage follows overall packages, including unchanged rejection counters.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task StatisticCounterPercentagesFollowOverallPackagesAsync(bool asynchronous, bool includeTotals)
        {
            using var harness = new Harness();
            var statistic = new CheckweigherStatisticState(null);
            if (includeTotals)
            {
                statistic.AddTotalPackages(harness.Context);
                statistic.TotalPackages!.AddPercentageOfTotal(harness.Context);
                statistic.AddTotalPackagesAccepted(harness.Context);
                statistic.TotalPackagesAccepted!.AddPercentageOfTotal(harness.Context);
            }
            statistic.AddTotalPackagesWeighed(harness.Context);
            statistic.TotalPackagesWeighed!.AddPercentageOfTotal(harness.Context);
            statistic.AddPackagesRejectedByMetal(harness.Context);
            statistic.PackagesRejectedByMetal!.AddPercentageOfTotal(harness.Context);
            statistic.AddPackagesRejectedByVision(harness.Context);
            statistic.PackagesRejectedByVision!.AddPercentageOfTotal(harness.Context);
            var recorder = new ScaleStatistics(
                harness.Context, statistic, ScaleStatisticsObservationMode.FullCheck, nominalWeight: 10);
            async Task Accepted(double weight)
            {
                if (asynchronous)
                {
                    await recorder.RecordAcceptedAsync(weight, false, null).ConfigureAwait(false);
                }
                else
                {
                    recorder.RecordAccepted(weight);
                }
            }
            async Task Rejected(double weight, CheckweigherRejectReason reason)
            {
                if (asynchronous)
                {
                    await recorder.RecordRejectedAsync(weight, reason, null).ConfigureAwait(false);
                }
                else
                {
                    recorder.RecordRejected(weight, reason);
                }
            }
            async Task Reset()
            {
                if (asynchronous)
                {
                    await recorder.ResetAsync(null).ConfigureAwait(false);
                }
                else
                {
                    recorder.Reset();
                }
            }
            await Reset().ConfigureAwait(false);
            Assert.That(statistic.TotalPackagesWeighed.PercentageOfTotal!.WrappedValue.GetDouble(), Is.Zero);
            await Accepted(double.NaN).ConfigureAwait(false);
            Assert.That(statistic.TotalPackagesWeighed.ItemCount!.WrappedValue.GetUInt64(), Is.Zero);
            await Accepted(9).ConfigureAwait(false);
            Assert.That(statistic.TotalPackagesWeighed.PercentageOfTotal.WrappedValue.GetDouble(), Is.EqualTo(50));
            await Rejected(20, CheckweigherRejectReason.Metal).ConfigureAwait(false);
            Assert.That(statistic.PackagesRejectedByMetal.PercentageOfTotal!.WrappedValue.GetDouble(),
                Is.EqualTo(100.0 / 3).Within(1e-10));
            await Rejected(double.NaN, CheckweigherRejectReason.Vision).ConfigureAwait(false);
            Assert.That(statistic.TotalPackagesWeighed.ItemCount.WrappedValue.GetUInt64(), Is.EqualTo(2UL));
            Assert.That(statistic.PackagesRejectedByMetal.ItemCount!.WrappedValue.GetUInt64(), Is.EqualTo(1UL));
            Assert.That(statistic.PackagesRejectedByMetal.PercentageOfTotal.WrappedValue.GetDouble(), Is.EqualTo(25));
            Assert.That(statistic.PackagesRejectedByVision.PercentageOfTotal!.WrappedValue.GetDouble(), Is.EqualTo(25));
            await Accepted(11).ConfigureAwait(false);
            Assert.That(statistic.PackagesRejectedByMetal.PercentageOfTotal.WrappedValue.GetDouble(), Is.EqualTo(20));
            Assert.That(statistic.PackagesRejectedByVision.PercentageOfTotal.WrappedValue.GetDouble(), Is.EqualTo(20));
            Assert.That(statistic.TotalPackagesWeighed.PercentageOfTotal.WrappedValue.GetDouble(), Is.EqualTo(60));
            if (includeTotals)
            {
                Assert.That(statistic.TotalPackages!.PercentageOfTotal!.WrappedValue.GetDouble(), Is.EqualTo(100));
                Assert.That(statistic.TotalPackagesAccepted!.PercentageOfTotal!.WrappedValue.GetDouble(),
                    Is.EqualTo(60));
            }
            else
            {
                Assert.That(statistic.TotalPackages, Is.Null);
                Assert.That(statistic.TotalPackagesAccepted, Is.Null);
            }
            var zone = new ZoneState(null);
            zone.AddZoneStatistic(harness.Context);
            StatisticCounterState zoneCounter = zone.ZoneStatistic!;
            zoneCounter.AddPercentageOfTotal(harness.Context);
            if (asynchronous)
            {
                await recorder.RecordCounterAsync(zoneCounter, 11).ConfigureAwait(false);
            }
            else
            {
                recorder.RecordCounter(zoneCounter, 11);
            }
            Assert.That(zoneCounter.ItemCount!.WrappedValue.GetUInt64(), Is.EqualTo(1UL));
            Assert.That(zoneCounter.Weighed!.WrappedValue.GetBoolean(), Is.True);
            await Reset().ConfigureAwait(false);
            Assert.That(zoneCounter.ItemCount.WrappedValue.GetUInt64(), Is.Zero);
            Assert.That(zoneCounter.PercentageOfTotal!.WrappedValue.GetDouble(), Is.Zero);
            Assert.That(statistic.PackagesRejectedByMetal.PercentageOfTotal.WrappedValue.GetDouble(), Is.Zero);
            Assert.That(statistic.PackagesRejectedByVision.PercentageOfTotal.WrappedValue.GetDouble(), Is.Zero);
            await Rejected(20, CheckweigherRejectReason.Metal).ConfigureAwait(false);
            Assert.That(statistic.PackagesRejectedByMetal.PercentageOfTotal.WrappedValue.GetDouble(), Is.EqualTo(100));
            Assert.That(statistic.TotalPackagesWeighed.PercentageOfTotal.WrappedValue.GetDouble(), Is.EqualTo(100));
            if (asynchronous)
            {
                await recorder.RecordCounterAsync(zoneCounter, 20).ConfigureAwait(false);
            }
            else
            {
                recorder.RecordCounter(zoneCounter, 20);
            }
            Assert.That(zoneCounter.ItemCount.WrappedValue.GetUInt64(), Is.EqualTo(1UL));
            Assert.That(zoneCounter.PercentageOfTotal.WrappedValue.GetDouble(), Is.EqualTo(100));
        }

        /// <summary>
        /// A queued writer runs after notification dispatch and retains its own deferred notification.
        /// </summary>
        [Test]
        public async Task NativeEnumerationTextFlushPreservesQueuedWriterNotificationAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateNativeEnumeration(product);
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            StatusCode synchronousWriteStatus = StatusCodes.Good;
            PropertyState<LocalizedText> text = variable.ValueAsText!;
            text.OnStateChangedAsync = async (_, _, _, cancellationToken) =>
            {
                calls++;
                if (calls == 1)
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    synchronousWriteStatus = Write(
                        harness.Owner, variable, Variant.From(4u)).StatusCode;
                }
            };

            Task flush = Task.CompletedTask;
            Task<ServiceResult>? queuedWrite = null;
            try
            {
                AssertGood(await WriteAsync(harness.Owner, variable, Variant.From(1u)).ConfigureAwait(false));
                flush = variable.ClearChangeMasksAsync(harness.Context, true).AsTask();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);

                queuedWrite = WriteAsync(harness.Owner, variable, Variant.From(4u)).AsTask();
                Assert.That(queuedWrite.IsCompleted, Is.False);
                Assert.That(variable.Value.GetUInt32(), Is.EqualTo(1u));

                release.TrySetResult(true);
                await flush.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                AssertGood(await queuedWrite.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false));
            }
            finally
            {
                release.TrySetResult(true);
                await flush.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                if (queuedWrite is not null)
                {
                    await queuedWrite.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                }
            }

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(variable.Value.GetUInt32(), Is.EqualTo(4u));
            Assert.That(text.Value.Text, Is.EqualTo("four"));
            Assert.That(variable.ChangeMasks & NodeStateChangeMasks.Value,
                Is.EqualTo(NodeStateChangeMasks.Value));
            Assert.That(text.ChangeMasks & NodeStateChangeMasks.Value,
                Is.EqualTo(NodeStateChangeMasks.Value));

            await variable.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(synchronousWriteStatus, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(variable.Value.GetUInt32(), Is.EqualTo(4u));
            Assert.That(text.Value.Text, Is.EqualTo("four"));
            Assert.That(variable.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(text.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Native application hooks retain authority; existing and subsequently added sync hooks are not adapted.
        /// </summary>
        [TestCase("existing", false)]
        [TestCase("existing", true)]
        [TestCase("late", false)]
        [TestCase("late", true)]
        [TestCase("native", false)]
        [TestCase("native", true)]
        public async Task NativeEnumerationPolicyPreservesApplicationHookAuthorityAsync(string policy, bool simple)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateNativeEnumeration(product);
            int synchronousCalls = 0;
            int nativeCalls = 0;
            NodeValueSimpleEventHandler sync = (_, _, ref _) =>
            {
                synchronousCalls++;
                return ServiceResult.Good;
            };
            if (policy is "existing" or "native")
            {
                if (simple)
                {
                    variable.OnSimpleWriteValue = sync;
                }
                else
                {
                    variable.OnWriteValue = (
                        ISystemContext _, NodeState _, NumericRange _, QualifiedName _,
                        ref Variant _, ref StatusCode _, ref DateTimeUtc _) =>
                    {
                        synchronousCalls++;
                        return ServiceResult.Good;
                    };
                }
            }
            if (policy == "native")
            {
                if (simple)
                {
                    variable.OnSimpleWriteValueAsync = (_, _, _, _) =>
                    {
                        nativeCalls++;
                        return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
                    };
                }
                else
                {
                    variable.OnWriteValueAsync = (_, _, _, _, _) =>
                    {
                        nativeCalls++;
                        return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
                    };
                }
            }
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            if (policy == "late")
            {
                if (simple)
                {
                    variable.OnSimpleWriteValue = sync;
                }
                else
                {
                    variable.OnWriteValue = (
                        ISystemContext _, NodeState _, NumericRange _, QualifiedName _,
                        ref Variant _, ref StatusCode _, ref DateTimeUtc _) =>
                    {
                        synchronousCalls++;
                        return ServiceResult.Good;
                    };
                }
            }

            ServiceResult result = await WriteAsync(harness.Owner, variable, Variant.From(4u))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)(
                policy == "native" ? StatusCodes.Good : StatusCodes.BadNotSupported)));
            Assert.That(synchronousCalls, Is.Zero);
            Assert.That(nativeCalls, Is.EqualTo(policy == "native" ? 1 : 0));
            Assert.That(variable.Value.GetUInt32(), Is.EqualTo(policy == "native" ? 4u : 1u));
            Assert.That(variable.ValueAsText!.Value.Text,
                Is.EqualTo(policy == "native" ? "four" : "initial"));
            if (policy == "late")
            {
                variable.OnSimpleWriteValue = null;
                variable.OnWriteValue = null;
                AssertGood(await WriteAsync(harness.Owner, variable, Variant.From(4u)).ConfigureAwait(false));
                Assert.That(variable.ValueAsText.Value.Text, Is.EqualTo("four"));
            }
        }

        /// <summary>
        /// Both initial subtree binding and dynamic registration compose the stock enumeration policy.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeEnumerationPolicyBindsInitialAndDynamicStockVariablesAsync(bool dynamic)
        {
            using var harness = new Harness();
            var variables = new List<MultiStateValueDiscreteState>();
            ScaleHandle scale = harness.CreateScale(
                ScaleKind.AutomaticFilling,
                (context, product) =>
                {
                    ConfigureFillingProduct(context, product);
                    if (!dynamic)
                    {
                        MultiStateValueDiscreteState variable = CreateNativeEnumeration(product);
                        variables.Add(variable);
                        product.AddChild(variable);
                    }
                });
            ProductState active = scale.ProductionPreset!.ActiveProduct!;
            if (dynamic)
            {
                MultiStateValueDiscreteState variable = CreateNativeEnumeration(active);
                variables.Add(variable);
                active.AddChild(variable);
                await active.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            }

            Assert.That(variables, Is.Not.Empty);
            foreach (MultiStateValueDiscreteState variable in variables)
            {
                Assert.That(variable.OnWriteValueAsync, Is.Not.Null);
                int dependentStateUpdates = 0;
                NodeStateChangeMasks textChangeMasks = NodeStateChangeMasks.None;
                PropertyState<LocalizedText> text = variable.ValueAsText!;
                await variable.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
                text.OnStateChangedAsync = (_, _, changeMasks, _) =>
                {
                    dependentStateUpdates++;
                    textChangeMasks |= changeMasks;
                    return default;
                };
                AssertGood(await WriteAsync(harness.Owner, variable, Variant.From(4u)).ConfigureAwait(false));
                Assert.That(variable.Value.GetUInt32(), Is.EqualTo(4u));
                Assert.That(text.Value.Text, Is.EqualTo("four"));
                Assert.That(variable.ChangeMasks & NodeStateChangeMasks.Value,
                    Is.EqualTo(NodeStateChangeMasks.Value));
                Assert.That(text.ChangeMasks & NodeStateChangeMasks.Value,
                    Is.EqualTo(NodeStateChangeMasks.Value));
                Assert.That(dependentStateUpdates, Is.Zero);

                await variable.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
                Assert.That(dependentStateUpdates, Is.EqualTo(1));
                Assert.That(textChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
            }
        }

        /// <summary>
        /// Real generated notification fields retain native sparse writes when their alarm is registered.
        /// </summary>
        [Test]
        public async Task RegisteredNotificationFieldsUseNativeEnumerationPolicyAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ScaleAlarmState alarm = scale.Notifications.EnsureAlarm(ScaleNotificationId.OverloadFault);
            MultiStateValueDiscreteState[] variables = [alarm.NotificationCategory!, alarm.NotificationId!];
            foreach (MultiStateValueDiscreteState variable in variables)
            {
                Assert.That(variable.AccessLevel & AccessLevels.CurrentWrite, Is.Not.Zero);
                Assert.That(variable.OnWriteValueAsync, Is.Not.Null);
                ArrayOf<EnumValueType> entries = variable.EnumValues!.Value;
                Assert.That(entries.Count, Is.GreaterThan(0));
                EnumValueType entry = entries[0];

                AssertGood(await WriteAsync(harness.Owner, variable, Variant.From((uint)entry.Value))
                    .ConfigureAwait(false));

                Assert.That(variable.Value.GetUInt32(), Is.EqualTo((uint)entry.Value));
                Assert.That(variable.ValueAsText!.Value, Is.EqualTo(entry.DisplayName));
            }
        }

        /// <summary>
        /// An application subclass cannot inherit automatic stock enum acceptance.
        /// </summary>
        [Test]
        public async Task DerivedEnumerationRequiresExplicitNativeImplementationAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            var variable = new CustomEnumerationState(product)
            {
                NodeId = new NodeId("CustomEnumeration", 1),
                BrowseName = new QualifiedName("CustomEnumeration", 1),
                DataType = Ua.DataTypeIds.UInt32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                Value = Variant.From(1u)
            };
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);

            Assert.That(variable.OnWriteValueAsync, Is.Null);
            ServiceResult result = await WriteAsync(harness.Owner, variable, Variant.From(4u))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(variable.Value.GetUInt32(), Is.EqualTo(1u));
        }

        /// <summary>
        /// Cancellation before admission cannot mutate either enumeration or dependent text.
        /// </summary>
        [Test]
        public async Task NativeEnumerationCancellationBeforeAdmissionLeavesBothValuesUnchangedAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateNativeEnumeration(product);
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThatAsync(
                async () => await variable.WriteAttributeAsync(
                    harness.Owner,
                    Attributes.Value,
                    NumericRange.Null,
                    new DataValue(Variant.From(4u)),
                    cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());

            Assert.That(variable.Value.GetUInt32(), Is.EqualTo(1u));
            Assert.That(variable.ValueAsText!.Value.Text, Is.EqualTo("initial"));
            Assert.That(variable.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(variable.ValueAsText.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            AssertGood(await WriteAsync(harness.Owner, variable, Variant.From(4u)).ConfigureAwait(false));
            Assert.That(variable.ValueAsText.Value.Text, Is.EqualTo("four"));
        }

        /// <summary>
        /// Cancellation during access validation prevents an ordinary native cache write from committing.
        /// </summary>
        [Test]
        public async Task NativeCacheWriteCancellationAfterAdmissionDoesNotCommitAsync()
        {
            using var harness = new Harness();
            using var cancellation = new CancellationTokenSource();
            ScaleHandle scale = harness.CreateScale(
                ScaleKind.AutomaticFilling,
                (context, product) =>
                {
                    ConfigureFillingProduct(context, product);
                    ((AutomaticFillingProductState)product).TargetWeight!.OnReadUserAccessLevel = (_, _, ref _) =>
                    {
                        cancellation.Cancel();
                        return ServiceResult.Good;
                    };
                });
            TargetItemState target = ((AutomaticFillingProductState)scale.ProductionPreset!.ActiveProduct!)
                .TargetWeight!;
            Variant original = target.WrappedValue;

            ServiceResult result = await target.WriteAttributeAsync(
                harness.Owner,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(Variant.From(15.0)),
                cancellation.Token).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUnexpectedError));
            Assert.That(target.WrappedValue, Is.EqualTo(original));
        }

        /// <summary>
        /// Models an unknown application enumeration subtype.
        /// </summary>
        private sealed class CustomEnumerationState : MultiStateValueDiscreteState
        {
            /// <summary>
            /// Creates the application subtype beneath a product.
            /// </summary>
            internal CustomEnumerationState(NodeState parent)
                : base(parent)
            {
            }
        }

        /// <summary>
        /// Other variable types with synchronous semantic overrides do not receive a cache-only async policy.
        /// </summary>
        [Test]
        public async Task SemanticVariableOverridesDoNotReceiveNativeCachePolicyAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            BaseVariableState[] variables =
            [
                new SelectionListState(product),
                new MultiStateDiscreteState(product)
            ];
            for (int ii = 0; ii < variables.Length; ii++)
            {
                BaseVariableState variable = variables[ii];
                variable.NodeId = new NodeId("SemanticVariable" + ii, 1);
                variable.BrowseName = new QualifiedName("SemanticVariable" + ii, 1);
                variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.Value = Variant.From(1u);
                product.AddChild(variable);
            }
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);

            foreach (BaseVariableState variable in variables)
            {
                Assert.That(variable.OnWriteValueAsync, Is.Null);
                ServiceResult rejected = await WriteAsync(harness.Owner, variable, Variant.From(2u))
                    .ConfigureAwait(false);
                Assert.That(rejected.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
                Assert.That(variable.Value.GetUInt32(), Is.EqualTo(1u));
            }
        }

        /// <summary>
        /// All reference-setting Calls preserve the caller's session before changing cached or product values.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="command"/> is not a supported reference-setting command.
        /// </exception>
        [TestCase(ScaleCommand.SetReferencePieceWeight)]
        [TestCase(ScaleCommand.SetNumberOfReferencePieces)]
        [TestCase(ScaleCommand.StartReference)]
        [Category("SecurityRegression")]
        public async Task SecurityF43ScaleReferenceCallsRespectSessionOwnerAsync(ScaleCommand command)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.PieceCounting);
            PieceCountingController counting = scale.PieceCounting!;
            var product = (PieceCountingProductState)scale.ProductionPreset!.ActiveProduct!;
            AssertGood(counting.SetReferencePieceWeight(2));
            AssertGood(counting.SetNumberOfReferencePieces(4));
            scale.PublishLoad(12);
            await harness.AcquireAsync(product).ConfigureAwait(false);

            (
                MethodState Method,
                ArrayOf<Variant> Inputs,
                double Weight,
                uint Pieces,
                ulong Count) scenario = command switch
                {
                    ScaleCommand.SetReferencePieceWeight => (
                        counting.Scale.SetReferencePieceWeight!,
                        [Variant.From(3u), Variant.From(new ExtensionObject(ScaleUnits.Kilogram))],
                        3, 4u, 4UL),
                    ScaleCommand.SetNumberOfReferencePieces => (
                        counting.Scale.SetNumberOfReferencePieces!, [Variant.From(6u)], 2, 6u, 6UL),
                    ScaleCommand.StartReference => (
                        counting.Scale.StartReference!, [Variant.From(3u)], 4, 3u, 3UL),
                    _ => throw new ArgumentOutOfRangeException(nameof(command))
                };

            AssertDenied(await CallAsync(harness.Other, scenario.Method, scale.NodeId, scenario.Inputs)
                .ConfigureAwait(false));
            AssertReference(counting, product, 2, 4, 6);

            AssertGood(await CallAsync(harness.Owner, scenario.Method, scale.NodeId, scenario.Inputs)
                .ConfigureAwait(false));
            AssertReference(counting, product, scenario.Weight, scenario.Pieces, scenario.Count);
        }

        /// <summary>
        /// Base weighing commands must not bypass the product lock by recalculating CurrentItemCount.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="command"/> is not a supported base weighing command.
        /// </exception>
        [TestCase(ScaleCommand.SetZero)]
        [TestCase(ScaleCommand.SetTare)]
        [TestCase(ScaleCommand.ClearTare)]
        [TestCase(ScaleCommand.SetPresetTare)]
        [TestCase(ScaleCommand.RegisterWeight)]
        [Category("SecurityRegression")]
        public async Task SecurityF43BaseWeighingCallsCannotMutateLockedProductAsync(ScaleCommand command)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(
                ScaleKind.PieceCounting,
                (context, state) => ((PieceCountingProductState)state).AddCurrentItemCount(context));
            PieceCountingController counting = scale.PieceCounting!;
            var product = (PieceCountingProductState)scale.ProductionPreset!.ActiveProduct!;
            AssertGood(counting.SetReferencePieceWeight(0.5));
            scale.PublishLoad(2);
            if (command == ScaleCommand.ClearTare)
            {
                AssertGood(scale.SetPresetTare(1));
            }
            await harness.AcquireAsync(product).ConfigureAwait(false);
            MethodState method = command switch
            {
                ScaleCommand.SetZero => scale.Scale.SetZero!,
                ScaleCommand.SetTare => scale.Scale.SetTare!,
                ScaleCommand.ClearTare => scale.Scale.ClearTare!,
                ScaleCommand.SetPresetTare => scale.Scale.SetPresetTare!,
                ScaleCommand.RegisterWeight => scale.Scale.RegisterWeight!,
                _ => throw new ArgumentOutOfRangeException(nameof(command))
            };
            ArrayOf<Variant> inputs = command == ScaleCommand.SetPresetTare
                ? [Variant.From(1.0), Variant.From(new ExtensionObject(ScaleUnits.Kilogram))]
                : [];
            ScaleReading before = scale.CurrentReading;
            ulong countBefore = counting.CurrentPieceCount;
            Variant productCountBefore = product.CurrentItemCount!.WrappedValue;

            AssertDenied(await CallAsync(harness.Other, method, scale.NodeId, inputs).ConfigureAwait(false));
            Assert.That(scale.CurrentReading.Gross, Is.EqualTo(before.Gross));
            Assert.That(scale.CurrentReading.Net, Is.EqualTo(before.Net));
            Assert.That(scale.CurrentReading.Tare, Is.EqualTo(before.Tare));
            Assert.That(counting.CurrentPieceCount, Is.EqualTo(countBefore));
            Assert.That(product.CurrentItemCount.WrappedValue, Is.EqualTo(productCountBefore));
            Assert.That(scale.RegisteredReading, Is.Null);

            AssertGood(await CallAsync(harness.Owner, method, scale.NodeId, inputs).ConfigureAwait(false));
            ulong expected = command switch
            {
                ScaleCommand.SetZero or ScaleCommand.SetTare => 0,
                ScaleCommand.SetPresetTare => 2,
                _ => 4
            };
            Assert.That(counting.CurrentPieceCount, Is.EqualTo(expected));
            Assert.That(product.CurrentItemCount.WrappedValue.TryGetValue(out uint productCount), Is.True);
            Assert.That(productCount, Is.EqualTo(expected));
            if (command == ScaleCommand.RegisterWeight)
            {
                Assert.That(scale.RegisteredReading, Is.Not.Null);
                Assert.That(scale.RegisteredReading!.Net, Is.EqualTo(2));
            }
        }

        /// <summary>
        /// A selection change during publication cannot redirect an authorized command into a locked product.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF43RemoteCountingPublicationKeepsAuthorizedProductAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(
                ScaleKind.PieceCounting,
                (context, state) =>
                {
                    var product = (PieceCountingProductState)state;
                    product.AddCurrentItemCount(context);
                    product.CurrentItemCount!.WrappedValue = Variant.From(100u);
                });
            ScaleProductionPreset preset = scale.ProductionPreset!;
            var first = (PieceCountingProductState)preset.Find("A")!;
            var locked = (PieceCountingProductState)preset.Find("B")!;
            AssertGood(scale.PieceCounting!.SetReferencePieceWeight(1));
            scale.PublishLoad(2);
            await harness.AcquireAsync(locked).ConfigureAwait(false);
            scale.Scale.CurrentWeight!.OnStateChanged = (_, _, _) => AssertGood(preset.Switch("B"));

            AssertGood(await CallAsync(harness.Other, scale.Scale.SetTare!, scale.NodeId, [])
                .ConfigureAwait(false));

            Assert.That(preset.ActiveProduct, Is.SameAs(locked), "the selection changed during weight publication");
            Assert.That(first.CurrentItemCount!.WrappedValue.TryGetValue(out uint firstCount), Is.True);
            Assert.That(firstCount, Is.Zero);
            Assert.That(locked.CurrentItemCount!.WrappedValue.TryGetValue(out uint lockedCount), Is.True);
            Assert.That(lockedCount, Is.EqualTo(100));
            Assert.That(harness.Locks.GetState(locked.NodeId).Locked, Is.True);
        }

        /// <summary>
        /// Equipment-side setters and direct node updates do not impersonate a remote caller.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF43TrustedEquipmentCanUpdateLockedProductAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.PieceCounting);
            PieceCountingController counting = scale.PieceCounting!;
            var product = (PieceCountingProductState)scale.ProductionPreset!.ActiveProduct!;
            await harness.AcquireAsync(product).ConfigureAwait(false);

            AssertGood(counting.SetReferencePieceWeight(3));
            AssertGood(counting.SetNumberOfReferencePieces(5));
            scale.PublishLoad(12);
            AssertReference(counting, product, 3, 5, 4);
            AssertGood(counting.StartReference(6));
            AssertReference(counting, product, 2, 6, 6);

            product.NumberOfReferencePieces!.WrappedValue = Variant.From(8u);
            Assert.That(ReadUInt32(product.NumberOfReferencePieces), Is.EqualTo(8));
            AssertGood(await WriteAsync(harness.Context, product.NumberOfReferencePieces, Variant.From(9u))
                .ConfigureAwait(false));
            Assert.That(ReadUInt32(product.NumberOfReferencePieces), Is.EqualTo(9));
            Assert.That(harness.Locks.GetState(product.NodeId).Locked, Is.True);
            AssertDenied(await WriteAsync(harness.Other, product.NumberOfReferencePieces, Variant.From(10u))
                .ConfigureAwait(false));
            Assert.That(ReadUInt32(product.NumberOfReferencePieces), Is.EqualTo(9));

            AssertGood(scale.SetTare());
            Assert.That(counting.CurrentPieceCount, Is.Zero);
            AssertGood(scale.ClearTare());
            Assert.That(counting.CurrentPieceCount, Is.EqualTo(6));
            AssertGood(scale.SetPresetTare(2));
            Assert.That(scale.CurrentReading.Net, Is.EqualTo(10));
            AssertGood(scale.ClearTare());
            AssertGood(scale.RegisterWeight());
            Assert.That(scale.RegisteredReading!.Net, Is.EqualTo(12));
            scale.PublishLoad(2);
            AssertGood(scale.SetZero());
            Assert.That(scale.CurrentReading.Net, Is.Zero);
        }

        /// <summary>
        /// Optional targets and their tolerance values remain protected after a real product method creates them.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF43DynamicallyCreatedTargetsKeepProductLockAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(
                ScaleKind.PieceCounting,
                (context, state) =>
                {
                    var pieces = (PieceCountingProductState)state;
                    pieces.AddSetTargetPieceCount(context)
                        .AddSetTargetItemCount(context);
                });
            var product = (PieceCountingProductState)scale.ProductionPreset!.ActiveProduct!;
            await harness.AcquireAsync(product).ConfigureAwait(false);

            AssertDenied(await CallAsync(harness.Other, product.SetTargetPieceCount!, product.NodeId,
                [Variant.From(50u), Variant.From(2u), Variant.From(1u)]).ConfigureAwait(false));
            Assert.That(product.TargetPieceCount, Is.Null);
            AssertGood(await CallAsync(harness.Owner, product.SetTargetPieceCount!, product.NodeId,
                [Variant.From(50u), Variant.From(2u), Variant.From(1u)]).ConfigureAwait(false));
            TargetItemState target = product.TargetPieceCount!;
            AssertDenied(await WriteAsync(harness.Other, target, Variant.From(60u)).ConfigureAwait(false));
            AssertDenied(await WriteAsync(harness.Other, target.PlusTolerance!, Variant.From(10u))
                .ConfigureAwait(false));
            Assert.That(ReadUInt32(target), Is.EqualTo(50));
            Assert.That(ReadUInt32(target.PlusTolerance!), Is.EqualTo(2));
            AssertGood(await WriteAsync(harness.Owner, target, Variant.From(60u)).ConfigureAwait(false));
            AssertGood(await WriteAsync(harness.Owner, target.PlusTolerance!, Variant.From(3u))
                .ConfigureAwait(false));
            Assert.That(ReadUInt32(target), Is.EqualTo(60));
            Assert.That(ReadUInt32(target.PlusTolerance!), Is.EqualTo(3));

            AssertGood(await CallAsync(harness.Owner, product.SetTargetItemCount!, product.NodeId,
                [Variant.From(7u)]).ConfigureAwait(false));
            AssertDenied(await WriteAsync(harness.Other, product.TargetItemCount!, Variant.From(8u))
                .ConfigureAwait(false));
            Assert.That(ReadUInt32(product.TargetItemCount!), Is.EqualTo(7));
        }

        /// <summary>
        /// Later optional children are guarded when their parent publishes its changed child set.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF43LaterOptionalDescendantKeepsProductLockAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling);
            var product = (AutomaticFillingProductState)scale.ProductionPreset!.ActiveProduct!;
            TargetItemState target = product.TargetWeight!;
            await harness.AcquireAsync(product).ConfigureAwait(false);

            target.AddPlusTolerance(harness.Context);
            target.PlusTolerance!.WrappedValue = Variant.From(1.0);
            await target.ClearChangeMasksAsync(harness.Context, includeChildren: true).ConfigureAwait(false);

            AssertDenied(await WriteAsync(harness.Other, target.PlusTolerance, Variant.From(10.0))
                .ConfigureAwait(false));
            Assert.That(ReadDouble(target.PlusTolerance), Is.EqualTo(1));
            AssertGood(await WriteAsync(harness.Owner, target.PlusTolerance, Variant.From(2.0))
                .ConfigureAwait(false));
            Assert.That(ReadDouble(target.PlusTolerance), Is.EqualTo(2));
        }

        /// <summary>
        /// An independently registered new zone inherits its enclosing product's lock rather than its own NodeId.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF43NewZoneValuesKeepEnclosingProductLockAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(
                ScaleKind.Catchweigher,
                (context, state) => ((CatchweigherProductState)state).AddAddZone(context));
            var product = (CatchweigherProductState)scale.ProductionPreset!.ActiveProduct!;
            await harness.AcquireAsync(product).ConfigureAwait(false);
            var outputs = new List<Variant>();
            AssertGood(await CallAsync(harness.Owner, product.AddZone!, product.NodeId,
                [
                    Variant.From(new LocalizedText("Zone")),
                    Variant.From(0.0),
                    Variant.From(20.0),
                    Variant.From(new ExtensionObject(ScaleUnits.Kilogram))
                ], outputs).ConfigureAwait(false));
            Assert.That(outputs, Has.Count.EqualTo(1));
            Assert.That(outputs[0].TryGetValue(out NodeId zoneId), Is.True);
            var zone = (ZoneState)harness.Registered[zoneId];

            AssertDenied(await WriteAsync(harness.Other, zone.LowerLimit!, Variant.From(1.0))
                .ConfigureAwait(false));
            Assert.That(ReadDouble(zone.LowerLimit!), Is.Zero);
            AssertGood(await WriteAsync(harness.Owner, zone.LowerLimit!, Variant.From(2.0))
                .ConfigureAwait(false));
            Assert.That(ReadDouble(zone.LowerLimit!), Is.EqualTo(2));
        }

        /// <summary>
        /// Switching to an unlocked product must not silently deselect another session's locked product.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF43SwitchChecksThePreviousProductLockAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.Simple);
            ScaleProductionPreset preset = scale.ProductionPreset!;
            ProductState previous = preset.ActiveProduct!;
            ProductState next = preset.Find("B")!;
            await harness.AcquireAsync(previous).ConfigureAwait(false);

            AssertDenied(await CallAsync(harness.Other, preset.Preset.SwitchProduct!, preset.Preset.NodeId,
                [Variant.From("B")]).ConfigureAwait(false));
            Assert.That(preset.CurrentProducts, Has.Count.EqualTo(1));
            Assert.That(preset.CurrentProducts[0], Is.EqualTo("A"));
            Assert.That(previous.ProductMode!.Value, Is.True);
            Assert.That(next.ProductMode!.Value, Is.False);

            AssertGood(await CallAsync(harness.Owner, preset.Preset.SwitchProduct!, preset.Preset.NodeId,
                [Variant.From("B")]).ConfigureAwait(false));
            Assert.That(preset.CurrentProducts, Has.Count.EqualTo(1));
            Assert.That(preset.CurrentProducts[0], Is.EqualTo("B"));
            Assert.That(previous.ProductMode.Value, Is.False);
            Assert.That(next.ProductMode.Value, Is.True);
        }

        /// <summary>
        /// Vehicle methods authorize the named product, not the unrelated product currently selected.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF43VehicleCallsAndLateValuesRespectNamedProductLockAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(
                ScaleKind.Vehicle,
                (context, state) => ((VehicleProductState)state).AddGetVehicleInformation(context));
            VehicleController vehicles = scale.Vehicle!;
            ScaleProductionPreset preset = scale.ProductionPreset!;
            var product = (VehicleProductState)preset.Find("B")!;
            product.VehicleId!.Value = "Truck";
            await harness.AcquireAsync(product).ConfigureAwait(false);
            preset.VehicleInformationProvider = _ => new VehicleInformation { Tare = 2 };
            AssertGood(await CallAsync(harness.Owner, product.GetVehicleInformation!, product.NodeId,
                [Variant.From("Truck")]).ConfigureAwait(false));
            AssertDenied(await WriteAsync(harness.Other, product.Tare!, Variant.From(100.0)).ConfigureAwait(false));
            Assert.That(ReadDouble(product.Tare!), Is.EqualTo(2));
            AssertGood(await WriteAsync(harness.Owner, product.Tare!, Variant.From(3.0)).ConfigureAwait(false));

            scale.PublishLoad(40);
            AssertGood(vehicles.InboundWeighing("Truck"));
            Variant inbound = product.InboundWeight!.WrappedValue;
            Variant outbound = product.OutboundWeight!.WrappedValue;
            Variant delta = product.DeltaWeight!.WrappedValue;
            scale.PublishLoad(10);
            foreach (MethodState method in new MethodState[]
            {
                vehicles.Scale.InboundWeighing!,
                vehicles.Scale.OutboundWeighing!,
                vehicles.Scale.OnePassWeighing!
            })
            {
                AssertDenied(await CallAsync(harness.Other, method, scale.NodeId, [Variant.From("Truck")])
                    .ConfigureAwait(false));
                Assert.That(product.InboundWeight.WrappedValue, Is.EqualTo(inbound));
                Assert.That(product.OutboundWeight.WrappedValue, Is.EqualTo(outbound));
                Assert.That(product.DeltaWeight.WrappedValue, Is.EqualTo(delta));
            }

            AssertGood(await CallAsync(harness.Owner, vehicles.Scale.OutboundWeighing!, scale.NodeId,
                [Variant.From("Truck")]).ConfigureAwait(false));
            Assert.That(
                product.DeltaWeight.Value.Gross,
                Is.EqualTo(30),
                "denied calls did not consume the inbound cache");
            AssertGood(await CallAsync(harness.Owner, vehicles.Scale.OnePassWeighing!, scale.NodeId,
                [Variant.From("Truck")]).ConfigureAwait(false));
            Assert.That(product.DeltaWeight.Value.Gross, Is.EqualTo(7));
            scale.PublishLoad(20);
            AssertGood(await CallAsync(harness.Owner, vehicles.Scale.InboundWeighing!, scale.NodeId,
                [Variant.From("Truck")]).ConfigureAwait(false));
            Assert.That(product.InboundWeight.Value.Gross, Is.EqualTo(20));
            Assert.That(preset.ActiveProduct, Is.SameAs(preset.Find("A")));
        }

        /// <summary>
        /// Recipe commands must not alter the active product's report or processing state for a different session.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="command"/> is not a supported recipe command.
        /// </exception>
        [TestCase(ScaleCommand.StartRecipe, false)]
        [TestCase(ScaleCommand.StopRecipe, false)]
        [TestCase(ScaleCommand.ContinueRecipe, false)]
        [TestCase(ScaleCommand.SkipCurrentRecipeElement, false)]
        [TestCase(ScaleCommand.AbortRecipe, false)]
        [TestCase(ScaleCommand.StartRecipe, true)]
        [TestCase(ScaleCommand.StopRecipe, true)]
        [TestCase(ScaleCommand.ContinueRecipe, true)]
        [TestCase(ScaleCommand.SkipCurrentRecipeElement, true)]
        [TestCase(ScaleCommand.AbortRecipe, true)]
        [Category("SecurityRegression")]
        public async Task SecurityF43RecipeCallsRespectProductLockAsync(ScaleCommand command, bool automaticWaits)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(
                ScaleKind.Recipe,
                (_, state) => ((RecipeProductState)state).Report!.Value = default);
            RecipeController recipes = scale.Recipes!;
            await recipes.SetApplicationReleaseAsync(true).ConfigureAwait(false);
            if (automaticWaits)
            {
                recipes.EnableConditionWaits(TimeSpan.FromSeconds(5));
            }
            var product = (RecipeProductState)scale.ProductionPreset!.ActiveProduct!;
            RecipeState recipe = recipes.AddRecipe("Recipe", new LocalizedText("Recipe"));
            recipes.AddRecipeElement(
                recipe,
                new NodeId(ObjectTypes.TimerType, ScaleNamespaceIndices.Resolve(harness.Context.NamespaceUris).Scales),
                "Step",
                recipe.NodeId);
            await harness.AcquireAsync(product).ConfigureAwait(false);
            if (command != ScaleCommand.StartRecipe)
            {
                AssertGood(recipes.Start(recipe.NodeId));
            }
            if (command == ScaleCommand.ContinueRecipe)
            {
                AssertGood(recipes.Stop(recipe.NodeId));
            }
            if (automaticWaits && command != ScaleCommand.StartRecipe)
            {
                AssertGood(scale.ProductionPreset.Switch("B"));
            }
            var otherProduct = (RecipeProductState)scale.ProductionPreset.Find("B")!;
            int otherReportCount = otherProduct.Report!.Value.Count;
            MethodState method = command switch
            {
                ScaleCommand.StartRecipe => recipes.Scale.StartRecipe!,
                ScaleCommand.StopRecipe => recipes.Scale.StopRecipe!,
                ScaleCommand.ContinueRecipe => recipes.Scale.ContinueRecipe!,
                ScaleCommand.SkipCurrentRecipeElement => recipes.Scale.SkipCurrentRecipeElement!,
                ScaleCommand.AbortRecipe => recipes.Scale.AbortRecipe!,
                _ => throw new ArgumentOutOfRangeException(nameof(command))
            };
            RecipeRunState stateBefore = recipes.State;
            RecipeState? activeBefore = recipes.ActiveRecipe;
            ArrayOf<RecipeElementState> elementsBefore = recipes.CurrentElements;
            ArrayOf<RecipeReportElementType> reportBefore = product.Report!.Value;

            AssertDenied(await CallAsync(harness.Other, method, scale.NodeId, [Variant.From(recipe.NodeId)])
                .ConfigureAwait(false));
            Assert.That(recipes.State, Is.EqualTo(stateBefore));
            Assert.That(recipes.ActiveRecipe, Is.SameAs(activeBefore));
            Assert.That(recipes.CurrentElements.ToArray(), Is.EqualTo(elementsBefore.ToArray()));
            Assert.That(product.Report.Value.ToArray(), Is.EqualTo(reportBefore.ToArray()));

            AssertGood(await CallAsync(harness.Owner, method, scale.NodeId, [Variant.From(recipe.NodeId)])
                .ConfigureAwait(false));
            RecipeRunState expected = command switch
            {
                ScaleCommand.StopRecipe => RecipeRunState.Paused,
                ScaleCommand.SkipCurrentRecipeElement or ScaleCommand.AbortRecipe => RecipeRunState.Idle,
                _ => RecipeRunState.Running
            };
            Assert.That(recipes.State, Is.EqualTo(expected));
            Assert.That(product.Report.Value.Count, Is.GreaterThan(reportBefore.Count));
            Assert.That(otherProduct.Report.Value.Count, Is.EqualTo(otherReportCount));
        }

        /// <summary>
        /// Runtime-created products carry both their caller-owned DI lock and descendant write guards.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF43NewProductIsLockedForCreatingSessionAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ScaleProductionPreset preset = scale.ProductionPreset!;
            var outputs = new List<Variant>();
            AssertGood(await CallAsync(harness.Owner, preset.Preset.AddProduct!, preset.Preset.NodeId,
                [Variant.From("New product"), Variant.From("C"), Variant.From(NodeId.Null)], outputs)
                .ConfigureAwait(false));
            var product = (AutomaticFillingProductState)preset.Find("C")!;
            Assert.That(outputs, Has.Count.EqualTo(1));
            Assert.That(outputs[0].TryGetValue(out NodeId productId), Is.True);
            Assert.That(productId, Is.EqualTo(product.NodeId));
            Assert.That(harness.Locks.GetState(productId).Locked, Is.True);

            AssertDenied(await WriteAsync(harness.Other, product.TargetWeight!, Variant.From(10.0))
                .ConfigureAwait(false));
            AssertGood(await WriteAsync(harness.Owner, product.TargetWeight!, Variant.From(20.0))
                .ConfigureAwait(false));
            Assert.That(ReadDouble(product.TargetWeight!), Is.EqualTo(20));
        }

        /// <summary>
        /// Lock denial happens before an existing asynchronous value callback, which remains active for the owner.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF43LockGuardPreservesExistingValueCallbackAsync(bool simple)
        {
            using var harness = new Harness();
            int calls = 0;
            int synchronousCalls = 0;
            bool applicationAllowsWrites = true;
            ScaleHandle scale = harness.CreateScale(
                ScaleKind.AutomaticFilling,
                (context, state) =>
                {
                    ConfigureFillingProduct(context, state);
                    TargetItemState target = ((AutomaticFillingProductState)state).TargetWeight!;
                    target.OnSimpleWriteValue = (_, _, ref _) =>
                    {
                        synchronousCalls++;
                        return StatusCodes.BadInvalidState;
                    };
                    target.OnReadUserAccessLevel = (_, _, ref value) =>
                    {
                        if (!applicationAllowsWrites)
                        {
                            value = (byte)(value & ~AccessLevels.CurrentWrite);
                        }
                        return ServiceResult.Good;
                    };
                    ValueTask<AttributeWriteResult> WriteValue(Variant value)
                    {
                        calls++;
                        ServiceResult result = value.TryGetValue(out double weight) && weight <= 20
                            ? ServiceResult.Good
                            : new ServiceResult(StatusCodes.BadOutOfRange);
                        return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(result));
                    }
                    if (simple)
                    {
                        target.OnSimpleWriteValueAsync = (_, _, value, _) => WriteValue(value);
                    }
                    else
                    {
                        target.OnWriteValueAsync = (_, _, _, value, _) => WriteValue(value);
                    }
                });
            var product = (AutomaticFillingProductState)scale.ProductionPreset!.ActiveProduct!;
            await harness.AcquireAsync(product).ConfigureAwait(false);

            AssertDenied(await WriteAsync(harness.Other, product.TargetWeight!, Variant.From(15.0))
                .ConfigureAwait(false));
            Assert.That(calls, Is.Zero);
            ServiceResult invalid = await WriteAsync(harness.Owner, product.TargetWeight!, Variant.From(21.0))
                .ConfigureAwait(false);
            Assert.That(invalid.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(ReadDouble(product.TargetWeight!), Is.EqualTo(10));
            AssertGood(await WriteAsync(harness.Owner, product.TargetWeight!, Variant.From(15.0))
                .ConfigureAwait(false));
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(ReadDouble(product.TargetWeight!), Is.EqualTo(15));
            Assert.That(synchronousCalls, Is.Zero);
            applicationAllowsWrites = false;
            AssertDenied(await WriteAsync(harness.Owner, product.TargetWeight!, Variant.From(16.0))
                .ConfigureAwait(false));
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(ReadDouble(product.TargetWeight!), Is.EqualTo(15));
            Assert.That(synchronousCalls, Is.Zero);
        }

        /// <summary>
        /// Supplies a filling target whose changed value would change the production result.
        /// </summary>
        private static void ConfigureFillingProduct(ISystemContext context, ProductState product)
        {
            TargetItemState target = ((AutomaticFillingProductState)product).TargetWeight!;
            target.AddPlusTolerance(context)
                .AddMinusTolerance(context);
            target.WrappedValue = Variant.From(10.0);
            target.PlusTolerance!.WrappedValue = Variant.From(1.0);
            target.MinusTolerance!.WrappedValue = Variant.From(1.0);
        }

        /// <summary>
        /// Enters the real method validation and generated callback dispatch path.
        /// </summary>
        private static async ValueTask<ServiceResult> CallAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputs,
            List<Variant>? outputs = null)
        {
            var errors = new List<ServiceResult>();
            ServiceResult result = await method.CallAsync(context, objectId, inputs, errors, outputs ?? [])
                .ConfigureAwait(false);
            foreach (ServiceResult error in errors)
            {
                AssertGood(error);
            }
            return result;
        }

        /// <summary>
        /// Creates an ordinary sparse stock enumeration before explicit product binding.
        /// </summary>
        private static MultiStateValueDiscreteState CreateNativeEnumeration(
            ProductState parent,
            bool generic = false,
            string name = "NativeEnumeration")
        {
            MultiStateValueDiscreteState variable = generic
                ? MultiStateValueDiscreteState<uint>.With<VariantBuilder>(parent)
                : new MultiStateValueDiscreteState(parent);
            variable.NodeId = new NodeId(parent.NodeId.ToString() + "." + name, 1);
            variable.BrowseName = new QualifiedName(name, 1);
            variable.DataType = Ua.DataTypeIds.UInt32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Value = Variant.From(1u);
            variable.EnumValues = PropertyState<ArrayOf<EnumValueType>>.With<StructureBuilder<EnumValueType>>(
                variable,
                new[]
                {
                    new EnumValueType { Value = 1, DisplayName = new LocalizedText("one") },
                    new EnumValueType { Value = 4, DisplayName = new LocalizedText("four") }
                }.ToArrayOf());
            variable.EnumValues.NodeId = new NodeId(variable.NodeId.ToString() + ".EnumValues", 1);
            variable.EnumValues.BrowseName = new QualifiedName(Ua.BrowseNames.EnumValues);
            variable.ValueAsText = PropertyState<LocalizedText>.With<VariantBuilder>(
                variable, new LocalizedText("initial"));
            variable.ValueAsText.NodeId = new NodeId(variable.NodeId.ToString() + ".ValueAsText", 1);
            variable.ValueAsText.BrowseName = new QualifiedName(Ua.BrowseNames.ValueAsText);
            return variable;
        }

        /// <summary>
        /// Uses the ordinary synchronous Value-attribute path, not the lock helper directly.
        /// </summary>
        private static ServiceResult Write(
            ISystemContext context,
            BaseVariableState variable,
            Variant value)
        {
            return variable.WriteAttribute(context, Attributes.Value, default, new DataValue(value));
        }

        /// <summary>
        /// Uses native asynchronous Value handlers without synchronous fallback.
        /// </summary>
        private static ValueTask<ServiceResult> WriteAsync(
            ISystemContext context,
            BaseVariableState variable,
            Variant value)
        {
            return variable.WriteAttributeAsync(context, Attributes.Value, default, new DataValue(value));
        }

        /// <summary>
        /// Checks that both the node values and the controller's live reference retain the expected state.
        /// </summary>
        private static void AssertReference(
            PieceCountingController counting,
            PieceCountingProductState product,
            double weight,
            uint pieces,
            ulong count)
        {
            Assert.That(counting.ReferencePieceWeight, Is.EqualTo(weight));
            Assert.That(ReadDouble(product.ReferencePieceWeight!), Is.EqualTo(weight));
            Assert.That(ReadUInt32(product.NumberOfReferencePieces!), Is.EqualTo(pieces));
            Assert.That(counting.CurrentPieceCount, Is.EqualTo(count));
            Assert.That(counting.Scale.CurrentPieceCount!.WrappedValue.TryGetValue(out uint publishedCount), Is.True);
            Assert.That(publishedCount, Is.EqualTo(count));
        }

        /// <summary>
        /// Requires the exact access-denial status rather than an unrelated failure.
        /// </summary>
        private static void AssertDenied(ServiceResult result)
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied), result.ToString());
        }

        /// <summary>
        /// Requires successful dispatch.
        /// </summary>
        private static void AssertGood(ServiceResult result)
        {
            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
        }

        /// <summary>
        /// Reads a real-valued product parameter.
        /// </summary>
        private static double ReadDouble(BaseVariableState variable)
        {
            Assert.That(variable.WrappedValue.TryGetValue(out double value), Is.True);
            return value;
        }

        /// <summary>
        /// Reads a piece-count parameter without boxing.
        /// </summary>
        private static uint ReadUInt32(BaseVariableState variable)
        {
            Assert.That(variable.WrappedValue.TryGetValue(out uint value), Is.True);
            return value;
        }

        /// <summary>The unchanged array used in indexed-write assertions.</summary>
        private static readonly double[] s_initialArray = [1.0, 2.0, 3.0];

        /// <summary>The accepted array expected after the copy-on-write test.</summary>
        private static readonly double[] s_copyOnWriteExpectedArray = [11.0, 12.0, 13.0];

        /// <summary>The valid replacement slice.</summary>
        private static readonly double[] s_slice = [20.0, 30.0];

        /// <summary>The expected indexed-write result.</summary>
        private static readonly double[] s_mergedArray = [1.0, 20.0, 30.0];

        /// <summary>A slice shorter than its requested range.</summary>
        private static readonly double[] s_invalidSlice = [20.0];

        /// <summary>The initial single-product selection.</summary>
        private static readonly string[] s_initialProducts = ["A"];

        /// <summary>The committed two-product selection.</summary>
        private static readonly string[] s_selectedProducts = ["A", "B"];

        /// <summary>The replacement single-product selection.</summary>
        private static readonly string[] s_switchedProducts = ["B"];

        /// <summary>The synchronous and awaited node notification order.</summary>
        private static readonly string[] s_nodeNotificationOrder = ["synchronous", "asynchronous"];

        /// <summary>The node notification order followed by the selection event.</summary>
        private static readonly string[] s_selectionNotificationOrder = ["synchronous", "asynchronous", "selection"];

        /// <summary>
        /// Builds real scale nodes, controllers and locks with in-memory address-space services.
        /// </summary>
        private sealed class Harness : IDisposable
        {
            /// <summary>
            /// Creates different sessions with the same username so identity equality cannot authorize the attacker.
            /// </summary>
            public Harness()
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                var server = new Mock<IServerInternal>();
                var namespaces = new NamespaceTable();
                namespaces.Append("urn:security:f43:instances");
                namespaces.Append(Namespaces.Scales);
                namespaces.Append(Di.Namespaces.OpcUaDi);
                namespaces.Append(Machinery.Namespaces.Machinery);
                namespaces.Append(IA.Namespaces.IA);
                namespaces.Append(PackML.Namespaces.PackML);
                var types = new TypeTable(namespaces);
                types.AddSubtype(Ua.DataTypeIds.BaseDataType, NodeId.Null);
                types.AddSubtype(Ua.DataTypeIds.Structure, Ua.DataTypeIds.BaseDataType);
                types.AddSubtype(Ua.DataTypeIds.EUInformation, Ua.DataTypeIds.Structure);
                server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
                server.SetupGet(value => value.ServerUris).Returns(new StringTable());
                server.SetupGet(value => value.TypeTree).Returns(types);
                server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
                server.SetupGet(value => value.Telemetry).Returns(telemetry);
                Context = new ServerSystemContext(server.Object)
                {
                    NodeIdFactory = new InstanceNodeIdFactory()
                };
                var identity = new Mock<IUserIdentity>();
                identity.SetupGet(value => value.DisplayName).Returns("same-operator");
                identity.SetupGet(value => value.TokenType).Returns(UserTokenType.UserName);
                Owner = Context.Copy();
                Owner.SessionId = new NodeId("owner-session", 1);
                Owner.UserIdentity = identity.Object;
                Other = Context.Copy();
                Other.SessionId = new NodeId("other-session", 1);
                Other.UserIdentity = identity.Object;
                Locks = new DefaultLockService(timeProvider: new FixedTimeProvider());
                m_services = new ScaleRuntimeServices(
                    Context,
                    ScaleNamespaceIndices.Resolve(namespaces),
                    new ScalesServerOptions(),
                    Register,
                    (nodeId, _) => new ValueTask<bool>(Registered.Remove(nodeId)),
                    (type, baseType) => type == baseType,
                    Locks,
                    telemetry.CreateLogger<Harness>());
            }

            /// <summary>
            /// Gets the trusted equipment context.
            /// </summary>
            public ServerSystemContext Context { get; }

            /// <summary>
            /// Gets the product-lock owner's remote request context.
            /// </summary>
            public ServerSystemContext Owner { get; }

            /// <summary>
            /// Gets the independent session with the same username and normal write permission.
            /// </summary>
            public ServerSystemContext Other { get; }

            /// <summary>
            /// Gets the production DI lock service.
            /// </summary>
            public DefaultLockService Locks { get; }

            /// <summary>
            /// Gets the in-memory published nodes.
            /// </summary>
            public Dictionary<NodeId, NodeState> Registered { get; } = [];

            /// <summary>
            /// Uses the same factory, builder and controller creation as the server runtime.
            /// </summary>
            public ScaleHandle CreateScale(
                ScaleKind kind,
                Action<ISystemContext, ProductState>? configureProduct = null,
                bool lockable = true)
            {
                ScaleDeviceState scale = ScaleNodes.CreateScale(
                    Context,
                    kind,
                    new FolderState(null),
                    new QualifiedName("Scale", 1));
                var builder = new ScaleBuilder(m_services, scale, kind);
                builder.WithIdentification(ScalesNodeManagerTests.Identity("SecurityF43"))
                    .WithWeighingRange(new WeighingRangeDefinition(0, 100, 0.01, 0.01))
                    .WithZeroAndTare()
                    .WithRegisterWeight()
                    .WithTypeFeatures()
                    .WithProductionPreset(preset =>
                    {
                        preset.AllowManagement().AllowSelection();
                        if (lockable)
                        {
                            preset.WithLocking();
                        }
                        preset.AddProduct("A", new LocalizedText("A"), configureProduct)
                            .AddProduct("B", new LocalizedText("B"), configureProduct)
                            .Select("A");
                    });
                builder.Finish();
                m_services.Register(scale);
                return builder.CreateHandle();
            }

            /// <summary>
            /// Acquires the real product lock through its remotely callable DI method.
            /// </summary>
            public async ValueTask AcquireAsync(ProductState product)
            {
                Assert.That(product.Lock, Is.Not.Null);
                var outputs = new List<Variant>();
                AssertGood(await CallAsync(Owner, product.Lock!.InitLock!, product.Lock.NodeId,
                    [Variant.From("owner")], outputs).ConfigureAwait(false));
                Assert.That(outputs, Has.Count.EqualTo(1));
                Assert.That(outputs[0].TryGetValue(out int status), Is.True);
                Assert.That(status, Is.EqualTo(LockStatus.Ok));
            }

            /// <summary>
            /// Releases the lock using the owning session, not a privileged break operation.
            /// </summary>
            public async ValueTask ReleaseAsync(ProductState product)
            {
                var outputs = new List<Variant>();
                AssertGood(await CallAsync(Owner, product.Lock!.ExitLock!, product.Lock.NodeId, [], outputs)
                    .ConfigureAwait(false));
                Assert.That(outputs, Has.Count.EqualTo(1));
                Assert.That(outputs[0].TryGetValue(out int status), Is.True);
                Assert.That(status, Is.EqualTo(LockStatus.Ok));
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_services.Dispose();
                Locks.Dispose();
            }

            /// <summary>
            /// Completes each generated node's ordinary registration lifecycle and indexes the subtree.
            /// </summary>
            private void Register(NodeState node)
            {
                node.CreateAsPredefinedNode(Context);
                Registered[node.NodeId] = node;
                var children = new List<BaseInstanceState>();
                node.GetChildren(Context, children);
                foreach (BaseInstanceState child in children)
                {
                    Register(child);
                }
            }

            /// <summary>The real runtime dependencies.</summary>
            private readonly ScaleRuntimeServices m_services;
        }

        /// <summary>
        /// Supplies unique instance NodeIds without a node manager or external state.
        /// </summary>
        private sealed class InstanceNodeIdFactory : INodeIdFactory
        {
            /// <inheritdoc/>
            public NodeId New(ISystemContext context, NodeState node)
            {
                return new NodeId((uint)Interlocked.Increment(ref m_nextId), 1);
            }

            /// <summary>The last allocated instance id.</summary>
            private int m_nextId;
        }

        /// <summary>
        /// Keeps lock ownership independent of wall-clock scheduling.
        /// </summary>
        private sealed class FixedTimeProvider : TimeProvider
        {
            /// <inheritdoc/>
            public override DateTimeOffset GetUtcNow()
            {
                return new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
            }
        }

        /// <summary>
        /// The installed full cache policy continues to own ordinary indexed writes until overridden.
        /// </summary>
        [Test]
        public async Task StockFullCachePolicyRetainsNativeIndexedWritesAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            BaseDataVariableState variable = CreateLateHookArray(product);
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            Assert.That(variable.OnWriteValueAsync, Is.Not.Null);
            Assert.That(variable.OnSimpleWriteValueAsync, Is.Null);
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 5);

            ServiceResult result = await variable.WriteAttributeAsync(
                harness.Owner, Attributes.Value, NumericRange.Parse("1:2"),
                new DataValue(Variant.From(s_slice), StatusCodes.Uncertain, timestamp)).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(variable.Value.GetDoubleArray(), Is.EqualTo(s_mergedArray.ToArrayOf()));
            Assert.That(variable.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
            Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
            Assert.That(variable.ChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
        }

        /// <summary>
        /// A later simple handler wins while the registered full policy remains installed.
        /// </summary>
        [Test]
        public async Task LateSimpleHookWinsOverStockFullPolicyAndPublishesAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            var product = (AutomaticFillingProductState)scale.ProductionPreset!.ActiveProduct!;
            TargetItemState target = product.TargetWeight!;
            target.AccessLevel |= AccessLevels.StatusWrite | AccessLevels.TimestampWrite;
            target.UserAccessLevel |= AccessLevels.StatusWrite | AccessLevels.TimestampWrite;
            await target.ClearChangeMasksAsync(harness.Context, false).ConfigureAwait(false);
            NodeValueWriteEventHandlerAsync? stock = target.OnWriteValueAsync;
            Assert.That(stock, Is.Not.Null);
            int calls = 0;
            target.OnSimpleWriteValueAsync = (context, node, value, cancellationToken) =>
            {
                calls++;
                Assert.That(context, Is.SameAs(harness.Owner));
                Assert.That(node, Is.SameAs(target));
                Assert.That(value.GetDouble(), Is.EqualTo(17.5));
                Assert.That(cancellationToken, Is.EqualTo(CancellationToken.None));
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(StatusCodes.GoodClamped));
            };
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 5);

            ServiceResult result = await target.WriteAttributeAsync(
                harness.Owner, Attributes.Value, NumericRange.Null,
                new DataValue(Variant.From(17.5), StatusCodes.Uncertain, timestamp)).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.GoodClamped));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(target.OnWriteValueAsync, Is.SameAs(stock));
            Assert.That(ReadDouble(target), Is.EqualTo(17.5));
            Assert.That(target.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
            Assert.That(target.Timestamp, Is.EqualTo(timestamp));
            Assert.That(target.ChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
        }

        /// <summary>
        /// Failure and explicit external ownership leave the previous cached observation untouched.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LateSimpleHookFailureOrSuppressionDoesNotPublishAsync(bool suppress)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            var product = (AutomaticFillingProductState)scale.ProductionPreset!.ActiveProduct!;
            TargetItemState target = product.TargetWeight!;
            var originalTimestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 1);
            target.StatusCode = StatusCodes.Uncertain;
            target.Timestamp = originalTimestamp;
            await target.ClearChangeMasksAsync(harness.Context, false).ConfigureAwait(false);
            int calls = 0;
            target.OnSimpleWriteValueAsync = (_, _, value, _) =>
            {
                calls++;
                Assert.That(value.GetDouble(), Is.EqualTo(18.0));
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(
                    suppress ? StatusCodes.GoodClamped : StatusCodes.BadOutOfRange)
                {
                    SuppressCacheUpdate = suppress
                });
            };

            ServiceResult result = await WriteAsync(harness.Owner, target, Variant.From(18.0))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)(
                suppress ? StatusCodes.GoodClamped : StatusCodes.BadOutOfRange)));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(ReadDouble(target), Is.EqualTo(10));
            Assert.That(target.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
            Assert.That(target.Timestamp, Is.EqualTo(originalTimestamp));
            Assert.That(target.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// A simple override rejects both otherwise valid and out-of-bounds indexed requests before dispatch.
        /// </summary>
        [TestCase("1:2")]
        [TestCase("4:5")]
        public async Task LateSimpleHookRejectsIndexedWritesBeforeDispatchAsync(string range)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            BaseDataVariableState variable = CreateLateHookArray(product);
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            int calls = 0;
            variable.OnSimpleWriteValueAsync = (_, _, _, _) =>
            {
                calls++;
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
            };
            DateTimeUtc timestamp = variable.Timestamp;

            ServiceResult result = await variable.WriteAttributeAsync(
                harness.Owner, Attributes.Value, NumericRange.Parse(range),
                new DataValue(Variant.From(s_slice))).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadWriteNotSupported));
            Assert.That(calls, Is.Zero);
            Assert.That(variable.Value.GetDoubleArray(), Is.EqualTo(s_initialArray.ToArrayOf()));
            Assert.That(variable.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
            Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
            Assert.That(variable.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Copy-on-write protects the accepted array when the late simple hook publishes it.
        /// </summary>
        [Test]
        public async Task LateSimpleHookCopyOnWriteDetachesArrayAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            BaseDataVariableState variable = CreateLateHookArray(product);
            variable.CopyPolicy = VariableCopyPolicy.CopyOnWrite;
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            double[] input = [11.0, 12.0, 13.0];
            int calls = 0;
            variable.OnSimpleWriteValueAsync = (_, _, value, _) =>
            {
                calls++;
                Assert.That(value.GetDoubleArray(), Is.EqualTo(input.ToArrayOf()));
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
            };
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 5);

            ServiceResult result = await variable.WriteAttributeAsync(
                harness.Owner, Attributes.Value, NumericRange.Null,
                new DataValue(Variant.From(input), StatusCodes.Good, timestamp)).ConfigureAwait(false);
            input[1] = 999;

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(variable.Value.GetDoubleArray(), Is.EqualTo(s_copyOnWriteExpectedArray.ToArrayOf()));
            Assert.That(variable.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
            Assert.That(variable.ChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
        }

        /// <summary>
        /// Replacing the registered full delegate restores full-handler precedence; restoring it yields again.
        /// </summary>
        [Test]
        public async Task CustomFullAsyncHandlerRetainsPrecedenceOverLateSimpleHookAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            var product = (AutomaticFillingProductState)scale.ProductionPreset!.ActiveProduct!;
            TargetItemState target = product.TargetWeight!;
            await target.ClearChangeMasksAsync(harness.Context, false).ConfigureAwait(false);
            NodeValueWriteEventHandlerAsync? stock = target.OnWriteValueAsync;
            Assert.That(stock, Is.Not.Null);
            int fullCalls = 0;
            int simpleCalls = 0;
            target.OnSimpleWriteValueAsync = (_, _, _, _) =>
            {
                simpleCalls++;
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
            };
            target.OnWriteValueAsync = (_, _, range, value, _) =>
            {
                fullCalls++;
                Assert.That(range.IsNull, Is.True);
                Assert.That(value.GetDouble(), Is.EqualTo(19.0));
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(StatusCodes.BadOutOfRange));
            };
            DateTimeUtc timestamp = target.Timestamp;

            ServiceResult rejected = await WriteAsync(harness.Owner, target, Variant.From(19.0))
                .ConfigureAwait(false);
            Assert.That(rejected.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
            Assert.That(fullCalls, Is.EqualTo(1));
            Assert.That(simpleCalls, Is.Zero);
            Assert.That(ReadDouble(target), Is.EqualTo(10));
            Assert.That(target.Timestamp, Is.EqualTo(timestamp));
            Assert.That(target.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));

            target.OnWriteValueAsync = stock;
            AssertGood(await WriteAsync(harness.Owner, target, Variant.From(19.0)).ConfigureAwait(false));
            Assert.That(fullCalls, Is.EqualTo(1));
            Assert.That(simpleCalls, Is.EqualTo(1));
            Assert.That(ReadDouble(target), Is.EqualTo(19));
            Assert.That(target.ChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
        }

        /// <summary>
        /// Sparse enum validation and dependent text remain native when the late simple hook wins.
        /// </summary>
        [TestCase(1u, "one")]
        [TestCase(2u, "two")]
        [TestCase(4u, "four")]
        [TestCase(8u, "eight")]
        public async Task LateSimpleEnumHookPublishesSparseValueAndTextAsync(uint input, string text)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateLateHookEnumeration(product);
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            int calls = 0;
            int valueNotifications = 0;
            int textNotifications = 0;
            NodeStateChangeMasks valueChangeMasks = NodeStateChangeMasks.None;
            NodeStateChangeMasks textChangeMasks = NodeStateChangeMasks.None;
            PropertyState<LocalizedText> textState = variable.ValueAsText!;
            variable.OnStateChangedAsync = (_, _, changeMasks, _) =>
            {
                valueNotifications++;
                valueChangeMasks |= changeMasks;
                return default;
            };
            textState.OnStateChangedAsync = (_, _, changeMasks, _) =>
            {
                textNotifications++;
                textChangeMasks |= changeMasks;
                return default;
            };
            variable.OnSimpleWriteValueAsync = (_, _, value, _) =>
            {
                calls++;
                Assert.That(value.GetUInt32(), Is.EqualTo(input));
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(StatusCodes.GoodClamped));
            };

            ServiceResult result = await WriteAsync(harness.Owner, variable, Variant.From(input))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.GoodClamped));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(variable.Value.GetUInt32(), Is.EqualTo(input));
            Assert.That(variable.ValueAsText!.Value.Text, Is.EqualTo(text));
            Assert.That(variable.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(valueNotifications, Is.Zero);
            Assert.That(textNotifications, Is.Zero);
            Assert.That(variable.ChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
            Assert.That(textState.ChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));

            await variable.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);

            Assert.That(valueNotifications, Is.EqualTo(1));
            Assert.That(textNotifications, Is.EqualTo(1));
            Assert.That(valueChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
            Assert.That(textChangeMasks & NodeStateChangeMasks.Value, Is.EqualTo(NodeStateChangeMasks.Value));
            Assert.That(variable.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(textState.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Invalid sparse values and indexed writes are rejected without invoking the simple hook.
        /// </summary>
        [TestCase(0u, false)]
        [TestCase(3u, false)]
        [TestCase(9u, false)]
        [TestCase(4u, true)]
        public async Task LateSimpleEnumHookRejectsInvalidWritesWithoutPublicationAsync(uint input, bool indexed)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateLateHookEnumeration(product);
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            int calls = 0;
            variable.OnSimpleWriteValueAsync = (_, _, _, _) =>
            {
                calls++;
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
            };
            DateTimeUtc timestamp = variable.Timestamp;

            ServiceResult result = await variable.WriteAttributeAsync(
                harness.Owner, Attributes.Value, indexed ? NumericRange.Parse("0") : NumericRange.Null,
                new DataValue(Variant.From(input))).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)(
                indexed ? StatusCodes.BadWriteNotSupported : StatusCodes.BadOutOfRange)));
            Assert.That(calls, Is.Zero);
            Assert.That(variable.Value.GetUInt32(), Is.EqualTo(1u));
            Assert.That(variable.ValueAsText!.Value.Text, Is.EqualTo("initial"));
            Assert.That(variable.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
            Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
            Assert.That(variable.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(variable.ValueAsText.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// A bad or suppressed enum write cannot update either the cached value or its dependent text.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LateSimpleEnumHookFailureOrSuppressionPreservesTextAsync(bool suppress)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ProductState product = scale.ProductionPreset!.ActiveProduct!;
            MultiStateValueDiscreteState variable = CreateLateHookEnumeration(product);
            product.AddChild(variable);
            await product.ClearChangeMasksAsync(harness.Context, true).ConfigureAwait(false);
            int calls = 0;
            variable.OnSimpleWriteValueAsync = (_, _, value, _) =>
            {
                calls++;
                Assert.That(value.GetUInt32(), Is.EqualTo(4u));
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(
                    suppress ? StatusCodes.GoodClamped : StatusCodes.BadOutOfRange)
                {
                    SuppressCacheUpdate = suppress
                });
            };
            DateTimeUtc timestamp = variable.Timestamp;

            ServiceResult result = await WriteAsync(harness.Owner, variable, Variant.From(4u))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)(
                suppress ? StatusCodes.GoodClamped : StatusCodes.BadOutOfRange)));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(variable.Value.GetUInt32(), Is.EqualTo(1u));
            Assert.That(variable.ValueAsText!.Value.Text, Is.EqualTo("initial"));
            Assert.That(variable.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
            Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
            Assert.That(variable.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(variable.ValueAsText.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        private static BaseDataVariableState CreateLateHookArray(ProductState product)
        {
            return new BaseDataVariableState(product)
            {
                NodeId = new NodeId("LateSimpleArray", 1),
                BrowseName = new QualifiedName("LateSimpleArray", 1),
                DataType = Ua.DataTypeIds.Double,
                ValueRank = ValueRanks.OneDimension,
                AccessLevel = AccessLevels.CurrentReadOrWrite | AccessLevels.StatusWrite | AccessLevels.TimestampWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite |
                    AccessLevels.StatusWrite | AccessLevels.TimestampWrite,
                Value = Variant.From((double[])s_initialArray.Clone()),
                StatusCode = StatusCodes.Uncertain,
                Timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 1)
            };
        }

        private static MultiStateValueDiscreteState CreateLateHookEnumeration(ProductState product)
        {
            MultiStateValueDiscreteState variable = CreateNativeEnumeration(product);
            variable.EnumValues!.Value = new[]
            {
                new EnumValueType { Value = 1, DisplayName = new LocalizedText("one") },
                new EnumValueType { Value = 2, DisplayName = new LocalizedText("two") },
                new EnumValueType { Value = 4, DisplayName = new LocalizedText("four") },
                new EnumValueType { Value = 8, DisplayName = new LocalizedText("eight") }
            }.ToArrayOf();
            variable.StatusCode = StatusCodes.Uncertain;
            variable.Timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 1);
            return variable;
        }
    }
}
