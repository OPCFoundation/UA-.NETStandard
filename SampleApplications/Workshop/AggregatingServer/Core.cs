using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Configuration;
using AggregatingServer.Servers;
using Opc.Ua.Client;


namespace AggregatingServer.Core
{
    /// <summary>
    /// Generic Application
    /// </summary>
    public class GenericApplication
    {
        protected ApplicationInstance application;        
        protected int stopTimeout;
        protected bool autoAccept;
        protected string configDir;
        protected bool haveAppCertificate;
        protected ApplicationConfiguration configuration;

        public delegate Task  ProcessRun();
        public ProcessRun processRun;



        public GenericApplication()
        {
            application = new ApplicationInstance();

            processRun = async() =>  {
                Console.WriteLine("empty run from GenericApplication");
                await Task.Delay(100);
            };
        }

        public void LoadConfiguration(string configDir = "", string configSectionName = "GenericServer")
        {
            application.ConfigSectionName = configSectionName;
            this.configDir = configDir;

            // load the application configuration.
            if (configDir.Length == 0)
            {
                string dir = System.IO.Directory.GetCurrentDirectory();
                configuration = application.LoadApplicationConfiguration(false).Result;
            }
            else
            {
                System.IO.FileInfo fileInfo = new System.IO.FileInfo(configDir + "\\" + application.ConfigSectionName + ".Config.xml");
                configuration = ApplicationConfiguration.Load(fileInfo, application.ApplicationType, null).Result;

                application.ApplicationConfiguration = configuration;
            }
        }

        public virtual void CheckCertificate()
        {
            // check the application certificate.
            haveAppCertificate =  application.CheckApplicationInstanceCertificate(false, 0).Result;
            
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            if (haveAppCertificate)
            {

                configuration.ApplicationUri = Opc.Ua.Utils.GetApplicationUriFromCertificate(configuration.SecurityConfiguration.ApplicationCertificate.Certificate);
                if (configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates)
                {
                    autoAccept = true;
                }
                configuration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }
            else
            {
                Console.WriteLine(" WARN: missing application certificate, using unsecure connection.");
            }
        }

        private void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = autoAccept;
                if (autoAccept)
                {
                    Console.WriteLine("Accepted Certificate: {0}", e.Certificate.Subject);
                }
                else
                {
                    Console.WriteLine("Rejected Certificate: {0}", e.Certificate.Subject);
                }
            }
        }

        virtual public void Initialize(string endPointUrl = "", int stopTimeout = 0, bool autoAccept = true, string applicationName = "GenericServer")
        {
                        
            //this.endPointUrl = endPointUrl;            
            this.stopTimeout = stopTimeout;
            this.autoAccept = autoAccept;            
            application.ApplicationName = applicationName;
            application.ApplicationType = ApplicationType.Server;
        }

        //virtual public async Task Run()
        public async Task Run()
        {
            await processRun();
        }
        
    }

    public class Client : GenericApplication
    {
        protected string endPointUrl;
        protected int clientRunTime = Timeout.Infinite;
        protected const int ReconnectPeriod = 10;
        protected SessionReconnectHandler reconnectHandler;
        protected Session session;

        public Client() : base()
        {
            // delegate function
            processRun = new ProcessRun(MyRun);
        }

        public override void Initialize(string endPointUrl, int stopTimeout = 0, bool autoAccept = true, string applicationName = "GenericServer")
        {
            base.Initialize(endPointUrl, stopTimeout, autoAccept, applicationName);
            application.ApplicationType = ApplicationType.Client;
            this.endPointUrl = endPointUrl;
        }        

        protected async Task MyRun()
        {
            // Select endpoint 
            var selectedEndpoint = CoreClientUtils.SelectEndpoint(this.endPointUrl, haveAppCertificate, 15000);

            // Create session
            var endpointConfiguration = EndpointConfiguration.Create(configuration);
            var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
            session = await Session.Create(configuration, endpoint, false, application.ApplicationName, 60000, new UserIdentity(new AnonymousIdentityToken()), null);


            // register keep alive handler
            session.KeepAlive += Client_KeepAlive;
        }
        
        private void Client_KeepAlive(Session sender, KeepAliveEventArgs e)
        {
            if (e.Status != null && ServiceResult.IsNotGood(e.Status))
            {
                Console.WriteLine("{0} {1}/{2}", e.Status, sender.OutstandingRequestCount, sender.DefunctRequestCount);

                if (reconnectHandler == null)
                {
                    Console.WriteLine("--- RECONNECTING ---");
                    reconnectHandler = new SessionReconnectHandler();
                    reconnectHandler.BeginReconnect(sender, ReconnectPeriod * 1000, Client_ReconnectComplete);
                }
            }
        }

        private void Client_ReconnectComplete(object sender, EventArgs e)
        {
            // ignore callbacks from discarded objects.
            if (!Object.ReferenceEquals(sender, reconnectHandler))
            {
                return;
            }

            session = reconnectHandler.Session;
            reconnectHandler.Dispose();
            reconnectHandler = null;

            Console.WriteLine("--- RECONNECTED ---");
        }        

    }
    
    public class Server : GenericApplication
    {
        protected ServerBase server;

        public Server()
        {
            server = new ServerBase();
            // delegate function
            processRun = new ProcessRun(MyRun);
        }

        protected async Task MyRun()
        {
            server.Start(application.ApplicationConfiguration);
            await Task.Delay(100);
        }        

    }
    
}
