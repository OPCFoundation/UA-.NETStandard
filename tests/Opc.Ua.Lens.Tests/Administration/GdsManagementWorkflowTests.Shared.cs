/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
