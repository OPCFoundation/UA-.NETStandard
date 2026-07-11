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
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class JsonUserDatabaseTests
    {
        [Test]
        public void LoadRejectsNullFileName()
        {
            Assert.That(
                () => JsonUserDatabase.Load(null!, NUnitTelemetryContext.Create()),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void LoadMissingFileReturnsEmptyDatabaseWithFileName()
        {
            string fileName = CreateDatabasePath();

            IUserDatabase database = JsonUserDatabase.Load(fileName, NUnitTelemetryContext.Create());

            var jsonDatabase = (JsonUserDatabase)database;
            Assert.That(jsonDatabase.FileName, Is.EqualTo(fileName));
            Assert.That(jsonDatabase.GetUsers(), Is.Empty);
        }

        [Test]
        public void SaveAndLoadRoundTripsUsersCredentialsAndRoles()
        {
            string fileName = CreateDatabasePath();
            var database = new JsonUserDatabase(fileName);
            database.CreateUser("alice", "secret"u8, [Role.AuthenticatedUser, Role.Engineer]);

            IUserDatabase loaded = JsonUserDatabase.Load(fileName, NUnitTelemetryContext.Create());

            Assert.That(((JsonUserDatabase)loaded).FileName, Is.EqualTo(fileName));
            Assert.That(loaded.GetUsers().Single().UserName, Is.EqualTo("alice"));
            Assert.That(loaded.CheckCredentials("alice", "secret"u8), Is.True);
            Assert.That(loaded.CheckCredentials("alice", "wrong"u8), Is.False);
            Assert.That(loaded.GetUserRoles("alice"), Has.Count.EqualTo(2));
            Assert.That(loaded.GetUserRoles("alice"), Does.Contain(Role.AuthenticatedUser));
            Assert.That(loaded.GetUserRoles("alice"), Does.Contain(Role.Engineer));
        }

        [Test]
        public void LoadLegacyObjectDatabasePreservesUsersCredentialsAndRoles()
        {
            const string legacyJson = """
                {
                  "users": [
                    {
                      "Id": "5ffc097e-566f-4e98-9a45-fa662826a6aa",
                      "UserName": "alice",
                      "Hash": "100000.fBXDf\u002BXgck10FwkXXEVetxGpOsM=.83/yGZTA1roMWqRsDO6TiuWllGOHfipTm01UaTduYJo=",
                      "Roles": [
                        {
                          "Name": "AuthenticatedUser",
                          "RoleId": {
                            "serverIndex": 0,
                            "isNull": false,
                            "isAbsolute": false,
                            "namespaceIndex": 0,
                            "idType": "numeric",
                            "identifier": 15656,
                            "identifierAsString": "15656",
                            "innerNodeId": {
                              "namespaceIndex": 0,
                              "idType": "numeric",
                              "identifier": 15656,
                              "identifierAsString": "15656",
                              "isNull": false,
                              "hasValue": true
                            }
                          }
                        },
                        {
                          "Name": "Engineer",
                          "RoleId": {
                            "serverIndex": 0,
                            "isNull": false,
                            "isAbsolute": false,
                            "namespaceIndex": 0,
                            "idType": "numeric",
                            "identifier": 16036,
                            "identifierAsString": "16036",
                            "innerNodeId": {
                              "namespaceIndex": 0,
                              "idType": "numeric",
                              "identifier": 16036,
                              "identifierAsString": "16036",
                              "isNull": false,
                              "hasValue": true
                            }
                          }
                        }
                      ]
                    }
                  ]
                }
                """;
            string fileName = CreateDatabasePath();
            File.WriteAllText(fileName, legacyJson);

            IUserDatabase loaded = JsonUserDatabase.Load(fileName, NUnitTelemetryContext.Create());

            Assert.That(((JsonUserDatabase)loaded).FileName, Is.EqualTo(fileName));
            Assert.That(loaded.GetUsers().Single().UserName, Is.EqualTo("alice"));
            Assert.That(loaded.CheckCredentials("alice", "secret"u8), Is.True);
            Assert.That(loaded.CheckCredentials("alice", "wrong"u8), Is.False);
            Assert.That(loaded.GetUserRoles("alice"), Has.Count.EqualTo(2));
            Assert.That(loaded.GetUserRoles("alice"), Does.Contain(Role.AuthenticatedUser));
            Assert.That(loaded.GetUserRoles("alice"), Does.Contain(Role.Engineer));
        }

        [Test]
        public void LoadCurrentStringRoleIdPreservesAbsoluteIdentifier()
        {
            var expectedRoleId = new ExpandedNodeId(
                "maintenance",
                0,
                "urn:example:roles",
                2);
            string json = CreateDatabaseJson(JsonSerializer.Serialize(expectedRoleId.ToString()));

            JsonUserDatabase loaded = LoadDatabase(json);

            Assert.That(loaded.GetUsers().Single().UserName, Is.EqualTo("alice"));
            Assert.That(loaded.GetUserRoles("alice").Single().Name, Is.EqualTo("Custom"));
            Assert.That(loaded.GetUserRoles("alice").Single().RoleId, Is.EqualTo(expectedRoleId));
        }

        [Test]
        public void LoadLegacyNullRoleIdPreservesNullIdentifier()
        {
            const string roleIdJson = """
                {
                  "serverIndex": 0,
                  "isNull": true,
                  "isAbsolute": false,
                  "namespaceIndex": 0,
                  "idType": "numeric",
                  "identifier": null
                }
                """;

            JsonUserDatabase loaded = LoadDatabase(CreateDatabaseJson(roleIdJson));

            Assert.That(loaded.GetUsers().Single().UserName, Is.EqualTo("alice"));
            Assert.That(loaded.GetUserRoles("alice").Single().RoleId, Is.EqualTo(ExpandedNodeId.Null));
        }

        [Test]
        public void LoadLegacyStringRoleIdPreservesIdentifier()
        {
            const string roleIdJson = """
                {
                  "serverIndex": 0,
                  "isNull": false,
                  "isAbsolute": false,
                  "namespaceIndex": 2,
                  "idType": "string",
                  "identifier": "custom-role"
                }
                """;

            JsonUserDatabase loaded = LoadDatabase(CreateDatabaseJson(roleIdJson));

            Assert.That(
                loaded.GetUserRoles("alice").Single().RoleId,
                Is.EqualTo(new ExpandedNodeId("custom-role", 2)));
        }

        [Test]
        public void LoadLegacyGuidRoleIdPreservesIdentifier()
        {
            const string roleIdJson = """
                {
                  "serverIndex": 0,
                  "isNull": false,
                  "isAbsolute": true,
                  "namespaceIndex": 0,
                  "namespaceUri": "urn:example:roles",
                  "idType": "guid",
                  "identifier": "65c5a128-701c-4a46-9d64-9c4dfc78bc9d"
                }
                """;

            JsonUserDatabase loaded = LoadDatabase(CreateDatabaseJson(roleIdJson));

            Assert.That(
                loaded.GetUserRoles("alice").Single().RoleId,
                Is.EqualTo(
                    new ExpandedNodeId(
                        Guid.Parse("65c5a128-701c-4a46-9d64-9c4dfc78bc9d"),
                        0,
                        "urn:example:roles",
                        0)));
        }

        [Test]
        public void LoadLegacyOpaqueRoleIdPreservesIdentifier()
        {
            const string roleIdJson = """
                {
                  "serverIndex": 3,
                  "isNull": false,
                  "isAbsolute": true,
                  "namespaceIndex": 1,
                  "idType": "opaque",
                  "identifier": {
                    "memory": "AQIDBA=="
                  }
                }
                """;

            JsonUserDatabase loaded = LoadDatabase(CreateDatabaseJson(roleIdJson));

            Assert.That(
                loaded.GetUserRoles("alice").Single().RoleId,
                Is.EqualTo(new ExpandedNodeId(ByteString.From([1, 2, 3, 4]), 1, null, 3)));
        }

        [TestCaseSource(nameof(InvalidRoleIdJson))]
        public void LoadInvalidRoleIdReturnsEmptyDatabase(string roleIdJson)
        {
            JsonUserDatabase loaded = LoadDatabase(CreateDatabaseJson(roleIdJson));

            Assert.That(loaded.GetUsers(), Is.Empty);
        }

        [Test]
        public void LoadExistingEmptyJsonPreservesFileName()
        {
            string fileName = CreateDatabasePath();
            File.WriteAllText(fileName, "{\"users\":[]}");

            IUserDatabase loaded = JsonUserDatabase.Load(fileName, NUnitTelemetryContext.Create());

            Assert.That(((JsonUserDatabase)loaded).FileName, Is.EqualTo(fileName));
            Assert.That(loaded.GetUsers().Select(user => user.UserName), Is.EqualTo(Array.Empty<string>()));
        }

        [Test]
        public void LoadInvalidJsonLogsAndReturnsEmptyDatabase()
        {
            string fileName = CreateDatabasePath();
            File.WriteAllText(fileName, "{ invalid json }");

            IUserDatabase loaded = JsonUserDatabase.Load(fileName, NUnitTelemetryContext.Create());

            Assert.That(((JsonUserDatabase)loaded).FileName, Is.EqualTo(fileName));
            Assert.That(loaded.GetUsers(), Is.Empty);
        }

        private static IEnumerable<TestCaseData> InvalidRoleIdJson()
        {
            yield return new TestCaseData("\"not-an-expanded-node-id\"")
                .SetName("LoadInvalidRoleIdReturnsEmptyDatabaseForInvalidString");
            yield return new TestCaseData("42")
                .SetName("LoadInvalidRoleIdReturnsEmptyDatabaseForUnexpectedToken");
            yield return new TestCaseData(
                """
                {
                  "serverIndex": 0,
                  "isNull": false,
                  "isAbsolute": true,
                  "namespaceIndex": 0,
                  "idType": "numeric",
                  "identifier": 1
                }
                """)
                .SetName("LoadInvalidRoleIdReturnsEmptyDatabaseForInvalidNamespace");
            yield return new TestCaseData(
                """
                {
                  "serverIndex": 1,
                  "isNull": true,
                  "isAbsolute": true,
                  "namespaceIndex": 0,
                  "idType": "numeric",
                  "identifier": null
                }
                """)
                .SetName("LoadInvalidRoleIdReturnsEmptyDatabaseForInvalidNull");
            yield return new TestCaseData(
                """
                {
                  "serverIndex": 0,
                  "isNull": false,
                  "isAbsolute": false,
                  "namespaceIndex": 0,
                  "idType": "unknown",
                  "identifier": 1
                }
                """)
                .SetName("LoadInvalidRoleIdReturnsEmptyDatabaseForUnknownIdentifierType");
            yield return new TestCaseData(
                """
                {
                  "serverIndex": 0,
                  "isNull": false,
                  "isAbsolute": false,
                  "namespaceIndex": 0,
                  "idType": null,
                  "identifier": 1
                }
                """)
                .SetName("LoadInvalidRoleIdReturnsEmptyDatabaseForMissingIdentifierType");
            yield return new TestCaseData(
                """
                {
                  "serverIndex": 0,
                  "isNull": false,
                  "isAbsolute": false,
                  "namespaceIndex": 0,
                  "idType": "string",
                  "identifier": null
                }
                """)
                .SetName("LoadInvalidRoleIdReturnsEmptyDatabaseForNullStringIdentifier");
            yield return new TestCaseData(
                """
                {
                  "serverIndex": 0,
                  "isNull": false,
                  "isAbsolute": false,
                  "namespaceIndex": 0,
                  "idType": "opaque",
                  "identifier": {
                    "memory": "not-base64"
                  }
                }
                """)
                .SetName("LoadInvalidRoleIdReturnsEmptyDatabaseForInvalidOpaqueIdentifier");
            yield return new TestCaseData(
                """
                {
                  "serverIndex": 0,
                  "isNull": false,
                  "isAbsolute": false,
                  "namespaceIndex": 0,
                  "idType": "opaque",
                  "identifier": {
                    "memory": null
                  }
                }
                """)
                .SetName("LoadInvalidRoleIdReturnsEmptyDatabaseForNullOpaqueIdentifier");
        }

        private static JsonUserDatabase LoadDatabase(string json)
        {
            string fileName = CreateDatabasePath();
            File.WriteAllText(fileName, json);
            return (JsonUserDatabase)JsonUserDatabase.Load(
                fileName,
                NUnitTelemetryContext.Create());
        }

        private static string CreateDatabaseJson(string roleIdJson)
        {
            return $$"""
                {
                  "users": [
                    {
                      "Id": "5ffc097e-566f-4e98-9a45-fa662826a6aa",
                      "UserName": "alice",
                      "Hash": "100000.fBXDf\u002BXgck10FwkXXEVetxGpOsM=.83/yGZTA1roMWqRsDO6TiuWllGOHfipTm01UaTduYJo=",
                      "Roles": [
                        {
                          "Name": "Custom",
                          "RoleId": {{roleIdJson}}
                        }
                      ]
                    }
                  ]
                }
                """;
        }

        private static string CreateDatabasePath()
        {
            string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "JsonUserDatabaseTests");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        }
    }
}
