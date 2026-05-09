using System.Reflection;
using HarmonyLib;
using Microsoft.Extensions.Logging;

namespace Aeroverra.MassTransitMassacre.Patches;

/// <summary>
/// Patches the <c>UsageTelemetryOptions.Enabled</c> getter to always return <c>false</c>.
///
/// All gates inside <c>UsageTracker</c> short-circuit when this is false:
///   - PreConfigureBus, PreConfigureRider, PostCreateBus, PostStartBus all bail out
///   - Telemetry field stays null, so ReportUsageTelemetry is never scheduled in the first place
///
/// Note: the type lives in <c>MassTransit.Abstractions.dll</c> under the bare <c>MassTransit</c>
/// namespace, not <c>MassTransit.UsageTelemetry</c> as references in the source decompile suggest.
/// </summary>
internal static class UsageTrackerEnabledPatch
{
    public static void Install(Harmony harmony)
    {
        Type? optionsType =
            AccessTools.TypeByName("MassTransit.UsageTelemetryOptions") ??
            AccessTools.TypeByName("MassTransit.UsageTelemetry.UsageTelemetryOptions") ??
            AccessTools.TypeByName("MassTransit.UsageTracking.UsageTelemetryOptions");

        if (optionsType is null)
        {
            MassTransitMassacre.RecordDiagnostic("UsageTrackerEnabledPatch: UsageTelemetryOptions type not found in any expected namespace", LogLevel.Warning);
            return;
        }

        PropertyInfo? enabledProperty = optionsType.GetProperty(
            "Enabled",
            BindingFlags.Public | BindingFlags.Instance);

        if (enabledProperty?.GetGetMethod() is not { } getter)
        {
            MassTransitMassacre.RecordDiagnostic($"UsageTrackerEnabledPatch: Enabled getter not found on {optionsType.FullName}", LogLevel.Warning);
            return;
        }

        MethodInfo postfix = typeof(UsageTrackerEnabledPatch).GetMethod(
            nameof(EnabledGetterPostfix),
            BindingFlags.Static | BindingFlags.NonPublic)!;

        harmony.Patch(getter, postfix: new HarmonyMethod(postfix));
        MassTransitMassacre.RecordDiagnostic($"UsageTrackerEnabledPatch: installed on {optionsType.FullName}.Enabled getter");
    }

    private static void EnabledGetterPostfix(ref bool __result)
    {
        __result = false;
    }
}
