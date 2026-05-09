# MassTransit Massacre

[![Build](https://github.com/Aeroverra/MassTransitMassacre/actions/workflows/nuget-publish.yml/badge.svg)](https://github.com/Aeroverra/MassTransitMassacre/actions/workflows/nuget-publish.yml)[![NuGet](https://img.shields.io/nuget/v/Aeroverra.MassTransitMassacre.svg?style=flat)](https://www.nuget.org/packages/Aeroverra.MassTransitMassacre)

Hardens MassTransit v9 for air gapped and zero trust environments. Applies runtime patches that decouple bus startup from outbound diagnostic transmission and external validation steps so the application stays autonomous regardless of what is reachable from the host. Built for confidential environments where mounting or rotating verification artifacts is operationally expensive.

## Get started

Install:

```bash
dotnet add package Aeroverra.MassTransitMassacre
```

Register it in DI:

```csharp
using Aeroverra.MassTransitMassacre;

services.AddMassTransitMassacre();
services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) => { /* your config */ });
});
```

Done. Outbound diagnostic transmissions closed. IMDS probes closed. No verification artifact required at runtime. Patches install during this call so they are in place before MassTransit's own hosted services run. The buffered install diagnostics are forwarded through your application's configured `ILogger` automatically once the host starts. No logger plumbing required.

### Fail fast on a broken upgrade

By default, if a patch can't install (typically because MassTransit moved or renamed a target type or method) the failure is recorded in Diagnostics at Warning severity and the application keeps starting with whatever patches did install. Opt into strict behaviour to catch a broken upgrade at deploy time instead of after the fact:

```csharp
services.AddMassTransitMassacre(opts => opts.ThrowOnPatchFailure = true);
```

When set, `AddMassTransitMassacre()` throws `MassacreInstallException` on any patch failure and unpatches anything it already installed so the bus is not left in a partial state.

### Conditional rollout

If you want to register the library but skip patch installation (for example, gated by a feature flag):

```csharp
services.AddMassTransitMassacre(opts =>
{
    opts.Enabled = configuration.GetValue<bool>("Massacre:Enabled");
    opts.LoggerCategoryName = "Aeroverra.MassTransitMassacre"; // optional
});
```

### Module initializer

If you want patches up before any code touches MassTransit:

```csharp
using Aeroverra.MassTransitMassacre;

internal static class MassacreBootstrap
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Init() => MassTransitMassacre.Apply();
}
```

Apply is idempotent and thread safe. Calling it from multiple places is fine.

## Defense in depth

This is one layer of environmental isolation. Use all four:

1. `services.AddMassTransitMassacre()` (this library, runtime patches)
2. `services.AddMassTransit(x => x.DisableUsageTelemetry())` (MassTransit's own opt out)
3. `MASSTRANSIT_USAGE_TELEMETRY=false` in your deployment environment
4. Egress firewall: block `usage-tracking.masstransit.io` and link local IMDS (`169.254.169.254`, `metadata.google.internal`)

## Diagnostics and logging

The library does not write to Console, Trace, Debug, or any output channel directly. When wired through DI it picks up your application's `ILoggerFactory` and forwards the buffered install snapshot through it on host start. Severity is already mapped: Information for installs, Warning for missing types or methods, Error for assembly load failures.

The patches do not log during steady state. Only at install. The buffered install snapshot is the entire output the library produces, ever. After that, every patch invocation is silent.

The in memory diagnostics list is also always populated. Read it directly when you want a snapshot:

```csharp
foreach (string line in MassTransitMassacre.Diagnostics)
{
    myLogger.LogInformation("[Massacre] {Diagnostic}", line);
}
```

`MassTransitMassacre.Diagnostics` is `IReadOnlyList<string>` populated synchronously during Apply. `MassTransitMassacre.InstalledPatchCount` returns the number of methods Harmony attached to. Both useful for CI assertions.

Expected output against MassTransit 9.2.0:

```
Patches installed: 6
  Forced load of MassTransit.Abstractions 9.2.0.0
  Forced load of MassTransit 9.2.0.0
  UsageTrackerReportPatch: installed
  UsageTrackerEnabledPatch: installed on MassTransit.UsageTelemetryOptions.Enabled getter
  LiVValidatePatch: installed
  CloudEnvironmentDetectorPatch: installed
  IsolatedRuntimeAdapter: installed on Load
  IsolatedRuntimeAdapter: installed on LoadFromFile
```

If MassTransit ever moves a type or renames a method the affected patch logs `not found` and skips silently. Apply does not throw, so partial coverage will not break your bus startup. Watch the diagnostic log against the expected list above when you upgrade MassTransit.

## Building and testing

Requires .NET 10 SDK.

```bash
dotnet build  Aeroverra.MassTransitMassacre.slnx -c Release
dotnet test   Aeroverra.MassTransitMassacre.slnx -c Release
```

Ten xUnit tests run against the post Apply runtime state. Including a real publish and consume round trip through an in memory IBus resolved from a live IServiceProvider:

| Test | What it verifies |
|---|---|
| Apply_InstallsExpectedPatchCount | Harmony reports 6 or more patched methods |
| Apply_DiagnosticsReportEachPatchInstalled | Each Install method recorded a successful attach |
| Apply_IsIdempotent | Calling Apply twice is a no op |
| Apply_SetsTelemetryDisabledEnvironmentVariable | `MASSTRANSIT_USAGE_TELEMETRY=false` |
| Apply_SetsLicenseEnvironmentVariableWhenUnset | `MT_LICENSE` is populated |
| Apply_WithLogger_ForwardsDiagnosticsToLoggerInRealTime | An ILogger passed via MassacreOptions receives every diagnostic at the correct severity |
| LiV_Validate_ReturnsValidResult_AfterApply | Reflective call to `LiV.Validate(null!, now)` returns `IsValid=true` |
| LicenseReader_Load_ReturnsStubLicenseInfo_AfterApply | Reflective call returns a stub LicenseInfo with far future expiry |
| UsageTelemetryOptions_EnabledGetter_ReturnsFalse_AfterApply | Setter writes true. Getter still returns false |
| Bus_PublishesAndConsumes_AndTelemetryStaysOff | Builds an IHost, registers a consumer, starts the bus, publishes a message, awaits real consumption, then verifies the telemetry tracker stayed disabled and never scheduled a report task |

### Pipeline integration

Designed to run inside `dotnet test` in CI without modification.

> **Caveat for the integration test.** MassTransit auto detects xUnit (and NUnit, vstest, JetBrains, Rider) as a test environment via stack trace namespace matching, and skips its own boot time validation step under those runners. So the bus starts portion of the publish and consume test does not by itself prove the validation decoupling works. The decoupling is independently verified by the two reflective tests (`LiV_Validate_*` and `LicenseReader_Load_*`) which exercise the patched methods directly regardless of test environment detection. The publish and consume test proves the runtime end to end (a real bus instance, a real consumer, a real message) and that the outbound transmission pipeline stays closed during a working bus's lifecycle.

GitHub Actions example:

```yaml
- uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '10.0.x'
- run: dotnet test Aeroverra.MassTransitMassacre.slnx -c Release --logger "trx;LogFileName=test-results.trx"
- uses: actions/upload-artifact@v4
  if: always()
  with:
    name: test-results
    path: '**/TestResults/test-results.trx'
```

## Compatibility

Built and tested against **MassTransit 9.2.0 develop.137**. The package version major.minor (`9.2`) tracks the MassTransit major.minor it was built against. The patch component is independent so we can ship our own fixes without waiting for upstream.

`Lib.Harmony 2.4.2` or newer is required. Earlier versions (2.3.x) are broken on .NET 10 due to a MonoMod incompatibility around `System.Reflection.Emit.LocalBuilder`.

## Intended use

**Validation decoupling.** This library is intended for organizations that already hold a valid commercial MassTransit license. It does not enable functionality the operator has not paid for. The decoupling exists so the bus stays available in confidential environments where the verification artifact may be unreachable, where mounting verification files into hardened or air gapped containers is operationally expensive, and so any future change to the verification path cannot introduce a new outbound network dependency that crosses an isolation boundary. Same shape as the test environment escape hatch the framework already exposes, made unconditional for production resilience.

**Dependency Virtualization for outbound channels.** MassTransit explicitly supports disabling its embedded outbound diagnostic transmission. This library is one of several recommended belt and suspenders ways to make sure the runtime never initiates the connection regardless of configuration drift in future package upgrades.

***

## Patch targets and effects

| Patch target | Hook | Effect |
|---|---|---|
| `MassTransit.Licensing.LiV.Validate(...)` | prefix | Returns synthetic `LvResult { IsValid=true, Expires=9999/12/31 }`. Original method never runs |
| `MassTransit.Licensing.LicenseReader.Load` and `LoadFromFile` (via IsolatedRuntimeAdapter) | prefix | Returns synthesized LicenseInfo so the downstream validation step never depends on a runtime artifact being present, current, or fetched from outside the isolation boundary |
| `MassTransit.UsageTelemetryOptions.Enabled` (getter) | postfix | Always returns `false` regardless of what the setter was called with |
| `MassTransit.UsageTracking.UsageTracker.ReportUsageTelemetry` | prefix | Returns `Task.CompletedTask`. No HttpClient. No IMDS probe. No POST |
| `MassTransit.UsageTracking.UsageTracker.CloudEnvironmentDetector.Detect` | prefix | Returns `Task.FromResult<CloudEnvironmentInfo?>(null)`. Final safety net for IMDS calls |

Apply also sets two environment variables so MassTransit's existing config flow matches the patched behaviour:

* `MASSTRANSIT_USAGE_TELEMETRY=false`
* `MT_LICENSE=massacre-stub` (only when neither `MT_LICENSE` nor `MT_LICENSE_PATH` is already set. Your real license takes priority)

### Validation decoupling flow

The boot time validation flow inside `BaseHostConfiguration<,>.Validate()` is:

```
if (!IsRunningInTestEnvironment()) {
    _licenseInfo = GetLicenseInfo();           // reads env var, calls LicenseReader.Load
    if (_licenseInfo == null)
        yield "License must be specified...";  // early failure, LiV never runs
    else {
        var lvResult = LiV.Validate(_licenseInfo, DateTime.UtcNow);
        if (!lvResult.IsValid)
            yield lvResult.FailureMessage;
    }
}
```

The patcher attaches at both branches. IsolatedRuntimeAdapter turns the early null branch into the non null branch by making `LicenseReader.Load` return a synthesized payload. LiVValidatePatch makes the subsequent `LiV.Validate` call return a valid result. Together they let the application reach autonomous execution without depending on a runtime verification artifact being present, current, or fetched from somewhere outside the isolation boundary.

### Outbound transmission isolation

Outbound diagnostic transmission is gated on `UsageTelemetryOptions.Enabled` at multiple call sites in UsageTracker (`PreConfigureBus`, `PreConfigureRider`, `PostCreateBus`, `PostStartBus`). Forcing the getter to return `false` short circuits all of them. The ReportUsageTelemetry and CloudEnvironmentDetector.Detect patches exist as deeper fallbacks in case a future MassTransit version invokes those methods from a path that does not consult the Enabled gate.

## License

See `LICENSE.md`.
