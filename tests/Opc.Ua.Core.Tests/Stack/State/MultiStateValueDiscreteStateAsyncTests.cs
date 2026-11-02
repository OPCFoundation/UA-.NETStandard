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
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.State
{
    public partial class MultiStateValueDiscreteStateTests
    {
        /// <summary>
        /// Validates sparse enum values before calling the async provider and updates the accepted row.
        /// </summary>
        [TestCase(4u, true)]
        [TestCase(3u, false)]
        public async Task AsyncEnumWriteValidatesBeforeProviderAndUpdatesDerivedRow(
            uint value,
            bool called)
        {
            StatusCode expected = called ? StatusCodes.Good : StatusCodes.BadOutOfRange;
            MultiStateValueDiscreteState node = CreateNode(s_states);
            BaseDataVariableState entries = AddDictionary(node);
            bool providerCalled = false;
            node.OnSimpleWriteValueAsync = (_, _, _, _) =>
            {
                providerCalled = true;
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
            };
            ServiceResult result = await node.WriteAttributeAsync(
                m_context, Attributes.Value, NumericRange.Null, new DataValue(Variant.From(value)));
            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(expected));
                Assert.That(providerCalled, Is.EqualTo(called));
                Assert.That(node.ValueAsText!.Value.Text, Is.EqualTo(called ? "four" : "initial"));
                Assert.That(node.WrappedValue, Is.EqualTo(Variant.From(called ? 4u : 0u)));
                Assert.That(entries.WrappedValue, Is.EqualTo(Variant.From(
                    (ArrayOf<NodeId>)
                    [
                        new NodeId(called ? "four-a" : "initial", 0),
                        new NodeId(called ? "four-b" : "initial", 0)
                    ])));
            });
        }

        /// <summary>
        /// A provider refusal cannot update the enum's text or dictionary entries.
        /// </summary>
        [Test]
        public async Task AsyncEnumProviderRejectionLeavesAllValuesUnchanged()
        {
            MultiStateValueDiscreteState node = CreateNode(s_states);
            BaseDataVariableState entries = AddDictionary(node);
            node.OnSimpleWriteValueAsync = (_, _, _, _) =>
                new ValueTask<AttributeWriteResult>(new AttributeWriteResult(StatusCodes.BadUserAccessDenied));
            ServiceResult result = await node.WriteAttributeAsync(
                m_context, Attributes.Value, NumericRange.Null, new DataValue(Variant.From(4u)));
            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(node.ValueAsText!.Value.Text, Is.EqualTo("initial"));
                Assert.That(node.WrappedValue, Is.EqualTo(Variant.From(0u)));
                Assert.That(entries.WrappedValue, Is.EqualTo(
                    Variant.From((ArrayOf<NodeId>)[new NodeId("initial", 0), new NodeId("initial", 0)])));
            });
        }

        /// <summary>
        /// Suppressed writes derive metadata from the provider's committed cache rather than the request.
        /// </summary>
        [Test]
        public async Task AsyncSuppressedCacheUsesAuthoritativeAcquiredEnum()
        {
            MultiStateValueDiscreteState node = CreateNode(s_states);
            BaseDataVariableState entries = AddDictionary(node);
            node.OnSimpleWriteValueAsync = (_, _, _, _) =>
            {
                node.WrappedValue = Variant.From(8u);
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good)
                {
                    SuppressCacheUpdate = true
                });
            };
            ServiceResult result = await node.WriteAttributeAsync(
                m_context, Attributes.Value, NumericRange.Null, new DataValue(Variant.From(4u)));
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(node.WrappedValue, Is.EqualTo(Variant.From(8u)));
                Assert.That(node.ValueAsText!.Value.Text, Is.EqualTo("eight"));
                Assert.That(entries.WrappedValue, Is.EqualTo(
                    Variant.From((ArrayOf<NodeId>)[new NodeId("eight-a", 0), new NodeId("eight-b", 0)])));
            });
        }

        /// <summary>
        /// A provider cannot report a successful suppressed write with a good-quality null cache.
        /// </summary>
        [Test]
        public async Task AsyncSuppressedGoodNullCacheIsRejectedWithoutReplacingDerivedValues()
        {
            MultiStateValueDiscreteState node = CreateNode(s_states);
            BaseDataVariableState entries = AddDictionary(node);
            node.OnSimpleWriteValueAsync = (_, _, _, _) =>
            {
                node.WrappedValue = Variant.Null;
                node.StatusCode = StatusCodes.Good;
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good)
                {
                    SuppressCacheUpdate = true
                });
            };

            ServiceResult result = await node.WriteAttributeAsync(
                m_context, Attributes.Value, NumericRange.Null, new DataValue(Variant.From(4u)));

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
                Assert.That(node.ValueAsText!.Value.Text, Is.EqualTo("initial"));
                Assert.That(entries.WrappedValue, Is.EqualTo(
                    Variant.From((ArrayOf<NodeId>)[new NodeId("initial", 0), new NodeId("initial", 0)])));
            });
        }

        /// <summary>
        /// Provider acquisitions update the same derived fields and preserve bad-quality null samples.
        /// </summary>
        [Test]
        public void AcquiredEnumUpdatesTextDictionaryQualityAndTimestamp()
        {
            MultiStateValueDiscreteState node = CreateNode(s_states);
            BaseDataVariableState entries = AddDictionary(node);
            DateTimeUtc timestamp = new(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
            ServiceResult first = node.UpdateEnumValue(m_context,
                new DataValue(Variant.From(2u), StatusCodes.UncertainSensorNotAccurate, timestamp));
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(first), Is.True);
                Assert.That(node.WrappedValue, Is.EqualTo(Variant.From(2u)));
                Assert.That(node.ValueAsText!.Value.Text, Is.EqualTo("two"));
                Assert.That(entries.WrappedValue, Is.EqualTo(
                    Variant.From((ArrayOf<NodeId>)[new NodeId("two-a", 0), new NodeId("two-b", 0)])));
                Assert.That(node.ValueAsText.Timestamp, Is.EqualTo(timestamp));
                Assert.That(entries.StatusCode, Is.EqualTo(StatusCodes.UncertainSensorNotAccurate));
            });
            ServiceResult bad = node.UpdateEnumValue(m_context,
                new DataValue(Variant.Null, StatusCodes.BadNoCommunication, timestamp));
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(bad), Is.True);
                Assert.That(node.WrappedValue.IsNull, Is.True);
                Assert.That(node.ValueAsText!.Value.Text, Is.EqualTo("two"));
                Assert.That(entries.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            });
        }

        /// <summary>
        /// Invalid acquired enum values and malformed dictionary rows cannot partially mutate the sample.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void InvalidAcquiredEnumLeavesPriorValueAndMetadataUnchanged(bool malformedDictionary)
        {
            MultiStateValueDiscreteState node = CreateNode(s_states);
            BaseDataVariableState entries = AddDictionary(node);
            if (malformedDictionary)
            {
                BaseVariableState matrix = (BaseVariableState)node.FindChild(
                    m_context, new QualifiedName(BrowseNames.EnumDictionaryEntries))!;
                matrix.WrappedValue = Variant.From((MatrixOf<NodeId>)new NodeId[,] { { new("wrong-row-count", 0) } });
            }
            ServiceResult result = node.UpdateEnumValue(
                m_context, new DataValue(Variant.From(malformedDictionary ? 4u : 3u)));
            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode,
                    Is.EqualTo(malformedDictionary ? StatusCodes.BadConfigurationError : StatusCodes.BadOutOfRange));
                Assert.That(node.WrappedValue, Is.EqualTo(Variant.From(0u)));
                Assert.That(node.ValueAsText!.Value.Text, Is.EqualTo("initial"));
                Assert.That(entries.WrappedValue, Is.EqualTo(
                    Variant.From((ArrayOf<NodeId>)[new NodeId("initial", 0), new NodeId("initial", 0)])));
            });
        }

        /// <summary>
        /// Rejects wrong types, wrong ranks and unsigned values that cannot match a signed EnumValue.
        /// </summary>
        [TestCase("text")]
        [TestCase("array")]
        [TestCase("overflow")]
        public async Task AsyncEnumRejectsInvalidPartitionsBeforeProvider(string partition)
        {
            StatusCode expected = partition == "overflow" ? StatusCodes.BadOutOfRange : StatusCodes.BadTypeMismatch;
            MultiStateValueDiscreteState node = CreateNode(s_states);
            Variant input = partition switch
            {
                "array" => Variant.From((ArrayOf<uint>)[1u, 2u]),
                "overflow" => Variant.From(ulong.MaxValue),
                _ => Variant.From("four")
            };
            if (partition == "overflow")
            {
                node.DataType = DataTypeIds.UInt64;
            }
            bool providerCalled = false;
            node.OnSimpleWriteValueAsync = (_, _, _, _) =>
            {
                providerCalled = true;
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
            };

            ServiceResult result = await node.WriteAttributeAsync(
                m_context, Attributes.Value, NumericRange.Null, new DataValue(input));

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(expected));
                Assert.That(providerCalled, Is.False);
                Assert.That(node.WrappedValue, Is.EqualTo(Variant.From(0u)));
                Assert.That(node.ValueAsText!.Value.Text, Is.EqualTo("initial"));
            });
        }

        /// <summary>
        /// A present empty enumeration rejects values while an absent property preserves the existing fallback.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task AsyncEnumPreservesEmptyAndMissingDeclarationBehavior(bool missing)
        {
            StatusCode expected = missing ? StatusCodes.Good : StatusCodes.BadOutOfRange;
            MultiStateValueDiscreteState node = CreateNode(missing ? null! : []);
            bool providerCalled = false;
            node.OnSimpleWriteValueAsync = (_, _, _, _) =>
            {
                providerCalled = true;
                return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
            };

            ServiceResult result = await node.WriteAttributeAsync(
                m_context, Attributes.Value, NumericRange.Null, new DataValue(Variant.From(4u)));

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(expected));
                Assert.That(providerCalled, Is.EqualTo(missing));
                Assert.That(node.WrappedValue, Is.EqualTo(Variant.From(missing ? 4u : 0u)));
                Assert.That(node.ValueAsText!.Value.Text, Is.EqualTo("initial"));
            });
        }

        /// <summary>
        /// An absent or good-quality null sample cannot replace a valid acquisition.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void NullAcquisitionCannotMasqueradeAsGoodEnumData(bool absent)
        {
            MultiStateValueDiscreteState node = CreateNode(s_states);
            DataValue sample = absent ? DataValue.Null : new DataValue(Variant.Null, StatusCodes.Good);

            ServiceResult result = node.UpdateEnumValue(m_context, sample);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
                Assert.That(node.WrappedValue, Is.EqualTo(Variant.From(0u)));
                Assert.That(node.ValueAsText!.Value.Text, Is.EqualTo("initial"));
            });
        }

        /// <summary>
        /// The existing synchronous write path also maintains the dictionary-derived row.
        /// </summary>
        [Test]
        public void SynchronousEnumWriteUpdatesDictionaryEntries()
        {
            MultiStateValueDiscreteState node = CreateNode(s_states);
            BaseDataVariableState entries = AddDictionary(node);
            ServiceResult result = WriteValue(node, Variant.From(4u));
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(entries.WrappedValue, Is.EqualTo(
                Variant.From((ArrayOf<NodeId>)[new NodeId("four-a", 0), new NodeId("four-b", 0)])));
        }

        /// <summary>
        /// A matched row with no display name clears previous text rather than being mistaken for an absent enum.
        /// </summary>
        [TestCase("synchronous")]
        [TestCase("asynchronous")]
        [TestCase("acquisition")]
        public async Task ResolvedEnumWithNullDisplayNameClearsThePreviousText(string path)
        {
            MultiStateValueDiscreteState node = CreateNode([(4, "four")]);
            node.EnumValues!.Value = [new EnumValueType { Value = 4, DisplayName = LocalizedText.Null }];
            var dictionary = new BaseDataVariableState(node)
            {
                BrowseName = new QualifiedName(BrowseNames.EnumDictionaryEntries),
                DataType = DataTypeIds.NodeId,
                ValueRank = ValueRanks.TwoDimensions,
                WrappedValue = Variant.From((MatrixOf<NodeId>)new NodeId[,] { { new("four", 0) } })
            };
            node.AddChild(dictionary);
            var entries = new BaseDataVariableState(node)
            {
                BrowseName = new QualifiedName(BrowseNames.ValueAsDictionaryEntries),
                DataType = DataTypeIds.NodeId,
                ValueRank = ValueRanks.OneDimension,
                WrappedValue = Variant.From((ArrayOf<NodeId>)[new NodeId("initial", 0)])
            };
            node.AddChild(entries);
            ServiceResult result;
            if (path == "synchronous")
            {
                result = WriteValue(node, Variant.From(4u));
            }
            else if (path == "asynchronous")
            {
                node.OnSimpleWriteValueAsync = (_, _, _, _) =>
                    new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
                result = await node.WriteAttributeAsync(
                    m_context, Attributes.Value, NumericRange.Null, new DataValue(Variant.From(4u)));
            }
            else
            {
                result = node.UpdateEnumValue(m_context, new DataValue(Variant.From(4u)));
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(node.WrappedValue, Is.EqualTo(Variant.From(4u)));
                Assert.That(node.ValueAsText!.Value.IsNull, Is.True);
                Assert.That(entries.WrappedValue,
                    Is.EqualTo(Variant.From((ArrayOf<NodeId>)[new NodeId("four", 0)])));
            });
        }

        /// <summary>
        /// The synchronous callback retains the row resolved before it changed the enum declaration.
        /// </summary>
        [Test]
        public void SynchronousHandlerChangesDoNotRevalidateAcceptedRequest()
        {
            MultiStateValueDiscreteState node = CreateNode(s_states);
            BaseDataVariableState entries = AddDictionary(node);
            node.OnSimpleWriteValue = (ISystemContext _, NodeState _, ref Variant _) =>
            {
                node.EnumValues!.Value = [];
                return ServiceResult.Good;
            };

            ServiceResult result = WriteValue(node, Variant.From(4u));

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(node.WrappedValue, Is.EqualTo(Variant.From(4u)));
                Assert.That(node.ValueAsText!.Value.Text, Is.EqualTo("four"));
                Assert.That(entries.WrappedValue, Is.EqualTo(
                    Variant.From((ArrayOf<NodeId>)[new NodeId("four-a", 0), new NodeId("four-b", 0)])));
            });
        }

        /// <summary>
        /// Adds a two-column classification matrix so tests detect row and column indexing errors.
        /// </summary>
        private static BaseDataVariableState AddDictionary(MultiStateValueDiscreteState node)
        {
            var matrix = new BaseDataVariableState(node)
            {
                BrowseName = new QualifiedName(BrowseNames.EnumDictionaryEntries),
                DataType = DataTypeIds.NodeId,
                ValueRank = ValueRanks.TwoDimensions,
                WrappedValue = Variant.From((MatrixOf<NodeId>)new NodeId[,]
                {
                    { new("one-a", 0), new("one-b", 0) },
                    { new("two-a", 0), new("two-b", 0) },
                    { new("four-a", 0), new("four-b", 0) },
                    { new("eight-a", 0), new("eight-b", 0) }
                })
            };
            node.AddChild(matrix);
            var entries = new BaseDataVariableState(node)
            {
                BrowseName = new QualifiedName(BrowseNames.ValueAsDictionaryEntries),
                DataType = DataTypeIds.NodeId,
                ValueRank = ValueRanks.OneDimension,
                WrappedValue = Variant.From((ArrayOf<NodeId>)[new NodeId("initial", 0), new NodeId("initial", 0)])
            };
            node.AddChild(entries);
            return entries;
        }
    }
}
