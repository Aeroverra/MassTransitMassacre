using System.Reflection;
using HarmonyLib;
using Microsoft.Extensions.Logging;

namespace Aeroverra.MassTransitMassacre.Patches;

/// <summary>
/// Patches <c>UsageTracker.CloudEnvironmentDetector.Detect()</c> to return a completed Task with a
/// null result. This is downstream of the Enabled-getter patch; it exists as a final safety net
/// in case a future version invokes the IMDS prober from a path that bypasses the Enabled gate.
/// </summary>
internal static class CloudEnvironmentDetectorPatch
{
    public static void Install(Harmony harmony)
    {
        Type? trackerType = AccessTools.TypeByName("MassTransit.UsageTracking.UsageTracker");
        if (trackerType is null)
        {
            MassTransitMassacre.RecordDiagnostic("CloudEnvironmentDetectorPatch: outer UsageTracker type not found", LogLevel.Warning);
            return;
        }

        Type? detectorType = AccessTools.Inner(trackerType, "CloudEnvironmentDetector");
        if (detectorType is null)
        {
            MassTransitMassacre.RecordDiagnostic("CloudEnvironmentDetectorPatch: nested CloudEnvironmentDetector type not found", LogLevel.Warning);
            return;
        }

        MethodInfo? detectMethod = AccessTools.Method(detectorType, "Detect");
        if (detectMethod is null)
        {
            MassTransitMassacre.RecordDiagnostic("CloudEnvironmentDetectorPatch: Detect method not found", LogLevel.Warning);
            return;
        }

        MethodInfo prefix = typeof(CloudEnvironmentDetectorPatch).GetMethod(
            nameof(DetectPrefix),
            BindingFlags.Static | BindingFlags.NonPublic)!;

        harmony.Patch(detectMethod, prefix: new HarmonyMethod(prefix));
        MassTransitMassacre.RecordDiagnostic("CloudEnvironmentDetectorPatch: installed");
    }

    private static bool DetectPrefix(ref object __result, MethodInfo __originalMethod)
    {
        Type returnType = __originalMethod.ReturnType;
        Type? resultType = returnType.IsGenericType ? returnType.GetGenericArguments()[0] : null;
        if (resultType is null)
        {
            return true;
        }

        MethodInfo? fromResult = typeof(Task)
            .GetMethod(nameof(Task.FromResult), BindingFlags.Public | BindingFlags.Static)?
            .MakeGenericMethod(resultType);

        if (fromResult is null)
        {
            return true;
        }

        object? nullResult = resultType.IsValueType
            ? Activator.CreateInstance(resultType)
            : null;

        __result = fromResult.Invoke(null, new[] { nullResult })!;
        return false;
    }
}
