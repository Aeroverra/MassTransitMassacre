using Microsoft.Extensions.DependencyInjection;

namespace Aeroverra.MassTransitMassacre;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Apply the MassTransit Massacre runtime patches at DI configuration time. Patches install
    /// before the bus's <c>UsageTracker</c> singleton is constructed and before the bus's
    /// <c>BaseHostConfiguration.Validate</c> runs, so both the telemetry pipeline and the
    /// license validation path are neutralized when MassTransit starts.
    /// </summary>
    /// <param name="configure">
    /// Optional configuration callback. Use it to attach an <see cref="Microsoft.Extensions.Logging.ILogger"/>
    /// for real-time diagnostics:
    /// <code>
    /// services.AddMassTransitMassacre(opts =&gt; opts.Logger = loggerFactory.CreateLogger("Massacre"));
    /// </code>
    /// </param>
    public static IServiceCollection AddMassTransitMassacre(
        this IServiceCollection services,
        Action<MassacreOptions>? configure = null)
    {
        MassacreOptions options = new();
        configure?.Invoke(options);
        MassTransitMassacre.Apply(options);
        return services;
    }
}
