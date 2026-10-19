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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.CranesHoists.Server;

namespace Opc.Ua.CranesHoists.Tests
{
    /// <summary>
    /// Tests provider behavior, native topology and exact write semantics through the public crane runtime.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class CraneRuntimeTests
    {
        /// <summary>
        /// Starts one real owner; each test builds an independent system and provider.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_fixture = new CraneServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Removes each test's graph before the next test inspects capabilities.
        /// </summary>
        [TearDown]
        public async Task RemoveAsync()
        {
            foreach (CraneSystemHandle handle in m_handles)
            {
                await handle.RemoveAsync().ConfigureAwait(false);
            }
            m_handles.Clear();
        }

        /// <summary>
        /// Stops the owning server after every graph has been released.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Default topology retains true generated inheritance while optional facets remain absent.
        /// </summary>
        [Test]
        public async Task MandatoryTopologyPreservesNativeTypesAndUnavailablePosition()
        {
            string name = NextName();
            await using var provider = new SimulatedCraneProvider(CraneRuntimeTestData.Samples(name));
            CraneSystemHandle crane = await BuildAsync(name, provider).ConfigureAwait(false);
            Assert.That(crane.State.TypeDefinitionId, Is.EqualTo(TypeId(ObjectTypes.CraneMotionDeviceSystemType)));
            Assert.That(crane.GetElement("Hoist"), Is.TypeOf<CraneMotionDeviceState>());
            Assert.That(crane.GetElement("Lift"), Is.TypeOf<CraneAxisState>());
            Assert.That(crane.GetElement("LiftMotor"), Is.TypeOf<CraneMotorState>());
            Assert.That(crane.State.ProtectiveFunctions, Is.Null);
            Assert.That(crane.State.Components, Is.Null);
            Assert.That(crane.State.Configuration, Is.Null);
            Assert.That(crane.State.Operational, Is.Null);
            Assert.That(crane.EventNotifierId.IsNull, Is.True);
            Assert.That(((CraneMotionDeviceState)crane.GetElement("Hoist")).ParameterSet!.ReferenceExists(
                Opc.Ua.ReferenceTypeIds.HasComponent, false,
                NodeId.Create(Variables.CraneMotionDeviceType_Operational_DesignedWorkingPeriod,
                    CranesHoistsModel.NamespaceUri, m_fixture.Manager.SystemContext.NamespaceUris)), Is.False);
            Assert.That(crane.ReadSignal(new(name, CraneSignal.SystemOn)).WrappedValue.GetBoolean(), Is.True);
            Assert.That(crane.ReadSignal(new("Hoist", CraneSignal.SpeedOverride)).WrappedValue.GetDouble(),
                Is.EqualTo(83.25));
            Assert.That(crane.ReadSignal(new("Lift", CraneSignal.ActualPositionCapability)).WrappedValue.GetBoolean(),
                Is.False);
            DataValue unavailable = crane.ReadSignal(new("Lift", CraneSignal.ActualPosition));
            Assert.That(unavailable.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
            Assert.That(unavailable.WrappedValue.IsNull, Is.True);
            Assert.That(crane.TelemetrySignals, Has.Count.EqualTo(7));
            Assert.That(crane.ConformanceUnits.ToArray()!.Select(value => value.Name),
                Does.Contain("Cranes Base Info CraneMotorType").And.Not.Contain("Cranes Base Info BrakeType"));
            Assert.That(crane.State.Identification!.SerialNumber!.Value, Is.EqualTo(name));
            Assert.That(((CraneMotionDeviceState)crane.GetElement("Hoist")).Manufacturer!.Value.Text,
                Is.EqualTo("Simulated manufacturer"));
            Assert.That(m_fixture.ReservedCount, Is.Zero, m_fixture.ReservedNames);
        }

        /// <summary>
        /// Every released optional crane facet has actual data, native metadata and semantic references.
        /// </summary>
        [Test]
        public async Task SelectedFacetsImplementAllElevenUnitsWithActualValuesAndReferences()
        {
            string name = NextName();
            await using var provider = new SimulatedCraneProvider(CraneRuntimeTestData.Samples(name, true), true);
            CraneSystemHandle crane = await BuildAsync(name, provider, true).ConfigureAwait(false);
            Assert.That(crane.ConformanceUnits, Has.Count.EqualTo(11));
            Assert.That(crane.ServerProfiles, Has.Count.EqualTo(1));
            Assert.That(crane.ServerProfiles[0], Is.EqualTo(
                "http://opcfoundation.org/UA-Profile/CranesHoists/Server/Basic"));
            Assert.That(crane.ReadSignal(new("Hoist", CraneSignal.DesignedWorkingPeriod)).WrappedValue.GetDouble(),
                Is.EqualTo(92.5));
            Assert.That(crane.ReadSignal(new("Lift", CraneSignal.LoadMass)).WrappedValue.GetDouble(), Is.EqualTo(153));
            Assert.That(crane.ReadSignal(new("Lift", CraneSignal.RatedSpeed)).WrappedValue.GetDouble(),
                Is.EqualTo(2.5));
            Assert.That(crane.ReadSignal(new("LiftMotor", CraneSignal.MotorTemperature)).WrappedValue.GetDouble(),
                Is.EqualTo(35.5));
            Assert.That(crane.ReadSignal(new("LiftMotor", CraneSignal.BrakeReleased)).WrappedValue.GetBoolean(),
                Is.True);
            Assert.That(crane.ReadSignal(new("BackupBrake", CraneSignal.BrakeReleased)).WrappedValue.GetBoolean(),
                Is.False);
            Assert.That(crane.ReadSignal(new("Overload", CraneSignal.DirMinusSlowdown)).WrappedValue.GetBoolean(),
                Is.True);
            Assert.That(crane.ReadSignal(new("Overload", CraneSignal.DirPlusSlowdown)).WrappedValue.GetBoolean(),
                Is.False);
            Assert.That(crane.ReadSignal(new("Overload", CraneSignal.DirPlusStop)).WrappedValue.GetBoolean(), Is.True);
            Assert.That(crane.ReadSignal(new("Overload", CraneSignal.RampDown)).WrappedValue.GetBoolean(), Is.True);
            var functions = (MultiStateValueDiscreteState<ArrayOf<ushort>>)crane.GetSignal(
                new("Lift", CraneSignal.AxisFunction));
            Assert.That(functions.EnumValues!.Value, Has.Count.EqualTo(9));
            Assert.That(functions.EnumValues.Value.ToArray()!.Select(value => value.Value),
                Is.EqualTo(Enumerable.Range(0, 9)));
            Assert.That(functions.ValueAsText!.WrappedValue.TryGetValue(out ArrayOf<LocalizedText> text), Is.True);
            Assert.That(text, Has.Count.EqualTo(3));
            Assert.That(text[0].Text, Is.EqualTo("RATED_SPEED"));
            Assert.That(text[1].Text, Is.EqualTo("MICROSPEED"));
            Assert.That(text[2].Text, Is.EqualTo("ANTISWAY"));
            Assert.That(crane.GetElement("Overload").ReferenceExists(
                NodeId.Create(ReferenceTypes.ProtectsAxis, CranesHoistsModel.NamespaceUri,
                    m_fixture.Manager.SystemContext.NamespaceUris),
                false, crane.GetElement("Lift").NodeId), Is.True);
            Assert.That(((CraneAxisState)crane.GetElement("Lift")).Operational!.ReferenceExists(
                Opc.Ua.ReferenceTypeIds.Organizes, false, functions.NodeId), Is.True);
            Assert.That(crane.State.Operational!.ReferenceExists(Opc.Ua.ReferenceTypeIds.Organizes,
                false, crane.GetSignal(new(name, CraneSignal.SystemOn)).NodeId), Is.True);
            Assert.That(crane.EventNotifierId.IsNull, Is.False);
            var pending = new Stack<NodeState>();
            pending.Push(crane.State);
            while (pending.Count > 0)
            {
                NodeState node = pending.Pop();
                Assert.That(node, Is.Not.InstanceOf<MethodState>(), "No crane actuation method may be invented.");
                var children = new List<BaseInstanceState>();
                node.GetChildren(m_fixture.Manager.SystemContext, children);
                foreach (BaseInstanceState child in children)
                {
                    pending.Push(child);
                }
            }
        }

        /// <summary>
        /// Accepted client requests retain their own state while active limits remain provider-owned.
        /// </summary>
        [TestCase(0.0)]
        [TestCase(100.0)]
        [TestCase(43.25)]
        public async Task AcceptedLimitRequestsDoNotInventActiveHardwareChanges(double limit)
        {
            string name = NextName();
            await using var provider = new SimulatedCraneProvider(CraneRuntimeTestData.Samples(name, true), true);
            Mock<ICraneWriteAuthorizer> authorizer = CraneRuntimeTestData.AllowWrites();
            CraneSystemHandle crane = await BuildAsync(name, provider, true, authorizer: authorizer.Object)
                .ConfigureAwait(false);
            ServiceResult status = await WriteAsync(crane, "Lift", CraneSignal.SpeedLimitDirPlusSetpoint,
                Variant.From(limit)).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(crane.ReadSignal(new("Lift", CraneSignal.SpeedLimitDirPlusSetpoint)).WrappedValue.GetDouble(),
                Is.EqualTo(limit));
            Assert.That(crane.ReadSignal(new("Lift", CraneSignal.SpeedLimitDirPlus)).WrappedValue.GetDouble(),
                Is.EqualTo(31.5));
            Assert.That(provider.AcceptedRequests, Has.Count.EqualTo(1));
            Assert.That(provider.AcceptedRequests[0].Address, Is.EqualTo(new CraneSignalAddress(
                "Lift", CraneSignal.SpeedLimitDirPlusSetpoint)));
            authorizer.Verify(value => value.AuthorizeAsync(m_fixture.Manager.SystemContext,
                It.Is<CraneLimitRequest>(request => request.SystemId == name),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Each standard Boolean request is accepted explicitly without rewriting reported active limits.
        /// </summary>
        [TestCase(CraneSignal.SpeedLimitGlobalEnabled, true)]
        [TestCase(CraneSignal.SpeedLimitGlobalEnabled, false)]
        [TestCase(CraneSignal.SpeedLimitEnabledDirMinus, true)]
        [TestCase(CraneSignal.SpeedLimitEnabledDirPlus, false)]
        public async Task BooleanLimitRequestsRemainSeparateFromReportedLimits(CraneSignal signal, bool requested)
        {
            string name = NextName();
            await using var provider = new SimulatedCraneProvider(CraneRuntimeTestData.Samples(name, true), true);
            CraneSystemHandle crane = await BuildAsync(name, provider, true).ConfigureAwait(false);
            string component = signal == CraneSignal.SpeedLimitGlobalEnabled ? name : "Lift";
            ServiceResult result = await WriteAsync(crane, component, signal, Variant.From(requested))
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(crane.ReadSignal(new(component, signal)).WrappedValue.GetBoolean(), Is.EqualTo(requested));
            Assert.That(crane.ReadSignal(new("Lift", CraneSignal.SpeedLimitDirMinus)).WrappedValue.GetDouble(),
                Is.EqualTo(27.5));
            Assert.That(crane.ReadSignal(new("Lift", CraneSignal.SpeedLimitDirPlus)).WrappedValue.GetDouble(),
                Is.EqualTo(31.5));
            Assert.That(provider.AcceptedRequests, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Immediately adjacent boundaries and non-finite values cannot reach the equipment provider.
        /// </summary>
        [TestCase(-double.Epsilon)]
        [TestCase(100.00000000000001)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public async Task InvalidLimitRequestLeavesRequestAndActiveStateUntouched(double limit)
        {
            string name = NextName();
            await using var provider = new SimulatedCraneProvider(CraneRuntimeTestData.Samples(name, true), true);
            CraneSystemHandle crane = await BuildAsync(name, provider, true).ConfigureAwait(false);
            DataValue before = crane.ReadSignal(new("Lift", CraneSignal.SpeedLimitDirMinusSetpoint));
            ServiceResult result = await WriteAsync(crane, "Lift", CraneSignal.SpeedLimitDirMinusSetpoint,
                Variant.From(limit)).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(crane.ReadSignal(new("Lift", CraneSignal.SpeedLimitDirMinusSetpoint)), Is.EqualTo(before));
            Assert.That(crane.ReadSignal(new("Lift", CraneSignal.SpeedLimitDirMinus)).WrappedValue.GetDouble(),
                Is.EqualTo(27.5));
            Assert.That(provider.AcceptedRequests, Is.Empty);
        }

        /// <summary>
        /// Unauthorized and refused writes never look successful and never mutate either state.
        /// </summary>
        [TestCase(false, true)]
        [TestCase(true, false)]
        public async Task AuthorizationAndProviderAcceptanceAreBothRequired(bool authorized, bool accepted)
        {
            string name = NextName();
            await using var provider = new SimulatedCraneProvider(CraneRuntimeTestData.Samples(name, true), accepted);
            var authorizer = new Mock<ICraneWriteAuthorizer>(MockBehavior.Strict);
            authorizer.Setup(value => value.AuthorizeAsync(
                It.IsAny<ISystemContext>(), It.IsAny<CraneLimitRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(authorized);
            CraneSystemHandle crane = await BuildAsync(name, provider, true, authorizer: authorizer.Object)
                .ConfigureAwait(false);
            ServiceResult result = await WriteAsync(
                crane, name, CraneSignal.SpeedLimitGlobalEnabled, Variant.From(true))
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(authorized
                ? StatusCodes.BadNotSupported : StatusCodes.BadUserAccessDenied));
            Assert.That(crane.ReadSignal(new(name, CraneSignal.SpeedLimitGlobalEnabled)).StatusCode,
                Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            Assert.That(provider.AcceptedRequests, Is.Empty);
        }

        /// <summary>
        /// A bad protective-function combination rejects the entire multi-field publication.
        /// </summary>
        [Test]
        public async Task DisabledProtectionCannotBecomeActiveOrPartiallyPublishAnotherSignal()
        {
            string name = NextName();
            ArrayOf<CraneSample> samples = CraneRuntimeTestData.Samples(name, true);
            await using var provider = new SimulatedCraneProvider(samples, true);
            CraneSystemHandle crane = await BuildAsync(name, provider, true).ConfigureAwait(false);
            ArrayOf<CraneSample> invalid = CraneRuntimeTestData.Replace(samples,
                CraneRuntimeTestData.Sample("Overload", CraneSignal.Enabled, Variant.From(false)));
            invalid = CraneRuntimeTestData.Replace(invalid,
                CraneRuntimeTestData.Sample("Overload", CraneSignal.Active, Variant.From(true)));
            invalid = CraneRuntimeTestData.Replace(invalid,
                CraneRuntimeTestData.Sample(name, CraneSignal.SystemOn, Variant.From(false)));
            await provider.SetSamplesAsync(invalid).ConfigureAwait(false);
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await crane.RefreshAsync().ConfigureAwait(false)))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(crane.ReadSignal(new(name, CraneSignal.SystemOn)).WrappedValue.GetBoolean(), Is.True);
            Assert.That(crane.ReadSignal(new("Overload", CraneSignal.Enabled)).WrappedValue.GetBoolean(), Is.True);
            Assert.That(crane.ReadSignal(new("Overload", CraneSignal.Active)).WrappedValue.GetBoolean(), Is.False);
        }

        /// <summary>
        /// Provider-reported active protection transitions are published
        /// without changing configured direction capability.
        /// </summary>
        [Test]
        public async Task EnabledProtectionCanReportAnActiveStopOrSlowdown()
        {
            string name = NextName();
            ArrayOf<CraneSample> samples = CraneRuntimeTestData.Samples(name, true);
            await using var provider = new SimulatedCraneProvider(samples, true);
            CraneSystemHandle crane = await BuildAsync(name, provider, true).ConfigureAwait(false);
            await provider.SetSamplesAsync(CraneRuntimeTestData.Replace(samples,
                CraneRuntimeTestData.Sample("Overload", CraneSignal.Active, Variant.From(true)))).ConfigureAwait(false);
            await crane.RefreshAsync().ConfigureAwait(false);
            Assert.That(crane.ReadSignal(new("Overload", CraneSignal.Active)).WrappedValue.GetBoolean(), Is.True);
            Assert.That(crane.ReadSignal(new("Overload", CraneSignal.Enabled)).WrappedValue.GetBoolean(), Is.True);
            Assert.That(crane.ReadSignal(new("Overload", CraneSignal.DirMinusSlowdown)).WrappedValue.GetBoolean(),
                Is.True);
            Assert.That(crane.ReadSignal(new("Overload", CraneSignal.DirPlusStop)).WrappedValue.GetBoolean(), Is.True);
        }

        /// <summary>
        /// Bad samples retain their actual status/source time instead of becoming absent or fresh Good values.
        /// </summary>
        [Test]
        public async Task ReportedQualityAndAcquisitionTimeSurviveRefresh()
        {
            string name = NextName();
            ArrayOf<CraneSample> samples = CraneRuntimeTestData.Samples(name);
            await using var provider = new SimulatedCraneProvider(samples);
            CraneSystemHandle crane = await BuildAsync(name, provider).ConfigureAwait(false);
            ArrayOf<CraneSample> updated = CraneRuntimeTestData.Replace(samples,
                CraneRuntimeTestData.Sample("LiftMotor", CraneSignal.MotorTemperature,
                    Variant.Null, StatusCodes.BadSensorFailure));
            await provider.SetSamplesAsync(updated).ConfigureAwait(false);
            await crane.RefreshAsync().ConfigureAwait(false);
            DataValue value = crane.ReadSignal(new("LiftMotor", CraneSignal.MotorTemperature));
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.BadSensorFailure));
            Assert.That(value.SourceTimestamp, Is.EqualTo(CraneRuntimeTestData.SourceTime));
            Assert.That(value.WrappedValue.IsNull, Is.True);
        }

        /// <summary>
        /// Discrete-value text cannot remain a stale Good value after the provider reports unavailable functions.
        /// </summary>
        [Test]
        public async Task UnavailableAxisFunctionsAlsoInvalidateTheirTextProjection()
        {
            string name = NextName();
            ArrayOf<CraneSample> samples = CraneRuntimeTestData.Samples(name, true);
            await using var provider = new SimulatedCraneProvider(samples, true);
            CraneSystemHandle crane = await BuildAsync(name, provider, true).ConfigureAwait(false);
            await provider.SetSamplesAsync(CraneRuntimeTestData.Replace(samples,
                CraneRuntimeTestData.Sample("Lift", CraneSignal.AxisFunction, Variant.Null, StatusCodes.BadNoData)))
                .ConfigureAwait(false);
            await crane.RefreshAsync().ConfigureAwait(false);
            var functions = (MultiStateValueDiscreteState<ArrayOf<ushort>>)crane.GetSignal(
                new("Lift", CraneSignal.AxisFunction));
            Assert.That(functions.ValueAsText!.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
            Assert.That(functions.ValueAsText.WrappedValue.IsNull, Is.True);
            Assert.That(functions.ValueAsText.Timestamp, Is.EqualTo(CraneRuntimeTestData.SourceTime));
        }

        /// <summary>
        /// Missing selected data fails startup and allows another system with the same name.
        /// </summary>
        [Test]
        public async Task MissingProviderDataRollsBackBeforePublicationAndAllowsRetry()
        {
            string name = NextName();
            await using var provider = new SimulatedCraneProvider([]);
            CranesHoistsBuildContext context = Context();
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await CraneRuntimeTestData.Define(context, name, provider).BuildAsync().ConfigureAwait(false)))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
            Assert.That(context.ConformanceUnits, Is.Empty);
            Assert.That(m_fixture.ReservedCount, Is.Zero, m_fixture.ReservedNames);
            await provider.SetSamplesAsync(CraneRuntimeTestData.Samples(name)).ConfigureAwait(false);
            CraneSystemHandle retry = await CraneRuntimeTestData.Define(context, name, provider).BuildAsync()
                .ConfigureAwait(false);
            m_handles.Add(retry);
            Assert.That(retry.State.Identification!.SerialNumber!.Value, Is.EqualTo(name));
        }

        /// <summary>
        /// Distinct systems can reuse component names without sharing values or accepted requests.
        /// </summary>
        [Test]
        public async Task MultipleSystemsKeepProviderAndRequestStateIsolated()
        {
            string firstName = NextName();
            string secondName = NextName();
            await using var firstProvider = new SimulatedCraneProvider(
                CraneRuntimeTestData.Samples(firstName, true), true);
            ArrayOf<CraneSample> secondSamples = CraneRuntimeTestData.Replace(
                CraneRuntimeTestData.Samples(secondName, true),
                CraneRuntimeTestData.Sample("Lift", CraneSignal.SpeedLimitDirPlus, Variant.From(64.0)));
            await using var secondProvider = new SimulatedCraneProvider(secondSamples, true);
            CraneSystemHandle first = await BuildAsync(firstName, firstProvider, true).ConfigureAwait(false);
            CraneSystemHandle second = await BuildAsync(secondName, secondProvider, true).ConfigureAwait(false);
            await WriteAsync(first, "Lift", CraneSignal.SpeedLimitDirPlusSetpoint, Variant.From(12.0))
                .ConfigureAwait(false);
            Assert.That(firstProvider.AcceptedRequests, Has.Count.EqualTo(1));
            Assert.That(secondProvider.AcceptedRequests, Is.Empty);
            Assert.That(first.ReadSignal(new("Lift", CraneSignal.SpeedLimitDirPlus)).WrappedValue.GetDouble(),
                Is.EqualTo(31.5));
            Assert.That(second.ReadSignal(new("Lift", CraneSignal.SpeedLimitDirPlus)).WrappedValue.GetDouble(),
                Is.EqualTo(64));
            Assert.That(first.GetElement("Lift").NodeId, Is.Not.EqualTo(second.GetElement("Lift").NodeId));
        }

        /// <summary>
        /// Creates another context on the existing manager, never another namespace owner.
        /// </summary>
        private CranesHoistsBuildContext Context()
        {
            return new CranesHoistsBuildContext(m_fixture.RoboticsContext(), m_fixture.MachineryContext());
        }

        /// <summary>
        /// Builds and tracks a system for deterministic removal.
        /// </summary>
        private async Task<CraneSystemHandle> BuildAsync(
            string name,
            ICraneDataProvider provider,
            bool full = false,
            ICraneLimitProvider? limits = null,
            ICraneWriteAuthorizer? authorizer = null)
        {
            CraneSystemHandle handle = await CraneRuntimeTestData.Define(
                Context(), name, provider, full, limits, authorizer).BuildAsync().ConfigureAwait(false);
            m_handles.Add(handle);
            return handle;
        }

        /// <summary>
        /// Exercises the real node write hook and the framework's type/access validation.
        /// </summary>
        private ValueTask<ServiceResult> WriteAsync(
            CraneSystemHandle crane,
            string component,
            CraneSignal signal,
            Variant value)
        {
            return crane.GetSignal(new(component, signal)).WriteAttributeAsync(
                m_fixture.Manager.SystemContext, Attributes.Value, NumericRange.Null, new DataValue(value));
        }

        /// <summary>
        /// Resolves a standard type without assuming a namespace index.
        /// </summary>
        private NodeId TypeId(uint identifier)
        {
            return NodeId.Create(
                identifier, CranesHoistsModel.NamespaceUri, m_fixture.Manager.SystemContext.NamespaceUris);
        }

        /// <summary>
        /// Creates deterministic unique names within the shared fixture.
        /// </summary>
        private string NextName()
        {
            return "RuntimeCrane" + (++m_counter).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private CraneServerFixture m_fixture = null!;
        private readonly List<CraneSystemHandle> m_handles = [];
        private int m_counter;
    }
}
