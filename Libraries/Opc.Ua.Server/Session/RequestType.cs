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
