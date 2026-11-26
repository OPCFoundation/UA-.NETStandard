// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.ISA95.JobControl
{
    /// <summary>
    /// Identifies the version of the interface accepting a job command.
    /// </summary>
    public enum Isa95JobControlVersion
    {
        V1 = 1,
        V2 = 2
    }

    /// <summary>
    /// The mutation a store should commit after evaluating a job command.
    /// </summary>
    public enum Isa95JobMutation
    {
        None,
        Create,
        Update,
        Remove
    }

    /// <summary>
    /// Result of evaluating a job command, without applying it to any store.
    /// </summary>
    /// <param name="Mutation">The mutation to apply, or None on rejection.</param>
    /// <param name="State">The resulting state, absent for removed or nonexistent orders.</param>
    /// <param name="ReturnStatus">The Annex B return status bits.</param>
    /// <param name="PublishStatusEvent">Whether to publish a V2 status event after commit.</param>
    public readonly record struct Isa95JobDecision(
        Isa95JobMutation Mutation,
        Isa95JobCanonicalState? State,
        ulong ReturnStatus,
        bool PublishStatusEvent);

    /// <summary>
    /// Stateless rules for evaluating Job Control order mutations.
    /// </summary>
    public static class Isa95JobControlRules
    {
        /// <summary>
        /// Evaluates a command against the stored state, or null if the order does not exist.
        /// Use the returned state unchanged when persisting a result. The legacy preparation
        /// states maintain the V1 execution workflow even when commands arrive through V2.
        /// </summary>
        public static Isa95JobDecision Evaluate(
            Isa95JobCanonicalState? current,
            Isa95JobOperation operation,
            Isa95JobControlVersion version)
        {
            bool v1 = version == Isa95JobControlVersion.V1;
            if (version is not (Isa95JobControlVersion.V1 or Isa95JobControlVersion.V2) ||
                operation is < Isa95JobOperation.Store or > Isa95JobOperation.MarkNotReady ||
                (v1 && operation is not (Isa95JobOperation.Store or Isa95JobOperation.StoreAndStart or
                    Isa95JobOperation.Start or Isa95JobOperation.Update or Isa95JobOperation.Stop or
                    Isa95JobOperation.StopAndRemove or Isa95JobOperation.Cancel or Isa95JobOperation.Clear)) ||
                (!v1 && operation == Isa95JobOperation.StopAndRemove))
            {
                return Reject(current, v1
                    ? Isa95JobReturnStatusBits.InvalidCommand
                    : Isa95JobReturnStatusBits.InvalidRequest);
            }
            if (operation is Isa95JobOperation.Store or Isa95JobOperation.StoreAndStart)
            {
                return current.HasValue
                    ? Reject(current, Isa95JobReturnStatusBits.UnableToAccept)
                    : new Isa95JobDecision(
                        Isa95JobMutation.Create,
                        Isa95V2StateMachine.Prepared(operation == Isa95JobOperation.StoreAndStart, v1 ? 0u : 1u),
                        Isa95JobReturnStatusBits.Success,
                        true);
            }
            if (!current.HasValue)
            {
                return Reject(null, Isa95JobReturnStatusBits.UnknownJobOrderId);
            }

            Isa95JobCanonicalState state = current.Value;
            ulong invalid = v1 ? Isa95JobReturnStatusBits.InvalidCommand : Isa95JobReturnStatusBits.InvalidStatus;
            if (state is < Isa95JobCanonicalState.NotAllowedToStart or > Isa95JobCanonicalState.AllowedToStartLoaded)
            {
                return Reject(state, invalid);
            }
            (uint parent, _) = Isa95V2StateMachine.TopLevel(state);
            bool beforeStart = (parent is Isa95V2StateMachine.NotAllowedToStartNumber
                or Isa95V2StateMachine.AllowedToStartNumber) && state != Isa95JobCanonicalState.Loaded;
            bool active = parent is Isa95V2StateMachine.RunningNumber or Isa95V2StateMachine.InterruptedNumber;
            uint preparation = Isa95V2StateMachine.PreparationNumber(state);
            switch (operation)
            {
                case Isa95JobOperation.Stop when v1:
                case Isa95JobOperation.StopAndRemove:
                    return active ? Remove() : Reject(state, invalid);
                case Isa95JobOperation.Cancel:
                    return beforeStart ? Remove() : Reject(state, invalid);
                case Isa95JobOperation.Clear:
                    return state is Isa95JobCanonicalState.Completed or Isa95JobCanonicalState.Closed
                        or Isa95JobCanonicalState.Aborted ? Remove() : Reject(state, invalid);
                case Isa95JobOperation.Start when beforeStart &&
                    parent == Isa95V2StateMachine.NotAllowedToStartNumber:
                    return Move(state, Isa95V2StateMachine.Prepared(true, preparation), V2.BrowseNames.Start);
                case Isa95JobOperation.RevokeStart when parent == Isa95V2StateMachine.AllowedToStartNumber:
                    return Move(state, Isa95V2StateMachine.Prepared(false, preparation), V2.BrowseNames.RevokeStart);
                case Isa95JobOperation.Update when beforeStart:
                    return Move(state, state, V2.BrowseNames.Update);
                case Isa95JobOperation.Stop when active:
                    return Move(state, Isa95JobCanonicalState.Completed, V2.BrowseNames.Stop);
                case Isa95JobOperation.Pause when state == Isa95JobCanonicalState.Running:
                    return Move(state, Isa95JobCanonicalState.Suspended, V2.BrowseNames.Pause);
                case Isa95JobOperation.Resume when parent == Isa95V2StateMachine.InterruptedNumber:
                    return Move(state, Isa95JobCanonicalState.Running, V2.BrowseNames.Resume);
                case Isa95JobOperation.Abort when beforeStart || active:
                    return Move(state, Isa95JobCanonicalState.Aborted, V2.BrowseNames.Abort);
                case Isa95JobOperation.BeginExecution when parent == Isa95V2StateMachine.AllowedToStartNumber &&
                    preparation != 1:
                    return Move(state, Isa95JobCanonicalState.Running);
                case Isa95JobOperation.Hold when state == Isa95JobCanonicalState.Running:
                    return Move(state, Isa95JobCanonicalState.Held);
                case Isa95JobOperation.Complete when active:
                    return Move(state, Isa95JobCanonicalState.Completed);
                case Isa95JobOperation.Close when state == Isa95JobCanonicalState.Completed:
                    return Move(state, Isa95JobCanonicalState.Closed, machine: Isa95V2Machine.Ended, from: 1, to: 2);
                case Isa95JobOperation.MarkReady or Isa95JobOperation.Load or Isa95JobOperation.Unload
                    or Isa95JobOperation.MarkNotReady when beforeStart && preparation != 0:
                    uint target = operation switch
                    {
                        Isa95JobOperation.MarkReady when preparation == 1 => 2,
                        Isa95JobOperation.Load when preparation == 2 => 3,
                        Isa95JobOperation.Unload when preparation == 3 => 2,
                        Isa95JobOperation.MarkNotReady when preparation is 2 or 3 => 1,
                        _ => 0
                    };
                    return target == 0 ? Reject(state, invalid) : Move(
                        state,
                        Isa95V2StateMachine.Prepared(parent == Isa95V2StateMachine.AllowedToStartNumber, target),
                        machine: Isa95V2Machine.Prepare,
                        from: preparation,
                        to: target);
                default:
                    return Reject(state, invalid);
            }
        }

        /// <summary>
        /// Checks that a V1 response request specifies exactly one valid selector.
        /// </summary>
        public static ulong ValidateV1ResponseSelector(string? jobOrderId, V1.ISA95JobOrderStateEnum state)
        {
            if (state is < V1.ISA95JobOrderStateEnum.Undefined or > V1.ISA95JobOrderStateEnum.Error)
            {
                return Isa95JobReturnStatusBits.InvalidStatus;
            }
            return string.IsNullOrEmpty(jobOrderId) == (state == V1.ISA95JobOrderStateEnum.Undefined)
                ? Isa95JobReturnStatusBits.InvalidRequest
                : Isa95JobReturnStatusBits.Success;
        }

        private static Isa95JobDecision Move(
            Isa95JobCanonicalState state,
            Isa95JobCanonicalState next,
            string? cause = null,
            Isa95V2Machine machine = Isa95V2Machine.Receiver,
            uint from = 0,
            uint to = 0)
        {
            if (machine == Isa95V2Machine.Receiver)
            {
                from = Isa95V2StateMachine.TopLevel(state).Number;
                to = Isa95V2StateMachine.TopLevel(next).Number;
            }
            if (!Isa95V2TransitionTable.TryGet(machine, from, to, out Isa95V2Transition edge) ||
                (cause != null && edge.Cause != cause))
            {
                return Reject(state, Isa95JobReturnStatusBits.InvalidStatus);
            }
            return new Isa95JobDecision(Isa95JobMutation.Update, next, Isa95JobReturnStatusBits.Success,
                edge.HasStatusEffect);
        }

        private static Isa95JobDecision Remove()
        {
            return new Isa95JobDecision(Isa95JobMutation.Remove, null, Isa95JobReturnStatusBits.Success, false);
        }

        private static Isa95JobDecision Reject(Isa95JobCanonicalState? state, ulong status)
        {
            return new Isa95JobDecision(Isa95JobMutation.None, state, status, false);
        }
    }
}
