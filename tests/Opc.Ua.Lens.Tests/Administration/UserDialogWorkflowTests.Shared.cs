// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client.UserManagement;
using UaLens.Plugins.UserManagement;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Administration;

public sealed partial class UserDialogWorkflowTests
{

    private static string NewPassword() => "  " + Guid.NewGuid().ToString("N") + "  ";

    private static void SetMask(Window dialog, UserConfigurationMask mask)
    {
        DesktopInteraction.Control<CheckBox>(dialog, "DisabledBox").IsChecked =
            (mask & UserConfigurationMask.Disabled) != 0;
        DesktopInteraction.Control<CheckBox>(dialog, "MustChangePasswordBox").IsChecked =
            (mask & UserConfigurationMask.MustChangePassword) != 0;
        DesktopInteraction.Control<CheckBox>(dialog, "NoChangeByUserBox").IsChecked =
            (mask & UserConfigurationMask.NoChangeByUser) != 0;
        DesktopInteraction.Control<CheckBox>(dialog, "NoDeleteBox").IsChecked =
            (mask & UserConfigurationMask.NoDelete) != 0;
    }

    private static void Click(Window dialog, string name)
    {
        DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, name));
    }
}
