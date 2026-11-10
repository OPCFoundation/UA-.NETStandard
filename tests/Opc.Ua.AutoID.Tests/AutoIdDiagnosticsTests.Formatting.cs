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
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.AutoID.Server;

namespace Opc.Ua.AutoID.Tests
{
    public sealed partial class AutoIdDiagnosticsTests
    {
        /// <summary>
        /// Binds the model's selection metadata and commits independently copied format indexes.
        /// </summary>
        [Test]
        public async Task DiagnosticFormatSelectionUsesPublishedMetadataAndCopiesCommitsAsync()
        {
            (AutoIdReader reader, Mock<IAutoIdRfidDiagnosticFormatter> provider) =
                await CreateFormatterAsync().ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                var variable = (MultiStateDiscreteState)FormatSelection(reader);
                Assert.Multiple(() =>
                {
                    Assert.That(variable.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.UInt32));
                    Assert.That(variable.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
                    Assert.That(variable.ArrayDimensions.ToArray(), Is.EqualTo(new uint[] { 0 }));
                    Assert.That(variable.TypeDefinitionId,
                        Is.EqualTo(Opc.Ua.VariableTypeIds.MultiStateDiscreteType));
                    Assert.That(variable.AccessLevel, Is.EqualTo(AccessLevels.CurrentReadOrWrite));
                    Assert.That(variable.OnWriteValueAsync, Is.Not.Null);
                    Assert.That(variable.EnumStrings!.Value.ToArray()!.Select(value => value.Text),
                        Is.EqualTo(s_rawDiagnosticFormats));
                    Assert.That(variable.EnumStrings.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
                });
                AutoIdScanParameters? committed = null;
                provider.Setup(value => value.WriteParametersAsync(
                    It.IsAny<AutoIdScanParameters>(), It.IsAny<CancellationToken>()))
                    .Callback<AutoIdScanParameters, CancellationToken>((parameters, _) => committed = parameters);
                uint[] indexes = [1, 0];
                Assert.That(ServiceResult.IsGood(await WriteFormatAsync(reader, indexes.ToArrayOf())),
                    Is.True);
                indexes[0] = 9;
                Assert.That(committed, Is.Not.Null);
                Assert.That(committed!.CodeTypesRWData.ToArray(), Is.EqualTo(new uint[] { 1, 0 }));
                committed.CodeTypesRWData.ToArray()![0] = 8;
                Assert.That(variable.Value, Is.EqualTo(Variant.From(new uint[] { 1, 0 }.ToArrayOf())));
                DateTimeUtc timestamp = variable.Timestamp;
                foreach (ArrayOf<uint> invalid in new ArrayOf<uint>[] { [2], [0, 0], default })
                {
                    Assert.That(ServiceResult.IsBad(await WriteFormatAsync(reader, invalid)), Is.True);
                    Assert.That(variable.Value, Is.EqualTo(Variant.From(new uint[] { 1, 0 }.ToArrayOf())));
                    Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
                }
                provider.Verify(value => value.WriteParametersAsync(
                    It.IsAny<AutoIdScanParameters>(), It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        /// <summary>
        /// Empty selection suppresses only subsequent diagnostic RWData and does not invoke the formatter.
        /// </summary>
        [Test]
        public async Task EmptyDiagnosticSelectionClearsDataWithoutChangingOtherAccessFieldsAsync()
        {
            (AutoIdReader reader, Mock<IAutoIdRfidDiagnosticFormatter> provider) =
                await CreateFormatterAsync().ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                RfidAccessResult report = FormattingAccess();
                await reader.ReportAccessAsync("ReadTag", "Controller", [report]).ConfigureAwait(false);
                BaseVariableState variable = FormatterDiagnostic(reader);
                Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Good));
                Variant previous = variable.Value;
                Assert.That(ServiceResult.IsGood(await WriteFormatAsync(reader, ArrayOf<uint>.Empty)), Is.True);
                Assert.That(variable.Value, Is.EqualTo(previous), "A selection write does not rewrite history.");
                await reader.ReportAccessAsync("ReadTag", "Controller", [report]).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(variable.Value.IsNull, Is.True);
                    Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
                    Assert.That(variable.Timestamp, Is.EqualTo(report.Timestamp));
                    Assert.That(Variable(reader, "LastAccess", "Identifier").Value, Is.EqualTo(Variant.From("Tag")));
                    Assert.That(report.RWData.ByteString, Is.EqualTo(ByteString.From(1, 2)));
                });
                provider.Verify(value => value.FormatRWDataAsync(
                    It.IsAny<ScanData>(), It.IsAny<string>(), It.IsAny<ArrayOf<string>>(),
                    It.IsAny<CancellationToken>()), Times.Once);
                Assert.That(ServiceResult.IsGood(await WriteFormatAsync(reader, new uint[] { 0 }.ToArrayOf())),
                    Is.True);
                await reader.ReportAccessAsync("ReadTag", "Controller", [report]).ConfigureAwait(false);
                Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(variable.Value, Is.EqualTo(previous));
            }
        }

        /// <summary>
        /// Providers choose an actual selected format while method output and access-event data remain raw bytes.
        /// </summary>
        [Test]
        public async Task DiagnosticFormatterChangesNeitherTagMethodOutputNorEventDataAsync()
        {
            await using var fixture = new AutoIdManagerFixture();
            await fixture.StartAsync(new AutoIdServerOptions { PublishAccessEvents = true }).ConfigureAwait(false);
            (AutoIdReader reader, Mock<IAutoIdRfidDiagnosticFormatter> provider) =
                await CreateFormatterAsync(fixture).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                Assert.That(ServiceResult.IsGood(await WriteFormatAsync(reader, new uint[] { 0, 1 }.ToArrayOf())),
                    Is.True);
                provider.Setup(value => value.FormatRWDataAsync(
                    It.IsAny<ScanData>(), "RAW:BYTES", It.IsAny<ArrayOf<string>>(),
                    It.IsAny<CancellationToken>()))
                    .Returns((ScanData data, string _, ArrayOf<string> selected, CancellationToken _) =>
                    {
                        Assert.That(selected.ToArray(), Is.EquivalentTo(s_rawDiagnosticFormats));
                        data.ByteString = ByteString.From(9);
                        return new ValueTask<AutoIdRfidDiagnosticData>(new AutoIdRfidDiagnosticData(
                            "RAW:STRING", new ScanData { SwitchField = ScanDataFields.String, String = "Product" }));
                    });
                var events = new EventCapture(reader.Device);
                var device = (RfidReaderDeviceState)reader.Device;
                ReadTagMethodStateResult result = await device.ReadTag!.OnCallAsync!(
                    fixture.Context, device.ReadTag, device.NodeId,
                    AutoIdTestData.Epc(AutoIdTestData.Bytes(1, 2)), "EPC", 1, 0, 2,
                    ByteString.Empty, CancellationToken.None).ConfigureAwait(false);
                RfidAccessResult access = events.Of<RfidAccessEventState>().Single().AccessResult!.Value[0];
                Assert.Multiple(() =>
                {
                    Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                    Assert.That(result.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                    Assert.That(result.ResultData, Is.EqualTo(ByteString.From(1, 2)));
                    Assert.That(access.CodeTypeRWData, Is.EqualTo("RAW:BYTES"));
                    Assert.That(access.RWData.ByteString, Is.EqualTo(ByteString.From(1, 2)));
                    Assert.That(FormatterDiagnostic(reader).Value, Is.EqualTo(Variant.From("Product")));
                    Assert.That(FormatterDiagnostic(reader).Timestamp, Is.EqualTo(access.Timestamp));
                });
                provider.Setup(value => value.FormatRWDataAsync(
                    It.IsAny<ScanData>(), It.IsAny<string>(), It.IsAny<ArrayOf<string>>(),
                    It.IsAny<CancellationToken>())).Throws(new InvalidOperationException("Formatting failed."));
                ReadTagMethodStateResult afterFailure = await device.ReadTag.OnCallAsync!(
                    fixture.Context, device.ReadTag, device.NodeId,
                    AutoIdTestData.Epc(AutoIdTestData.Bytes(1, 2)), "EPC", 1, 0, 2,
                    ByteString.Empty, CancellationToken.None).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(ServiceResult.IsGood(afterFailure.ServiceResult), Is.True);
                    Assert.That(afterFailure.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                    Assert.That(afterFailure.ResultData, Is.EqualTo(ByteString.From(1, 2)));
                    Assert.That(events.Of<RfidAccessEventState>().Count(), Is.EqualTo(2));
                    Assert.That(FormatterDiagnostic(reader).StatusCode, Is.EqualTo(StatusCodes.BadNoData));
                });
                ReadTagMethodStateResult protectedData = await device.ReadTag.OnCallAsync!(
                    fixture.Context, device.ReadTag, device.NodeId,
                    AutoIdTestData.Epc(AutoIdTestData.Bytes(1, 2)), "EPC", 0, 0, 2,
                    ByteString.Empty, CancellationToken.None).ConfigureAwait(false);
                Assert.That(protectedData.Status, Is.EqualTo(AutoIdOperationStatusEnumeration.SUCCESS));
                Assert.That(events.Of<RfidAccessEventState>().Last().AccessResult!.Value[0].EncodingMask &
                    (uint)RfidAccessResultFields.RWData, Is.Zero);
                provider.Verify(value => value.FormatRWDataAsync(
                    It.IsAny<ScanData>(), It.IsAny<string>(), It.IsAny<ArrayOf<string>>(),
                    It.IsAny<CancellationToken>()), Times.Exactly(2));
            }
        }

        /// <summary>
        /// Copies formatter output and prevents a formatter from rewriting its input report.
        /// </summary>
        [Test]
        public async Task FormatterInputAndOutputStorageRemainIndependentAsync()
        {
            (AutoIdReader reader, Mock<IAutoIdRfidDiagnosticFormatter> provider) =
                await CreateFormatterAsync().ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                byte[] output = [3, 4];
                var formatted = new ScanData
                {
                    SwitchField = ScanDataFields.ByteString, ByteString = ByteString.From(output)
                };
                provider.Setup(value => value.FormatRWDataAsync(
                    It.IsAny<ScanData>(), It.IsAny<string>(), It.IsAny<ArrayOf<string>>(),
                    It.IsAny<CancellationToken>()))
                    .Returns((ScanData input, string _, ArrayOf<string> _, CancellationToken _) =>
                    {
                        Assert.That(MemoryMarshal.TryGetArray(input.ByteString.Memory, out ArraySegment<byte> bytes),
                            Is.True);
                        bytes.Array![bytes.Offset] = 6;
                        input.ByteString = ByteString.From(9);
                        return new ValueTask<AutoIdRfidDiagnosticData>(
                            new AutoIdRfidDiagnosticData("RAW:BYTES", formatted));
                    });
                RfidAccessResult report = FormattingAccess();
                await reader.ReportAccessAsync("ReadTag", "Controller", [report]).ConfigureAwait(false);
                output[0] = 8;
                formatted.ByteString = ByteString.From(7);
                Assert.Multiple(() =>
                {
                    Assert.That(FormatterDiagnostic(reader).Value, Is.EqualTo(Variant.From(ByteString.From(3, 4))));
                    Assert.That(report.RWData.ByteString, Is.EqualTo(ByteString.From(1, 2)));
                });
            }
        }

        /// <summary>
        /// Rejected configuration commits retain the previously selected diagnostic representation.
        /// </summary>
        [Test]
        public async Task RejectedDiagnosticSelectionRetainsConfigurationAndPublishedDataAsync()
        {
            (AutoIdReader reader, Mock<IAutoIdRfidDiagnosticFormatter> provider) =
                await CreateFormatterAsync().ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                RfidAccessResult report = FormattingAccess();
                await reader.ReportAccessAsync("Previous", "Controller", [report]).ConfigureAwait(false);
                BaseVariableState selection = FormatSelection(reader);
                DateTimeUtc timestamp = selection.Timestamp;
                provider.Setup(value => value.WriteParametersAsync(
                    It.IsAny<AutoIdScanParameters>(), It.IsAny<CancellationToken>()))
                    .Throws(new ServiceResultException(StatusCodes.BadDeviceFailure));
                ServiceResult rejected = await WriteFormatAsync(reader, new uint[] { 1 }.ToArrayOf());
                Assert.Multiple(() =>
                {
                    Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadDeviceFailure));
                    Assert.That(selection.Value, Is.EqualTo(Variant.From(new uint[] { 0 }.ToArrayOf())));
                    Assert.That(selection.Timestamp, Is.EqualTo(timestamp));
                });
                await reader.ReportAccessAsync("Next", "Controller", [report]).ConfigureAwait(false);
                Assert.That(FormatterDiagnostic(reader).StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(FormatterDiagnostic(reader).Value, Is.EqualTo(Variant.From(ByteString.From(1, 2))));
                provider.Verify(value => value.FormatRWDataAsync(
                    It.IsAny<ScanData>(), It.IsAny<string>(),
                    It.Is<ArrayOf<string>>(names => names.Count == 1 && names[0] == "RAW:BYTES"),
                    It.IsAny<CancellationToken>()), Times.Exactly(2));
            }
        }

        /// <summary>
        /// Cancellation during formatting leaves the complete previous diagnostic group unchanged.
        /// </summary>
        [Test]
        public async Task CancelledDiagnosticFormattingRetainsPreviousAccessSnapshotAsync()
        {
            (AutoIdReader reader, Mock<IAutoIdRfidDiagnosticFormatter> provider) =
                await CreateFormatterAsync().ConfigureAwait(false);
            await reader.ReportAccessAsync("Previous", "Controller", [FormattingAccess()]).ConfigureAwait(false);
            BaseVariableState variable = FormatterDiagnostic(reader);
            Variant previous = variable.Value;
            DateTimeUtc timestamp = variable.Timestamp;
            using var cancellation = new CancellationTokenSource();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.Setup(value => value.FormatRWDataAsync(
                It.IsAny<ScanData>(), It.IsAny<string>(), It.IsAny<ArrayOf<string>>(),
                It.IsAny<CancellationToken>()))
                .Returns(async (ScanData input, string _, ArrayOf<string> _, CancellationToken ct) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    return new AutoIdRfidDiagnosticData("RAW:BYTES", input);
                });
            await AutoIdGatedTest.RunAsync(
                reader,
                async () =>
                {
                    try
                    {
                        await reader.ReportAccessAsync(
                            "Cancelled", "Controller", [FormattingAccess()], cancellation.Token).ConfigureAwait(false);
                        return false;
                    }
                    catch (OperationCanceledException)
                    {
                        return true;
                    }
                },
                () => release.TrySetResult(true),
                async (invocation, _) =>
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Task<bool> report = await invocation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Assert.That(report.IsCompleted, Is.False);
                    cancellation.Cancel();
                },
                cancelled =>
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(cancelled, Is.True);
                        Assert.That(variable.Value, Is.EqualTo(previous));
                        Assert.That(variable.Timestamp, Is.EqualTo(timestamp));
                        Assert.That(variable.StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(Variable(reader, "LastAccess", "Command").Value,
                            Is.EqualTo(Variant.From("Previous")));
                    });
                }).ConfigureAwait(false);
        }

        /// <summary>
        /// The simulated provider supplies the initial raw-only capability and copies parameter snapshots.
        /// </summary>
        [Test]
        public async Task SimulatedReaderProvidesRawDiagnosticFormattingAsync()
        {
            var provider = new InMemoryRfidReaderProvider();
            await using AutoIdReader reader = await m_fixture.Runtime.Reader(
                m_fixture.Name("RawFormatter" + Interlocked.Increment(ref m_sequence)), provider)
                .OwnsProvider().WithRuntimeParameters().WithDiagnostics().BuildAsync().ConfigureAwait(false);
            var selection = (MultiStateDiscreteState)FormatSelection(reader);
            Assert.That(selection.EnumStrings!.Value.ToArray()!.Select(value => value.Text),
                Is.EqualTo(s_rawByteDiagnosticFormats));
            AutoIdScanParameters snapshot = await provider.ReadParametersAsync().ConfigureAwait(false);
            snapshot.CodeTypesRWData.ToArray()![0] = 9;
            AutoIdScanParameters next = await provider.ReadParametersAsync().ConfigureAwait(false);
            Assert.That(next.CodeTypesRWData.ToArray(), Is.EqualTo(new uint[] { 0 }));
            await reader.ReportAccessAsync("ReadTag", "Controller", [FormattingAccess()]).ConfigureAwait(false);
            Assert.That(FormatterDiagnostic(reader).Value, Is.EqualTo(Variant.From(ByteString.From(1, 2))));
            Assert.That(FormatterDiagnostic(reader).StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// A catalogue alone cannot expose a setting whose diagnostic behavior has no provider implementation.
        /// </summary>
        [Test]
        public async Task DiagnosticCatalogueWithoutFormatterIsRejectedBeforeAttachAsync()
        {
            var provider = new Mock<IAutoIdReaderParametersProvider>();
            provider.SetupGet(value => value.Kind).Returns(AutoIdReaderKind.Rfid);
            provider.SetupGet(value => value.SupportedCodeTypes).Returns(s_rawByteDiagnosticFormats.ToArrayOf());
            provider.SetupGet(value => value.ReaderParameterCapabilities)
                .Returns(new AutoIdReaderParameterCapabilities { CodeTypesRWData = ["RAW:BYTES"] });
            provider.Setup(value => value.ReadParametersAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AutoIdScanParameters>(new AutoIdScanParameters
                {
                    CodeType = "RAW:BYTES", CodeTypes = [0], CodeTypesRWData = [0]
                }));
            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_fixture.Runtime.Reader(
                    m_fixture.Name("MissingFormatter" + Interlocked.Increment(ref m_sequence)), provider.Object)
                    .WithRuntimeParameters().BuildAsync().ConfigureAwait(false));
            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            provider.Verify(value => value.AttachAsync(
                It.IsAny<IAutoIdReaderSink>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Invalid or failed formatting clears only RWData and cannot substitute a modified selection.
        /// </summary>
        [TestCase("unselected")]
        [TestCase("union")]
        [TestCase("throw")]
        [TestCase("mutatedSelection")]
        [TestCase("nullValue")]
        [TestCase("nullResult")]
        public async Task InvalidFormatterResultsMakeDiagnosticDataUnavailableAsync(string scenario)
        {
            (AutoIdReader reader, Mock<IAutoIdRfidDiagnosticFormatter> provider) =
                await CreateFormatterAsync().ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                RfidAccessResult report = FormattingAccess();
                await reader.ReportAccessAsync("Previous", "Controller", [report]).ConfigureAwait(false);
                provider.Setup(value => value.FormatRWDataAsync(
                    It.IsAny<ScanData>(), It.IsAny<string>(), It.IsAny<ArrayOf<string>>(),
                    It.IsAny<CancellationToken>()))
                    .Returns((ScanData _, string _, ArrayOf<string> selected, CancellationToken _) =>
                    {
                        if (scenario == "throw")
                        {
                            throw new InvalidOperationException("Formatting failed.");
                        }
                        if (scenario == "mutatedSelection")
                        {
                            selected.ToArray()![0] = "RAW:STRING";
                        }
                        return new ValueTask<AutoIdRfidDiagnosticData>(scenario switch
                        {
                            "nullResult" => null!,
                            "unselected" or "mutatedSelection" => new AutoIdRfidDiagnosticData(
                                "RAW:STRING", new ScanData { SwitchField = ScanDataFields.String, String = "Wrong" }),
                            "nullValue" => new AutoIdRfidDiagnosticData(
                                "RAW:BYTES", new ScanData
                                {
                                    SwitchField = ScanDataFields.ByteString,
                                    ByteString = default
                                }),
                            _ => new AutoIdRfidDiagnosticData(
                                "RAW:BYTES", new ScanData { SwitchField = ScanDataFields.String, String = "Wrong" })
                        });
                    });
                await reader.ReportAccessAsync("Replacement", "Controller", [report]).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(FormatterDiagnostic(reader).Value.IsNull, Is.True);
                    Assert.That(FormatterDiagnostic(reader).StatusCode, Is.EqualTo(StatusCodes.BadNoData));
                    Assert.That(Variable(reader, "LastAccess", "Command").Value,
                        Is.EqualTo(Variant.From("Replacement")));
                    Assert.That(report.RWData.ByteString, Is.EqualTo(ByteString.From(1, 2)));
                });
            }
        }

        /// <summary>
        /// Accepts the predefined and vendor code types only with their specified nonnull union values.
        /// </summary>
        [TestCase("RAW:BYTES", ScanDataFields.ByteString)]
        [TestCase("RAW:STRING", ScanDataFields.String)]
        [TestCase("EPC", ScanDataFields.Epc)]
        [TestCase("UID", ScanDataFields.ByteString)]
        [TestCase("GS1", ScanDataFields.ByteString)]
        [TestCase("ASC", ScanDataFields.ByteString)]
        [TestCase("URI", ScanDataFields.String)]
        [TestCase("CUSTOM:Product", ScanDataFields.ByteString)]
        [TestCase("CUSTOM:Product", ScanDataFields.String)]
        [TestCase("CUSTOM:Product", ScanDataFields.Custom)]
        public async Task SelectedDiagnosticCodeTypesUseTheirDeclaredUnionFieldsAsync(
            string codeType, ScanDataFields field)
        {
            (AutoIdReader reader, Mock<IAutoIdRfidDiagnosticFormatter> provider) =
                await CreateFormatterAsync(formats: new[] { codeType }.ToArrayOf()).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                var epc = new ScanDataEpc { PC = 1, UId = ByteString.From(2) };
                var data = new ScanData { SwitchField = field };
                Variant expected;
                switch (field)
                {
                    case ScanDataFields.ByteString:
                        data.ByteString = ByteString.From(3, 4);
                        expected = Variant.From(data.ByteString);
                        break;
                    case ScanDataFields.String:
                        data.String = "Product";
                        expected = Variant.From(data.String);
                        break;
                    case ScanDataFields.Epc:
                        data.Epc = epc;
                        expected = Variant.FromStructure(epc);
                        break;
                    default:
                        data.Custom = Variant.From(42);
                        expected = data.Custom;
                        break;
                }
                provider.Setup(value => value.FormatRWDataAsync(
                    It.IsAny<ScanData>(), It.IsAny<string>(), It.IsAny<ArrayOf<string>>(),
                    It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<AutoIdRfidDiagnosticData>(new AutoIdRfidDiagnosticData(codeType, data)));
                await reader.ReportAccessAsync("ReadTag", "Controller", [FormattingAccess()]).ConfigureAwait(false);
                Assert.That(FormatterDiagnostic(reader).Value, Is.EqualTo(expected));
                Assert.That(FormatterDiagnostic(reader).StatusCode, Is.EqualTo(StatusCodes.Good));
                provider.Verify(value => value.FormatRWDataAsync(
                    It.IsAny<ScanData>(), "RAW:BYTES",
                    It.Is<ArrayOf<string>>(selected => selected.Count == 1 && selected[0] == codeType),
                    It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        /// <summary>
        /// Keeps formatting admitted through shutdown detach and prevents disposal of the provider until release.
        /// </summary>
        [Test]
        public async Task DiagnosticFormatterKeepsReportAndProviderDisposalPendingAsync()
        {
            (AutoIdReader reader, Mock<IAutoIdRfidDiagnosticFormatter> provider) =
                await CreateFormatterAsync().ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var detached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            RfidAccessResult input = FormattingAccess();
            DateTimeUtc acquired = input.Timestamp;
            provider.Setup(value => value.DetachAsync(It.IsAny<CancellationToken>()))
                .Callback(() => detached.TrySetResult(true));
            provider.Setup(value => value.FormatRWDataAsync(
                It.IsAny<ScanData>(), It.IsAny<string>(), It.IsAny<ArrayOf<string>>(),
                It.IsAny<CancellationToken>()))
                .Returns(async (ScanData data, string _, ArrayOf<string> _, CancellationToken ct) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    return new AutoIdRfidDiagnosticData("RAW:BYTES", data);
                });
            await AutoIdGatedTest.RunAsync(
                reader,
                async () =>
                {
                    await reader.ReportAccessAsync("ReadTag", "Controller", [input])
                        .ConfigureAwait(false);
                    return true;
                },
                () => release.TrySetResult(true),
                async (invocation, startDisposal) =>
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Task<bool> report = await invocation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Task disposal = await startDisposal().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await detached.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(report.IsCompleted, Is.False);
                        Assert.That(disposal.IsCompleted, Is.False);
                    });
                    input.Timestamp = default;
                    input.EncodingMask = 0;
                    input.Identifier.String = "Mutated";
                    input.RWData.ByteString = ByteString.From(9);
                    provider.As<IDisposable>().Verify(value => value.Dispose(), Times.Never);
                },
                _ =>
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(FormatterDiagnostic(reader).Value,
                            Is.EqualTo(Variant.From(ByteString.From(1, 2))));
                        Assert.That(FormatterDiagnostic(reader).Timestamp, Is.EqualTo(acquired));
                        Assert.That(Variable(reader, "LastAccess", "Identifier").Value,
                            Is.EqualTo(Variant.From("Tag")));
                        Assert.That(Variable(reader, "LastAccess", "Timestamp").Value,
                            Is.EqualTo(Variant.From(acquired)));
                    });
                }).ConfigureAwait(false);
            provider.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// Creates an injected formatter with raw and string capabilities and a raw tag-read operation.
        /// </summary>
        private async Task<(AutoIdReader Reader, Mock<IAutoIdRfidDiagnosticFormatter> Provider)> CreateFormatterAsync(
            AutoIdManagerFixture? fixture = null, ArrayOf<string> formats = default)
        {
            fixture ??= m_fixture;
            ArrayOf<string> catalogue = formats.IsNull ? ["RAW:BYTES", "RAW:STRING"] : formats;
            var provider = new Mock<IAutoIdRfidDiagnosticFormatter>();
            provider.As<IDisposable>();
            provider.SetupGet(value => value.Kind).Returns(AutoIdReaderKind.Rfid);
            provider.SetupGet(value => value.SupportedCodeTypes).Returns(s_rawByteDiagnosticFormats.ToArrayOf());
            provider.SetupGet(value => value.ReaderParameterCapabilities).Returns(new AutoIdReaderParameterCapabilities
            {
                CodeTypesRWData = catalogue
            });
            provider.Setup(value => value.ReadParametersAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AutoIdScanParameters>(new AutoIdScanParameters
                {
                    CodeType = "RAW:BYTES", CodeTypes = [0], CodeTypesRWData = [0]
                }));
            provider.Setup(value => value.FormatRWDataAsync(
                It.IsAny<ScanData>(), It.IsAny<string>(), It.IsAny<ArrayOf<string>>(),
                It.IsAny<CancellationToken>()))
                .Returns((ScanData data, string _, ArrayOf<string> _, CancellationToken _) =>
                    new ValueTask<AutoIdRfidDiagnosticData>(new AutoIdRfidDiagnosticData("RAW:BYTES", data)));
            Mock<IAutoIdRfidProvider> rfid = provider.As<IAutoIdRfidProvider>();
            rfid.SetupGet(value => value.SupportedOperations).Returns(AutoIdRfidOperations.ReadTag);
            rfid.Setup(value => value.ReadTagAsync(
                It.IsAny<ScanData>(), It.IsAny<string>(), It.IsAny<ushort>(), It.IsAny<uint>(),
                It.IsAny<uint>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AutoIdTagReadOutcome>(
                    new AutoIdTagReadOutcome(ByteString.From(1, 2), AutoIdOperationStatusEnumeration.SUCCESS)));
            provider.As<IAutoIdRfidAccessEventPolicy>().Setup(value => value.CanPublishData(
                It.IsAny<string>(), It.IsAny<ushort>(), It.IsAny<uint>(), It.IsAny<uint>())).Returns(true);
            AutoIdReader reader = await fixture.Runtime.Reader(
                fixture.Name("Formatter" + Interlocked.Increment(ref m_sequence)), provider.Object)
                .OwnsProvider().WithRuntimeParameters().WithDiagnostics().BuildAsync().ConfigureAwait(false);
            return (reader, provider);
        }

        /// <summary>
        /// Gets the standard diagnostic format selection variable.
        /// </summary>
        private static BaseVariableState FormatSelection(AutoIdReader reader)
        {
            ISystemContext context = reader.Runtime.SystemContext;
            return (BaseVariableState)reader.Device.RuntimeParameters!.FindChildWithQualifiedName(
                context, new QualifiedName("CodeTypesRWData",
                    context.NamespaceUris.GetIndexOrAppend(AutoIdModel.NamespaceUri)))!;
        }

        /// <summary>
        /// Commits a complete format-selection value through the reader's asynchronous write callback.
        /// </summary>
        private static async ValueTask<ServiceResult> WriteFormatAsync(AutoIdReader reader, ArrayOf<uint> selected)
        {
            BaseVariableState variable = FormatSelection(reader);
            AttributeWriteResult result = await variable.OnWriteValueAsync!(
                reader.Runtime.SystemContext, variable, default, Variant.From(selected),
                CancellationToken.None).ConfigureAwait(false);
            return result.Result;
        }

        /// <summary>
        /// Finds RWData using the reader's own namespace table.
        /// </summary>
        private static BaseVariableState FormatterDiagnostic(AutoIdReader reader)
        {
            ISystemContext context = reader.Runtime.SystemContext;
            ushort ns = context.NamespaceUris.GetIndexOrAppend(AutoIdModel.NamespaceUri);
            NodeState access = reader.Device.Diagnostics!.FindChildWithQualifiedName(
                context, new QualifiedName("LastAccess", ns))!;
            return (BaseVariableState)access.FindChildWithQualifiedName(context, new QualifiedName("RWData", ns))!;
        }

        /// <summary>
        /// Creates a timestamped provider report containing raw user bytes.
        /// </summary>
        private static RfidAccessResult FormattingAccess()
        {
            RfidAccessResult report = Access();
            report.EncodingMask |= (uint)(RfidAccessResultFields.RWData | RfidAccessResultFields.CodeTypeRWData);
            report.CodeTypeRWData = "RAW:BYTES";
            report.RWData = new ScanData
            {
                SwitchField = ScanDataFields.ByteString, ByteString = ByteString.From(1, 2)
            };
            return report;
        }

        /// <summary>
        /// Expected raw diagnostic formats for the formatter test provider.
        /// </summary>
        private static readonly string[] s_rawDiagnosticFormats = ["RAW:BYTES", "RAW:STRING"];

        /// <summary>
        /// Expected diagnostic format for the simulated reader.
        /// </summary>
        private static readonly string[] s_rawByteDiagnosticFormats = ["RAW:BYTES"];
    }
}
