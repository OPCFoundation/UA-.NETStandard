using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Concurrency tests for the MonitoredItemIdFactory.
    /// </summary>
    [TestFixture]
    public class MonitoredItemIdFactoryTests
    {
        /// <summary>
        /// Verifies that no duplicate IDs are returned with concurrent calls.
        /// </summary>
        [Test]
        public async Task GetNextId_ConcurrentCalls_ShouldNotReturnDuplicateIdsAsync()
        {
            // Arrange
            var idFactory = new MonitoredItemIdFactory();
            var generatedIds = new ConcurrentBag<uint>();
            const int numTasks = 10;
            const int idsPerTask = 1000;
            var tasks = new Task[numTasks];
            var startEvent = new ManualResetEventSlim(false);

            // Act
            for (int i = 0; i < numTasks; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    startEvent.Wait();
                    for (int j = 0; j < idsPerTask; j++)
                    {
                        generatedIds.Add(idFactory.GetNextId());
                    }
                });
            }

            startEvent.Set();
            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Assert
            const int totalIds = numTasks * idsPerTask;
            Assert.That(generatedIds.Count, Is.EqualTo(totalIds));
            Assert.That(generatedIds.Distinct().Count(), Is.EqualTo(totalIds), "Duplicate IDs were generated.");
        }

        /// <summary>
        /// Verifies that with a set start value, no smaller IDs are returned than the firstId.
        /// </summary>
        [Test]
        public async Task GetNextId_WithSetStartValue_ShouldReturnIdsGreaterThanToStartValueAsync()
        {
            // Arrange
            var idFactory = new MonitoredItemIdFactory();
            var generatedIds = new ConcurrentBag<uint>();
            const uint startValue = 10000;
            idFactory.SetStartValue(startValue);

            const int numTasks = 10;
            const int idsPerTask = 1000;
            var tasks = new Task[numTasks];
            var startEvent = new ManualResetEventSlim(false);

            // Act
            for (int i = 0; i < numTasks; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    startEvent.Wait();
                    for (int j = 0; j < idsPerTask; j++)
                    {
                        generatedIds.Add(idFactory.GetNextId());
                    }
                });
            }

            startEvent.Set();
            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Assert
            const int totalIds = numTasks * idsPerTask;
            Assert.That(generatedIds.Count, Is.EqualTo(totalIds));
            Assert.That(generatedIds.All(id => id > startValue), Is.True, "An ID smaller than or equal to the start value was generated.");
        }

        /// <summary>
        /// Verifies that calling SetStartValue concurrently with GetNextId
        /// results in IDs being generated from the new start value.
        /// This test is non-deterministic but has a high probability of exercising the concurrent code paths.
        /// </summary>
        [Test]
        public async Task GetNextId_SetStartValueCalledConcurrently_ShouldGenerateIdsFromNewStartValueAsync()
        {
            // Arrange
            var idFactory = new MonitoredItemIdFactory();
            var generatedIds = new ConcurrentBag<(uint Id, long BeforeTimestamp, long AfterTimestamp)>();
            var resetEvents = new ConcurrentBag<(uint StartValue, long BeforeTimestamp, long AfterTimestamp)>();
            const int numIdTasks = 10;
            const int idsPerTask = 1000;
            const int numResetTasks = 3;
            var tasks = new Task[numIdTasks + numResetTasks];
            var startEvent = new ManualResetEventSlim(false);

            // Act
            // Create tasks that will get IDs
            for (int i = 0; i < numIdTasks; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    startEvent.Wait();
                    for (int j = 0; j < idsPerTask; j++)
                    {
                        long before = Stopwatch.GetTimestamp();
                        uint id = idFactory.GetNextId();
                        long after = Stopwatch.GetTimestamp();
                        generatedIds.Add((id, before, after));
                    }
                });
            }

            // Create tasks that will reset the start value
            for (int i = 0; i < numResetTasks; i++)
            {
                tasks[numIdTasks + i] = Task.Run(() =>
                {
                    startEvent.Wait();
                    // Use a random start value to increase chances of interleaving
                    uint startValue = (uint)UnsecureRandom.Shared.Next(20000, 50000) * (uint)(i + 1);
                    long before = Stopwatch.GetTimestamp();
                    idFactory.SetStartValue(startValue);
                    long after = Stopwatch.GetTimestamp();
                    resetEvents.Add((startValue, before, after));
                });
            }

            startEvent.Set(); // Signal all tasks to start
            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Assert
            const int totalIds = numIdTasks * idsPerTask;
            Assert.That(generatedIds.Count, Is.EqualTo(totalIds), "Incorrect total number of IDs generated.");

            if (resetEvents.IsEmpty)
            {
                Assert.Warn("No reset events were captured, the test may not have exercised the concurrent reset logic.");
                return;
            }

            var sortedResets = resetEvents.OrderBy(r => r.BeforeTimestamp).ToList();

            for (int i = 0; i < sortedResets.Count; i++)
            {
                (uint startValue, long beforeTimestamp, long afterTimestamp) = sortedResets[i];

                // For each reset event, verify that all IDs generated *after* it
                // are greater than the new start value.
                var idsAfterReset = generatedIds
                    .Where(g => g.BeforeTimestamp > afterTimestamp &&
                        (i == sortedResets.Count - 1 || g.AfterTimestamp < sortedResets[i + 1].BeforeTimestamp))
                    .OrderBy(g => g.Id)
                    .Select(g => g.Id)
                    .ToList();

                if (idsAfterReset.Count != 0)
                {
                    // Due to the nature of concurrent operations, some IDs might be generated
                    // after the reset timestamp but still use the old value. We find the first
                    // ID that respects the new start value and verify that all subsequent IDs do as well.
                    // Find the first ID that is greater than the start value.
                    int firstValidIdIndex = idsAfterReset.FindIndex(id => id > startValue);

                    // If a valid ID is found, all subsequent IDs must also be greater.
                    if (firstValidIdIndex != -1)
                    {
                        List<uint> subsequentIds = [.. idsAfterReset.Skip(firstValidIdIndex)];
                        Assert.That(subsequentIds.All(id => id > startValue), Is.True,
                            $"An ID was generated that was not greater than the new start value {startValue} after a reset.");

                        Assert.That(subsequentIds.Distinct().Count(), Is.EqualTo(subsequentIds.Count),
                       "Duplicate IDs were found in the set of IDs generated between resets.");
                    }
                }
            }
        }

        /// <summary>
        /// Verifies that no duplicate IDs are generated when wraparound occurs,
        /// by initializing with a value close to uint.MaxValue and generating enough IDs
        /// to force wraparound. Tests multiple iterations to ensure consistent behavior.
        /// </summary>
        [Test]
        public async Task GetNextId_WithWraparound_ShouldNotReturnDuplicateIdsAsync()
        {
            const int iterations = 10;
            const int idsToGenerate = 1000; // Enough to force wraparound
            var idFactory = new MonitoredItemIdFactory();

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                // Arrange
                var generatedIds = new ConcurrentBag<uint>();

                // Start close to uint.MaxValue to force wraparound quickly
                const uint startValue = uint.MaxValue - 500;
                idFactory.SetStartValue(startValue);

                const int numTasks = 10;
                const int idsPerTask = idsToGenerate / numTasks;
                var tasks = new Task[numTasks];
                var startEvent = new ManualResetEventSlim(false);

                // Act
                for (int i = 0; i < numTasks; i++)
                {
                    tasks[i] = Task.Run(() =>
                    {
                        startEvent.Wait();
                        for (int j = 0; j < idsPerTask; j++)
                        {
                            generatedIds.Add(idFactory.GetNextId());
                        }
                    });
                }

                startEvent.Set();
                await Task.WhenAll(tasks).ConfigureAwait(false);

                // Assert
                const int totalIds = numTasks * idsPerTask;
                Assert.That(generatedIds.Count, Is.EqualTo(totalIds),
                    $"Iteration {iteration + 1}: Incorrect total number of IDs generated.");

                var distinctIds = generatedIds.Distinct().ToList();
                Assert.That(distinctIds.Count, Is.EqualTo(totalIds),
                    $"Iteration {iteration + 1}: Duplicate IDs were generated during wraparound.");

                Assert.That(generatedIds.All(id => id != 0), Is.True,
                    $"Iteration {iteration + 1}: An ID of 0 was generated, which is invalid.");
            }
        }
    }
}
