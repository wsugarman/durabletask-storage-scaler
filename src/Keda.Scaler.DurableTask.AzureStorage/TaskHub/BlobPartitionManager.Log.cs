// Copyright © William Sugarman.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System;

namespace Keda.Scaler.DurableTask.AzureStorage.TaskHub;

internal static partial class Log
{
    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Debug,
        Message = "Found Task Hub '{TaskHubName}' with {Partitions} partitions created at {CreatedTime:O} in blob {TaskHubBlobName}.")]
    public static partial void FoundTaskHubBlob(this ILogger logger, string taskHubName, int partitions, DateTimeOffset createdTime, string? taskHubBlobName);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "Cannot find Task Hub '{TaskHubName}' metadata blob '{TaskHubBlobName}' in container '{LeaseContainerName}'.")]
    public static partial void CannotFindTaskHubBlob(this ILogger logger, string taskHubName, string taskHubBlobName, string leaseContainerName);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Warning,
        Message = "Metadata blob '{TaskHubBlobName}' for Task Hub {TaskHubName} in container '{LeaseContainerName}' is missing required values.")]
    public static partial void ReadInvalidTaskHubBlob(this ILogger logger, string taskHubBlobName, string taskHubName, string leaseContainerName);
}
