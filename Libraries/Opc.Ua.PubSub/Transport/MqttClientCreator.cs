/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Connecting;
using MQTTnet.Client.Options;
using MQTTnet.Exceptions;

namespace Opc.Ua.PubSub.Mqtt
{
    internal class MqttClientCreator
    {


        #region Private
        private static readonly Lazy<MqttFactory> mqttClientFactory = new Lazy<MqttFactory>(() => new MqttFactory());
        #endregion

        /// <summary>
        /// The method which returns an MQTT client
        /// </summary>
        /// <param name="reconnectInterval">Number of seconds to reconnect atter to the MQTT broker</param>
        /// <param name="mqttClientOptions">The client options for MQTT broker connection</param>
        /// <param name="receiveMessageHandler">The receiver message handler</param>
        /// <returns></returns>
        internal static async Task<IMqttClient> GetMqttClientAsync(int reconnectInterval,
                                                                   IMqttClientOptions mqttClientOptions,
                                                                   Action<MqttApplicationMessageReceivedEventArgs> receiveMessageHandler)
        {

            IMqttClient mqttClient = mqttClientFactory.Value.CreateMqttClient();


            async void Connect()
            {
                try
                {
                    var result = await mqttClient.ConnectAsync(mqttClientOptions, CancellationToken.None);
                    if (MqttClientConnectResultCode.Success == result.ResultCode)
                    {
                        Utils.Trace("MQTT client {0} successfully connected", mqttClient?.Options?.ClientId);
                    }
                    else
                    {
                        Utils.Trace("MQTT client {0} connect atempt returned {0}", mqttClient?.Options?.ClientId, result?.ResultCode);
                    }
                }
                catch (Exception e) when (e is MqttCommunicationException)
                {
                    Utils.Trace("MQTT client {0} connect atempt returned {1} will try to reconnect in {2} seconds",
                        mqttClient?.Options?.ClientId,
                        e.Message,
                        reconnectInterval);
                }
            }

            // Hook the receiveMessageHandler in case we deal with a subscriber
            if (receiveMessageHandler != null)
            {
                mqttClient.UseApplicationMessageReceivedHandler(receiveMessageHandler);
                mqttClient.UseConnectedHandler(async e => {
                    Utils.Trace("{0} Connected to MQTTBroker", mqttClient?.Options?.ClientId);

                    // Subscribe to all topics since messages are filtered on the receiveMessageHandler
                    await mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic("#").Build());

                    Utils.Trace("{0} Subscribed");
                });
            }
            else
            {
                Utils.Trace("The provided MQTT message handler is null therefore messages will not be processed on client {0}!!!", mqttClient?.Options?.ClientId);
            }

            while (mqttClient.IsConnected == false)
            {
               Connect();
               await Task.Delay(TimeSpan.FromSeconds(reconnectInterval));
            }

            // Setup reconnect handler
            mqttClient.UseDisconnectedHandler(async e => {
                await Task.Delay(TimeSpan.FromSeconds(reconnectInterval));
                try
                {
                    Utils.Trace("Disconnect Handler called on client {0}, reason: {1} wasconnected: {2}",
                        mqttClient?.Options?.ClientId,
                        e.Reason,
                        e.ClientWasConnected);
                    Connect();
                }
                catch (Exception excOnDisconnect)
                {
                    Utils.Trace("{0} Failed to reconnect after disconnect occured: {1}", mqttClient?.Options?.ClientId, excOnDisconnect.Message);
                }
            });

            return mqttClient;
        }
    }
}
