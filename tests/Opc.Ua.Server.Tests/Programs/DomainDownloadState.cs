// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using Opc.Ua.Server.StateMachines;

namespace Opc.Ua.TestModels.DomainDownload
{
    public partial class DomainDownloadState
    {
        /// <inheritdoc/>
        protected override void OnAfterCreate(ISystemContext context, NodeState node, CancellationToken ct = default)
        {
            base.OnAfterCreate(context, node, ct);
            StateMachineBuilder.For(this, context).BindSubStateMachine(
                global::Opc.Ua.Objects.ProgramStateMachineType_Running,
                StateMachineBuilder.For(TransferStateMachine!, context),
                Objects.TransferStateMachineType_Opening,
                preserveOnReentry: true);
        }
    }

    public partial class TransferStateMachineState
    {
        /// <inheritdoc/>
        protected override ElementInfo[] StateTable => s_states;

        /// <inheritdoc/>
        protected override ElementInfo[] TransitionTable => s_transitions;

        /// <inheritdoc/>
        protected override uint[,] TransitionMappings => s_mappings;

        private static readonly ElementInfo[] s_states =
        [
            new(Objects.TransferStateMachineType_Opening, "Opening", 1),
            new(Objects.TransferStateMachineType_Sending, "Sending", 2),
            new(Objects.TransferStateMachineType_Closing, "Closing", 3)
        ];

        private static readonly ElementInfo[] s_transitions =
        [
            new(Objects.TransferStateMachineType_OpeningToSending, "OpeningToSending", 10),
            new(Objects.TransferStateMachineType_SendingToSending, "SendingToSending", 11),
            new(Objects.TransferStateMachineType_SendingToClosing, "SendingToClosing", 12)
        ];

        private static readonly uint[,] s_mappings =
        {
            {
                Objects.TransferStateMachineType_OpeningToSending, Objects.TransferStateMachineType_Opening,
                Objects.TransferStateMachineType_Sending, 1
            },
            {
                Objects.TransferStateMachineType_SendingToSending, Objects.TransferStateMachineType_Sending,
                Objects.TransferStateMachineType_Sending, 1
            },
            {
                Objects.TransferStateMachineType_SendingToClosing, Objects.TransferStateMachineType_Sending,
                Objects.TransferStateMachineType_Closing, 1
            }
        };
    }
}
