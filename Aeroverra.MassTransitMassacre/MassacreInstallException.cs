namespace Aeroverra.MassTransitMassacre;

/// <summary>
/// Thrown by <see cref="MassTransitMassacre.Apply()"/> when one or more patches fail to
/// install and <see cref="MassacreOptions.ThrowOnPatchFailure"/> is <c>true</c> (the default).
/// Indicates the loaded MassTransit assembly does not match the surface area Massacre was
/// built against — typically because MassTransit was upgraded and a target type or method
/// was renamed or moved. Any patches that did install are unpatched before this is thrown
/// so the bus is not left in a partial state.
/// </summary>
public sealed class MassacreInstallException : Exception
{
    /// <summary>
    /// Snapshot of <see cref="MassTransitMassacre.Diagnostics"/> at the time of failure.
    /// </summary>
    public IReadOnlyList<string> Diagnostics { get; }

    public MassacreInstallException(string message, IReadOnlyList<string> diagnostics)
        : base(message)
    {
        Diagnostics = diagnostics;
    }
}
