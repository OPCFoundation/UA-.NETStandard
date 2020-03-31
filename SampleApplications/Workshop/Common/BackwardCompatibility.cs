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
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Server;
using Opc.Ua.Configuration;

namespace Quickstarts
{
    public partial class ConnectServerCtrl : Opc.Ua.Client.Controls.ConnectServerCtrl
    {
    }

    public partial class DiscoverServerDlg : Opc.Ua.Client.Controls.DiscoverServerDlg
    {
    }

    public partial class EditDataValueCtrl : Opc.Ua.Client.Controls.EditDataValueCtrl
    {
    }

    public partial class EditDataValueDlg : Opc.Ua.Client.Controls.EditDataValueDlg
    {
    }

    public partial class EditValueCtrl : Opc.Ua.Client.Controls.EditValueCtrl
    {
    }

    public partial class EditValueDlg : Opc.Ua.Client.Controls.EditComplexValueDlg
    {
    }

    public partial class HistoryDataListView : Opc.Ua.Client.Controls.HistoryDataListView
    {
    }

    public partial class SelectNodeDlg : Opc.Ua.Client.Controls.SelectNodeDlg
    {
    }

    public partial class BrowseNodeCtrl : Opc.Ua.Client.Controls.BrowseNodeCtrl
    {
    }
    
    public partial class ServerForm : Opc.Ua.Server.Controls.ServerForm
    {
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public ServerForm() : base()
        {
        }

        /// <summary>
        /// Creates a form which displays the status for a UA server.
        /// </summary>
        public ServerForm(StandardServer server, ApplicationConfiguration configuration) : base(server, configuration)
        {
        }

        /// <summary>
        /// Creates a form which displays the status for a UA server.
        /// </summary>
        public ServerForm(ApplicationInstance application) : base(application)
        {
        }
    }
}
