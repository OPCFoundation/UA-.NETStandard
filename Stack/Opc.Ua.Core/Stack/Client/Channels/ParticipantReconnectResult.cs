/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
