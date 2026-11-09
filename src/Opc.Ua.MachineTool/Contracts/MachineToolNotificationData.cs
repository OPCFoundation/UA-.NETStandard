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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.MachineTool
{
    /// <summary>
    /// Controls only methods already declared by the standard alert/condition types.
    /// Server role and user permissions still apply independently.
    /// </summary>
    [Flags]
    public enum MachineToolConditionOperations
    {
        /// <summary>
        /// Allows passive observation only.
        /// </summary>
        None = 0,

        /// <summary>
        /// Allows the standard Enable method.
        /// </summary>
        Enable = 1,

        /// <summary>
        /// Allows the standard Disable method.
        /// </summary>
        Disable = 2,

        /// <summary>
        /// Allows the standard AddComment method.
        /// </summary>
        Comment = 4,

        /// <summary>
        /// Allows the standard Acknowledge method, never a machine reset or error-clear command.
        /// </summary>
        Acknowledge = 8,

        /// <summary>
        /// Allows all four supported standard condition operations.
        /// </summary>
        All = Enable | Disable | Comment | Acknowledge
    }

    /// <summary>
    /// Reports one operator-visible error or warning with an active and inactive state.
    /// Acknowledgement and enabling are standard condition operations, not equipment commands.
    /// </summary>
    public sealed record MachineToolAlert
    {
        /// <summary>
        /// Gets the stable, per-machine identity of the condition.
        /// </summary>
        public required string Key { get; init; }

        /// <summary>
        /// Gets the manufacturer's error or warning code, as specified by AlertType.
        /// </summary>
        public required string ErrorCode { get; init; }

        /// <summary>
        /// Gets the message shown to the operator.
        /// </summary>
        public required LocalizedText Message { get; init; }

        /// <summary>
        /// Gets severity: warnings use 334 through 666 and errors use 667 through 1000.
        /// </summary>
        public ushort Severity { get; init; } = 667;

        /// <summary>
        /// Gets whether the underlying error or warning is currently active.
        /// </summary>
        public bool IsActive { get; init; }

        /// <summary>
        /// Gets the most appropriate standard or application condition-class identifier.
        /// A null identifier selects BaseConditionClassType.
        /// </summary>
        public NodeId ConditionClassId { get; init; }

        /// <summary>
        /// Gets the provider's permitted standard condition operations.
        /// Changing these permissions does not itself change the condition's enabled or acknowledged state.
        /// </summary>
        public MachineToolConditionOperations AllowedOperations { get; init; } = MachineToolConditionOperations.All;

        /// <summary>
        /// Gets the acquisition time and quality, including unavailable or uncertain observations.
        /// </summary>
        public MachineToolSampleInfo Sample { get; init; } = MachineToolSampleInfo.Unavailable;
    }

    /// <summary>
    /// Reports an informational notification without an active/inactive state.
    /// </summary>
    public sealed record MachineToolMessage
    {
        /// <summary>
        /// Gets a consecutive per-provider sequence starting at one; a gap is an error, not silent data loss.
        /// </summary>
        public required ulong Sequence { get; init; }

        /// <summary>
        /// Gets the manufacturer's notification code.
        /// </summary>
        public required string Identifier { get; init; }

        /// <summary>
        /// Gets the operator-visible message.
        /// </summary>
        public required LocalizedText Message { get; init; }

        /// <summary>
        /// Gets the specified informational severity, from one through 333.
        /// </summary>
        public ushort Severity { get; init; } = 100;

        /// <summary>
        /// Gets the actual occurrence time.
        /// </summary>
        public required DateTimeUtc Time { get; init; }
    }

    /// <summary>
    /// Supplies a complete bounded alert inventory and the messages after the requested sequence.
    /// Inactive unacknowledged alerts remain in the inventory until their standard condition is no longer retained.
    /// </summary>
    public sealed record MachineToolNotificationSnapshot
    {
        /// <summary>
        /// Gets the monotonically increasing observation revision.
        /// </summary>
        public ulong Revision { get; init; }

        /// <summary>
        /// Gets all operator-visible conditions, including inactive conditions awaiting acknowledgement.
        /// </summary>
        public ArrayOf<MachineToolAlert> Alerts { get; init; }

        /// <summary>
        /// Gets consecutive messages after the requested sequence, without replaying previously delivered messages.
        /// </summary>
        public ArrayOf<MachineToolMessage> Messages { get; init; }
    }

    /// <summary>
    /// Supplies the complete operator-visible notification stream for one machine.
    /// Implementations remain application-owned and must not command equipment when reading.
    /// </summary>
    public interface IMachineToolNotificationProvider
    {
        /// <summary>
        /// Reads current alerts and the next bounded batch of messages after the accepted sequence.
        /// </summary>
        /// <param name="afterSequence">The last accepted message sequence, initially zero.</param>
        /// <param name="cancellationToken">Cancels acquisition without changing published conditions.</param>
        ValueTask<MachineToolNotificationSnapshot> ReadAsync(
            ulong afterSequence, CancellationToken cancellationToken = default);
    }
}
