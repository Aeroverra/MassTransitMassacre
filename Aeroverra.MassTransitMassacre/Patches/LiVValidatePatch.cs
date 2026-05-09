using System.Reflection;
using HarmonyLib;
using Microsoft.Extensions.Logging;

namespace Aeroverra.MassTransitMassacre.Patches;

/// <summary>
/// Patches <c>MassTransit.Licensing.LiV.Validate(LicenseInfo, DateTime, DateTime?)</c> with a
/// prefix that short-circuits the original method and returns a hand-crafted valid <c>LvResult</c>.
///
/// Why: belt-and-suspenders for confidential environments. The license check is already pure
/// offline ECDsa, but bypassing it removes any conceivable code path that could be added in
/// future versions (e.g. a build that decides to phone home as part of validation). Also makes
/// the bus resilient to a missing/expired license file.
/// </summary>
internal static class LiVValidatePatch
{
    public static void Install(Harmony harmony)
    {
        Type? livType = AccessTools.TypeByName("MassTransit.Licensing.LiV");
        if (livType is null)
        {
            MassTransitMassacre.RecordDiagnostic("LiVValidatePatch: type MassTransit.Licensing.LiV not found", LogLevel.Warning);
            return;
        }

        MethodInfo? validateMethod = AccessTools.Method(livType, "Validate");
        if (validateMethod is null)
        {
            MassTransitMassacre.RecordDiagnostic("LiVValidatePatch: method LiV.Validate not found", LogLevel.Warning);
            return;
        }

        MethodInfo prefix = typeof(LiVValidatePatch).GetMethod(
            nameof(ValidatePrefix),
            BindingFlags.Static | BindingFlags.NonPublic)!;

        harmony.Patch(validateMethod, prefix: new HarmonyMethod(prefix));
        MassTransitMassacre.RecordDiagnostic("LiVValidatePatch: installed");
    }

    /// <summary>
    /// Harmony prefix. Returning false skips the original. We synthesize an LvResult that
    /// reports IsValid=true with a far-future expiry so any consumer-side checks stay green.
    /// </summary>
    private static bool ValidatePrefix(ref object __result)
    {
        Type? lvResultType = AccessTools.TypeByName("MassTransit.Licensing.LiV+LvResult");
        if (lvResultType is null)
        {
            return true;
        }

        DateTime farFuture = new DateTime(9999, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        ConstructorInfo? primaryCtor = lvResultType
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(c => c.GetParameters().Length == 6);

        if (primaryCtor is null)
        {
            return true;
        }

        __result = primaryCtor.Invoke(new object?[]
        {
            true,
            false,
            farFuture,
            false,
            false,
            null,
        });

        return false;
    }
}
