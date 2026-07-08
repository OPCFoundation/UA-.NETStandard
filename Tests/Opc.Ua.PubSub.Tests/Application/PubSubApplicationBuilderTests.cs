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
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Application
{
    /// <summary>
    /// Unit tests for the manual non-DI
    /// <see cref="PubSubApplicationBuilder"/>.
    /// </summary>
    [TestFixture]
    public class PubSubApplicationBuilderTests
    {
        [Test]
        public void Constructor_NullTelemetry_Throws()
        {
            Assert.That(
                () => new PubSubApplicationBuilder(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void Build_WithoutAnyConfiguration_Succeeds()
        {
            var builder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create());
            IPubSubApplication app = builder
                .UseAllStandardEncoders()
                .Build();
            Assert.That(app, Is.Not.Null);
            Assert.That(app.Connections, Is.Empty);
        }

        [Test]
        public void Build_WithInlineConfiguration_BuildsApplication()
        {
            var config = new PubSubConfigurationDataType
            {
                Connections = [],
                PublishedDataSets = []
            };
            var builder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("test-app")
                .UseConfiguration(config)
                .UseAllStandardEncoders();
            IPubSubApplication app = builder.Build();
            Assert.That(app, Is.Not.Null);
            Assert.That(app.ApplicationId, Is.Not.Empty);
            Assert.That(app.Connections, Is.Empty);
        }

        [Test]
        public void Build_WhenInlineAndFileBothSet_Throws()
        {
            var builder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Connections = [],
                    PublishedDataSets = []
                })
                .UseConfigurationFile("does-not-matter.xml");
            Assert.That(builder.Build, Throws.TypeOf<PubSubApplicationBuildException>());
        }

        [Test]
        public void Configure_ModifiesOptions()
        {
            string? captured = null;
            var builder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .Configure(o =>
                {
                    o.ApplicationId = "configured-id";
                    captured = o.ApplicationId;
                });
            Assert.That(captured, Is.EqualTo("configured-id"));
            Assert.That(builder, Is.Not.Null);
        }

        [Test]
        public void WithDiagnosticsLevel_PropagatesLevel()
        {
            var builder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithDiagnosticsLevel(PubSubDiagnosticsLevel.Medium);
            IPubSubApplication app = builder.Build();
            Assert.That(app, Is.Not.Null);
        }

        [Test]
        public void WithTimeProvider_NullClock_Throws()
        {
            var builder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create());
            Assert.That(
                () => builder.WithTimeProvider(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WithApplicationId_Empty_Throws()
        {
            var builder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create());
            Assert.That(
                () => builder.WithApplicationId(string.Empty),
                Throws.ArgumentException);
        }

        [Test]
        public void UseConfiguration_Null_Throws()
        {
            var builder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create());
            Assert.That(
                () => builder.UseConfiguration(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void UseConfigurationFile_Empty_Throws()
        {
            var builder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create());
            Assert.That(
                () => builder.UseConfigurationFile(string.Empty),
                Throws.ArgumentException);
        }

        [Test]
        public void Configure_NullCallback_Throws()
        {
            var builder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create());
            Assert.That(
                () => builder.Configure(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddTransportFactory_Null_Throws()
        {
            var builder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create());
            Assert.That(
                () => builder.AddTransportFactory(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddEncoder_Null_Throws()
        {
            var builder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create());
            Assert.That(
                () => builder.AddEncoder(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddDecoder_Null_Throws()
        {
            var builder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create());
            Assert.That(
                () => builder.AddDecoder(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void UseInMemorySks_RegistersServer()
        {
            var builder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .UseInMemorySks();
            Assert.That(builder.SecurityKeyServiceServer, Is.Not.Null);
        }

        [Test]
        public void AddPublishedActionWithNullActionThrowsArgumentNullException()
        {
            var builder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create());

            Assert.That(
                () => builder.AddPublishedAction("ActionDataSet", (PublishedActionDataType)null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("action"));
        }

        [Test]
        public void BuildWithPublishedActionConfigurationSucceeds()
        {
            DataSetMetaDataType requestMetaData = CreateActionRequestMetaData();
            PubSubConfigurationDataType config = PubSubConfigurationBuilder.Create()
                .AddPublishedAction("ActionDataSet", requestMetaData, CreateActionTargets())
                .Build();

            IPubSubApplication app = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .UseConfiguration(config)
                .Build();

            Assert.That(app.GetConfiguration().PublishedDataSets, Has.Count.EqualTo(1));
        }

        private static DataSetMetaDataType CreateActionRequestMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "ActionRequest",
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 0
                }
            };
        }

        private static ArrayOf<ActionTargetDataType> CreateActionTargets()
        {
            return
            [
                new ActionTargetDataType
                {
                    ActionTargetId = 1,
                    Name = "Target",
                    Description = new LocalizedText("en-US", "Target action")
                }
            ];
        }
    }
}
