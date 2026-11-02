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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.AutoID.Server;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// Exercises the optional parameter and variable-control capability of AutoIdReader.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    public sealed class AutoIdParameterControlTests
    {
        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new AutoIdManagerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task CapabilityIsOptInAndExistingProvidersRemainCompatibleAsync()
        {
            AutoIdReader reader = await m_fixture.Runtime.CreateReaderAsync(
                Name(), new FakeProvider()).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(reader.Device.ScanActive, Is.Null);
                Assert.That(reader.Device.RuntimeParameters, Is.Null);
                Assert.Throws<InvalidOperationException>(() =>
                    m_fixture.Runtime.Reader(Name(), new FakeProvider()).WithRuntimeParameters(true));
            });
        }

        [Test]
        public async Task ParametersHavePublishedMetadataAndContributeOnlyConfiguredUnitsAsync()
        {
            (AutoIdReader reader, _) = await CreateAsync().ConfigureAwait(false);
            BaseVariableState duration = Parameter(reader, "Duration");
            BaseVariableState codes = Codes(reader);
            var strings = (PropertyState<ArrayOf<LocalizedText>>)codes.FindChild(
                m_fixture.Context, new QualifiedName(Opc.Ua.BrowseNames.EnumStrings))!;
            string?[] units = m_fixture.Runtime.ConformanceUnits.ToArray()!.Select(u => u.Name).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(duration.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Duration));
                Assert.That(reader.Device.RuntimeParameters!.ReferenceTypeId,
                    Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.HasComponent));
                Assert.That(reader.Device.ScanActive!.ReferenceTypeId,
                    Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.HasComponent));
                Assert.That(((BaseInstanceState)duration.Parent!).ReferenceTypeId,
                    Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.HasComponent));
                Assert.That(duration.ReferenceTypeId, Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.HasComponent));
                Assert.That(Parameter(reader, "Cycles").ReferenceTypeId,
                    Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.HasComponent));
                Assert.That(Parameter(reader, "DataAvailable").ReferenceTypeId,
                    Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.HasComponent));
                Assert.That(Parameter(reader, "CodeType").ReferenceTypeId,
                    Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.HasComponent));
                Assert.That(duration.ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(codes.DataType, Is.EqualTo(Opc.Ua.DataTypeIds.UInt32));
                Assert.That(codes.ReferenceTypeId, Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.HasComponent));
                Assert.That(strings.ReferenceTypeId, Is.EqualTo(Opc.Ua.Types.ReferenceTypeIds.HasProperty));
                Assert.That(codes.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
                Assert.That(strings.Value[0].Text, Is.EqualTo("EPC"));
                Assert.That(units, Does.Contain(AutoIdProfiles.DeviceParametersUnit));
                Assert.That(units, Does.Contain(AutoIdProfiles.SimpleSyncAccessControlUnit));
                Assert.That(m_fixture.Runtime.ServerProfiles.ToArray(), Does.Not.Contain(AutoIdProfiles.FullServer));
            });
        }

        [Test]
        public async Task WritesCommitBeforeCacheAndStartUsesAnIndependentSnapshotAsync()
        {
            (AutoIdReader reader, ParameterProvider provider) = await CreateAsync().ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(
                await WriteAsync(Parameter(reader, "Duration"), Variant.From(123d))), Is.True);
            Assert.That(ServiceResult.IsGood(await WriteAsync(Parameter(reader, "Cycles"), Variant.From(2))), Is.True);
            Assert.That(ServiceResult.IsGood(
                await WriteAsync(Parameter(reader, "DataAvailable"), Variant.From(true))), Is.True);
            Assert.That(ServiceResult.IsGood(await WriteAsync(reader.Device.ScanActive!, Variant.From(true))), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(provider.StartSettings!.Duration, Is.EqualTo(123));
                Assert.That(provider.StartSettings.Cycles, Is.EqualTo(2));
                Assert.That(provider.StartSettings.DataAvailable, Is.True);
                Assert.That(provider.Parameters.Duration, Is.EqualTo(123));
                Assert.That(reader.Device.ScanActive!.Value, Is.True);
            });
            ServiceResult busy = await WriteAsync(Parameter(reader, "Cycles"), Variant.From(3));
            Assert.That(busy.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(provider.StartSettings!.Cycles, Is.EqualTo(2));
            await WriteAsync(reader.Device.ScanActive!, Variant.From(false));
        }

        [Test]
        public async Task ReaderSpecificParameterBindingsAndConfiguredRolePermissionsArePreservedAsync()
        {
            var provider = new ParameterProvider();
            BaseDataVariableState? vendor = null;
            int writes = 0;
            AutoIdReader reader = await m_fixture.Runtime.Reader(Name(), provider)
                .WithRuntimeParameters(true, (context, parent) =>
                {
                    vendor = new BaseDataVariableState(parent);
                    vendor.Create(context, NodeId.Null, m_fixture.Name("Power"), new LocalizedText("Power"), true);
                    vendor.AccessLevel = vendor.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                    vendor.OnWriteValueAsync = (ctx, node, range, value, ct) =>
                    {
                        writes++;
                        return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
                    };
                    parent.AddChild(vendor);
                })
                .WithScanWritePermissions([new RolePermissionType
                {
                    RoleId = Opc.Ua.ObjectIds.WellKnownRole_Operator,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Read | PermissionType.Write)
                }])
                .BuildAsync().ConfigureAwait(false);
            await vendor!.OnWriteValueAsync!(m_fixture.Context, vendor, default, Variant.From(10), default);
            Assert.Multiple(() =>
            {
                Assert.That(vendor!.NodeId.IsNull, Is.False);
                Assert.That(writes, Is.EqualTo(1));
                Assert.That(provider.Writes, Is.Zero);
                Assert.That(reader.Device.ScanActive!.RolePermissions[0].RoleId,
                    Is.EqualTo(Opc.Ua.ObjectIds.WellKnownRole_Operator));
                Assert.That(Parameter(reader, "Cycles").RolePermissions[0].Permissions,
                    Is.EqualTo((uint)(PermissionType.Browse | PermissionType.Read | PermissionType.Write)));
            });
        }

        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public async Task InvalidDurationsDoNotReachProviderAsync(double duration)
        {
            (AutoIdReader reader, ParameterProvider provider) = await CreateAsync().ConfigureAwait(false);
            ServiceResult result = await WriteAsync(Parameter(reader, "Duration"), Variant.From(duration));
            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(provider.Writes, Is.Zero);
                Assert.That(Parameter(reader, "Duration").WrappedValue, Is.EqualTo(Variant.From(0d)));
            });
        }

        [Test]
        public async Task InvalidCodeTypeAndIndexesAreRejectedWithoutChangingConfigurationAsync()
        {
            (AutoIdReader reader, ParameterProvider provider) = await CreateAsync().ConfigureAwait(false);
            ServiceResult format = await WriteAsync(Parameter(reader, "CodeType"), Variant.From("unsupported"));
            ServiceResult index = await WriteAsync(Codes(reader), Variant.From(new uint[] { 1 }.ToArrayOf()));
            ServiceResult duplicate = await WriteAsync(Codes(reader), Variant.From(new uint[] { 0, 0 }.ToArrayOf()));
            ServiceResult empty = await WriteAsync(Codes(reader), Variant.From(default(ArrayOf<uint>)));
            Assert.Multiple(() =>
            {
                Assert.That(format.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(index.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(duplicate.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(empty.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(provider.Writes, Is.Zero);
                Assert.That(provider.Parameters.CodeType, Is.EqualTo("EPC"));
            });
        }

        [Test]
        public async Task ProviderCommitFailurePreservesPublishedAndProviderValuesAsync()
        {
            (AutoIdReader reader, ParameterProvider provider) = await CreateAsync().ConfigureAwait(false);
            provider.RejectWrites = true;
            ServiceResult result = await WriteAsync(Parameter(reader, "Cycles"), Variant.From(4));
            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadDeviceFailure));
                Assert.That(provider.Parameters.Cycles, Is.Zero);
                Assert.That(Parameter(reader, "Cycles").WrappedValue, Is.EqualTo(Variant.From(0)));
            });
        }

        [Test]
        public async Task ParameterCommitSerializesStartAndCancelledCommitLeavesPreviousValuesAsync()
        {
            (AutoIdReader reader, ParameterProvider provider) = await CreateAsync().ConfigureAwait(false);
            provider.CommitRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<ServiceResult> commit = WriteAsync(Parameter(reader, "Cycles"), Variant.From(7)).AsTask();
            await provider.CommitEntered.Task.ConfigureAwait(false);
            Task<ServiceResult> start = WriteAsync(reader.Device.ScanActive!, Variant.From(true)).AsTask();
            Assert.That(start.IsCompleted, Is.False);
            Assert.That(provider.Starts, Is.Zero);
            provider.CommitRelease.SetResult(true);
            Assert.That(ServiceResult.IsGood(await commit), Is.True);
            Assert.That(ServiceResult.IsGood(await start), Is.True);
            Assert.That(provider.StartSettings!.Cycles, Is.EqualTo(7));
            await WriteAsync(reader.Device.ScanActive!, Variant.From(false));

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThatAsync(async () => await Parameter(reader, "Cycles").OnWriteValueAsync!(
                m_fixture.Context, Parameter(reader, "Cycles"), default, Variant.From(9), cancelled.Token),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(provider.Parameters.Cycles, Is.EqualTo(7));
            Assert.That(Parameter(reader, "Cycles").WrappedValue, Is.EqualTo(Variant.From(7)));
        }

        [Test]
        public async Task WritesAfterShutdownAreRejectedWithoutTouchingDisposedResourcesAsync()
        {
            (AutoIdReader reader, ParameterProvider provider) = await CreateAsync().ConfigureAwait(false);
            await reader.DisposeAsync().ConfigureAwait(false);
            ServiceResult parameter = await WriteAsync(Parameter(reader, "Cycles"), Variant.From(3));
            ServiceResult control = await WriteAsync(reader.Device.ScanActive!, Variant.From(true));
            Assert.Multiple(() =>
            {
                Assert.That(parameter.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(control.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(provider.Writes, Is.Zero);
                Assert.That(provider.Starts, Is.Zero);
            });
        }

        [Test]
        public async Task RepeatedWritesDoNotRestartAndVariableScanSurvivesSessionClosureAsync()
        {
            (AutoIdReader reader, ParameterProvider provider) = await CreateAsync().ConfigureAwait(false);
            var context = new TestSessionContext(m_fixture.Context, new NodeId(42));
            await WriteAsync(reader.Device.ScanActive!, Variant.From(true), context);
            await WriteAsync(reader.Device.ScanActive!, Variant.From(true), context);
            await reader.OnSessionClosingAsync(new NodeId(42), default);
            Assert.Multiple(() =>
            {
                Assert.That(provider.Starts, Is.EqualTo(1));
                Assert.That(provider.Stops, Is.Zero);
                Assert.That(reader.IsAsyncScanActive, Is.True);
            });
            await WriteAsync(reader.Device.ScanActive!, Variant.From(false));
            await WriteAsync(reader.Device.ScanActive!, Variant.From(false));
            Assert.Multiple(() =>
            {
                Assert.That(provider.Stops, Is.EqualTo(1));
                Assert.That(reader.Device.ScanActive!.Value, Is.False);
            });
        }

        [Test]
        public async Task MethodAndVariableControlsShareActivityButPreserveMethodOwnershipAsync()
        {
            (AutoIdReader reader, ParameterProvider provider) = await CreateAsync().ConfigureAwait(false);
            var owner = new TestSessionContext(m_fixture.Context, new NodeId(91));
            ScanStartMethodStateResult started = await reader.Device.ScanStart!.OnCallAsync!(
                owner, reader.Device.ScanStart, reader.NodeId, new ScanSettings(), default);
            Assert.That(ServiceResult.IsGood(started.ServiceResult), Is.True);
            await WriteAsync(reader.Device.ScanActive!, Variant.From(true));
            await reader.OnSessionClosingAsync(new NodeId(91), default);
            Assert.Multiple(() =>
            {
                Assert.That(provider.Starts, Is.EqualTo(1));
                Assert.That(provider.Stops, Is.EqualTo(1));
                Assert.That(reader.Device.ScanActive!.Value, Is.False);
            });
            await WriteAsync(reader.Device.ScanActive!, Variant.From(true));
            ScanStartMethodStateResult competing = await reader.Device.ScanStart.OnCallAsync(
                owner, reader.Device.ScanStart, reader.NodeId, new ScanSettings(), default);
            Assert.That(competing.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            await reader.Device.ScanStop!.OnCallMethod2Async!(
                owner, reader.Device.ScanStop, reader.NodeId, default, [], default);
            Assert.That(reader.Device.ScanActive!.Value, Is.False);
        }

        [Test]
        public async Task RejectedStartAndImmediateCompletionNeverLeaveScanActiveTrueAsync()
        {
            (AutoIdReader reader, ParameterProvider provider) = await CreateAsync().ConfigureAwait(false);
            provider.StartStatus = AutoIdOperationStatusEnumeration.DEVICE_NOT_READY;
            ServiceResult rejected = await WriteAsync(reader.Device.ScanActive!, Variant.From(true));
            Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadDeviceFailure));
            Assert.That(reader.Device.ScanActive!.Value, Is.False);
            provider.StartStatus = AutoIdOperationStatusEnumeration.SUCCESS;
            provider.CompleteDuringStart = true;
            ServiceResult completed = await WriteAsync(reader.Device.ScanActive, Variant.From(true));
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(completed), Is.True);
                Assert.That(reader.Device.ScanActive.Value, Is.False);
                Assert.That(reader.Status, Is.EqualTo(DeviceStatusEnumeration.Idle));
            });
        }

        [Test]
        public async Task NaturalCompletionAndShutdownResetScanActiveAsync()
        {
            (AutoIdReader reader, ParameterProvider provider) = await CreateAsync().ConfigureAwait(false);
            await WriteAsync(reader.Device.ScanActive!, Variant.From(true));
            await provider.Sink!.CompleteScanAsync();
            Assert.That(reader.Device.ScanActive!.Value, Is.False);
            await WriteAsync(reader.Device.ScanActive, Variant.From(true));
            await reader.DisposeAsync();
            Assert.Multiple(() =>
            {
                Assert.That(reader.Device.ScanActive.Value, Is.False);
                Assert.That(provider.Stops, Is.EqualTo(1));
                Assert.That(provider.Detached, Is.True);
            });
        }

        [Test]
        public async Task RejectedStopKeepsTheScanTrackedUntilProviderConfirmationAsync()
        {
            (AutoIdReader reader, ParameterProvider provider) = await CreateAsync().ConfigureAwait(false);
            await WriteAsync(reader.Device.ScanActive!, Variant.From(true));
            provider.RejectStops = true;
            await Assert.ThatAsync(
                async () => await WriteAsync(reader.Device.ScanActive!, Variant.From(false)),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadDeviceFailure));
            Assert.That(reader.Device.ScanActive!.Value, Is.True);
            Assert.That(reader.IsAsyncScanActive, Is.True);
            provider.RejectStops = false;
            await WriteAsync(reader.Device.ScanActive, Variant.From(false));
            Assert.That(reader.Device.ScanActive.Value, Is.False);
        }

        [Test]
        public async Task AccessDenialWrongTypeAndIndexRangeDoNotChangeSettingsAsync()
        {
            (AutoIdReader reader, ParameterProvider provider) = await CreateAsync().ConfigureAwait(false);
            BaseVariableState cycles = Parameter(reader, "Cycles");
            ServiceResult wrongType = await WriteAsync(cycles, Variant.From("wrong"));
            AttributeWriteResult range = await cycles.OnWriteValueAsync!(
                m_fixture.Context, cycles, new NumericRange(0), Variant.From(1), default);
            cycles.UserAccessLevel = AccessLevels.CurrentRead;
            ServiceResult denied = await cycles.WriteAttributeAsync(
                m_fixture.Context, Attributes.Value, default, new DataValue(Variant.From(2)), default);
            Assert.Multiple(() =>
            {
                Assert.That(wrongType.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
                Assert.That(range.Result.StatusCode, Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(provider.Writes, Is.Zero);
            });
        }

        private async Task<(AutoIdReader, ParameterProvider)> CreateAsync()
        {
            var provider = new ParameterProvider();
            AutoIdReader reader = await m_fixture.Runtime.Reader(Name(), provider)
                .WithRuntimeParameters(enableScanControl: true).BuildAsync().ConfigureAwait(false);
            return (reader, provider);
        }

        private QualifiedName Name()
        {
            return m_fixture.Name("Parameters" + Interlocked.Increment(ref m_sequence));
        }

        private BaseVariableState Parameter(AutoIdReader reader, string name)
        {
            ushort ns = (ushort)m_fixture.Context.NamespaceUris.GetIndex(AutoIdModel.NamespaceUri);
            return (BaseVariableState)reader.Device.RuntimeParameters!.FindChild(
                m_fixture.Context, new QualifiedName("ScanSettings", ns))!.FindChild(
                    m_fixture.Context, new QualifiedName(name, ns))!;
        }

        private BaseVariableState Codes(AutoIdReader reader)
        {
            return (BaseVariableState)reader.Device.RuntimeParameters!.FindChild(m_fixture.Context,
                new QualifiedName("CodeTypes",
                    (ushort)m_fixture.Context.NamespaceUris.GetIndex(AutoIdModel.NamespaceUri)))!;
        }

        private async ValueTask<ServiceResult> WriteAsync(
            BaseVariableState variable, Variant value, ISystemContext? context = null)
        {
            AttributeWriteResult result = await variable.OnWriteValueAsync!(
                context ?? m_fixture.Context, variable, default, value, default).ConfigureAwait(false);
            if (ServiceResult.IsGood(result.Result))
            {
                Assert.That(result.SuppressCacheUpdate, Is.True);
            }
            return result.Result;
        }

        private AutoIdManagerFixture m_fixture = null!;
        private int m_sequence;

        /// <summary>
        /// Scripted provider for deterministic parameter and completion tests.
        /// </summary>
        private sealed class ParameterProvider : IAutoIdAsyncScanProvider, IAutoIdScanParametersProvider
        {
            public AutoIdReaderKind Kind => AutoIdReaderKind.Rfid;

            public ArrayOf<string> SupportedCodeTypes => ["EPC"];

            public AutoIdScanParameters Parameters { get; private set; } = new() { CodeType = "EPC", CodeTypes = [0] };

            public IAutoIdReaderSink? Sink { get; private set; }

            public ScanSettings? StartSettings { get; private set; }

            public bool RejectWrites { get; set; }

            public bool RejectStops { get; set; }

            public TaskCompletionSource<bool> CommitEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool>? CommitRelease { get; set; }

            public bool CompleteDuringStart { get; set; }

            public bool Detached { get; private set; }

            public int Writes { get; private set; }

            public int Starts { get; private set; }

            public int Stops { get; private set; }

            public AutoIdOperationStatusEnumeration StartStatus { get; set; } =
                AutoIdOperationStatusEnumeration.SUCCESS;

            public ValueTask AttachAsync(IAutoIdReaderSink sink, CancellationToken cancellationToken = default)
            {
                Sink = sink;
                return default;
            }

            public ValueTask DetachAsync(CancellationToken cancellationToken = default)
            {
                Detached = true;
                return default;
            }

            public ValueTask<AutoIdScanParameters> ReadParametersAsync(CancellationToken cancellationToken = default)
            {
                return new ValueTask<AutoIdScanParameters>(Parameters);
            }

            public async ValueTask WriteParametersAsync(
                AutoIdScanParameters parameters, CancellationToken cancellationToken = default)
            {
                if (RejectWrites)
                {
                    throw new ServiceResultException(StatusCodes.BadDeviceFailure);
                }
                CommitEntered.TrySetResult(true);
                if (CommitRelease != null)
                {
                    await CommitRelease.Task.ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                Writes++;
                Parameters = parameters;
            }

            public async ValueTask<AutoIdOperationStatusEnumeration> StartScanAsync(
                ScanSettings settings, CancellationToken cancellationToken = default)
            {
                Starts++;
                StartSettings = settings;
                if (CompleteDuringStart)
                {
                    await Sink!.CompleteScanAsync(cancellationToken).ConfigureAwait(false);
                }
                return StartStatus;
            }

            public ValueTask StopScanAsync(CancellationToken cancellationToken = default)
            {
                if (RejectStops)
                {
                    throw new ServiceResultException(StatusCodes.BadDeviceFailure);
                }
                Stops++;
                return default;
            }
        }
    }
}
