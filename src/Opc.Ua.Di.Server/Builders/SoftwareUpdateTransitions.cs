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

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Serialises the method-driven state changes of one <c>SoftwareUpdate</c> instance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OPC 10000-100 §8.4.8 allows <c>Prepare</c> only from <c>Idle</c>,
    /// <c>Abort</c> only from <c>Preparing</c> and <c>Resume</c> only from
    /// <c>PreparedForUpdate</c> while no installation runs; §8.4.9 allows
    /// <c>Installation.Resume</c> only from <c>Error</c>.
    /// Each check of the current state and the move that follows it happen
    /// under one lock, so two concurrent calls cannot both pass the check.
    /// </para>
    /// <para>
    /// <c>Prepare</c> runs the application's handler inside the method call.
    /// The preparation in flight is tracked here so that <c>Abort</c>, which
    /// exists for a preparation that "takes too long or does not complete at
    /// all", can cancel it. The <see cref="Preparation"/> is disposed only
    /// once both the Prepare call and a racing Abort are done with it.
    /// </para>
    /// </remarks>
    internal sealed class SoftwareUpdateTransitions
    {
        /// <summary>
        /// Creates the transitions of one <c>SoftwareUpdate</c> instance.
        /// </summary>
        /// <param name="diNamespaceIndex">The DI namespace index.</param>
        /// <param name="context">The system context the state is written with.</param>
        public SoftwareUpdateTransitions(ushort diNamespaceIndex, ISystemContext context)
        {
            m_diNamespaceIndex = diNamespaceIndex;
            m_context = context;
        }

        /// <summary>
        /// Moves <c>PrepareForUpdate</c> from <c>Idle</c> to <c>Preparing</c>.
        /// </summary>
        /// <param name="machine">The PrepareForUpdate state machine.</param>
        /// <param name="cancellationToken">The token of the Prepare call.</param>
        /// <returns>
        /// The preparation <c>Abort</c> cancels, which the caller releases
        /// after <see cref="TryEndPrepare"/>, or <see langword="null"/> when
        /// the machine is not <c>Idle</c>.
        /// </returns>
        public Preparation? TryBeginPrepare(
            PrepareForUpdateStateMachineState machine,
            CancellationToken cancellationToken)
        {
            lock (m_lock)
            {
                if (!IsIn(machine, SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_Idle))
                {
                    return null;
                }
                Move(
                    machine,
                    SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_Preparing,
                    SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_IdleToPreparing);
                m_preparation = new Preparation(cancellationToken);
                return m_preparation;
            }
        }

        /// <summary>
        /// Ends a preparation <c>Abort</c> has not ended, moving to
        /// <c>PreparedForUpdate</c> or back to <c>Idle</c>.
        /// </summary>
        /// <param name="machine">The PrepareForUpdate state machine.</param>
        /// <param name="preparation">The preparation returned by <see cref="TryBeginPrepare"/>.</param>
        /// <param name="succeeded">Whether the preparation completed.</param>
        /// <returns>
        /// <see langword="false"/> when <c>Abort</c> ended the preparation first.
        /// </returns>
        public bool TryEndPrepare(
            PrepareForUpdateStateMachineState machine,
            Preparation preparation,
            bool succeeded)
        {
            lock (m_lock)
            {
                if (!ReferenceEquals(m_preparation, preparation))
                {
                    return false;
                }
                m_preparation = null;
                if (succeeded)
                {
                    Move(
                        machine,
                        SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_PreparedForUpdate,
                        SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_PreparingToPreparedForUpdate);
                }
                else
                {
                    Move(
                        machine,
                        SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_Idle,
                        SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_PreparingToIdle);
                }
                return true;
            }
        }

        /// <summary>
        /// Moves <c>PrepareForUpdate</c> from <c>Preparing</c> back to
        /// <c>Idle</c> and cancels the preparation in flight.
        /// </summary>
        /// <remarks>
        /// §8.4.8.4 also allows <c>Abort</c> from <c>Resuming</c>, but
        /// <see cref="TryResume"/> leaves that state in the step that enters
        /// it, so a client never finds the machine there. The preparation is
        /// cancelled outside the lock, because its token callbacks and the
        /// continuation of the Prepare call can run inline.
        /// </remarks>
        /// <param name="machine">The PrepareForUpdate state machine.</param>
        /// <returns>
        /// <see langword="false"/> when the machine is not <c>Preparing</c>.
        /// </returns>
        public bool TryAbort(PrepareForUpdateStateMachineState machine)
        {
            Preparation? preparation;
            lock (m_lock)
            {
                if (!IsIn(machine, SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_Preparing))
                {
                    return false;
                }
                Move(
                    machine,
                    SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_Idle,
                    SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_PreparingToIdle);
                preparation = m_preparation;
                m_preparation = null;

                // The Prepare call cannot have released the preparation yet:
                // it releases only after TryEndPrepare, which needs this lock.
                preparation?.Hold();
            }
            if (preparation != null)
            {
                try
                {
                    preparation.Cancel();
                }
                finally
                {
                    preparation.Release();
                }
            }
            return true;
        }

        /// <summary>
        /// Moves <c>PrepareForUpdate</c> from <c>PreparedForUpdate</c> through
        /// <c>Resuming</c> back to <c>Idle</c>.
        /// </summary>
        /// <remarks>
        /// No application work is attached to resuming, so the automatic
        /// transition to <c>Idle</c> (§8.4.8.5) follows at once.
        /// </remarks>
        /// <param name="machine">The PrepareForUpdate state machine.</param>
        /// <param name="installation">The Installation state machine, if present.</param>
        /// <returns>
        /// <see langword="false"/> when the machine is not
        /// <c>PreparedForUpdate</c> or an installation is running.
        /// </returns>
        public bool TryResume(
            PrepareForUpdateStateMachineState machine,
            InstallationStateMachineState? installation)
        {
            lock (m_lock)
            {
                if (!IsIn(machine, SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_PreparedForUpdate) ||
                    (installation != null &&
                        IsIn(installation, SoftwareUpdateStateMachineDispatcher.Installation_Installing)))
                {
                    return false;
                }
                Move(
                    machine,
                    SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_Resuming,
                    SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_PreparedForUpdateToResuming);
                Move(
                    machine,
                    SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_Idle,
                    SoftwareUpdateStateMachineDispatcher.PrepareForUpdate_ResumingToIdle);
                return true;
            }
        }

        /// <summary>
        /// Moves <c>Installation</c> from <c>Error</c> back to <c>Idle</c>.
        /// </summary>
        /// <param name="machine">The Installation state machine.</param>
        /// <returns>
        /// <see langword="false"/> when the machine is not in <c>Error</c>.
        /// </returns>
        public bool TryResumeInstallation(InstallationStateMachineState machine)
        {
            lock (m_lock)
            {
                if (!IsIn(machine, SoftwareUpdateStateMachineDispatcher.Installation_Error))
                {
                    return false;
                }
                Move(
                    machine,
                    SoftwareUpdateStateMachineDispatcher.Installation_Idle,
                    SoftwareUpdateStateMachineDispatcher.Installation_ErrorToIdle);
                return true;
            }
        }

        private bool IsIn(FiniteStateMachineState machine, uint stateId)
        {
            return SoftwareUpdateStateMachineDispatcher.TryGetCurrentState(
                    machine, m_diNamespaceIndex, out uint current) &&
                current == stateId;
        }

        private void Move(FiniteStateMachineState machine, uint toStateId, uint transitionId)
        {
            SoftwareUpdateStateMachineDispatcher.Move(
                machine, toStateId, transitionId, m_diNamespaceIndex, m_context);
        }

        private readonly Lock m_lock = new();
        private readonly ushort m_diNamespaceIndex;
        private readonly ISystemContext m_context;
        private Preparation? m_preparation;

        /// <summary>
        /// A preparation in flight and the token <c>Abort</c> cancels.
        /// </summary>
        /// <remarks>
        /// The Prepare call holds the preparation from the start and a racing
        /// <c>Abort</c> holds it while it cancels; the token source is
        /// disposed when the last holder releases it, so neither side can
        /// see it disposed while it still uses it.
        /// </remarks>
        internal sealed class Preparation
        {
            /// <summary>
            /// Creates a preparation held by the Prepare call.
            /// </summary>
            /// <param name="cancellationToken">The token of the Prepare call.</param>
            public Preparation(CancellationToken cancellationToken)
            {
                m_source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }

            /// <summary>
            /// Gets the token the application's Prepare handler observes.
            /// </summary>
            public CancellationToken Token => m_source.Token;

            /// <summary>
            /// Adds a holder.
            /// </summary>
            public void Hold()
            {
                Interlocked.Increment(ref m_holders);
            }

            /// <summary>
            /// Cancels the preparation.
            /// </summary>
            public void Cancel()
            {
                m_source.Cancel();
            }

            /// <summary>
            /// Removes a holder and disposes the token source after the last one.
            /// </summary>
            public void Release()
            {
                if (Interlocked.Decrement(ref m_holders) == 0)
                {
                    m_source.Dispose();
                }
            }

            private readonly CancellationTokenSource m_source;
            private int m_holders = 1;
        }
    }
}
