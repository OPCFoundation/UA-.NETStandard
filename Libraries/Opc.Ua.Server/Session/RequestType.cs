/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

        /// <summary>
        /// <see cref="IDiscoveryServer.FindServers" />
        /// </summary>
        FindServers,

        /// <summary>
        /// <see cref="IDiscoveryServer.GetEndpoints" />
        /// </summary>
        GetEndpoints,

        /// <summary>
        /// <see cref="ISessionServer.CreateSession" />
        /// </summary>
        CreateSession,

        /// <summary>
        /// <see cref="ISessionServer.ActivateSession" />
        /// </summary>
        ActivateSession,

        /// <summary>
        /// <see cref="ISessionServer.CloseSession" />
        /// </summary>
        CloseSession,

        /// <summary>
        /// <see cref="ISessionServer.Cancel" />
        /// </summary>
        Cancel,

        /// <summary>
        /// <see cref="ISessionServer.Read" />
        /// </summary>
        Read,

        /// <summary>
        /// <see cref="ISessionServer.HistoryRead" />
        /// </summary>
        HistoryRead,

        /// <summary>
        /// <see cref="ISessionServer.Write" />
        /// </summary>
        Write,

        /// <summary>
        /// <see cref="ISessionServer.HistoryUpdate" />
        /// </summary>
        HistoryUpdate,

        /// <summary>
        /// <see cref="ISessionServer.Call" />
        /// </summary>
        Call,

        /// <summary>
        /// <see cref="ISessionServer.CreateMonitoredItems" />
        /// </summary>
        CreateMonitoredItems,

        /// <summary>
        /// <see cref="ISessionServer.ModifyMonitoredItems" />
        /// </summary>
        ModifyMonitoredItems,

        /// <summary>
        /// <see cref="ISessionServer.SetMonitoringMode" />
        /// </summary>
        SetMonitoringMode,

        /// <summary>
        /// <see cref="ISessionServer.SetTriggering" />
        /// </summary>
        SetTriggering,

        /// <summary>
        /// <see cref="ISessionServer.DeleteMonitoredItems" />
        /// </summary>
        DeleteMonitoredItems,

        /// <summary>
        /// <see cref="ISessionServer.CreateSubscription" />
        /// </summary>
        CreateSubscription,

        /// <summary>
        /// <see cref="ISessionServer.ModifySubscription" />
        /// </summary>
        ModifySubscription,

        /// <summary>
        /// <see cref="ISessionServer.SetPublishingMode" />
        /// </summary>
        SetPublishingMode,

        /// <summary>
        /// <see cref="ISessionServer.Publish" />
        /// </summary>
        Publish,

        /// <summary>
        /// <see cref="ISessionServer.Republish" />
        /// </summary>
        Republish,

        /// <summary>
        /// <see cref="ISessionServer.TransferSubscriptions" />
        /// </summary>
        TransferSubscriptions,

        /// <summary>
        /// <see cref="ISessionServer.DeleteSubscriptions" />
        /// </summary>
        DeleteSubscriptions,

        /// <summary>
        /// <see cref="ISessionServer.AddNodes" />
        /// </summary>
        AddNodes,

        /// <summary>
        /// <see cref="ISessionServer.AddReferences" />
        /// </summary>
        AddReferences,

        /// <summary>
        /// <see cref="ISessionServer.DeleteNodes" />
        /// </summary>
        DeleteNodes,

        /// <summary>
        /// <see cref="ISessionServer.DeleteReferences" />
        /// </summary>
        DeleteReferences,

        /// <summary>
        /// <see cref="ISessionServer.Browse" />
        /// </summary>
        Browse,

        /// <summary>
        /// <see cref="ISessionServer.BrowseNext" />
        /// </summary>
        BrowseNext,

        /// <summary>
        /// <see cref="ISessionServer.TranslateBrowsePathsToNodeIds" />
        /// </summary>
        TranslateBrowsePathsToNodeIds,

        /// <summary>
        /// <see cref="ISessionServer.QueryFirst" />
        /// </summary>
        QueryFirst,

        /// <summary>
        /// <see cref="ISessionServer.QueryNext" />
        /// </summary>
        QueryNext,

        /// <summary>
        /// <see cref="ISessionServer.RegisterNodes" />
        /// </summary>
        RegisterNodes,

        /// <summary>
        /// <see cref="ISessionServer.UnregisterNodes" />
        /// </summary>
        UnregisterNodes
    }
}
