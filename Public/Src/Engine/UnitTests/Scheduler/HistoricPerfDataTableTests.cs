// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Remoting;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Scheduler;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Scheduler
{
    public class HistoricPerfDataTableTests
    {
        private LoggingContext LoggingContext = new LoggingContext("Unittests");

        [Fact]
        public void PipHistoricPerfDataConstructorDoesntCrash()
        {
            foreach (var obj in ConstructorDoesntCrashTestData())
            {
                var executionStart = (DateTime)obj[0];
                var executionStop = (DateTime)obj[1];
                var processExecutionTime = (TimeSpan)obj[2];
                var fileMonitoringWarnings = (int)obj[3];
                var ioCounters = (IOCounters)obj[4];
                var userTime = (TimeSpan)obj[5];
                var kernelTime = (TimeSpan)obj[6];
                var peakMemoryUsage = (ulong)obj[7];
                var numberOfProcesses = (uint)obj[8];
                var workerId = (uint)obj[9];

                if (executionStart > executionStop)
                {
                    continue;
                }

                var performance = new ProcessPipExecutionPerformance(
                    PipExecutionLevel.Executed,
                    executionStart,
                    executionStop,
                    FingerprintUtilities.ZeroFingerprint,
                    processExecutionTime,
                    new FileMonitoringViolationCounters(fileMonitoringWarnings, fileMonitoringWarnings, fileMonitoringWarnings),
                    ioCounters,
                    userTime,
                    kernelTime,
                    ProcessMemoryCounters.CreateFromBytes(peakMemoryUsage, peakMemoryUsage, peakMemoryUsage, peakMemoryUsage),
                    numberOfProcesses,
                    workerId,
                    0);
                var data = new ProcessPipHistoricPerfData(performance, (long)processExecutionTime.TotalMilliseconds);
                data = data.Merge(data);
                Analysis.IgnoreResult(data);
            }
        }

        public static IEnumerable<object[]> ConstructorDoesntCrashTestData()
        {
            var times = new object[]{
                new DateTime(DateTime.MinValue.Year + 1, 1, 1).ToUniversalTime(),
                new DateTime(2015, 1, 1).ToUniversalTime(),
                new DateTime(DateTime.MaxValue.Year, 1, 1).ToUniversalTime() };

            var spans = new object[] { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.MaxValue };
            var ints = new object[] { 0, int.MaxValue };

            // Let's say that the highest possible memory usage is currently 1TB.
            var ulongs = new object[] { (ulong)0, (ulong)1, (ulong)1024 * 1024 * 1024 * 1024 };
            var uints = new object[] { (uint)0, (uint)1, uint.MaxValue };
            var ioCounters = new object[]{
                new IOCounters(
                    readCounters: new IOTypeCounters(operationCount: 1, transferCount: ulong.MaxValue),
                    writeCounters: new IOTypeCounters(operationCount: 0, transferCount: 0),
                    otherCounters: new IOTypeCounters(operationCount: 0, transferCount: 0)
                    ),
                new IOCounters(
                    readCounters: new IOTypeCounters(operationCount: 0, transferCount: 0),
                    writeCounters: new IOTypeCounters(operationCount: 0, transferCount: 0),
                    otherCounters: new IOTypeCounters(operationCount: 0, transferCount: 0)
                    )};

            return BuildXLTestBase.CrossProductN(times, times, spans, ints, ioCounters, spans, spans, ulongs, uints, uints);
        }

        [Fact]
        public void EverythingAsync()
        {
            const int MaxExecTime = 24 * 3600 * 1000;

            var stream = new MemoryStream();
            var r = new Random(0);
            for (int i = 0; i < 10; i++)
            {
                int seed = r.Next(100 * 1000);
                HistoricPerfDataTable table = new HistoricPerfDataTable(LoggingContext);
                XAssert.IsTrue(table.Count == 0);

                var s = new Random(seed);
                var buffer = new byte[sizeof(long)];
                for (int j = 0; j < 10; j++)
                {
                    s.NextBytes(buffer);
                    long semiStableHash = BitConverter.ToInt64(buffer, 0);

                    var execTime = (uint)s.Next(MaxExecTime);
                    var processPipExecutionPerformance = new ProcessPipExecutionPerformance(
                        PipExecutionLevel.Executed,
                        DateTime.UtcNow,
                        DateTime.UtcNow.AddMilliseconds(execTime),
                        FingerprintUtilities.ZeroFingerprint,
                        TimeSpan.FromMilliseconds(execTime),
                        default(FileMonitoringViolationCounters),
                        default(IOCounters),
                        TimeSpan.FromMilliseconds(execTime),
                        TimeSpan.FromMilliseconds(execTime / 2),
                        ProcessMemoryCounters.CreateFromMb(1024, 1024, 1024, 1024),
                        1,
                        workerId: 0,
                        suspendedDurationMs: 0);

                    ProcessPipHistoricPerfData runTimeData = new ProcessPipHistoricPerfData(processPipExecutionPerformance, execTime);
                    table[semiStableHash] = runTimeData;
                }

                XAssert.IsTrue(table.Count == 10);

                stream.Position = 0;
                table.Save(stream);
                stream.Position = 0;
                table = HistoricPerfDataTable.Load(LoggingContext, stream);
                XAssert.IsTrue(table.Count == 10);

                s = new Random(seed);
                for (int j = 0; j < 10; j++)
                {
                    s.NextBytes(buffer);
                    long semiStableHash = BitConverter.ToInt64(buffer, 0);
                    XAssert.IsTrue(table[semiStableHash].ExeDurationInMs >= (uint) s.Next(MaxExecTime));
                }
            }
        }

        [Fact]
        public void TimeToLive()
        {
            int execTime = 1;
            uint runTime = 2;
            var processPipExecutionPerformance = new ProcessPipExecutionPerformance(
                PipExecutionLevel.Executed,
                DateTime.UtcNow,
                DateTime.UtcNow.AddMilliseconds(execTime),
                FingerprintUtilities.ZeroFingerprint,
                TimeSpan.FromMilliseconds(execTime),
                default(FileMonitoringViolationCounters),
                default(IOCounters),
                TimeSpan.FromMilliseconds(execTime),
                TimeSpan.FromMilliseconds(execTime / 2),
                ProcessMemoryCounters.CreateFromMb(1024, 1024, 1024, 1024),
                1,
                workerId: 0,
                suspendedDurationMs: 0);

            ProcessPipHistoricPerfData runTimeData = new ProcessPipHistoricPerfData(processPipExecutionPerformance, runTime);
            HistoricPerfDataTable table = new HistoricPerfDataTable(LoggingContext);
            var semiStableHashToKeep = 0;
            table[semiStableHashToKeep] = runTimeData;
            var semiStableHashToDrop = 1;
            table[semiStableHashToDrop] = runTimeData;
            var stream = new MemoryStream();
            for (int i = 0; i < ProcessPipHistoricPerfData.DefaultTimeToLive; i++)
            {
                stream.Position = 0;
                table.Save(stream);
                stream.Position = 0;
                table = HistoricPerfDataTable.Load(LoggingContext, stream);
                Analysis.IgnoreResult(table[semiStableHashToKeep]);
            }

            stream.Position = 0;
            table = HistoricPerfDataTable.Load(LoggingContext, stream);
            XAssert.AreEqual(1u, table[semiStableHashToKeep].ExeDurationInMs);
            XAssert.AreEqual(0u, table[semiStableHashToDrop].ExeDurationInMs);
        }
    }
}
