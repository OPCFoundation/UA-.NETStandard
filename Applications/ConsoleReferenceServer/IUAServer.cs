using System.Collections.Generic;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace Quickstarts
{
    public interface IUAServer<T> where T : IStandardServer
    {
        IApplicationInstance Application { get; }

        ApplicationConfiguration Configuration { get; }

        bool AutoAccept { get; set; }

        string Password { get; set; }

        ExitCode ExitCode { get; }

        T Server { get; }

        /// <summary>
        /// Load the application configuration.
        /// </summary>
        Task LoadAsync(string applicationName, string configSectionName);

        /// <summary>
        /// Load the application configuration.
        /// </summary>
        Task CheckCertificateAsync(bool renewCertificate);

        /// <summary>
        /// Create server instance and add node managers.
        /// </summary>
        void Create(IList<INodeManagerFactory> nodeManagerFactories);

        /// <summary>
        /// Start the server.
        /// </summary>
        Task StartAsync();

        /// <summary>
        /// Stops the server.
        /// </summary>
        Task StopAsync();
    }
}
