using FleetOps.Kernel.Authorization;
using Xunit;

namespace FleetOps.Tests;

public sealed class VehiclePermissionTests
{
    [Fact]
    public void Vehicles_manage_cells_match_section_15()
    {
        var entry = PermissionMatrix.Entries.Single(x => x.ActionKey == "vehicles.manage");
        Assert.Equal(PermissionMatrix.Cell.None, entry.Cells[RoleCode.Driver]);
        Assert.Equal(PermissionMatrix.Cell.Full, entry.Cells[RoleCode.Dispatcher]);
        Assert.Equal(PermissionMatrix.Cell.View, entry.Cells[RoleCode.Hr]);
        Assert.Equal(PermissionMatrix.Cell.Full, entry.Cells[RoleCode.Owner]);
    }
}
