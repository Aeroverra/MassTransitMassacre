using Microsoft.Extensions.Logging;

namespace Aeroverra.MassTransitMassacre;

/// <summary>
/// Configuration for <see cref="ServiceCollectionExtensions.AddMassTransitMassacre"/> and
/// <see cref="MassTransitMassacre.Apply(MassacreOptions?)"/>.
/// </summary>
public sealed class MassacreOptions
{
    /// <summary>
    /// When <c>true</c> (the default) the runtime patches are installed during DI registration
    /// and a hosted service is registered to forward the buffered diagnostic snapshot through
    /// the application's configured <see cref="ILogger"/> when the host starts. Set to
    /// <c>false</c> to register the library without installing patches, useful for conditional
    /// rollouts driven by configuration.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, <see cref="MassTransitMassacre.Apply()"/> throws a
    /// <see cref="MassacreInstallException"/> if any patch fails to install (typically
    /// because MassTransit moved or renamed a target type or method). Any patches that did
    /// install are unpatched first so the bus is not left in a partial state.
    ///
    /// Default <c>false</c>: failed patches are recorded in
    /// <see cref="MassTransitMassacre.Diagnostics"/> at <c>Warning</c> severity and
    /// <c>Apply</c> returns successfully, allowing the application to continue with whatever
    /// patches did install. Set to <c>true</c> to fail fast on a broken upgrade so the
    /// problem surfaces at deploy time instead of after the fact when a queue, a license
    /// check, or telemetry quietly behaves the wrong way.
    /// </summary>
    public bool ThrowOnPatchFailure { get; set; } = false;

    /// <summary>
    /// Category name used when resolving an <see cref="ILogger"/> from the application's
    /// <see cref="ILoggerFactory"/>. Defaults to <c>Aeroverra.MassTransitMassacre</c>.
    /// </summary>
    public string LoggerCategoryName { get; set; } = "Aeroverra.MassTransitMassacre";

    /// <summary>
    /// Advanced. If set, replays buffered diagnostics through this logger and forwards future
    /// diagnostics in real time. Normally you do not need to set this — the DI extension
    /// registers a hosted service that resolves an <see cref="ILogger"/> from the framework's
    /// <see cref="ILoggerFactory"/> for you. Provided for callers wiring the library outside
    /// of <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
    /// </summary>
    public ILogger? Logger { get; set; }
}
