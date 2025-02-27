// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Host.Configuration;

#nullable enable

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    ///     Arguments for constructing the nlog logger.
    /// </summary>
    public record LoggerFactoryArguments
    {
        /// <nodoc />
        public ILogger Logger { get; internal set; }

        /// <nodoc />
        public LoggingSettings? LoggingSettings { get; }

        /// <nodoc />
        public ISecretsProvider SecretsProvider { get; }

        /// <nodoc />
        public ITelemetryFieldsProvider TelemetryFieldsProvider { get; }

        /// <nodoc />
        public LoggerFactoryArguments(
            ILogger logger,
            ISecretsProvider secretsProvider,
            LoggingSettings? loggingSettings,
            ITelemetryFieldsProvider telemetryFieldsProvider)
        {
            Logger = logger;
            LoggingSettings = loggingSettings;
            SecretsProvider = secretsProvider;
            TelemetryFieldsProvider = telemetryFieldsProvider;
        }
    }
}
