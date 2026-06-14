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

#nullable enable

using System;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.RestApi
{
    /// <summary>
    /// Unit tests for <see cref="RestApiServiceRoutes"/> — the static
    /// service route table for the HTTPS REST binding
    /// (OPC UA Part 6 §G.3 "OpenAPI Mapping", v1.05.07).
    /// </summary>
    /// <remarks>
    /// The expected paths and operation IDs in this fixture are pinned
    /// against <c>opc.ua.openapi.allservices.json</c> from the spec
    /// release. Updating either side of the contract requires updating
    /// both.
    /// </remarks>
    [TestFixture]
    [Category("RestApiServiceRoutes")]
    [Parallelizable]
    public class RestApiServiceRoutesTests
    {
        private static readonly (string Path, string OperationId)[] s_expectedRoutes =
        [
            ("/read", "Read"),
            ("/write", "Write"),
            ("/historyread", "HistoryRead"),
            ("/historyupdate", "HistoryUpdate"),
            ("/call", "Call"),
            ("/browse", "Browse"),
            ("/browsenext", "BrowseNext"),
            ("/translate", "TranslateBrowsePathsToNodeIds"),
            ("/registernodes", "RegisterNodes"),
            ("/unregisternodes", "UnregisterNodes"),
            ("/findservers", "FindServers"),
            ("/getendpoints", "GetEndpoints"),
            ("/createsession", "CreateSession"),
            ("/activatesession", "ActivateSession"),
            ("/closesession", "CloseSession"),
            ("/cancel", "Cancel"),
            ("/createmonitoreditems", "CreateMonitoredItems"),
            ("/modifymonitoreditems", "ModifyMonitoredItems"),
            ("/setmonitoringmode", "SetMonitoringMode"),
            ("/settriggering", "SetTriggering"),
            ("/deletemonitoreditems", "DeleteMonitoredItems"),
            ("/createsubscription", "CreateSubscription"),
            ("/modifysubscription", "ModifySubscription"),
            ("/setpublishingmode", "SetPublishingMode"),
            ("/publish", "Publish"),
            ("/republish", "Republish"),
            ("/transfersubscriptions", "TransferSubscriptions"),
            ("/deletesubscriptions", "DeleteSubscriptions")
        ];

        [Test]
        public void CountMatchesSpecAllServicesDocument()
        {
            Assert.That(
                RestApiServiceRoutes.Count,
                Is.EqualTo(s_expectedRoutes.Length),
                "Route count must match opc.ua.openapi.allservices.json (28 services).");
            Assert.That(
                RestApiServiceRoutes.Routes,
                Has.Count.EqualTo(s_expectedRoutes.Length));
        }

        [Test]
        public void RoutesEnumerateAllExpectedPaths()
        {
            string[] actual = [.. RestApiServiceRoutes.Routes.Select(r => r.Path).Order()];
            string[] expected = [.. s_expectedRoutes.Select(t => t.Path).Order()];

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void RoutesEnumerateAllExpectedOperationIds()
        {
            string[] actual = [.. RestApiServiceRoutes.Routes.Select(r => r.OperationId).Order()];
            string[] expected = [.. s_expectedRoutes.Select(t => t.OperationId).Order()];

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void EveryRoutePathIsLowercase()
        {
            foreach (RestApiServiceRoute route in RestApiServiceRoutes.Routes)
            {
                Assert.That(
                    route.Path,
                    Is.EqualTo(route.Path.ToLowerInvariant()),
                    $"Route '{route.Path}' must be all-lowercase per spec.");
            }
        }

        [Test]
        public void EveryRoutePathStartsWithSlash()
        {
            foreach (RestApiServiceRoute route in RestApiServiceRoutes.Routes)
            {
                Assert.That(
                    route.Path,
                    Does.StartWith("/"),
                    $"Route '{route.Path}' must start with '/'.");
            }
        }

        [Test]
        public void EveryRouteRequestTypeImplementsIServiceRequest()
        {
            foreach (RestApiServiceRoute route in RestApiServiceRoutes.Routes)
            {
                Assert.That(
                    typeof(IServiceRequest).IsAssignableFrom(route.RequestType),
                    Is.True,
                    $"Route '{route.Path}' request type '{route.RequestType.FullName}' must implement IServiceRequest.");
            }
        }

        [Test]
        public void EveryRouteResponseTypeImplementsIServiceResponse()
        {
            foreach (RestApiServiceRoute route in RestApiServiceRoutes.Routes)
            {
                Assert.That(
                    typeof(IServiceResponse).IsAssignableFrom(route.ResponseType),
                    Is.True,
                    $"Route '{route.Path}' response type '{route.ResponseType.FullName}' must implement IServiceResponse.");
            }
        }

        [Test]
        public void EveryRouteRequestTypeNameMatchesOperationIdRequestPattern()
        {
            foreach (RestApiServiceRoute route in RestApiServiceRoutes.Routes)
            {
                string expected = route.OperationId + "Request";
                Assert.That(
                    route.RequestType.Name,
                    Is.EqualTo(expected),
                    $"Route '{route.Path}' request type must be named '{expected}'.");
            }
        }

        [Test]
        public void EveryRouteResponseTypeNameMatchesOperationIdResponsePattern()
        {
            foreach (RestApiServiceRoute route in RestApiServiceRoutes.Routes)
            {
                string expected = route.OperationId + "Response";
                Assert.That(
                    route.ResponseType.Name,
                    Is.EqualTo(expected),
                    $"Route '{route.Path}' response type must be named '{expected}'.");
            }
        }

        [Test]
        public void EveryRequestTypeExposesParameterlessConstructor()
        {
            foreach (RestApiServiceRoute route in RestApiServiceRoutes.Routes)
            {
                Assert.That(
                    route.RequestType.GetConstructor(Type.EmptyTypes),
                    Is.Not.Null,
                    $"Route '{route.Path}' request type '{route.RequestType.Name}' must have a parameterless constructor for envelope-less decode.");
            }
        }

        [Test]
        public void EveryResponseTypeExposesParameterlessConstructor()
        {
            foreach (RestApiServiceRoute route in RestApiServiceRoutes.Routes)
            {
                Assert.That(
                    route.ResponseType.GetConstructor(Type.EmptyTypes),
                    Is.Not.Null,
                    $"Route '{route.Path}' response type '{route.ResponseType.Name}' must have a parameterless constructor for envelope-less decode.");
            }
        }

        [Test]
        public void PathsAreUnique()
        {
            string[] paths = [.. RestApiServiceRoutes.Routes.Select(r => r.Path)];
            Assert.That(paths, Is.Unique);
        }

        [Test]
        public void OperationIdsAreUnique()
        {
            string[] operationIds = [.. RestApiServiceRoutes.Routes.Select(r => r.OperationId)];
            Assert.That(operationIds, Is.Unique);
        }

        [Test]
        public void RequestTypesAreUnique()
        {
            Type[] requestTypes = [.. RestApiServiceRoutes.Routes.Select(r => r.RequestType)];
            Assert.That(requestTypes, Is.Unique);
        }

        [Test]
        public void ResponseTypesAreUnique()
        {
            Type[] responseTypes = [.. RestApiServiceRoutes.Routes.Select(r => r.ResponseType)];
            Assert.That(responseTypes, Is.Unique);
        }

        [Test]
        public void NodeManagementServicesAreNotExposed()
        {
            string[] paths = [.. RestApiServiceRoutes.Routes.Select(r => r.Path)];
            Assert.That(paths, Does.Not.Contain("/addnodes"));
            Assert.That(paths, Does.Not.Contain("/addreferences"));
            Assert.That(paths, Does.Not.Contain("/deletenodes"));
            Assert.That(paths, Does.Not.Contain("/deletereferences"));
        }

        [Test]
        public void QueryServicesAreNotExposed()
        {
            string[] paths = [.. RestApiServiceRoutes.Routes.Select(r => r.Path)];
            Assert.That(paths, Does.Not.Contain("/queryfirst"));
            Assert.That(paths, Does.Not.Contain("/querynext"));
        }

        [Test]
        public void TryGetByPathReturnsExpectedRouteForRead()
        {
            bool found = RestApiServiceRoutes.TryGetByPath("/read", out RestApiServiceRoute route);
            Assert.That(found, Is.True);
            Assert.That(route.OperationId, Is.EqualTo("Read"));
            Assert.That(route.RequestType, Is.EqualTo(typeof(ReadRequest)));
            Assert.That(route.ResponseType, Is.EqualTo(typeof(ReadResponse)));
        }

        [Test]
        public void TryGetByPathIsCaseInsensitive()
        {
            Assert.That(RestApiServiceRoutes.TryGetByPath("/READ", out _), Is.True);
            Assert.That(RestApiServiceRoutes.TryGetByPath("/Read", out _), Is.True);
            Assert.That(RestApiServiceRoutes.TryGetByPath("/HistoryRead", out _), Is.True);
        }

        [Test]
        [TestCase(null)]
        [TestCase("")]
        [TestCase("/unknown")]
        [TestCase("read")]
        [TestCase("/read/")]
        [TestCase("/read?x=1")]
        public void TryGetByPathReturnsFalseForUnknownOrInvalid(string? path)
        {
            Assert.That(RestApiServiceRoutes.TryGetByPath(path, out _), Is.False);
        }

        [Test]
        public void TryGetByOperationIdReturnsExpectedRouteForTranslate()
        {
            bool found = RestApiServiceRoutes.TryGetByOperationId(
                "TranslateBrowsePathsToNodeIds", out RestApiServiceRoute route);
            Assert.That(found, Is.True);
            Assert.That(route.Path, Is.EqualTo("/translate"));
        }

        [Test]
        public void TryGetByOperationIdIsCaseInsensitive()
        {
            Assert.That(
                RestApiServiceRoutes.TryGetByOperationId("READ", out _),
                Is.True);
        }

        [Test]
        [TestCase(null)]
        [TestCase("")]
        [TestCase("Unknown")]
        public void TryGetByOperationIdReturnsFalseForUnknown(string? operationId)
        {
            Assert.That(
                RestApiServiceRoutes.TryGetByOperationId(operationId, out _),
                Is.False);
        }

        [Test]
        public void TryGetByRequestTypeReturnsExpectedRouteForPublishRequest()
        {
            bool found = RestApiServiceRoutes.TryGetByRequestType(
                typeof(PublishRequest), out RestApiServiceRoute route);
            Assert.That(found, Is.True);
            Assert.That(route.Path, Is.EqualTo("/publish"));
            Assert.That(route.OperationId, Is.EqualTo("Publish"));
        }

        [Test]
        public void TryGetByRequestTypeReturnsFalseForNull()
        {
            Assert.That(
                RestApiServiceRoutes.TryGetByRequestType(null, out _),
                Is.False);
        }

        [Test]
        public void TryGetByRequestTypeReturnsFalseForUnrelatedType()
        {
            Assert.That(
                RestApiServiceRoutes.TryGetByRequestType(typeof(string), out _),
                Is.False);
        }

        [Test]
        public void TryGetByRequestTypeReturnsFalseForResponseType()
        {
            Assert.That(
                RestApiServiceRoutes.TryGetByRequestType(typeof(ReadResponse), out _),
                Is.False);
        }
    }
}
