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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.MachineTool.Server;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Verifies complete prepublication validation of section 8.6.2 severity bands and bounded provider inventories.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("MachineTool")]
    public sealed class MachineToolNotificationValidationTests
    {
        /// <summary>
        /// An invalid batch cannot allocate even the root factory or publish the valid prefix of its inventory.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        [TestCase(11)]
        [TestCase(12)]
        [TestCase(13)]
        [TestCase(14)]
        [TestCase(15)]
        [TestCase(16)]
        [TestCase(17)]
        [TestCase(18)]
        public async Task InvalidProviderBatchFailsBeforeFactoryAsync(int scenario)
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolAlert alert = Alert();
            MachineToolMessage message = Message(1);
            MachineToolNotificationSnapshot snapshot = scenario switch
            {
                0 => null!,
                1 => new() { Alerts = [alert with { Severity = 333 }] },
                2 => new() { Alerts = [alert with { Severity = 1001 }] },
                3 => new() { Alerts = [alert, alert] },
                4 => new() { Alerts = [alert with { Key = string.Empty }] },
                5 => new() { Alerts = [alert with { ErrorCode = string.Empty }] },
                6 => new() { Alerts = [alert with { Message = LocalizedText.Null }] },
                7 => new() { Alerts = [alert with { ConditionClassId = Ua.DataTypeIds.String }] },
                8 => new() { Alerts = [alert with { Sample = new MachineToolSampleInfo(default, StatusCodes.Good) }] },
                9 => new() { Messages = [message with { Sequence = 2 }] },
                10 => new() { Messages = [message with { Severity = 334 }] },
                11 => new() { Messages = [message with { Time = default }] },
                12 => new() { Alerts = [alert, alert with { Key = "Other" }] },
                13 => new() { Messages = [message, Message(2)] },
                14 => new() { Alerts = [alert with { AllowedOperations = (MachineToolConditionOperations)16 }] },
                15 => new() { Alerts = [alert with { ErrorCode = new string('x', 4097) }] },
                16 => LargeBatch(alert),
                17 => new() { Alerts = [null!] },
                _ => new() { Messages = [null!] }
            };
            var provider = new MachineToolNotificationTests.NotificationProvider(snapshot);
            var options = new MachineToolNotificationOptions
            {
                Provider = provider,
                MaximumAlerts = scenario == 12 ? 1 : 128,
                MaximumMessages = scenario == 13 ? 1 : 128,
                MaximumTextLength = scenario == 16 ? 65536 : 4096
            };
            bool factoryCalled = false;
            MachineToolBuilder<MachineToolState> builder = fixture.Context.AddMachineTool("Invalid",
                (context, parent, name) =>
                {
                    factoryCalled = true;
                    return context.CreateInstanceOfMachineToolType(parent, name);
                }).WithProvider(new InMemoryMachineToolProvider(new MachineToolSnapshot()))
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Validation");
                    identity.SerialNumber = "Invalid";
                    identity.ProductInstanceUri = "urn:notification-validation";
                }).WithNotifications(options);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await builder.BuildAsync().ConfigureAwait(false))!;

            StatusCode expected = scenario switch
            {
                0 => StatusCodes.BadNoData,
                12 or 13 or 16 => StatusCodes.BadEncodingLimitsExceeded,
                _ => StatusCodes.BadInvalidState
            };
            Assert.That(error.StatusCode, Is.EqualTo(expected));
            Assert.That(factoryCalled, Is.False);
        }

        /// <summary>
        /// Every invalid configuration is rejected before any provider-driven node is published.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public async Task InvalidBoundsRejectSelectionAsync(int scenario)
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var options = new MachineToolNotificationOptions
            {
                Provider = new MachineToolNotificationTests.NotificationProvider(new MachineToolNotificationSnapshot())
            };
            options = scenario switch
            {
                0 => options with { MaximumAlerts = 0 },
                1 => options with { MaximumMessages = 1025 },
                2 => options with { MaximumTextLength = 0 },
                3 => options with { MaximumTextLength = 65537 },
                4 => options with { RefreshTimeout = TimeSpan.Zero },
                _ => options with { RefreshTimeout = TimeSpan.FromMinutes(2) }
            };
            MachineToolBuilder<MachineToolState> builder = fixture.Context.AddMachineTool("Limits")
                .WithProvider(new InMemoryMachineToolProvider(new MachineToolSnapshot()))
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Validation");
                    identity.SerialNumber = "Limits";
                    identity.ProductInstanceUri = "urn:notification-limits";
                }).WithNotifications(options);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await builder.BuildAsync().ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        /// <summary>
        /// Existing alert state and event identity are unchanged by an invalid later inventory.
        /// </summary>
        [Test]
        public async Task RejectedRefreshPreservesExistingAlertAndPendingRetentionAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolAlert alert = Alert();
            var provider = new MachineToolNotificationTests.NotificationProvider(
                new MachineToolNotificationSnapshot { Alerts = [alert] });
            await using MachineToolRuntime<MachineToolState> runtime =
                await MachineToolNotificationTests.BuildAsync(fixture, provider).ConfigureAwait(false);
            Assert.That(runtime.Notifications!.TryGetAlert(alert.Key, out AlertState? state), Is.True);
            ByteString eventId = state!.EventId!.Value;
            provider.Snapshot = new MachineToolNotificationSnapshot
            {
                Revision = 1,
                Alerts = [alert with { Severity = 800 }, alert with { Key = "Invalid", Severity = 1 }]
            };
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await runtime.RefreshNotificationsAsync().ConfigureAwait(false));
            Assert.That(state.Severity!.Value, Is.EqualTo(500));
            Assert.That(state.EventId.Value, Is.EqualTo(eventId));
            provider.Snapshot = new MachineToolNotificationSnapshot { Revision = 1 };
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await runtime.RefreshNotificationsAsync().ConfigureAwait(false));
            Assert.That(runtime.Notifications.TryGetAlert(alert.Key, out _), Is.True);
            provider.Snapshot = new MachineToolNotificationSnapshot
            {
                Revision = 1,
                Alerts = [alert with { Severity = 800 }]
            };
            await runtime.RefreshNotificationsAsync().ConfigureAwait(false);
            provider.Snapshot = new MachineToolNotificationSnapshot { Alerts = [alert] };
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await runtime.RefreshNotificationsAsync().ConfigureAwait(false));
            Assert.That(state.Severity.Value, Is.EqualTo(800));
        }

        /// <summary>
        /// Supplies one deterministic valid warning.
        /// </summary>
        internal static MachineToolAlert Alert()
        {
            return new()
            {
                Key = "Warning",
                ErrorCode = "W1",
                Message = new LocalizedText("Warning"),
                Severity = 500,
                IsActive = true,
                Sample = new MachineToolSampleInfo(
                new DateTime(2026, 8, 1, 2, 3, 4, DateTimeKind.Utc), StatusCodes.Good)
            };
        }

        /// <summary>
        /// Supplies one deterministic informational notification.
        /// </summary>
        internal static MachineToolMessage Message(ulong sequence)
        {
            return new()
            {
                Sequence = sequence,
                Identifier = "M1",
                Message = new LocalizedText("Ready"),
                Severity = 100,
                Time = new DateTime(2026, 8, 1, 2, 3, 4, DateTimeKind.Utc)
            };
        }

        /// <summary>
        /// Exceeds the aggregate bound without exceeding an individual field's configured bound.
        /// </summary>
        private static MachineToolNotificationSnapshot LargeBatch(MachineToolAlert alert)
        {
            var alerts = new MachineToolAlert[17];
            string text = new('x', 65536);
            for (int index = 0; index < alerts.Length; index++)
            {
                alerts[index] = alert with
                {
                    Key = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Message = new LocalizedText(text)
                };
            }
            return new MachineToolNotificationSnapshot { Alerts = alerts.ToArrayOf() };
        }
    }
}
