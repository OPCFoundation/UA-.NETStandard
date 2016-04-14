/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The set of all service request types (used for collecting diagnostics and checking permissions).
    /// </summary>
    public enum RequestType
    {
        /// <summary>
        /// The request type is not known.
        /// </summary>
		Unknown,

        /// <see cref="IDiscoveryServer.FindServers" />
		FindServers,

        /// <see cref="IDiscoveryServer.GetEndpoints" />
		GetEndpoints,

        /// <see cref="ISessionServer.CreateSession" />
		CreateSession,

        /// <see cref="ISessionServer.ActivateSession" />
		ActivateSession,

        /// <see cref="ISessionServer.CloseSession" />
		CloseSession,

        /// <see cref="ISessionServer.Cancel" />
		Cancel,

        /// <see cref="ISessionServer.Read" />
		Read,

        /// <see cref="ISessionServer.HistoryRead" />
		HistoryRead,

        /// <see cref="ISessionServer.Write" />
		Write,

        /// <see cref="ISessionServer.HistoryUpdate" />
		HistoryUpdate,

        /// <see cref="ISessionServer.Call" />
		Call,

        /// <see cref="ISessionServer.CreateMonitoredItems" />
		CreateMonitoredItems,

        /// <see cref="ISessionServer.ModifyMonitoredItems" />
		ModifyMonitoredItems,

        /// <see cref="ISessionServer.SetMonitoringMode" />
		SetMonitoringMode,

        /// <see cref="ISessionServer.SetTriggering" />
		SetTriggering,

        /// <see cref="ISessionServer.DeleteMonitoredItems" />
		DeleteMonitoredItems,

        /// <see cref="ISessionServer.CreateSubscription" />
		CreateSubscription,

        /// <see cref="ISessionServer.ModifySubscription" />
		ModifySubscription,

        /// <see cref="ISessionServer.SetPublishingMode" />
		SetPublishingMode,

        /// <see cref="ISessionServer.Publish" />
		Publish,

        /// <see cref="ISessionServer.Republish" />
		Republish,

        /// <see cref="ISessionServer.TransferSubscriptions" />
		TransferSubscriptions,

        /// <see cref="ISessionServer.DeleteSubscriptions" />
		DeleteSubscriptions,

        /// <see cref="ISessionServer.AddNodes" />
		AddNodes,

        /// <see cref="ISessionServer.AddReferences" />
		AddReferences,

        /// <see cref="ISessionServer.DeleteNodes" />
		DeleteNodes,

        /// <see cref="ISessionServer.DeleteReferences" />
		DeleteReferences,

        /// <see cref="ISessionServer.Browse" />
		Browse,

        /// <see cref="ISessionServer.BrowseNext" />
		BrowseNext,

        /// <see cref="ISessionServer.TranslateBrowsePathsToNodeIds" />
		TranslateBrowsePathsToNodeIds,

        /// <see cref="ISessionServer.QueryFirst" />
		QueryFirst,

        /// <see cref="ISessionServer.QueryNext" />
		QueryNext,

        /// <see cref="ISessionServer.RegisterNodes" />
		RegisterNodes,

        /// <see cref="ISessionServer.UnregisterNodes" />
		UnregisterNodes
    }
}
