/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Gds;
using UaLens.Plugins.Gds;
using UaLens.Plugins.GdsManagement;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Administration;

public sealed partial class GdsManagementWorkflowTests
{

    private static RegisteredApp Press()
    {
        return RegisteredApp.FromRecord(new ApplicationRecordDataType
        {
            ApplicationId = new NodeId(7601),
            ApplicationNames = [new LocalizedText("Press Alpha")],
            ApplicationUri = "urn:cell:A",
            ProductUri = "urn:press:sku",
            ApplicationType = ApplicationType.Server,
            DiscoveryUrls = ["opc.tcp://endpoint-only.test:4840"],
            ServerCapabilities = ["DA", "HA"]
        });
    }
}
