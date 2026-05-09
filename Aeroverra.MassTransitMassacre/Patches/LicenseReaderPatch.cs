using System.Reflection;
using HarmonyLib;
using Microsoft.Extensions.Logging;

namespace Aeroverra.MassTransitMassacre.Patches;

/// <summary>
/// Patches the static <c>MassTransit.Licensing.LicenseReader.Load(string)</c> and
/// <c>LoadFromFile(string)</c> methods to return a hand-built stub <c>LicenseInfo</c>
/// instead of parsing/verifying a real license payload.
///
/// Combined with the <see cref="LiVValidatePatch"/> (which short-circuits LiV.Validate to
/// always return IsValid=true), this lets <c>BaseHostConfiguration.Validate()</c> traverse
/// its non-null branch successfully without a real license file present.
///
/// The MT_LICENSE env var is set in <see cref="MassTransitMassacre.Apply"/> so that
/// <c>BaseHostConfiguration.GetLicenseInfo()</c> takes the path that calls this patched
/// reader (it returns null without invoking the reader if neither MT_LICENSE nor
/// MT_LICENSE_PATH is set).
/// </summary>
internal static class LicenseReaderPatch
{
    public static void Install(Harmony harmony)
    {
        Type? readerType = AccessTools.TypeByName("MassTransit.Licensing.LicenseReader");
        if (readerType is null)
        {
            MassTransitMassacre.RecordDiagnostic("LicenseReaderPatch: MassTransit.Licensing.LicenseReader not found", LogLevel.Warning);
            return;
        }

        Type? licenseInfoType = AccessTools.TypeByName("MassTransit.Licensing.LicenseInfo");
        if (licenseInfoType is null)
        {
            MassTransitMassacre.RecordDiagnostic("LicenseReaderPatch: MassTransit.Licensing.LicenseInfo not found", LogLevel.Warning);
            return;
        }

        MethodInfo? load = readerType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static);
        MethodInfo? loadFromFile = readerType.GetMethod("LoadFromFile", BindingFlags.Public | BindingFlags.Static);

        MethodInfo prefix = typeof(LicenseReaderPatch).GetMethod(
            nameof(LoadPrefix),
            BindingFlags.Static | BindingFlags.NonPublic)!;

        if (load is not null)
        {
            harmony.Patch(load, prefix: new HarmonyMethod(prefix));
            MassTransitMassacre.RecordDiagnostic("LicenseReaderPatch: installed on Load");
        }
        else
        {
            MassTransitMassacre.RecordDiagnostic("LicenseReaderPatch: Load method not found", LogLevel.Warning);
        }

        if (loadFromFile is not null)
        {
            harmony.Patch(loadFromFile, prefix: new HarmonyMethod(prefix));
            MassTransitMassacre.RecordDiagnostic("LicenseReaderPatch: installed on LoadFromFile");
        }
        else
        {
            MassTransitMassacre.RecordDiagnostic("LicenseReaderPatch: LoadFromFile method not found", LogLevel.Warning);
        }
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
