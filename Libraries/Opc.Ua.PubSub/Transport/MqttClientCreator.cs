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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MQTTnet;
#if !NET8_0_OR_GREATER
using MQTTnet.Client;
#endif

namespace Opc.Ua.PubSub.Transport
{
    internal static class MqttClientCreator
    {
#if !NET8_0_OR_GREATER
        private static readonly Lazy<MqttFactory> s_mqttClientFactory = new(
            () => new MqttFactory());
#else
        private static readonly Lazy<MqttClientFactory> s_mqttClientFactory = new(
            () => new MqttClientFactory());
#endif

        /// <summary>
        /// The method which returns an MQTT client
        /// </summary>
        /// <param name="reconnectInterval">Number of seconds to reconnect to the MQTT broker</param>
        /// <param name="mqttClientOptions">The client options for MQTT broker connection</param>
        /// <param name="receiveMessageHandler">The receiver message handler</param>
        /// <param name="logger">A contextual logger to log to</param>
        /// <param name="topicFilter">The topics to which to subscribe</param>
        /// <param name="ct"></param>
        internal static async Task<IMqttClient> GetMqttClientAsync(
            int reconnectInterval,
            MqttClientOptions mqttClientOptions,
            Func<MqttApplicationMessageReceivedEventArgs, Task> receiveMessageHandler,
            ILogger logger,
            StringCollection topicFilter = null,
            CancellationToken ct = default)
        {
            IMqttClient mqttClient = s_mqttClientFactory.Value.CreateMqttClient();

            // Hook the receiveMessageHandler in case we deal with a subscriber
            if ((receiveMessageHandler != null) && (topicFilter != null))
            {
                mqttClient.ApplicationMessageReceivedAsync += receiveMessageHandler;
                mqttClient.ConnectedAsync += async _ =>
                {
                    logger.LogInformation("{ClientId} Connected to MQTTBroker", mqttClient?.Options?.ClientId);

                    try
                    {
                        foreach (string topic in topicFilter)
                        {
                            // subscribe to provided topics, messages are also filtered on the receiveMessageHandler
                            await mqttClient.SubscribeAsync(topic).ConfigureAwait(false);
                        }

                        logger.LogInformation(
                            "{ClientId} Subscribed to topics: {Topics}",
                            mqttClient?.Options?.ClientId,
                            string.Join(",", topicFilter));
                    }
                    catch (Exception exception)
                    {
                        logger.LogError(
                            exception,
                            "{ClientId} could not subscribe to topics: {Topics}",
                            mqttClient?.Options?.ClientId,
                            string.Join(",", topicFilter));
                    }
                };
            }
            else
            {
                if (receiveMessageHandler == null)
                {
                    logger.LogInformation(
                        "The provided MQTT message handler is null therefore messages will not be processed on client {ClientId}!!!",
                        mqttClient?.Options?.ClientId);
                }
                if (topicFilter == null)
                {
                    logger.LogInformation(
                        "The provided MQTT message topic filter is null therefore messages will not be processed on client {ClientId}!!!",
                        mqttClient?.Options?.ClientId);
                }
            }

            // Setup reconnect handler
            mqttClient.DisconnectedAsync += async e =>
            {
                await Task.Delay(TimeSpan.FromSeconds(reconnectInterval)).ConfigureAwait(false);
                try
                {
                    logger.LogInformation(
                        "Disconnect Handler called on client {ClientId}, reason: {Reason} was connected: {ClientWasConnected}",
                        mqttClient?.Options?.ClientId,
                        e.Reason,
                        e.ClientWasConnected);
                    await ConnectAsync(reconnectInterval, mqttClientOptions, mqttClient, logger, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception excOnDisconnect)
                {
                    logger.LogError(
                        "{ClientId} Failed to reconnect after disconnect occurred: {Message}",
                        mqttClient?.Options?.ClientId,
                        excOnDisconnect.Message);
                }
            };

            await ConnectAsync(reconnectInterval, mqttClientOptions, mqttClient, logger, ct)
                .ConfigureAwait(false);

            return mqttClient;
        }

        /// <summary>
        /// Perform the connection to the MQTTBroker
        /// </summary>
        private static async Task ConnectAsync(
            int reconnectInterval,
            MqttClientOptions mqttClientOptions,
            IMqttClient mqttClient,
            ILogger logger,
            CancellationToken ct = default)
        {
            try
            {
                MqttClientConnectResult result = await mqttClient
                    .ConnectAsync(mqttClientOptions, ct)
                    .ConfigureAwait(false);
                if (MqttClientConnectResultCode.Success == result.ResultCode)
                {
                    logger.LogInformation(
                        "MQTT client {ClientId} successfully connected",
                        mqttClient?.Options?.ClientId);
                }
                else
                {
                    logger.LogInformation(
                        "MQTT client {ClientId} connect attempt returned {ResultCode}",
                        mqttClient?.Options?.ClientId,
                        result?.ResultCode);
                }
            }
            catch (Exception e)
            {
                logger.LogError(
                    "MQTT client {ClientId} connect attempt returned {Message} will try to reconnect in {ReconnectInterval} seconds",
                    mqttClient?.Options?.ClientId,
                    e.Message,
                    reconnectInterval);
            }
        }
    }
}
