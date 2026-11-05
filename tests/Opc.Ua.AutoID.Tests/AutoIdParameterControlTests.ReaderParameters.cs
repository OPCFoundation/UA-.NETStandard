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
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.AutoID.Server;

namespace Opc.Ua.AutoID.Tests
{
    public sealed partial class AutoIdParameterControlTests
    {
        /// <summary>
        /// Publishes only explicitly declared optional parameters, with the exact standard metadata.
        /// </summary>
        [TestCase(AutoIdReaderKind.Rfid)]
        [TestCase(AutoIdReaderKind.Ocr)]
        [TestCase(AutoIdReaderKind.Optical)]
        [TestCase(AutoIdReaderKind.OpticalVerifier)]
        [TestCase(AutoIdReaderKind.Rtls)]
        public async Task ReaderParametersMatchKindCapabilitiesAndPublishedMetadataAsync(AutoIdReaderKind kind)
        {
            ParameterProvider provider = SpecificProvider(kind);
            await using AutoIdReader reader = await m_fixture.Runtime.Reader(Name(), provider)
                .WithRuntimeParameters().WithScanWritePermissions([new RolePermissionType
                {
                    RoleId = Opc.Ua.ObjectIds.WellKnownRole_Operator,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Read | PermissionType.Write)
                }]).BuildAsync().ConfigureAwait(false);
            var children = new List<BaseInstanceState>();
            reader.Device.RuntimeParameters!.GetChildren(m_fixture.Context, children);
            string[] expected = kind switch
            {
                AutoIdReaderKind.Rfid =>
                    [
                        "CodeTypes", "ScanSettings", "TagTypes",
                        "RfPower", "MinRssi", "EnableAntennas"
                    ],
                AutoIdReaderKind.Rtls => ["CodeTypes", "ScanSettings"],
                _ => ["CodeTypes", "ScanSettings", "TemplateName", "MatchCode"]
            };
            Assert.That(children.Select(child => child.BrowseName.Name), Is.EquivalentTo(expected));
            Assert.That(children.Select(child => child.BrowseName.Name), Does.Not.Contain("CodeTypesRWData"));
            foreach (string name in expected.Where(name => name is not ("CodeTypes" or "ScanSettings")))
            {
                BaseVariableState variable = ReaderParameter(reader, name);
                bool selection = name == "TagTypes";
                NodeId dataType = name switch
                {
                    "TagTypes" or "EnableAntennas" => Opc.Ua.DataTypeIds.UInt32,
                    "RfPower" => Opc.Ua.DataTypeIds.SByte,
                    "MinRssi" => Opc.Ua.DataTypeIds.Int32,
                    _ => Opc.Ua.DataTypeIds.String
                };
                Assert.Multiple(() =>
                {
                    Assert.That(variable.DataType, Is.EqualTo(dataType), name);
                    Assert.That(variable.ValueRank,
                        Is.EqualTo(selection ? ValueRanks.OneDimension : ValueRanks.Scalar));
                    Assert.That(variable.ReferenceTypeId, Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.HasComponent));
                    Assert.That(variable.BrowseName.NamespaceIndex,
                        Is.EqualTo(m_fixture.Context.NamespaceUris.GetIndex(AutoIdModel.NamespaceUri)));
                    Assert.That(variable.AccessLevel, Is.EqualTo(AccessLevels.CurrentReadOrWrite));
                    Assert.That(variable.UserAccessLevel, Is.EqualTo(AccessLevels.CurrentReadOrWrite));
                    Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(variable.Timestamp.IsNull, Is.False);
                    Assert.That(variable.OnWriteValueAsync, Is.Not.Null);
                    Assert.That(variable.RolePermissions[0].RoleId,
                        Is.EqualTo(Opc.Ua.ObjectIds.WellKnownRole_Operator));
                });
                if (selection)
                {
                    var discrete = (MultiStateDiscreteState)variable;
                    Assert.Multiple(() =>
                    {
                        Assert.That(discrete.TypeDefinitionId,
                            Is.EqualTo(Opc.Ua.VariableTypeIds.MultiStateDiscreteType));
                        Assert.That(discrete.ArrayDimensions.ToArray(), Is.EqualTo(new uint[] { 0 }));
                        Assert.That(discrete.EnumStrings!.BrowseName,
                            Is.EqualTo(new QualifiedName(Opc.Ua.BrowseNames.EnumStrings)));
                        Assert.That(discrete.EnumStrings.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.LocalizedText));
                        Assert.That(discrete.EnumStrings.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
                        Assert.That(discrete.EnumStrings.ArrayDimensions.ToArray(), Is.EqualTo(new uint[] { 0 }));
                        Assert.That(discrete.EnumStrings.ReferenceTypeId,
                            Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.HasProperty));
                        Assert.That(discrete.EnumStrings.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
                        Assert.That(discrete.EnumStrings.Value.ToArray()!.Select(value => value.Text),
                            Is.EqualTo(s_tagTypeNames));
                    });
                }
            }
        }

        /// <summary>
        /// Leaves unsupported fields absent even for a provider opting into another reader parameter.
        /// </summary>
        [Test]
        public async Task UnsupportedReaderParametersRemainAbsentAsync()
        {
            var provider = new ParameterProvider
            {
                ReaderParameterCapabilities = new() { SupportsMinRssi = true },
                Parameters = new() { CodeType = "EPC", CodeTypes = [0], MinRssi = -60 }
            };
            await using AutoIdReader reader = await m_fixture.Runtime.Reader(Name(), provider)
                .WithRuntimeParameters().BuildAsync().ConfigureAwait(false);
            var children = new List<BaseInstanceState>();
            reader.Device.RuntimeParameters!.GetChildren(m_fixture.Context, children);
            Assert.That(children.Select(child => child.BrowseName.Name),
                Is.EquivalentTo(s_minRssiParameterNames));
        }

        /// <summary>
        /// Commits the complete supported snapshot and preserves the other parameter values.
        /// </summary>
        [TestCase(AutoIdReaderKind.Rfid)]
        [TestCase(AutoIdReaderKind.Ocr)]
        [TestCase(AutoIdReaderKind.Optical)]
        [TestCase(AutoIdReaderKind.OpticalVerifier)]
        public async Task ReaderParameterWritesCommitThroughTheExistingProviderAsync(AutoIdReaderKind kind)
        {
            ParameterProvider provider = SpecificProvider(kind);
            await using AutoIdReader reader = await m_fixture.Runtime.Reader(Name(), provider)
                .WithRuntimeParameters().BuildAsync().ConfigureAwait(false);
            (string Name, Variant Value)[] writes = kind == AutoIdReaderKind.Rfid
                ? [
                    ("TagTypes", Variant.From(new uint[] { 1 }.ToArrayOf())),
                    ("RfPower", Variant.From((sbyte)15)),
                    ("MinRssi", Variant.From(-70)),
                    ("EnableAntennas", Variant.From(1u))
                ]
                : [("TemplateName", Variant.From("B")), ("MatchCode", Variant.From("Target"))];
            foreach ((string name, Variant value) in writes)
            {
                BaseVariableState variable = ReaderParameter(reader, name);
                Assert.That(ServiceResult.IsGood(await WriteAsync(variable, value)), Is.True, name);
                Assert.That(variable.Value, Is.EqualTo(value), name);
                Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Good), name);
            }
            Assert.That(provider.Writes, Is.EqualTo(writes.Length));
            Assert.That(provider.Parameters.CodeType, Is.EqualTo("EPC"));
            if (kind == AutoIdReaderKind.Rfid)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(provider.Parameters.TagTypes.ToArray(), Is.EqualTo(new uint[] { 1 }));
                    Assert.That(provider.Parameters.RfPower, Is.EqualTo((sbyte)15));
                    Assert.That(provider.Parameters.MinRssi, Is.EqualTo(-70));
                    Assert.That(provider.Parameters.EnableAntennas, Is.EqualTo(1u));
                });
            }
            else
            {
                Assert.That(provider.Parameters.TemplateName, Is.EqualTo("B"));
                Assert.That(provider.Parameters.MatchCode, Is.EqualTo("Target"));
            }
        }

        /// <summary>
        /// Keeps a parameter write pending until its asynchronous change notification completes.
        /// </summary>
        [Test]
        public async Task ParameterWriteAwaitsAsyncChangeNotificationAsync()
        {
            (AutoIdReader reader, ParameterProvider provider) = await CreateAsync().ConfigureAwait(false);
            BaseVariableState cycles = Parameter(reader, "Cycles");
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            cycles.OnStateChangedAsync = async (_, _, _, cancellationToken) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            };

            await AutoIdGatedTest.RunAsync(
                reader,
                () => WriteAsync(cycles, Variant.From(4)).AsTask(),
                () => release.TrySetResult(true),
                async (invocation, _) =>
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Task<ServiceResult> write =
                        await invocation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(write.IsCompleted, Is.False);
                        Assert.That(provider.Parameters.Cycles, Is.EqualTo(4));
                        Assert.That(cycles.Value, Is.EqualTo(Variant.From(4)));
                    });
                },
                result => Assert.That(ServiceResult.IsGood(result), Is.True)).ConfigureAwait(false);
        }

        /// <summary>
        /// Zero has no observable effect and nonzero masks cannot extend the general device configuration.
        /// </summary>
        [Test]
        public async Task AntennaZeroIsNoOpAndUnconfiguredBitsAreRejectedAsync()
        {
            ParameterProvider provider = SpecificProvider(AutoIdReaderKind.Rfid);
            await using AutoIdReader reader = await m_fixture.Runtime.Reader(Name(), provider)
                .WithRuntimeParameters().BuildAsync().ConfigureAwait(false);
            BaseVariableState antennas = ReaderParameter(reader, "EnableAntennas");
            DateTimeUtc timestamp = antennas.Timestamp;
            StatusCode quality = antennas.StatusCode;
            ServiceResult zero = await WriteAsync(antennas, Variant.From(0u));
            ServiceResult outside = await WriteAsync(antennas, Variant.From(4u));
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(zero), Is.True);
                Assert.That(outside.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(antennas.Value, Is.EqualTo(Variant.From(3u)));
                Assert.That(antennas.Timestamp, Is.EqualTo(timestamp));
                Assert.That(antennas.StatusCode, Is.EqualTo(quality));
                Assert.That(provider.Parameters.EnableAntennas, Is.EqualTo(3u));
                Assert.That(provider.Writes, Is.Zero);
            });
        }

        /// <summary>
        /// Invalid indexes, wrong scalar types and out-of-device selections never reach the provider.
        /// </summary>
        [Test]
        public async Task InvalidReaderValuesPreserveConfigurationAndQualityAsync()
        {
            ParameterProvider provider = SpecificProvider(AutoIdReaderKind.Rfid);
            await using AutoIdReader reader = await m_fixture.Runtime.Reader(Name(), provider)
                .WithRuntimeParameters().BuildAsync().ConfigureAwait(false);
            BaseVariableState tags = ReaderParameter(reader, "TagTypes");
            DateTimeUtc timestamp = tags.Timestamp;
            Assert.That((await WriteAsync(tags, Variant.From(new uint[] { 0, 0 }.ToArrayOf()))).StatusCode,
                Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That((await WriteAsync(tags, Variant.From(new uint[] { 2 }.ToArrayOf()))).StatusCode,
                Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That((await WriteAsync(tags, Variant.From(0u))).StatusCode,
                Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That((await WriteAsync(ReaderParameter(reader, "RfPower"), Variant.From(20))).StatusCode,
                Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.Multiple(() =>
            {
                Assert.That(tags.Value, Is.EqualTo(Variant.From(new uint[] { 0 }.ToArrayOf())));
                Assert.That(tags.Timestamp, Is.EqualTo(timestamp));
                Assert.That(tags.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(provider.Writes, Is.Zero);
            });
            Assert.That(ServiceResult.IsGood(await WriteAsync(tags, Variant.From(ArrayOf.Empty<uint>()))), Is.True);
            Assert.That(provider.Parameters.TagTypes.IsEmpty, Is.True);
        }

        /// <summary>
        /// Optional selections retain the standard access and index-range validation before commit.
        /// </summary>
        [Test]
        public async Task ReaderParameterAccessDenialAndIndexRangePreserveSettingsAsync()
        {
            ParameterProvider provider = SpecificProvider(AutoIdReaderKind.Rfid);
            await using AutoIdReader reader = await m_fixture.Runtime.Reader(Name(), provider)
                .WithRuntimeParameters().BuildAsync().ConfigureAwait(false);
            BaseVariableState tags = ReaderParameter(reader, "TagTypes");
            DateTimeUtc timestamp = tags.Timestamp;
            Variant selection = Variant.From(new uint[] { 1 }.ToArrayOf());
            AttributeWriteResult range = await tags.OnWriteValueAsync!(
                m_fixture.Context, tags, new NumericRange(0), selection, default).ConfigureAwait(false);
            tags.UserAccessLevel = AccessLevels.CurrentRead;
            ServiceResult denied = await tags.WriteAttributeAsync(
                m_fixture.Context, Attributes.Value, default, new DataValue(selection), default).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(range.Result.StatusCode, Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(tags.Value, Is.EqualTo(Variant.From(new uint[] { 0 }.ToArrayOf())));
                Assert.That(tags.Timestamp, Is.EqualTo(timestamp));
                Assert.That(provider.Writes, Is.Zero);
            });
        }

        /// <summary>
        /// Provider rejection, cancellation and reader activity leave the published snapshot untouched.
        /// </summary>
        [Test]
        public async Task ReaderParameterCommitFailureCancellationAndActivityPreserveValuesAsync()
        {
            ParameterProvider provider = SpecificProvider(AutoIdReaderKind.Optical);
            await using AutoIdReader reader = await m_fixture.Runtime.Reader(Name(), provider)
                .WithRuntimeParameters(true).BuildAsync().ConfigureAwait(false);
            BaseVariableState template = ReaderParameter(reader, "TemplateName");
            DateTimeUtc timestamp = template.Timestamp;
            Assert.That((await WriteAsync(template, Variant.From("Unknown"))).StatusCode,
                Is.EqualTo(StatusCodes.BadOutOfRange));
            provider.RejectWrites = true;
            Assert.That((await WriteAsync(template, Variant.From("B"))).StatusCode,
                Is.EqualTo(StatusCodes.BadDeviceFailure));
            provider.RejectWrites = false;
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await template.OnWriteValueAsync!(
                    m_fixture.Context, template, default, Variant.From("B"), cancelled.Token).ConfigureAwait(false));
            await WriteAsync(reader.Device.ScanActive!, Variant.From(true));
            Assert.That((await WriteAsync(template, Variant.From("B"))).StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidState));
            await WriteAsync(reader.Device.ScanActive!, Variant.From(false));
            Assert.Multiple(() =>
            {
                Assert.That(template.Value, Is.EqualTo(Variant.From("A")));
                Assert.That(template.Timestamp, Is.EqualTo(timestamp));
                Assert.That(template.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(provider.Parameters.TemplateName, Is.EqualTo("A"));
                Assert.That(provider.Writes, Is.Zero);
            });
        }

        /// <summary>
        /// Rejects inconsistent declarations before publishing a reader and snapshots provider-owned arrays.
        /// </summary>
        [Test]
        public async Task CapabilityMismatchIsRejectedAndParameterArraysAreCopiedAsync()
        {
            ParameterProvider rfid = SpecificProvider(AutoIdReaderKind.Rfid);
            var invalid = new ParameterProvider
            {
                Kind = AutoIdReaderKind.Rtls,
                ReaderParameterCapabilities = rfid.ReaderParameterCapabilities,
                Parameters = rfid.Parameters
            };
            ServiceResultException? rejected = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_fixture.Runtime.Reader(Name(), invalid).WithRuntimeParameters().BuildAsync()
                    .ConfigureAwait(false));
            Assert.That(rejected!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            uint[] initial = [0];
            string[] names = ["TypeA", "TypeB"];
            var provider = new ParameterProvider
            {
                ReaderParameterCapabilities = new() { TagTypes = names.ToArrayOf() },
                Parameters = new() { CodeType = "EPC", CodeTypes = [0], TagTypes = initial.ToArrayOf() }
            };
            await using AutoIdReader reader = await m_fixture.Runtime.Reader(Name(), provider)
                .WithRuntimeParameters().BuildAsync().ConfigureAwait(false);
            initial[0] = 100;
            names[0] = "Changed";
            var tags = (MultiStateDiscreteState)ReaderParameter(reader, "TagTypes");
            Assert.That(tags.Value, Is.EqualTo(Variant.From(new uint[] { 0 }.ToArrayOf())));
            Assert.That(tags.EnumStrings!.Value[0].Text, Is.EqualTo("TypeA"));
            uint[] selected = [1];
            await WriteAsync(tags, Variant.From(selected.ToArrayOf()));
            selected[0] = 100;
            Assert.That(tags.Value, Is.EqualTo(Variant.From(new uint[] { 1 }.ToArrayOf())));
            Assert.That(provider.Parameters.TagTypes[0], Is.EqualTo(1));
            Assert.That(MemoryMarshal.TryGetArray(provider.Parameters.TagTypes.Memory,
                out ArraySegment<uint> providerStorage), Is.True);
            providerStorage.Array![providerStorage.Offset] = 100;
            Assert.That(tags.Value, Is.EqualTo(Variant.From(new uint[] { 1 }.ToArrayOf())));
            await WriteAsync(Parameter(reader, "Cycles"), Variant.From(2));
            Assert.That(provider.Parameters.TagTypes[0], Is.EqualTo(1));
        }

        /// <summary>
        /// Rejects inconsistent values, duplicate catalogues and selections outside their declared domain.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public async Task InvalidInitialReaderParameterDeclarationsAreRejectedAsync(int scenario)
        {
            StatusCode status = scenario < 4 ? StatusCodes.BadConfigurationError : StatusCodes.BadOutOfRange;
            AutoIdReaderParameterCapabilities capabilities = scenario switch
            {
                0 => new() { SupportsMinRssi = true },
                1 => new() { TagTypes = ["Duplicate", "Duplicate"] },
                2 => new() { TagTypes = [string.Empty] },
                3 => new(),
                4 => new() { TagTypes = ["Only"] },
                _ => new() { ConfiguredAntennas = 1 }
            };
            AutoIdScanParameters parameters = new() { CodeType = "EPC", CodeTypes = [0] };
            parameters = scenario switch
            {
                1 or 2 => parameters with { TagTypes = [0] },
                3 => parameters with { MinRssi = -60 },
                4 => parameters with { TagTypes = [1] },
                5 => parameters with { EnableAntennas = 2 },
                _ => parameters
            };
            var provider = new ParameterProvider
            {
                ReaderParameterCapabilities = capabilities,
                Parameters = parameters
            };
            QualifiedName name = Name();
            ServiceResultException? rejected = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_fixture.Runtime.Reader(name, provider).WithRuntimeParameters().BuildAsync()
                    .ConfigureAwait(false));
            Assert.That(rejected!.StatusCode, Is.EqualTo(status));
            Assert.That(provider.Sink, Is.Null);
            await using AutoIdReader valid = await m_fixture.Runtime.Reader(name, new ParameterProvider())
                .WithRuntimeParameters().BuildAsync().ConfigureAwait(false);
            Assert.That(valid.NodeId.IsNull, Is.False);
        }

        /// <summary>
        /// A cancelled optional commit retains its published value and prevents a concurrent start until released.
        /// </summary>
        [Test]
        public async Task ReaderParameterCancellationSerializesStartAndShutdownRejectsWritesAsync()
        {
            ParameterProvider provider = SpecificProvider(AutoIdReaderKind.Rfid);
            provider.CommitRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using AutoIdReader reader = await m_fixture.Runtime.Reader(Name(), provider)
                .WithRuntimeParameters(true).BuildAsync().ConfigureAwait(false);
            BaseVariableState power = ReaderParameter(reader, "RfPower");
            DateTimeUtc timestamp = power.Timestamp;
            using var cancellation = new CancellationTokenSource();
            Task<AttributeWriteResult> write = power.OnWriteValueAsync!(
                m_fixture.Context, power, default, Variant.From((sbyte)15), cancellation.Token).AsTask();
            await provider.CommitEntered.Task.ConfigureAwait(false);
            Task<ServiceResult> start = WriteAsync(reader.Device.ScanActive!, Variant.From(true)).AsTask();
            Assert.That(provider.Starts, Is.Zero);
            cancellation.Cancel();
            provider.CommitRelease.SetResult(true);
            await Assert.ThatAsync(() => write,
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(ServiceResult.IsGood(await start.ConfigureAwait(false)), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(provider.Parameters.RfPower, Is.EqualTo((sbyte)10));
                Assert.That(power.Value, Is.EqualTo(Variant.From((sbyte)10)));
                Assert.That(power.Timestamp, Is.EqualTo(timestamp));
                Assert.That(power.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(provider.Writes, Is.Zero);
            });
            await reader.DisposeAsync().ConfigureAwait(false);
            Assert.That((await WriteAsync(power, Variant.From((sbyte)15))).StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidState));
        }

        /// <summary>
        /// Supplies explicit scripted capabilities without deriving them from reader kind or antenna names.
        /// </summary>
        internal static ParameterProvider SpecificProvider(AutoIdReaderKind kind)
        {
            bool rfid = kind == AutoIdReaderKind.Rfid;
            bool optical = kind is AutoIdReaderKind.Ocr or
                AutoIdReaderKind.Optical or AutoIdReaderKind.OpticalVerifier;
            return new ParameterProvider
            {
                Kind = kind,
                ReaderParameterCapabilities = new()
                {
                    TagTypes = rfid ? ["EPC Class1 Gen2 V2", "ISO 15693"] : default,
                    SupportsRfPower = rfid,
                    SupportsMinRssi = rfid,
                    ConfiguredAntennas = rfid ? 3u : null,
                    TemplateNames = optical ? ["A", "B"] : default,
                    SupportsMatchCode = optical
                },
                Parameters = new()
                {
                    CodeType = "EPC",
                    CodeTypes = [0],
                    TagTypes = rfid ? [0] : default,
                    RfPower = rfid ? (sbyte)10 : null,
                    MinRssi = rfid ? -60 : null,
                    EnableAntennas = rfid ? 3u : null,
                    TemplateName = optical ? "A" : null,
                    MatchCode = optical ? string.Empty : null
                }
            };
        }

        /// <summary>
        /// Gets a standard direct child rather than a ScanSettings member.
        /// </summary>
        private BaseVariableState ReaderParameter(AutoIdReader reader, string name)
        {
            return (BaseVariableState)reader.Device.RuntimeParameters!.FindChildWithQualifiedName(
                m_fixture.Context,
                new QualifiedName(name, m_fixture.Context.NamespaceUris.GetIndexOrAppend(AutoIdModel.NamespaceUri)))!;
        }

        /// <summary>The declared tag type names.</summary>
        private static readonly string[] s_tagTypeNames = ["EPC Class1 Gen2 V2", "ISO 15693"];

        /// <summary>The parameters exposed when only MinRssi is supported.</summary>
        private static readonly string[] s_minRssiParameterNames = ["CodeTypes", "ScanSettings", "MinRssi"];
    }
}
