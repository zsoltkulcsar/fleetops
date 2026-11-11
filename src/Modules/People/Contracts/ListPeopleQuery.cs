using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

[QueryPolicy("people.view")]
public sealed record ListPeopleQuery(int Page = 1, int PageSize = 50, string? Role = null, string? Status = null,
    bool BlockedOnly = false, bool ExpiringWithin30 = false, string? Search = null) : IQuery<PeoplePage>;
