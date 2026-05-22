/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Conformance.Tests.MethodServices
{
    /// <summary>
    /// compliance tests for Method Service Set – Call.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("MethodCall")]
    public class MethodCallTests : TestFixture
    {
        [Description("Call Methods_Void with no arguments. Expect Good status.")]
        [Test]
        [Property("ConformanceUnit", "Method Call")]
        [Property("Tag", "001")]
        public async Task MethodCall001CallVoidMethodAsync()
        {
            NodeId objectId = ToNodeId(Constants.MethodsFolder);
            NodeId methodId = ToNodeId(
                new ExpandedNodeId("Methods_Void", Constants.ReferenceServerNamespaceUri));

            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = default
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Void method call should return Good.");
        }

        [Description("Call Methods_Add with (1.5f, 2u). Expect output 3.5f.")]
        [Test]
        [Property("ConformanceUnit", "Method Call")]
        [Property("Tag", "004")]
        public async Task MethodCall002CallAddMethodAsync()
        {
            NodeId objectId = ToNodeId(Constants.MethodsFolder);
            NodeId methodId = ToNodeId(
                new ExpandedNodeId("Methods_Add", Constants.ReferenceServerNamespaceUri));

            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = new Variant[]
                        {
                            new(1.5f),
                            new((uint)2)
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Add method call should return Good.");
            Assert.That(response.Results[0].OutputArguments.Count, Is.GreaterThan(0),
                "Add method should return output arguments.");

            float result = (float)response.Results[0].OutputArguments[0];
            Assert.That(result, Is.EqualTo(3.5f).Within(0.001f),
                "Add(1.5, 2) should return 3.5.");
        }

        [Description("Call Methods_Hello with \"World\". Expect \"hello World\".")]
        [Test]
        [Property("ConformanceUnit", "Method Call")]
        [Property("Tag", "007")]
        public async Task MethodCall003CallHelloMethodAsync()
        {
            NodeId objectId = ToNodeId(Constants.MethodsFolder);
            NodeId methodId = ToNodeId(
                new ExpandedNodeId("Methods_Hello", Constants.ReferenceServerNamespaceUri));

            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = new Variant[]
                        {
                            new("World")
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Hello method call should return Good.");
            Assert.That(response.Results[0].OutputArguments.Count, Is.GreaterThan(0),
                "Hello method should return output arguments.");

            string result = (string)response.Results[0].OutputArguments[0];
            Assert.That(result, Is.EqualTo("hello World"),
                "Hello('World') should return 'hello World'.");
        }

        [Description("Call Methods_Multiply with appropriate arguments.")]
        [Test]
        [Property("ConformanceUnit", "Method Call")]
        [Property("Tag", "008")]
        public async Task MethodCall004CallMultiplyMethodAsync()
        {
            NodeId objectId = ToNodeId(Constants.MethodsFolder);
            NodeId methodId = ToNodeId(
                new ExpandedNodeId("Methods_Multiply", Constants.ReferenceServerNamespaceUri));

            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = new Variant[]
                        {
                            new((short)3),
                            new((ushort)4)
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Multiply method call should return Good.");
            Assert.That(response.Results[0].OutputArguments.Count, Is.GreaterThan(0),
                "Multiply method should return output arguments.");
        }

        [Description("Call Void and Hello in a single request. Both should return Good.")]
        [Test]
        [Property("ConformanceUnit", "Method Call")]
        [Property("Tag", "005")]
        public async Task MethodCall005CallMultipleMethodsInOneRequestAsync()
        {
            NodeId objectId = ToNodeId(Constants.MethodsFolder);
            NodeId voidMethodId = ToNodeId(
                new ExpandedNodeId("Methods_Void", Constants.ReferenceServerNamespaceUri));
            NodeId helloMethodId = ToNodeId(
                new ExpandedNodeId("Methods_Hello", Constants.ReferenceServerNamespaceUri));

            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = objectId,
                        MethodId = voidMethodId,
                        InputArguments = default
                    },
                    new() {
                        ObjectId = objectId,
                        MethodId = helloMethodId,
                        InputArguments = new Variant[]
                        {
                            new("Test")
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(2));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Void method should return Good.");
            Assert.That(StatusCode.IsGood(response.Results[1].StatusCode), Is.True,
                "Hello method should return Good.");
        }

        [Description("Call Methods_Output which has output arguments only.")]
        [Test]
        [Property("ConformanceUnit", "Method Call")]
        [Property("Tag", "003")]
        public async Task MethodCall006CallOutputOnlyMethodAsync()
        {
            NodeId objectId = ToNodeId(Constants.MethodsFolder);
            NodeId methodId = ToNodeId(
                new ExpandedNodeId("Methods_Output", Constants.ReferenceServerNamespaceUri));

            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = default
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Output-only method call should return Good.");
            Assert.That(response.Results[0].OutputArguments.Count, Is.GreaterThan(0),
                "Output-only method should return output arguments.");
        }

        [Description("Call Methods_Input which has input arguments only.")]
        [Test]
        [Property("ConformanceUnit", "Method Call")]
        [Property("Tag", "009")]
        public async Task MethodCall007CallInputOnlyMethodAsync()
        {
            NodeId objectId = ToNodeId(Constants.MethodsFolder);
            NodeId methodId = ToNodeId(
                new ExpandedNodeId("Methods_Input", Constants.ReferenceServerNamespaceUri));

            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = new Variant[]
                        {
                            new("TestInput")
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Input-only method call should return Good.");
        }

        [Description("Call a non-existent method. Expect BadNodeIdUnknown or BadMethodInvalid.")]
        [Test]
        [Property("ConformanceUnit", "Method Call")]
        [Property("Tag", "Err-005")]
        public async Task MethodCallErr001CallNonExistentMethodAsync()
        {
            NodeId objectId = ToNodeId(Constants.MethodsFolder);

            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = objectId,
                        MethodId = Constants.InvalidNodeId,
                        InputArguments = default
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True,
                "Calling a non-existent method should return a Bad status.");
        }

        [Description("Call Methods_Void with wrong ObjectId. Expect BadMethodInvalid or similar Bad status.")]
        [Test]
        [Property("ConformanceUnit", "Method Call")]
        [Property("Tag", "Err-006")]
        public async Task MethodCallErr002CallWithWrongObjectIdAsync()
        {
            NodeId methodId = ToNodeId(
                new ExpandedNodeId("Methods_Void", Constants.ReferenceServerNamespaceUri));

            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = ObjectIds.Server,
                        MethodId = methodId,
                        InputArguments = default
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True,
                "Calling method with wrong ObjectId should return a Bad status.");
        }

        [Description("Call Methods_Add with only 1 argument. Expect BadArgumentsMissing or BadInvalidArgument.")]
        [Test]
        [Property("ConformanceUnit", "Method Call")]
        [Property("Tag", "Err-003")]
        public async Task MethodCallErr003CallWithMissingArgumentsAsync()
        {
            NodeId objectId = ToNodeId(Constants.MethodsFolder);
            NodeId methodId = ToNodeId(
                new ExpandedNodeId("Methods_Add", Constants.ReferenceServerNamespaceUri));

            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = new Variant[]
                        {
                            new(1.5f)
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True,
                "Calling Add with missing arguments should return a Bad status.");
        }

        [Description("Call Methods_Void with unexpected arguments. Expect BadInvalidArgument or BadTooManyArguments.")]
        [Test]
        [Property("ConformanceUnit", "Method Call")]
        [Property("Tag", "Err-004")]
        public async Task MethodCallErr004CallWithTooManyArgumentsAsync()
        {
            NodeId objectId = ToNodeId(Constants.MethodsFolder);
            NodeId methodId = ToNodeId(
                new ExpandedNodeId("Methods_Void", Constants.ReferenceServerNamespaceUri));

            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = new Variant[]
                        {
                            new("unexpected")
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True,
                "Calling Void method with arguments should return a Bad status.");
        }

        [Description("Call GetMonitoredItems on Server with an active subscription.")]
        [Test]
        [Property("ConformanceUnit", "Method Call")]
        [Property("Tag", "016")]
        public async Task MethodCall008VerifyMethodNodeClassIsMethodAsync()
        {
            CancellationToken ct = CancellationToken.None;

            // Create a subscription
            CreateSubscriptionResponse subResp = await Session.CreateSubscriptionAsync(
                null, 1000, 100, 10, 0, true, 0, ct).ConfigureAwait(false);
            uint subscriptionId = subResp.SubscriptionId;

            try
            {
                // Add monitored items
                CreateMonitoredItemsResponse createItems = await Session.CreateMonitoredItemsAsync(
                    null, subscriptionId, TimestampsToReturn.Both,
                    new MonitoredItemCreateRequest[]
                    {
                        new() {
                            ItemToMonitor = new ReadValueId
                            {
                                NodeId = ToNodeId(Constants.ScalarStaticInt32),
                                AttributeId = Attributes.Value
                            },
                            MonitoringMode = MonitoringMode.Reporting,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 1,
                                SamplingInterval = 1000,
                                QueueSize = 1,
                                DiscardOldest = true
                            }
                        },
                        new() {
                            ItemToMonitor = new ReadValueId
                            {
                                NodeId = ToNodeId(Constants.ScalarStaticDouble),
                                AttributeId = Attributes.Value
                            },
                            MonitoringMode = MonitoringMode.Reporting,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 2,
                                SamplingInterval = 1000,
                                QueueSize = 1,
                                DiscardOldest = true
                            }
                        }
                    }.ToArrayOf(), ct).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(createItems.Results[0].StatusCode), Is.True);
                Assert.That(StatusCode.IsGood(createItems.Results[1].StatusCode), Is.True);

                // Call GetMonitoredItems
                CallResponse callResponse = await Session.CallAsync(
                    null,
                    new CallMethodRequest[]
                    {
                        new() {
                            ObjectId = ObjectIds.Server,
                            MethodId = MethodIds.Server_GetMonitoredItems,
                            InputArguments = new Variant[]
                            {
                                new(subscriptionId)
                            }.ToArrayOf()
                        }
                    }.ToArrayOf(), ct).ConfigureAwait(false);

                Assert.That(callResponse.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsGood(callResponse.Results[0].StatusCode), Is.True,
                    "GetMonitoredItems should return Good.");
                Assert.That(callResponse.Results[0].OutputArguments.Count, Is.EqualTo(2),
                    "GetMonitoredItems returns server handles and client handles.");
            }
            finally
            {
                await Session.DeleteSubscriptionsAsync(
                    null, new uint[] { subscriptionId }.ToArrayOf(), ct).ConfigureAwait(false);
            }
        }

        [Description("Call a method that has IN parameters only.")]
        [Test]
        [Property("ConformanceUnit", "Method Call")]
        [Property("Tag", "002")]
        public async Task MethodCallInputOnlyAsync()
        {
            CancellationToken ct = CancellationToken.None;
            NodeId objectId = ToNodeId(Constants.MethodsFolder);
            NodeId methodId = ToNodeId(Constants.MethodInput);

            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = new Variant[]
                        {
                            new("TestInput")
                        }.ToArrayOf()
                    }
                }.ToArrayOf(), ct).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Input-only method call should return Good.");
        }

        [Description("Call the same method multiple times in a single Call request.")]
        [Test]
        [Property("ConformanceUnit", "Method Call")]
        [Property("Tag", "006")]
        public async Task MethodCallSameMethodMultipleTimesAsync()
        {
            CancellationToken ct = CancellationToken.None;
            NodeId objectId = ToNodeId(Constants.MethodsFolder);
            NodeId methodId = ToNodeId(Constants.MethodHello);

            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = new Variant[]
                        {
                            new("First")
                        }.ToArrayOf()
                    },
                    new() {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = new Variant[]
                        {
                            new("Second")
                        }.ToArrayOf()
                    },
                    new() {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = new Variant[]
                        {
                            new("Third")
                        }.ToArrayOf()
                    }
                }.ToArrayOf(), ct).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(3));
            for (int i = 0; i < 3; i++)
            {
                Assert.That(StatusCode.IsGood(response.Results[i].StatusCode), Is.True,
                    $"Call {i + 1} should return Good.");
            }
        }

        [Description("Call with an invalid Object NodeId.")]
        [Test]
        [Property("ConformanceUnit", "Method Call")]
        [Property("Tag", "Err-001")]
        public async Task MethodCallErrInvalidObjectNodeIdAsync()
        {
            CancellationToken ct = CancellationToken.None;
            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = Constants.InvalidNodeId,
                        MethodId = ToNodeId(Constants.MethodVoid)
                    }
                }.ToArrayOf(), ct).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True,
                "Invalid Object NodeId should return Bad status.");
        }

        [Description("Call with valid object but invalid Method NodeId.")]
        [Test]
        [Property("ConformanceUnit", "Method Call")]
        [Property("Tag", "Err-002")]
        public async Task MethodCallErrInvalidMethodNodeIdAsync()
        {
            CancellationToken ct = CancellationToken.None;
            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = ToNodeId(Constants.MethodsFolder),
                        MethodId = Constants.InvalidNodeId
                    }
                }.ToArrayOf(), ct).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True,
                "Invalid Method NodeId should return Bad status.");
        }

        [Description("Call method with wrong data type for input arguments.")]
        [Test]
        [Property("ConformanceUnit", "Method Call")]
        [Property("Tag", "Err-004")]
        public async Task MethodCallErrWrongArgumentTypesAsync()
        {
            CancellationToken ct = CancellationToken.None;
            NodeId objectId = ToNodeId(Constants.MethodsFolder);
            NodeId methodId = ToNodeId(Constants.MethodMultiply);

            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = new Variant[]
                        {
                            new("not_a_number"),
                            new("also_not_a_number")
                        }.ToArrayOf()
                    }
                }.ToArrayOf(), ct).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True,
                "Wrong argument types should return Bad status.");
        }
    }
}
