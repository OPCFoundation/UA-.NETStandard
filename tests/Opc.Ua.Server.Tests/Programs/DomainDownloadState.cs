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
