using System;
using System.Configuration;

namespace Microsoft.Azure.Devices.Relay.Worker
{
    static public class Configuration
    {
        public static long CheckpointMessageCount = 10;

        // This is the name of the storage account and can be found 
        // under Settings->Access keys->Storage account name of your storage account on the Azure portal, eg. *myopcstore*.
        // {AccessKey} is the access key of the storage account and can be found 
        // under Settings->Access keys->key1 of your storage account on the Azure portal.
        public static string StorageConnectionString = "DefaultEndpointsProtocol=https;AccountName=myopcstore;AccountKey=gu81z6nzW98ePtE0TZRblahblahblahStq1KefIXSDwzCRng8A==";

        // This is the name of a consumer group of your IoTHub. The IoTHub you use is the
        // one you have created for use with the OPC UA Publisher sample.
        // You need to create this consumer group via Settings->Endpoints->Events of your IoTHub in the Azure portal. 
        // We recommend that you do not share this Consumer group with other consumers, nor that you use the $Default consumer group. 
        public static string EventHubConsumerGroup = "myConsumerGroup";

        // This is the Event Hub compatible endpoint of your IoTHub and can be found 
        // under Settings->Endpoints->Events of your IoTHub in the Azure portal.
        //
        // SharedAccessKey is the IoT Hub primary key for access with iothubowner policy and can be found
        // under Settings->Shared access policies->iothubowner of your IoTHub in the Azure portal.  
        public static string EventHubConnectionString = "Endpoint=sb://iothub-ns-myiothub-123456-ceggddddbe.servicebus.windows.net/;SharedAccessKeyName=iothubowner;SharedAccessKey=oidy1JcblahblahblahUinA=";

        // This is the Event Hub compatible name of your IoTHub and can be found 
        // under Settings->Endpoints->Events of your IoTHub in the Azure portal, eg. *myiothub*
        public static string EventHubName = "myiothub";
    }
}
