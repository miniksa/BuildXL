﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.Serialization;
using BuildXL.Cache.ContentStore.Distributed.NuCache.CopyScheduling;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using ContentStore.Grpc;

#nullable disable

namespace BuildXL.Cache.Host.Configuration
{
    /// <summary>
    /// Feature flag settings for an L2 Content Cache.
    /// </summary>
    [DataContract]
    public class DistributedContentSettings
    {
        private const int DefaultMaxConcurrentCopyOperations = 512;

        internal static readonly int[] DefaultRetryIntervalForCopiesMs =
            new int[]
            {
                // retry the first 2 times quickly.
                20,
                200,

                // then back-off exponentially.
                1000,
                5000,
                10000,
                30000,

                // Borrowed from Empirical CacheV2 determined to be appropriate for general remote server restarts.
                60000,
                120000,
            };

        public static DistributedContentSettings CreateDisabled()
        {
            return new DistributedContentSettings
            {
                IsDistributedContentEnabled = false,
            };
        }

        public static DistributedContentSettings CreateEnabled()
        {
            return new DistributedContentSettings()
            {
                IsDistributedContentEnabled = true,
            };
        }

        /// <summary>
        /// Settings for running deployment launcher
        /// </summary>
        [DataMember]
        public LauncherSettings LauncherSettings { get; set; } = null;

        /// <summary>
        /// Settings for running cache out of proc as a .net core process.
        /// </summary>
        [DataMember]
        public OutOfProcCacheSettings OutOfProcCacheSettings { get; set; }

        /// <summary>
        /// If true the cache should run out of proc as a .net core process.
        /// </summary>
        /// <remarks>
        /// If this property is true and <see cref="OutOfProcCacheSettings"/> is null, that property should be created
        /// by the host and set <see cref="BuildXL.Cache.Host.Configuration.OutOfProcCacheSettings.CacheConfigPath"/> property.
        /// </remarks>
        [DataMember]
        public bool? RunCacheOutOfProc { get; set; }

        [DataMember]
        public LogManagerConfiguration LogManager { get; set; } = null;

        /// <summary>
        /// Feature flag to turn on distributed content tracking (L2/datacenter cache).
        /// </summary>
        [DataMember]
        public bool IsDistributedContentEnabled { get; set; }

        /// <summary>
        /// Feature flag to turn off Grpc.Core grpc server and instead use AspNet.Core grpc implementation
        /// </summary>
        [DataMember]
        public bool EnableAspNetCoreGrpc { get; set; }

        /// <summary>
        /// Grpc port for backing cache service instance
        /// </summary>
        [DataMember]
        public int? BackingGrpcPort { get; set; } = null;

        /// <summary>
        /// Grpc port for backing cache service instance
        /// </summary>
        [DataMember]
        public string BackingScenario { get; set; } = null;

        /// <summary>
        /// The amount of time for nagling GetBulk (locations) for proactive copy operations
        /// </summary>
        [DataMember]
        [Validation.Range(0, double.MaxValue)]
        public double ProactiveCopyGetBulkIntervalSeconds { get; set; } = 10;

        /// <summary>
        /// The size of nagle batch for proactive copy get bulk
        /// </summary>
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int ProactiveCopyGetBulkBatchSize { get; set; } = 20;

        [DataMember]
        [Validation.Range(0, int.MaxValue)]
        public int ProactiveCopyMaxRetries { get; set; } = 0;

        /// <summary>
        /// Configurable Keyspace Prefixes
        /// </summary>
        [DataMember]
        public string KeySpacePrefix { get; set; } = "CBPrefix";

        /// <summary>
        /// Name of the EventHub instance to connect to.
        /// </summary>
        [DataMember]
        public string EventHubName { get; set; } = "eventhub";

        /// <summary>
        /// Name of the EventHub instance's consumer group name.
        /// </summary>
        [DataMember]
        public string EventHubConsumerGroupName { get; set; } = "$Default";

        /// <summary>
        /// Adds suffix to resource names to allow instances in different ring to
        /// share the same resource without conflicts. In particular this will add the
        /// ring suffix to Azure blob container name and Redis keyspace (which is included
        /// in event hub epoch). 
        /// </summary>
        [DataMember]
        public bool UseRingIsolation { get; set; } = false;

        // Redis-related configuration

        [DataMember]
        public bool PreventRedisUsage { get; set; } = false;

        public void DisableRedis()
        {
            ContentMetadataStoreMode = Configuration.ContentMetadataStoreMode.Distributed;
            MemoizationContentMetadataStoreModeOverride = Configuration.ContentMetadataStoreMode.Distributed;
            LocationContentMetadataStoreModeOverride = Configuration.ContentMetadataStoreMode.Distributed;
            BlobContentMetadataStoreModeOverride = Configuration.ContentMetadataStoreMode.Distributed;
            UseBlobClusterStateStorage = true;
            BlobClusterStateStorageStandalone = true;
            UseBlobVolatileStorage = true;
            ContentMetadataUseBlobCheckpointRegistry = true;
            ContentMetadataUseBlobCheckpointRegistryStandalone = true;
        }

        /// <summary>
        /// TTL to be set in Redis for memoization entries.
        /// </summary>
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int RedisMemoizationExpiryTimeMinutes { get; set; } = 1500;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int RedisBatchPageSize { get; set; } = 500;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? RedisConnectionErrorLimit { get; set; }

        #region Redis Connection Multiplexer Configuration

        [DataMember]
        public bool? UseRedisPreventThreadTheftFeature { get; set; }

        [DataMember]
        [Validation.Enum(typeof(Severity), allowNull: true)]
        public string RedisInternalLogSeverity { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? RedisConfigCheckInSeconds { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? RedisKeepAliveInSeconds { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? RedisConnectionTimeoutInSeconds { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? RedisMultiplexerOperationTimeoutTimeoutInSeconds { get; set; }
        #endregion Redis Connection Multiplexer Configuration

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? RedisMemoizationDatabaseOperationTimeoutInSeconds { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? RedisMemoizationSlowOperationCancellationTimeoutInSeconds { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? RedisReconnectionLimitBeforeServiceRestart { get; set; }

        [DataMember]
        public double? DefaultRedisOperationTimeoutInSeconds { get; set; }

        [DataMember]
        public TimeSpan? MinRedisReconnectInterval { get; set; }

        [DataMember]
        public bool? CancelBatchWhenMultiplexerIsClosed { get; set; }

        [DataMember]
        public bool? TreatObjectDisposedExceptionAsTransient { get; set; }

        [DataMember]
        [Validation.Range(-1, int.MaxValue)]
        public int? RedisGetBlobTimeoutMilliseconds { get; set; }

        [DataMember]
        [Validation.Range(0, int.MaxValue)]
        public int? RedisGetCheckpointStateTimeoutInSeconds { get; set; }

        // Redis retry configuration
        [DataMember]
        [Validation.Range(0, int.MaxValue, minInclusive: false)]
        public int? RedisFixedIntervalRetryCount { get; set; }

        // Just setting this value is enough to configure a custom exponential back-off policy with default min/max/interval.
        [DataMember]
        [Validation.Range(0, int.MaxValue, minInclusive: false)]
        public int? RedisExponentialBackoffRetryCount { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? RedisExponentialBackoffMinIntervalInSeconds { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? RedisExponentialBackoffMaxIntervalInSeconds { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? RedisExponentialBackoffDeltaIntervalInSeconds { get; set; }


        // TODO: file a work item to remove the flag!
        [DataMember]
        public bool CheckLocalFiles { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int MaxShutdownDurationInMinutes { get; set; } = 30;

        /// <summary>
        /// If true, then content store will start a self-check to validate that the content in cache is valid at startup.
        /// </summary>
        [DataMember]
        public bool StartSelfCheckAtStartup { get; set; } = false;

        /// <summary>
        /// An interval between self checks performed by a content store to make sure that all the data on disk matches it's hashes.
        /// </summary>
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int SelfCheckFrequencyInMinutes { get; set; } = (int)TimeSpan.FromDays(1).TotalMinutes;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? SelfCheckProgressReportingIntervalInMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? SelfCheckDelayInMilliseconds { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? SelfCheckDefaultHddDelayInMilliseconds { get; set; }

        /// <summary>
        /// An epoch used for reseting self check of a content directory.
        /// </summary>
        [DataMember]
        public string SelfCheckEpoch { get; set; } = "E0";

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? ReserveSpaceTimeoutInMinutes { get; set; }

        [DataMember]
        public bool IsRepairHandlingEnabled { get; set; } = false;

        [DataMember]
        public bool UseMdmCounters { get; set; } = true;

        [DataMember]
        public bool UseContextualEntryDatabaseOperationLogging { get; set; } = false;

        [DataMember]
        public bool TraceTouches { get; set; } = true;

        [DataMember]
        public bool? TraceNoStateChangeDatabaseOperations { get; set; }

        [DataMember]
        public bool LogReconciliationHashes { get; set; } = false;

        /// <summary>
        /// The TTL of blobs in Redis. Setting to 0 will disable blobs.
        /// </summary>
        [DataMember]
        [Validation.Range(0, int.MaxValue)]
        public int BlobExpiryTimeMinutes { get; set; } = 0;

        /// <summary>
        /// Max size of blobs in Redis. Setting to 0 will disable blobs.
        /// </summary>
        [DataMember]
        [Validation.Range(0, long.MaxValue)]
        public long MaxBlobSize { get; set; } = 1024 * 4;

        /// <summary>
        /// Max capacity that blobs can occupy in Redis. Setting to 0 will disable blobs.
        /// </summary>
        [DataMember]
        [Validation.Range(0, long.MaxValue)]
        public long MaxBlobCapacity { get; set; } = 1024 * 1024 * 1024;

        /// <summary>
        /// The span of time that will delimit the operation count limit for blob operations.
        /// </summary>
        [DataMember]
        [Validation.Range(0, int.MaxValue)]
        public int? BlobOperationLimitSpanSeconds { get; set; }

        /// <summary>
        /// The amount of blob operations that we allow to go to Redis in a given period of time.
        /// </summary>
        [DataMember]
        [Validation.Range(0, int.MaxValue)]
        public long? BlobOperationLimitCount { get; set; }

        /// <summary>
        /// Amount of entries to compute evictability metric for in a single pass. The larger this is, the faster the
        /// candidate pool fills up, but also the slower it is to produce a candidate. Helps control how fast we need
        /// to produce candidates.
        /// </summary>
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int EvictionWindowSize { get; set; } = 500;

        /// <summary>
        /// Amount of entries to compute evictability metric for before determining eviction order. The larger this is,
        /// the slower and more resources eviction takes, but also the more accurate it becomes.
        /// </summary>
        /// <remarks>
        /// Two pools are kept in memory at the same time, so we effectively keep double the amount of data in memory.
        /// </remarks>
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int EvictionPoolSize { get; set; } = 5000;

        /// <summary>
        /// A candidate must have an age older than this amount, or else it won't be evicted.
        /// </summary>
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? EvictionMinAgeMinutes { get; set; }

        /// <summary>
        /// Fraction of the pool considered trusted to be in the accurate order.
        /// </summary>
        [DataMember]
        [Validation.Range(0, 1, maxInclusive: false)]
        public float EvictionRemovalFraction { get; set; } = 0.015355f;

        /// <summary>
        /// Fraction of the pool that can be trusted to be spurious at each iteration
        /// </summary>
        [DataMember]
        [Validation.Range(0, 1)]
        public float EvictionDiscardFraction { get; set; } = 0;

        /// <summary>
        /// Configures whether to use full eviction sort logic
        /// </summary>
        [DataMember]
        public bool UseFullEvictionSort { get; set; } = false;

        [DataMember]
        [Validation.Range(0, double.MaxValue)]
        public double? ThrottledEvictionIntervalMinutes { get; set; }

        /// <summary>
        /// Configures whether ages of content are updated when sorting during eviction
        /// </summary>
        [DataMember]
        public bool UpdateStaleLocalLastAccessTimes { get; set; } = false;

        /// <summary>
        /// Configures whether to use new tiered eviction logic or not.
        /// </summary>
        [DataMember]
        public bool UseTieredDistributedEviction { get; set; } = false;

        [DataMember]
        public bool PrioritizeDesignatedLocationsOnCopies { get; set; } = false;

        [DataMember]
        public bool DeprioritizeMasterOnCopies { get; set; } = false;

        [DataMember]
        [Validation.Range(0, int.MaxValue)]
        public int CopyAttemptsWithRestrictedReplicas { get; set; } = 0;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int RestrictedCopyReplicaCount { get; set; } = 3;

        [DataMember]
        [Validation.Range(0, double.MaxValue)]
        public double PeriodicCopyTracingIntervalMinutes { get; set; } = 5.0;

        /// <summary>
        /// After the first raided redis instance completes, the second instance is given a window of time to complete before the retries are cancelled.
        /// Default to always wait for both instances to complete.
        /// </summary>
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? RetryWindowSeconds { get; set; }

        /// <summary>
        /// If this variable is set we perform periodic logging to a file, indicating CaSaaS is still running and accepting operations.
        /// We then print the duration CaSaaS was just down, in the log message stating service has started.
        /// Default with the variable being null, no periodic logging will occur, and CaSaaS start log message does not include duration of last down time.
        /// </summary>
        [DataMember]
        public int? ServiceRunningLogInSeconds { get; set; }

        /// <summary>
        /// Delays for retries for file copies
        /// </summary>
        // NOTE: This must be null so that System.Text.Json serialization does not try to add to the
        // collection which will fail because its an array and add is not supported. This may be fixed in
        // newer versions of System.Text.Json. Also, changing to IReadOnlyList<int> fails DataContractSerialization
        // which is needed by QuickBuild.
        [DataMember]
        public int[] RetryIntervalForCopiesMs { get; set; }

        public IReadOnlyList<TimeSpan> RetryIntervalForCopies => (RetryIntervalForCopiesMs ?? DefaultRetryIntervalForCopiesMs).Select(ms => TimeSpan.FromMilliseconds(ms)).ToList();

        /// <summary>
        /// Controls the maximum total number of copy retry attempts
        /// </summary>
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int MaxRetryCount { get; set; } = 32;

        [DataMember]
        public bool UseUnsafeByteStringConstruction { get; set; } = false;

        [DataMember]
        public ColdStorageSettings ColdStorageSettings { get; set; }

        #region DistributedContentCopier

        [DataMember]
        [Validation.Range(0, long.MaxValue)]
        public long? GrpcCopyCompressionSizeThreshold { get; set; }

        [DataMember]
        [Validation.Enum(typeof(CopyCompression), allowNull: true)]
        public string GrpcCopyCompressionAlgorithm { get; set; }

        [DataMember]
        public bool? UseInRingMachinesForCopies { get; set; }

        [DataMember]
        public bool? StoreBuildIdInCache { get; set; }

        #endregion

        #region Grpc File Copier

        [DataMember]
        public string GrpcFileCopierGrpcCopyClientInvalidationPolicy { get; set; }

        #endregion

        #region Grpc Copy Client Cache

        [DataMember]
        [Validation.Range(0, 2)]
        public int? GrpcCopyClientCacheResourcePoolVersion { get; set; }

        /// <summary>
        /// Upper bound on number of cached GRPC clients.
        /// </summary>
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? MaxGrpcClientCount { get; set; }

        /// <summary>
        /// Maximum cached age for GRPC clients.
        /// </summary>
        [DataMember]
        [Validation.Range(1, double.MaxValue)]
        public double? MaxGrpcClientAgeMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, double.MaxValue)]
        public double? GrpcCopyClientCacheGarbageCollectionPeriodMinutes { get; set; }

        [DataMember]
        public bool? GrpcCopyClientCacheEnableInstanceInvalidation { get; set; }

        #endregion

        #region Grpc Copy Client

        [DataMember]
        [Validation.Range(0, int.MaxValue)]
        public int? GrpcCopyClientBufferSizeBytes { get; set; }

        [DataMember]
        public bool? GrpcCopyClientConnectOnStartup { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue)]
        public double? TimeToFirstByteTimeoutInSeconds { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue)]
        public double? GrpcCopyClientDisconnectionTimeoutSeconds { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue)]
        public double? GrpcCopyClientConnectionTimeoutSeconds { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue)]
        public double? GrpcCopyClientOperationDeadlineSeconds { get; set; }

        [DataMember]
        public bool? GrpcCopyClientPropagateCallingMachineName { get; set; }

        /// <remarks>
        /// It is OK to embed serializable types with no DataContract inside
        /// </remarks>
        [DataMember]
        public GrpcCoreClientOptions GrpcCopyClientGrpcCoreClientOptions { get; set; }

        #endregion

        #region Distributed Eviction

        /// <summary>
        /// When set to true, we will shut down the quota keeper before hibernating sessions to prevent a race condition of evicting pinned content
        /// </summary>
        [DataMember]
        public bool ShutdownEvictionBeforeHibernation { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? ReplicaCreditInMinutes { get; set; } = 180;

        #endregion

        #region Bandwidth Check
        [DataMember]
        public bool IsBandwidthCheckEnabled { get; set; } = true;

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? MinimumSpeedInMbPerSec { get; set; } = null;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int BandwidthCheckIntervalSeconds { get; set; } = 60;

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double MaxBandwidthLimit { get; set; } = double.MaxValue;

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double BandwidthLimitMultiplier { get; set; } = 1;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int HistoricalBandwidthRecordsStored { get; set; } = 64;

        [DataMember]
        public BandwidthConfiguration[] BandwidthConfigurations { get; set; }
        #endregion

        #region Pin Configuration
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? PinMinUnverifiedCount { get; set; }

        /// <summary>
        /// Obsolete: will be removed in favor of AsyncCopyOnPinThreshold.
        /// </summary>
        [DataMember]
        public int? StartCopyWhenPinMinUnverifiedCountThreshold { get; set; }

        [DataMember]
        public int? AsyncCopyOnPinThreshold { get; set; }

        [DataMember]
        [Validation.Range(0, 1, minInclusive: false, maxInclusive: false)]
        public double? MachineRisk { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? MaxIOOperations { get; set; }
        #endregion

        #region Local Location Store

        #region Distributed Central Storage

        [DataMember]
        public bool UseDistributedCentralStorage { get; set; } = false;

        [DataMember]
        public bool UseSelfCheckSettingsForDistributedCentralStorage { get; set; } = false;

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double MaxCentralStorageRetentionGb { get; set; } = 25;

        [DataMember]
        [Validation.Range(0, int.MaxValue)]
        public int CentralStoragePropagationDelaySeconds { get; set; } = 5;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int CentralStoragePropagationIterations { get; set; } = 3;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int CentralStorageMaxSimultaneousCopies { get; set; } = 10;

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? DistributedCentralStoragePeerToPeerCopyTimeoutSeconds { get; set; } = null;

        [DataMember]
        public bool ProactiveCopyCheckpointFiles { get; set; } = false;

        [DataMember]
        public bool InlineCheckpointProactiveCopies { get; set; } = false;

        #endregion

        [DataMember]
        public bool IsMasterEligible { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int ReconciliationCycleFrequencyMinutes { get; set; } = 30;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int ReconciliationMaxCycleSize { get; set; } = 100_000;

        [DataMember]
        public int? ReconciliationMaxRemoveHashesCycleSize { get; set; } = null;

        [DataMember]
        [Validation.Range(0, 1)]
        public double? ReconciliationMaxRemoveHashesAddPercentage { get; set; } = null;

        [DataMember]
        [Validation.Enum(typeof(ReconciliationMode))]
        public string ReconcileMode { get; set; } = ReconciliationMode.Once.ToString();

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int ReconciliationAddLimit { get; set; } = 100_000;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int ReconciliationRemoveLimit { get; set; } = 1_000_000;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? ReconcileHashesLogLimit { get; set; }

        [DataMember]
        public bool IsContentLocationDatabaseEnabled { get; set; } = false;

        [DataMember]
        public bool IsMachineReputationEnabled { get; set; } = true;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? IncrementalCheckpointDegreeOfParallelism { get; set; }

        [DataMember]
        public bool? ShouldFilterInactiveMachinesInLocalLocationStore { get; set; }

        #region Content Location Database

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? ContentLocationDatabaseGcIntervalMinutes { get; set; }

        [DataMember]
        public bool ContentLocationDatabaseLogsBackupEnabled { get; set; }

        [DataMember]
        public bool? ContentLocationDatabaseOpenReadOnly { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? ContentLocationDatabaseLogsBackupRetentionMinutes { get; set; }

        [DataMember]
        public string ContentLocationDatabaseCompression { get; set; }

        [DataMember]
        [Validation.Range(1, long.MaxValue)]
        public long? ContentLocationDatabaseEnumerateSortedKeysFromStorageBufferSize { get; set; }

        [DataMember]
        [Validation.Range(1, long.MaxValue)]
        public long? ContentLocationDatabaseEnumerateEntriesWithSortedKeysFromStorageBufferSize { get; set; }

        [DataMember]
        public bool? ContentLocationDatabaseGarbageCollectionConcurrent { get; set; }

        [DataMember]
        public string ContentLocationDatabaseMetadataGarbageCollectionStrategy { get; set; }

        [DataMember]
        [Validation.Range(1, double.MaxValue)]
        public double? ContentLocationDatabaseMetadataGarbageCollectionMaximumSizeMb { get; set; }

        [DataMember]
        public bool? ContentLocationDatabaseUseReadOptionsWithSetTotalOrderSeekInDbEnumeration { get; set; }

        [DataMember]
        public bool? ContentLocationDatabaseUseReadOptionsWithSetTotalOrderSeekInGarbageCollection { get; set; }

        [DataMember]
        public bool? ContentLocationDatabaseMetadataGarbageCollectionLogEnabled { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? MaximumNumberOfMetadataEntriesToStore { get; set; }

        #endregion

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int SecretsRetrievalRetryCount { get; set; } = 5;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int SecretsRetrievalMinBackoffSeconds { get; set; } = 10;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int SecretsRetrievalMaxBackoffSeconds { get; set; } = 60;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int SecretsRetrievalDeltaBackoffSeconds { get; set; } = 10;

        /// <summary>
        /// Name of an environment variable which contains an EventHub connection string.
        /// </summary>
        [DataMember]
        public string EventHubSecretName { get; set; }

        /// <summary>
        /// This is either:
        /// * a connection string for EventHub (don't do this).
        /// * OR a URI such that:
        ///   * The URI's scheme and host define the URI for the intended Event Hub Namespace. This
        ///     resembles "sb://yourEventHub.servicebus.windows.net".
        ///   * The URI's query string contains a value for the Event Hub Name. Note that this must
        ///     be an Event Hub within the Namespace defined at the beginning of the URI.
        ///   * The URI's query string contains a value for the Managed Identity Id. Note that this
        ///     is a guid which currently appears as the "Client ID" for the managed identity.
        ///   * In all, this should resemble "sb://yourEventHub.servicebus.windows.net/name=eventHubName&identity=identityId".
        ///     Use <see cref="ManagedIdentityUriHelper"/> to construct or parse this value.
        /// </summary>
        [DataMember]
        public string EventHubConnectionString { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? MaxEventProcessingConcurrency { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? EventBatchSize { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? EventProcessingMaxQueueSize { get; set; }

        [DataMember]
        public string[] AzureStorageSecretNames { get; set; }

        [DataMember]
        public string AzureStorageSecretName { get; set; }

        [DataMember]
        public bool AzureBlobStorageUseSasTokens { get; set; } = false;

        [DataMember]
        public string EventHubEpoch { get; set; } = ".LLS_V1.2";

        [DataMember]
        public string GlobalRedisSecretName { get; set; }

        [DataMember]
        public string SecondaryGlobalRedisSecretName { get; set; }

        [DataMember]
        public bool? MirrorClusterState { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? HeartbeatIntervalMinutes { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? CreateCheckpointIntervalMinutes { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? RestoreCheckpointIntervalMinutes { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? ProactiveReplicationIntervalMinutes { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? RestoreCheckpointTimeoutMinutes { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? UpdateClusterStateIntervalSeconds { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? SafeToLazilyUpdateMachineCountThreshold { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? CentralStorageOperationTimeoutInMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? LocationEntryExpiryMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? RestoreCheckpointAgeThresholdMinutes { get; set; }

        [DataMember]
        public bool? PacemakerEnabled { get; set; }

        [DataMember]
        public uint? PacemakerNumberOfBuckets { get; set; }

        [DataMember]
        public bool? PacemakerUseRandomIdentifier { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? TouchFrequencyMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? ReconcileCacheLifetimeMinutes { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue)]
        public double? MaxProcessingDelayToReconcileMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, double.MaxValue)]
        public double? MachineStateRecomputeIntervalMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, double.MaxValue)]
        public double? MachineActiveToClosedIntervalMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, double.MaxValue)]
        public double? MachineActiveToExpiredIntervalMinutes { get; set; }

        // Files smaller than this will use the untrusted hash
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int TrustedHashFileSizeBoundary = 100000;

        [DataMember]
        [Validation.Range(-1, long.MaxValue)]
        public long ParallelHashingFileSizeBoundary { get; set; } = -1;

        [DataMember]
        public bool UseRedundantPutFileShortcut { get; set; } = false;

        /// <summary>
        /// Gets or sets whether to override Unix file access modes.
        /// </summary>
        [DataMember]
        public bool OverrideUnixFileAccessMode { get; set; } = false;

        [DataMember]
        public bool TraceFileSystemContentStoreDiagnosticMessages { get; set; } = false;

        [DataMember]
        public bool? UseAsynchronousFileStreamOptionByDefault { get; set; }

        [DataMember]
        public bool TraceProactiveCopy { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? SilentOperationDurationThreshold { get; set; }

        [DataMember]
        public bool? UseHierarchicalTraceIds { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? DefaultPendingOperationTracingIntervalInMinutes { get; set; }

        [DataMember]
        public bool UseFastHibernationPin { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? MaximumConcurrentPutAndPlaceFileOperations { get; set; }

        [DataMember]
        public bool? UseChannelBasedActionBlockSlimImplementation { get; set; }

        #region Metadata Storage

        [DataMember]
        public bool EnableMetadataStore { get; set; } = false;

        [DataMember]
        public bool EnableDistributedCache { get; set; } = false;

        [DataMember]
        public bool UseRedisMetadataStore { get; set; } = false;

        [DataMember]
        public bool UseMemoizationContentMetadataStore { get; set; } = false;

        [DataMember]
        public bool EnablePublishingCache { get; set; } = false;

        #endregion

        /// <summary>
        /// Gets or sets the time period between logging incremental stats
        /// </summary>
        [DataMember]
        public TimeSpan? LogIncrementalStatsInterval { get; set; }

        [DataMember]
        public string[] IncrementalStatisticsCounterNames { get; set; }

        /// <summary>
        /// Gets or sets the time period between logging machine-specific performance statistics.
        /// </summary>
        [DataMember]
        public TimeSpan? LogMachineStatsInterval { get; set; }

        [DataMember]
        public bool? Unsafe_MasterThroughputCheckMode { get; set; }

        [DataMember]
        public DateTime? Unsafe_EventHubCursorPosition { get; set; }

        [DataMember]
        public bool? Unsafe_IgnoreEpoch { get; set; }

        [DataMember]
        public bool? TraceServiceGrpcOperations { get; set; }

        #endregion

        #region Proactive Copy / Replication

        /// <summary>
        /// Valid values: Disabled, InsideRing, OutsideRing, Both (See ProactiveCopyMode enum)
        /// </summary>
        [DataMember]
        [Validation.Enum(typeof(ProactiveCopyMode))]
        public string ProactiveCopyMode { get; set; } = "Disabled";

        [DataMember]
        public bool PushProactiveCopies { get; set; } = false;

        [DataMember]
        public bool ProactiveCopyOnPut { get; set; } = true;

        [DataMember]
        public bool ProactiveCopyOnPin { get; set; } = false;

        [DataMember]
        public bool ProactiveCopyUsePreferredLocations { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int ProactiveCopyLocationsThreshold { get; set; } = 3;

        [DataMember]
        public bool ProactiveCopyRejectOldContent { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int ProactiveReplicationCopyLimit { get; set; } = 5;

        [DataMember]
        public bool EnableProactiveReplication { get; set; } = false;

        [DataMember]
        [Validation.Range(0, double.MaxValue)]
        public double ProactiveReplicationDelaySeconds { get; set; } = 30;

        [DataMember]
        [Validation.Range(0, int.MaxValue)]
        public int PreferredLocationsExpiryTimeMinutes { get; set; } = 30;

        [DataMember]
        public bool UseBinManager { get; set; } = false;

        /// <summary>
        /// Indicates whether machine locations should use universal format (i.e. uri) which
        /// allows communication across machines of different platforms
        /// </summary>
        [DataMember]
        public bool UseUniversalLocations { get; set; }

        /// <summary>
        /// Include domain name in machine location.
        /// </summary>
        [DataMember]
        public bool UseDomainName { get; set; }

        #endregion

        #region Copy Scheduler
        [DataMember]
        public string CopySchedulerType { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int MaxConcurrentCopyOperations { get; set; } = DefaultMaxConcurrentCopyOperations;

        [DataMember]
        [Validation.Enum(typeof(SemaphoreOrder))]
        public string OrderForCopies { get; set; } = SemaphoreOrder.NonDeterministic.ToString();

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int MaxConcurrentProactiveCopyOperations { get; set; } = DefaultMaxConcurrentCopyOperations;

        [DataMember]
        [Validation.Enum(typeof(SemaphoreOrder))]
        public string OrderForProactiveCopies { get; set; } = SemaphoreOrder.NonDeterministic.ToString();

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int ProactiveCopyIOGateTimeoutSeconds { get; set; } = 900;

        [DataMember]
        public PrioritizedCopySchedulerConfiguration PrioritizedCopySchedulerConfiguration { get; set; }

        #endregion

        /// <summary>
        /// The map of drive paths to alternate paths to access them
        /// </summary>
        [DataMember]
        public Dictionary<string, string> AlternateDriveMap { get; set; } = new Dictionary<string, string>();

        [DataMember]
        public bool TouchContentHashLists { get; set; }

        [DataMember]
        public bool EnableCacheActivityTracker { get; set; }

        [DataMember]
        public TimeSpan TrackingActivityWindow { get; set; } = TimeSpan.FromMinutes(1);

        [DataMember]
        public TimeSpan TrackingSnapshotPeriod { get; set; } = TimeSpan.FromSeconds(30);

        [DataMember]
        public TimeSpan TrackingReportPeriod { get; set; } = TimeSpan.FromSeconds(30);

        [DataMember]
        public bool? UseSeparateConnectionForRedisBlobs { get; set; }

        /// <summary>
        /// Indicates whether distributed content store operates in special mode where content is only consumed from other machines but
        /// not available as a content replica from which other machines can copy content.
        ///
        /// In this mode, the machine does not register itself as a part of the distributed network and thus is not
        /// discoverable as a content replica for any content on the machine. This is useful for scenarios where distributed network
        /// partially composed of machines which are short-lived and thus should not participate in the distributed network for the
        /// sake of avoid churn. The machines can still pull content from other machines by querying LLS DB/Redis and getting replicas
        /// on machines which are long-lived (and this flag is false).
        /// </summary>
        [DataMember]
        public bool? DistributedContentConsumerOnly { get; set; }

        [DataMember]
        public int PublishingConcurrencyLimit { get; set; } = 128;

        [DataMember]
        public bool GlobalCacheBackgroundRestore { get; set; }

        [DataMember]
        public bool ContentMetadataDisableDatabaseRegisterLocation { get; set; }

        [DataMember]
        public bool ContentMetadataEnableResilience { get; set; }

        [DataMember]
        public bool ContentMetadataOptimizeWrites { get; set; }

        [DataMember]
        public bool UseBlobVolatileStorage { get; set; }

        [DataMember]
        public string ContentMetadataRedisSecretName { get; set; }

        [DataMember]
        public string ContentMetadataBlobSecretName { get; set; }

        [DataMember]
        public bool ContentMetadataUseBlobCheckpointRegistry { get; set; }

        [DataMember]
        public bool ContentMetadataUseBlobCheckpointRegistryStandalone { get; set; }

        [DataMember]
        public bool UseBlobCheckpointLegacyFormat { get; set; } = true;

        [DataMember]
        public string ContentMetadataBlobCheckpointRegistryContainerName { get; set; } = "contentmetadata";

        [DataMember]
        public TimeSpanSetting ContentMetadataPersistInterval { get; set; } = TimeSpan.FromSeconds(5);

        [DataMember]
        public TimeSpanSetting ContentMetadataServerMetadataRotationInterval { get; set; } = TimeSpan.FromDays(3.5);

        [DataMember]
        public TimeSpanSetting ContentMetadataShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

        [DataMember]
        public TimeSpanSetting ContentMetadataClientConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

        [DataMember]
        public TimeSpanSetting? ContentMetadataClientOperationTimeout { get; set; }

        [DataMember]
        public TimeSpanSetting? ContentMetadataClientRetryMinimumWaitTime { get; set; }

        [DataMember]
        public TimeSpanSetting? ContentMetadataClientRetryMaximumWaitTime { get; set; }

        [DataMember]
        public TimeSpanSetting? ContentMetadataClientRetryDelta { get; set; }

        [DataMember]
        public TimeSpanSetting ContentMetadataRedisMaximumKeyLifetime { get; set; } = TimeSpan.FromMinutes(120);

        [DataMember]
        public TimeSpanSetting? ContentMetadataCheckpointMaxAge { get; set; } = null;

        [DataMember]
        public string ContentMetadataLogBlobContainerName { get; set; } = "persistenteventstorage";

        [DataMember]
        public string ContentMetadataCentralStorageContainerName { get; set; } = "contentmetadata";

        [DataMember]
        public string RedisWriteAheadKeyPrefix { get; set; } = "rwalog";

        [DataMember]
        public bool ContentMetadataBatchVolatileWrites { get; set; } = true;

        [DataMember]
        public EnumSetting<ContentMetadataStoreMode> ContentMetadataStoreMode { get; set; } = Configuration.ContentMetadataStoreMode.Redis;

        [DataMember]
        public EnumSetting<ContentMetadataStoreMode>? BlobContentMetadataStoreModeOverride { get; set; }

        [DataMember]
        public EnumSetting<ContentMetadataStoreMode>? LocationContentMetadataStoreModeOverride { get; set; }

        [DataMember]
        public EnumSetting<ContentMetadataStoreMode>? MemoizationContentMetadataStoreModeOverride { get; set; }

        [DataMember]
        public int? MetadataStoreMaxOperationConcurrency { get; set; }

        [DataMember]
        public int? MetadataStoreMaxOperationQueueLength { get; set; }

        [DataMember]
        public bool ContentMetadataBlobsEnabled { get; set; } = true;

        [DataMember]
        public TimeSpanSetting? AsyncSessionShutdownTimeout { get; set; }

        [DataMember]
        public EnumSetting<CheckpointDistributionModes> CheckpointDistributionMode { get; set; } = CheckpointDistributionModes.Legacy;

        #region Azure Blob Storage-based Checkpoint Registry

        [DataMember]
        public int? BlobCheckpointRegistryCheckpointLimit { get; set; }

        [DataMember]
        public TimeSpanSetting? BlobCheckpointRegistryGarbageCollectionTimeout { get; set; }

        [DataMember]
        public TimeSpanSetting? BlobCheckpointRegistryRegisterCheckpointTimeout { get; set; }

        [DataMember]
        public TimeSpanSetting? BlobCheckpointRegistryGetCheckpointStateTimeout { get; set; }

        [DataMember]
        public TimeSpanSetting? BlobCheckpointRegistrySlotWaitTime { get; set; }

        [DataMember]
        public int? BlobCheckpointRegistryMaxNumSlots { get; set; }

        [DataMember]
        public int? BlobCheckpointRegistryFanout { get; set; }

        #endregion

        #region Azure Blob Storage-based Master Election

        [DataMember]
        public string BlobMasterElectionFileName { get; set; }

        [DataMember]
        public TimeSpanSetting? BlobMasterElectionLeaseExpiryTime { get; set; }

        [DataMember]
        public TimeSpanSetting? BlobMasterElectionFetchCurrentMasterTimeout { get; set; }

        [DataMember]
        public TimeSpanSetting? BlobMasterElectionUpdateMasterLeaseTimeout { get; set; }

        [DataMember]
        public TimeSpanSetting? BlobMasterElectionReleaseRoleIfNecessaryTimeout { get; set; }

        [DataMember]
        public TimeSpanSetting? BlobMasterElectionStorageInteractionTimeout { get; set; }

        [DataMember]
        public TimeSpanSetting? BlobMasterElectionSlotWaitTime { get; set; }

        [DataMember]
        public int? BlobMasterElectionMaxNumSlots { get; set; }

        #endregion

        #region Azure Blob Storage-based Cluster State

        [DataMember]
        public bool UseBlobClusterStateStorage { get; set; } = false;

        [DataMember]
        public string BlobClusterStateStorageFileName { get; set; }

        [DataMember]
        public TimeSpanSetting? BlobClusterStateStorageStorageInteractionTimeout { get; set; }

        [DataMember]
        public bool? BlobClusterStateStorageStandalone { get; set; }

        [DataMember]
        public TimeSpanSetting? BlobClusterStateStorageSlotWaitTime { get; set; }

        [DataMember]
        public int? BlobClusterStateStorageMaxNumSlots { get; set; }

        #endregion
    }

    public enum CheckpointDistributionModes
    {
        /// <summary>
        /// Initial mode
        ///
        /// LLS
        /// Checkpoint storage: DistributedCentralStorage
        /// Checkpoint registry: Blob
        /// Checkpoint manifest format: Key=Value (line-delimited)
        /// DistributedCentralStorage.LocationStore: LLS
        ///
        /// GCS
        /// Checkpoint storage: CachingCentralStorage
        /// Checkpoint registry: As configured (RedisWriteAheadEventStorage, AzureBlobStorageCheckpointRegistry)
        /// Checkpoint manifest format: Key=Value (line-delimited)
        /// </summary>
        Legacy,

        /// <summary>
        /// Transitional state in preparation for switching to proxy mode
        ///
        /// LLS
        /// Checkpoint storage: DistributedCentralStorage
        /// Checkpoint registry: Blob
        /// Checkpoint manifest format: Json
        /// DistributedCentralStorage.LocationStore: LLS
        ///
        /// GCS
        /// Checkpoint storage: CachingCentralStorage
        /// Checkpoint registry: Transitional if prior state was RedisWriteAheadEventStorage
        /// Checkpoint manifest format: Json
        /// </summary>
        Transitional,

        /// <summary>
        /// Mode where locations for checkpoints are centrally registry in blob storage and copy chain forms a tree.
        ///
        /// LLS
        /// Checkpoint storage: DistributedCentralStorage
        /// Checkpoint registry: Blob
        /// Checkpoint manifest format: Json
        /// DistributedCentralStorage.LocationStore: LLS
        ///
        /// GCS
        /// Checkpoint storage: CachingCentralStorage
        /// Checkpoint registry: Blob
        /// Checkpoint manifest format: Json
        /// </summary>
        Proxy,
    }

    /// <nodoc />
    public class BandwidthConfiguration
    {
        /// <summary>
        /// Whether to invalidate Grpc Copy Client in case of an error.
        /// </summary>
        public bool? InvalidateOnTimeoutError { get; set; }

        /// <summary>
        /// Gets an optional connection timeout that can be used to reject the copy more aggressively during early copy attempts.
        /// </summary>
        public double? ConnectionTimeoutInSeconds { get; set; }

        /// <summary>
        /// The interval between the copy progress is checked.
        /// </summary>
        public double IntervalInSeconds { get; set; }

        /// <summary>
        /// The number of required bytes that should be copied within a given interval. Otherwise the copy would be canceled.
        /// </summary>
        public long RequiredBytes { get; set; }

        /// <summary>
        /// If true, the server will return an error response immediately if the number of pending copy operations crosses a threshold.
        /// </summary>
        public bool FailFastIfServerIsBusy { get; set; }
    }
}
