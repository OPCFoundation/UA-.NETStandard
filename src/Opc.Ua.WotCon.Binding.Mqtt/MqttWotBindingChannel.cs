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
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Exceptions;
using MQTTnet.Protocol;

namespace Opc.Ua.WotCon.Binding.Mqtt
{
    /// <summary>
    /// A live MQTT binding channel that publishes writes / actions and subscribes
    /// for reads / observes / events per the pinned MQTT binding, with bounded QoS,
    /// payload sizes and read timeouts.
    /// </summary>
    internal sealed class MqttWotBindingChannel : IWotBindingChannel
    {
        public MqttWotBindingChannel(
            IMqttClient client,
            WotCompiledForm form,
            WotExecutorContext context,
            MqttWotBindingOptions options)
        {
            m_client = client;
            m_form = form;
            m_context = context;
            m_options = options;
            m_topic = form.Addressing.Target;
            m_qos = ParseQos(form.Addressing.Metadata);
            m_retain = ParseBool(form.Addressing.Metadata, "retain");
            context.Codecs.TrySelect(form.Payload.ContentType, out m_codec);
            m_client.ApplicationMessageReceivedAsync += OnMessageAsync;
        }

        public WotCompiledForm Form => m_form;

        public async ValueTask<WotReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Exchange(ref m_pendingRead, completion);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(m_options.ReadTimeout);
            try
            {
                await SubscribeAsync(cancellationToken).ConfigureAwait(false);
                byte[] payload = await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                WotDecodeResult decoded = m_codec.Decode(payload, m_form.Payload);
                if (!decoded.Success)
                {
                    return new WotReadResult(
                        StatusCodes.BadDecodingError, DataValue.FromStatusCode(StatusCodes.BadDecodingError), decoded.Error);
                }
                return new WotReadResult(
                    StatusCodes.Good, new DataValue(decoded.Value, StatusCodes.Good, DateTimeUtc.Now, DateTimeUtc.Now));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new WotReadResult(
                    StatusCodes.BadTimeout, DataValue.FromStatusCode(StatusCodes.BadTimeout),
                    "Timed out waiting for an MQTT message.");
            }
            finally
            {
                Interlocked.CompareExchange(ref m_pendingRead, null, completion);
                if (!m_observing)
                {
                    await TryUnsubscribeAsync().ConfigureAwait(false);
                }
            }
        }

        public async ValueTask<WotWriteResult> WriteAsync(
            DataValue value, CancellationToken cancellationToken = default)
        {
            WotEncodeResult encoded = m_codec.Encode(value.WrappedValue, m_form.Payload);
            if (!encoded.Success)
            {
                return new WotWriteResult(StatusCodes.BadEncodingError, encoded.Error);
            }
            try
            {
                await PublishAsync(encoded.Data.ToArray(), cancellationToken).ConfigureAwait(false);
                return new WotWriteResult(StatusCodes.Good);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new WotWriteResult(StatusCodes.BadTimeout, "The MQTT publish timed out.");
            }
            catch (MqttCommunicationException ex)
            {
                return new WotWriteResult(StatusCodes.BadCommunicationError, ex.Message);
            }
        }

        public async ValueTask<WotInvokeResult> InvokeAsync(
            IReadOnlyList<Variant> inputs, CancellationToken cancellationToken = default)
        {
            byte[] payload = Array.Empty<byte>();
            if (inputs is { Count: > 0 })
            {
                WotEncodeResult encoded = m_codec.Encode(inputs[0], m_form.Payload);
                if (!encoded.Success)
                {
                    return new WotInvokeResult(StatusCodes.BadEncodingError, null, encoded.Error);
                }
                payload = encoded.Data.ToArray();
            }
            try
            {
                await PublishAsync(payload, cancellationToken).ConfigureAwait(false);
                return new WotInvokeResult(StatusCodes.Good, Array.Empty<DataValue>());
            }
            catch (MqttCommunicationException ex)
            {
                return new WotInvokeResult(StatusCodes.BadCommunicationError, null, ex.Message);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of the subscription is transferred to the caller, who disposes it.")]
        public async ValueTask<IWotSubscription> ObserveAsync(
            Action<WotNotification> onNotification, CancellationToken cancellationToken = default)
        {
            if (onNotification is null)
            {
                throw new ArgumentNullException(nameof(onNotification));
            }
            lock (m_lock)
            {
                m_handlers.Add(onNotification);
                m_observing = true;
            }
            await SubscribeAsync(cancellationToken).ConfigureAwait(false);
            return new HandlerSubscription(this, onNotification);
        }

        public ValueTask<IWotSubscription> SubscribeEventAsync(
            Action<WotNotification> onEvent, CancellationToken cancellationToken = default)
            => ObserveAsync(onEvent, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            m_client.ApplicationMessageReceivedAsync -= OnMessageAsync;
            try
            {
                await m_client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build())
                    .ConfigureAwait(false);
            }
            catch (MqttCommunicationException)
            {
                // Ignore disconnect faults during teardown.
            }
            m_client.Dispose();
        }

        private Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs args)
        {
            byte[] payload = ToArray(args.ApplicationMessage.Payload);
            TaskCompletionSource<byte[]>? pending = Interlocked.Exchange(ref m_pendingRead, null);
            pending?.TrySetResult(payload);

            Action<WotNotification>[] handlers;
            lock (m_lock)
            {
                if (m_handlers.Count == 0)
                {
                    return Task.CompletedTask;
                }
                handlers = m_handlers.ToArray();
            }
            WotDecodeResult decoded = m_codec.Decode(payload, m_form.Payload);
            if (decoded.Success)
            {
                var notification = new WotNotification(
                    new DataValue(decoded.Value, StatusCodes.Good, DateTimeUtc.Now, DateTimeUtc.Now));
                foreach (Action<WotNotification> handler in handlers)
                {
                    handler(notification);
                }
            }
            return Task.CompletedTask;
        }

        private async Task SubscribeAsync(CancellationToken cancellationToken)
        {
            MqttClientSubscribeOptions options = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(m_topic, m_qos)
                .Build();
            await m_client.SubscribeAsync(options, cancellationToken).ConfigureAwait(false);
        }

        private async Task TryUnsubscribeAsync()
        {
            try
            {
                MqttClientUnsubscribeOptions options = new MqttClientUnsubscribeOptionsBuilder()
                    .WithTopicFilter(m_topic)
                    .Build();
                await m_client.UnsubscribeAsync(options).ConfigureAwait(false);
            }
            catch (MqttCommunicationException)
            {
                // Ignore unsubscribe faults.
            }
        }

        private async Task PublishAsync(byte[] payload, CancellationToken cancellationToken)
        {
            MqttApplicationMessage message = new MqttApplicationMessageBuilder()
                .WithTopic(m_topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(m_qos)
                .WithRetainFlag(m_retain)
                .Build();
            await m_client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
        }

        private void RemoveHandler(Action<WotNotification> handler)
        {
            lock (m_lock)
            {
                m_handlers.Remove(handler);
                m_observing = m_handlers.Count > 0;
            }
        }

        private static byte[] ToArray(ReadOnlySequence<byte> payload)
            => payload.IsEmpty ? Array.Empty<byte>() : payload.ToArray();

        private static MqttQualityOfServiceLevel ParseQos(
            System.Collections.Immutable.ImmutableDictionary<string, string> metadata)
        {
            if (metadata.TryGetValue("qos", out string? value) &&
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int qos))
            {
                return qos switch
                {
                    1 => MqttQualityOfServiceLevel.AtLeastOnce,
                    2 => MqttQualityOfServiceLevel.ExactlyOnce,
                    _ => MqttQualityOfServiceLevel.AtMostOnce
                };
            }
            return MqttQualityOfServiceLevel.AtMostOnce;
        }

        private static bool ParseBool(
            System.Collections.Immutable.ImmutableDictionary<string, string> metadata, string key)
            => metadata.TryGetValue(key, out string? value) && bool.TryParse(value, out bool result) && result;

        private sealed class HandlerSubscription : IWotSubscription
        {
            public HandlerSubscription(MqttWotBindingChannel channel, Action<WotNotification> handler)
            {
                m_channel = channel;
                m_handler = handler;
            }

            public WotCompiledForm Form => m_channel.m_form;

            public async ValueTask DisposeAsync()
            {
                m_channel.RemoveHandler(m_handler);
                if (!m_channel.m_observing)
                {
                    await m_channel.TryUnsubscribeAsync().ConfigureAwait(false);
                }
            }

            private readonly MqttWotBindingChannel m_channel;
            private readonly Action<WotNotification> m_handler;
        }

        private readonly IMqttClient m_client;
        private readonly WotCompiledForm m_form;
        private readonly WotExecutorContext m_context;
        private readonly MqttWotBindingOptions m_options;
        private readonly string m_topic;
        private readonly MqttQualityOfServiceLevel m_qos;
        private readonly bool m_retain;
        private readonly IWotPayloadCodec m_codec;
        private readonly object m_lock = new object();
        private readonly List<Action<WotNotification>> m_handlers = new List<Action<WotNotification>>();
        private volatile bool m_observing;
        private TaskCompletionSource<byte[]>? m_pendingRead;
    }
}
