using System;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using System.Web.Mvc;
using Microsoft.Azure.Devices.Relay.Worker;
using Microsoft.ServiceBus.Messaging;

namespace OpcUaWebTelemetry.Controllers
{
    public class HomeController : Controller
    {
        private static EventProcessorHost m_host = new EventProcessorHost(
                    Configuration.EventHubName.ToLower(),
                    Configuration.EventHubConsumerGroup,
                    Configuration.EventHubConnectionString,
                    Configuration.StorageConnectionString);
        private static object m_lock = new object();
        private static bool m_registered = false;

        public void ProcessMessages(ref EventProcessorHost host, ref object thelock, ref bool registered, bool active)
        {
            ServicePointManager.DefaultConnectionLimit = 12;

            lock (thelock)
            {
                if (active)
                {
                    if (!registered)
                    {
                        try
                        {
                            EventProcessorOptions options = new EventProcessorOptions();
                            options.InitialOffsetProvider = (partitionId) => DateTime.UtcNow;

                            host.RegisterEventProcessorAsync<MessageProcessor>(options).Wait();
                            registered = true;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex.Message);
                            registered = false;
                        }
                    }
                }
                else
                {
                    if (registered)
                    {
                        try
                        {
                            host.UnregisterEventProcessorAsync().Wait();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex.Message);
                        }
                        finally
                        {
                            registered = false;
                        }
                    }
                }
            }
        }

        public ActionResult StartProcessor()
        {
            Task.Factory.StartNew(() =>
            {
                ProcessMessages(ref m_host, ref m_lock, ref m_registered, true);
            });

            return View("Index");
        }

        public ActionResult StopProcessor()
        {
            Task.Factory.StartNew(() =>
            {
                ProcessMessages(ref m_host, ref m_lock, ref m_registered, false);
            });

            return View("Index");
        }

        public ActionResult Index()
        {
            return View();
        }

        public ActionResult About()
        {
            return View();
        }

        public ActionResult Contact()
        {
            return View();
        }

        public ActionResult Delete(string value)
        {
            try
            {
                int index = int.Parse(value);
                MessageProcessor.DeleteElement(index - 1);
            }
            catch (Exception ex)
            {
                Trace.TraceError("Exception deleting row '{0}'", ex.Message);
            }

            return View("Index");
        }
    }
}