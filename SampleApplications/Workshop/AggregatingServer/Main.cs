using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AggregatingServer
{
    class Program
    {
        static void Main()
        {
            Core.Client client = new Core.Client();

            client.Initialize(endPointUrl: "opc.tcp://192.168.0.137:35121/", applicationName: "DefaultClient");
            client.LoadConfiguration(configSectionName: "DefaultClient");
            client.CheckCertificate();

            client.Run().Wait();


        }
    }
}
