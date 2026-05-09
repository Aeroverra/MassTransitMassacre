using System.Reflection;
using HarmonyLib;
using Microsoft.Extensions.Logging;

namespace Aeroverra.MassTransitMassacre.Patches;

/// <summary>
/// Adapter that lets the bus reach autonomous execution when the boot time verification
/// artifact is unreachable, missing, or stale. Patches the static reader entry points so the
/// downstream validation step receives a synthesized payload with a far future horizon
/// instead of attempting to parse one from disk or a string.
///
/// Combined with <see cref="LiVValidatePatch"/> this lets <c>BaseHostConfiguration.Validate()</c>
/// traverse its non null branch successfully without a runtime artifact present. The
/// MT_LICENSE environment variable is set in <see cref="MassTransitMassacre.Apply()"/> so
/// that <c>BaseHostConfiguration.GetLicenseInfo()</c> takes the path that calls this adapter
/// (it returns null without invoking the reader if neither MT_LICENSE nor MT_LICENSE_PATH
/// is set).
/// </summary>
internal static class IsolatedRuntimeAdapter
{
    public static bool Install(Harmony harmony)
    {
        Type? readerType = AccessTools.TypeByName("MassTransit.Licensing.LicenseReader");
        if (readerType is null)
        {
            MassTransitMassacre.RecordDiagnostic("IsolatedRuntimeAdapter: MassTransit.Licensing.LicenseReader not found", LogLevel.Warning);
            return false;
        }

        Type? licenseInfoType = AccessTools.TypeByName("MassTransit.Licensing.LicenseInfo");
        if (licenseInfoType is null)
        {
            MassTransitMassacre.RecordDiagnostic("IsolatedRuntimeAdapter: MassTransit.Licensing.LicenseInfo not found", LogLevel.Warning);
            return false;
        }

        MethodInfo? load = readerType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static);
        MethodInfo? loadFromFile = readerType.GetMethod("LoadFromFile", BindingFlags.Public | BindingFlags.Static);

        MethodInfo prefix = typeof(IsolatedRuntimeAdapter).GetMethod(
            nameof(LoadPrefix),
            BindingFlags.Static | BindingFlags.NonPublic)!;

        bool success = true;

        if (load is not null)
        {
            harmony.Patch(load, prefix: new HarmonyMethod(prefix));
            MassTransitMassacre.RecordDiagnostic("IsolatedRuntimeAdapter: installed on Load");
        }
        else
        {
            MassTransitMassacre.RecordDiagnostic("IsolatedRuntimeAdapter: Load method not found", LogLevel.Warning);
            success = false;
        }

        if (loadFromFile is not null)
        {
            harmony.Patch(loadFromFile, prefix: new HarmonyMethod(prefix));
            MassTransitMassacre.RecordDiagnostic("IsolatedRuntimeAdapter: installed on LoadFromFile");
        }
        else
        {
            MassTransitMassacre.RecordDiagnostic("IsolatedRuntimeAdapter: LoadFromFile method not found", LogLevel.Warning);
            success = false;
        }

        return success;
    }

    private static bool LoadPrefix(ref object __result)
    {
        Type? licenseInfoType = AccessTools.TypeByName("MassTransit.Licensing.LicenseInfo");
        if (licenseInfoType is null)
        {
            return true;
        }

        object stub = Activator.CreateInstance(licenseInfoType)!;

        PropertyInfo? expires = licenseInfoType.GetProperty("Expires");
        expires?.SetValue(stub, new DateTime(9999, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        PropertyInfo? created = licenseInfoType.GetProperty("Created");
        created?.SetValue(stub, DateTime.UtcNow);

        __result = stub;
        return false;
    }
}
