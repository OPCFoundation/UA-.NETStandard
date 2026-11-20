/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Linq;
using Avalonia;
using NUnit.Framework;

namespace UaLens.Tests
{
    [TestFixture]
    public sealed class DesktopGraphicsOptionsTests
    {
        [Test]
        public void SoftwareGlRemainsExplicitlyOptIn()
        {
            var defaults = new X11PlatformOptions();
            X11PlatformOptions options = Program.CreateX11PlatformOptions(allowSoftwareGl: false);

            Assert.That(options.GlxRendererBlacklist, Is.EqualTo(defaults.GlxRendererBlacklist));
            Assert.That(options.GlxRendererBlacklist, Does.Contain("llvmpipe"));
            Assert.That(options.RenderingMode, Is.EqualTo(defaults.RenderingMode));
            Assert.That(options.GlProfiles, Is.EqualTo(defaults.GlProfiles));
        }

        [Test]
        public void SoftwareGlOptInPreservesOtherDriverRestrictionsAndDefaults()
        {
            var defaults = new X11PlatformOptions();
            X11PlatformOptions enabled = Program.CreateX11PlatformOptions(allowSoftwareGl: true);
            X11PlatformOptions laterDefault = Program.CreateX11PlatformOptions(allowSoftwareGl: false);

            Assert.That(enabled.GlxRendererBlacklist,
                Is.EqualTo(defaults.GlxRendererBlacklist.Where(name => name != "llvmpipe")));
            Assert.That(enabled.GlxRendererBlacklist, Does.Not.Contain("llvmpipe"));
            Assert.That(enabled.RenderingMode, Is.EqualTo(defaults.RenderingMode));
            Assert.That(enabled.GlProfiles, Is.EqualTo(defaults.GlProfiles));
            Assert.That(defaults.GlxRendererBlacklist, Does.Contain("llvmpipe"));
            Assert.That(laterDefault.GlxRendererBlacklist, Is.EqualTo(defaults.GlxRendererBlacklist));
        }

        [Test]
        public void ExplicitVulkanCompositorCannotFallBackToAnotherRenderingMode()
        {
            var defaults = new X11PlatformOptions();
            X11PlatformOptions options = Program.CreateX11PlatformOptions(
                allowSoftwareGl: false, x11Vulkan: true);

            Assert.That(options.RenderingMode, Is.EqualTo(new[] { X11RenderingMode.Vulkan }));
            Assert.That(options.GlxRendererBlacklist, Is.EqualTo(defaults.GlxRendererBlacklist));
            Assert.That(options.GlProfiles, Is.EqualTo(defaults.GlProfiles));
            Assert.That(Program.CreateX11PlatformOptions(allowSoftwareGl: false).RenderingMode,
                Is.EqualTo(defaults.RenderingMode));
        }
    }
}
