/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Threading.Tasks;
using Opc.Ua.Configuration;

namespace Opc.Ua.Server.Tests
{
    public class ServerFixture<T> where T : ServerBase, new()
    {
        private NUnitTraceLogger m_traceLogger;
        public T Server { get; set; }
        public bool LogConsole { get; set; }
        public bool AutoAccept { get; set; }
        public int Port { get; set; }

        private void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                if (AutoAccept)
                {
                    if (!LogConsole)
                    {
                        Console.WriteLine("Accepted Certificate: {0}", e.Certificate.Subject);
                    }
                    Utils.Trace(Utils.TraceMasks.Security, "Accepted Certificate: {0}", e.Certificate.Subject);
                    e.Accept = true;
                    return;
                }
            }
            if (!LogConsole)
            {
                Console.WriteLine("Rejected Certificate: {0} {1}", e.Error, e.Certificate.Subject);
            }
            Utils.Trace(Utils.TraceMasks.Security, "Rejected Certificate: {0} {1}", e.Error, e.Certificate.Subject);
        }

        /// <summary>
        /// Start server fixture on random port.
        /// </summary>
        public async Task<T> StartAsync(TextWriter writer, bool clean)
        {
            Random m_random = new Random();
            int testPort;
            bool retryStartServer = false;
            int serverStartRetries = 10;
            do
            {
                try
                {
                    testPort = m_random.Next(50000, 60000);
                    await InternalStartServerAsync(writer, clean, testPort).ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    serverStartRetries--;
                    if (serverStartRetries == 0 ||
                        sre.StatusCode != StatusCodes.BadNoCommunication)
                    {
                        throw;
                    }
                    retryStartServer = true;
                }
                await Task.Delay(m_random.Next(100, 1000)).ConfigureAwait(false);
            } while (retryStartServer);

            return Server;
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task InternalStartServerAsync(TextWriter writer, bool clean, int port)
        {
            //CertificatePasswordProvider PasswordProvider = new CertificatePasswordProvider(Password);
            ApplicationInstance application = new ApplicationInstance
            {
                ApplicationName = nameof(T),
                ApplicationType = ApplicationType.Server
            };

            // create the application configuration. Use temp path for cert stores.
            var pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();
            var endpointUrl = $"opc.tcp://localhost:{port}/" + nameof(T);
            ApplicationConfiguration config = await application.Build(
                "urn:localhost:UA:" + nameof(T),
                "uri:opcfoundation.org:" + nameof(T))
                .AsServer(
                    new string[] {
                    endpointUrl
                })
                .AddSecurityConfiguration(
                    "CN=" + nameof(T) + ", C=US, S=Arizona, O=OPC Foundation, DC=localhost",
                    pkiRoot).Create().ConfigureAwait(false);

            if (writer != null)
            {
                m_traceLogger = NUnitTraceLogger.Create(writer, config, Utils.TraceMasks.All);
            }

            // check the application certificate.
            bool haveAppCertificate = await application.CheckApplicationInstanceCertificate(
                true, CertificateFactory.DefaultKeySize, CertificateFactory.DefaultLifeTime).ConfigureAwait(false);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            // start the server.
            T server = new T();
            await application.Start(server).ConfigureAwait(false);
            Server = server;
            Port = port;
        }

        public void SetTraceOutput(TextWriter writer)
        {
            m_traceLogger.SetWriter(writer);
        }

        /// <summary>
        /// 
        /// </summary>
        public async Task StopAsync()
        {
            Server.Stop();
            Server = null;
            await Task.Delay(100).ConfigureAwait(false);
        }
    }
}
