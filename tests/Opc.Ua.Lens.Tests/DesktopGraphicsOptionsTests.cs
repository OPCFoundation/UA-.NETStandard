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
