// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.GdsDiscovery;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;

namespace UaLens.Tests.Administration;

public sealed partial class GdsDiscoveryWorkflowTests
{

    private static EndpointDescription Endpoint()
    {
        return new EndpointDescription
        {
            EndpointUrl = "opc.tcp://wire.test:4842",
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            UserIdentityTokens = [new UserTokenPolicy(UserTokenType.Certificate) { PolicyId = "operator-certificate" }],
            Server = new ApplicationDescription { ApplicationUri = "urn:fixture:server" }
        };
    }

    private static DiscoveryEndpointRow Row(EndpointDescription endpoint)
    {
        return new DiscoveryEndpointRow
        {
            Url = "opc.tcp://displayed.test:4841",
            SecurityMode = "SignAndEncrypt",
            SecurityProfile = "Basic256Sha256",
            Endpoint = endpoint
        };
    }
}
