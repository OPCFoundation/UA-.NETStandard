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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Exercises <see cref="WotProjectionBindingRuntimeFactory"/> and
    /// <see cref="WotProjectionBindingRuntime"/> directly against a lightweight
    /// <see cref="Ua.Server.Fluent.NodeManagerBuilder"/> graph (no running
    /// server): direct scalar read/write, ignore rules, conflict/duplicate
    /// diagnostics, lazy single-open channel caching, failed-open retry,
    /// disposal, structured field composition, and generation isolation.
    /// </summary>
    [TestFixture]
    public sealed class WotProjectionBindingRuntimeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task DirectReadReturnsChannelValuePreservingStatusAndTimestamp(bool uncertainResult)
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm readForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText));
            var channel = new FakeWotBindingChannel(readForm);
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 0);
            channel.OnRead = _ => new ValueTask<WotReadResult>(new WotReadResult(
                uncertainResult ? StatusCodes.UncertainInitialValue : StatusCodes.Good,
                new DataValue(new Variant(42), StatusCodes.UncertainInitialValue, timestamp)));
            h.ChannelFactory.SetChannel(readForm, channel);

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readForm)]).ConfigureAwait(false);

            (ServiceResult result, DataValue value) = await h.ScalarVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGoodOrUncertain(result), Is.True);
            Assert.That(value.WrappedValue.TryGetValue(out int read) && read == 42, Is.True);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.UncertainInitialValue));
            Assert.That(value.SourceTimestamp, Is.EqualTo(timestamp));

            Assert.That(runtime, Is.Not.Null);
            await runtime!.DisposeAsync().ConfigureAwait(false);
            Assert.That(channel.DisposeCount, Is.EqualTo(1));
        }

        [TestCaseSource(nameof(s_readQualityCases))]
        public async Task ReadCombinesOperationAndValueQuality(
            bool structured,
            StatusCode operationStatus,
            StatusCode valueStatus,
            StatusCode expectedStatus)
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotTargetMappingDescriptor mapping = structured
                ? new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "A")
                : new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText);
            WotCompiledForm readForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty, mapping);
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 0);
            h.ChannelFactory.SetChannel(readForm, new FakeWotBindingChannel(readForm)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(operationStatus, new DataValue(new Variant(42), valueStatus, timestamp)))
            });

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readForm)]).ConfigureAwait(false);
            BaseDataVariableState variable = structured ? h.StructVar : h.ScalarVar;

            (ServiceResult result, DataValue value) = await variable.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);

            Assert.That(result.StatusCode.Code, Is.EqualTo(expectedStatus.Code));
            Assert.That(value.StatusCode.Code, Is.EqualTo(expectedStatus.Code));
            Assert.That(value.SourceTimestamp, Is.EqualTo(timestamp));
            if (StatusCode.IsBad(expectedStatus))
            {
                Assert.That(value.WrappedValue.IsNull, Is.True);
            }
            else if (structured)
            {
                Assert.That(value.WrappedValue.TryGetValue(out ExtensionObject rootValue), Is.True);
                Assert.That(rootValue.TryGetValue(out IEncodeable? rootEncodeable), Is.True);
                Assert.That(rootEncodeable, Is.TypeOf<TestRootStructure>());
                Assert.That(((TestRootStructure)rootEncodeable!).A, Is.EqualTo(42));
            }
            else
            {
                Assert.That(value.WrappedValue.TryGetValue(out int scalar), Is.True);
                Assert.That(scalar, Is.EqualTo(42));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DirectWriteWritesThroughChannel(bool clamped)
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm writeForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.WriteProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText));
            var channel = new FakeWotBindingChannel(writeForm);
            StatusCode writeStatus = clamped ? StatusCodes.GoodClamped : StatusCodes.Good;
            DataValue? written = null;
            channel.OnWrite = (value, _) =>
            {
                written = value;
                return new ValueTask<WotWriteResult>(new WotWriteResult(writeStatus));
            };
            h.ChannelFactory.SetChannel(writeForm, channel);

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(writeForm)]).ConfigureAwait(false);

            ServiceResult result = await h.ScalarVar.WriteAttributeAsync(
                h.Builder.Context, Attributes.Value, default, new DataValue(new Variant(7))).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(result.StatusCode, Is.EqualTo(writeStatus));
            Assert.That(written, Is.Not.Null);
            Assert.That(written!.Value.WrappedValue.TryGetValue(out int w) && w == 7, Is.True);
        }

        [Test]
        public async Task DirectReadAppliesRequestedIndexRange()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            h.ScalarVar.ValueRank = ValueRanks.OneDimension;
            WotCompiledForm readForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText));
            ArrayOf<int> sourceValues = [1, 2, 3];
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 0);
            h.ChannelFactory.SetChannel(readForm, new FakeWotBindingChannel(readForm)
            {
                OnRead = _ => new ValueTask<WotReadResult>(new WotReadResult(
                    StatusCodes.UncertainInitialValue,
                    new DataValue(new Variant(sourceValues), StatusCodes.UncertainInitialValue, timestamp)))
            });

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readForm)]).ConfigureAwait(false);

            (ServiceResult result, DataValue value) = await h.ScalarVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, NumericRange.Parse("1"), QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.UncertainInitialValue));
            Assert.That(value.SourceTimestamp, Is.EqualTo(timestamp));
            Assert.That(value.WrappedValue.TryGetValue(out ArrayOf<int> slice), Is.True);
            Assert.That(slice.Count, Is.EqualTo(1));
            Assert.That(slice[0], Is.EqualTo(2));

            (_, DataValue fullValue) = await h.ScalarVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);
            Assert.That(fullValue.WrappedValue.TryGetValue(out ArrayOf<int> full), Is.True);
            Assert.That(full.Count, Is.EqualTo(3));
            Assert.That(full[0], Is.EqualTo(1));
            Assert.That(full[1], Is.EqualTo(2));
            Assert.That(full[2], Is.EqualTo(3));
        }

        [Test]
        public async Task IndexedWriteWithoutNativeCapabilityDoesNotWriteWholeValue()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            h.ScalarVar.ValueRank = ValueRanks.OneDimension;
            WotCompiledForm writeForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.WriteProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText));
            var channel = new FakeWotBindingChannel(writeForm);
            h.ChannelFactory.SetChannel(writeForm, channel);

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(writeForm)]).ConfigureAwait(false);
            ArrayOf<int> replacement = [9];

            ServiceResult result = await h.ScalarVar.WriteAttributeAsync(
                h.Builder.Context,
                Attributes.Value,
                NumericRange.Parse("1"),
                new DataValue(new Variant(replacement))).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadWriteNotSupported));
            Assert.That(channel.WriteCount, Is.Zero);
        }

        [Test]
        public async Task IndexedWriteUsesContextualChannelWithoutDroppingRange()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            h.ScalarVar.ValueRank = ValueRanks.OneDimension;
            ArrayOf<int> initial = [1, 2, 3];
            h.ScalarVar.Value = new Variant(initial);
            WotCompiledForm writeForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.WriteProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText));
            WotWriteRequest? captured = null;
            var channel = new Mock<IWotPropertyBindingChannel>();
            channel.SetupGet(c => c.Form).Returns(writeForm);
            channel.Setup(c => c.WriteAsync(It.IsAny<WotWriteRequest>(), It.IsAny<CancellationToken>()))
                .Returns((WotWriteRequest request, CancellationToken _) =>
                {
                    captured = request;
                    return new ValueTask<WotWriteResult>(new WotWriteResult(StatusCodes.GoodClamped));
                });
            h.ChannelFactory.SetChannel(writeForm, channel.Object);

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(writeForm)]).ConfigureAwait(false);
            ArrayOf<int> replacement = [9];

            ServiceResult result = await h.ScalarVar.WriteAttributeAsync(
                h.Builder.Context,
                Attributes.Value,
                NumericRange.Parse("1"),
                new DataValue(new Variant(replacement))).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.GoodClamped));
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.IndexRange.ToString(), Is.EqualTo("1"));
            Assert.That(captured.Context.NamespaceUris, Is.SameAs(h.Builder.Context.NamespaceUris));
            Assert.That(captured.Value.WrappedValue.TryGetValue(out ArrayOf<int> actual), Is.True);
            Assert.That(actual.Count, Is.EqualTo(1));
            Assert.That(actual[0], Is.EqualTo(9));
            Assert.That(h.ScalarVar.Value.TryGetValue(out ArrayOf<int> cached), Is.True);
            Assert.That(cached.Count, Is.EqualTo(3));
            Assert.That(cached[0], Is.EqualTo(1));
            Assert.That(cached[1], Is.EqualTo(9));
            Assert.That(cached[2], Is.EqualTo(3));
            channel.Verify(c => c.WriteAsync(It.IsAny<DataValue>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task IndexedReadUsesNativeRangeWithoutApplyingItTwice()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            h.ScalarVar.ValueRank = ValueRanks.OneDimension;
            WotCompiledForm readForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText));
            WotReadRequest? captured = null;
            ArrayOf<int> source = [1, 2, 3];
            ArrayOf<int> slice = [2];
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 0);
            var channel = new Mock<IWotPropertyBindingChannel>();
            channel.SetupGet(c => c.Form).Returns(readForm);
            channel.Setup(c => c.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.Good, new DataValue(new Variant(source)))));
            channel.Setup(c => c.ReadAsync(It.IsAny<WotReadRequest>(), It.IsAny<CancellationToken>()))
                .Returns((WotReadRequest request, CancellationToken _) =>
                {
                    captured = request;
                    return new ValueTask<WotReadResult>(new WotReadResult(
                        StatusCodes.GoodClamped,
                        new DataValue(new Variant(slice), StatusCodes.GoodClamped, timestamp)));
                });
            h.ChannelFactory.SetChannel(readForm, channel.Object);

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readForm)]).ConfigureAwait(false);

            (ServiceResult result, DataValue value) = await h.ScalarVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, NumericRange.Parse("1"), QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.GoodClamped));
            Assert.That(value.SourceTimestamp, Is.EqualTo(timestamp));
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.IndexRange.ToString(), Is.EqualTo("1"));
            Assert.That(captured.Context.NamespaceUris, Is.SameAs(h.Builder.Context.NamespaceUris));
            Assert.That(value.WrappedValue.TryGetValue(out ArrayOf<int> actual), Is.True);
            Assert.That(actual.Count, Is.EqualTo(1));
            Assert.That(actual[0], Is.EqualTo(2));
            channel.Verify(c => c.ReadAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReadRejectsInvalidDataEncodingName(bool structured)
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotTargetMappingDescriptor mapping = structured
                ? new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "A")
                : new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText);
            WotCompiledForm readForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty, mapping);
            h.ChannelFactory.SetChannel(readForm, new FakeWotBindingChannel(readForm)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.Good, new DataValue(new Variant(1))))
            });
            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readForm)]).ConfigureAwait(false);
            BaseDataVariableState variable = structured ? h.StructVar : h.ScalarVar;

            (ServiceResult result, DataValue value) = await variable.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.From("Unsupported"), new DataValue())
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadDataEncodingInvalid));
            Assert.That(value.WrappedValue.IsNull, Is.True);
        }

        [Test]
        public async Task StructuredIndexedWriteRejectsBeforeWritingFields()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm writeForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.WriteProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "A"));
            var channel = new FakeWotBindingChannel(writeForm);
            h.ChannelFactory.SetChannel(writeForm, channel);
            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(writeForm)]).ConfigureAwait(false);

            ServiceResult result = await h.StructVar.WriteAttributeAsync(
                h.Builder.Context,
                Attributes.Value,
                NumericRange.Parse("1"),
                new DataValue(new Variant(new ExtensionObject(new TestRootStructure { A = 9 }))))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadWriteNotSupported));
            Assert.That(channel.WriteCount, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DirectReadTranslatesSourceNamespaceByUri(bool qualifiedName)
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            h.ScalarVar.DataType = qualifiedName ? Ua.DataTypeIds.QualifiedName : Ua.DataTypeIds.NodeId;
            h.Builder.Context.NamespaceUris.Append("urn:aggregate-only");
            ushort localIndex = h.Builder.Context.NamespaceUris.GetIndexOrAppend("urn:device");
            ServiceMessageContext source = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            source.NamespaceUris.Append("urn:source-only");
            ushort sourceIndex = source.NamespaceUris.GetIndexOrAppend("urn:device");
            Variant sourceValue = qualifiedName
                ? new Variant(new QualifiedName("Sensor", sourceIndex))
                : new Variant(new NodeId("Sensor", sourceIndex));
            var timestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 0);
            WotCompiledForm readForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText));
            h.ChannelFactory.SetChannel(readForm, new FakeWotBindingChannel(readForm)
            {
                OnRead = _ => new ValueTask<WotReadResult>(new WotReadResult(
                    StatusCodes.UncertainInitialValue,
                    new DataValue(sourceValue, StatusCodes.UncertainInitialValue, timestamp)).WithContext(source))
            });
            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readForm)]).ConfigureAwait(false);

            (ServiceResult result, DataValue value) = await h.ScalarVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.UncertainInitialValue));
            Assert.That(value.SourceTimestamp, Is.EqualTo(timestamp));
            if (qualifiedName)
            {
                Assert.That(value.WrappedValue.TryGetValue(out QualifiedName name), Is.True);
                Assert.That(name, Is.EqualTo(new QualifiedName("Sensor", localIndex)));
            }
            else
            {
                Assert.That(value.WrappedValue.TryGetValue(out NodeId nodeId), Is.True);
                Assert.That(nodeId, Is.EqualTo(new NodeId("Sensor", localIndex)));
            }
            Assert.That(source.NamespaceUris.Count, Is.EqualTo(3));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ContextlessWriteRejectsSessionLocalNamespacesBeforeSideEffects(bool sessionLocal)
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            h.ScalarVar.DataType = Ua.DataTypeIds.NodeId;
            WotCompiledForm writeForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.WriteProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText));
            var channel = new FakeWotBindingChannel(writeForm);
            h.ChannelFactory.SetChannel(writeForm, channel);
            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(writeForm)]).ConfigureAwait(false);
            var nodeId = new NodeId("Sensor", sessionLocal ? h.Ns : (ushort)0);

            ServiceResult result = await h.ScalarVar.WriteAttributeAsync(
                h.Builder.Context, Attributes.Value, default, new DataValue(new Variant(nodeId)))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(sessionLocal ? StatusCodes.BadNodeIdInvalid : StatusCodes.Good));
            Assert.That(channel.WriteCount, Is.EqualTo(sessionLocal ? 0 : 1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task StructuredReadTranslatesEachFieldNamespace(bool contextualChannel)
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            h.Builder.Context.NamespaceUris.Append("urn:aggregate-only");
            ushort localIndex = h.Builder.Context.NamespaceUris.GetIndexOrAppend("urn:device");
            ServiceMessageContext source = ServiceMessageContext.CreateEmpty(
                TelemetryExtensions.InternalOnly__TelemetryHook());
            source.NamespaceUris.Append("urn:source-only");
            ushort sourceIndex = source.NamespaceUris.GetIndexOrAppend("urn:device");
            WotCompiledForm readForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(
                    targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "Target"));
            WotReadResult sourceResult = new WotReadResult(
                StatusCodes.Good, new DataValue(new Variant(new NodeId("PayloadType", sourceIndex))))
                .WithContext(source);
            WotReadRequest? captured = null;
            if (contextualChannel)
            {
                var channel = new Mock<IWotPropertyBindingChannel>();
                channel.SetupGet(c => c.Form).Returns(readForm);
                channel.Setup(c => c.ReadAsync(It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<WotReadResult>(
                        new WotReadResult(StatusCodes.BadNotSupported, DataValue.Null)));
                channel.Setup(c => c.ReadAsync(It.IsAny<WotReadRequest>(), It.IsAny<CancellationToken>()))
                    .Returns((WotReadRequest request, CancellationToken _) =>
                    {
                        captured = request;
                        return new ValueTask<WotReadResult>(sourceResult);
                    });
                h.ChannelFactory.SetChannel(readForm, channel.Object);
            }
            else
            {
                h.ChannelFactory.SetChannel(readForm, new FakeWotBindingChannel(readForm)
                {
                    OnRead = _ => new ValueTask<WotReadResult>(sourceResult)
                });
            }
            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readForm)]).ConfigureAwait(false);

            (ServiceResult result, DataValue value) = await h.StructVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good), result.ToString());
            Assert.That(value.WrappedValue.TryGetValue(out ExtensionObject rootValue), Is.True);
            Assert.That(rootValue.TryGetValue(out IEncodeable? encodeable), Is.True);
            Assert.That(encodeable, Is.TypeOf<TestRootStructure>());
            var root = (IStructure)encodeable!;
            Assert.That(root["Target"].TryGetValue(out NodeId dataType), Is.True);
            Assert.That(dataType, Is.EqualTo(new NodeId("PayloadType", localIndex)));
            if (contextualChannel)
            {
                Assert.That(captured, Is.Not.Null);
                Assert.That(captured!.Context.NamespaceUris, Is.SameAs(h.Builder.Context.NamespaceUris));
                Assert.That(captured.IndexRange.IsNull, Is.True);
                Assert.That(captured.DataEncoding.IsNull, Is.True);
            }
        }

        [Test]
        public async Task StructuredWriteCarriesEachFieldsNamespaceContext()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            ushort localIndex = h.Builder.Context.NamespaceUris.GetIndexOrAppend("urn:device");
            WotCompiledForm writeForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.WriteProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "Target"));
            WotWriteRequest? captured = null;
            var channel = new Mock<IWotPropertyBindingChannel>();
            channel.SetupGet(c => c.Form).Returns(writeForm);
            channel.Setup(c => c.WriteAsync(It.IsAny<WotWriteRequest>(), It.IsAny<CancellationToken>()))
                .Returns((WotWriteRequest request, CancellationToken _) =>
                {
                    captured = request;
                    return new ValueTask<WotWriteResult>(new WotWriteResult(StatusCodes.Good));
                });
            channel.Setup(c => c.WriteAsync(It.IsAny<DataValue>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WotWriteResult>(new WotWriteResult(StatusCodes.Good)));
            h.ChannelFactory.SetChannel(writeForm, channel.Object);
            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(writeForm)]).ConfigureAwait(false);
            var incoming = new TestRootStructure { Target = new NodeId("Sensor", localIndex) };

            ServiceResult result = await h.StructVar.WriteAttributeAsync(
                h.Builder.Context, Attributes.Value, default, new DataValue(new Variant(new ExtensionObject(incoming))))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.IndexRange.IsNull, Is.True);
            Assert.That(captured.Context.NamespaceUris, Is.SameAs(h.Builder.Context.NamespaceUris));
            Assert.That(captured.Value.WrappedValue.TryGetValue(out NodeId target), Is.True);
            Assert.That(target, Is.EqualTo(new NodeId("Sensor", localIndex)));
            channel.Verify(c => c.WriteAsync(It.IsAny<DataValue>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task StructuredContextlessWriteRejectsUnknownNamespaceBeforeWritingTheField()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm writeForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.WriteProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "Target"));
            var channel = new FakeWotBindingChannel(writeForm);
            h.ChannelFactory.SetChannel(writeForm, channel);
            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(writeForm)]).ConfigureAwait(false);
            var incoming = new TestRootStructure { Target = new NodeId("Sensor", h.Ns) };

            ServiceResult result = await h.StructVar.WriteAttributeAsync(
                h.Builder.Context, Attributes.Value, default, new DataValue(new Variant(new ExtensionObject(incoming))))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(channel.WriteCount, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ContextlessReadRejectsSessionLocalNamespaces(bool structured)
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotTargetMappingDescriptor mapping = structured
                ? new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "Target")
                : new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText);
            WotCompiledForm readForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty, mapping);
            h.ChannelFactory.SetChannel(readForm, new FakeWotBindingChannel(readForm)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.Good, new DataValue(new Variant(new NodeId("Sensor", h.Ns)))))
            });
            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readForm)]).ConfigureAwait(false);
            BaseDataVariableState variable = structured ? h.StructVar : h.ScalarVar;

            (ServiceResult result, DataValue value) = await variable.ReadAttributeAsync(
                h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue()).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(value.WrappedValue.IsNull, Is.True);
        }

        [Test]
        public async Task OutOfBoundsReadRangeReturnsNoDataAndDoesNotPoisonFullReads()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            h.ScalarVar.ValueRank = ValueRanks.OneDimension;
            WotCompiledForm readForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText));
            ArrayOf<int> source = [1, 2, 3];
            h.ChannelFactory.SetChannel(readForm, new FakeWotBindingChannel(readForm)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.Good, new DataValue(new Variant(source))))
            });
            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readForm)]).ConfigureAwait(false);

            (ServiceResult result, DataValue value) = await h.ScalarVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, NumericRange.Parse("7"), QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
            Assert.That(value.WrappedValue.IsNull, Is.True);
            (_, DataValue whole) = await h.ScalarVar.ReadAttributeAsync(
                h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue()).ConfigureAwait(false);
            Assert.That(whole.WrappedValue.TryGetValue(out ArrayOf<int> actual), Is.True);
            Assert.That(actual.Count, Is.EqualTo(3));
            Assert.That(actual[0], Is.EqualTo(1));
            Assert.That(actual[1], Is.EqualTo(2));
            Assert.That(actual[2], Is.EqualTo(3));
        }

        [Test]
        public async Task FormsWithoutTargetMappingAreIgnored()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm form = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty, WotTargetMappingDescriptor.Empty);

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(form)]).ConfigureAwait(false);

            Assert.That(h.ScalarVar.OnReadValueAsync, Is.Null);
            Assert.That(h.ChannelFactory.OpenCount, Is.Zero);
        }

        [Test]
        public async Task OrdinaryPropertyFormReadsThroughItsLocalDeclaration()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            h.ScalarVar.Value = new Variant(7);
            string localId = NodeId.ToExpandedNodeId(h.ScalarVar.NodeId, h.Builder.Context.NamespaceUris).ToString();
            string ownerId = NodeId.ToExpandedNodeId(h.Root.NodeId, h.Builder.Context.NamespaceUris).ToString();
            string document = $$"""
                {
                  "@type": "uav:object",
                  "uav:id": "{{ownerId}}",
                  "properties": {
                    "value": {
                      "uav:id": "{{localId}}",
                      "type": "integer",
                      "forms": [{ "href": "test://source/value", "op": "readproperty" }]
                    }
                  }
                }
                """;
            WotBindingPlanRequest request = WotBindingPlanRequest.FromDocument(
                "res", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(document));
            WotCompiledForm form = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty, WotTargetMappingDescriptor.Empty);
            var channel = new FakeWotBindingChannel(form)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.Good, new DataValue(new Variant(42))))
            };
            h.ChannelFactory.SetChannel(form, channel);
            WotBindingPlan plan = WotProjectionBindingRuntimeTestHarness.Plan(form)
                .WithProjectedAffordances(request.ProjectedAffordances);
            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(h.Builder, [plan]).ConfigureAwait(false);

            (ServiceResult result, DataValue value) = await h.ScalarVar.ReadAttributeAsync(
                h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue()).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out int actual), Is.True);
            Assert.That(actual, Is.EqualTo(42));
            Assert.That(channel.ReadCount, Is.EqualTo(1));
            Assert.That(form.TargetMapping.IsEmpty, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PropertyReadAlternativesSelectOneExecutableSource(bool structured)
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotTargetMappingDescriptor mapping = structured
                ? new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "A")
                : new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText);
            WotCompiledForm unavailable = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty, mapping, executable: false);
            WotCompiledForm first = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty, mapping, formIndex: 1);
            WotCompiledForm second = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty, mapping, formIndex: 2);
            var selected = new FakeWotBindingChannel(first)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.Good, new DataValue(new Variant(42))))
            };
            var alternative = new FakeWotBindingChannel(second)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.Good, new DataValue(new Variant(99))))
            };
            h.ChannelFactory.SetChannel(first, selected);
            h.ChannelFactory.SetChannel(second, alternative);
            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(unavailable, first, second)])
                .ConfigureAwait(false);
            BaseDataVariableState variable = structured ? h.StructVar : h.ScalarVar;

            (ServiceResult result, DataValue value) = await variable.ReadAttributeAsync(
                h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue()).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            if (structured)
            {
                Assert.That(value.WrappedValue.TryGetValue(out ExtensionObject extension), Is.True);
                Assert.That(extension.TryGetValue(out IEncodeable? encodeable), Is.True);
                Assert.That(encodeable, Is.TypeOf<TestRootStructure>());
                Assert.That(((TestRootStructure)encodeable!).A, Is.EqualTo(42));
            }
            else
            {
                Assert.That(value.WrappedValue.TryGetValue(out int scalar), Is.True);
                Assert.That(scalar, Is.EqualTo(42));
            }
            Assert.That(selected.ReadCount, Is.EqualTo(1));
            Assert.That(alternative.ReadCount, Is.Zero);
            Assert.That(h.ChannelFactory.OpenCount, Is.EqualTo(1));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task PropertyWriteAlternativesNeverFanOutOrRetryAnotherSource(bool structured, bool failed)
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotTargetMappingDescriptor mapping = structured
                ? new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "A")
                : new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText);
            WotCompiledForm first = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.WriteProperty, mapping);
            WotCompiledForm second = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.WriteProperty, mapping, formIndex: 1);
            Variant received = Variant.Null;
            StatusCode expected = failed ? StatusCodes.BadNotWritable : StatusCodes.GoodClamped;
            var selected = new FakeWotBindingChannel(first)
            {
                OnWrite = (value, _) =>
                {
                    received = value.WrappedValue;
                    return new ValueTask<WotWriteResult>(new WotWriteResult(expected));
                }
            };
            var alternative = new FakeWotBindingChannel(second);
            h.ChannelFactory.SetChannel(first, selected);
            h.ChannelFactory.SetChannel(second, alternative);
            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(first, second)]).ConfigureAwait(false);
            BaseDataVariableState variable = structured ? h.StructVar : h.ScalarVar;
            Variant written = structured
                ? new Variant(new ExtensionObject(new TestRootStructure { A = 17 }))
                : new Variant(17);

            ServiceResult result = await variable.WriteAttributeAsync(
                h.Builder.Context, Attributes.Value, default, new DataValue(written)).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(expected));
            Assert.That(received.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(17));
            Assert.That(selected.WriteCount, Is.EqualTo(1));
            Assert.That(alternative.WriteCount, Is.Zero);
            Assert.That(h.ChannelFactory.OpenCount, Is.EqualTo(1));
        }

        [Test]
        public async Task NonExecutableFormsAreIgnored()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm form = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText),
                executable: false);

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(form)]).ConfigureAwait(false);

            Assert.That(h.ScalarVar.OnReadValueAsync, Is.Null);
            Assert.That(h.ChannelFactory.OpenCount, Is.Zero);
        }

        [Test]
        public async Task ObserveOnlyDoesNotWireASeparateBridge()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm form = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ObserveProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText));

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(form)]).ConfigureAwait(false);

            Assert.That(h.ScalarVar.OnReadValueAsync, Is.Null);
            Assert.That(h.ScalarVar.OnWriteValueAsync, Is.Null);
            Assert.That(h.ChannelFactory.OpenCount, Is.Zero);
        }

        [Test]
        public void ConflictingDirectAndFieldMappingThrowsDuringWire()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm direct = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText),
                affordanceName: "direct");
            WotCompiledForm field = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.WriteProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText, fieldPath: "X"),
                affordanceName: "field");

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);

            ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await factory.CreateAsync(
                    h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(direct, field)]).ConfigureAwait(false));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public void DuplicateReadMappingThrowsDuringWire()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm read1 = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText),
                affordanceName: "read1");
            WotCompiledForm read2 = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText),
                affordanceName: "read2");

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);

            ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await factory.CreateAsync(
                    h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(read1, read2)]).ConfigureAwait(false));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public void DuplicateWriteMappingThrowsDuringWire()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm write1 = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.WriteProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText),
                affordanceName: "write1");
            WotCompiledForm write2 = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.WriteProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText),
                affordanceName: "write2");

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);

            ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await factory.CreateAsync(
                    h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(write1, write2)]).ConfigureAwait(false));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public void UnsupportedOperationThrowsDuringWire()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm invoke = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.InvokeAction,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText));

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);

            ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await factory.CreateAsync(
                    h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(invoke)]).ConfigureAwait(false));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public async Task LazyOpenConcurrentFirstUseOpensChannelOnce()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm readForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText));
            var channel = new FakeWotBindingChannel(readForm);
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            h.ChannelFactory.SetOpener(readForm, async () =>
            {
                await gate.Task.ConfigureAwait(false);
                return channel;
            });

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readForm)]).ConfigureAwait(false);

            Task<(ServiceResult, DataValue)> read1 = h.ScalarVar.ReadAttributeAsync(
                h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue()).AsTask();
            Task<(ServiceResult, DataValue)> read2 = h.ScalarVar.ReadAttributeAsync(
                h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue()).AsTask();

            gate.SetResult(true);
            await Task.WhenAll(read1, read2).ConfigureAwait(false);

            Assert.That(h.ChannelFactory.OpenCount, Is.EqualTo(1),
                "Concurrent first use must open the shared channel exactly once.");
        }

        [Test]
        public async Task FailedOpenIsEvictedRetrySucceeds()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm readForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText));
            var channel = new FakeWotBindingChannel(readForm)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.Good, new DataValue(new Variant(1))))
            };
            int attempt = 0;
            h.ChannelFactory.SetOpener(readForm, () =>
            {
                attempt++;
                if (attempt == 1)
                {
                    return new ValueTask<IWotBindingChannel>(
                        Task.FromException<IWotBindingChannel>(new InvalidOperationException("open failed")));
                }
                return new ValueTask<IWotBindingChannel>(channel);
            });

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readForm)]).ConfigureAwait(false);

            (ServiceResult firstResult, _) = await h.ScalarVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsBad(firstResult), Is.True, "The first (faulted) open must fail the read.");

            (ServiceResult secondResult, DataValue secondValue) = await h.ScalarVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(secondResult), Is.True, "A retry after a faulted open must succeed.");
            Assert.That(secondValue.WrappedValue.TryGetValue(out int v) && v == 1, Is.True);
            Assert.That(attempt, Is.EqualTo(2), "The faulted open must be evicted so the retry opens again.");
        }

        [Test]
        public async Task DisposeDisposesOpenedChannelsAggregatesFailures()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm readForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText),
                affordanceName: "read");
            WotCompiledForm writeForm = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.WriteProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText),
                affordanceName: "write");
            var readChannel = new FakeWotBindingChannel(readForm);
            var writeChannel = new FakeWotBindingChannel(writeForm)
            {
                OnDispose = () => throw new InvalidOperationException("dispose failed")
            };
            h.ChannelFactory.SetChannel(readForm, readChannel);
            h.ChannelFactory.SetChannel(writeForm, writeChannel);

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readForm, writeForm)]).ConfigureAwait(false);

            // Open both channels.
            await h.ScalarVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);
            await h.ScalarVar.WriteAttributeAsync(
                h.Builder.Context, Attributes.Value, default, new DataValue(new Variant(1))).ConfigureAwait(false);

            AggregateException? ex = Assert.ThrowsAsync<AggregateException>(
                async () => await runtime!.DisposeAsync().ConfigureAwait(false));
            Assert.That(ex!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(readChannel.DisposeCount, Is.EqualTo(1),
                "A sibling channel's dispose failure must not prevent other channels from being disposed.");
            Assert.That(writeChannel.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task GenerationIsolationTwoRuntimesHaveIndependentChannelsAndDisposal()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm form1 = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetNodeId: h.ScalarNodeIdText));
            var channel1 = new FakeWotBindingChannel(form1);
            h.ChannelFactory.SetChannel(form1, channel1);

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            IAsyncDisposable? runtime1 = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(form1)]).ConfigureAwait(false);
            await h.ScalarVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);

            // A second, independent generation with its own compiled form (and
            // hence its own channel) targeting the same variable.
            var h2 = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm form2 = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetNodeId: h2.ScalarNodeIdText));
            var channel2 = new FakeWotBindingChannel(form2);
            h2.ChannelFactory.SetChannel(form2, channel2);
            var factory2 = new WotProjectionBindingRuntimeFactory(h2.ChannelFactory);
            IAsyncDisposable? runtime2 = await factory2.CreateAsync(
                h2.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(form2)]).ConfigureAwait(false);
            await h2.ScalarVar.ReadAttributeAsync(
                    h2.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);

            await runtime1!.DisposeAsync().ConfigureAwait(false);

            Assert.That(channel1.DisposeCount, Is.EqualTo(1));
            Assert.That(channel2.DisposeCount, Is.Zero,
                "Disposing one generation's runtime must not affect a different generation's channels.");

            await runtime2!.DisposeAsync().ConfigureAwait(false);
            Assert.That(channel2.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task StructuredOneLevelReadComposesFields()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm readA = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "A"),
                affordanceName: "readA");
            var channelA = new FakeWotBindingChannel(readA)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.Good, new DataValue(new Variant(11))))
            };
            h.ChannelFactory.SetChannel(readA, channelA);

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readA)]).ConfigureAwait(false);

            (ServiceResult result, DataValue value) = await h.StructVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(value.WrappedValue.TryGetValue(out ExtensionObject ext), Is.True);
            Assert.That(ext.TryGetValue(out IEncodeable? encodeable), Is.True);
            var root = (TestRootStructure)encodeable!;
            Assert.That(root.A, Is.EqualTo(11));
        }

        [Test]
        public async Task StructuredOneLevelWriteExtractsFields()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm writeA = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.WriteProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "A"),
                affordanceName: "writeA");
            var channelA = new FakeWotBindingChannel(writeA);
            Variant written = default;
            channelA.OnWrite = (value, _) =>
            {
                written = value.WrappedValue;
                return new ValueTask<WotWriteResult>(new WotWriteResult(StatusCodes.Good));
            };
            h.ChannelFactory.SetChannel(writeA, channelA);

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(writeA)]).ConfigureAwait(false);

            var incoming = new TestRootStructure { A = 55 };
            ServiceResult result = await h.StructVar.WriteAttributeAsync(
                h.Builder.Context,
                Attributes.Value,
                default,
                new DataValue(new Variant(new ExtensionObject(incoming)))).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(written.IsNull, Is.False);
            Assert.That(written.TryGetValue(out int a) && a == 55, Is.True);
        }

        [Test]
        public async Task StructuredNestedReadComposesNestedStructure()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm readChildX = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "Child/X"),
                affordanceName: "readChildX");
            var channel = new FakeWotBindingChannel(readChildX)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.Good, new DataValue(new Variant(99))))
            };
            h.ChannelFactory.SetChannel(readChildX, channel);

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readChildX)]).ConfigureAwait(false);

            (ServiceResult result, DataValue value) = await h.StructVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(value.WrappedValue.TryGetValue(out ExtensionObject ext), Is.True);
            Assert.That(ext.TryGetValue(out IEncodeable? rootEncodeable), Is.True);
            var root = (TestRootStructure)rootEncodeable!;
            Assert.That(root.ChildValue.TryGetValue(out ExtensionObject childExt), Is.True);
            Assert.That(childExt.TryGetValue(out IEncodeable? childEncodeable), Is.True);
            var child = (TestChildStructure)childEncodeable!;
            Assert.That(child.X, Is.EqualTo(99));
        }

        [Test]
        public async Task StructuredNestedWriteExtractsNestedField()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm writeChildX = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.WriteProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "Child/X"),
                affordanceName: "writeChildX");
            var channel = new FakeWotBindingChannel(writeChildX);
            Variant written = default;
            channel.OnWrite = (value, _) =>
            {
                written = value.WrappedValue;
                return new ValueTask<WotWriteResult>(new WotWriteResult(StatusCodes.Good));
            };
            h.ChannelFactory.SetChannel(writeChildX, channel);

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(writeChildX)]).ConfigureAwait(false);

            var incomingChild = new TestChildStructure { X = 77 };
            var incoming = new TestRootStructure { ChildValue = new Variant(new ExtensionObject(incomingChild)) };
            ServiceResult result = await h.StructVar.WriteAttributeAsync(
                h.Builder.Context,
                Attributes.Value,
                default,
                new DataValue(new Variant(new ExtensionObject(incoming)))).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(written.IsNull, Is.False);
            Assert.That(written.TryGetValue(out int x) && x == 77, Is.True);
        }

        [Test]
        public async Task StructuredUnknownFieldFirstReadFailsDeterministically()
        {
            // BuildPlan validation is deferred to first use (see class remarks
            // on WotStructuredGroupState): CreateAsync itself must not throw.
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm form = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "NoSuchField"));

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(form)]).ConfigureAwait(false);
            Assert.That(runtime, Is.Not.Null, "Wiring must succeed; field-path validation is deferred.");

            (ServiceResult result, _) = await h.StructVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public async Task StructuredArrayValuedIntermediateFieldFirstReadFailsDeterministically()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm form = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "ArrayField/X"));

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(form)]).ConfigureAwait(false);
            Assert.That(runtime, Is.Not.Null, "Wiring must succeed; field-path validation is deferred.");

            (ServiceResult result, _) = await h.StructVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public async Task StructuredEmptyPathSegmentFirstReadFailsDeterministically()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm form = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "Child//X"));

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(form)]).ConfigureAwait(false);
            Assert.That(runtime, Is.Not.Null, "Wiring must succeed; field-path validation is deferred.");

            (ServiceResult result, _) = await h.StructVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public async Task StructuredUnregisteredTypeFirstReadFailsDeterministicallyThenRetrySucceedsAfterRegistration()
        {
            // The structure type is not yet registered when the runtime is
            // wired (mirrors ConfigureAsync running before
            // NodeManagerLifecycle.RefreshComplexTypesAsync). Wiring, and a
            // first read attempted before registration, must both fail
            // deterministically without throwing out of the request pipeline;
            // once the type is registered into the same factory instance
            // (simulating RefreshComplexTypesAsync) a later read must succeed.
            var h = new WotProjectionBindingRuntimeTestHarness(registerStructureTypes: false);
            WotCompiledForm readA = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "A"),
                affordanceName: "readA");
            var channelA = new FakeWotBindingChannel(readA)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.Good, new DataValue(new Variant(11))))
            };
            h.ChannelFactory.SetChannel(readA, channelA);

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readA)]).ConfigureAwait(false);
            Assert.That(runtime, Is.Not.Null,
                "Wiring must succeed even though the structure type is not registered yet.");

            (ServiceResult beforeResult, _) = await h.StructVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsBad(beforeResult), Is.True,
                "A read before the type is registered must fail deterministically, not throw.");
            Assert.That(beforeResult.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));

            // Simulate NodeManagerLifecycle.RefreshComplexTypesAsync completing:
            // register the type into the very same factory instance the
            // runtime captured at wiring time.
            h.Builder.Context.EncodeableFactory.Builder
                .AddEncodeableType(TestRootType.EncodingId, new TestRootType())
                .Commit();

            (ServiceResult afterResult, DataValue afterValue) = await h.StructVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(afterResult), Is.True,
                "A read after the type is registered must retry resolution and succeed.");
            Assert.That(afterValue.WrappedValue.TryGetValue(out ExtensionObject ext), Is.True);
            Assert.That(ext.TryGetValue(out IEncodeable? encodeable), Is.True);
            Assert.That(((TestRootStructure)encodeable!).A, Is.EqualTo(11));
        }

        [Test]
        public async Task StructuredUnregisteredTypeFirstWriteFailsDeterministicallyThenRetrySucceedsAfterRegistration()
        {
            var h = new WotProjectionBindingRuntimeTestHarness(registerStructureTypes: false);
            WotCompiledForm writeA = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.WriteProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "A"),
                affordanceName: "writeA");
            var channelA = new FakeWotBindingChannel(writeA);
            Variant written = default;
            channelA.OnWrite = (value, _) =>
            {
                written = value.WrappedValue;
                return new ValueTask<WotWriteResult>(new WotWriteResult(StatusCodes.Good));
            };
            h.ChannelFactory.SetChannel(writeA, channelA);

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(writeA)]).ConfigureAwait(false);

            var incomingBefore = new TestRootStructure { A = 1 };
            ServiceResult beforeResult = await h.StructVar.WriteAttributeAsync(
                h.Builder.Context,
                Attributes.Value,
                default,
                new DataValue(new Variant(new ExtensionObject(incomingBefore)))).ConfigureAwait(false);
            Assert.That(ServiceResult.IsBad(beforeResult), Is.True,
                "A write before the type is registered must fail deterministically, not throw.");
            Assert.That(written.IsNull, Is.True);

            h.Builder.Context.EncodeableFactory.Builder
                .AddEncodeableType(TestRootType.EncodingId, new TestRootType())
                .Commit();

            var incomingAfter = new TestRootStructure { A = 55 };
            ServiceResult afterResult = await h.StructVar.WriteAttributeAsync(
                h.Builder.Context,
                Attributes.Value,
                default,
                new DataValue(new Variant(new ExtensionObject(incomingAfter)))).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(afterResult), Is.True,
                "A write after the type is registered must retry resolution and succeed.");
            Assert.That(written.IsNull, Is.False);
            Assert.That(written.TryGetValue(out int a) && a == 55, Is.True);
        }

        [Test]
        public async Task StructuredPartialReadFailureReturnsBadStatus()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm readA = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "A"),
                affordanceName: "readA");
            WotCompiledForm readChildX = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "Child/X"),
                affordanceName: "readChildX");
            var channelA = new FakeWotBindingChannel(readA)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.Good, new DataValue(new Variant(1))))
            };
            var channelChildX = new FakeWotBindingChannel(readChildX)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.BadNotConnected, DataValue.Null, "not connected"))
            };
            h.ChannelFactory.SetChannel(readA, channelA);
            h.ChannelFactory.SetChannel(readChildX, channelChildX);

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readA, readChildX)]).ConfigureAwait(false);

            (ServiceResult result, _) = await h.StructVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsBad(result), Is.True,
                "A single failing field must fail the whole structured read.");
        }

        [Test]
        public async Task StructuredPartialReadFailureReturnsFailedFieldStatusAndTimestampWhenAvailable()
        {
            // A failed field's own status and timestamp — not a hardcoded
            // Bad/Now pair — must surface on the overall structured read.
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm readA = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "A"),
                affordanceName: "readA");
            WotCompiledForm readChildX = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "Child/X"),
                affordanceName: "readChildX");
            var channelA = new FakeWotBindingChannel(readA)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.Good, new DataValue(new Variant(1))))
            };
            var channelChildX = new FakeWotBindingChannel(readChildX);
            var staleTimestamp = new DateTimeUtc(2020, 1, 1, 0, 0, 0);
            channelChildX.OnRead = _ => new ValueTask<WotReadResult>(
                new WotReadResult(
                    StatusCodes.BadNotConnected,
                    new DataValue(Variant.Null, StatusCodes.BadNotConnected, staleTimestamp),
                    "not connected"));
            h.ChannelFactory.SetChannel(readA, channelA);
            h.ChannelFactory.SetChannel(readChildX, channelChildX);

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readA, readChildX)]).ConfigureAwait(false);

            (ServiceResult result, DataValue value) = await h.StructVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
            Assert.That(value.SourceTimestamp, Is.EqualTo(staleTimestamp));
        }

        [Test]
        public async Task StructuredReadPreservesNonDefaultGoodStatusAndUsesOldestSourceTimestamp()
        {
            // The composed value must not collapse every field's metadata to
            // a hardcoded Good/Now: a non-default Good status among the
            // fields must survive, and the oldest field timestamp must win.
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm readA = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "A"),
                affordanceName: "readA");
            WotCompiledForm readChildX = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "Child/X"),
                affordanceName: "readChildX");
            var channelA = new FakeWotBindingChannel(readA);
            var newerTimestamp = new DateTimeUtc(2026, 1, 2, 0, 0, 0);
            channelA.OnRead = _ => new ValueTask<WotReadResult>(
                new WotReadResult(StatusCodes.Good, new DataValue(new Variant(1), StatusCodes.Good, newerTimestamp)));
            var channelChildX = new FakeWotBindingChannel(readChildX);
            var olderTimestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 0);
            channelChildX.OnRead = _ => new ValueTask<WotReadResult>(
                new WotReadResult(
                    StatusCodes.GoodClamped,
                    new DataValue(new Variant(2), StatusCodes.GoodClamped, olderTimestamp)));
            h.ChannelFactory.SetChannel(readA, channelA);
            h.ChannelFactory.SetChannel(readChildX, channelChildX);

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readA, readChildX)]).ConfigureAwait(false);

            (ServiceResult result, DataValue value) = await h.StructVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.GoodClamped));
            Assert.That(value.SourceTimestamp, Is.EqualTo(olderTimestamp),
                "The oldest non-MinValue field timestamp must be used, not the current time.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task StructuredReadKeepsUncertainFieldValuesAndSeverity(bool uncertainFirstField)
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm readA = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "A"),
                affordanceName: "readA");
            WotCompiledForm readChildX = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "Child/X"),
                affordanceName: "readChildX");
            StatusCode statusA = uncertainFirstField ? StatusCodes.UncertainInitialValue : StatusCodes.GoodClamped;
            StatusCode statusX = uncertainFirstField ? StatusCodes.GoodClamped : StatusCodes.UncertainInitialValue;
            var newerTimestamp = new DateTimeUtc(2026, 1, 2, 0, 0, 0);
            var olderTimestamp = new DateTimeUtc(2026, 1, 1, 0, 0, 0);
            h.ChannelFactory.SetChannel(readA, new FakeWotBindingChannel(readA)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(statusA, new DataValue(new Variant(1), statusA, newerTimestamp)))
            });
            h.ChannelFactory.SetChannel(readChildX, new FakeWotBindingChannel(readChildX)
            {
                OnRead = _ => new ValueTask<WotReadResult>(
                    new WotReadResult(statusX, new DataValue(new Variant(2), statusX, olderTimestamp)))
            });

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readA, readChildX)]).ConfigureAwait(false);

            (ServiceResult result, DataValue value) = await h.StructVar.ReadAttributeAsync(
                    h.Builder.Context, Attributes.Value, default, QualifiedName.Null, new DataValue())
                .ConfigureAwait(false);

            Assert.That(value.WrappedValue.TryGetValue(out ExtensionObject rootValue), Is.True);
            Assert.That(rootValue.TryGetValue(out IEncodeable? rootEncodeable), Is.True);
            Assert.That(rootEncodeable, Is.TypeOf<TestRootStructure>());
            var root = (TestRootStructure)rootEncodeable!;
            Assert.That(root.A, Is.EqualTo(1));
            Assert.That(root.ChildValue.TryGetValue(out ExtensionObject childValue), Is.True);
            Assert.That(childValue.TryGetValue(out IEncodeable? childEncodeable), Is.True);
            Assert.That(childEncodeable, Is.TypeOf<TestChildStructure>());
            Assert.That(((TestChildStructure)childEncodeable!).X, Is.EqualTo(2));
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.UncertainInitialValue));
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.UncertainInitialValue));
            Assert.That(value.SourceTimestamp, Is.EqualTo(olderTimestamp));
        }

        [TestCaseSource(nameof(s_structuredWriteStatusCases))]
        public async Task StructuredWriteReturnsMostSevereFieldStatus(
            StatusCode statusA,
            StatusCode statusX,
            StatusCode expectedStatus)
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm writeA = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.WriteProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "A"),
                affordanceName: "writeA");
            WotCompiledForm writeChildX = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.WriteProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "Child/X"),
                affordanceName: "writeChildX");
            var channelA = new FakeWotBindingChannel(writeA)
            {
                OnWrite = (_, _) => new ValueTask<WotWriteResult>(new WotWriteResult(statusA))
            };
            var channelChildX = new FakeWotBindingChannel(writeChildX)
            {
                OnWrite = (_, _) => new ValueTask<WotWriteResult>(new WotWriteResult(statusX))
            };
            h.ChannelFactory.SetChannel(writeA, channelA);
            h.ChannelFactory.SetChannel(writeChildX, channelChildX);

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);
            await using IAsyncDisposable? runtime = await factory.CreateAsync(
                h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(writeA, writeChildX)]).ConfigureAwait(false);

            var incomingChild = new TestChildStructure { X = 1 };
            var incoming = new TestRootStructure
            {
                A = 2,
                ChildValue = new Variant(new ExtensionObject(incomingChild))
            };
            ServiceResult result = await h.StructVar.WriteAttributeAsync(
                h.Builder.Context,
                Attributes.Value,
                default,
                new DataValue(new Variant(new ExtensionObject(incoming)))).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(expectedStatus));
            Assert.That(channelA.WriteCount, Is.EqualTo(1));
            Assert.That(channelChildX.WriteCount, Is.EqualTo(1));
        }

        [Test]
        public void StructuredDuplicateFieldMappingThrowsDuringWire()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            WotCompiledForm readA1 = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "A"),
                affordanceName: "readA1");
            WotCompiledForm readA2 = WotProjectionBindingRuntimeTestHarness.Form(
                WoTBindingCapabilityEnum.ReadProperty,
                new WotTargetMappingDescriptor(targetTypeNodeId: h.StructTypeNodeIdText, fieldPath: "A"),
                affordanceName: "readA2");

            var factory = new WotProjectionBindingRuntimeFactory(h.ChannelFactory);

            ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await factory.CreateAsync(
                    h.Builder, [WotProjectionBindingRuntimeTestHarness.Plan(readA1, readA2)]).ConfigureAwait(false));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        private static readonly TestCaseData[] s_readQualityCases =
        [
            new(false, StatusCodes.UncertainInitialValue, StatusCodes.Good, StatusCodes.UncertainInitialValue),
            new(true, StatusCodes.UncertainInitialValue, StatusCodes.Good, StatusCodes.UncertainInitialValue),
            new(false, StatusCodes.GoodClamped, StatusCodes.Good, StatusCodes.GoodClamped),
            new(true, StatusCodes.GoodClamped, StatusCodes.Good, StatusCodes.GoodClamped),
            new(false, StatusCodes.GoodClamped, StatusCodes.UncertainInitialValue, StatusCodes.UncertainInitialValue),
            new(true, StatusCodes.GoodClamped, StatusCodes.UncertainInitialValue, StatusCodes.UncertainInitialValue),
            new(false, StatusCodes.UncertainInitialValue, StatusCodes.BadNoData, StatusCodes.BadNoData),
            new(true, StatusCodes.UncertainInitialValue, StatusCodes.BadNoData, StatusCodes.BadNoData),
            new(false, StatusCodes.BadNotConnected, StatusCodes.Good, StatusCodes.BadNotConnected),
            new(true, StatusCodes.BadNotConnected, StatusCodes.Good, StatusCodes.BadNotConnected)
        ];

        private static readonly TestCaseData[] s_structuredWriteStatusCases =
        [
            new(StatusCodes.Good, StatusCodes.Good, StatusCodes.Good),
            new(StatusCodes.GoodClamped, StatusCodes.Good, StatusCodes.GoodClamped),
            new(StatusCodes.Good, StatusCodes.GoodClamped, StatusCodes.GoodClamped),
            new(StatusCodes.GoodClamped, StatusCodes.UncertainInitialValue, StatusCodes.UncertainInitialValue),
            new(StatusCodes.UncertainInitialValue, StatusCodes.GoodClamped, StatusCodes.UncertainInitialValue),
            new(StatusCodes.UncertainInitialValue, StatusCodes.BadNotConnected, StatusCodes.BadNotConnected),
            new(StatusCodes.BadNotConnected, StatusCodes.UncertainInitialValue, StatusCodes.BadNotConnected),
            new(StatusCodes.Good, StatusCodes.BadNotConnected, StatusCodes.BadNotConnected)
        ];
    }
}
