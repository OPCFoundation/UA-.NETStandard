/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
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
        /// Native Boolean and structured PID writes publish only after the selected provider succeeds.
        /// </summary>
        [TestCase("automatic", 0, false)]
        [TestCase("automatic", 0, true)]
        [TestCase("automatic", 1, false)]
        [TestCase("automatic", 1, true)]
        [TestCase("pid", 0, false)]
        [TestCase("pid", 0, true)]
        [TestCase("pid", 1, false)]
        [TestCase("pid", 1, true)]
        [TestCase("pid", 3, false)]
        [TestCase("pid", 3, true)]
        public async Task ClosedLoopSettingsPublishOnlyAfterUaWriteCommitAsync(
            string setting, int partition, bool reject)
        {
            ClosedLoopControlState node = CreateClosedLoopSettings();
            bool automatic = setting == "automatic";
            if (automatic)
            {
                node.AutomaticControllerMode!.Value = partition == 0;
            }
            BaseVariableState member = automatic ? node.AutomaticControllerMode! : node.PIDParameters!;
            member.StatusCode = StatusCodes.UncertainDataSubNormal;
            Variant old = member.Value;
            Variant oldPid = ((BaseVariableState)node.PIDParameters!).Value;
            Variant expected = automatic
                ? Variant.From(partition != 0)
                : Variant.FromStructure(PidInput(partition));
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
                    throw ServiceResultException.Create(StatusCodes.BadInvalidState,
                        "Controller rejected the setting.");
                }
            }
            var provider = new Mock<IClosedLoopControlProvider>(MockBehavior.Strict);
            if (automatic)
            {
                provider.Setup(value => value.SetAutomaticModeAsync(
                    It.IsAny<PlasticsRubberRequestContext>(), partition != 0, It.IsAny<CancellationToken>()))
                    .Returns((PlasticsRubberRequestContext request, bool _, CancellationToken token)
                        => CommitAsync(request, token));
            }
            else
            {
                provider.Setup(value => value.SetParametersAsync(
                    It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<ArrayOf<PIDParametersDataType>>(),
                    It.IsAny<CancellationToken>()))
                    .Returns((PlasticsRubberRequestContext request, ArrayOf<PIDParametersDataType> parameters,
                        CancellationToken token) =>
                    {
                        AssertPidSequence(parameters, partition);
                        return CommitAsync(request, token);
                    });
            }
            var policy = new Mock<IPlasticsRubberAccessPolicy>(MockBehavior.Strict);
            policy.Setup(value => value.AuthorizeAsync(
                It.IsAny<PlasticsRubberRequestContext>(), member.NodeId, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            await using var binding = new ClosedLoopControlBinding(node, m_context, provider.Object, policy.Object);
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
                    Assert.That(((BaseVariableState)node.PIDParameters!).Value, Is.EqualTo(oldPid));
                    Assert.That(node.AutoTuningActive!.Value, Is.True);
                });
            }
            finally
            {
                commit.TrySetResult(true);
            }
            ServiceResult result = await pending.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(reject ? StatusCodes.BadInvalidState : StatusCodes.Good));
                Assert.That(member.Value, Is.EqualTo(reject ? old : expected));
                Assert.That(member.StatusCode, Is.EqualTo(reject
                    ? StatusCodes.UncertainDataSubNormal : StatusCodes.Good));
                Assert.That(node.AutoTuningActive!.Value, Is.True);
                Assert.That(node.AutoTuningActive.StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
                if (automatic)
                {
                    Assert.That(((BaseVariableState)node.PIDParameters!).Value, Is.EqualTo(oldPid));
                }
                else if (!reject)
                {
                    AssertPidSequence(node.PIDParameters!.Value, partition);
                }
            });
            provider.VerifyAll();
            Assert.That(provider.Invocations, Has.Count.EqualTo(1));
            provider.VerifyNoOtherCalls();
            policy.Verify(value => value.AuthorizeAsync(
                It.IsAny<PlasticsRubberRequestContext>(), member.NodeId, It.IsAny<CancellationToken>()), Times.Once);
            policy.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Invalid coefficients and controller ownership cannot replace the old PID array.
        /// </summary>
        [TestCaseSource(nameof(InvalidClosedLoopSettings))]
        public async Task ClosedLoopPidWritesEnforceStructureFinitenessAndOwnershipAsync(
            string partition, Variant value, bool automatic, StatusCode expected)
        {
            ClosedLoopControlState node = CreateClosedLoopSettings();
            node.AutomaticControllerMode!.Value = automatic;
            BaseVariableState member = partition == "automatic-wrong-type"
                ? node.AutomaticControllerMode : node.PIDParameters!;
            member.StatusCode = StatusCodes.UncertainDataSubNormal;
            Variant old = member.Value;
            Variant oldPid = ((BaseVariableState)node.PIDParameters!).Value;
            var provider = new Mock<IClosedLoopControlProvider>(MockBehavior.Strict);
            await using var binding = new ClosedLoopControlBinding(node, m_context, provider.Object, AllowAccess());

            ServiceResult result = await member.WriteAttributeAsync(
                m_context, Attributes.Value, default, new DataValue(value)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(expected), partition);
                Assert.That(member.Value, Is.EqualTo(old));
                Assert.That(member.StatusCode, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
                Assert.That(((BaseVariableState)node.PIDParameters!).Value, Is.EqualTo(oldPid));
                Assert.That(node.AutomaticControllerMode.Value, Is.EqualTo(automatic));
                Assert.That(node.AutoTuningActive!.Value, Is.True);
            });
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// On/off requests never fabricate a physical tuning transition, even after successful acceptance.
        /// </summary>
        [TestCase(true, false)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        [TestCase(false, true)]
        public async Task ClosedLoopTuningDoesNotPublishOptimisticActivityAsync(bool start, bool reject)
        {
            ClosedLoopControlState node = CreateClosedLoopSettings();
            node.AutoTuningActive!.Value = !start;
            MethodState method = start ? node.AutoTuningOn! : node.AutoTuningOff!;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var commit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            async ValueTask RequestAsync(PlasticsRubberRequestContext request, CancellationToken token)
            {
                Assert.That(request.SystemContext, Is.SameAs(m_context));
                Assert.That(request.TargetId, Is.EqualTo(node.NodeId));
                Assert.That(token.CanBeCanceled, Is.True);
                entered.TrySetResult(true);
                await commit.Task.ConfigureAwait(false);
                if (reject)
                {
                    throw ServiceResultException.Create(StatusCodes.BadInvalidState, "Tuning unavailable.");
                }
            }
            var provider = new Mock<IClosedLoopControlProvider>(MockBehavior.Strict);
            if (start)
            {
                provider.Setup(value => value.StartTuningAsync(
                    It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CancellationToken>()))
                    .Returns((PlasticsRubberRequestContext request, CancellationToken token)
                        => RequestAsync(request, token));
            }
            else
            {
                provider.Setup(value => value.StopTuningAsync(
                    It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CancellationToken>()))
                    .Returns((PlasticsRubberRequestContext request, CancellationToken token)
                        => RequestAsync(request, token));
            }
            await using var binding = new ClosedLoopControlBinding(node, m_context, provider.Object, AllowAccess());
            Variant oldPid = ((BaseVariableState)node.PIDParameters!).Value;

            Task<ServiceResult> pending = method.CallAsync(m_context, node.NodeId, [], [], []).AsTask();
            try
            {
                Task first = await Task.WhenAny(entered.Task, pending).ConfigureAwait(false);
                Assert.That(first, Is.SameAs(entered.Task));
                Assert.That(pending.IsCompleted, Is.False);
                Assert.That(node.AutoTuningActive.Value, Is.EqualTo(!start));
                Assert.That(node.AutoTuningActive.StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
                Assert.That(((BaseVariableState)node.PIDParameters).Value, Is.EqualTo(oldPid));
            }
            finally
            {
                commit.TrySetResult(true);
            }
            ServiceResult result = await pending.ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(reject ? StatusCodes.BadInvalidState : StatusCodes.Good));
                Assert.That(node.AutoTuningActive.Value, Is.EqualTo(!start));
                Assert.That(node.AutoTuningActive.StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
                Assert.That(((BaseVariableState)node.PIDParameters).Value, Is.EqualTo(oldPid));
            });
            provider.VerifyAll();
            Assert.That(provider.Invocations, Has.Count.EqualTo(1));
            provider.VerifyNoOtherCalls();
        }

        private ClosedLoopControlState CreateClosedLoopSettings()
        {
            var types = (TypeTable)m_context.TypeTable;
            if (!types.IsKnown(Opc.Ua.DataTypeIds.Structure))
            {
                types.AddSubtype(Opc.Ua.DataTypeIds.BaseDataType, NodeId.Null);
                types.AddSubtype(Opc.Ua.DataTypeIds.Structure, Opc.Ua.DataTypeIds.BaseDataType);
            }
            types.AddSubtype(
                ExpandedNodeId.ToNodeId(DataTypeIds.PIDParametersDataType, m_context.NamespaceUris),
                Opc.Ua.DataTypeIds.Structure);
            ClosedLoopControlState node = m_context.CreateInstanceOfClosedLoopControlType(
                Parent(), new QualifiedName("ClosedLoop", 1));
            node.AddAutomaticControllerMode(m_context);
            node.AddPIDParameters(m_context);
            node.AddAutoTuningOn(m_context);
            node.AddAutoTuningOff(m_context);
            node.AddAutoTuningActive(m_context);
            node.AutomaticControllerMode!.Value = false;
            node.PIDParameters!.Value = [new PIDParametersDataType { P = 91, I = 92, D = 93 }];
            node.AutoTuningActive!.Value = true;
            node.AutoTuningActive.StatusCode = StatusCodes.UncertainLastUsableValue;
            return node;
        }

        private static ArrayOf<PIDParametersDataType> PidInput(int count)
        {
            PIDParametersDataType[] values =
            [
                new() { P = -1.5, I = 0, D = 2.25 },
                new() { P = 0, I = 3.75, D = -4.5 },
                new() { P = 5.25, I = -6.5, D = 0 }
            ];
            return values.Take(count).ToArray().ToArrayOf();
        }

        private static void AssertPidSequence(ArrayOf<PIDParametersDataType> actual, int count)
        {
            (double P, double I, double D)[] expected = [(-1.5, 0, 2.25), (0, 3.75, -4.5), (5.25, -6.5, 0)];
            Assert.That(actual.Count, Is.EqualTo(count));
            Assert.That(actual.ToArray()!.Select(value => (value.P, value.I, value.D)),
                Is.EqualTo(expected.Take(count)));
        }

        private static IEnumerable<TestCaseData> InvalidClosedLoopSettings()
        {
            yield return new TestCaseData("wrong-structure", Variant.From("not a PID array"), false,
                StatusCodes.BadTypeMismatch);
            yield return new TestCaseData("automatic-wrong-type", Variant.From(1), false, StatusCodes.BadTypeMismatch);
            yield return new TestCaseData("owned", Variant.FromStructure(PidInput(3)), true,
                StatusCodes.BadNotWritable);
            ArrayOf<PIDParametersDataType> missing = [new() { P = 1, I = 2, D = 3 }, null!];
            // A null encoded structure fails the UA value-type check before the binding can
            // validate finite coefficients. Pin the public WriteAttributeAsync contract.
            yield return new TestCaseData("null-element", Variant.FromStructure(missing), false,
                StatusCodes.BadTypeMismatch);
            (double Value, string Name)[] invalid =
                [(double.NaN, "NaN"), (double.PositiveInfinity, "PositiveInfinity"), (double.NegativeInfinity,
                    "NegativeInfinity")];
            foreach ((double value, string name) in invalid)
            {
                ArrayOf<PIDParametersDataType> p = [new() { P = value, I = 2, D = 3 }];
                ArrayOf<PIDParametersDataType> i = [new() { P = 1, I = value, D = 3 }];
                ArrayOf<PIDParametersDataType> d = [new() { P = 1, I = 2, D = value }];
                yield return new TestCaseData("P", Variant.FromStructure(p), false, StatusCodes.BadOutOfRange)
                    .SetName($"{nameof(ClosedLoopPidWritesEnforceStructureFinitenessAndOwnershipAsync)}(P,{name})");
                yield return new TestCaseData("I", Variant.FromStructure(i), false, StatusCodes.BadOutOfRange)
                    .SetName($"{nameof(ClosedLoopPidWritesEnforceStructureFinitenessAndOwnershipAsync)}(I,{name})");
                yield return new TestCaseData("D", Variant.FromStructure(d), false, StatusCodes.BadOutOfRange)
                    .SetName($"{nameof(ClosedLoopPidWritesEnforceStructureFinitenessAndOwnershipAsync)}(D,{name})");
            }
        }
    }
}
