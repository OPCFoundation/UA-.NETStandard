/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;
using OpcRcw.Ae;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// Manages the AE COM connections used by the UA server.
    /// </summary>
    public class ComAeClientManager : ComClientManager
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComAeClientManager"/> class.
        /// </summary>
        public ComAeClientManager()
        {
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Selects the AE COM client to use for the current context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="useDefault">If true the default instance is used.</param>
        /// <returns>A AE COM client instance.</returns>
        public new ComAeClient SelectClient(ServerSystemContext context, bool useDefault)
        {
            return (ComAeClient)base.SelectClient(context, useDefault);
        }
        #endregion

        #region Protected Members
        /// <summary>
        /// Gets or sets the default COM client instance.
        /// </summary>
        /// <value>The default client.</value>
        protected new ComAeClient DefaultClient
        {
            get { return base.DefaultClient as ComAeClient; }
            set { base.DefaultClient = value; }
        }

        /// <summary>
        /// Gets the configuration.
        /// </summary>
        /// <value>The configuration.</value>
        protected new ComAeClientConfiguration Configuration
        {
            get { return base.Configuration as ComAeClientConfiguration; }
        }

        /// <summary>
        /// Creates a new client object.
        /// </summary>
        protected override ComClient CreateClient()
        {
            return new ComAeClient(Configuration);
        }

        /// <summary>
        /// Updates the status node.
        /// </summary>
        protected override bool UpdateStatus()
        {
            // get the status from the server.
            ComAeClient client = DefaultClient;
            OPCEVENTSERVERSTATUS? status = client.GetStatus();

            // check the client has been abandoned.
            if (!Object.ReferenceEquals(client, DefaultClient))
            {
                return false;
            }

            // update the server status.
            lock (StatusNodeLock)
            {
                StatusNode.ServerUrl.Value = Configuration.ServerUrl;

                if (status != null)
                {
                    StatusNode.SetStatusCode(DefaultSystemContext, StatusCodes.Good, DateTime.UtcNow);

                    StatusNode.ServerState.Value = Utils.Format("{0}", status.Value.dwServerState);
                    StatusNode.CurrentTime.Value = ComUtils.GetDateTime(status.Value.ftCurrentTime);
                    StatusNode.LastUpdateTime.Value = ComUtils.GetDateTime(status.Value.ftLastUpdateTime);
                    StatusNode.StartTime.Value = ComUtils.GetDateTime(status.Value.ftStartTime);
                    StatusNode.VendorInfo.Value = status.Value.szVendorInfo;
                    StatusNode.SoftwareVersion.Value = Utils.Format("{0}.{1}.{2}", status.Value.wMajorVersion, status.Value.wMinorVersion, status.Value.wBuildNumber);
                }
                else
                {
                    StatusNode.SetStatusCode(DefaultSystemContext, StatusCodes.BadOutOfService, DateTime.UtcNow);
                }

                StatusNode.ClearChangeMasks(DefaultSystemContext, true);
                return status != null;
            }
        }
        #endregion

        #region Private Fields
        #endregion
    }
}
