namespace FleetOps.Kernel.Authorization;

public static class PermissionMatrix
{
    public enum Cell { None, Own, Mask, View, Full }
    public sealed record Entry(string ActionKey, IReadOnlyDictionary<RoleCode, Cell> Cells,
        SeparationOfDuty RequiredSeparation = SeparationOfDuty.None, bool RequiresStatePrecondition = false)
    {
        public IReadOnlySet<RoleCode> AllowedRoles => Cells.Where(x => x.Value is Cell.Own or Cell.Full).Select(x => x.Key).ToHashSet();
        public IReadOnlySet<RoleCode> OwnRoles => Cells.Where(x => x.Value == Cell.Own).Select(x => x.Key).ToHashSet();
        public IReadOnlySet<RoleCode> ReadableRoles => Cells.Where(x => x.Value is not Cell.None).Select(x => x.Key).ToHashSet();
    }
    public sealed record Resolution(Entry Entry, RoleCode Role, Cell Cell);

    public static Resolution? Resolve(string actionKey, IEnumerable<RoleCode> roles)
    {
        var entry = Entries.SingleOrDefault(x => x.ActionKey == actionKey);
        if (entry is null) return null;
        var resolved = entry.Cells
            .Where(x => roles.Contains(x.Key))
            .OrderByDescending(x => Strength(x.Value))
            .Select(x => (KeyValuePair<RoleCode, Cell>?)x)
            .FirstOrDefault();
        return resolved is null
            ? null
            : new Resolution(entry, resolved.Value.Key, resolved.Value.Value);
    }

    private static int Strength(Cell cell) => cell switch
    {
        Cell.Full => 4,
        Cell.View => 3,
        Cell.Mask => 2,
        Cell.Own => 1,
        _ => 0
    };
    private static Entry E(string key, Cell driver, Cell dispatcher, Cell hr, Cell owner,
        SeparationOfDuty separation = SeparationOfDuty.None, bool state = false) =>
        new(key, new Dictionary<RoleCode, Cell> { [RoleCode.Driver] = driver, [RoleCode.Dispatcher] = dispatcher, [RoleCode.Hr] = hr, [RoleCode.Owner] = owner }, separation, state);
    public static IReadOnlyList<Entry> Entries { get; } =
    [
        E("shifts.enter_edit", Cell.Own, Cell.Full, Cell.Full, Cell.Full, state: true), E("shifts.approve_reject", Cell.None, Cell.Full, Cell.Full, Cell.Full, SeparationOfDuty.OwnerRoutesToHr),
        E("shifts.reopen_exported", Cell.None, Cell.None, Cell.Full, Cell.Full), E("payroll.export", Cell.None, Cell.None, Cell.Full, Cell.Full),
        E("employees.create_edit", Cell.None, Cell.None, Cell.Full, Cell.Full), E("employees.view", Cell.Own, Cell.Mask, Cell.Full, Cell.Full),
        E("documents.driver.upload", Cell.Own, Cell.None, Cell.Full, Cell.View), E("documents.driver.verify", Cell.None, Cell.None, Cell.Full, Cell.View),
        E("documents.driver.delete", Cell.None, Cell.None, Cell.Full, Cell.None), E("documents.medical.view", Cell.Own, Cell.Mask, Cell.Full, Cell.Full),
        E("documents.vehicle.manage", Cell.None, Cell.Full, Cell.View, Cell.View), E("vehicles.manage", Cell.None, Cell.Full, Cell.View, Cell.Full), E("vehicles.view", Cell.None, Cell.Full, Cell.View, Cell.View), E("fleet.manage", Cell.None, Cell.Full, Cell.View, Cell.View), E("defects.report", Cell.Own, Cell.None, Cell.None, Cell.None),
        E("vehicles.retire.undo", Cell.None, Cell.None, Cell.None, Cell.Full),
        E("schedule.view", Cell.Own, Cell.Full, Cell.View, Cell.Full), E("schedule.request_change", Cell.Own, Cell.Full, Cell.None, Cell.Full),
        E("schedule.edit", Cell.None, Cell.Full, Cell.View, Cell.Full),
        E("override.manage", Cell.None, Cell.Full, Cell.None, Cell.Full), E("absences.vacation.request", Cell.Own, Cell.None, Cell.Full, Cell.View),
        E("absences.sick_other.record", Cell.None, Cell.Full, Cell.Full, Cell.View), E("absences.vacation.decide", Cell.None, Cell.None, Cell.Full, Cell.View),
        E("settings.users", Cell.None, Cell.None, Cell.Full, Cell.Full), E("settings.purge", Cell.None, Cell.None, Cell.Full, Cell.Full),
        E("settings.manage", Cell.None, Cell.None, Cell.None, Cell.Full), E("settings.payroll", Cell.None, Cell.None, Cell.Full, Cell.Full), E("sessions.manage", Cell.None, Cell.Full, Cell.Full, Cell.Full),
        E("employees.phone.change", Cell.None, Cell.None, Cell.Full, Cell.Full), E("audit.export", Cell.None, Cell.None, Cell.Full, Cell.Full),
        E("notifications.mine", Cell.Own, Cell.Own, Cell.Own, Cell.Own)
    ];
}
