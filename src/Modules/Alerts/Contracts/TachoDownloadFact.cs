namespace FleetOps.Modules.Alerts;

public sealed record TachoDownloadFact(bool Eligible, DateOnly? LastDownload);
