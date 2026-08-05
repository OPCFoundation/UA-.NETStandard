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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Robotics.Server;
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;
using Opc.Ua.Server;
using Opc.Ua.Tests;
using RiDataTypeIds = Opc.Ua.RobotIntent.DataTypeIds;
using RiNamespaces = Opc.Ua.RobotIntent.Namespaces;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Verifies Robot Intent method bindings accept their generated argument shape.
    /// </summary>
    [TestFixture]
    public class IntentBuilderMethodBindingTests
    {
        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(true);
            ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.Append(RiNamespaces.RobotIntent);
            messageContext.Factory.Builder.AddOpcUaRobotIntent().Commit();
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                EncodeableFactory = messageContext.Factory
            };

            m_controller = new IntentControllerState(null);
            m_controller.Create(
                m_context,
                new NodeId("Controller", 1),
                new QualifiedName("Controller", 1),
                new LocalizedText("Controller"),
                true);
            AddOptionalMethods();

            m_host = new IntentControllerHost(
                m_controller,
                new CompletingExecutor(),
                (_, _) => default,
                Options());
            m_host.Start(m_context);
        }

        [TearDown]
        public void TearDown()
        {
            m_host?.Dispose();
        }

        [TestCaseSource(nameof(MethodCalls))]
        public async Task BoundMethodReturnsGoodForGeneratedArgumentShape(BoundMethodCall methodCall)
        {
            (ServiceResult result, int outputCount) = await methodCall.InvokeAsync(
                m_controller,
                m_context).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True, $"{methodCall.Name} returned {result}.");
                Assert.That(outputCount, Is.EqualTo(methodCall.OutputCount), methodCall.Name);
            });
        }

        private static IEnumerable<BoundMethodCall> MethodCalls()
        {
            yield return Call(
                "RequestControl",
                async (controller, context) =>
                {
                    Assert.That(controller.RequestControl!.OnCallAsync, Is.Not.Null);
                    RequestControlMethodStateResult result = await controller.RequestControl.OnCallAsync!(
                        context,
                        controller.RequestControl,
                        controller.NodeId,
                        CancellationToken.None).ConfigureAwait(false);
                    return (result.ServiceResult, 2);
                },
                2);
            yield return Call(
                "ReleaseControl",
                async (controller, context) =>
                {
                    Assert.That(controller.ReleaseControl!.OnCallMethod2Async, Is.Not.Null);
                    var outputs = new List<Variant>();
                    ServiceResult result = await controller.ReleaseControl.OnCallMethod2Async!(
                        context,
                        controller.ReleaseControl,
                        controller.NodeId,
                        [],
                        outputs,
                        CancellationToken.None).ConfigureAwait(false);
                    return (result, outputs.Count);
                },
                0);
            yield return Call(
                "SubmitIntent",
                async (controller, context) =>
                {
                    Assert.That(controller.SubmitIntent!.OnCallAsync, Is.Not.Null);
                    SubmitIntentMethodStateResult result = await controller.SubmitIntent.OnCallAsync!(
                        context,
                        controller.SubmitIntent,
                        controller.NodeId,
                        Move(),
                        CancellationToken.None).ConfigureAwait(false);
                    return (result.ServiceResult, 5);
                },
                5);
            yield return Call(
                "CancelIntent",
                async (controller, context) =>
                {
                    Assert.That(controller.CancelIntent!.OnCallAsync, Is.Not.Null);
                    CancelIntentMethodStateResult result = await controller.CancelIntent.OnCallAsync!(
                        context,
                        controller.CancelIntent,
                        controller.NodeId,
                        "missing",
                        StopModeEnum.OnPath,
                        CancellationToken.None).ConfigureAwait(false);
                    return (result.ServiceResult, 1);
                },
                1);
            yield return Call(
                "CancelAll",
                async (controller, context) =>
                {
                    Assert.That(controller.CancelAll!.OnCallAsync, Is.Not.Null);
                    CancelAllMethodStateResult result = await controller.CancelAll.OnCallAsync!(
                        context,
                        controller.CancelAll,
                        controller.NodeId,
                        StopModeEnum.OnPath,
                        CancellationToken.None).ConfigureAwait(false);
                    return (result.ServiceResult, 1);
                },
                1);
            yield return Call(
                "Pause",
                async (controller, context) =>
                {
                    Assert.That(controller.Pause!.OnCallAsync, Is.Not.Null);
                    PauseMethodStateResult result = await controller.Pause.OnCallAsync!(
                        context,
                        controller.Pause,
                        controller.NodeId,
                        CancellationToken.None).ConfigureAwait(false);
                    return (result.ServiceResult, 1);
                },
                1);
            yield return Call(
                "Resume",
                async (controller, context) =>
                {
                    Assert.That(controller.Resume!.OnCallAsync, Is.Not.Null);
                    ResumeMethodStateResult result = await controller.Resume.OnCallAsync!(
                        context,
                        controller.Resume,
                        controller.NodeId,
                        CancellationToken.None).ConfigureAwait(false);
                    return (result.ServiceResult, 1);
                },
                1);
            yield return Call(
                "Retry",
                async (controller, context) =>
                {
                    Assert.That(controller.Retry!.OnCallAsync, Is.Not.Null);
                    RetryMethodStateResult result = await controller.Retry.OnCallAsync!(
                        context,
                        controller.Retry,
                        controller.NodeId,
                        "missing",
                        CancellationToken.None).ConfigureAwait(false);
                    return (result.ServiceResult, 4);
                },
                4);
            yield return Call(
                "SubmitMission",
                async (controller, context) =>
                {
                    Assert.That(controller.SubmitMission!.OnCallAsync, Is.Not.Null);
                    SubmitMissionMethodStateResult result = await controller.SubmitMission.OnCallAsync!(
                        context,
                        controller.SubmitMission,
                        controller.NodeId,
                        new MissionDataType { MissionId = Guid.NewGuid().ToString("N") },
                        CancellationToken.None).ConfigureAwait(false);
                    return (result.ServiceResult, 5);
                },
                5);
            yield return Call(
                "UpdateMission",
                async (controller, context) =>
                {
                    Assert.That(controller.UpdateMission!.OnCallAsync, Is.Not.Null);
                    UpdateMissionMethodStateResult result = await controller.UpdateMission.OnCallAsync!(
                        context,
                        controller.UpdateMission,
                        controller.NodeId,
                        "missing",
                        1,
                        [],
                        CancellationToken.None).ConfigureAwait(false);
                    return (result.ServiceResult, 2);
                },
                2);
            yield return Call(
                "CancelMission",
                async (controller, context) =>
                {
                    Assert.That(controller.CancelMission!.OnCallAsync, Is.Not.Null);
                    CancelMissionMethodStateResult result = await controller.CancelMission.OnCallAsync!(
                        context,
                        controller.CancelMission,
                        controller.NodeId,
                        "missing",
                        StopModeEnum.OnPath,
                        CancellationToken.None).ConfigureAwait(false);
                    return (result.ServiceResult, 1);
                },
                1);
            yield return Call(
                "OpenRealTimeChannel",
                async (controller, context) =>
                {
                    Assert.That(controller.OpenRealTimeChannel!.OnCallAsync, Is.Not.Null);
                    OpenRealTimeChannelMethodStateResult result = await controller.OpenRealTimeChannel.OnCallAsync!(
                        context,
                        controller.OpenRealTimeChannel,
                        controller.NodeId,
                        "test-channel",
                        10.0,
                        CancellationToken.None).ConfigureAwait(false);
                    return (result.ServiceResult, 5);
                },
                5);
            yield return Call(
                "CloseRealTimeChannel",
                async (controller, context) =>
                {
                    Assert.That(controller.CloseRealTimeChannel!.OnCallAsync, Is.Not.Null);
                    CloseRealTimeChannelMethodStateResult result = await controller.CloseRealTimeChannel.OnCallAsync!(
                        context,
                        controller.CloseRealTimeChannel,
                        controller.NodeId,
                        "test-channel",
                        CancellationToken.None).ConfigureAwait(false);
                    return (result.ServiceResult, 1);
                },
                1);
        }

        private static BoundMethodCall Call(
            string name,
            Func<IntentControllerState, SystemContext, ValueTask<(ServiceResult Result, int OutputCount)>> invokeAsync,
            int outputCount)
        {
            return new BoundMethodCall(name, invokeAsync, outputCount);
        }

        private static IntentControllerHostOptions Options()
        {
            var options = new IntentControllerHostOptions
            {
                MissionsSupported = true,
                MissionHorizonSupported = true,
                RealTimeChannelsSupported = true,
                AxisCount = 6,
                MaxQueueDepth = 4
            };
            options.Accept(RiDataTypeIds.LinearMoveIntentDataType);
            options.Channels.Add(new DeclaredChannel
            {
                ChannelId = "test-channel",
                EndpointUrl = "udp://239.0.0.40:4840",
                Transport = RealTimeTransportEnum.OpcUaFx,
                Initiator = ChannelInitiatorEnum.Client,
                RequiredMode = OperationalModeEnum.AutomaticExternal
            });
            return options;
        }

        private void AddOptionalMethods()
        {
            m_controller.AddSubmitMission(m_context);
            m_controller.AddUpdateMission(m_context);
            m_controller.AddCancelMission(m_context);
            m_controller.AddOpenRealTimeChannel(m_context);
            m_controller.AddCloseRealTimeChannel(m_context);
            m_controller.AddPause(m_context);
            m_controller.AddResume(m_context);
            m_controller.AddRetry(m_context);
        }

        private static LinearMoveIntentDataType Move()
        {
            return new LinearMoveIntentDataType
            {
                IntentId = "move",
                BufferMode = BufferModeEnum.Aborting,
                Target = new Pose3DDataType
                {
                    FrameId = "base",
                    Position = new[] { 0.1, 0.0, 0.1 },
                    Orientation = new[] { 0.0, 0.0, 0.0, 1.0 }
                }
            };
        }

        private SystemContext m_context = null!;
        private IntentControllerState m_controller = null!;
        private IntentControllerHost m_host = null!;

        public sealed record BoundMethodCall(
            string Name,
            Func<IntentControllerState, SystemContext, ValueTask<(ServiceResult Result, int OutputCount)>> InvokeAsync,
            int OutputCount);

        private sealed class CompletingExecutor : IIntentExecutor
        {
            public ValueTask<IntentOutcome> ExecuteAsync(
                IntentExecution execution,
                CancellationToken cancellationToken)
            {
                return new ValueTask<IntentOutcome>(IntentOutcome.Success);
            }

            public bool CanCancel(IntentExecution execution)
            {
                return true;
            }
        }
    }
}
