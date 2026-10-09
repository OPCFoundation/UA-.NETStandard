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
using System.IO;
using System.Threading.Tasks;
using InlineMethodModel;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// End-to-end tests for a source-generated node manager whose model
    /// method declares its arguments inline, without a method type.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class InlineMethodIntegrationTests
    {
        private string m_pkiRoot;
        private ServerFixture<InlineMethodServer> m_fixture;
        private InlineMethodServer m_server;
        private InlineMethodNodeManager m_manager;
        private RequestHeader m_requestHeader;
        private SecureChannelContext m_secureChannelContext;

        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            m_pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(InlineMethodIntegrationTests),
                Guid.NewGuid().ToString("N"));

            m_fixture = new ServerFixture<InlineMethodServer>(
                telemetry => new InlineMethodServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            m_server = await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
            m_manager = m_server.NodeManagerFactory.Manager;
            (m_requestHeader, m_secureChannelContext) = await m_server
                .CreateAndActivateSessionAsync(nameof(InlineMethodIntegrationTests))
                .ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            if (m_requestHeader is not null)
            {
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                await m_server
                    .CloseSessionAsync(
                        m_secureChannelContext,
                        m_requestHeader,
                        true,
                        RequestLifetime.None)
                    .ConfigureAwait(false);
            }

            m_server?.Dispose();
            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
            if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        [Test]
        public void InlineMethodIsBaseMethodStateWithArgumentProperties()
        {
            NodeState node = m_manager.Find(
                ToNodeId(InlineMethodModel.MethodIds.Thermostat_Boost))!;

            Assert.That(node, Is.TypeOf<MethodState>());
            var method = (MethodState)node;
            Assert.That(method.InputArguments, Is.Not.Null);
            Assert.That(method.OutputArguments, Is.Not.Null);
            ArrayOf<Argument> inputs = method.InputArguments.Value;
            ArrayOf<Argument> outputs = method.OutputArguments.Value;
            Assert.Multiple(() =>
            {
                Assert.That(inputs.Count, Is.EqualTo(1));
                Assert.That(inputs[0].Name, Is.EqualTo("Degrees"));
                Assert.That(inputs[0].DataType, Is.EqualTo(DataTypeIds.Double));
                Assert.That(outputs.Count, Is.EqualTo(1));
                Assert.That(outputs[0].Name, Is.EqualTo("NewSetpoint"));
                Assert.That(outputs[0].DataType, Is.EqualTo(DataTypeIds.Double));
            });
        }

        [Test]
        public async Task InlineMethodCallReturnsHandlerResultAsync()
        {
            CallMethodResult result = await CallBoostAsync(
                InlineMethodModel.MethodIds.Thermostat_Boost,
                Variant.From(2.5)).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.OutputArguments.Count, Is.EqualTo(1));
            Assert.That(result.OutputArguments[0].TryGetValue(out double newSetpoint), Is.True);
            Assert.That(newSetpoint, Is.EqualTo(InlineMethodTestModel.InitialSetpoint + 2.5));
        }

        [Test]
        public async Task TypeDeclarationMethodIdCallsInstanceHandlerAsync()
        {
            CallMethodResult result = await CallBoostAsync(
                InlineMethodModel.MethodIds.ThermostatType_Boost,
                Variant.From(1.0)).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.OutputArguments[0].TryGetValue(out double newSetpoint), Is.True);
            Assert.That(newSetpoint, Is.EqualTo(InlineMethodTestModel.InitialSetpoint + 1.0));
        }

        [Test]
        public async Task InlineMethodCallRejectsWrongArgumentTypeAsync()
        {
            CallMethodResult result = await CallBoostAsync(
                InlineMethodModel.MethodIds.Thermostat_Boost,
                Variant.From("2.5")).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(result.InputArgumentResults.Count, Is.EqualTo(1));
            Assert.That(result.InputArgumentResults[0], Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        private async Task<CallMethodResult> CallBoostAsync(
            ExpandedNodeId methodId,
            Variant degrees)
        {
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            CallResponse response = await m_server.CallAsync(
                m_secureChannelContext,
                m_requestHeader,
                [
                    new CallMethodRequest
                    {
                        ObjectId = ToNodeId(InlineMethodModel.ObjectIds.Thermostat),
                        MethodId = ToNodeId(methodId),
                        InputArguments = [degrees]
                    }
                ],
                RequestLifetime.None).ConfigureAwait(false);

            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private NodeId ToNodeId(ExpandedNodeId nodeId)
        {
            return ExpandedNodeId.ToNodeId(nodeId, m_server.CurrentInstance.NamespaceUris);
        }
    }
}
