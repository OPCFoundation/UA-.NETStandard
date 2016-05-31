using Owin;
using System.Collections.Generic;
using Microsoft.AspNet.SignalR;
using OpcUaWebTelemetry.JsonData;

namespace OpcUaWebTelemetry
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            // Any connection or hub wire up and configuration should go here
            app.MapSignalR();
        }

    }

    public class TelemetryHub : Hub
    {
        public void Send(List<data>message)
        {
             Clients.All.addNewMessageToPage(message);
        }

        public void Send(string alert)
        {
            Clients.All.addNewAlertToPage(alert);
        }
    }
}