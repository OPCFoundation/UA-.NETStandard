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

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    internal interface IChannelEntryHost : IClientChannelManager
    {
        ILogger? Logger { get; }
        TimeProvider TimeProvider { get; }
        IChannelReconnectPolicy ReconnectPolicy { get; }
        Bindings.ITransportChannelBindings? ChannelFactory { get; }
        ApplicationConfiguration Configuration { get; }

        void OnEntryStateChanged(ChannelEntry entry, ChannelStateChange change);

        void OnEntryClosed(
            ChannelEntry entry,
            ClientChannelManager.ChannelCloseReason reason);

        (Certificate? Certificate, CertificateCollection? Chain, long Version) SnapshotClientCertificate();

        ValueTask<ITransportChannel> CreateChannelAsync(
            ConfiguredEndpoint endpoint,
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain,
            ITransportWaitingConnection? reverseConnection,
            CancellationToken ct);

        Activity? StartReconnectActivity(ChannelEntry entry);
        void CompleteReconnectActivity(
            Activity? activity,
            ChannelEntry entry,
            int attemptCount,
            string outcome,
            ServiceResult? error);
        void OnEntryReconnectFailed(
            ChannelEntry entry,
            int attempt,
            string outcome,
            ServiceResult? error);
        void OnEntryOpened(ChannelEntry entry);
        void OnEntryParticipantAttached(
            ChannelEntry entry,
            string participantId,
            int refCount,
            int participantCount);
        void OnEntryParticipantDetached(
            ChannelEntry entry,
            string participantId,
            int refCount,
            int participantCount);
        void RecordChannelOpen(ChannelEntry entry);
        void RecordChannelActiveChanged(ChannelEntry entry, long delta);
        void RecordReconnectAttempt(ChannelEntry entry, string outcome);
        void RecordReconnectDuration(
            ChannelEntry entry,
            TimeSpan duration,
            string outcome);
        void RecordGateWait(ChannelEntry entry, TimeSpan duration);
        void RemoveEntryIfPresent(ManagedChannelKey key, ChannelEntry entry);
        void CloseChannel(ITransportChannel channel);
    }
}
