using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Opc.Ua.Server
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public interface ISubscriptionStore
    {
        IEnumerable<IStoredSubscription> RestoreSubscriptions();
        void StoreSubscriptions(IEnumerable<IStoredSubscription> subscriptions);
        IDataChangeMonitoredItemQueue RestoreDataChangeMonitoredItemQueue(uint monitoredItemId);
        IEventMonitoredItemQueue RestoreEventMonitoredItemQueue(uint monitoredItemId);

        /// <summary>
        /// Provide created Subscriptiosn incl MonitoredItems to SubscriptionStore, to signal cleanup can take place
        /// The store shall clean all stored subscriptions, monitoredItems, and only keep the persitent queues for the monitoredItemIds provided
        /// <paramref name="createdSubscriptions"/> key = subscription id, value = monitoredItems
        /// </summary>
        void OnSubscriptionRestoreComplete(Dictionary<uint, uint[]> createdSubscriptions);
    }

    public class SubscriptionStore : ISubscriptionStore
    {

        public void StoreSubscriptions(IEnumerable<IStoredSubscription> subscriptions)
        {
            string result = JsonConvert.SerializeObject(subscriptions);
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "subscriptionsStore.txt"), result);
        }

        public IEnumerable<IStoredSubscription> RestoreSubscriptions()
        {
            var path = Path.Combine(Environment.CurrentDirectory, "subscriptionsStore.txt");
            if (File.Exists(path))
            {

                string json = File.ReadAllText(path);
                List<StoredSubscription> result = JsonConvert.DeserializeObject<List<StoredSubscription>>(json);
                return result;
            }
            return null;
            //throw new NotImplementedException();
        }

        public IDataChangeMonitoredItemQueue RestoreDataChangeMonitoredItemQueue(uint monitoredItemId)
        {
            return null;
            //throw new NotImplementedException();
        }
        public IEventMonitoredItemQueue RestoreEventMonitoredItemQueue(uint monitoredItemId)
        {
            return null;
            //throw new NotImplementedException();
        }

        public void OnSubscriptionRestoreComplete(Dictionary<uint, uint[]> createdSubscriptions)
        {
            //throw new NotImplementedException();
        }
    }

    public interface IStoredSubscription
    {
        uint Id { get; set; }
        bool IsDurable { get; set; }
        int LastSentMessage { get; set; }
        uint LifetimeCounter { get; set; }
        uint MaxKeepaliveCount { get; set; }
        uint MaxLifetimeCount { get; set; }
        uint MaxMessageCount { get; set; }
        uint MaxNotificationsPerPublish { get; set; }
        IEnumerable<StoredMonitoredItem> MonitoredItems { get; set; }
        byte Priority { get; set; }
        double PublishingInterval { get; set; }
        List<NotificationMessage> SentMessages { get; set; }
        long SequenceNumber { get; set; }
        UserIdentityToken UserIdentityToken { get; set; }
    }

    public class StoredSubscription : IStoredSubscription
    {
        public uint Id { get; set; }
        public uint LifetimeCounter { get; set; }
        public uint MaxLifetimeCount { get; set; }
        public uint MaxKeepaliveCount { get; set; }
        public uint MaxMessageCount { get; set; }
        public uint MaxNotificationsPerPublish { get; set; }
        public double PublishingInterval { get; set; }
        public byte Priority { get; set; }
        public UserIdentityToken UserIdentityToken { get; set; }
        public int LastSentMessage { get; set; }
        public bool IsDurable { get; set; }
        public long SequenceNumber { get; set; }
        public List<NotificationMessage> SentMessages { get; set; }
        public IEnumerable<StoredMonitoredItem> MonitoredItems { get; set; }
    }

    public interface IStoredMonitoredItem
    {
        /// <summary>
        /// If the item was restored by a node manager
        /// </summary>
        bool IsRestored { get; set; }
        bool AlwaysReportUpdates { get; set; }
        uint AttributeId { get; set; }
        uint ClientHandle { get; set; }
        DiagnosticsMasks DiagnosticsMasks { get; set; }
        bool DiscardOldest { get; set; }
        QualifiedName Encoding { get; set; }
        MonitoringFilter FilterToUse { get; set; }
        uint Id { get; set; }
        string IndexRange { get; set; }
        bool IsDurable { get; set; }
        ServiceResult LastError { get; set; }
        DataValue LastValue { get; set; }
        MonitoringMode MonitoringMode { get; set; }
        NodeId NodeId { get; set; }
        MonitoringFilter OriginalFilter { get; set; }
        uint QueueSize { get; set; }
        double Range { get; set; }
        double SamplingInterval { get; set; }
        int SourceSamplingInterval { get; set; }
        uint SubscriptionId { get; set; }
        TimestampsToReturn TimestampsToReturn { get; set; }
        int TypeMask { get; set; }
        NumericRange ParsedIndexRange { get; set; }
    }

    public class StoredMonitoredItem : IStoredMonitoredItem
    {
        public bool IsRestored { get; set; } = false;
        public uint SubscriptionId { get; set; }
        public uint Id { get; set; }
        public int TypeMask { get; set; }
        public NodeId NodeId { get; set; }
        public uint AttributeId { get; set; }
        public string IndexRange { get; set; }
        public QualifiedName Encoding { get; set; }
        public DiagnosticsMasks DiagnosticsMasks { get; set; }
        public TimestampsToReturn TimestampsToReturn { get; set; }
        public uint ClientHandle { get; set; }
        public MonitoringMode MonitoringMode { get; set; }
        public MonitoringFilter OriginalFilter { get; set; }
        public MonitoringFilter FilterToUse { get; set; }
        public double Range { get; set; }
        public double SamplingInterval { get; set; }
        public uint QueueSize { get; set; }
        public bool DiscardOldest { get; set; }
        public int SourceSamplingInterval { get; set; }
        public bool AlwaysReportUpdates { get; set; }
        public bool IsDurable { get; set; }
        public DataValue LastValue { get; set; }
        public ServiceResult LastError { get; set; }
        [JsonIgnore]
        public NumericRange ParsedIndexRange { get; set; }
    }

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
