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

#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Kafka.Internal;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Kafka.Tests
{
    /// <summary>
    /// Guard tests for the default Dekaf-backed Kafka adapter that do not require a broker.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("B.2", Summary = "Kafka default adapter guard behavior")]
    [CancelAfter(10000)]
    public sealed class KafkaClientAdapterGuardTests
    {
        [Test]
        public void ConstructorRejectsInvalidArguments()
        {
            Assert.That(
                () => new DekafKafkaClientAdapter(null!, TimeProvider.System),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => new DekafKafkaClientAdapter(NUnitTelemetryContext.Create(), null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void FactoryCreatesDekafAdapter()
        {
            var factory = new DekafKafkaClientFactory();

            object adapter = factory.Create(NUnitTelemetryContext.Create(), TimeProvider.System);

            Assert.That(adapter, Is.InstanceOf<DekafKafkaClientAdapter>());
        }

        [Test]
        public async Task ConnectDisconnectTransitionsStateAsync()
        {
            await using var adapter = new DekafKafkaClientAdapter(NUnitTelemetryContext.Create(), TimeProvider.System);
            var events = new List<KafkaConnectionStateChangedEventArgs>();
            adapter.ConnectionStateChanged += (_, e) => events.Add(e);
            var options = new KafkaConnectionOptions
            {
                Endpoint = KafkaTestHelper.EndpointUrl
            };

            await adapter.ConnectAsync(options, CancellationToken.None).ConfigureAwait(false);
            await adapter.ConnectAsync(options, CancellationToken.None).ConfigureAwait(false);
            await adapter.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            await adapter.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(adapter.IsConnected, Is.False);
            Assert.That(events, Has.Count.EqualTo(2));
            Assert.That(events[0].IsConnected, Is.True);
            Assert.That(events[1].IsConnected, Is.False);
        }

        [Test]
        public async Task ConnectValidatesCredentialsTlsAndSaslAsync()
        {
            await using var adapter = new DekafKafkaClientAdapter(NUnitTelemetryContext.Create(), TimeProvider.System);

            Assert.That(async () => await adapter.ConnectAsync(null!, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(async () => await adapter.ConnectAsync(new KafkaConnectionOptions
            {
                Endpoint = KafkaTestHelper.EndpointUrl,
                SaslMechanism = KafkaSaslMechanism.Plain,
                UserName = "alice"
            }, CancellationToken.None).ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>());
            Assert.That(async () => await adapter.ConnectAsync(new KafkaConnectionOptions
            {
                Endpoint = KafkaTestHelper.EndpointUrl,
                SaslMechanism = KafkaSaslMechanism.OAuthBearer,
                AllowCredentialsOverPlaintext = true
            }, CancellationToken.None).ConfigureAwait(false), Throws.TypeOf<NotSupportedException>());
            Assert.That(async () => await adapter.ConnectAsync(new KafkaConnectionOptions
            {
                Endpoint = KafkaTestHelper.EndpointUrl,
                Tls = new KafkaTlsOptions
                {
                    UseTls = true,
                    ClientCertificatePath = "client.pem"
                }
            }, CancellationToken.None).ConfigureAwait(false), Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public async Task SubscribeUnsubscribeGuardPathsDoNotRequireBrokerAsync()
        {
            await using var adapter = new DekafKafkaClientAdapter(NUnitTelemetryContext.Create(), TimeProvider.System);
            await adapter.ConnectAsync(new KafkaConnectionOptions { Endpoint = KafkaTestHelper.EndpointUrl },
                CancellationToken.None).ConfigureAwait(false);

            await adapter.SubscribeAsync(Array.Empty<string>(), CancellationToken.None).ConfigureAwait(false);
            await adapter.UnsubscribeAsync(Array.Empty<string>(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(async () => await adapter.SubscribeAsync(null!, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(async () => await adapter.UnsubscribeAsync(null!, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task ProduceAndDisposedGuardsDoNotRequireBrokerAsync()
        {
            var adapter = new DekafKafkaClientAdapter(NUnitTelemetryContext.Create(), TimeProvider.System);
            await adapter.ConnectAsync(new KafkaConnectionOptions { Endpoint = KafkaTestHelper.EndpointUrl },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(async () => await adapter.ProduceAsync(default, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>());

            await adapter.DisposeAsync().ConfigureAwait(false);
            Assert.That(async () => await adapter.ConnectAsync(new KafkaConnectionOptions(), CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(async () => await adapter.SubscribeAsync(Array.Empty<string>(), CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(async () => await adapter.UnsubscribeAsync(Array.Empty<string>(), CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void PrivateMappingHelpersCoverKafkaConfigurationBranches()
        {
            var endpointOptions = new KafkaConnectionOptions { Endpoint = "kafka://broker1,broker2:19092" };
            var groupOptions = new KafkaConnectionOptions { ClientId = "client-a" };
            var passwordOptions = new KafkaConnectionOptions { PasswordBytes = System.Text.Encoding.UTF8.GetBytes("pw") };
            var tlsOptions = new KafkaConnectionOptions
            {
                Tls = new KafkaTlsOptions
                {
                    UseTls = true,
                    ValidateServerCertificate = false,
                    CaCertificatePath = "ca.pem",
                    ClientCertificatePath = "client.pem",
                    ClientKeyPath = "client.key"
                }
            };
            var headersMessage = new KafkaMessage(
                KafkaTestHelper.JsonTopic,
                new byte[] { 0x01 },
                new byte[] { 0x02 },
                "application/json",
                new Dictionary<string, string> { ["x-opcua"] = "value" });

            Assert.That(InvokePrivate<string>("ResolveBootstrapServers", endpointOptions),
                Is.EqualTo("broker1:9092,broker2:19092"));
            Assert.That(InvokePrivate<string>("ResolveGroupId", groupOptions), Does.StartWith("client-a-"));
            Assert.That(InvokePrivate<string>("ResolvePassword", passwordOptions), Is.EqualTo("pw"));
            Assert.That(InvokePrivate<object>("CreateHeaders", headersMessage), Is.Not.Null);
            Assert.That(InvokePrivate<object>("CreateTlsConfig", new KafkaConnectionOptions()), Is.Not.Null);
            Assert.That(InvokePrivate<object>("CreateTlsConfig", tlsOptions), Is.Not.Null);
            Assert.That(InvokePrivate<object>("MapAcks", KafkaAcks.None).ToString(), Is.EqualTo("None"));
            Assert.That(InvokePrivate<object>("MapAcks", KafkaAcks.Leader).ToString(), Is.EqualTo("Leader"));
            Assert.That(InvokePrivate<object>("MapAcks", KafkaAcks.All).ToString(), Is.EqualTo("All"));
            Assert.That(InvokePrivate<object>("MapAutoOffsetReset", KafkaAutoOffsetReset.Earliest).ToString(),
                Is.EqualTo("Earliest"));
            Assert.That(InvokePrivate<object>("MapAutoOffsetReset", KafkaAutoOffsetReset.Latest).ToString(),
                Is.EqualTo("Latest"));
        }

        [Test]
        public void PrivateValidationHelpersThrowForMissingSaslInputs()
        {
            object exception = InvokePrivate<object>(
                "CreateUnsupportedSaslMechanismException",
                KafkaSaslMechanism.OAuthBearer);

            Assert.That(() => InvokePrivate<object>("RequireUserName", new KafkaConnectionOptions()),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => InvokePrivate<object>("ResolvePassword", new KafkaConnectionOptions()),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(exception, Is.InstanceOf<NotSupportedException>());
        }

        [Test]
        public void PrivateProducerAndConsumerConfigHelpersCoverSecurityBranches()
        {
            var plaintext = new KafkaConnectionOptions
            {
                BootstrapServers = "broker.example.com:9092",
                ClientId = "client-a",
                GroupId = "group-a",
                DeliveryGuarantee = KafkaQualityOfService.BestEffort,
                AutoOffsetReset = KafkaAutoOffsetReset.Earliest,
                EnableAutoCommit = false
            };
            var tls = new KafkaConnectionOptions
            {
                BootstrapServers = "broker.example.com:9092",
                ClientId = "client-tls",
                GroupId = "group-tls",
                SecurityProtocol = KafkaSecurityProtocol.Ssl,
                Tls = new KafkaTlsOptions
                {
                    UseTls = true,
                    ValidateServerCertificate = true,
                    CaCertificatePath = "ca.pem"
                }
            };
            var saslPlain = new KafkaConnectionOptions
            {
                BootstrapServers = "broker.example.com:9092",
                UserName = "alice",
                PasswordBytes = System.Text.Encoding.UTF8.GetBytes("password"),
                SaslMechanism = KafkaSaslMechanism.Plain,
                AllowCredentialsOverPlaintext = true
            };
            var saslScram256 = new KafkaConnectionOptions
            {
                BootstrapServers = "broker.example.com:9092",
                UserName = "alice",
                PasswordBytes = System.Text.Encoding.UTF8.GetBytes("password"),
                SaslMechanism = KafkaSaslMechanism.ScramSha256,
                SecurityProtocol = KafkaSecurityProtocol.SaslSsl
            };
            var saslScram512 = new KafkaConnectionOptions
            {
                BootstrapServers = "broker.example.com:9092",
                UserName = "alice",
                PasswordBytes = System.Text.Encoding.UTF8.GetBytes("password"),
                SaslMechanism = KafkaSaslMechanism.ScramSha512,
                Tls = new KafkaTlsOptions { UseTls = true }
            };

            InvokeConfig("ApplyProducerConfig", CreateProducerBuilder(), plaintext);
            InvokeConfig("ApplyConsumerConfig", CreateConsumerBuilder(), plaintext);
            InvokeConfig("ApplyProducerConfig", CreateProducerBuilder(), tls);
            InvokeConfig("ApplyConsumerConfig", CreateConsumerBuilder(), tls);
            InvokeConfig("ApplyProducerConfig", CreateProducerBuilder(), saslPlain);
            InvokeConfig("ApplyConsumerConfig", CreateConsumerBuilder(), saslPlain);
            InvokeConfig("ApplyProducerConfig", CreateProducerBuilder(), saslScram256);
            InvokeConfig("ApplyConsumerConfig", CreateConsumerBuilder(), saslScram256);
            InvokeConfig("ApplyProducerConfig", CreateProducerBuilder(), saslScram512);
            InvokeConfig("ApplyConsumerConfig", CreateConsumerBuilder(), saslScram512);

            Assert.That(
                () => InvokeConfig(
                    "ApplyProducerConfig",
                    CreateProducerBuilder(),
                    new KafkaConnectionOptions
                    {
                        BootstrapServers = "broker.example.com:9092",
                        SaslMechanism = KafkaSaslMechanism.Plain,
                        PasswordBytes = System.Text.Encoding.UTF8.GetBytes("password"),
                        AllowCredentialsOverPlaintext = true
                    }),
                Throws.TypeOf<InvalidOperationException>());
        }

        private static T InvokePrivate<T>(string methodName, params object?[] args)
        {
            MethodInfo method = typeof(DekafKafkaClientAdapter).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static)!;
            try
            {
                return (T)method.Invoke(null, args)!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
        }

        private static void InvokeConfig(string methodName, object builder, KafkaConnectionOptions options)
        {
            MethodInfo method = typeof(DekafKafkaClientAdapter).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static)!;
            try
            {
                method.Invoke(null, [builder, options]);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
        }

        private static object CreateProducerBuilder()
        {
            MethodInfo method = GetDekafKafkaMethod("CreateProducer")
                .MakeGenericMethod(typeof(byte[]), typeof(byte[]));
            return method.Invoke(null, null)!;
        }

        private static object CreateConsumerBuilder()
        {
            MethodInfo method = GetDekafKafkaMethod("CreateConsumer")
                .MakeGenericMethod(typeof(byte[]), typeof(byte[]));
            return method.Invoke(null, null)!;
        }

        private static MethodInfo GetDekafKafkaMethod(string name)
        {
            Type kafkaType = Type.GetType("Dekaf.Kafka, Dekaf", throwOnError: true)!;
            foreach (MethodInfo method in kafkaType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name == name && method.IsGenericMethodDefinition)
                {
                    return method;
                }
            }
            throw new MissingMethodException("Dekaf.Kafka", name);
        }
    }
}
#endif
