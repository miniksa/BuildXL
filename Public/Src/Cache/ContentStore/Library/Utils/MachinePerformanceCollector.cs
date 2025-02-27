// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using BuildXL.Utilities;

#nullable enable

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Performance statistics in a strongly typed form.
    /// </summary>
    internal record PerformanceStatistics
    {
        public int CommitTotalMb { get; init; } = -1;

        public long MachineKbitsPerSecReceived { get; init; } = -1;

        public long MachineKbitsPerSecSent { get; init; } = -1;

        public int ProcessCpuPercentage { get; init; } = -1;

        public int CpuUsagePercentage { get; init; } = -1;

        public int ProcessWorkingSetMb { get; init; } = -1;

        public long GCTotalMemoryMb { get; init; } = -1;

        public int ProcessThreadCount { get; init; } = -1;

        public int ThreadPoolWorkerThreads { get; init; } = -1;

        public int ThreadPoolCompletionPortThreads { get; init; } = -1;

        public PerformanceCollector.Aggregator.DiskStatistics? DriveD { get; init; }

        public PerformanceCollector.Aggregator.DiskStatistics? DriveK { get; init; }

        /// <summary>
        /// Gets a string representation suitable for tracing.
        /// </summary>
        /// <returns></returns>
        public string ToTracingString()
        {
            List<string> parts = new List<string>();
            CollectMetrics((metricName, value) => parts.Add($"{metricName}: {value}"));
            return string.Join(", ", parts);
        }

        public delegate void AddMetric(string metricName, long value);

        public void CollectMetrics(AddMetric addMetric)
        {
            addMetric("CommitTotalMb", CommitTotalMb);
            addMetric("CpuUsagePercentage", CpuUsagePercentage);
            addMetric("MachineKbitsPerSecReceived", MachineKbitsPerSecReceived);
            addMetric("MachineKbitsPerSecSent", MachineKbitsPerSecSent);
            addMetric("ProcessCpuPercentage", ProcessCpuPercentage);
            addMetric("ProcessWorkingSetMB", ProcessWorkingSetMb);
            addMetric("GCTotalMemoryMb", (long)Math.Ceiling(GC.GetTotalMemory(forceFullCollection: false) / 1e6));
            addMetric("ProcessThreadCount", ProcessThreadCount);

            ThreadPool.GetAvailableThreads(out var workerThreads, out var completionPortThreads);
            addMetric("ThreadPoolWorkerThreads", workerThreads);
            addMetric("ThreadPoolCompletionPortThreads", completionPortThreads);

            // We're interested only in two drives: D and K, and K drive is optional.
            addDriveStats(DriveD);
            addDriveStats(DriveK);

            void addDriveStats(PerformanceCollector.Aggregator.DiskStatistics? stats)
            {
                if (stats != null)
                {
                    var prefix = stats.Drive + "_";
                    addMetric(prefix + "QueueDepth", (long)stats.QueueDepth.Latest);
                    addMetric(prefix + "AvailableSpaceGb", (long)stats.AvailableSpaceGb.Latest);
                }
            }
        }
    }

    internal class MachinePerformanceCollector
    {
        private readonly PerformanceCollector _collector = new PerformanceCollector(collectionFrequency: TimeSpan.FromMinutes(1));
        private readonly PerformanceCollector.Aggregator _perfStatsAggregator;

        public MachinePerformanceCollector()
        {
            _perfStatsAggregator = _collector.CreateAggregator();
        }

        /// <summary>
        /// Get machine performance statistics.
        /// </summary>
        public PerformanceStatistics GetMachinePerformanceStatistics()
        {
            var perfInfo = _perfStatsAggregator.ComputeMachinePerfInfo(ensureSample: true);
            var commitUsedMb = perfInfo.CommitUsedMb ?? -1;
            var cpuUsagePercentage = perfInfo.CpuUsagePercentage;
            var machineKbitsPerSecReceived = (long)perfInfo.MachineKbitsPerSecReceived;
            var machineKbitsPerSecSent = (long)perfInfo.MachineKbitsPerSecSent;
            var processCpuPercentage = perfInfo.ProcessCpuPercentage;
            var processWorkingSetMb = perfInfo.ProcessWorkingSetMB;
            var gcTotalMemoryMB = (long)Math.Ceiling(GC.GetTotalMemory(forceFullCollection: false) / 1e6);
            var processThreadCount = (int)_perfStatsAggregator.ProcessThreadCount.Latest;

            ThreadPool.GetAvailableThreads(out var workerThreads, out var completionPortThreads);
            var threadPoolWorkerThreads = workerThreads;
            var threadPoolCompletionPortThreads = completionPortThreads;

            // We're interested only in two drives: D and K, and K drive is optional.
            var driveD = _perfStatsAggregator.DiskStats.FirstOrDefault(s => s.Drive == "D");
            var driveK = _perfStatsAggregator.DiskStats.FirstOrDefault(s => s.Drive == "K");

            return new PerformanceStatistics()
            {
                CommitTotalMb = commitUsedMb,
                CpuUsagePercentage = cpuUsagePercentage,
                MachineKbitsPerSecReceived = machineKbitsPerSecReceived,
                MachineKbitsPerSecSent = machineKbitsPerSecSent,
                ProcessCpuPercentage = processCpuPercentage,
                ProcessWorkingSetMb = processWorkingSetMb,
                GCTotalMemoryMb = gcTotalMemoryMB,
                ProcessThreadCount = processThreadCount,
                ThreadPoolWorkerThreads = threadPoolWorkerThreads,
                ThreadPoolCompletionPortThreads = threadPoolCompletionPortThreads,
                DriveD = driveD,
                DriveK = driveK,
            };
        }
    }

    /// <summary>
    /// A set of extension methods to keep the implementation of <see cref="MachinePerformanceCollector"/> simpler.
    /// </summary>
    internal static class StringBuilderExtensions
    {
        /// <nodoc />
        public static StringBuilder AddMetric(this StringBuilder sb, string metricName, long value, bool first = false)
        {

            sb.Append($"{(!first ? ", " : "")}{metricName}: {value}");
            return sb;
        }
    }

}
