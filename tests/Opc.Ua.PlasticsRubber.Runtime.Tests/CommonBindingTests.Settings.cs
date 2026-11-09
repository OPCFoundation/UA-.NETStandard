/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PlasticsRubber.Server;

namespace Opc.Ua.PlasticsRubber.Runtime.Tests
{
    public sealed partial class CommonBindingTests
    {
        /// <summary>
        /// Real UA writes retain the old native value and quality until the typed authority commits.
        /// </summary>
        [TestCaseSource(nameof(MonitoredCommitCases))]
        public async Task MonitoredSettingsCommitThroughUaWritesBeforePublicationAsync(
            string setting, double number, bool reject)
        {
            MonitoredParameterState node = CreateSettingsNode(true);
            BaseVariableState member = SettingMember(node, setting);
            Variant expected = setting is "Sensitivity" or "Suppression"
                ? Variant.From((ushort)number) : Variant.From(number);
            if (setting is "Sensitivity" or "Suppression")
            {
                member.Value = Variant.From(number == 1 ? (ushort)0 : (ushort)1);
            }
            member.StatusCode = StatusCodes.UncertainDataSubNormal;
            Variant old = member.Value;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var commit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            async ValueTask CommitAsync(PlasticsRubberRequestContext request, CancellationToken token)
            {
                Assert.That(request.SystemContext, Is.SameAs(m_context));
                Assert.That(request.TargetId, Is.EqualTo(node.NodeId));
                Assert.That(token.CanBeCanceled, Is.True);
                entered.TrySetResult(true);
                await commit.Task.ConfigureAwait(false);
                if (reject)
                {
                    throw ServiceResultException.Create(StatusCodes.BadResourceUnavailable,
                        "Setting was not committed.");
                }
            }
            var provider = new Mock<IMonitoredParameterProvider>(MockBehavior.Strict);
            if (setting == "Sensitivity")
            {
                provider.Setup(value => value.SetSensitivityAsync(
                    It.IsAny<PlasticsRubberRequestContext>(), (ushort)number, It.IsAny<CancellationToken>()))
                    .Returns((PlasticsRubberRequestContext request, ushort _, CancellationToken token)
                        => CommitAsync(request, token));
            }
            else if (setting == "Suppression")
            {
                provider.Setup(value => value.SetAlarmSuppressionAsync(
                    It.IsAny<PlasticsRubberRequestContext>(), (ushort)number, It.IsAny<CancellationToken>()))
                    .Returns((PlasticsRubberRequestContext request, ushort _, CancellationToken token)
                        => CommitAsync(request, token));
            }
            else
            {
                MonitoredParameterSetting expectedSetting = setting switch
                {
                    "SetValue" => MonitoredParameterSetting.SetValue,
                    "Minimum" => MonitoredParameterSetting.Minimum,
                    "Maximum" => MonitoredParameterSetting.Maximum,
                    "LowerTolerance" => MonitoredParameterSetting.LowerTolerance,
                    "UpperTolerance" => MonitoredParameterSetting.UpperTolerance,
                    "LowerTolerance2" => MonitoredParameterSetting.LowerTolerance2,
                    "UpperTolerance2" => MonitoredParameterSetting.UpperTolerance2,
                    "RampUp" => MonitoredParameterSetting.RampUp,
                    "RampDown" => MonitoredParameterSetting.RampDown,
                    _ => throw new ArgumentOutOfRangeException(nameof(setting))
                };
                provider.Setup(value => value.WriteSettingAsync(
                    It.IsAny<PlasticsRubberRequestContext>(), expectedSetting, number, It.IsAny<CancellationToken>()))
                    .Returns((PlasticsRubberRequestContext request, MonitoredParameterSetting _, double _,
                        CancellationToken token) =>
                        CommitAsync(request, token));
            }
            var policy = new Mock<IPlasticsRubberAccessPolicy>(MockBehavior.Strict);
            policy.Setup(value => value.AuthorizeAsync(
                It.IsAny<PlasticsRubberRequestContext>(), member.NodeId, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            await using var binding = new MonitoredParameterBinding(node, m_context, provider.Object, policy.Object);
            byte access = member.AccessLevel;
            byte userAccess = member.UserAccessLevel;
            Assert.That(access & AccessLevels.CurrentWrite, Is.EqualTo(AccessLevels.CurrentWrite));
            Assert.That(old, Is.Not.EqualTo(expected));

            Task<ServiceResult> pending = member.WriteAttributeAsync(
                m_context, Attributes.Value, default, new DataValue(expected)).AsTask();
            try
            {
                Task first = await Task.WhenAny(entered.Task, pending).ConfigureAwait(false);
                Assert.That(first, Is.SameAs(entered.Task));
                Assert.Multiple(() =>
                {
                    Assert.That(pending.IsCompleted, Is.False);
                    Assert.That(member.Value, Is.EqualTo(old));
                    Assert.That(member.StatusCode, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
                    Assert.That(member.AccessLevel, Is.EqualTo(access));
                    Assert.That(member.UserAccessLevel, Is.EqualTo(userAccess));
                    Assert.That(node.ActualValue!.Value, Is.EqualTo(-1.25));
                });
            }
            finally
            {
                commit.TrySetResult(true);
            }
            ServiceResult result = await pending.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(reject
                    ? StatusCodes.BadResourceUnavailable : StatusCodes.Good));
                Assert.That(member.Value, Is.EqualTo(reject ? old : expected));
                Assert.That(member.StatusCode, Is.EqualTo(reject
                    ? StatusCodes.UncertainDataSubNormal : StatusCodes.Good));
                Assert.That(member.AccessLevel, Is.EqualTo(access));
                Assert.That(member.UserAccessLevel, Is.EqualTo(userAccess));
                Assert.That(node.ActualValue!.Value, Is.EqualTo(-1.25));
                Assert.That(node.ActualValue.StatusCode, Is.EqualTo(StatusCodes.UncertainSensorNotAccurate));
            });
            provider.VerifyAll();
            Assert.That(provider.Invocations, Has.Count.EqualTo(1));
            provider.VerifyNoOtherCalls();
            policy.Verify(value => value.AuthorizeAsync(
                It.IsAny<PlasticsRubberRequestContext>(), member.NodeId, It.IsAny<CancellationToken>()), Times.Once);
            policy.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A committed automatic-mode change alone transfers tolerance ownership and preserves other access bits.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task AutomaticToleranceOwnershipChangesOnlyAfterCommitAsync(bool initialAutomatic, bool reject)
        {
            MonitoredParameterState node = CreateSettingsNode(true);
            PropertyState<bool> automatic = node.AutomaticMonitoring!;
            automatic.Value = initialAutomatic;
            automatic.StatusCode = StatusCodes.UncertainDataSubNormal;
            AnalogItemState<double>[] tolerances =
                [node.LowerTolerance!, node.UpperTolerance!, node.LowerTolerance2!, node.UpperTolerance2!];
            foreach (AnalogItemState<double> tolerance in tolerances)
            {
                tolerance.AccessLevel = AccessLevels.CurrentRead | AccessLevels.HistoryRead;
                tolerance.UserAccessLevel = AccessLevels.CurrentRead | AccessLevels.HistoryRead;
                tolerance.StatusCode = StatusCodes.UncertainDataSubNormal;
            }
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var commit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IMonitoredParameterProvider>(MockBehavior.Strict);
            provider.Setup(value => value.SetAutomaticMonitoringAsync(
                It.Is<PlasticsRubberRequestContext>(request =>
                    ReferenceEquals(request.SystemContext, m_context) && request.TargetId == node.NodeId),
                !initialAutomatic, It.IsAny<CancellationToken>()))
                .Returns(async (PlasticsRubberRequestContext _, bool _, CancellationToken _) =>
                {
                    entered.TrySetResult(true);
                    await commit.Task.ConfigureAwait(false);
                    if (reject)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadInvalidState,
                            "Ownership transition refused.");
                    }
                });
            bool finallyAutomatic = reject ? initialAutomatic : !initialAutomatic;
            if (!finallyAutomatic)
            {
                provider.Setup(value => value.WriteSettingAsync(
                    It.IsAny<PlasticsRubberRequestContext>(), MonitoredParameterSetting.LowerTolerance,
                    -6.75, It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            }
            await using var binding = new MonitoredParameterBinding(node, m_context, provider.Object, AllowAccess());
            byte originalBits = (byte)(AccessLevels.CurrentRead | AccessLevels.HistoryRead |
                (initialAutomatic ? 0 : AccessLevels.CurrentWrite));
            foreach (AnalogItemState<double> tolerance in tolerances)
            {
                Assert.That(tolerance.AccessLevel, Is.EqualTo(originalBits));
                Assert.That(tolerance.UserAccessLevel, Is.EqualTo(originalBits));
            }

            Task<ServiceResult> pending = automatic.WriteAttributeAsync(
                m_context, Attributes.Value, default, new DataValue(Variant.From(!initialAutomatic))).AsTask();
            try
            {
                Task first = await Task.WhenAny(entered.Task, pending).ConfigureAwait(false);
                Assert.That(first, Is.SameAs(entered.Task));
                Assert.That(pending.IsCompleted, Is.False);
                Assert.That(automatic.Value, Is.EqualTo(initialAutomatic));
                Assert.That(automatic.StatusCode, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
                foreach (AnalogItemState<double> tolerance in tolerances)
                {
                    Assert.That(tolerance.AccessLevel, Is.EqualTo(originalBits));
                    Assert.That(tolerance.UserAccessLevel, Is.EqualTo(originalBits));
                    Assert.That(tolerance.StatusCode, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
                }
            }
            finally
            {
                commit.TrySetResult(true);
            }
            ServiceResult result = await pending.ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(reject ? StatusCodes.BadInvalidState : StatusCodes.Good));
            Assert.That(automatic.Value, Is.EqualTo(finallyAutomatic));
            Assert.That(automatic.StatusCode, Is.EqualTo(reject
                ? StatusCodes.UncertainDataSubNormal : StatusCodes.Good));
            byte finalBits = (byte)(AccessLevels.CurrentRead | AccessLevels.HistoryRead |
                (finallyAutomatic ? 0 : AccessLevels.CurrentWrite));
            foreach (AnalogItemState<double> tolerance in tolerances)
            {
                Assert.That(tolerance.AccessLevel, Is.EqualTo(finalBits));
                Assert.That(tolerance.UserAccessLevel, Is.EqualTo(finalBits));
            }

            ServiceResult toleranceWrite = await node.LowerTolerance!.WriteAttributeAsync(
                m_context, Attributes.Value, default, new DataValue(Variant.From(-6.75))).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(toleranceWrite.StatusCode, Is.EqualTo(finallyAutomatic
                    ? StatusCodes.BadNotWritable : StatusCodes.Good));
                Assert.That(node.LowerTolerance.Value, Is.EqualTo(finallyAutomatic ? -5 : -6.75));
                Assert.That(node.LowerTolerance.StatusCode, Is.EqualTo(
                    finallyAutomatic ? StatusCodes.UncertainDataSubNormal : StatusCodes.Good));
                Assert.That(node.UpperTolerance!.Value, Is.EqualTo(5));
                Assert.That(node.LowerTolerance2!.Value, Is.EqualTo(-20));
                Assert.That(node.UpperTolerance2!.Value, Is.EqualTo(20));
            });
            provider.Verify(value => value.SetAutomaticMonitoringAsync(
                It.IsAny<PlasticsRubberRequestContext>(), !initialAutomatic, It.IsAny<CancellationToken>()),
                    Times.Once);
            provider.Verify(value => value.WriteSettingAsync(
                It.IsAny<PlasticsRubberRequestContext>(), MonitoredParameterSetting.LowerTolerance,
                -6.75, It.IsAny<CancellationToken>()), finallyAutomatic ? Times.Never() : Times.Once());
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Width, finite-domain and limit failures are distinguished without dispatching a commit.
        /// </summary>
        [TestCaseSource(nameof(InvalidMonitoredSettings))]
        public async Task MonitoredSettingsRejectWrongTypesAndOutOfRangeValuesAsync(
            string setting, Variant value, StatusCode expected)
        {
            MonitoredParameterState node = CreateSettingsNode(true);
            BaseVariableState member = SettingMember(node, setting);
            member.StatusCode = StatusCodes.UncertainDataSubNormal;
            var provider = new Mock<IMonitoredParameterProvider>(MockBehavior.Strict);
            await using var binding = new MonitoredParameterBinding(node, m_context, provider.Object, AllowAccess());
            Variant old = member.Value;
            byte access = member.AccessLevel;
            byte userAccess = member.UserAccessLevel;

            ServiceResult result = await member.WriteAttributeAsync(
                m_context, Attributes.Value, default, new DataValue(value)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(expected));
                Assert.That(member.Value, Is.EqualTo(old));
                Assert.That(member.StatusCode, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
                Assert.That(member.AccessLevel, Is.EqualTo(access));
                Assert.That(member.UserAccessLevel, Is.EqualTo(userAccess));
                Assert.That(node.AutomaticMonitoring!.Value, Is.False);
                Assert.That(node.ActualValue!.Value, Is.EqualTo(-1.25));
            });
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Identical ramps are observations on a monitor and committed controls on a controlled parameter.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PlainAndControlledRampsHaveDistinctWriteCapabilitiesAsync(bool controlled)
        {
            MonitoredParameterState node = CreateSettingsNode(controlled);
            node.SetRampUp!.StatusCode = StatusCodes.UncertainDataSubNormal;
            var provider = new Mock<IMonitoredParameterProvider>(MockBehavior.Strict);
            if (controlled)
            {
                provider.Setup(value => value.WriteSettingAsync(
                    It.Is<PlasticsRubberRequestContext>(request =>
                        ReferenceEquals(request.SystemContext, m_context) && request.TargetId == node.NodeId),
                    MonitoredParameterSetting.RampUp, 6.25, It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            }
            await using var binding = new MonitoredParameterBinding(node, m_context, provider.Object, AllowAccess());

            ServiceResult result = await node.SetRampUp.WriteAttributeAsync(
                m_context, Attributes.Value, default, new DataValue(Variant.From(6.25))).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(controlled ? StatusCodes.Good : StatusCodes.BadNotWritable));
                Assert.That(node.SetRampUp.Value, Is.EqualTo(controlled ? 6.25 : 1.5));
                Assert.That(node.SetRampUp.StatusCode, Is.EqualTo(
                    controlled ? StatusCodes.Good : StatusCodes.UncertainDataSubNormal));
                Assert.That(node.SetRampDown!.Value, Is.EqualTo(2.5));
            });
            provider.Verify(value => value.WriteSettingAsync(
                It.IsAny<PlasticsRubberRequestContext>(), MonitoredParameterSetting.RampUp,
                6.25, It.IsAny<CancellationToken>()), controlled ? Times.Once() : Times.Never());
            provider.VerifyNoOtherCalls();
        }

        private static IEnumerable<TestCaseData> InvalidMonitoredSettings()
        {
            yield return new TestCaseData("SetValue", Variant.From(12), StatusCodes.BadTypeMismatch);
            yield return new TestCaseData("Sensitivity", Variant.From((byte)1), StatusCodes.BadTypeMismatch);
            yield return new TestCaseData("Sensitivity", Variant.From((short)1), StatusCodes.BadTypeMismatch);
            yield return new TestCaseData("Suppression", Variant.From(1U), StatusCodes.BadTypeMismatch);
            yield return new TestCaseData("Automatic", Variant.From(1), StatusCodes.BadTypeMismatch);
            yield return new TestCaseData("SetValue", Variant.From(double.NaN), StatusCodes.BadOutOfRange);
            yield return new TestCaseData("SetValue", Variant.From(double.PositiveInfinity), StatusCodes.BadOutOfRange);
            yield return new TestCaseData("SetValue", Variant.From(double.NegativeInfinity), StatusCodes.BadOutOfRange);
            yield return new TestCaseData("RampUp", Variant.From(-double.Epsilon), StatusCodes.BadOutOfRange);
            yield return new TestCaseData("RampDown", Variant.From(-double.Epsilon), StatusCodes.BadOutOfRange);
            yield return new TestCaseData("Sensitivity", Variant.From((ushort)3), StatusCodes.BadOutOfRange);
            yield return new TestCaseData("Sensitivity", Variant.From(ushort.MaxValue), StatusCodes.BadOutOfRange);
            yield return new TestCaseData("Suppression", Variant.From((ushort)3), StatusCodes.BadOutOfRange);
            yield return new TestCaseData("Suppression", Variant.From(ushort.MaxValue), StatusCodes.BadOutOfRange);
            yield return new TestCaseData("LowerTolerance", Variant.From(1.0), StatusCodes.BadOutOfRange);
            yield return new TestCaseData("Minimum", Variant.From(201.0), StatusCodes.BadOutOfRange);
        }

        private static IEnumerable<TestCaseData> MonitoredCommitCases()
        {
            (string Setting, double Value)[] cases =
            [
                ("SetValue", -12.5), ("Minimum", -250), ("Maximum", 350),
                ("LowerTolerance", -7.5), ("UpperTolerance", 9.25),
                ("LowerTolerance2", -35.5), ("UpperTolerance2", 44.25),
                ("RampUp", 0), ("RampUp", 3.5), ("RampDown", 0), ("RampDown", 7.5),
                ("Sensitivity", 0), ("Sensitivity", 1), ("Sensitivity", 2),
                ("Suppression", 0), ("Suppression", 1), ("Suppression", 2)
            ];
            foreach ((string setting, double value) in cases)
            {
                yield return new TestCaseData(setting, value, false);
                yield return new TestCaseData(setting, value, true);
            }
        }

        private MonitoredParameterState CreateSettingsNode(bool controlled)
        {
            MonitoredParameterState node = controlled
                ? m_context.CreateInstanceOfControlledParameterType(Parent(), new QualifiedName("Controller", 1))
                : m_context.CreateInstanceOfMonitoredParameterType(Parent(), new QualifiedName("Monitor", 1));
            if (node.SetValue == null)
            {
                node.AddSetValue(m_context);
            }
            node.AddMinValue(m_context);
            node.AddMaxValue(m_context);
            node.AddLowerTolerance(m_context);
            node.AddUpperTolerance(m_context);
            node.AddLowerTolerance2(m_context);
            node.AddUpperTolerance2(m_context);
            node.AddSetRampUp(m_context);
            node.AddSetRampDown(m_context);
            node.AddAutomaticMonitoring(m_context);
            node.AddMonitoringSensitivity(m_context);
            node.AddAlarmSuppression(m_context);
            node.SetValue!.Value = -10;
            node.MinValue!.Value = -200;
            node.MaxValue!.Value = 200;
            node.LowerTolerance!.Value = -5;
            node.UpperTolerance!.Value = 5;
            node.LowerTolerance2!.Value = -20;
            node.UpperTolerance2!.Value = 20;
            node.SetRampUp!.Value = 1.5;
            node.SetRampDown!.Value = 2.5;
            node.AutomaticMonitoring!.Value = false;
            node.ActualValue!.Value = -1.25;
            node.ActualValue.StatusCode = StatusCodes.UncertainSensorNotAccurate;
            AnalogItemState<double>[] analogs =
            [
                node.ActualValue, node.SetValue, node.MinValue, node.MaxValue,
                node.LowerTolerance, node.UpperTolerance, node.LowerTolerance2, node.UpperTolerance2,
                node.SetRampUp, node.SetRampDown
            ];
            foreach (AnalogItemState<double> analog in analogs)
            {
                analog.AddEngineeringUnits(m_context);
                analog.EngineeringUnits!.Value = new EUInformation
                {
                    NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                    UnitId = 4408652,
                    DisplayName = new LocalizedText("°C")
                };
            }
            return node;
        }

        private static BaseVariableState SettingMember(MonitoredParameterState node, string setting)
        {
            return setting switch
            {
                "SetValue" => node.SetValue!,
                "Minimum" => node.MinValue!,
                "Maximum" => node.MaxValue!,
                "LowerTolerance" => node.LowerTolerance!,
                "UpperTolerance" => node.UpperTolerance!,
                "LowerTolerance2" => node.LowerTolerance2!,
                "UpperTolerance2" => node.UpperTolerance2!,
                "RampUp" => node.SetRampUp!,
                "RampDown" => node.SetRampDown!,
                "Automatic" => node.AutomaticMonitoring!,
                "Sensitivity" => node.MonitoringSensitivity!,
                "Suppression" => node.AlarmSuppression!,
                _ => throw new ArgumentOutOfRangeException(nameof(setting))
            };
        }
    }
}
