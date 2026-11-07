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

using System;
using Moq;
using NUnit.Framework;
using Opc.Ua.LADS.Server;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Verifies parameter identity and the retained scalar command contract without hosting a server.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    public sealed class LadsFunctionCommandTests
    {
        /// <summary>
        /// Named commands preserve the exact escaped member identity and argument.
        /// </summary>
        [Test]
        public void NamedCommandPreservesIdentityAndValue()
        {
            ILadsFunction function = Mock.Of<ILadsFunction>();
            const string path = "ControllerModeSet/Heat%2FHigh/TargetValue";
            var command = new LadsFunctionCommand(
                function, LadsFunctionCommandKind.SetParameter, Variant.From(12.5), path);

            Assert.That(command.Function, Is.SameAs(function));
            Assert.That(command.Kind, Is.EqualTo(LadsFunctionCommandKind.SetParameter));
            Assert.That(command.ParameterPath, Is.EqualTo(path));
            Assert.That(command.Value, Is.EqualTo(Variant.From(12.5)));
        }

        /// <summary>
        /// A named write must identify its member.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t")]
        public void NamedCommandRejectsMissingPath(string? path)
        {
            ILadsFunction function = Mock.Of<ILadsFunction>();
            Assert.Throws<ArgumentException>(() => new LadsFunctionCommand(
                function, LadsFunctionCommandKind.SetParameter, Variant.From(1.0), path));
        }

        /// <summary>
        /// Scalar commands retain their existing constructor and do not accept a named parameter identity.
        /// </summary>
        [TestCase(LadsFunctionCommandKind.Start)]
        [TestCase(LadsFunctionCommandKind.SetTargetValue)]
        [TestCase(LadsFunctionCommandKind.ResetTotalizer)]
        [TestCase(LadsFunctionCommandKind.SetMode)]
        public void ScalarCommandRetainsExistingContract(LadsFunctionCommandKind kind)
        {
            ILadsFunction function = Mock.Of<ILadsFunction>();
            var command = new LadsFunctionCommand(function, kind, Variant.From(7.5));

            Assert.That(command.Function, Is.SameAs(function));
            Assert.That(command.Kind, Is.EqualTo(kind));
            Assert.That(command.Value, Is.EqualTo(Variant.From(7.5)));
            Assert.That(command.ParameterPath, Is.Null);
            Assert.Throws<ArgumentException>(() => new LadsFunctionCommand(
                function, kind, Variant.From(7.5), "ControllerTuningParameter/CtrlP"));
        }

        /// <summary>
        /// Both construction paths require a function handle.
        /// </summary>
        [Test]
        public void CommandRejectsMissingFunction()
        {
            Assert.Throws<ArgumentNullException>(() => new LadsFunctionCommand(
                null!, LadsFunctionCommandKind.SetTargetValue));
            Assert.Throws<ArgumentNullException>(() => new LadsFunctionCommand(
                null!, LadsFunctionCommandKind.SetParameter, Variant.From(1.0), "ControllerTuningParameter/CtrlP"));
        }
    }
}
