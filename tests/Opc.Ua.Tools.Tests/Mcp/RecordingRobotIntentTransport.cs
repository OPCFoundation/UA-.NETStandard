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

#if NET10_0
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// A Robot Intent transport double that records how often the MCP layer
    /// reads, submits, or touches command authority.
    /// </summary>
    internal sealed class RecordingRobotIntentTransport : IRobotIntentTransport
    {
        public event RobotIntentReconnectHandler? Reconnected
        {
            add { }
            remove { }
        }

        public ILogger Logger { get; } = NullLogger.Instance;

        public NodeId ControllerId { get; init; } = new("Controllers/Controller1", 2);

        public NamespaceTable NamespaceUris { get; } = new();

        public IServiceMessageContext MessageContext { get; } = new ServiceMessageContext(
            DefaultTelemetry.Create(static _ => { }),
            EncodeableFactory.Create());

        public RobotIntentControllerInfo ControllerInfo { get; init; } = new();

        public RobotIntentControllerState ControllerState { get; init; } = new();

        public IntentSubmissionResult SubmissionResult { get; init; } = new() { Accepted = true };

        public MissionSubmissionResult MissionResult { get; init; } = new() { Accepted = true };

        public IntentOperationSnapshot OperationSnapshot { get; init; } = new();

        public MissionSnapshot MissionSnapshot { get; init; } = new();

        public int ReadControllerCallCount { get; private set; }

        public int ReadMissionSnapshotCallCount { get; private set; }

        public int ListMissionsCallCount { get; private set; }

        public int ListOperationsCallCount { get; private set; }

        public int SubmitIntentCallCount { get; private set; }

        public int SubmitMissionCallCount { get; private set; }

        public int RequestControlCallCount { get; private set; }

        public int ReleaseControlCallCount { get; private set; }

        public int SubscribeCallCount { get; private set; }

        public ValueTask<ArrayOf<RobotIntentNodeLookupEntry>> BrowseControllersAsync(CancellationToken ct = default)
        {
            return ValueTask.FromResult<ArrayOf<RobotIntentNodeLookupEntry>>(
                [new RobotIntentNodeLookupEntry(ControllerId, new QualifiedName("Controller1", 2), "Controller1")]);
        }

        public ValueTask<RobotIntentControllerInfo> ReadControllerAsync(CancellationToken ct = default)
        {
            ReadControllerCallCount++;
            return ValueTask.FromResult(ControllerInfo);
        }

        public ValueTask<RobotIntentControllerState> ReadControllerStateAsync(CancellationToken ct = default)
        {
            return ValueTask.FromResult(ControllerState);
        }

        public ValueTask<ArrayOf<IntentOperationSnapshot>> ListOperationsAsync(CancellationToken ct = default)
        {
            ListOperationsCallCount++;
            return ValueTask.FromResult<ArrayOf<IntentOperationSnapshot>>([OperationSnapshot]);
        }

        public ValueTask<ArrayOf<MissionSnapshot>> ListMissionsAsync(CancellationToken ct = default)
        {
            ListMissionsCallCount++;
            return ValueTask.FromResult<ArrayOf<MissionSnapshot>>([MissionSnapshot]);
        }

        public ValueTask<IntentSubmissionResult> SubmitIntentAsync(
            IntentDataType intent,
            CancellationToken ct = default)
        {
            SubmitIntentCallCount++;
            return ValueTask.FromResult(SubmissionResult);
        }

        public ValueTask<IntentCommandOutcome> CancelIntentAsync(
            string intentId,
            StopModeEnum stopMode,
            CancellationToken ct = default)
        {
            return ValueTask.FromResult(new IntentCommandOutcome(true));
        }

        public ValueTask<uint> CancelAllAsync(StopModeEnum stopMode, CancellationToken ct = default)
        {
            return ValueTask.FromResult<uint>(1);
        }

        public ValueTask<IntentCommandOutcome> PauseAsync(CancellationToken ct = default)
        {
            return ValueTask.FromResult(new IntentCommandOutcome(true));
        }

        public ValueTask<IntentCommandOutcome> ResumeAsync(CancellationToken ct = default)
        {
            return ValueTask.FromResult(new IntentCommandOutcome(true));
        }

        public ValueTask<IntentSubmissionResult> RetryAsync(string intentId, CancellationToken ct = default)
        {
            return ValueTask.FromResult(SubmissionResult);
        }

        public ValueTask<MissionSubmissionResult> SubmitMissionAsync(
            MissionDataType mission,
            CancellationToken ct = default)
        {
            SubmitMissionCallCount++;
            return ValueTask.FromResult(MissionResult);
        }

        public ValueTask<MissionUpdateOutcome> UpdateMissionAsync(
            string missionId,
            uint missionUpdateId,
            ArrayOf<MissionStepDataType> steps,
            CancellationToken ct = default)
        {
            return ValueTask.FromResult(
                new MissionUpdateOutcome(MissionUpdateResultEnum.Accepted, LocalizedText.Null));
        }

        public ValueTask<IntentCommandOutcome> CancelMissionAsync(
            string missionId,
            StopModeEnum stopMode,
            CancellationToken ct = default)
        {
            return ValueTask.FromResult(new IntentCommandOutcome(true));
        }

        public ValueTask<CommandAuthorityOutcome> RequestControlAsync(CancellationToken ct = default)
        {
            RequestControlCallCount++;
            return ValueTask.FromResult(new CommandAuthorityOutcome(true, NodeId.Null));
        }

        public ValueTask ReleaseControlAsync(CancellationToken ct = default)
        {
            ReleaseControlCallCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<RealTimeChannelOpenResult> OpenRealTimeChannelAsync(
            string channelId,
            double requestedLease,
            CancellationToken ct = default)
        {
            return ValueTask.FromResult(new RealTimeChannelOpenResult { Granted = true });
        }

        public ValueTask<bool> CloseRealTimeChannelAsync(string channelId, CancellationToken ct = default)
        {
            return ValueTask.FromResult(true);
        }

        public ValueTask<NodeId> ResolveChildAsync(NodeId root, string browseName, CancellationToken ct = default)
        {
            return ValueTask.FromResult(new NodeId(browseName, 2));
        }

        public ValueTask<IntentOperationSnapshot> ReadOperationSnapshotAsync(
            NodeId operation,
            CancellationToken ct = default)
        {
            return ValueTask.FromResult(OperationSnapshot);
        }

        public ValueTask<MissionSnapshot> ReadMissionSnapshotAsync(
            NodeId mission,
            CancellationToken ct = default)
        {
            ReadMissionSnapshotCallCount++;
            return ValueTask.FromResult(MissionSnapshot);
        }

        public ValueTask<NodeId> ReadControlOwnerAsync(CancellationToken ct = default)
        {
            return ValueTask.FromResult(NodeId.Null);
        }

        public async IAsyncEnumerable<RobotIntentDataChange> SubscribeDataChangesAsync(
            ArrayOf<NodeId> nodeIds,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            SubscribeCallCount++;
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }
}
#endif
