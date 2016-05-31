using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpcUaWebTelemetry;
using OpcUaWebTelemetry.CircularBuffer;
using OpcUaWebTelemetry.JsonData;

namespace Microsoft.Azure.Devices.Relay.Worker
{
    public class MessageProcessor : IEventProcessor
    {
        private long m_messageCount = 0;
        private const int m_historyLength = 10;
        public static List<CircularBuffer<data>> m_devices = new List<CircularBuffer<data>>();
        static CancellationTokenSource cts = new CancellationTokenSource();
        CancellationToken token = cts.Token;

        public Task OpenAsync(PartitionContext context)
        {
            m_messageCount = Configuration.CheckpointMessageCount;
            m_devices.Clear();

            Task.Factory.StartNew(() =>
            {
                UpdateClientBrowser();
            }, token);

            return Task.FromResult<object>(null);
        }

        private async void UpdateClientBrowser()
        {
            while (true)
            {
                await Task.Delay(1000);

                IHubContext hubContext = GlobalHost.ConnectionManager.GetHubContext<TelemetryHub>();
                hubContext.Clients.All.addNewMessageToPage(m_devices);
            }
        }

        public async Task ProcessEventsAsync(PartitionContext context, IEnumerable<EventData> messages)
        {
            foreach (var eventData in messages)
            {
                try
                {
                    string message = Encoding.UTF8.GetString(eventData.GetBytes()).Replace("\"", "'");
                    if (message != null)
                    {
                        data newElement = ParseJsonObject<data>(message);
                        if (newElement != null)
                        {
                            bool elementFound = false;
                            for (int i = 0; i < m_devices.Count; i++)
                            {
                                // if both the ID and the URI is the same, we say it's from the same device
                                if ((m_devices[i].getElement(0).MonitoredItem.Id == newElement.MonitoredItem.Id)
                                    && (m_devices[i].getElement(0).MonitoredItem.Uri == newElement.MonitoredItem.Uri))
                                {
                                    m_devices[i].Enqueue(newElement);
                                    elementFound = true;
                                    break;
                                }
                            }

                            if (!elementFound)
                            {
                                // new element
                                CircularBuffer<data> buffer = new CircularBuffer<data>(m_historyLength);
                                buffer.Enqueue(newElement);
                                m_devices.Add(buffer);
                            }
                        }
                    }

                    if (--m_messageCount < 0)
                    {
                        await context.CheckpointAsync();
                        m_messageCount = Configuration.CheckpointMessageCount;
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Exception processing message '{0}'", ex.Message);
                }
            }
        }

        public Task CloseAsync(PartitionContext context, CloseReason reason)
        {
            cts.Cancel();
            return Task.FromResult<object>(null);
        }

        public static T ParseJsonObject<T>(string json) where T : class, new()
        {
            try
            {
                JObject jobject = JObject.Parse(json);
                return JsonConvert.DeserializeObject<T>(jobject.ToString());
            }
            catch (Exception ex)
            {
                Trace.TraceError("Exception parsing json message '{0}'", ex.Message);
                throw ex;
            }
        }

        public static void DeleteElement(int index)
        {
            if ((0 <= index) && (index < m_devices.Count))
            {
                m_devices.RemoveAt(index);
            }
        }
    }  
}


