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
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.Servers
{
    public class SubscriptionStore : ISubscriptionStore
    {
        private static readonly JsonSerializerSettings s_settings = new()
        {
            TypeNameHandling = TypeNameHandling.All,
            Converters = { new ExtensionObjectConverter(), new NumericRangeConverter() }
        };

        private static readonly string s_storage_path = Path.Combine(
            Environment.CurrentDirectory,
            "Durable Subscriptions");

        private const string kFilename = "subscriptionsStore.txt";
        private readonly DurableMonitoredItemQueueFactory m_durableMonitoredItemQueueFactory;
        private readonly ILogger m_logger;
        private readonly ITelemetryContext m_telemetry;

        public SubscriptionStore(IServerInternal server)
        {
            m_logger = server.Telemetry.CreateLogger<SubscriptionStore>();
            m_telemetry = server.Telemetry;
            m_durableMonitoredItemQueueFactory = server
                .MonitoredItemQueueFactory as DurableMonitoredItemQueueFactory;
        }

        public bool StoreSubscriptions(IEnumerable<IStoredSubscription> subscriptions)
        {
            try
            {
                using IDisposable scope = AmbientMessageContext.SetScopedContext(m_telemetry);
                string result = JsonConvert.SerializeObject(subscriptions, s_settings);

                if (!Directory.Exists(s_storage_path))
                {
                    Directory.CreateDirectory(s_storage_path);
                }

                File.WriteAllText(Path.Combine(s_storage_path, kFilename), result);

                if (m_durableMonitoredItemQueueFactory != null)
                {
                    IEnumerable<uint> ids = subscriptions.SelectMany(
                        s => s.MonitoredItems.Select(m => m.Id));
                    m_durableMonitoredItemQueueFactory.PersistQueues(ids, s_storage_path);
                }
                return true;
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Failed to store subscriptions");
            }
            return false;
        }

        public RestoreSubscriptionResult RestoreSubscriptions()
        {
            string filePath = Path.Combine(s_storage_path, kFilename);
            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    using IDisposable scope = AmbientMessageContext.SetScopedContext(m_telemetry);
                    List<IStoredSubscription> result =
                        JsonConvert.DeserializeObject<List<IStoredSubscription>>(json, s_settings);

                    File.Delete(filePath);

                    return new RestoreSubscriptionResult(true, result);
                }
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Failed to restore subscriptions");
            }

            return new RestoreSubscriptionResult(false, null);
        }

        public class ExtensionObjectConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(ExtensionObject);
            }

            public override object ReadJson(
                JsonReader reader,
                Type objectType,
                object existingValue,
                JsonSerializer serializer)
            {
                var jo = JObject.Load(reader);
                object body = jo["Body"].ToObject<object>(serializer);
                ExpandedNodeId typeId = jo["TypeId"].ToObject<ExpandedNodeId>(serializer);
                return new ExtensionObject { Body = body, TypeId = typeId };
            }

            public override void WriteJson(
                JsonWriter writer,
                object value,
                JsonSerializer serializer)
            {
                var extensionObject = (ExtensionObject)value;
                var jo = new JObject
                {
                    ["Body"] = JToken.FromObject(extensionObject.Body, serializer),
                    ["TypeId"] = JToken.FromObject(extensionObject.TypeId, serializer)
                };
                jo.WriteTo(writer);
            }
        }

        public class NumericRangeConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(NumericRange);
            }

            public override object ReadJson(
                JsonReader reader,
                Type objectType,
                object existingValue,
                JsonSerializer serializer)
            {
                var jo = JObject.Load(reader);
                int begin = jo["Begin"].ToObject<int>(serializer);
                int end = jo["End"].ToObject<int>(serializer);
                return new NumericRange(begin, end);
            }

            public override void WriteJson(
                JsonWriter writer,
                object value,
                JsonSerializer serializer)
            {
                var extensionObject = (NumericRange)value;
                var jo = new JObject
                {
                    ["Begin"] = JToken.FromObject(extensionObject.Begin, serializer),
                    ["End"] = JToken.FromObject(extensionObject.End, serializer)
                };
                jo.WriteTo(writer);
            }
        }

        public IDataChangeMonitoredItemQueue RestoreDataChangeMonitoredItemQueue(
            uint monitoredItemId)
        {
            return m_durableMonitoredItemQueueFactory?.RestoreDataChangeQueue(
                monitoredItemId,
                s_storage_path);
        }

        public IEventMonitoredItemQueue RestoreEventMonitoredItemQueue(uint monitoredItemId)
        {
            return m_durableMonitoredItemQueueFactory?.RestoreEventQueue(
                monitoredItemId,
                s_storage_path);
        }

        public void OnSubscriptionRestoreComplete(Dictionary<uint, uint[]> createdSubscriptions)
        {
            string filePath = Path.Combine(s_storage_path, kFilename);

            //remove old file
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(ex, "Failed to cleanup files for stored subscsription");
                }
            }
            //remove old batches & queues
            if (m_durableMonitoredItemQueueFactory != null)
            {
                IEnumerable<uint> ids = createdSubscriptions.SelectMany(s => s.Value);
                m_durableMonitoredItemQueueFactory.CleanStoredQueues(s_storage_path, ids);
            }
        }
    }
}
