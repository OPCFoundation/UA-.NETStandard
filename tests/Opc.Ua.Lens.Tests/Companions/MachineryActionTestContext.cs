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
using System.Security.Cryptography;
using Moq;
using Opc.Ua;
using Opc.Ua.Machinery.Result;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Machinery;
using UaLens.Samples;

namespace UaLens.Tests.Companions
{
    /// <summary>
    /// Deterministic current-selection and owned-simulator service evidence.
    /// </summary>
    internal sealed class MachineryActionTestContext
    {
        public MachineryActionTestContext()
        {
            Services.NamespaceUris.GetIndexOrAppend(Opc.Ua.Machinery.Namespaces.Machinery);
            Services.NamespaceUris.GetIndexOrAppend(Opc.Ua.Machinery.ProcessValues.Namespaces.MachineryProcessValues);
            Services.NamespaceUris.GetIndexOrAppend(Opc.Ua.PADIM.Namespaces.PADIM);
            Services.NamespaceUris.GetIndexOrAppend(Opc.Ua.Machinery.Result.Namespaces.MachineryResult);
            Services.NamespaceUris.GetIndexOrAppend(Opc.Ua.Machinery.Jobs.Namespaces.MachineryJobs);
            Services.NamespaceUris.GetIndexOrAppend(Opc.Ua.ISA95.JobControl.V2.Namespaces.ISA95JobControlV2);
            Services.MessageContext.Factory.Builder.AddOpcUaMachineryResult().Commit();
            Endpoint = new EndpointDescription("opc.tcp://127.0.0.1:58124/MachineryServer")
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                Server = new ApplicationDescription { ApplicationUri = "urn:owned:MachineryServer" },
                ServerCertificate = ByteString.From("owned-public-test-peer"u8)
            };
            Services.Session.SetupGet(session => session.Connected).Returns(true);
            Services.Session.SetupGet(session => session.SessionId).Returns(new NodeId(1234u));
            Services.Session.SetupGet(session => session.Identity).Returns(new UserIdentity());
            Services.Session.SetupGet(session => session.Endpoint).Returns(Endpoint);
            Transport.SetupGet(channel => channel.MessageContext).Returns(Services.MessageContext);
            Services.Session.SetupGet(session => session.TransportChannel).Returns(Transport.Object);
            Machine = new MachineryMachine(Services.Id("machine"), "Press", "Machine");
            NodeId process = Services.Id("process");
            Services.AddObject(process, Opc.Ua.Machinery.ProcessValues.ObjectTypeIds.ProcessValueType);
            Services.AddChild(Machine.NodeId, process);
            Signal = Services.AddProperty(process, Opc.Ua.PADIM.Namespaces.PADIM,
                Opc.Ua.PADIM.BrowseNames.AnalogSignal, Variant.From(42.0));
            Services.SetValue(Signal, Variant.From((int)NodeClass.Variable), Attributes.NodeClass);
            Services.SetValue(Signal, Variant.From(Opc.Ua.DataTypeIds.Double), Attributes.DataType);
            Process = new MachineryItem(process, "Pressure", "42", "Process") { SourceNodeId = Signal };
            Method = Services.Id("zero");
            Services.AddObject(Method, Opc.Ua.ObjectTypeIds.BaseObjectType);
            Services.AddChild(
                process, Method, Opc.Ua.PADIM.Namespaces.PADIM, Opc.Ua.PADIM.BrowseNames.ZeroPointAdjustment);
            Services.SetValue(Method, Variant.From((int)NodeClass.Method), Attributes.NodeClass);
            Services.SetValue(Method, Variant.From(true), Attributes.Executable);
            Services.SetValue(Method, Variant.From(true), Attributes.UserExecutable);
            Status = Services.AddProperty(process, Opc.Ua.Machinery.ProcessValues.Namespaces.MachineryProcessValues,
                "Status", Variant.From((ushort)0));
            Suppression = Services.AddProperty(
                process, Opc.Ua.Machinery.ProcessValues.Namespaces.MachineryProcessValues,
                "AlarmSuppression", Variant.From((ushort)0));
            Snapshot = new RepositorySampleSnapshot(
                RepositorySampleId.MachineryServer, RepositorySamplePhase.AdvertisedReady, "Owned")
            {
                RunId = Guid.NewGuid(),
                OwnsResources = true,
                CompanionOperationsAuthorized = true,
                CompanionOperationsExpireAt = Clock.Now.AddMinutes(1),
                Evidence = new RepositorySampleEvidence(new Uri(Endpoint.EndpointUrl ??
                    throw new InvalidOperationException("A test endpoint is required.")),
                    Endpoint.Server.ApplicationUri, "MachineryServer", "uri:opcfoundation.org:MachineryServer",
                    ByteString.From(SHA256.HashData(Endpoint.ServerCertificate.Span)))
            };
            Samples.SetupGet(value => value.Snapshot).Returns(() => Snapshot);
            Policy = new RepositorySampleCompanionPolicy(Samples.Object, timeProvider: Clock);
            Selection = Select(MachinerySection.ProcessValues, Process,
                [new("Analog signal", Variant.From(42.0)) { SourceNodeId = Signal }]);
            Provider = new MachineryZeroPointProvider(Selection, Policy, Clock);
        }

        public IndustrialCompanionTestSession Services { get; } = new(includeModels: false);
        public CompanionContext Context => Services.Context();
        public Mock<ITransportChannel> Transport { get; } = new(MockBehavior.Strict);
        public Mock<IRepositorySampleService> Samples { get; } = new(MockBehavior.Strict);
        public MachineryActionClock Clock { get; } = new();
        public EndpointDescription Endpoint { get; }
        public RepositorySampleSnapshot Snapshot { get; set; }
        public RepositorySampleCompanionPolicy Policy { get; }
        public MachineryMachine Machine { get; }
        public MachineryItem Process { get; }
        public NodeId Signal { get; }
        public NodeId Method { get; }
        public NodeId Status { get; }
        public NodeId Suppression { get; }
        public MachineryActionSelection Selection { get; }
        public MachineryZeroPointProvider Provider { get; }
        public bool Current { get; set; } = true;
        public Action? OnRequireCurrent { get; set; }

        public MachineryActionSelection Select(
            MachinerySection section, MachineryItem item, ArrayOf<CompanionValue> values,
            MachineryMachine? machine = null)
        {
            return new MachineryActionSelection(Context, machine ?? Machine, section, item, values, () =>
            {
                OnRequireCurrent?.Invoke();
                if (!Current)
                {
                    throw new InvalidOperationException("The test selection changed.");
                }
            });
        }

        public MachineryActionSelection SelectResult(string id = "result-exact")
        {
            NodeId node = Services.Id("result-variable");
            return Select(MachinerySection.Results,
                new MachineryItem(node, "Display label, not ID", "Result", string.Empty),
                ResultValues(node, id));
        }

        public static ArrayOf<CompanionValue> ResultValues(NodeId node, string id)
        {
            return
            [
                new("Published result", Variant.FromStructure(new ResultDataType
                {
                    ResultMetaData = new ResultMetaDataType { ResultId = id }
                })) { SourceNodeId = node },
                new("Result ID", Variant.From(id))
            ];
        }
    }

    internal sealed class MachineryActionClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }

    /// <summary>
    /// Owns only a test-specific export directory beneath the runner's working directory.
    /// </summary>
    internal sealed class MachineryActionFiles : IDisposable
    {
        public MachineryActionFiles()
        {
            Root = Path.Combine(
                Environment.CurrentDirectory, ".ualens-machinery-action-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }
        public string Destination => Path.Combine(Root, "selected.bin");

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
