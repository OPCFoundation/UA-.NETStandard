// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client.Roles;
using UaLens.Plugins.RoleManagement;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Administration;

public sealed partial class RoleDialogWorkflowTests
{
    public static IEnumerable<IdentityCriteriaType> CriteriaKinds => Enum.GetValues<IdentityCriteriaType>();

    private static void Click(Window dialog, string name)
    {
        DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, name));
    }
}
