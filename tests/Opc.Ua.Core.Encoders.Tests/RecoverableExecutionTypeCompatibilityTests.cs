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
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Encoders.Tests
{
    /// <summary>
    /// Verifies restart interpretation qualification independently of Client and Server persistence.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class RecoverableExecutionTypeCompatibilityTests
    {
        [Test]
        public async Task UnchangedCoreAndRseRegistrationsReopenWithoutReinterpretingPayloads()
        {
            ServiceMessageContext first = CreateContext();
            var compatibility = new RecoverableExecutionTypeCompatibility();
            RecoverableExecutionTypeContext saved = await compatibility.CaptureAsync(first).ConfigureAwait(false);
            var request = new SubmitRequestDataType
            {
                ClientOperationId = Uuid.NewUuid(),
                RecoveryMode = ServiceOperationRecoveryMode.Durable,
                Request = new ExtensionObject(new ReadRequest
                {
                    RequestHeader = new RequestHeader { RequestHandle = 37 },
                    NodesToRead = [new ReadValueId { NodeId = new NodeId(100u), AttributeId = Attributes.Value }],
                    TimestampsToReturn = TimestampsToReturn.Neither
                }),
                ExecutionAllowance = 8000,
                ResultRetentionTime = 4000
            };
            ByteString payload = saved.Encode(request);
            RecoverableExecutionTypeContext reopened = await compatibility.RestoreAsync(CreateContext(), saved.Witness)
                .ConfigureAwait(false);
            SubmitRequestDataType restored = reopened.Decode<SubmitRequestDataType>(payload);
            Assert.That(CoreUtils.IsEqual(restored, request), Is.True);
            Assert.That(saved.Witness.Length, Is.EqualTo(37));
            Assert.That(reopened.Witness, Is.EqualTo(saved.Witness));
            Assert.That(restored.RecoveryMode, Is.EqualTo(ServiceOperationRecoveryMode.Durable));
            Assert.That(restored.Request.TryGetValue(out ReadRequest? original), Is.True);
            Assert.That(original!.RequestHeader.RequestHandle, Is.EqualTo(37u));
        }

        [TestCase("field-order")]
        [TestCase("field-type")]
        [TestCase("field-name")]
        [TestCase("optional")]
        [TestCase("rank")]
        [TestCase("dimensions")]
        [TestCase("description")]
        [TestCase("maximum-string")]
        [TestCase("structure-kind")]
        [TestCase("explicit-field-index")]
        [TestCase("encoding-alias")]
        [TestCase("codec-revision")]
        public async Task SameIdsAndMappingsCannotHideChangedInterpretation(string change)
        {
            ServiceMessageContext context = CreateContext();
            var codec = new CustomCodec();
            Register(context, codec);
            var qualifier = new CustomQualifier();
            var compatibility = new RecoverableExecutionTypeCompatibility(qualifier);
            RecoverableExecutionTypeContext first = await compatibility.CaptureAsync(context).ConfigureAwait(false);
            ByteString payload = first.Encode(new CustomValue { First = 12, Second = 34 });
            var changed = new CustomCodec { Change = change };
            if (change == "codec-revision")
            {
                qualifier.Revision = ByteString.From("v2"u8);
            }
            Register(context, changed);
            if (change == "encoding-alias")
            {
                context.Factory.Builder.AddEncodeableType(new ExpandedNodeId(7u, kNamespace), changed).Commit();
            }
            int decoded = changed.Decodes;
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await compatibility.RestoreAsync(context, first.Witness).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(changed.Decodes, Is.EqualTo(decoded),
                "Saved payloads must not be decoded to test compatibility.");
            CustomValue captured = first.Decode<CustomValue>(payload);
            Assert.That(captured.First, Is.EqualTo(12));
            Assert.That(captured.Second, Is.EqualTo(34));
        }

        [TestCase("namespace-order")]
        [TestCase("namespace-addition")]
        [TestCase("server-order")]
        [TestCase("server-addition")]
        public async Task ExactNamespaceAndServerMappingsArePartOfTheWitness(string change)
        {
            ServiceMessageContext context = CreateContext();
            var compatibility = new RecoverableExecutionTypeCompatibility();
            RecoverableExecutionTypeContext first = await compatibility.CaptureAsync(context).ConfigureAwait(false);
            switch (change)
            {
                case "namespace-order":
                    context.NamespaceUris.Update([Namespaces.OpcUa, RseNamespaces.Rse, kNamespace]);
                    break;
                case "namespace-addition":
                    context.NamespaceUris.Append("urn:changed:namespace");
                    break;
                case "server-order":
                    context.ServerUris.Update(["urn:server:two", "urn:server:one"]);
                    break;
                case "server-addition":
                    context.ServerUris.Append("urn:server:three");
                    break;
            }
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await compatibility.RestoreAsync(context, first.Witness).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CustomCodecRequiresBothMetadataAndExplicitRevision(bool metadata)
        {
            ServiceMessageContext context = CreateContext();
            IEncodeableType codec = metadata ? new CustomCodec() : new MissingMetadataCodec();
            context.Factory.Builder.AddEncodeableType(codec).Commit();
            var compatibility = new RecoverableExecutionTypeCompatibility(metadata ? null : new CustomQualifier());
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await compatibility.CaptureAsync(context).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task QualifiedCustomMetadataCanReopenWithAnUnchangedExplicitCodecRevision()
        {
            ServiceMessageContext first = CreateContext();
            Register(first, new CustomCodec());
            var compatibility = new RecoverableExecutionTypeCompatibility(new CustomQualifier());
            RecoverableExecutionTypeContext saved = await compatibility.CaptureAsync(first).ConfigureAwait(false);
            ByteString payload = saved.Encode(new CustomValue { First = 12, Second = 34 });
            ServiceMessageContext next = CreateContext();
            var codec = new CustomCodec();
            Register(next, codec);
            RecoverableExecutionTypeContext restored = await compatibility.RestoreAsync(next, saved.Witness)
                .ConfigureAwait(false);
            CustomValue decoded = restored.Decode<CustomValue>(payload);
            Assert.That(decoded.First, Is.EqualTo(12));
            Assert.That(decoded.Second, Is.EqualTo(34));
            Assert.That(codec.Decodes, Is.EqualTo(1));
        }

        [Test]
        public async Task UnregisteredNestedDefinitionDependencyIsRejected()
        {
            ServiceMessageContext context = CreateContext();
            Register(context, new CustomCodec { Change = "missing-dependency" });
            var compatibility = new RecoverableExecutionTypeCompatibility(new CustomQualifier());
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await compatibility.CaptureAsync(context).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OpaqueNestedUnknownOrMalformedTypesCannotBePersisted(bool known)
        {
            ServiceMessageContext context = CreateContext();
            var qualifier = new CustomQualifier();
            if (known)
            {
                Register(context, new CustomCodec());
            }
            RecoverableExecutionTypeContext captured = await new RecoverableExecutionTypeCompatibility(qualifier)
                .CaptureAsync(context).ConfigureAwait(false);
            var response = new ReadResponse
            {
                Results =
                [
                    new DataValue(Variant.From(new ExtensionObject(
                        new ExpandedNodeId(known ? 2u : 900u, kNamespace), ByteString.From(new byte[] { 1 }))))
                ]
            };
            ServiceResultException failure = Assert.Throws<ServiceResultException>(() => captured.Encode(response))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(captured.MessageContext.MaxDecoderRecoveries, Is.Zero);
        }

        [Test]
        public async Task CapturedFactoryAndTablesCannotBeRetargetedThroughThePublicContext()
        {
            ServiceMessageContext context = CreateContext();
            RecoverableExecutionTypeContext captured = await new RecoverableExecutionTypeCompatibility()
                .CaptureAsync(context).ConfigureAwait(false);
            IServiceMessageContext exposed = captured.MessageContext;
            exposed.NamespaceUris.Append("urn:must-not-change-capture");
            exposed.ServerUris.Append("urn:must-not-change-server");
            _ = Assert.Throws<ServiceResultException>(() => _ = exposed.Factory.Builder);
            Assert.That(captured.MessageContext.NamespaceUris.Count, Is.EqualTo(context.NamespaceUris.Count));
            Assert.That(captured.MessageContext.ServerUris.Count, Is.EqualTo(context.ServerUris.Count));
        }

        [Test]
        [NonParallelizable]
        public async Task MutableGlobalFactoryCannotBecomeBuiltinQualificationAuthority()
        {
            EncodeableFactory global = EncodeableFactory.Root.Value;
            global.TryGetEncodeableType(DataTypeIds.ReadResponse, out IEncodeableType? original);
            try
            {
                var impersonator = new ReadResponseImpersonator();
                global.Builder.AddEncodeableType(DataTypeIds.ReadResponse, impersonator).Commit();
                ServiceMessageContext context = CreateContext();
                context.Factory.Builder.AddEncodeableType(DataTypeIds.ReadResponse, impersonator).Commit();
                ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await new RecoverableExecutionTypeCompatibility().CaptureAsync(context).ConfigureAwait(false))
                    .ConfigureAwait(false))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            }
            finally
            {
                global.Builder.AddEncodeableType(
                    DataTypeIds.ReadResponse, original ?? ReadResponseActivator.Instance).Commit();
            }
        }

        [TestCase(0)]
        [TestCase(32)]
        [TestCase(38)]
        public async Task UnversionedOrMalformedWitnessIsRejectedBeforeQualification(int length)
        {
            var qualifier = new CustomQualifier();
            var compatibility = new RecoverableExecutionTypeCompatibility(qualifier);
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await compatibility.RestoreAsync(CreateContext(), ByteString.From(new byte[length]))
                    .ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(qualifier.Calls, Is.Zero);
        }

        [Test]
        public async Task CompatibilityAndCustomQualificationAreInjectable()
        {
            var services = new ServiceCollection();
            var qualifier = new CustomQualifier();
            services.AddSingleton<IRecoverableExecutionCodecQualifier>(qualifier);
            services.AddRecoverableExecutionTypeCompatibility();
            services.AddRecoverableExecutionTypeCompatibility();
            await using ServiceProvider provider = services.BuildServiceProvider();
            RecoverableExecutionTypeCompatibility compatibility =
                provider.GetRequiredService<RecoverableExecutionTypeCompatibility>();
            Assert.That(provider.GetRequiredService<RecoverableExecutionTypeCompatibility>(), Is.SameAs(compatibility));
            ServiceMessageContext context = CreateContext();
            Register(context, new CustomCodec());
            _ = await compatibility.CaptureAsync(context).ConfigureAwait(false);
            Assert.That(qualifier.Calls, Is.GreaterThan(0));
        }

        [Test]
        public async Task SmallerMessageLimitsDoNotChangeSchemaIdentityButStillBoundDecoding()
        {
            ServiceMessageContext context = CreateContext();
            var compatibility = new RecoverableExecutionTypeCompatibility();
            RecoverableExecutionTypeContext saved = await compatibility.CaptureAsync(context).ConfigureAwait(false);
            ByteString payload = saved.Encode(new ReadResponse
            {
                Results = [new DataValue(Variant.From(new string('x', 128)))]
            });
            var bounded = new ServiceMessageContext(context, context.Telemetry)
            {
                MaxMessageSize = 32,
                MaxByteStringLength = 32,
                MaxArrayLength = 1
            };
            RecoverableExecutionTypeContext restored = await compatibility.RestoreAsync(bounded, saved.Witness)
                .ConfigureAwait(false);
            Assert.That(restored.Witness, Is.EqualTo(saved.Witness));
            Assert.That(restored.MessageContext.MaxMessageSize, Is.EqualTo(32));
            ServiceResultException failure = Assert.Throws<ServiceResultException>(() =>
                restored.Decode<ReadResponse>(payload))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [TestCase("order")]
        [TestCase("value")]
        [TestCase("option-set")]
        [TestCase("display-name")]
        public async Task CompleteEnumerationDefinitionChangesInvalidateTheWitness(string change)
        {
            ServiceMessageContext context = CreateContext();
            var id = new ExpandedNodeId(20u, kNamespace);
            context.Factory.Builder.AddEnumeratedType(id, new CustomEnumCodec()).Commit();
            var compatibility = new RecoverableExecutionTypeCompatibility(new CustomQualifier());
            RecoverableExecutionTypeContext saved = await compatibility.CaptureAsync(context).ConfigureAwait(false);
            context.Factory.Builder.AddEnumeratedType(id, new CustomEnumCodec { Change = change }).Commit();
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await compatibility.RestoreAsync(context, saved.Witness).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public async Task AmbiguousXmlRegistrationCannotQualifyAFactory()
        {
            ServiceMessageContext context = CreateContext();
            Register(context, new CustomCodec());
            context.Factory.Builder.AddEncodeableType(new ExpandedNodeId(99u, kNamespace), new CustomCodec()).Commit();
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await new RecoverableExecutionTypeCompatibility(new CustomQualifier()).CaptureAsync(context)
                    .ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        private enum CustomEnum
        {
            First,
            Second
        }

        private sealed class CustomEnumCodec : EnumeratedType<CustomEnum>
        {
            internal string? Change { get; init; }
            public override XmlQualifiedName XmlName => new("CustomEnum", kNamespace);

            public override DataTypeDefinition GetDataTypeDefinition(NamespaceTable namespaceUris)
            {
                var first = new EnumField
                {
                    Name = "First",
                    Value = Change == "value" ? 2 : 0,
                    DisplayName = Change == "display-name" ? new LocalizedText("changed") : new LocalizedText("first")
                };
                var second = new EnumField { Name = "Second", Value = 1 };
                return new EnumDefinition
                {
                    Fields = Change == "order" ? [second, first] : [first, second],
                    IsOptionSet = Change == "option-set"
                };
            }
        }

        private static ServiceMessageContext CreateContext()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            context.Factory.Builder.AddOpcUa().AddOpcUaRse().Commit();
            context.NamespaceUris.Update([Namespaces.OpcUa, kNamespace, RseNamespaces.Rse]);
            context.ServerUris.Update(["urn:server:one", "urn:server:two"]);
            return context;
        }

        private static void Register(ServiceMessageContext context, CustomCodec codec)
        {
            context.Factory.Builder.AddEncodeableType(codec).Commit();
        }

        private sealed class CustomQualifier : IRecoverableExecutionCodecQualifier
        {
            internal ByteString Revision { get; set; } = ByteString.From("v1"u8);
            internal int Calls { get; private set; }

            public ValueTask<ByteString> GetRevisionAsync(
                ExpandedNodeId registrationId, IType codec, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                Calls++;
                return new ValueTask<ByteString>(Revision);
            }
        }

        private sealed class CustomCodec : IEncodeableType, IDataTypeDefinitionSource
        {
            public Type Type => typeof(CustomValue);
            public XmlQualifiedName XmlName => new("CustomValue", kNamespace);
            internal string? Change { get; init; }
            internal int Decodes { get; private set; }

            public IEncodeable CreateInstance()
            {
                return new CustomValue { Decoded = () => Decodes++ };
            }

            public DataTypeDefinition GetDataTypeDefinition(NamespaceTable namespaceUris)
            {
                var first = new StructureField
                {
                    Name = Change == "field-name" ? "Renamed" : "First",
                    DataType = Change == "field-type" ? DataTypeIds.Int32 : Change == "missing-dependency"
                        ? NodeId.Create(99u, kNamespace, namespaceUris) : DataTypeIds.UInt32,
                    ValueRank = Change == "rank" ? ValueRanks.OneDimension : ValueRanks.Scalar,
                    IsOptional = Change == "optional",
                    ArrayDimensions = Change == "dimensions" ? [2u] : default,
                    Description = Change == "description" ? new LocalizedText("changed") : default,
                    MaxStringLength = Change == "maximum-string" ? 10u : 0u
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
                    DefaultEncodingId = NodeId.Create(2u, kNamespace, namespaceUris),
                    StructureType = Change == "structure-kind" ? StructureType.Union : StructureType.Structure,
                    FirstExplicitFieldIndex = Change == "explicit-field-index" ? 1 : 0,
                    Fields = Change == "field-order" ? [second, first] : [first, second]
                };
            }
        }

        private sealed class MissingMetadataCodec : IEncodeableType
        {
            public Type Type => typeof(CustomValue);
            public XmlQualifiedName XmlName => new("CustomValue", kNamespace);

            public IEncodeable CreateInstance()
            {
                return new CustomValue();
            }
        }

        private sealed class ReadResponseImpersonator : IEncodeableType, IDataTypeDefinitionSource
        {
            public Type Type => typeof(ReadResponse);
            public XmlQualifiedName XmlName => ReadResponseActivator.Instance.XmlName;

            public IEncodeable CreateInstance()
            {
                return new ReadResponse();
            }

            public DataTypeDefinition GetDataTypeDefinition(NamespaceTable namespaceUris)
            {
                return ReadResponseActivator.Instance.GetDataTypeDefinition(namespaceUris);
            }
        }

        private sealed class CustomValue : IEncodeable
        {
            internal uint First { get; set; }
            internal uint Second { get; set; }
            internal Action? Decoded { get; init; }
            public ExpandedNodeId TypeId => new(1u, kNamespace);
            public ExpandedNodeId BinaryEncodingId => new(2u, kNamespace);
            public ExpandedNodeId XmlEncodingId => new(3u, kNamespace);

            public void Encode(IEncoder encoder)
            {
                encoder.WriteUInt32("First", First);
                encoder.WriteUInt32("Second", Second);
            }

            public void Decode(IDecoder decoder)
            {
                Decoded?.Invoke();
                First = decoder.ReadUInt32("First");
                Second = decoder.ReadUInt32("Second");
            }

            public bool IsEqual(IEncodeable? encodeable)
            {
                return encodeable is CustomValue value && value.First == First && value.Second == Second;
            }

            public object Clone()
            {
                return new CustomValue { First = First, Second = Second, Decoded = Decoded };
            }
        }

        private const string kNamespace = "urn:synthetic:rse-witness";
    }
}
