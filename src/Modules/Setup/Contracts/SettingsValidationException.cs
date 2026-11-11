namespace FleetOps.Modules.Setup;
public sealed class SettingsValidationException(IReadOnlyList<string> fields) : Exception("validation_error")
{
    public IReadOnlyList<string> Fields { get; } = fields;
}
