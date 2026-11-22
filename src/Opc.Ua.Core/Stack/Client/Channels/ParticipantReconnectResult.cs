// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// Outcome returned by an <see cref="IReconnectParticipant"/> from
    /// <see cref="IReconnectParticipant.OnReconnectAsync"/>. The
    /// <see cref="IClientChannelManager"/> uses the aggregated outcomes
    /// of all participants on a shared channel to decide what to do next.
    /// </summary>
    public enum ParticipantReconnectResult
    {
        /// <summary>
        /// The participant successfully reactivated against the
        /// reconnected channel. The channel can transition to
        /// <see cref="ChannelState.Ready"/> once all participants report
        /// <see cref="Reactivated"/>, <see cref="FatalForParticipant"/>
        /// or <see cref="RequiresSessionRecreate"/>.
        /// </summary>
        Reactivated = 0,

        /// <summary>
        /// The channel itself is fine, but this participant's
        /// server-side state was lost and the participant will
        /// independently recreate it (e.g. via CreateSession). The
        /// manager keeps the channel in <see cref="ChannelState.Ready"/>
        /// for all other participants; this participant is responsible
        /// for completing its own recreation out of band.
        /// </summary>
        RequiresSessionRecreate = 1,

        /// <summary>
        /// A transient failure occurred (channel-level, not
        /// participant-specific). The manager should retry the channel
        /// reconnect cycle according to its
        /// <see cref="IChannelReconnectPolicy"/>.
        /// </summary>
        TransientFailure = 2,

        /// <summary>
        /// The participant cannot continue (e.g. authentication failed
        /// permanently, certificate rejected). The manager should
        /// detach this participant only and continue serving other
        /// participants on the shared channel.
        /// </summary>
        FatalForParticipant = 3,

        /// <summary>
        /// A fatal error affects the channel itself; the manager should
        /// transition the channel to <see cref="ChannelState.Faulted"/>
        /// and stop retrying.
        /// </summary>
        FatalForChannel = 4
    }
}
