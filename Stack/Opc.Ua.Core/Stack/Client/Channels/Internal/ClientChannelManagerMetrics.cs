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
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using ChannelCloseReason = Opc.Ua.ClientChannelManager.ChannelCloseReason;

namespace Opc.Ua
{
    internal sealed class ClientChannelManagerMetrics
    {
        public ClientChannelManagerMetrics(ClientChannelManager owner, Meter meter)
        {
            m_owner = owner;
            m_channelOpen = meter.CreateCounter<long>(
                "opcua.channel.open",
                description: "Number of OPC UA client transport channels opened.");
            m_channelClose = meter.CreateCounter<long>(
                "opcua.channel.close",
                description: "Number of OPC UA client transport channels closed.");
            m_channelActive = meter.CreateUpDownCounter<long>(
                "opcua.channel.active",
                description: "Current number of active OPC UA client channel entries.");
            m_reconnectAttempts = meter.CreateCounter<long>(
                "opcua.channel.reconnect.attempts",
                description: "Number of OPC UA client channel reconnect attempts.");
            m_reconnectDuration = meter.CreateHistogram<double>(
                "opcua.channel.reconnect.duration",
                unit: "ms",
                description: "Duration of OPC UA client channel reconnect cycles.");
            m_gateWait = meter.CreateHistogram<double>(
                "opcua.channel.gate.wait",
                unit: "ms",
                description: "Time spent waiting for an OPC UA client channel ready gate.");
            m_participantTimeout = meter.CreateCounter<long>(
                "opcua.channel.participant.timeout.count",
                description: "Number of OPC UA client reconnect participant callbacks that timed out.");
            m_participantRecreate = meter.CreateCounter<long>(
                "opcua.channel.participant.recreate.count",
                description: "Number of OPC UA client reconnect participant recreate callbacks.");
            m_refCountGauge = meter.CreateObservableGauge<long>(
                "opcua.channel.refcount",
                ObserveRefCounts,
                description: "Current reference count for OPC UA client channel entries.");
            m_participantGauge = meter.CreateObservableGauge<long>(
                "opcua.channel.participants",
                ObserveParticipants,
                description: "Current participant count for OPC UA client channel entries.");
        }

        public void RecordChannelOpen(ChannelEntry entry)
        {
            m_channelOpen.Add(1, CreateEndpointReverseTags(entry));
        }

        public void RecordChannelClosed(ChannelEntry entry, ChannelCloseReason reason)
        {
            m_channelClose.Add(1, CreateEndpointReverseReasonTags(entry, reason));
        }

        public void RecordChannelActiveChanged(ChannelEntry entry, long delta)
        {
            m_channelActive.Add(delta, CreateEndpointTags(entry));
        }

        public void RecordReconnectAttempt(ChannelEntry entry, string outcome)
        {
            m_reconnectAttempts.Add(1, CreateEndpointOutcomeTags(entry, outcome));
        }

        public void RecordReconnectDuration(ChannelEntry entry, TimeSpan duration, string outcome)
        {
            m_reconnectDuration.Record(
                duration.TotalMilliseconds,
                CreateEndpointOutcomeTags(entry, outcome));
        }

        public void RecordGateWait(ChannelEntry entry, TimeSpan duration)
        {
            m_gateWait.Record(duration.TotalMilliseconds, CreateEndpointTags(entry));
        }

        public void RecordParticipantTimeout(ChannelEntry entry, string participantId)
        {
            m_participantTimeout.Add(1, CreateEndpointParticipantTags(entry, participantId));
        }

        public void RecordParticipantRecreate(ChannelEntry entry, string participantId, bool success)
        {
            m_participantRecreate.Add(1, CreateEndpointParticipantSuccessTags(entry, participantId, success));
        }

        private IEnumerable<Measurement<long>> ObserveRefCounts()
        {
            foreach (ChannelEntry entry in m_owner.GetMetricEntriesSnapshot())
            {
                yield return new Measurement<long>(entry.RefCount, CreateEndpointTagsArray(entry));
            }
        }

        private IEnumerable<Measurement<long>> ObserveParticipants()
        {
            foreach (ChannelEntry entry in m_owner.GetMetricEntriesSnapshot())
            {
                yield return new Measurement<long>(entry.ParticipantCount, CreateEndpointTagsArray(entry));
            }
        }

        private static TagList CreateEndpointTags(ChannelEntry entry)
        {
            return new TagList(new KeyValuePair<string, object?>("endpoint", entry.EndpointUrl));
        }

        private static TagList CreateEndpointReverseTags(ChannelEntry entry)
        {
            return new TagList(
                new KeyValuePair<string, object?>("endpoint", entry.EndpointUrl),
                new KeyValuePair<string, object?>("reverse", entry.IsReverse));
        }

        private static TagList CreateEndpointReverseReasonTags(
            ChannelEntry entry,
            ChannelCloseReason reason)
        {
            return new TagList(
                new KeyValuePair<string, object?>("endpoint", entry.EndpointUrl),
                new KeyValuePair<string, object?>("reverse", entry.IsReverse),
                new KeyValuePair<string, object?>("reason", GetCloseReason(reason)));
        }

        private static TagList CreateEndpointOutcomeTags(ChannelEntry entry, string outcome)
        {
            return new TagList(
                new KeyValuePair<string, object?>("endpoint", entry.EndpointUrl),
                new KeyValuePair<string, object?>("outcome", outcome));
        }

        private static TagList CreateEndpointParticipantTags(ChannelEntry entry, string participantId)
        {
            return new TagList(
                new KeyValuePair<string, object?>("endpoint", entry.EndpointUrl),
                new KeyValuePair<string, object?>("participant", participantId));
        }

        private static TagList CreateEndpointParticipantSuccessTags(
            ChannelEntry entry,
            string participantId,
            bool success)
        {
            return new TagList(
                new KeyValuePair<string, object?>("endpoint", entry.EndpointUrl),
                new KeyValuePair<string, object?>("participant", participantId),
                new KeyValuePair<string, object?>("success", success));
        }

        private static KeyValuePair<string, object?>[] CreateEndpointTagsArray(ChannelEntry entry)
        {
            return [new KeyValuePair<string, object?>("endpoint", entry.EndpointUrl)];
        }

        private static string GetCloseReason(ChannelCloseReason reason)
        {
            return reason switch
            {
                ChannelCloseReason.LeaseReleased => "lease-released",
                ChannelCloseReason.ManagerDisposed => "manager-disposed",
                ChannelCloseReason.Faulted => "faulted",
                _ => "faulted"
            };
        }

        private readonly ClientChannelManager m_owner;
        private readonly Counter<long> m_channelOpen;
        private readonly Counter<long> m_channelClose;
        private readonly UpDownCounter<long> m_channelActive;
        private readonly Counter<long> m_reconnectAttempts;
        private readonly Histogram<double> m_reconnectDuration;
        private readonly Histogram<double> m_gateWait;
        private readonly Counter<long> m_participantTimeout;
        private readonly Counter<long> m_participantRecreate;
        private readonly ObservableGauge<long> m_refCountGauge;
        private readonly ObservableGauge<long> m_participantGauge;
    }
}
