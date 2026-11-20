/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Unit tests for viewport pick options and command fallback plumbing.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class UsdViewOptionsTests
    {
        [Test]
        public void DefaultsLeavePickingDisabled()
        {
            var options = new UsdViewOptions();

            Assert.That(options.StagePath, Is.Empty);
            Assert.That(options.PluginPath, Is.Null);
            Assert.That(options.Renderer, Is.Null);
            Assert.That(options.Title, Is.Null);
            Assert.That(options.CameraPath, Is.Null);
            Assert.That(options.Telemetry, Is.Null);
            Assert.That(options.PrimPicked, Is.Null);
            Assert.That(options.CommandPrimPath, Is.EqualTo("/World/IntentCommand"));
            Assert.That(options.PickMode, Is.EqualTo(UsdViewPickMode.Auto));
        }

        [Test]
        public void CommandFallbackRaisesOnceForChangedTarget()
        {
            string? lastTarget = null;

            bool first = UsdViewPickCommand.TryUpdatePickedPrim(
                "/World/RobotTargets/TargetA", ref lastTarget, emitInitialTarget: false,
                out string firstPick);
            bool second = UsdViewPickCommand.TryUpdatePickedPrim(
                "/World/RobotTargets/TargetB", ref lastTarget, emitInitialTarget: true,
                out string secondPick);

            Assert.That(first, Is.False);
            Assert.That(firstPick, Is.Empty);
            Assert.That(second, Is.True);
            Assert.That(secondPick, Is.EqualTo("/World/RobotTargets/TargetB"));
        }

        [Test]
        public void CommandFallbackIgnoresUnchangedTarget()
        {
            string? lastTarget = null;

            bool first = UsdViewPickCommand.TryUpdatePickedPrim(
                "/World/RobotTargets/TargetA", ref lastTarget, emitInitialTarget: true,
                out string firstPick);
            bool second = UsdViewPickCommand.TryUpdatePickedPrim(
                "/World/RobotTargets/TargetA", ref lastTarget, emitInitialTarget: true,
                out string secondPick);

            Assert.That(first, Is.True);
            Assert.That(firstPick, Is.EqualTo("/World/RobotTargets/TargetA"));
            Assert.That(second, Is.False);
            Assert.That(secondPick, Is.Empty);
        }

        [Test]
        public void CommandFallbackRejectsBlankRelativeAndWhitespaceTargets()
        {
            string? lastTarget = "/World/RobotTargets/TargetA";

            bool blank = UsdViewPickCommand.TryUpdatePickedPrim(
                " ",
                ref lastTarget,
                emitInitialTarget: true,
                out string blankPick);
            bool relative = UsdViewPickCommand.TryUpdatePickedPrim(
                "World/RobotTargets/TargetB",
                ref lastTarget,
                emitInitialTarget: true,
                out string relativePick);
            bool trimmed = UsdViewPickCommand.TryUpdatePickedPrim(
                " /World/RobotTargets/TargetC ",
                ref lastTarget,
                emitInitialTarget: true,
                out string trimmedPick);

            Assert.That(blank, Is.False);
            Assert.That(blankPick, Is.Empty);
            Assert.That(relative, Is.False);
            Assert.That(relativePick, Is.Empty);
            Assert.That(trimmed, Is.True);
            Assert.That(trimmedPick, Is.EqualTo("/World/RobotTargets/TargetC"));
        }
    }
}
