using System.Reflection;
using HarmonyLib;
using Microsoft.Extensions.Logging;

namespace Aeroverra.MassTransitMassacre.Patches;

/// <summary>
/// Patches <c>MassTransit.UsageTracking.UsageTracker.ReportUsageTelemetry()</c> with a prefix that
/// returns <see cref="Task.CompletedTask"/> without ever instantiating an <c>HttpClient</c>,
/// probing IMDS, or POSTing to <c>usage-tracking.masstransit.io</c>.
/// </summary>
internal static class UsageTrackerReportPatch
{
    public static bool Install(Harmony harmony)
    {
        Type? trackerType = AccessTools.TypeByName("MassTransit.UsageTracking.UsageTracker");
        if (trackerType is null)
        {
            MassTransitMassacre.RecordDiagnostic("UsageTrackerReportPatch: type MassTransit.UsageTracking.UsageTracker not found", LogLevel.Warning);
            return false;
        }

        MethodInfo? reportMethod = AccessTools.Method(trackerType, "ReportUsageTelemetry");
        if (reportMethod is null)
        {
            MassTransitMassacre.RecordDiagnostic("UsageTrackerReportPatch: method ReportUsageTelemetry not found", LogLevel.Warning);
            return false;
        }

        MethodInfo prefix = typeof(UsageTrackerReportPatch).GetMethod(
            nameof(ReportUsageTelemetryPrefix),
            BindingFlags.Static | BindingFlags.NonPublic)!;

        harmony.Patch(reportMethod, prefix: new HarmonyMethod(prefix));
        MassTransitMassacre.RecordDiagnostic("UsageTrackerReportPatch: installed");
        return true;
    }

    private static bool ReportUsageTelemetryPrefix(ref Task __result)
    {
        __result = Task.CompletedTask;
        return false;
    }
}
