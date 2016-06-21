using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Sample;
using System;
using System.Threading.Tasks;

namespace NetCoreConsoleServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            try
            {
                ApplicationInstance application = new ApplicationInstance();
                application.ApplicationName = "UA Sample Server";
                application.ApplicationType = ApplicationType.Server;
                application.ConfigSectionName = "Opc.Ua.SampleServer";

                // load the application configuration.
                Task<ApplicationConfiguration> task = application.LoadApplicationConfiguration(false);
                task.Wait();

                // check the application certificate.
                Task<bool> task2 = application.CheckApplicationInstanceCertificate(false, 0);
                task2.Wait();

                // start the server.
                Task task3 = application.Start(new SampleServer());
                task3.Wait();

                Console.WriteLine("Server Started. Press any key to exit...");
                Console.ReadKey(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exit due to Exception: {0}", ex.Message);
            }
        }
    }
}
