using System;
using System.Configuration;

namespace Microsoft.Azure.Devices.Relay.Worker
{
    static public class Configuration
    {
        public static long CheckpointMessageCount = 10;

        // {StorageAccountName} is the name of the storage account and can be found 
        // under Settings->Access keys->Storage account name of your storage account on the Azure portal, eg. *myopcstore*.
        // {AccessKey} is the access key of the storage account and can be found 
        // under Settings->Access keys->key1 of your storage account on the Azure portal.
        public static string StorageConnectionString = "DefaultEndpointsProtocol=https;AccountName={StorageAccountName};AccountKey={AccessKey}";

        // {ConsumerGroupName} is the name of a aonsumer group of your IoTHub. The IoTHub you use is the
        // one you have created for use with the OPC UA Publisher sample.
        // You need to create this consumer group via the messaging settings of your IoTHub in the Azure portal. 
        // We recommend that you do not share this Consumer group with other consumers, nor that you use the $Default consumer group. 
        public static string EventHubConsumerGroup = "{ConsumerGroupName}";

        // {EventHubEndpoint} is the Event Hub compatible endpoint of your IoTHub and can be found 
        // under Settings->Messaging->Event Hub-compatible endpoint of your IoTHub in the Azure portal,
        // eg. *sb://iothub-ns-myiothub-12345-d35c0ac1cab.servicebus.windows.net/*
        // {PrimaryKey} is the IoT Hub primary key for access with iothubowner policy and can be found
        // under Settings->Shared access policies->iothubowner->Primary key of your IoTHub in the Azure portal.  
        public static string EventHubConnectionString = "Endpoint={EventHubEndpoint};SharedAccessKeyName=iothubowner;SharedAccessKey={PrimaryKey}";

        // {HubName} is the Event Hub compatible name of your IoTHub and can be found 
        // under Settings->Messaging->Event Hub-compatible name of your IoTHub in the Azure portal, eg. *myiothub*
        public static string EventHubName = "{HubName}";
    }
}
