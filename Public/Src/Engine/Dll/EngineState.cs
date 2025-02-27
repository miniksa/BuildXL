// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Engine.Tracing;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Scheduler;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Qualifier;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Engine
{
    /// <summary>
    /// This immutable object carries the state of the engine between BuildXL client sessions
    /// </summary>
    public sealed class EngineState : IDisposable
    {
        private readonly Guid m_graphId;

        /// <summary>
        /// String table
        /// </summary>
        public StringTable StringTable
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_stringTable;
            }
        }

        private readonly StringTable m_stringTable;

        /// <summary>
        /// Path table
        /// </summary>
        public PathTable PathTable
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_pathTable;
            }
        }

        private readonly PathTable m_pathTable;

        /// <summary>
        /// Symbol table
        /// </summary>
        public SymbolTable SymbolTable
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_symbolTable;
            }
        }

        private readonly SymbolTable m_symbolTable;

        /// <summary>
        /// Qualifier table
        /// </summary>
        public QualifierTable QualifierTable
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_qualifierTable;
            }
        }

        private readonly QualifierTable m_qualifierTable;

        private readonly HistoricTableSizes m_historicTableSizes;

        /// <summary>
        /// Historic table sizes
        /// </summary>
        public HistoricTableSizes HistoricTableSizes
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_historicTableSizes;
            }
        }

        /// <summary>
        /// Pip table
        /// </summary>
        public PipTable PipTable
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_pipTable;
            }
        }

        private readonly PipTable m_pipTable;

        /// <summary>
        /// Pip graph
        /// </summary>
        public PipGraph PipGraph
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_pipGraph;
            }
        }

        private readonly PipGraph m_pipGraph;

        /// <summary>
        /// Mount path expander
        /// </summary>
        public MountPathExpander MountPathExpander
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_mountPathExpander;
            }
        }

        private readonly MountPathExpander m_mountPathExpander;

        /// <summary>
        /// Scheduler state
        /// </summary>
        public SchedulerState SchedulerState
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_schedulerState;
            }
        }

        private readonly SchedulerState m_schedulerState;

        /// <summary>
        /// File content table
        /// </summary>
        public FileContentTable FileContentTable
        {
            get
            {
                Contract.Requires(!IsDisposed);
                return m_fileContentTable;
            }
        }

        private readonly FileContentTable m_fileContentTable;

        /// <summary>
        /// Whether this instance got disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        private EngineState(
            Guid graphId,
            StringTable stringTable,
            PathTable pathTable,
            SymbolTable symbolTable,
            QualifierTable qualifierTable,
            PipTable pipTable,
            PipGraph pipGraph,
            MountPathExpander mountPathExpander,
            SchedulerState schedulerState,
            HistoricTableSizes historicTableSizes,
            FileContentTable fileContentTable)
        {
            Contract.Requires(graphId != default(Guid), "GraphId is not unique enough to be represented in EngineState");
            Contract.Requires(stringTable != null);
            Contract.Requires(pathTable != null);
            Contract.Requires(symbolTable != null);
            Contract.Requires(qualifierTable != null);
            Contract.Requires(stringTable == pathTable.StringTable);
            Contract.Requires(pathTable.StringTable == symbolTable.StringTable);
            Contract.Requires(pathTable.StringTable == qualifierTable.StringTable);
            Contract.Requires(pipTable != null);
            Contract.Requires(!pipTable.IsDisposed);
            Contract.Requires(pipGraph != null);
            Contract.Requires(mountPathExpander != null);
            Contract.Requires(schedulerState != null);
            Contract.Requires(historicTableSizes != null);
            Contract.Requires(fileContentTable != null);

            m_stringTable = stringTable;
            m_pathTable = pathTable;
            m_symbolTable = symbolTable;
            m_qualifierTable = qualifierTable;
            m_pipTable = pipTable;
            m_pipGraph = pipGraph;
            m_mountPathExpander = mountPathExpander;
            m_schedulerState = schedulerState;
            m_graphId = graphId;
            m_historicTableSizes = historicTableSizes;
            m_fileContentTable = fileContentTable;
        }

        private EngineState(bool disposed)
        {
            IsDisposed = disposed;
        }

        /// <summary>
        /// Creates a new <see cref="EngineState"/>  with given engine schedule and root filter
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public static EngineState CreateNew(EngineSchedule engineSchedule)
        {
            Contract.Requires(engineSchedule != null);

            var schedulerState = new SchedulerState(engineSchedule.Scheduler);

            return new EngineState(
                engineSchedule.Scheduler.PipGraph.GraphId,
                engineSchedule.Context.StringTable,
                engineSchedule.Context.PathTable,
                engineSchedule.Context.SymbolTable,
                engineSchedule.Context.QualifierTable,
                engineSchedule.PipTable,
                engineSchedule.Scheduler.PipGraph,
                engineSchedule.MountPathExpander,
                schedulerState,
                engineSchedule.Context.HistoricTableSizes,
                engineSchedule.FileContentTable);
        }

        /// <summary>
        /// Used only for testing
        /// </summary>
        internal static EngineState CreateDummy(bool disposed)
        {
            return new EngineState(disposed);
        }

        /// <summary>
        /// Returns a new EngineState updating the SchedulerState with new RootFilter and FilterPassingNodes
        /// The current instance becomes unusable after this call.
        /// </summary>
        /// <returns>The updated engine state </returns>
        public EngineState WithUpdatedSchedulerState(Scheduler.Scheduler scheduler)
        {
            Contract.Requires(!IsDisposed);
            m_schedulerState?.Dispose();

            IsDisposed = true;
            return new EngineState(
                m_graphId,
                m_stringTable,
                m_pathTable,
                m_symbolTable,
                m_qualifierTable,
                m_pipTable,
                m_pipGraph,
                m_mountPathExpander,
                new SchedulerState(scheduler),
                m_historicTableSizes,
                m_fileContentTable);
        }

        /// <summary>
        /// Returns a new EngineState updating the FileContentTable
        /// The current instance becomes unusable after this call.
        /// </summary>
        /// <returns>The updated engine state </returns>
        public EngineState WithUpdatedFileContentTable(FileContentTable fileContentTable)
        {
            Contract.Requires(!IsDisposed);

            IsDisposed = true;
            return new EngineState(
                m_graphId,
                m_stringTable,
                m_pathTable,
                m_symbolTable,
                m_qualifierTable,
                m_pipTable,
                m_pipGraph,
                m_mountPathExpander,
                m_schedulerState,
                m_historicTableSizes,
                fileContentTable);
        }

        /// <summary>
        /// Returns a new EngineState preserving the state except for the replaced FileContentTable.
        /// The current EngineState becomes unusable after this call.
        /// </summary>
        public EngineState WithFileContentTable(FileContentTable fileContentTable)
        {
            Contract.Requires(!IsDisposed);
            
            IsDisposed = true;
            return new EngineState(
                m_graphId,
                m_stringTable,
                m_pathTable,
                m_symbolTable,
                m_qualifierTable,
                m_pipTable,
                m_pipGraph,
                m_mountPathExpander,
                m_schedulerState,
                m_historicTableSizes,
                fileContentTable);
        }

        /// <summary>
        /// Dispose the <see cref="EngineState"/>
        /// </summary>
        public void Dispose()
        {
            IsDisposed = true;
            m_pipTable?.Dispose();
            m_schedulerState?.Dispose();
        }

        internal CachedGraphLoader TryLoad(
            LoggingContext loggingContext,
            CancellationToken cancellationToken,
            Guid graphIdOfGraphToReload)
        {
            if (m_graphId != graphIdOfGraphToReload)
            {
                Logger.Log.DisposedEngineStateDueToGraphId(loggingContext);
                Dispose();
                return null;
            }

            var graphLoader = CachedGraphLoader.CreateFromEngineState(cancellationToken, this);
            Contract.Assert(graphLoader != null);

            // The graph is loaded from the engine state.
            Logger.Log.ReusedEngineState(loggingContext);

            return graphLoader;
        }

        /// <summary>
        /// Checks if an instance of <see cref="EngineState"/> is usable.
        /// </summary>
        public static bool IsUsable(EngineState engineState) => engineState != null && !engineState.IsDisposed;

        /// <summary>
        /// Returns true for correct transition from previous <see cref="EngineState"/> to new <see cref="EngineState"/> after the engine runs.
        /// </summary>
        public static bool CorrectEngineStateTransition(EngineState previousEngineState, EngineState newEngineState, out string incorrectMessage)
        {
            // Correct engine state transitions have the following rules:
            // 1. If the previous engine state is usable, then it becomes the new engine state.
            // 2. If the previous engine state becomes unusable, then either no new engine state is created, e.g. BuildXL engine failed, or a new usable engine state is created.
            bool result = (IsUsable(previousEngineState) && newEngineState == previousEngineState)
                          || (!IsUsable(previousEngineState) && (newEngineState == null || IsUsable(newEngineState)));

            incorrectMessage = result
                ? string.Empty
                : I($"Incorrect engine state transition, previous state: [{DebugString(previousEngineState)}], new state: [{DebugString(newEngineState)}], previous == new: {previousEngineState == newEngineState}");

            return result;
        }

        /// <summary>
        /// Gets string for debugging status of engine state.
        /// </summary>
        public static string DebugString(EngineState engineState) => "IsNull: " + (engineState == null) + ", IsUsable: " + IsUsable(engineState);
    }
}
