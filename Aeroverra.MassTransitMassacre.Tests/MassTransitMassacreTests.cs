using System.Reflection;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Aeroverra.MassTransitMassacre.Tests;

/// <summary>
/// Verifies that <see cref="MassTransitMassacre.Apply"/> installs all five Harmony patches
/// and that the post-Apply runtime state matches what a privacy-policy review would expect.
/// All tests share a single static Apply() since patches are global and idempotent.
///
/// Note: MassTransit auto-detects xUnit as a test environment (via stack-trace namespace
/// matching in <c>BaseHostConfiguration.IsRunningInTestEnvironment()</c>) and skips its
/// license-validation block entirely under <c>dotnet test</c>. That means the bus-startup
/// scenario does not prove our license patches work — for that we exercise <c>LiV.Validate</c>
/// and <c>LicenseReader.Load</c> directly via reflection. The telemetry patches are
/// observable through their post-Apply runtime state regardless of the test environment.
/// </summary>
public class MassTransitMassacreTests
{
    static MassTransitMassacreTests()
    {
        MassTransitMassacre.Apply();
    }

    [Fact]
    public void Apply_InstallsExpectedPatchCount()
    {
        Assert.True(MassTransitMassacre.IsApplied);
        Assert.True(
            MassTransitMassacre.InstalledPatchCount >= 6,
            $"expected at least 6 patched methods (LiV.Validate, LicenseReader.Load, " +
            $"LicenseReader.LoadFromFile, UsageTelemetryOptions.Enabled getter, " +
            $"UsageTracker.ReportUsageTelemetry, CloudEnvironmentDetector.Detect); " +
            $"got {MassTransitMassacre.InstalledPatchCount}. Diagnostics:\n  - " +
            string.Join("\n  - ", MassTransitMassacre.Diagnostics));
    }

    [Fact]
    public void Apply_DiagnosticsReportEachPatchInstalled()
    {
        IReadOnlyList<string> diagnostics = MassTransitMassacre.Diagnostics;
        string joined = string.Join("\n  - ", diagnostics);

        Assert.Contains(diagnostics, d => d.StartsWith("LiVValidatePatch: installed"));
        Assert.Contains(diagnostics, d => d.StartsWith("IsolatedRuntimeAdapter: installed on Load"));
        Assert.Contains(diagnostics, d => d.StartsWith("IsolatedRuntimeAdapter: installed on LoadFromFile"));
        Assert.Contains(diagnostics, d => d.StartsWith("UsageTrackerReportPatch: installed"));
        Assert.Contains(diagnostics, d => d.StartsWith("UsageTrackerEnabledPatch: installed"));
        Assert.Contains(diagnostics, d => d.StartsWith("CloudEnvironmentDetectorPatch: installed"));
    }

    [Fact]
    public void MassacreOptions_ThrowOnPatchFailure_DefaultsFalse()
    {
        MassacreOptions options = new();
        Assert.False(options.ThrowOnPatchFailure);
    }

    [Fact]
    public void MassacreInstallException_CarriesDiagnosticsSnapshot()
    {
        string[] sample = { "patch A: not found", "patch B: installed" };
        MassacreInstallException ex = new("test message", sample);

        Assert.Equal("test message", ex.Message);
        Assert.Equal(sample, ex.Diagnostics);
    }

    [Fact]
    public void Apply_IsIdempotent()
    {
        int before = MassTransitMassacre.InstalledPatchCount;
        MassTransitMassacre.Apply();
        MassTransitMassacre.Apply();
        Assert.Equal(before, MassTransitMassacre.InstalledPatchCount);
    }

    [Fact]
    public void Apply_SetsTelemetryDisabledEnvironmentVariable()
    {
        Assert.Equal("false", Environment.GetEnvironmentVariable("MASSTRANSIT_USAGE_TELEMETRY"));
    }

    [Fact]
    public void Apply_SetsLicenseEnvironmentVariableWhenUnset()
    {
        string? license = Environment.GetEnvironmentVariable("MT_LICENSE");
        Assert.False(string.IsNullOrEmpty(license));
    }

    [Fact]
    public void LiV_Validate_ReturnsValidResult_AfterApply()
    {
        Type livType = LocateMassTransitType("MassTransit.Licensing.LiV");

        MethodInfo validate = livType.GetMethod("Validate", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("LiV.Validate not found");

        object? lvResult = validate.Invoke(null, new object?[] { null, DateTime.UtcNow, null });
        Assert.NotNull(lvResult);

        bool isValid = (bool)lvResult!.GetType().GetProperty("IsValid")!.GetValue(lvResult)!;
        Assert.True(isValid, "LiV.Validate should report IsValid=true after Apply");
    }

    [Fact]
    public void LicenseReader_Load_ReturnsStubLicenseInfo_AfterApply()
    {
        Type readerType = LocateMassTransitType("MassTransit.Licensing.LicenseReader");

        MethodInfo load = readerType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("LicenseReader.Load not found");

        object? stub = load.Invoke(null, new object?[] { "garbage-not-a-real-license" });
        Assert.NotNull(stub);
        Assert.Equal("MassTransit.Licensing.LicenseInfo", stub!.GetType().FullName);

        DateTime expires = (DateTime)stub.GetType().GetProperty("Expires")!.GetValue(stub)!;
        Assert.True(expires.Year >= 9999, "Expires should be far-future");
    }

    [Fact]
    public void UsageTelemetryOptions_EnabledGetter_ReturnsFalse_AfterApply()
    {
        Type optionsType = LocateMassTransitType("MassTransit.UsageTelemetryOptions");

        object instance = Activator.CreateInstance(optionsType)!;

        // Try to set Enabled = true through the setter; the getter should still return false
        // because of the postfix patch.
        PropertyInfo enabled = optionsType.GetProperty("Enabled", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Enabled property not found");

        enabled.SetValue(instance, true);

        bool actual = (bool)enabled.GetValue(instance)!;
        Assert.False(actual, "Enabled getter should return false after Apply, even when setter writes true");
    }

    [Fact]
    public async Task Bus_PublishesAndConsumes_AndTelemetryStaysOff()
    {
        ProbeSignal probe = new();

        IHost host = Host.CreateDefaultBuilder()
            .ConfigureLogging(b => b.SetMinimumLevel(LogLevel.Warning))
            .ConfigureServices(services =>
            {
                services.AddSingleton(probe);
                services.AddMassTransitMassacre();
                services.AddMassTransit(x =>
                {
                    x.AddConsumer<ProbeConsumer>();
                    x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
                });
            })
            .Build();

        await host.StartAsync();

        try
        {
            IBus bus = host.Services.GetRequiredService<IBus>();
            await bus.Publish(new ProbeMessage("hello-from-massacre-test"));

            Task completedFirst = await Task.WhenAny(
                probe.Tcs.Task,
                Task.Delay(TimeSpan.FromSeconds(10)));

            Assert.True(
                ReferenceEquals(completedFirst, probe.Tcs.Task),
                "Consumer did not receive the published message within 10 seconds — bus is not running.");

            string deliveredPayload = await probe.Tcs.Task;
            Assert.Equal("hello-from-massacre-test", deliveredPayload);

            Type trackerType = LocateMassTransitType("MassTransit.UsageTracking.UsageTracker");
            Type trackerInterface = LocateMassTransitType("MassTransit.UsageTracking.IUsageTracker");

            object? tracker = host.Services.GetService(trackerInterface);
            Assert.NotNull(tracker);

            bool enabled = (bool)trackerType.GetProperty("Enabled")!.GetValue(tracker)!;
            Assert.False(enabled, "UsageTracker.Enabled should be false during a live publish/consume round-trip");

            FieldInfo reportTaskField = trackerType.GetField(
                "_reportTask",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_reportTask field not found");

            object? reportTask = reportTaskField.GetValue(tracker);
            Assert.Null(reportTask);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public void Apply_WithLogger_ForwardsDiagnosticsToLoggerInRealTime()
    {
        CapturingLogger capture = new();
        MassTransitMassacre.Apply(new MassacreOptions { Logger = capture });

        Assert.NotEmpty(capture.Entries);

        Assert.Contains(capture.Entries, e => e.Message.Contains("UsageTrackerReportPatch: installed"));
        Assert.Contains(capture.Entries, e => e.Message.Contains("UsageTrackerEnabledPatch: installed"));
        Assert.Contains(capture.Entries, e => e.Message.Contains("LiVValidatePatch: installed"));
        Assert.Contains(capture.Entries, e => e.Message.Contains("CloudEnvironmentDetectorPatch: installed"));
        Assert.Contains(capture.Entries, e => e.Message.Contains("IsolatedRuntimeAdapter: installed on Load"));
        Assert.Contains(capture.Entries, e => e.Message.Contains("IsolatedRuntimeAdapter: installed on LoadFromFile"));

        Assert.All(capture.Entries, e => Assert.Equal(LogLevel.Information, e.Level));
    }

    [Fact]
    public async Task AddMassTransitMassacre_RegistersHostedServiceThatResolvesLoggerFromDI()
    {
        CapturingLogger capture = new();

        ServiceCollection services = new();
        services.AddLogging(b => b.AddProvider(new CapturingLoggerProvider(capture)));
        services.AddMassTransitMassacre();

        await using ServiceProvider provider = services.BuildServiceProvider();

        IHostedService hostedService = provider
            .GetServices<IHostedService>()
            .Single(s => s.GetType().Name.Contains("MassacreDiagnostics"));

        await hostedService.StartAsync(CancellationToken.None);

        Assert.NotEmpty(capture.Entries);
        Assert.Contains(capture.Entries, e => e.Message.Contains("LiVValidatePatch: installed"));
        Assert.Contains(capture.Entries, e => e.Message.Contains("IsolatedRuntimeAdapter: installed on Load"));

        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void AddMassTransitMassacre_WhenEnabledFalse_DoesNotRegisterHostedService()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddMassTransitMassacre(opts => opts.Enabled = false);

        using ServiceProvider provider = services.BuildServiceProvider();

        bool anyMassacreHostedService = provider
            .GetServices<IHostedService>()
            .Any(s => s.GetType().Name.Contains("Massacre"));

        Assert.False(anyMassacreHostedService);
    }

    public sealed record ProbeMessage(string Payload);

    private sealed class ProbeSignal
    {
        public TaskCompletionSource<string> Tcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ProbeConsumer : IConsumer<ProbeMessage>
    {
        private readonly ProbeSignal _signal;

        public ProbeConsumer(ProbeSignal signal)
        {
            _signal = signal;
        }

        public Task Consume(ConsumeContext<ProbeMessage> context)
        {
            _signal.Tcs.TrySetResult(context.Message.Payload);
            return Task.CompletedTask;
        }
    }

    private static Type LocateMassTransitType(string fullName)
    {
        Type? type = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(SafeGetTypes)
            .FirstOrDefault(t => t.FullName == fullName);

        return type
            ?? throw new InvalidOperationException(
                $"Type {fullName} not found in any loaded assembly. " +
                "Apply() must run before this test.");
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly CapturingLogger _logger;

        public CapturingLoggerProvider(CapturingLogger logger)
        {
            _logger = logger;
        }

        public ILogger CreateLogger(string categoryName) => _logger;

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly List<(LogLevel Level, string Message)> _entries = new();
        private readonly object _gate = new();

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_gate)
                {
                    return _entries.ToArray();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            string formatted = formatter(state, exception);
            lock (_gate)
            {
                _entries.Add((logLevel, formatted));
            }
        }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException loadException)
        {
            return loadException.Types.Where(t => t is not null)!;
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }
}
