// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Native.Processes;
using BuildXL.Processes.Containers;
using BuildXL.Processes.Sideband;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using CanBeNullAttribute = JetBrains.Annotations.CanBeNullAttribute;
using static BuildXL.Utilities.BuildParameters;
using BuildXL.Processes.Remoting;

namespace BuildXL.Processes
{
    /// <summary>
    /// Data-structure that holds all information needed to launch a sandboxed process.
    /// </summary>
    public sealed class SandboxedProcessInfo
    {
        private const int BufSize = 4096;

        /// <summary>
        /// OS-imposed limit on command-line length
        /// </summary>
        /// <remarks>
        /// Windows:
        ///   http://msdn.microsoft.com/en-us/library/windows/desktop/ms682425(v=vs.85).aspx
        ///   The maximum length of this string is 32,768 characters, including the Unicode terminating null character.
        ///
        /// MacOS:
        ///   256K (can be obtained by executing `getconf ARG_MAX`)
        ///
        /// Linux:
        ///   typically 2M (can be obtained by executing `xargs --show-limits`)
        /// </remarks>
        public static readonly int MaxCommandLineLength =
            OperatingSystemHelper.IsLinuxOS ? 2 * 1024 * 1024 :
            OperatingSystemHelper.IsMacOS   ?      256 * 1024 : short.MaxValue;

        /// <summary>
        /// See <see cref="NumRetriesPipeReadOnCancel"/> for details. Having a high number of retry count has shown to mitigate the issue.
        /// </summary>
#if NET_COREAPP_60
        public const int DefaultPipeReadRetryOnCancellationCount = 10_000;
#else
        public const int DefaultPipeReadRetryOnCancellationCount = 0;
#endif

        /// <summary>
        /// Make sure we always wait for a moment after the main process exits by default.
        /// </summary>
        /// <remarks>
        /// We observed that e.g. cmd.exe spawns conhost.exe which tends to outlive cmd.exe for a brief moment (under ideal conditions).
        /// Across OS versions and under heavy load, experience shows one needs to be fairly generous.
        /// </remarks>
        public static readonly TimeSpan DefaultNestedProcessTerminationTimeout = TimeSpan.FromSeconds(30);

        private string m_arguments;

        private string m_commandLine;

        private byte[] m_environmentBlock;

        private string m_rootMappingBlock;

        private int m_maxLengthInMemory = 16384;

        /// <summary>
        /// The logging context used to log the messages to.
        /// </summary>
        public LoggingContext LoggingContext { get; private set; }

        /// <summary>
        /// A detours event listener.
        /// </summary>
        public IDetoursEventListener DetoursEventListener { get; private set; }

        /// <summary>
        /// A macOS kernel extension connection.
        /// </summary>
        public ISandboxConnection SandboxConnection;

        /// <summary>
        /// An optional shared opaque output logger to use to record file writes under shared opaque directories as soon as they happen.
        /// </summary>
        public SidebandWriter SidebandWriter { get; }

        /// <summary>
        /// An optional file system view to report outputs as soon as they are produced
        /// </summary>
        public ISandboxFileSystemView FileSystemView { get; }

        /// <summary>
        /// Whether the process creating a <see cref="SandboxedProcess"/> gets added to a job object
        /// with limit <see cref="JOBOBJECT_LIMIT_FLAGS.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE"/>
        /// </summary>
        /// <remarks>
        /// Defaults to true. This is useful to ensure that any process started by the current process will
        /// terminate when the current process terminates. This is always the case for BuildXL since we don't want
        /// any process creation to 'leak' outside the lifespan of a build.
        /// Setting this to false implies that processes allowed to breakaway may survive the main process. This setting is
        /// used by external projects that use the sandbox as a library.
        /// </remarks>
        public bool CreateJobObjectForCurrentProcess { get; }

        /// <summary>
        /// Indicates whether resource usage of the sandboxed process tree should be measured and logged for later inspection.
        /// </summary>
        public SandboxedProcessResourceMonitoringConfig MonitoringConfig;

        /// <summary>
        /// Holds the path remapping information for a process that needs to run in a container
        /// </summary>
        public ContainerConfiguration ContainerConfiguration { get; }

        /// <summary>
        /// Enable diagnostics for this process. 
        /// This will add additional information to the SandboxedProcessResult
        /// </summary>
        internal bool DiagnosticsEnabled { get; set; }

        /// <remarks>
        /// This constructor is never used in this project, but there exist external projects that
        /// compile against this assembly and already depend on this constructor.
        /// </remarks>
        public SandboxedProcessInfo(
             [CanBeNull] ISandboxedProcessFileStorage fileStorage,
             string fileName,
             bool disableConHostSharing,
             bool testRetries = false,
             LoggingContext loggingContext = null,
             IDetoursEventListener detoursEventListener = null,
             ISandboxConnection sandboxConnection = null,
             bool createJobObjectForCurrentProcess = true,
             SandboxedProcessResourceMonitoringConfig monitoringConfig = null)
             : this(
                   new PathTable(),
                   fileStorage,
                   fileName,
                   disableConHostSharing,
                   loggingContext ?? new LoggingContext("ExternalComponent"),
                   testRetries,
                   detoursEventListener,
                   sandboxConnection,
                   createJobObjectForCurrentProcess: createJobObjectForCurrentProcess,
                   monitoringConfig: monitoringConfig)
        {
        }

        /// <summary>
        /// Creates instance
        /// </summary>
        public SandboxedProcessInfo(
            PathTable pathTable,
            [CanBeNull] ISandboxedProcessFileStorage fileStorage,
            string fileName,
            FileAccessManifest fileAccessManifest,
            bool disableConHostSharing,
            ContainerConfiguration containerConfiguration,
            LoggingContext loggingContext,
            bool testRetries = false,
            IDetoursEventListener detoursEventListener = null,
            ISandboxConnection sandboxConnection = null,
            SidebandWriter sidebandWriter = null,
            bool createJobObjectForCurrentProcess = true,
            ISandboxFileSystemView fileSystemView = null,
            SandboxedProcessResourceMonitoringConfig monitoringConfig = null)
        {
            Contract.RequiresNotNull(pathTable);
            Contract.RequiresNotNull(fileName);

            PathTable = pathTable;
            FileAccessManifest = fileAccessManifest;
            FileStorage = fileStorage;
            FileName = fileName;
            DisableConHostSharing = disableConHostSharing;

            // This should be set for testing purposes only.
            TestRetries = testRetries;

            NestedProcessTerminationTimeout = DefaultNestedProcessTerminationTimeout;
            LoggingContext = loggingContext;
            DetoursEventListener = detoursEventListener;
            SandboxConnection = sandboxConnection;
            ContainerConfiguration = containerConfiguration;
            SidebandWriter = sidebandWriter;
            CreateJobObjectForCurrentProcess = createJobObjectForCurrentProcess;
            FileSystemView = fileSystemView;
            MonitoringConfig = monitoringConfig;
        }

        /// <summary>
        /// Creates instance for test
        /// </summary>
        public SandboxedProcessInfo(
            PathTable pathTable,
            [CanBeNull] ISandboxedProcessFileStorage fileStorage,
            string fileName,
            bool disableConHostSharing,
            LoggingContext loggingContext,
            bool testRetries = false,
            IDetoursEventListener detoursEventListener = null,
            ISandboxConnection sandboxConnection = null,
            ContainerConfiguration containerConfiguration = null,
            FileAccessManifest fileAccessManifest = null,
            bool createJobObjectForCurrentProcess = true,
            SandboxedProcessResourceMonitoringConfig monitoringConfig = null
            )
            : this(
                  pathTable,
                  fileStorage,
                  fileName,
                  fileAccessManifest ?? new FileAccessManifest(pathTable),
                  disableConHostSharing,
                  containerConfiguration ?? ContainerConfiguration.DisabledIsolation,
                  loggingContext,
                  testRetries,
                  detoursEventListener,
                  sandboxConnection,
                  createJobObjectForCurrentProcess: createJobObjectForCurrentProcess,
                  monitoringConfig: monitoringConfig)
        {
            Contract.RequiresNotNull(pathTable);
            Contract.RequiresNotNull(fileName);
        }

        /// <summary>
        /// A special flag used for testing purposes only.
        /// If true it executes the retry process execution logic.
        /// </summary>
        public bool TestRetries { get; }

        /// <summary>
        /// The path table.
        /// </summary>
        public PathTable PathTable { get; }

        /// <summary>
        /// Allow-list of allowed file accesses, and general file access reporting flags
        /// </summary>
        public FileAccessManifest FileAccessManifest { get; }

        /// <summary>
        /// Optional file storage options for stdout and stderr output streams.
        /// </summary>
        public ISandboxedProcessFileStorage FileStorage { get; }

        /// <summary>
        /// Access to disableConHostSharing
        /// </summary>
        public bool DisableConHostSharing { get; }

        /// <summary>
        /// Name of executable (pure path name; no command-line encoding)
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// How to decode the standard input; if not set, encoding of current process is used
        /// </summary>
        public Encoding StandardInputEncoding { get; set; }

        /// <summary>
        /// How to decode the standard output; if not set, encoding of current process is used
        /// </summary>
        public Encoding StandardErrorEncoding { get; set; }

        /// <summary>
        /// How to decode the standard output; if not set, encoding of current process is used
        /// </summary>
        public Encoding StandardOutputEncoding { get; set; }

        /// <summary>
        /// Number of pipe reading retries on cancellation.
        /// </summary>
        /// <remarks>
        /// BuildXL and Detours communicate with each other by establishing named pipes. In some builds,
        /// the pipe reading got canceled in the middle of the builds. It is still unknown
        /// what caused the cancellation because none of BuildXL code (except the test code) performs
        /// cancellation on pipe reading. To be more reliable, we can retry pipe reading
        /// when a cancellation happens.
        /// 
        /// When set to value &lt; 0, the pipe reading retries are unlimited.
        /// </remarks>
        public int NumRetriesPipeReadOnCancel { get; set; } = 0;

        /// <summary>
        /// Encoded command line arguments
        /// </summary>
        public string Arguments
        {
            get
            {
                return m_arguments;
            }

            set
            {
                m_arguments = value;
                m_commandLine = null;
            }
        }

        /// <summary>
        /// Working directory (can be null)
        /// </summary>
        public string WorkingDirectory { get; set; }

        /// <summary>
        /// Root jail information (can be null)
        /// </summary>
        /// <remarks>
        /// Currently implemented for Mac/Linux only using <c>chroot</c> and requires NOPASSWD sudo privileges.
        /// </remarks>
        public RootJailInfo? RootJailInfo { get; set; }

        /// <summary>
        /// Environment variables (can be null)
        /// </summary>
        public IBuildParameters EnvironmentVariables { get; set; }

        /// <summary>
        /// Root drive remappings (can be null)
        /// </summary>
        public IReadOnlyDictionary<string, string> RootMappings { get; set; }

        /// <summary>
        /// Optional standard input stream from which to read
        /// </summary>
        public TextReader StandardInputReader { get; set; }

        /// <summary>
        /// Optional observer of each output line
        /// </summary>
        public Action<string> StandardOutputObserver { get; set; }

        /// <summary>
        /// Optional observer of each output line
        /// </summary>
        public Action<string> StandardErrorObserver { get; set; }

        /// <summary>
        /// Data needed for remote execution.
        /// </summary>
        public RemoteSandboxedProcessData RemoteSandboxedProcessData { get; set; }

        /// <summary>
        /// Allowed surviving child processes.
        /// </summary>
        public string[] AllowedSurvivingChildProcessNames { get; set; }

        /// <summary>
        /// Temp folder redirection.
        /// </summary>
        public (string source, string target)[] RedirectedTempFolders { get; set; }

        /// <summary>
        /// Max. number of characters buffered in memory before output is streamed to disk
        /// </summary>
        public int MaxLengthInMemory
        {
            get
            {
                return m_maxLengthInMemory;
            }

            set
            {
                Contract.Requires(value >= 0);
                m_maxLengthInMemory = value;
            }
        }

        /// <summary>
        /// Number of bytes for output buffers
        /// </summary>
        public static int BufferSize => BufSize;

        /// <summary>
        /// File where Detours log failure message, e.g., communication failure, injection failure, etc.
        /// </summary>
        public string DetoursFailureFile { get; set; }

        /// <summary>
        /// Gets the command line, comprised of the executable file name and the arguments.
        /// </summary>
        public string GetCommandLine()
        {
            m_commandLine = m_commandLine ?? CommandLineEscaping.EscapeAsCreateProcessApplicationName(FileName) + " " + Arguments;
            return m_commandLine;
        }

        /// <summary>
        /// Gets the root mappings as a unicode block.
        /// Format: ((drive letter unicode character)(null terminated target path string))* (null character)
        /// </summary>
        public string GetUnicodeRootMappingBlock()
        {
            IReadOnlyDictionary<string, string> rootMappings = RootMappings;
            if (rootMappings == null)
            {
                return string.Empty;
            }

            if (m_rootMappingBlock == null)
            {
                using (PooledObjectWrapper<StringBuilder> wrap = Pools.GetStringBuilder())
                {
                    StringBuilder stringBuff = wrap.Instance;
                    foreach (var rootMapping in rootMappings)
                    {
                        stringBuff.Append(rootMapping.Key[0]);
                        stringBuff.Append(rootMapping.Value);
                        stringBuff.Append('\0');
                    }

                    // an extra null at the end indicates end of list.
                    stringBuff.Append('\0');

                    m_rootMappingBlock = stringBuff.ToString();
                }
            }

            return m_rootMappingBlock;
        }

        /// <summary>
        /// Gets the current environment variables, if any, as a unicode environment block
        /// </summary>
        public byte[] GetUnicodeEnvironmentBlock()
        {
            return m_environmentBlock ?? (m_environmentBlock = ProcessUtilities.SerializeEnvironmentBlock(EnvironmentVariables?.ToDictionary()));
        }

        /// <summary>
        /// Optional total wall clock time limit on process
        /// </summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>
        /// Wall clock time limit to wait for nested processes to exit after main process has terminated
        /// </summary>
        /// <remarks>
        /// After the time is up, we kill the corresponding job object and complain about surviving nested processes.
        /// </remarks>
        public TimeSpan NestedProcessTerminationTimeout { get; set; }

        /// <summary>
        /// Pip's semi stable hash. Used for logging.
        /// </summary>
        public long PipSemiStableHash { get; set; }

        /// <summary>
        /// Root directory where timeout dumps for the process should be stored. This directory may contain other outputs
        /// for the process.
        /// </summary>
        public string TimeoutDumpDirectory { get; set; }

        /// <summary>
        /// Root directory where surviving child process dumps should be saved
        /// </summary>
        public string SurvivingPipProcessChildrenDumpDirectory { get; set; }

        /// <summary>
        /// The kind of sandboxing to use.
        /// </summary>
        public SandboxKind SandboxKind { get; set; }

        /// <summary>
        /// Pip's Description. Used for logging.
        /// </summary>
        public string PipDescription { get; set; }

        /// <summary>
        /// Standard output and error options for the sandboxed process.
        /// </summary>
        /// <remarks>
        /// This instance of <see cref="SandboxedProcessStandardFiles"/> is used as an alternative to <see cref="FileStorage"/>.
        /// </remarks>
        public SandboxedProcessStandardFiles SandboxedProcessStandardFiles { get; set; }

        /// <summary>
        /// Info about the source of standard input.
        /// </summary>
        /// <remarks>
        /// This instance of <see cref="StandardInputInfo"/> is used as a serialized version of <see cref="StandardInputReader"/>.
        /// </remarks>
        public StandardInputInfo StandardInputSourceInfo { get; set; }

        /// <summary>
        /// Observer descriptor.
        /// </summary>
        /// <remarks>
        /// This instance of <see cref="SandboxObserverDescriptor"/> is used as a serialized version of <see cref="StandardOutputObserver"/> and <see cref="StandardErrorObserver"/>.
        /// </remarks>
        public SandboxObserverDescriptor StandardObserverDescriptor { get; set; }

        /// <summary>
        /// Provenance description.
        /// </summary>
        public string Provenance => $"[Pip{PipSemiStableHash:X16} -- {PipDescription}] ";

        /// <summary>
        /// Overrides <see cref="SandboxedProcessUnix.ReportQueueProcessTimeout"/> when running tests
        /// </summary>
        public TimeSpan? ReportQueueProcessTimeoutForTests { get; internal set; }

        /// <summary>
        /// Extra data for executing a pip in an external VM.
        /// </summary>
        public ExternalVMSandboxedProcessData ExternalVMSandboxedProcessData { get; set; }

        #region Serialization

        /// <nodoc />
        public void Serialize(Stream stream)
        {
            using (var writer = new BuildXLWriter(false, stream, true, true))
            {
                writer.WriteNullableString(m_arguments);
                writer.WriteNullableString(m_commandLine);
                writer.Write(DisableConHostSharing);
                writer.WriteNullableString(FileName);
                writer.Write(StandardInputEncoding, (w, v) => w.Write(v));
                writer.Write(StandardOutputEncoding, (w, v) => w.Write(v));
                writer.Write(StandardErrorEncoding, (w, v) => w.Write(v));
                writer.WriteNullableString(WorkingDirectory);
                writer.Write(RootJailInfo, (w, v) => v.Serialize(w));
                writer.Write(
                    EnvironmentVariables,
                    (w, v) => w.WriteReadOnlyList(
                        v.ToDictionary().ToList(),
                        (w2, kvp) =>
                        {
                            w2.Write(kvp.Key);
                            w2.Write(kvp.Value);
                        }));
                writer.Write(
                    AllowedSurvivingChildProcessNames,
                    (w, v) => w.WriteReadOnlyList(v, (w2, v2) => w2.Write(v2)));
                writer.Write(MaxLengthInMemory);
                writer.Write(Timeout, (w, v) => w.Write(v));
                writer.Write(NestedProcessTerminationTimeout);
                writer.Write(PipSemiStableHash);
                writer.WriteNullableString(TimeoutDumpDirectory);
                writer.WriteNullableString(SurvivingPipProcessChildrenDumpDirectory);
                writer.Write((byte)SandboxKind);
                writer.WriteNullableString(PipDescription);

                if (SandboxedProcessStandardFiles == null)
                {
                    if (FileStorage != null)
                    {
                        SandboxedProcessStandardFiles.From(FileStorage).Serialize(writer);
                    }
                    else
                    {
                        SandboxedProcessStandardFiles.SerializeEmpty(writer);
                    }
                }
                else
                {
                    SandboxedProcessStandardFiles.Serialize(writer);
                }

                writer.Write(StandardInputSourceInfo, (w, v) => v.Serialize(w));
                writer.Write(StandardObserverDescriptor, (w, v) => v.Serialize(w));
                writer.Write(NumRetriesPipeReadOnCancel);
                writer.Write(
                    RedirectedTempFolders,
                    (w, v) => w.WriteReadOnlyList(v, (w2, v2) => { w2.Write(v2.source); w2.Write(v2.target); }));

                writer.Write(SidebandWriter, (w, v) => v.Serialize(w));
                writer.Write(CreateJobObjectForCurrentProcess);
                writer.WriteNullableString(DetoursFailureFile);
                writer.Write(RemoteSandboxedProcessData, (w, v) => v.Serialize(w));
                writer.Write(ExternalVMSandboxedProcessData, (w, v) => v.Serialize(w));

                // File access manifest should be serialized the last.
                writer.Write(FileAccessManifest, (w, v) => FileAccessManifest.Serialize(stream));
            }
        }

        /// <nodoc />
        public static SandboxedProcessInfo Deserialize(Stream stream, LoggingContext loggingContext, IDetoursEventListener detoursEventListener)
        {
            using (var reader = new BuildXLReader(false, stream, true))
            {
                string arguments = reader.ReadNullableString();
                string commandLine = reader.ReadNullableString();
                bool disableConHostSharing = reader.ReadBoolean();
                string fileName = reader.ReadNullableString();
                Encoding standardInputEncoding = reader.ReadNullable(r => r.ReadEncoding());
                Encoding standardOutputEncoding = reader.ReadNullable(r => r.ReadEncoding());
                Encoding standardErrorEncoding = reader.ReadNullable(r => r.ReadEncoding());
                string workingDirectory = reader.ReadNullableString();
                RootJailInfo? rootJailInfo = reader.ReadNullableStruct(r => BuildXL.Processes.RootJailInfo.Deserialize(r));
                IBuildParameters buildParameters = null;
                var envVars = reader.ReadNullable(r => r.ReadReadOnlyList(r2 => new KeyValuePair<string, string>(r2.ReadString(), r2.ReadString())));
                if (envVars != null)
                {
                    buildParameters = BuildParameters.GetFactory().PopulateFromDictionary(envVars.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
                }

                string[] allowedSurvivingChildNames = reader.ReadNullable(r => r.ReadReadOnlyList(r2 => r2.ReadString()))?.ToArray();
                int maxLengthInMemory = reader.ReadInt32();
                TimeSpan? timeout = reader.ReadNullableStruct(r => r.ReadTimeSpan());
                TimeSpan nestedProcessTerminationTimeout = reader.ReadTimeSpan();
                long pipSemiStableHash = reader.ReadInt64();
                string timeoutDumpDirectory = reader.ReadNullableString();
                string survivingPipProcessChildrenDumpDirectory = reader.ReadNullableString();
                SandboxKind sandboxKind = (SandboxKind)reader.ReadByte();
                string pipDescription = reader.ReadNullableString();
                SandboxedProcessStandardFiles sandboxedProcessStandardFiles = SandboxedProcessStandardFiles.Deserialize(reader);
                StandardInputInfo standardInputSourceInfo = reader.ReadNullable(r => StandardInputInfo.Deserialize(r));
                SandboxObserverDescriptor standardObserverDescriptor = reader.ReadNullable(r => SandboxObserverDescriptor.Deserialize(r));
                int numRetriesPipeReadOnCancel = reader.ReadInt32();

                (string source, string target)[] redirectedTempFolder = reader.ReadNullable(r => r.ReadReadOnlyList(r2 => (source: r2.ReadString(), target: r2.ReadString())))?.ToArray();

                var sidebandWritter = reader.ReadNullable(r => SidebandWriter.Deserialize(r));
                var createJobObjectForCurrentProcess = reader.ReadBoolean();
                var detoursFailureFile = reader.ReadNullableString();
                var remoteSandboxedProcessData = reader.ReadNullable(r => RemoteSandboxedProcessData.Deserialize(r));
                var externalVMSandboxedProcessData = reader.ReadNullable(r => ExternalVMSandboxedProcessData.Deserialize(r));

                var fam = reader.ReadNullable(r => FileAccessManifest.Deserialize(stream));

                return new SandboxedProcessInfo(
                    new PathTable(),
                    sandboxedProcessStandardFiles != null ? new StandardFileStorage(sandboxedProcessStandardFiles) : null,
                    fileName,
                    fam,
                    disableConHostSharing,
                    // TODO: serialize/deserialize container configuration.
                    containerConfiguration: ContainerConfiguration.DisabledIsolation,
                    loggingContext: loggingContext,
                    sidebandWriter: sidebandWritter,
                    detoursEventListener: detoursEventListener,
                    createJobObjectForCurrentProcess: createJobObjectForCurrentProcess)
                {
                    m_arguments = arguments,
                    m_commandLine = commandLine,
                    StandardInputEncoding = standardInputEncoding,
                    StandardOutputEncoding = standardOutputEncoding,
                    StandardErrorEncoding = standardErrorEncoding,
                    WorkingDirectory = workingDirectory,
                    RootJailInfo = rootJailInfo,
                    EnvironmentVariables = buildParameters,
                    AllowedSurvivingChildProcessNames = allowedSurvivingChildNames,
                    MaxLengthInMemory = maxLengthInMemory,
                    Timeout = timeout,
                    NestedProcessTerminationTimeout = nestedProcessTerminationTimeout,
                    PipSemiStableHash = pipSemiStableHash,
                    TimeoutDumpDirectory = timeoutDumpDirectory,
                    SurvivingPipProcessChildrenDumpDirectory = survivingPipProcessChildrenDumpDirectory,
                    SandboxKind = sandboxKind,
                    PipDescription = pipDescription,
                    SandboxedProcessStandardFiles = sandboxedProcessStandardFiles,
                    StandardInputSourceInfo = standardInputSourceInfo,
                    StandardObserverDescriptor = standardObserverDescriptor,
                    RedirectedTempFolders = redirectedTempFolder,
                    DetoursFailureFile = detoursFailureFile,
                    RemoteSandboxedProcessData = remoteSandboxedProcessData,
                    ExternalVMSandboxedProcessData = externalVMSandboxedProcessData,
                    NumRetriesPipeReadOnCancel = numRetriesPipeReadOnCancel,
                };
            }
        }

        #endregion
    }
}
