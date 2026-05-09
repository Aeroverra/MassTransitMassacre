using System.Reflection;
using Aeroverra.MassTransitMassacre.Patches;
using HarmonyLib;
using Microsoft.Extensions.Logging;

namespace Aeroverra.MassTransitMassacre;

/// <summary>
/// Runtime patcher that neutralizes MassTransit's usage-telemetry phone-home and short-circuits
/// license validation. This is a belt-and-suspenders measure intended to run in addition to:
///   - <c>builder.Services.AddMassTransit(x =&gt; x.DisableUsageTelemetry())</c>
///   - environment variable <c>MASSTRANSIT_USAGE_TELEMETRY=false</c>
///   - egress firewall rules blocking <c>usage-tracking.masstransit.io</c> and link-local IMDS
/// </summary>
public static class MassTransitMassacre
{
    private const string HarmonyId = "com.aeroverra.masstransit.massacre";
    private const string LogPrefix = "[Massacre]";

    private static Harmony? _harmony;
    private static readonly object _gate = new();
    private static readonly List<string> _diagnostics = new();
    private static ILogger? _logger;

    /// <summary>
    /// Whether <see cref="Apply(MassacreOptions?)"/> has run successfully and patches are installed.
    /// </summary>
    public static bool IsApplied { get; private set; }

    /// <summary>
    /// Per-patch installation log. Useful for debugging when a patch fails to attach.
    /// </summary>
    public static IReadOnlyList<string> Diagnostics
    {
        get
        {
            lock (_diagnostics)
            {
                return _diagnostics.ToArray();
            }
        }
    }

    /// <summary>
    /// Number of patches successfully installed. Useful for assertions in test projects.
    /// </summary>
    public static int InstalledPatchCount
    {
        get
        {
            if (_harmony is null)
            {
                return 0;
            }
            return _harmony.GetPatchedMethods().Count();
        }
    }

    internal static void RecordDiagnostic(string message, LogLevel level = LogLevel.Information)
    {
        lock (_diagnostics)
        {
            _diagnostics.Add(message);
        }

        ILogger? logger = _logger;
        if (logger is not null)
        {
            logger.Log(level, "{Prefix} {Diagnostic}", LogPrefix, message);
        }
    }

    /// <summary>
    /// Apply Harmony patches with no logger attached. Equivalent to
    /// <c>Apply(options: null)</c>. Idempotent and thread-safe.
    /// </summary>
    public static void Apply()
    {
        Apply(options: null);
    }

    /// <summary>
    /// Apply Harmony patches with the supplied options. Idempotent for patch installation —
    /// calling twice is a no-op for the patches themselves. The logger, however, can be
    /// attached at any time: if patches are already installed and a new logger is supplied,
    /// the buffered diagnostic history is replayed through it so a late-attached observer
    /// sees the full installation log.
    /// </summary>
    public static void Apply(MassacreOptions? options)
    {
        lock (_gate)
        {
            ILogger? newLogger = options?.Logger;
            if (newLogger is not null && !ReferenceEquals(newLogger, _logger))
            {
                _logger = newLogger;

                // Replay buffered diagnostics so a logger attached after Apply() already
                // ran still sees the full history.
                string[] buffered;
                lock (_diagnostics)
                {
                    buffered = _diagnostics.ToArray();
                }
                foreach (string line in buffered)
                {
                    newLogger.Log(LogLevel.Information, "{Prefix} {Diagnostic}", LogPrefix, line);
                }
            }

            if (IsApplied)
            {
                return;
            }

            Environment.SetEnvironmentVariable("MASSTRANSIT_USAGE_TELEMETRY", "false");

            // Set MT_LICENSE to a placeholder so BaseHostConfiguration.GetLicenseInfo()
            // routes through LicenseReader.Load (which we patch) instead of returning null
            // and tripping the early "License must be specified" failure.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MT_LICENSE")) &&
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MT_LICENSE_PATH")))
            {
                Environment.SetEnvironmentVariable("MT_LICENSE", "massacre-stub");
            }

            // Force-load both MassTransit assemblies. AccessTools.TypeByName only finds types
            // in already-loaded assemblies; a 'using MassTransit;' directive does not trigger
            // a load on its own. Touching a known public type from each assembly forces it.
            // Note: MassTransit.IBus lives in MassTransit.Abstractions; LiV and UsageTracker
            // live in MassTransit.dll proper, so we need a type from that assembly too.
            Type abstractionsAnchor = typeof(global::MassTransit.IBus);
            RecordDiagnostic($"Forced load of {abstractionsAnchor.Assembly.GetName().Name} {abstractionsAnchor.Assembly.GetName().Version}");

            try
            {
                Assembly coreAssembly = Assembly.Load("MassTransit");
                RecordDiagnostic($"Forced load of {coreAssembly.GetName().Name} {coreAssembly.GetName().Version}");
            }
            catch (Exception loadFailure)
            {
                RecordDiagnostic(
                    $"Failed to load MassTransit core assembly: {loadFailure.GetType().Name}: {loadFailure.Message}",
                    LogLevel.Error);
            }

            _harmony = new Harmony(HarmonyId);

            UsageTrackerReportPatch.Install(_harmony);
            UsageTrackerEnabledPatch.Install(_harmony);
            LiVValidatePatch.Install(_harmony);
            CloudEnvironmentDetectorPatch.Install(_harmony);
            LicenseReaderPatch.Install(_harmony);

            IsApplied = true;
        }
    }
}
