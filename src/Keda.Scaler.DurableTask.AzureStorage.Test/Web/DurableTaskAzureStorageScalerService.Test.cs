// Copyright © William Sugarman.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Keda.Scaler.DurableTask.AzureStorage.Accounts;
using Keda.Scaler.DurableTask.AzureStorage.Cloud;
using Keda.Scaler.DurableTask.AzureStorage.Common;
using Keda.Scaler.DurableTask.AzureStorage.TaskHub;
using Keda.Scaler.DurableTask.AzureStorage.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Keda.Scaler.DurableTask.AzureStorage.Test.Web;

public class DurableTaskAzureStorageScalerServiceTest
{
    private readonly MockEnvironment _environment = new();
    private readonly AzureStorageTaskHubClient _taskHubClient = Substitute.For<AzureStorageTaskHubClient>();
    private readonly IOrchestrationAllocator _allocator = Substitute.For<IOrchestrationAllocator>();
    private readonly DurableTaskAzureStorageScalerService _service;

    public DurableTaskAzureStorageScalerServiceTest()
    {
        IServiceProvider serviceProvider = new ServiceCollection()
            .AddSingleton(_taskHubClient)
            .AddSingleton<IProcessEnvironment>(_environment)
            .AddSingleton(_allocator)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();

        _service = new DurableTaskAzureStorageScalerService(serviceProvider);
    }

    [Fact]
    public void GivenNullServiceProvider_WhenCreatingDurableTaskAzureStorageScalerService_ThenThrowArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => new DurableTaskAzureStorageScalerService(null!));

    [Fact]
    public void GivenMissingAzureStorageTaskHubClientService_WhenCreatingDurableTaskAzureStorageScalerService_ThenThrowInvalidOperationException()
    {
        IServiceProvider serviceProvider = new ServiceCollection()
            .AddSingleton<IProcessEnvironment>(_environment)
            .AddSingleton(Substitute.For<IOrchestrationAllocator>())
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();

        _ = Assert.Throws<InvalidOperationException>(() => new DurableTaskAzureStorageScalerService(serviceProvider));
    }

    [Fact]
    public void GivenMissingProcessEnvironmentService_WhenCreatingDurableTaskAzureStorageScalerService_ThenThrowInvalidOperationException()
    {
        IServiceProvider serviceProvider = new ServiceCollection()
            .AddSingleton(Substitute.For<AzureStorageTaskHubClient>())
            .AddSingleton(Substitute.For<IOrchestrationAllocator>())
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();

        _ = Assert.Throws<InvalidOperationException>(() => new DurableTaskAzureStorageScalerService(serviceProvider));
    }

    [Fact]
    public void GivenMissingOrchestrationAllocatorService_WhenCreatingDurableTaskAzureStorageScalerService_ThenThrowInvalidOperationException()
    {
        IServiceProvider serviceProvider = new ServiceCollection()
            .AddSingleton(Substitute.For<AzureStorageTaskHubClient>())
            .AddSingleton<IProcessEnvironment>(_environment)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();

        _ = Assert.Throws<InvalidOperationException>(() => new DurableTaskAzureStorageScalerService(serviceProvider));
    }

    [Fact]
    public void GivenMissingLoggerFactoryService_WhenCreatingDurableTaskAzureStorageScalerService_ThenThrowInvalidOperationException()
    {
        IServiceProvider serviceProvider = new ServiceCollection()
            .AddSingleton(Substitute.For<AzureStorageTaskHubClient>())
            .AddSingleton<IProcessEnvironment>(_environment)
            .AddSingleton(Substitute.For<IOrchestrationAllocator>())
            .BuildServiceProvider();

        _ = Assert.Throws<InvalidOperationException>(() => new DurableTaskAzureStorageScalerService(serviceProvider));
    }

    [Fact]
    public Task GivenNullRequest_WhenGettingMetrics_ThenThrowArgumentNullException()
        => Assert.ThrowsAsync<ArgumentNullException>(() => _service.GetMetrics(null!, Substitute.For<ServerCallContext>()));

    [Fact]
    public Task GivenNullContext_WhenGettingMetrics_ThenThrowArgumentNullException()
        => Assert.ThrowsAsync<ArgumentNullException>(() => _service.GetMetrics(new GetMetricsRequest(), null!));

    [Fact]
    public Task GivenInvalidMetadata_WhenGettingMetrics_ThenThrowValidationException()
    {
        ScalerMetadata metadata = new()
        {
            AccountName = "unitteststorage",
            Cloud = nameof(CloudEnvironment.AzurePublicCloud),
            Connection = "UseDevelopmentStorage=true",
            TaskHubName = "UnitTestTaskHub",
        };

        ScaledObjectRef scaledObjectRef = CreateScaledObjectRef(metadata);

        GetMetricsRequest request = new() { ScaledObjectRef = scaledObjectRef };
        return Assert.ThrowsAsync<ValidationException>(() => _service.GetMetrics(request, Substitute.For<ServerCallContext>()));
    }

    [Fact]
    public async Task GivenValidMetadata_WhenGettingMetrics_ThenReturnMetricValues()
    {
        const int MaxActivities = 3;
        const int MaxOrchestrations = 2;
        const string TaskHubName = "UnitTestTaskHub";
        const int WorkerCount = 4;

        ScalerMetadata metadata = new()
        {
            AccountName = "unitteststorage",
            Cloud = nameof(CloudEnvironment.AzurePublicCloud),
            MaxActivitiesPerWorker = MaxActivities,
            MaxOrchestrationsPerWorker = MaxOrchestrations,
            TaskHubName = TaskHubName,
        };

        AzureStorageAccountInfo accountInfo = metadata.GetAccountInfo(_environment);

        using CancellationTokenSource tokenSource = new();
        GetMetricsRequest request = CreateGetMetricsRequest(metadata);
        MockServerCallContext context = new(tokenSource.Token);

        ITaskHubQueueMonitor monitor = Substitute.For<ITaskHubQueueMonitor>();
        TaskHubQueueUsage usage = new([1, 2, 3, 4], 1);
        IOrchestrationAllocator allocator = Substitute.For<IOrchestrationAllocator>();
        _ = _taskHubClient
            .GetMonitorAsync(Arg.Any<AzureStorageAccountInfo>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(monitor);
        _ = monitor
            .GetUsageAsync(Arg.Any<CancellationToken>())
            .Returns(usage);
        _ = allocator
            .GetWorkerCount(Arg.Any<IReadOnlyList<int>>(), Arg.Any<int>())
            .Returns(WorkerCount);

        GetMetricsResponse actual = await _service.GetMetrics(request, context);

        _ = await _taskHubClient
            .Received(1)
            .GetMonitorAsync(
                Arg.Is<AzureStorageAccountInfo>(a => AzureStorageAccountInfoEqualityComparer.Instance.Equals(a, accountInfo)),
                Arg.Is(TaskHubName),
                Arg.Is(tokenSource.Token));
        _ = await monitor
            .Received(1)
            .GetUsageAsync(Arg.Is(tokenSource.Token));
        _ = allocator
            .Received(1)
            .GetWorkerCount(
                Arg.Is<IReadOnlyList<int>>(t => t.SequenceEqual(new List<int>() { 1, 2, 3 })),
                Arg.Is(MaxOrchestrations));

        int expected = usage.WorkItemQueueMessages + (WorkerCount * MaxActivities);
        Assert.Equal(DurableTaskAzureStorageScalerService.MetricName, actual.MetricValues.Single().MetricName);
        Assert.Equal(expected, actual.MetricValues.Single().MetricValue_);
    }

    [Fact]
    public Task GivenNullScaledObjectRef_WhenGettingMetricSpec_ThenThrowArgumentNullException()
        => Assert.ThrowsAsync<ArgumentNullException>(() => _service.GetMetricSpec(null!, Substitute.For<ServerCallContext>()));

    [Fact]
    public Task GivenNullContext_WhenGettingMetricSpec_ThenThrowArgumentNullException()
        => Assert.ThrowsAsync<ArgumentNullException>(() => _service.GetMetricSpec(new ScaledObjectRef(), null!));

    [Fact]
    public Task GivenInvalidMetadata_WhenGettingMetricSpec_ThenThrowValidationException()
    {
        ScalerMetadata metadata = new()
        {
            AccountName = "unitteststorage",
            Cloud = nameof(CloudEnvironment.AzurePublicCloud),
            Connection = "UseDevelopmentStorage=true",
            TaskHubName = "UnitTestTaskHub",
        };

        ScaledObjectRef scaledObjectRef = CreateScaledObjectRef(metadata);
        return Assert.ThrowsAsync<ValidationException>(() => _service.GetMetricSpec(scaledObjectRef, Substitute.For<ServerCallContext>()));
    }

    [Fact]
    public async Task GivenValidMetadata_WhenGettingMetricSpec_ThenReturnMetricTarget()
    {
        const int MaxActivities = 3;
        const string TaskHubName = "UnitTestTaskHub";

        ScalerMetadata metadata = new()
        {
            AccountName = "unitteststorage",
            Cloud = nameof(CloudEnvironment.AzurePublicCloud),
            MaxActivitiesPerWorker = MaxActivities,
            TaskHubName = TaskHubName,
        };

        using CancellationTokenSource tokenSource = new();
        ScaledObjectRef scaledObjectRef = CreateScaledObjectRef(metadata);
        MockServerCallContext context = new(tokenSource.Token);

        GetMetricSpecResponse actual = await _service.GetMetricSpec(scaledObjectRef, context);

        Assert.Equal(DurableTaskAzureStorageScalerService.MetricName, actual.MetricSpecs.Single().MetricName);
        Assert.Equal(MaxActivities, actual.MetricSpecs.Single().TargetSize);
    }

    [Fact]
    public Task GivenNullRequest_WhenCheckingIfActive_ThenThrowArgumentNullException()
        => Assert.ThrowsAsync<ArgumentNullException>(() => _service.IsActive(null!, Substitute.For<ServerCallContext>()));

    [Fact]
    public Task GivenNullContext_WhenCheckingIfActive_ThenThrowArgumentNullException()
        => Assert.ThrowsAsync<ArgumentNullException>(() => _service.IsActive(new ScaledObjectRef(), null!));

    [Fact]
    public Task GivenInvalidMetadata_WhenCheckingIfActive_ThenThrowValidationException()
    {
        ScalerMetadata metadata = new()
        {
            AccountName = "unitteststorage",
            Cloud = nameof(CloudEnvironment.AzurePublicCloud),
            Connection = "UseDevelopmentStorage=true",
            TaskHubName = "UnitTestTaskHub",
        };

        ScaledObjectRef scaledObjectRef = CreateScaledObjectRef(metadata);
        return Assert.ThrowsAsync<ValidationException>(() => _service.IsActive(scaledObjectRef, Substitute.For<ServerCallContext>()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenValidMetadata_WhenCheckingIfActive_ThenReturnMetricValues(bool hasActivity)
    {
        const string TaskHubName = "UnitTestTaskHub";

        ScalerMetadata metadata = new()
        {
            AccountName = "unitteststorage",
            Cloud = nameof(CloudEnvironment.AzurePublicCloud),
            TaskHubName = TaskHubName,
        };

        AzureStorageAccountInfo accountInfo = metadata.GetAccountInfo(_environment);

        using CancellationTokenSource tokenSource = new();
        ScaledObjectRef scaledObjectRef = CreateScaledObjectRef(metadata);
        MockServerCallContext context = new(tokenSource.Token);

        ITaskHubQueueMonitor monitor = Substitute.For<ITaskHubQueueMonitor>();
        TaskHubQueueUsage usage = hasActivity ? new([1, 2, 3, 4], 1) : new([0, 0, 0, 0], 0);
        _ = _taskHubClient
            .GetMonitorAsync(Arg.Any<AzureStorageAccountInfo>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(monitor);
        _ = monitor
            .GetUsageAsync(Arg.Any<CancellationToken>())
            .Returns(usage);

        IsActiveResponse actual = await _service.IsActive(scaledObjectRef, context);

        _ = await _taskHubClient
            .Received(1)
            .GetMonitorAsync(
                Arg.Is<AzureStorageAccountInfo>(a => AzureStorageAccountInfoEqualityComparer.Instance.Equals(a, accountInfo)),
                Arg.Is(TaskHubName),
                Arg.Is(tokenSource.Token));
        _ = await monitor
            .Received(1)
            .GetUsageAsync(Arg.Is(tokenSource.Token));

        Assert.Equal(hasActivity, actual.Result);
    }

    private static GetMetricsRequest CreateGetMetricsRequest(ScalerMetadata metadata)
    {
        return new()
        {
            MetricName = DurableTaskAzureStorageScalerService.MetricName,
            ScaledObjectRef = CreateScaledObjectRef(metadata),
        };
    }

    private static ScaledObjectRef CreateScaledObjectRef(ScalerMetadata metadata)
    {
        ScaledObjectRef result = new()
        {
            ScalerMetadata =
            {
                { nameof(ScalerMetadata.MaxActivitiesPerWorker), metadata.MaxActivitiesPerWorker.ToString(CultureInfo.InvariantCulture) },
                { nameof(ScalerMetadata.MaxOrchestrationsPerWorker), metadata.MaxOrchestrationsPerWorker.ToString(CultureInfo.InvariantCulture) },
                { nameof(ScalerMetadata.UseManagedIdentity), metadata.UseManagedIdentity.ToString(CultureInfo.InvariantCulture) },
            }
        };

        if (metadata.AccountName is not null)
            result.ScalerMetadata[nameof(ScalerMetadata.AccountName)] = metadata.AccountName;

        if (metadata.Connection is not null)
            result.ScalerMetadata[nameof(ScalerMetadata.Connection)] = metadata.Connection;

        if (metadata.ConnectionFromEnv is not null)
            result.ScalerMetadata[nameof(ScalerMetadata.ConnectionFromEnv)] = metadata.ConnectionFromEnv;

        if (metadata.TaskHubName is not null)
            result.ScalerMetadata[nameof(ScalerMetadata.TaskHubName)] = metadata.TaskHubName;

        if (metadata.Cloud is not null)
            result.ScalerMetadata[nameof(ScalerMetadata.Cloud)] = metadata.Cloud;

        return result;
    }

    private sealed class MockServerCallContext(CancellationToken cancellationToken) : ServerCallContext
    {
        protected override CancellationToken CancellationTokenCore { get; } = cancellationToken;

        #region Unimplemented

        protected override string MethodCore => throw new NotImplementedException();

        protected override string HostCore => throw new NotImplementedException();

        protected override string PeerCore => throw new NotImplementedException();

        protected override DateTime DeadlineCore => throw new NotImplementedException();

        protected override Metadata RequestHeadersCore => throw new NotImplementedException();

        protected override Metadata ResponseTrailersCore => throw new NotImplementedException();

        protected override Status StatusCore { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        protected override WriteOptions? WriteOptionsCore { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        protected override AuthContext AuthContextCore => throw new NotImplementedException();

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotImplementedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => throw new NotImplementedException();

        #endregion
    }
}
