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
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Configuration;

namespace Quickstarts.ConsoleReferenceClient
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            IOutput console = new ConsoleOutput();
            console.WriteLine("OPC UA Console Reference Client");
            try
            {
                // Define the UA Client application
                ApplicationInstance application = new ApplicationInstance();
                application.ApplicationName = "Quickstart Console Reference Client";
                application.ApplicationType = ApplicationType.Client;

                // load the application configuration.
                await application.LoadApplicationConfiguration("ConsoleReferenceClient.Config.xml", silent: false);
                // check the application certificate.
                await application.CheckApplicationInstanceCertificate(silent: false, minimumKeySize: 0);

                // create the UA Client object and connect to configured server.
                UAClient uaClient = new UAClient(application.ApplicationConfiguration, console, ClientBase.ValidateResponse);
                bool connected = await uaClient.ConnectAsync();
                if (connected)
                {
                    // Run tests for available methods.
                    uaClient.ReadNodes();
                    uaClient.WriteNodes();
                    uaClient.Browse();
                    uaClient.CallMethod();

                    uaClient.SubscribeToDataChanges();
                    // Wait for some DataChange notifications from MonitoredItems
                    await Task.Delay(20_000);

                    uaClient.Disconnect();
                }
                else
                {
                    console.WriteLine("Could not connect to server!");
                }

                console.WriteLine("\nProgram ended.");
                console.WriteLine("Press any key to finish...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                console.WriteLine(ex.Message);
            }
        }
    }
}
