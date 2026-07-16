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
using Opc.Ua.Server.Fluent;
using Opc.Ua.Tests;

// CA2000: BaseObjectState/BaseDataVariableState instances created in the test fixture
// are passed to the builder under test which owns them for the test fixture lifetime.
#pragma warning disable CA2000

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// End-to-end round-trip tests for <see cref="IVariableBuilder{TValue}"/>:
    /// each typed convenience overload registers the appropriate hook
    /// slot on the underlying <see cref="BaseDataVariableState"/>, and
    /// driving the variable through <c>ReadAttribute</c> /
    /// <c>WriteAttribute</c> (sync) and <c>ReadAttributeAsync</c> /
    /// <c>WriteAttributeAsync</c> (async) reproduces the typed values
    /// the user supplied.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class TypedBuilderTests
    {
        private const ushort kNs = 2;
        private static readonly NamespaceTable s_namespaces = new();
        private static readonly TypeTable s_typeTable = new(s_namespaces);

        private static SystemContext CreateContext()
        {
            return new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = s_namespaces,
                TypeTable = s_typeTable
            };
        }

        private static (NodeManagerBuilder Builder, BaseDataVariableState Var)
            CreateBuilderForVariable<TValue>(NodeId dataType)
        {
            _ = typeof(TValue);
            SystemContext ctx = CreateContext();

            var root = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("Root", kNs),
                BrowseName = new QualifiedName("Root", kNs),
                DisplayName = new LocalizedText("Root")
            };

            var v = new BaseDataVariableState(root)
            {
                NodeId = new NodeId("Root.Var", kNs),
                BrowseName = new QualifiedName("Var", kNs),
                DisplayName = new LocalizedText("Var"),
                DataType = dataType,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                StatusCode = StatusCodes.Good
            };
            root.AddChild(v);

            var roots = new Dictionary<QualifiedName, NodeState> { [root.BrowseName] = root };
            var byId = new Dictionary<NodeId, NodeState>
            {
                [root.NodeId] = root,
                [v.NodeId] = v
            };

            var builder = new NodeManagerBuilder(
                ctx,
                nodeManager: Mock.Of<IAsyncNodeManager>(),
                defaultNamespaceIndex: kNs,
                rootResolver: q => roots.TryGetValue(q, out NodeState n) ? n : null,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState n) ? n : null,
                typeIdResolver: _ => []);

            return (builder, v);
        }

        // -----------------------------------------------------------------
        // Resolution
        // -----------------------------------------------------------------

        [Test]
        public void VariableByPathReturnsTypedBuilder()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) =
                CreateBuilderForVariable<int>(DataTypeIds.Int32);

            IVariableBuilder<int> typed = b.Variable<int>("Root/Var");

            Assert.That(typed, Is.Not.Null);
            Assert.That(typed.Node, Is.SameAs(v));
        }

        [Test]
        public void VariableByNodeIdReturnsTypedBuilder()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) =
                CreateBuilderForVariable<int>(DataTypeIds.Int32);

            IVariableBuilder<int> typed = b.Variable<int>(new NodeId("Root.Var", kNs));

            Assert.That(typed, Is.Not.Null);
            Assert.That(typed.Node, Is.SameAs(v));
        }

        [Test]
        public void AsVariableReturnsTypedBuilder()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) =
                CreateBuilderForVariable<int>(DataTypeIds.Int32);

            INodeBuilder nb = b.Node("Root/Var");
            IVariableBuilder<int> typed = nb.AsVariable<int>();

            Assert.That(typed.Node, Is.SameAs(v));
        }

        [Test]
        public void AsVariableThrowsWhenNodeIsNotAVariable()
        {
            SystemContext ctx = CreateContext();
            var folder = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("Root", kNs),
                BrowseName = new QualifiedName("Root", kNs),
                DisplayName = new LocalizedText("Root")
            };
            var roots = new Dictionary<QualifiedName, NodeState> { [folder.BrowseName] = folder };
            var byId = new Dictionary<NodeId, NodeState> { [folder.NodeId] = folder };
            var builder = new NodeManagerBuilder(
                ctx,
                nodeManager: Mock.Of<IAsyncNodeManager>(),
                defaultNamespaceIndex: kNs,
                rootResolver: q => roots.TryGetValue(q, out NodeState n) ? n : null,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState n) ? n : null,
                typeIdResolver: _ => []);

            INodeBuilder nb = builder.Node("Root");

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => nb.AsVariable<int>());
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidArgument));
        }

        // -----------------------------------------------------------------
        // OnRead — sync
        // -----------------------------------------------------------------

        [Test]
        public void OnReadFuncTValueIsInvokedOnValueRead()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) =
                CreateBuilderForVariable<int>(DataTypeIds.Int32);

            int callCount = 0;
            b.Variable<int>("Root/Var").OnRead(() =>
            {
                callCount++;
                return 42;
            });

            var dv = new DataValue();
            ServiceResult result = v.ReadAttribute(
                CreateContext(), Attributes.Value, NumericRange.Null, QualifiedName.Null, ref dv);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(dv.WrappedValue.GetInt32(), Is.EqualTo(42));
            Assert.That(callCount, Is.EqualTo(1));
        }

        [Test]
        public void OnReadFuncContextTValueReceivesSystemContext()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) =
                CreateBuilderForVariable<int>(DataTypeIds.Int32);

            ISystemContext seenContext = null;
            b.Variable<int>("Root/Var").OnRead(c =>
            {
                seenContext = c;
                return 7;
            });

            SystemContext ctx = CreateContext();
            var dv = new DataValue();
            v.ReadAttribute(ctx, Attributes.Value, NumericRange.Null, QualifiedName.Null, ref dv);

            Assert.That(seenContext, Is.SameAs(ctx));
            Assert.That(dv.WrappedValue.GetInt32(), Is.EqualTo(7));
        }

        [Test]
        public void OnReadThrowsForNullGetter()
        {
            (NodeManagerBuilder b, _) =
                CreateBuilderForVariable<int>(DataTypeIds.Int32);

            IVariableBuilder<int> tb = b.Variable<int>("Root/Var");

            Assert.Throws<ArgumentNullException>(() => tb.OnRead((Func<int>)null));
            Assert.Throws<ArgumentNullException>(() => tb.OnRead((Func<ISystemContext, int>)null));
        }

        // -----------------------------------------------------------------
        // OnRead — async
        // -----------------------------------------------------------------

        [Test]
        public async Task OnReadAsyncFuncTValueRoutesThroughAsyncSlot()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) =
                CreateBuilderForVariable<int>(DataTypeIds.Int32);

            int callCount = 0;
            b.Variable<int>("Root/Var").OnRead(async ct =>
            {
                callCount++;
                await Task.Yield();
                return 99;
            });

            // Async-typed OnRead must register OnSimpleReadValueAsync, not the sync slot.
            Assert.That(v.OnSimpleReadValueAsync, Is.Not.Null);
            Assert.That(v.OnSimpleReadValue, Is.Null);

            var dv = new DataValue();
            ServiceResult result;
            (result, dv) = await v.ReadAttributeAsync(
                CreateContext(), Attributes.Value, NumericRange.Null, QualifiedName.Null, dv).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(dv.WrappedValue.GetInt32(), Is.EqualTo(99));
            Assert.That(callCount, Is.EqualTo(1));
        }

        [Test]
        public async Task OnReadAsyncReceivesCancellationToken()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) =
                CreateBuilderForVariable<int>(DataTypeIds.Int32);

            using var cts = new CancellationTokenSource();
            CancellationToken seenToken = default;

            b.Variable<int>("Root/Var").OnRead(ct =>
            {
                seenToken = ct;
                return new ValueTask<int>(33);
            });

            var dv = new DataValue();
            ServiceResult result;
            (result, dv) = await v.ReadAttributeAsync(
                CreateContext(),
                Attributes.Value,
                NumericRange.Null,
                QualifiedName.Null,
                dv,
                cts.Token).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(seenToken, Is.EqualTo(cts.Token));
        }

        // -----------------------------------------------------------------
        // OnWrite — sync
        // -----------------------------------------------------------------

        [Test]
        public void OnWriteActionTValueIsInvokedOnValueWrite()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) =
                CreateBuilderForVariable<double>(DataTypeIds.Double);

            double captured = double.NaN;
            b.Variable<double>("Root/Var").OnWrite(x => captured = x);

            // Action<TValue> overload registers OnSimpleWriteValue (since it
            // only needs the Variant, not the full ref-StatusCode/Timestamp
            // payload). Confirm wiring before we verify behavior.
            Assert.That(v.OnSimpleWriteValue, Is.Not.Null);

            var dv = new DataValue(
                new Variant(2.5),
                StatusCodes.Good,
                DateTimeUtc.Now);
            ServiceResult result = v.WriteAttribute(
                CreateContext(), Attributes.Value, NumericRange.Null, dv);

            Assert.That(ServiceResult.IsGood(result), Is.True,
                $"StatusCode=0x{result.StatusCode.Code:X8}; Inner={result.InnerResult}");
            Assert.That(captured, Is.EqualTo(2.5));
        }

        [Test]
        public void OnWriteActionContextTValueReceivesSystemContext()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) =
                CreateBuilderForVariable<double>(DataTypeIds.Double);

            ISystemContext seenContext = null;
            double captured = 0;
            b.Variable<double>("Root/Var").OnWrite((c, x) =>
            {
                seenContext = c;
                captured = x;
            });

            SystemContext ctx = CreateContext();
            var dv = new DataValue(
                new Variant(11.0),
                StatusCodes.Good,
                DateTimeUtc.Now);
            v.WriteAttribute(ctx, Attributes.Value, NumericRange.Null, dv);

            Assert.That(seenContext, Is.SameAs(ctx));
            Assert.That(captured, Is.EqualTo(11.0));
        }

        [Test]
        public void OnWriteThrowsForNullSetter()
        {
            (NodeManagerBuilder b, _) =
                CreateBuilderForVariable<double>(DataTypeIds.Double);

            IVariableBuilder<double> tb = b.Variable<double>("Root/Var");

            Assert.Throws<ArgumentNullException>(() => tb.OnWrite((Action<double>)null));
            Assert.Throws<ArgumentNullException>(
                () => tb.OnWrite((Action<ISystemContext, double>)null));
        }

        // -----------------------------------------------------------------
        // OnWrite — async
        // -----------------------------------------------------------------

        [Test]
        public async Task OnWriteAsyncRoutesThroughAsyncSlot()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) =
                CreateBuilderForVariable<double>(DataTypeIds.Double);

            double captured = 0;
            b.Variable<double>("Root/Var").OnWrite(async (x, ct) =>
            {
                captured = x;
                await Task.Yield();
            });

            // Async-typed OnWrite must register OnSimpleWriteValueAsync, not the sync slot.
            Assert.That(v.OnSimpleWriteValueAsync, Is.Not.Null);
            Assert.That(v.OnSimpleWriteValue, Is.Null);

            var dv = new DataValue(
                new Variant(7.5),
                StatusCodes.Good,
                DateTimeUtc.Now);
            ServiceResult result = await v.WriteAttributeAsync(
                CreateContext(), Attributes.Value, NumericRange.Null, dv).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(captured, Is.EqualTo(7.5));
        }

        [Test]
        public async Task OnWriteAsyncReceivesContextAndCancellationToken()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) =
                CreateBuilderForVariable<double>(DataTypeIds.Double);

            using var cts = new CancellationTokenSource();
            ISystemContext seenContext = null;
            CancellationToken seenToken = default;

            b.Variable<double>("Root/Var").OnWrite(
                (c, x, ct) =>
                {
                    seenContext = c;
                    seenToken = ct;
                    return default;
                });

            SystemContext ctx = CreateContext();
            var dv = new DataValue(
                new Variant(1.0),
                StatusCodes.Good,
                DateTimeUtc.Now);
            await v.WriteAttributeAsync(
                ctx, Attributes.Value, NumericRange.Null, dv, cts.Token).ConfigureAwait(false);

            Assert.That(seenContext, Is.SameAs(ctx));
            Assert.That(seenToken, Is.EqualTo(cts.Token));
        }

        // -----------------------------------------------------------------
        // Type-marshalling
        // -----------------------------------------------------------------

        [Test]
        public void OnReadHandlesNullFromVariantForReferenceTypes()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) =
                CreateBuilderForVariable<string>(DataTypeIds.String);

            // Hook a writer that captures whatever the framework hands us;
            // we want to verify ReadValue can yield a default(string) if
            // OnRead returns null.
            b.Variable<string>("Root/Var").OnRead(() => null);

            var dv = new DataValue();
            ServiceResult result = v.ReadAttribute(
                CreateContext(), Attributes.Value, NumericRange.Null, QualifiedName.Null, ref dv);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(dv.WrappedValue.IsNull, Is.True);
        }

        [Test]
        public void OnWriteFromVariantUnwrapsTypedValue()
        {
            (NodeManagerBuilder b, BaseDataVariableState v) =
                CreateBuilderForVariable<string>(DataTypeIds.String);

            string captured = null;
            b.Variable<string>("Root/Var").OnWrite(s => captured = s);

            var dv = new DataValue(
                new Variant("hello"),
                StatusCodes.Good,
                DateTimeUtc.Now);
            v.WriteAttribute(CreateContext(), Attributes.Value, NumericRange.Null, dv);

            Assert.That(captured, Is.EqualTo("hello"));
        }
    }
}
