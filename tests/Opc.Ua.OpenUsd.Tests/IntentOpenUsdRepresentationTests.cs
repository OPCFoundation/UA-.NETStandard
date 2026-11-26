// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET10_0_OR_GREATER
using NUnit.Framework;
using Opc.Ua.OpenUsd.Server.Tests;
using Robotics.IntentEnabledRobot;

namespace Opc.Ua.OpenUsd.Tests
{
    [TestFixture]
    [Category("OpenUsd")]
    [Category("RobotIntent")]
    public sealed class IntentOpenUsdRepresentationTests
    {
        [Test]
        public void IntentSampleMountsRepresentationsAsAddIns()
        {
            (SystemContext context, ushort ns) = OpenUsdAuthoringHarness.NewContext();
            OpenUsdRootState root = OpenUsdAuthoringHarness.NewFacility(context, ns);
            OpenUsdStageState stage = OpenUsdAuthoringHarness.NewStage(context, root, ns, "Cell");
            BaseObjectState owner = OpenUsdAuthoringHarness.NewOwner(context, ns, "Controller");

            _ = IntentRobotCell.AttachRepresentation(
                context,
                stage,
                owner,
                "/World/IntentCommand",
                ns);

            OpenUsdRepresentationMountAssert.AllAreAddIns(context, owner);
        }
    }
}
#endif
