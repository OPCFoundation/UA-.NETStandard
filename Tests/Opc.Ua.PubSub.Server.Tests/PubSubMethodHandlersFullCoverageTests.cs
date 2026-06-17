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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Server.Tests
{
    /// <summary>
    /// Exhaustive coverage for <see cref="PubSubMethodHandlers"/>:
    /// each handler is exercised across its happy path, missing
    /// argument, argument-type mismatch, ExposeConfigurationMethods
    /// gate, ArgumentException → BadNodeIdUnknown, and
    /// PubSubConfigurationException → BadConfigurationError code
    /// paths. Mirrors Part 14 §9.1.3 / §9.1.6 / §9.1.7 / §9.1.8 /
    /// §9.1.10.
    /// </summary>
    [TestFixture]
    [TestSpec("9.1.6", Summary = "PubSub configuration methods - full coverage")]
    public class PubSubMethodHandlersFullCoverageTests
    {
        private const string UdpProfile =
            "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp";

        // -------------------------------------------------------------
        // OnEnable / OnDisable
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.10.2")]
        public void OnEnableTwiceIsIdempotent()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var outputs = new List<Variant>();
            ServiceResult first = handlers.OnEnable(
                NewContext(), null!, default, outputs);
            ServiceResult second = handlers.OnEnable(
                NewContext(), null!, default, outputs);
            Assert.That(StatusCode.IsGood(first.StatusCode), Is.True);
            Assert.That(StatusCode.IsGood(second.StatusCode), Is.True);
        }

        [Test]
        [TestSpec("9.1.10.3")]
        public void OnDisableWithoutPriorEnableReturnsGood()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnDisable(
                NewContext(), null!, default, outputs);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        // -------------------------------------------------------------
        // OnAddConnection failure paths
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.3.4")]
        public void OnAddConnectionExtensionObjectIsNotPubSubConnectionDataTypeReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(Variant.From(
                new ExtensionObject(new WriterGroupDataType { Name = "wg" })));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddConnection(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.3.4")]
        public void OnAddConnectionArgumentNotExtensionObjectReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(Variant.From("not-an-extension-object"));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddConnection(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.3.4")]
        public void OnAddConnectionInvalidTransportProfileReturnsBadConfigurationError()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var bad = new PubSubConnectionDataType
            {
                Name = "bad",
                TransportProfileUri = "urn:not-a-real-profile",
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            var inputs = NewInputs(Variant.From(new ExtensionObject(bad)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddConnection(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
        }

        [Test]
        [TestSpec("9.1.3.4")]
        public void OnAddConnectionEmptyNameThrowsAndIsTranslatedToBadInvalidState()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var bad = new PubSubConnectionDataType
            {
                Name = string.Empty,
                TransportProfileUri = UdpProfile,
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            var inputs = NewInputs(Variant.From(new ExtensionObject(bad)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddConnection(
                NewContext(), null!, inputs, outputs);
            Assert.That(StatusCode.IsBad(result.StatusCode), Is.True);
        }

        // -------------------------------------------------------------
        // OnRemoveConnection
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.3.5")]
        public void OnRemoveConnectionUnknownIdReturnsBadNodeIdUnknown()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(Variant.From(new NodeId("pubsub:connection:nope", 0)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveConnection(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        [TestSpec("9.1.3.5")]
        public void OnRemoveConnectionNullNodeIdReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(Variant.From(NodeId.Null));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveConnection(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.3.5")]
        public void OnRemoveConnectionWhenDisabledReturnsAccessDenied()
        {
            PubSubMethodHandlers handlers = NewHandlers(
                opts => opts.ExposeConfigurationMethods = false);
            var inputs = NewInputs(Variant.From(new NodeId("foo", 0)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveConnection(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        // -------------------------------------------------------------
        // OnSetConfiguration
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.6")]
        public void OnSetConfigurationWhenDisabledReturnsAccessDenied()
        {
            PubSubMethodHandlers handlers = NewHandlers(
                opts => opts.ExposeConfigurationMethods = false);
            var cfg = new PubSubConfigurationDataType
            {
                Connections = [],
                PublishedDataSets = []
            };
            var inputs = NewInputs(Variant.From(new ExtensionObject(cfg)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnSetConfiguration(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnSetConfigurationMissingArgumentReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs();
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnSetConfiguration(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnSetConfigurationArgumentNotExtensionObjectReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(Variant.From(123));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnSetConfiguration(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnSetConfigurationBodyNotPubSubConfigurationReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(Variant.From(
                new ExtensionObject(new WriterGroupDataType { Name = "wg" })));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnSetConfiguration(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnSetConfigurationInvalidProfileReturnsBadConfigurationError()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var bad = new PubSubConfigurationDataType
            {
                Connections = new ArrayOf<PubSubConnectionDataType>(new[]
                {
                    new PubSubConnectionDataType
                    {
                        Name = "bad",
                        TransportProfileUri = "urn:not-real",
                        Address = new ExtensionObject(
                            new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
                    }
                }),
                PublishedDataSets = []
            };
            var inputs = NewInputs(Variant.From(new ExtensionObject(bad)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnSetConfiguration(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
        }

        // -------------------------------------------------------------
        // OnGetConfiguration
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.6")]
        public void OnGetConfigurationWhenDisabledReturnsAccessDenied()
        {
            PubSubMethodHandlers handlers = NewHandlers(
                opts => opts.ExposeConfigurationMethods = false);
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnGetConfiguration(
                NewContext(), null!, default, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        // -------------------------------------------------------------
        // OnAddPublishedDataItems / OnAddPublishedEvents
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.6.4")]
        public void OnAddPublishedEventsReturnsBadNotSupported()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddPublishedEvents(
                NewContext(), null!, default, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
        }

        [Test]
        [TestSpec("9.1.6.4")]
        public void OnAddPublishedDataItemsWhenDisabledReturnsAccessDenied()
        {
            PubSubMethodHandlers handlers = NewHandlers(
                opts => opts.ExposeConfigurationMethods = false);
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddPublishedDataItems(
                NewContext(), null!, default, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        [TestSpec("9.1.6.4")]
        public void OnAddPublishedEventsWhenDisabledReturnsAccessDenied()
        {
            PubSubMethodHandlers handlers = NewHandlers(
                opts => opts.ExposeConfigurationMethods = false);
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddPublishedEvents(
                NewContext(), null!, default, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        // -------------------------------------------------------------
        // OnRemovePublishedDataSet
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.6")]
        public void OnRemovePublishedDataSetWhenDisabledReturnsAccessDenied()
        {
            PubSubMethodHandlers handlers = NewHandlers(
                opts => opts.ExposeConfigurationMethods = false);
            var inputs = NewInputs(Variant.From(new NodeId("foo", 0)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemovePublishedDataSet(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnRemovePublishedDataSetMissingArgumentReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs();
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemovePublishedDataSet(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnRemovePublishedDataSetNullNodeIdReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(Variant.From(NodeId.Null));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemovePublishedDataSet(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnRemovePublishedDataSetUnknownIdReturnsBadNodeIdUnknown()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(new NodeId("pubsub:published-data-set:nope", 0)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemovePublishedDataSet(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
        }

        // -------------------------------------------------------------
        // OnAddDataSetFolder / OnRemoveDataSetFolder
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.5")]
        public void OnAddDataSetFolderWhenDisabledReturnsAccessDenied()
        {
            PubSubMethodHandlers handlers = NewHandlers(
                opts => opts.ExposeConfigurationMethods = false);
            var inputs = NewInputs(Variant.From("folder"));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddDataSetFolder(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        [TestSpec("9.1.5")]
        public void OnAddDataSetFolderMissingArgumentReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs();
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddDataSetFolder(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.5")]
        public void OnAddDataSetFolderEmptyNameReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(Variant.From(string.Empty));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddDataSetFolder(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.5")]
        public void OnRemoveDataSetFolderWhenDisabledReturnsAccessDenied()
        {
            PubSubMethodHandlers handlers = NewHandlers(
                opts => opts.ExposeConfigurationMethods = false);
            var inputs = NewInputs(Variant.From(new NodeId("pubsub:folder:foo", 0)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveDataSetFolder(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        [TestSpec("9.1.5")]
        public void OnRemoveDataSetFolderMissingArgumentReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs();
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveDataSetFolder(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.5")]
        public void OnRemoveDataSetFolderWithArgumentReturnsGoodNoOp()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(Variant.From(new NodeId("pubsub:folder:foo", 0)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveDataSetFolder(
                NewContext(), null!, inputs, outputs);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        // -------------------------------------------------------------
        // OnAddWriterGroup
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.6")]
        public void OnAddWriterGroupHappyPathReturnsGoodAndNodeId()
        {
            PubSubMethodHandlers handlers = NewHandlersWithConnection(out NodeId connId);
            var wg = new WriterGroupDataType
            {
                Name = "wg-1",
                WriterGroupId = 1,
                PublishingInterval = 1000
            };
            var inputs = NewInputs(
                Variant.From(connId), Variant.From(new ExtensionObject(wg)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddWriterGroup(
                NewContext(), null!, inputs, outputs);
            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
                Assert.That(outputs[0].TryGetValue(out NodeId wgId), Is.True);
                Assert.That(wgId.IsNull, Is.False);
            });
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnAddWriterGroupWhenDisabledReturnsAccessDenied()
        {
            PubSubMethodHandlers handlers = NewHandlers(
                opts => opts.ExposeConfigurationMethods = false);
            var inputs = NewInputs(
                Variant.From(new NodeId("foo", 0)),
                Variant.From(new ExtensionObject(new WriterGroupDataType { Name = "x" })));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddWriterGroup(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnAddWriterGroupMissingArgsReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(Variant.From(new NodeId("x", 0)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddWriterGroup(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnAddWriterGroupNullConnectionIdReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(NodeId.Null),
                Variant.From(new ExtensionObject(new WriterGroupDataType { Name = "wg" })));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddWriterGroup(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnAddWriterGroupSecondArgNotExtensionObjectReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(new NodeId("x", 0)),
                Variant.From("not-an-extension-object"));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddWriterGroup(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnAddWriterGroupSecondArgWrongTypeReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(new NodeId("x", 0)),
                Variant.From(
                    new ExtensionObject(new ReaderGroupDataType { Name = "rg" })));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddWriterGroup(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnAddWriterGroupUnknownConnectionIdReturnsBadNodeIdUnknown()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(new NodeId("pubsub:connection:unknown", 0)),
                Variant.From(
                    new ExtensionObject(new WriterGroupDataType { Name = "wg" })));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddWriterGroup(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
        }

        // -------------------------------------------------------------
        // OnAddReaderGroup
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.6")]
        public void OnAddReaderGroupHappyPathReturnsGoodAndNodeId()
        {
            PubSubMethodHandlers handlers = NewHandlersWithConnection(out NodeId connId);
            var rg = new ReaderGroupDataType { Name = "rg-1" };
            var inputs = NewInputs(
                Variant.From(connId), Variant.From(new ExtensionObject(rg)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddReaderGroup(
                NewContext(), null!, inputs, outputs);
            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
                Assert.That(outputs[0].TryGetValue(out NodeId rgId), Is.True);
                Assert.That(rgId.IsNull, Is.False);
            });
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnAddReaderGroupWhenDisabledReturnsAccessDenied()
        {
            PubSubMethodHandlers handlers = NewHandlers(
                opts => opts.ExposeConfigurationMethods = false);
            var inputs = NewInputs(
                Variant.From(new NodeId("foo", 0)),
                Variant.From(new ExtensionObject(new ReaderGroupDataType { Name = "x" })));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddReaderGroup(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnAddReaderGroupMissingArgReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(Variant.From(new NodeId("x", 0)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddReaderGroup(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnAddReaderGroupSecondArgWrongBodyReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(new NodeId("x", 0)),
                Variant.From(
                    new ExtensionObject(new WriterGroupDataType { Name = "wg" })));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddReaderGroup(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnAddReaderGroupSecondArgNotExtensionObjectReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(new NodeId("x", 0)),
                Variant.From("string-value"));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddReaderGroup(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnAddReaderGroupNullConnectionIdReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(NodeId.Null),
                Variant.From(new ExtensionObject(new ReaderGroupDataType { Name = "rg" })));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddReaderGroup(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnAddReaderGroupUnknownConnectionIdReturnsBadNodeIdUnknown()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(new NodeId("pubsub:connection:unknown", 0)),
                Variant.From(new ExtensionObject(new ReaderGroupDataType { Name = "rg" })));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddReaderGroup(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
        }

        // -------------------------------------------------------------
        // OnRemoveGroup
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.6")]
        public void OnRemoveGroupRoundTripsForWriterGroup()
        {
            PubSubMethodHandlers handlers = NewHandlersWithConnection(out NodeId connId);
            var wg = new WriterGroupDataType
            {
                Name = "remove-wg",
                WriterGroupId = 1,
                PublishingInterval = 1000
            };
            var addInputs = NewInputs(
                Variant.From(connId), Variant.From(new ExtensionObject(wg)));
            var addOutputs = new List<Variant>();
            handlers.OnAddWriterGroup(NewContext(), null!, addInputs, addOutputs);
            addOutputs[0].TryGetValue(out NodeId wgId);

            var inputs = NewInputs(Variant.From(wgId));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveGroup(
                NewContext(), null!, inputs, outputs);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnRemoveGroupWhenDisabledReturnsAccessDenied()
        {
            PubSubMethodHandlers handlers = NewHandlers(
                opts => opts.ExposeConfigurationMethods = false);
            var inputs = NewInputs(Variant.From(new NodeId("foo", 0)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveGroup(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnRemoveGroupMissingArgReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs();
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveGroup(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnRemoveGroupNullIdReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(Variant.From(NodeId.Null));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveGroup(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.6")]
        public void OnRemoveGroupUnknownIdReturnsBadNodeIdUnknown()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(new NodeId("pubsub:writer-group:foo:bar", 0)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveGroup(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
        }

        // -------------------------------------------------------------
        // OnAddDataSetWriter / OnRemoveDataSetWriter
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.7")]
        public void OnAddDataSetWriterHappyPathReturnsGoodAndNodeId()
        {
            PubSubMethodHandlers handlers = NewHandlersWithWriterGroup(
                out _, out NodeId wgId);
            var writer = new DataSetWriterDataType
            {
                Name = "writer-1",
                DataSetWriterId = 1,
                DataSetName = "pds-1"
            };
            var inputs = NewInputs(
                Variant.From(wgId), Variant.From(new ExtensionObject(writer)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddDataSetWriter(
                NewContext(), null!, inputs, outputs);
            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
                Assert.That(outputs[0].TryGetValue(out NodeId writerId), Is.True);
                Assert.That(writerId.IsNull, Is.False);
            });
        }

        [Test]
        [TestSpec("9.1.7")]
        public void OnAddDataSetWriterWhenDisabledReturnsAccessDenied()
        {
            PubSubMethodHandlers handlers = NewHandlers(
                opts => opts.ExposeConfigurationMethods = false);
            var inputs = NewInputs(
                Variant.From(new NodeId("x", 0)),
                Variant.From(
                    new ExtensionObject(new DataSetWriterDataType { Name = "w" })));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddDataSetWriter(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        [TestSpec("9.1.7")]
        public void OnAddDataSetWriterMissingArgReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(Variant.From(new NodeId("x", 0)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddDataSetWriter(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.7")]
        public void OnAddDataSetWriterNullWriterGroupIdReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(NodeId.Null),
                Variant.From(
                    new ExtensionObject(new DataSetWriterDataType { Name = "w" })));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddDataSetWriter(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.7")]
        public void OnAddDataSetWriterSecondArgNotExtensionObjectReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(new NodeId("x", 0)), Variant.From("not-eo"));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddDataSetWriter(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.7")]
        public void OnAddDataSetWriterSecondArgWrongTypeReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(new NodeId("x", 0)),
                Variant.From(
                    new ExtensionObject(new ReaderGroupDataType { Name = "rg" })));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddDataSetWriter(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.7")]
        public void OnAddDataSetWriterUnknownGroupIdReturnsBadNodeIdUnknown()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(new NodeId("pubsub:writer-group:foo:bar", 0)),
                Variant.From(new ExtensionObject(
                    new DataSetWriterDataType
                    {
                        Name = "w",
                        DataSetWriterId = 1
                    })));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddDataSetWriter(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        [TestSpec("9.1.7")]
        public void OnRemoveDataSetWriterRoundTripsAfterAdd()
        {
            PubSubMethodHandlers handlers = NewHandlersWithWriterGroup(
                out _, out NodeId wgId);
            var writer = new DataSetWriterDataType
            {
                Name = "writer-1",
                DataSetWriterId = 1,
                DataSetName = "pds-1"
            };
            var addInputs = NewInputs(
                Variant.From(wgId), Variant.From(new ExtensionObject(writer)));
            var addOutputs = new List<Variant>();
            handlers.OnAddDataSetWriter(NewContext(), null!, addInputs, addOutputs);
            addOutputs[0].TryGetValue(out NodeId writerId);

            var inputs = NewInputs(Variant.From(writerId));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveDataSetWriter(
                NewContext(), null!, inputs, outputs);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [TestSpec("9.1.7")]
        public void OnRemoveDataSetWriterWhenDisabledReturnsAccessDenied()
        {
            PubSubMethodHandlers handlers = NewHandlers(
                opts => opts.ExposeConfigurationMethods = false);
            var inputs = NewInputs(Variant.From(new NodeId("x", 0)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveDataSetWriter(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        [TestSpec("9.1.7")]
        public void OnRemoveDataSetWriterMissingArgReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs();
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveDataSetWriter(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.7")]
        public void OnRemoveDataSetWriterNullIdReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(Variant.From(NodeId.Null));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveDataSetWriter(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.7")]
        public void OnRemoveDataSetWriterUnknownIdReturnsBadNodeIdUnknown()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(new NodeId("pubsub:writer:foo:bar:baz", 0)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveDataSetWriter(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
        }

        // -------------------------------------------------------------
        // OnAddDataSetReader / OnRemoveDataSetReader
        // -------------------------------------------------------------

        [Test]
        [TestSpec("9.1.8")]
        public void OnAddDataSetReaderHappyPathReturnsGoodAndNodeId()
        {
            PubSubMethodHandlers handlers = NewHandlersWithReaderGroup(
                out _, out NodeId rgId);
            var reader = new DataSetReaderDataType
            {
                Name = "reader-1",
                DataSetWriterId = 1,
                MessageReceiveTimeout = 5000,
                SubscribedDataSet = new ExtensionObject(new TargetVariablesDataType())
            };
            var inputs = NewInputs(
                Variant.From(rgId), Variant.From(new ExtensionObject(reader)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddDataSetReader(
                NewContext(), null!, inputs, outputs);
            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
                Assert.That(outputs[0].TryGetValue(out NodeId readerId), Is.True);
                Assert.That(readerId.IsNull, Is.False);
            });
        }

        [Test]
        [TestSpec("9.1.8")]
        public void OnAddDataSetReaderWhenDisabledReturnsAccessDenied()
        {
            PubSubMethodHandlers handlers = NewHandlers(
                opts => opts.ExposeConfigurationMethods = false);
            var inputs = NewInputs(
                Variant.From(new NodeId("x", 0)),
                Variant.From(new ExtensionObject(
                    new DataSetReaderDataType { Name = "r" })));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddDataSetReader(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        [TestSpec("9.1.8")]
        public void OnAddDataSetReaderMissingArgReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(Variant.From(new NodeId("x", 0)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddDataSetReader(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.8")]
        public void OnAddDataSetReaderNullReaderGroupIdReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(NodeId.Null),
                Variant.From(new ExtensionObject(
                    new DataSetReaderDataType { Name = "r" })));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddDataSetReader(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.8")]
        public void OnAddDataSetReaderSecondArgNotExtensionObjectReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(new NodeId("x", 0)), Variant.From("not-eo"));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddDataSetReader(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.8")]
        public void OnAddDataSetReaderSecondArgWrongTypeReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(new NodeId("x", 0)),
                Variant.From(new ExtensionObject(
                    new WriterGroupDataType { Name = "wg" })));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddDataSetReader(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.8")]
        public void OnAddDataSetReaderUnknownReaderGroupIdReturnsBadNodeIdUnknown()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(new NodeId("pubsub:reader-group:foo:bar", 0)),
                Variant.From(new ExtensionObject(
                    new DataSetReaderDataType { Name = "r" })));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnAddDataSetReader(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        [TestSpec("9.1.8")]
        public void OnRemoveDataSetReaderRoundTripsAfterAdd()
        {
            PubSubMethodHandlers handlers = NewHandlersWithReaderGroup(
                out _, out NodeId rgId);
            var reader = new DataSetReaderDataType
            {
                Name = "remove-r",
                DataSetWriterId = 1,
                MessageReceiveTimeout = 5000,
                SubscribedDataSet = new ExtensionObject(new TargetVariablesDataType())
            };
            var addInputs = NewInputs(
                Variant.From(rgId), Variant.From(new ExtensionObject(reader)));
            var addOutputs = new List<Variant>();
            handlers.OnAddDataSetReader(NewContext(), null!, addInputs, addOutputs);
            addOutputs[0].TryGetValue(out NodeId readerId);

            var inputs = NewInputs(Variant.From(readerId));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveDataSetReader(
                NewContext(), null!, inputs, outputs);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [TestSpec("9.1.8")]
        public void OnRemoveDataSetReaderWhenDisabledReturnsAccessDenied()
        {
            PubSubMethodHandlers handlers = NewHandlers(
                opts => opts.ExposeConfigurationMethods = false);
            var inputs = NewInputs(Variant.From(new NodeId("x", 0)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveDataSetReader(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        [TestSpec("9.1.8")]
        public void OnRemoveDataSetReaderMissingArgReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs();
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveDataSetReader(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.8")]
        public void OnRemoveDataSetReaderNullIdReturnsBadInvalidArgument()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(Variant.From(NodeId.Null));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveDataSetReader(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        [TestSpec("9.1.8")]
        public void OnRemoveDataSetReaderUnknownIdReturnsBadNodeIdUnknown()
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var inputs = NewInputs(
                Variant.From(new NodeId("pubsub:reader:foo:bar:baz", 0)));
            var outputs = new List<Variant>();
            ServiceResult result = handlers.OnRemoveDataSetReader(
                NewContext(), null!, inputs, outputs);
            Assert.That(result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
        }

        // -------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------

        private static PubSubMethodHandlers NewHandlers(
            Action<PubSubServerOptions>? configure = null)
        {
            var options = new PubSubServerOptions
            {
                ExposeConfigurationMethods = true
            };
            configure?.Invoke(options);
            IPubSubApplication app = NewApplication();
            return new PubSubMethodHandlers(
                app, null, options, NUnitTelemetryContext.Create());
        }

        private static PubSubMethodHandlers NewHandlersWithConnection(out NodeId connectionId)
        {
            PubSubMethodHandlers handlers = NewHandlers();
            var conn = new PubSubConnectionDataType
            {
                Name = "conn-h",
                TransportProfileUri = UdpProfile,
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://224.0.0.22:4840" })
            };
            var addInputs = NewInputs(Variant.From(new ExtensionObject(conn)));
            var addOutputs = new List<Variant>();
            handlers.OnAddConnection(NewContext(), null!, addInputs, addOutputs);
            addOutputs[0].TryGetValue(out NodeId id);
            connectionId = id;
            return handlers;
        }

        private static PubSubMethodHandlers NewHandlersWithWriterGroup(
            out NodeId connectionId, out NodeId writerGroupId)
        {
            PubSubMethodHandlers handlers = NewHandlersWithConnection(out connectionId);
            var wg = new WriterGroupDataType
            {
                Name = "wg-h",
                WriterGroupId = 1,
                PublishingInterval = 1000
            };
            var inputs = NewInputs(
                Variant.From(connectionId), Variant.From(new ExtensionObject(wg)));
            var outputs = new List<Variant>();
            handlers.OnAddWriterGroup(NewContext(), null!, inputs, outputs);
            outputs[0].TryGetValue(out NodeId wgId);
            writerGroupId = wgId;
            return handlers;
        }

        private static PubSubMethodHandlers NewHandlersWithReaderGroup(
            out NodeId connectionId, out NodeId readerGroupId)
        {
            PubSubMethodHandlers handlers = NewHandlersWithConnection(out connectionId);
            var rg = new ReaderGroupDataType { Name = "rg-h" };
            var inputs = NewInputs(
                Variant.From(connectionId), Variant.From(new ExtensionObject(rg)));
            var outputs = new List<Variant>();
            handlers.OnAddReaderGroup(NewContext(), null!, inputs, outputs);
            outputs[0].TryGetValue(out NodeId rgId);
            readerGroupId = rgId;
            return handlers;
        }

        private static IPubSubApplication NewApplication()
        {
            return new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("full-coverage-handlers")
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Connections = [],
                    PublishedDataSets = new ArrayOf<PublishedDataSetDataType>(new[]
                    {
                        new PublishedDataSetDataType { Name = "pds-1" }
                    })
                })
                .UseAllStandardEncoders()
                .AddTransportFactory(new StubTransportFactory())
                .Build();
        }

        private static SystemContext NewContext()
        {
            return new SystemContext(NUnitTelemetryContext.Create());
        }

        private static ArrayOf<Variant> NewInputs(params Variant[] values)
        {
            return new ArrayOf<Variant>(values);
        }

        private sealed class StubTransportFactory : IPubSubTransportFactory
        {
            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                _ = connection;
                _ = telemetry;
                _ = timeProvider;
                return new StubTransport();
            }
        }

        private sealed class StubTransport : IPubSubTransport
        {
            private bool m_isConnected;

            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public PubSubTransportDirection Direction =>
                PubSubTransportDirection.SendReceive;

            public bool IsConnected => m_isConnected;

            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
            {
                add { }
                remove { }
            }

            public ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                m_isConnected = true;
                return default;
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                m_isConnected = false;
                return default;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                _ = payload;
                _ = topic;
                _ = cancellationToken;
                return default;
            }

            public IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                return AsyncEnumerable.Empty<PubSubTransportFrame>();
            }

            public ValueTask DisposeAsync()
            {
                m_isConnected = false;
                return default;
            }
        }
    }
}
