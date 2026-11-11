using FleetOps.Kernel.Authorization;
using FleetOps.Api.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FleetOps.Tests;

public sealed class AuthorizationTests
{
    [Fact]
    public async Task Generated_cross_employee_own_cases_refuse_and_do_not_invoke_handler()
    {
        foreach (var entry in PermissionMatrix.Entries.Where(x => x.OwnRoles.Count > 0))
        foreach (var role in entry.OwnRoles)
        {
            var handler = new CountingHandler();
            var policy = new CommandPolicy(entry.ActionKey, entry.AllowedRoles, SubjectRelation.Own,
                subjectId: c => ((MatrixCommand)c).SubjectId, ownRoles: entry.OwnRoles);
            var dispatcher = CreateDispatcher(AccessContext.For(Guid.NewGuid(), role), policy, handler);
            await Assert.ThrowsAsync<AuthorizationException>(() => dispatcher.DispatchAsync<MatrixCommand, int>(new MatrixCommand(Guid.NewGuid())));
            Assert.Equal(0, handler.Count);
        }
    }

    [Fact]
    public void Default_policy_provider_resolves_matrix_and_dispatch_refuses_without_policy()
    {
        var provider = new DefaultCommandPolicyProvider();
        var policy = provider.GetPolicy(typeof(AttributedCommand));
        Assert.NotNull(policy);
        Assert.Equal(PermissionMatrix.Entries.Single(x => x.ActionKey == "notifications.mine").AllowedRoles, policy!.AllowedRoles);
        Assert.Null(provider.GetPolicy(typeof(MatrixCommand)));
    }

    [Fact]
    public void Default_policy_provider_builds_own_and_separation_policies_and_null_refuses()
    {
        var provider = new DefaultCommandPolicyProvider();
        var own = provider.GetPolicy(typeof(OwnAttributedCommand));
        var separated = provider.GetPolicy(typeof(SeparatedAttributedCommand));
        Assert.Equal(SubjectRelation.Own, own!.SubjectRelation);
        Assert.NotNull(own.SubjectId);
        Assert.Equal(SeparationOfDuty.OwnerRoutesToHr, separated!.Separation);
        Assert.NotNull(separated.SubjectId);
        Assert.Null(provider.GetPolicy(typeof(MatrixCommand)));
    }

    [Fact]
    public void Database_connection_resolution_fails_closed_for_blank_primary_and_fallback()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:FleetOps"] = " ", ["FLEETOPS_DATABASE_RUNTIME_URL"] = ""
        }).Build();
        Assert.Null(DatabaseConnectionString.Resolve(configuration));
    }

    [Fact]
    public async Task Recording_transaction_observes_commit_rollback_dispose_and_preserves_handler_exception()
    {
        var recording = new RecordingTransaction { ThrowOnRollback = true };
        var handler = new ThrowingHandler();
        var dispatcher = CreateDispatcher(AccessContext.For(Guid.NewGuid(), RoleCode.Dispatcher),
            CommandPolicy.For("shifts.approve_reject", RoleCode.Dispatcher), handler, transaction: recording);
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync<MatrixCommand, int>(new MatrixCommand(null)));
        Assert.Equal(1, recording.Rollbacks); Assert.Equal(1, recording.Disposals); Assert.Equal(0, recording.Commits);
    }

    [Fact]
    public async Task Transaction_commit_is_observed_without_postgres()
    {
        var transaction = new RecordingTransaction();
        var handler = new ContextRecordingHandler();
        var dispatcher = CreateDispatcher(AccessContext.For(Guid.NewGuid(), RoleCode.Dispatcher),
            CommandPolicy.For("shifts.approve_reject", RoleCode.Dispatcher), handler, transaction: transaction);

        await dispatcher.DispatchAsync<MatrixCommand, int>(new MatrixCommand(null), TestContext.Current.CancellationToken);

        Assert.Equal(1, transaction.Commits);
        Assert.Same(transaction.Context, handler.Context);
    }

    [Fact]
    public async Task Dispatch_with_a_null_policy_throws_and_never_runs_the_handler()
    {
        var handler = new CountingHandler();
        var services = new ServiceCollection()
            .AddSingleton<IAccessContextAccessor>(new TestAccessor(AccessContext.For(Guid.NewGuid(), RoleCode.Dispatcher)))
            .AddSingleton<ICommandPolicyProvider, NullPolicyProvider>()
            .AddSingleton<ICommandTransactionFactory>(new TestTransactions(new TestTransaction()))
            .AddSingleton<ISubjectRoleLookup>(new FailClosedSubjectRoleLookup())
            .AddSingleton<ICommandHandler<MatrixCommand, int>>(handler)
            .AddLogging()
            .AddScoped<CommandDispatcher>()
            .BuildServiceProvider();

        var dispatcher = services.GetRequiredService<CommandDispatcher>();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync<MatrixCommand, int>(new MatrixCommand(null), TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task Query_dispatcher_enforces_session_visibility_and_multi_role_subject_rules()
    {
        var services = new ServiceCollection().AddSingleton<IQueryHandler<MedicalQuery, MedicalReadModel>, MedicalQueryHandler>().BuildServiceProvider();
        var noSession = new QueryDispatcher(new TestAccessor(null), services);
        await Assert.ThrowsAsync<AuthorizationException>(() => noSession.DispatchAsync<MedicalQuery, MedicalReadModel>(new MedicalQuery(Guid.NewGuid())));
        var ownId = Guid.NewGuid();
        var own = new QueryDispatcher(new TestAccessor(AccessContext.For(ownId, RoleCode.Driver, RoleCode.Dispatcher)), services);
        var result = await own.DispatchAsync<MedicalQuery, MedicalReadModel>(new MedicalQuery(ownId));
        Assert.Equal(ownId, result.EmployeeId);
        var hr = new QueryDispatcher(new TestAccessor(AccessContext.For(Guid.NewGuid(), RoleCode.Dispatcher, RoleCode.Hr)), services);
        var hrResult = await hr.DispatchAsync<MedicalQuery, MedicalReadModel>(new MedicalQuery(Guid.NewGuid()));
        Assert.NotEqual(Guid.Empty, hrResult.EmployeeId);
    }

    [Fact]
    public void Matrix_contains_all_permission_rows()
    {
        Assert.Equal(41, PermissionMatrix.Entries.Count);
        Assert.Contains(PermissionMatrix.Entries, x => x.ActionKey == "shifts.approve_reject" && x.AllowedRoles.SetEquals([RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner]));
        Assert.Contains(PermissionMatrix.Entries, x => x.ActionKey == "documents.medical.view" && x.OwnRoles.SetEquals([RoleCode.Driver]));
    }

    [Fact]
    public void Allowed_roles_exclude_view_and_mask_cells()
    {
        var expected = new Dictionary<string, RoleCode[]>
        {
            ["documents.view"] = [RoleCode.Hr, RoleCode.Owner],
            ["documents.people.lookup"] = [RoleCode.Hr, RoleCode.Owner],
            ["readiness.summary"] = [RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner],
            ["shifts.enter_edit"] = [RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner],
            ["shifts.approve_reject"] = [RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner],
            ["shifts.reopen_exported"] = [RoleCode.Hr, RoleCode.Owner],
            ["payroll.export"] = [RoleCode.Hr, RoleCode.Owner],
            ["people.manage"] = [RoleCode.Hr, RoleCode.Owner],
            ["people.view"] = [RoleCode.Hr, RoleCode.Owner],
            ["employees.create_edit"] = [RoleCode.Hr, RoleCode.Owner],
            ["employees.view"] = [RoleCode.Driver, RoleCode.Hr, RoleCode.Owner],
            ["documents.driver.upload"] = [RoleCode.Driver, RoleCode.Hr],
            ["documents.driver.verify"] = [RoleCode.Hr],
            ["documents.driver.delete"] = [RoleCode.Hr],
            ["documents.medical.view"] = [RoleCode.Driver, RoleCode.Hr, RoleCode.Owner],
            ["documents.vehicle.manage"] = [RoleCode.Dispatcher],
            ["vehicles.manage"] = [RoleCode.Dispatcher, RoleCode.Owner],
            ["vehicles.view"] = [RoleCode.Dispatcher],
            ["vehicles.retire.undo"] = [RoleCode.Owner],
            ["fleet.manage"] = [RoleCode.Dispatcher],
            ["schedule.view"] = [RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Owner],
            ["schedule.request_change"] = [RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Owner],
            ["override.manage"] = [RoleCode.Dispatcher, RoleCode.Owner],
            ["absences.vacation.request"] = [RoleCode.Driver, RoleCode.Hr],
            ["absences.sick_other.record"] = [RoleCode.Dispatcher, RoleCode.Hr],
            ["absences.vacation.decide"] = [RoleCode.Hr],
            ["settings.users"] = [RoleCode.Hr, RoleCode.Owner],
            ["settings.purge"] = [RoleCode.Hr, RoleCode.Owner],
            ["settings.manage"] = [RoleCode.Owner],
            ["sessions.manage"] = [RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner],
            ["employees.phone.change"] = [RoleCode.Hr, RoleCode.Owner],
            ["audit.export"] = [RoleCode.Hr, RoleCode.Owner],
            ["alerts.view"] = [RoleCode.Dispatcher, RoleCode.Hr],
            ["alerts.snooze"] = [RoleCode.Dispatcher, RoleCode.Hr],
            ["defects.report"] = [RoleCode.Driver],
            ["defects.manage"] = [RoleCode.Dispatcher],
            ["driver.config"] = [RoleCode.Driver],
            ["schedule.edit"] = [RoleCode.Dispatcher, RoleCode.Owner],
            ["settings.payroll"] = [RoleCode.Hr, RoleCode.Owner],
            ["notifications.mine"] = [RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner]
            , ["jobs.trigger"] = [RoleCode.Owner]
        };

        Assert.Equal(expected.Keys.Order(), PermissionMatrix.Entries.Select(x => x.ActionKey).Order());
        foreach (var entry in PermissionMatrix.Entries)
            Assert.True(entry.AllowedRoles.SetEquals(expected[entry.ActionKey]), entry.ActionKey);
    }

    [Fact]
    public void Literal_role_matrix_catches_each_cell_without_deriving_expectations()
    {
        var expected = new Dictionary<string, (PermissionMatrix.Cell driver, PermissionMatrix.Cell dispatcher, PermissionMatrix.Cell hr, PermissionMatrix.Cell owner)>
        {
            ["documents.view"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.View, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full),
            ["documents.people.lookup"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full),
            ["readiness.summary"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full),
            ["shifts.enter_edit"] = (PermissionMatrix.Cell.Own, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full),
            ["shifts.approve_reject"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full),
            ["shifts.reopen_exported"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full),
            ["payroll.export"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full),
            ["people.manage"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full),
            ["people.view"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.Mask, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full),
            ["employees.create_edit"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full),
            ["employees.view"] = (PermissionMatrix.Cell.Own, PermissionMatrix.Cell.Mask, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full),
            ["documents.driver.upload"] = (PermissionMatrix.Cell.Own, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.View),
            ["documents.driver.verify"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.View),
            ["documents.driver.delete"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.None),
            ["documents.medical.view"] = (PermissionMatrix.Cell.Own, PermissionMatrix.Cell.Mask, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full),
            ["documents.vehicle.manage"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.View, PermissionMatrix.Cell.View),
            ["vehicles.manage"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.View, PermissionMatrix.Cell.Full),
            ["vehicles.view"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.View, PermissionMatrix.Cell.View),
            ["vehicles.retire.undo"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full),
            ["fleet.manage"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.View, PermissionMatrix.Cell.View),
            ["schedule.view"] = (PermissionMatrix.Cell.Own, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.View, PermissionMatrix.Cell.Full),
            ["schedule.request_change"] = (PermissionMatrix.Cell.Own, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full),
            ["override.manage"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full),
            ["absences.vacation.request"] = (PermissionMatrix.Cell.Own, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.View),
            ["absences.sick_other.record"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.View),
            ["absences.vacation.decide"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.View),
            ["settings.users"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full),
            ["settings.purge"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full),
            ["settings.manage"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full),
            ["sessions.manage"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full),
            ["employees.phone.change"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full),
            ["audit.export"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full),
            ["defects.report"] = (PermissionMatrix.Cell.Own, PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.None),
            ["defects.manage"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.None, PermissionMatrix.Cell.View),
            ["driver.config"] = (PermissionMatrix.Cell.Own, PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.None),
            ["schedule.edit"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.View, PermissionMatrix.Cell.Full),
            ["settings.payroll"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full),
            ["notifications.mine"] = (PermissionMatrix.Cell.Own, PermissionMatrix.Cell.Own, PermissionMatrix.Cell.Own, PermissionMatrix.Cell.Own)
            , ["jobs.trigger"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full)
            , ["alerts.view"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.View)
            , ["alerts.snooze"] = (PermissionMatrix.Cell.None, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.Full, PermissionMatrix.Cell.None)
        };
        Assert.Equal(expected.Keys.Order(), PermissionMatrix.Entries.Select(x => x.ActionKey).Order());
        foreach (var item in expected)
        {
            var cells = PermissionMatrix.Entries.Single(x => x.ActionKey == item.Key).Cells;
            Assert.Equal(item.Value.driver, cells[RoleCode.Driver]); Assert.Equal(item.Value.dispatcher, cells[RoleCode.Dispatcher]);
            Assert.Equal(item.Value.hr, cells[RoleCode.Hr]); Assert.Equal(item.Value.owner, cells[RoleCode.Owner]);
        }
    }

    [Fact]
    public void Production_role_codes_are_stable()
    {
        Assert.Equal("driver", RoleCode.Driver.Code());
        Assert.Equal("dispatcher", RoleCode.Dispatcher.Code());
        Assert.Equal("hr", RoleCode.Hr.Code());
        Assert.Equal("owner", RoleCode.Owner.Code());
    }

    public static IEnumerable<object[]> MatrixCases() =>
        from entry in PermissionMatrix.Entries
        from role in Enum.GetValues<RoleCode>()
        select new object[] { entry, role };

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public async Task Every_matrix_role_and_action_uses_the_dispatchers_real_decision(PermissionMatrix.Entry entry, RoleCode role)
    {
        var employeeId = Guid.NewGuid();
        var command = new MatrixCommand(employeeId);
        var policy = new CommandPolicy(entry.ActionKey, entry.AllowedRoles, entry.OwnRoles.Count == 0 ? SubjectRelation.Any : SubjectRelation.Own,
            subjectId: c => ((MatrixCommand)c).SubjectId, ownRoles: entry.OwnRoles);
        var handler = new CountingHandler();
        var dispatcher = CreateDispatcher(AccessContext.For(employeeId, role), policy, handler);

        var expectedAllowed = HandWrittenAllowed(entry.ActionKey, role);
        if (expectedAllowed)
        {
            await dispatcher.DispatchAsync<MatrixCommand, int>(command, TestContext.Current.CancellationToken);
            Assert.Equal(1, handler.Count);
        }
        else
        {
            await Assert.ThrowsAsync<AuthorizationException>(() => dispatcher.DispatchAsync<MatrixCommand, int>(command, TestContext.Current.CancellationToken));
            Assert.Equal(0, handler.Count);
        }
    }

    private static bool HandWrittenAllowed(string actionKey, RoleCode role) => actionKey switch
    {
        "shifts.enter_edit" => role is RoleCode.Driver or RoleCode.Dispatcher or RoleCode.Hr or RoleCode.Owner,
        "shifts.approve_reject" or "sessions.manage" or "notifications.mine" => role is RoleCode.Dispatcher or RoleCode.Hr or RoleCode.Owner || (actionKey == "notifications.mine" && role == RoleCode.Driver),
        "jobs.trigger" => role == RoleCode.Owner,
        "people.manage" or "people.view" or "shifts.reopen_exported" or "payroll.export" or "employees.create_edit" or "settings.users" or "settings.purge" or "settings.payroll" or "employees.phone.change" or "audit.export" => role is RoleCode.Hr or RoleCode.Owner,
        "employees.view" or "documents.medical.view" => role is RoleCode.Driver or RoleCode.Hr or RoleCode.Owner,
        "documents.view" or "documents.people.lookup" => role is RoleCode.Hr or RoleCode.Owner,
        "readiness.summary" => role is RoleCode.Dispatcher or RoleCode.Hr or RoleCode.Owner,
        "documents.driver.upload" => role is RoleCode.Driver or RoleCode.Hr,
        "schedule.view" or "schedule.request_change" => role is RoleCode.Driver or RoleCode.Dispatcher or RoleCode.Owner,
        "absences.vacation.request" => role is RoleCode.Driver or RoleCode.Hr,
        "documents.driver.verify" or "documents.driver.delete" or "absences.vacation.decide" => role == RoleCode.Hr,
        "documents.vehicle.manage" or "fleet.manage" or "defects.manage" => role == RoleCode.Dispatcher,
        "vehicles.retire.undo" => role == RoleCode.Owner,
        "vehicles.manage" => role is RoleCode.Dispatcher or RoleCode.Owner,
        "vehicles.view" => role is RoleCode.Dispatcher,
        "schedule.edit" => role is RoleCode.Dispatcher or RoleCode.Owner,
        "override.manage" => role is RoleCode.Dispatcher or RoleCode.Owner,
        "absences.sick_other.record" => role is RoleCode.Dispatcher or RoleCode.Hr,
        "alerts.view" => role is RoleCode.Dispatcher or RoleCode.Hr,
        "alerts.snooze" => role is RoleCode.Dispatcher or RoleCode.Hr,
        "settings.manage" => role == RoleCode.Owner,
        "defects.report" => role == RoleCode.Driver,
        "driver.config" => role == RoleCode.Driver,
        _ => false
    };

    [Fact]
    public async Task Own_role_refuses_null_subject_and_full_access_role_is_not_limited()
    {
        var policy = new CommandPolicy("shifts.enter_edit", new[] { RoleCode.Driver, RoleCode.Dispatcher }.ToHashSet(), SubjectRelation.Own,
            subjectId: c => ((MatrixCommand)c).SubjectId, ownRoles: new[] { RoleCode.Driver }.ToHashSet());
        var handler = new CountingHandler();
        var dispatcher = CreateDispatcher(AccessContext.For(Guid.NewGuid(), RoleCode.Driver), policy, handler);
        await Assert.ThrowsAsync<AuthorizationException>(() => dispatcher.DispatchAsync<MatrixCommand, int>(new MatrixCommand(null), TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Count);

        var other = Guid.NewGuid();
        var fullAccess = CreateDispatcher(AccessContext.For(Guid.NewGuid(), RoleCode.Dispatcher), policy, handler);
        await fullAccess.DispatchAsync<MatrixCommand, int>(new MatrixCommand(other), TestContext.Current.CancellationToken);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task Multi_role_actor_uses_the_strongest_cell_on_another_employee_record()
    {
        var actor = Guid.NewGuid();
        var handler = new CountingHandler();
        var policy = new CommandPolicy("shifts.enter_edit", PermissionMatrix.Entries.Single(x => x.ActionKey == "shifts.enter_edit").AllowedRoles,
            SubjectRelation.Own, subjectId: c => ((MatrixCommand)c).SubjectId, ownRoles: new[] { RoleCode.Driver }.ToHashSet());
        var dispatcher = CreateDispatcher(AccessContext.For(actor, RoleCode.Driver, RoleCode.Dispatcher), policy, handler);
        await dispatcher.DispatchAsync<MatrixCommand, int>(new MatrixCommand(Guid.NewGuid()), TestContext.Current.CancellationToken);
        Assert.Equal(1, handler.Count);
        Assert.Equal(PermissionMatrix.Cell.Full, PermissionMatrix.Resolve("shifts.enter_edit", [RoleCode.Driver, RoleCode.Dispatcher])!.Cell);
    }

    [Fact]
    public async Task Separation_of_duty_refuses_owner_and_non_owner_on_their_own_record_but_allows_hr()
    {
        var ownerId = Guid.NewGuid();
        foreach (var separation in new[] { SeparationOfDuty.NotOwnSubject, SeparationOfDuty.OwnerRoutesToHr })
        {
            var policy = new CommandPolicy("shifts.approve_reject",
                new[] { RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner }.ToHashSet(),
                separation: separation,
                subjectId: c => ((MatrixCommand)c).SubjectId);
            var handler = new CountingHandler();
            var command = new MatrixCommand(ownerId);

            var nonOwner = CreateDispatcher(AccessContext.For(ownerId, RoleCode.Dispatcher), policy, handler, new OwnerLookup(ownerId));
            await Assert.ThrowsAsync<AuthorizationException>(() => nonOwner.DispatchAsync<MatrixCommand, int>(command, TestContext.Current.CancellationToken));

            var owner = CreateDispatcher(AccessContext.For(ownerId, RoleCode.Owner), policy, handler, new OwnerLookup(ownerId));
            await Assert.ThrowsAsync<AuthorizationException>(() => owner.DispatchAsync<MatrixCommand, int>(command, TestContext.Current.CancellationToken));

            var hr = CreateDispatcher(AccessContext.For(Guid.NewGuid(), RoleCode.Hr), policy, handler, new OwnerLookup(ownerId));
            await hr.DispatchAsync<MatrixCommand, int>(command, TestContext.Current.CancellationToken);

            Assert.Equal(1, handler.Count);
        }
    }

    [Fact]
    public async Task Production_fail_closed_subject_lookup_refuses_owner_route()
    {
        var ownerId = Guid.NewGuid();
        var policy = new CommandPolicy("shifts.approve_reject",
            new[] { RoleCode.Hr, RoleCode.Owner }.ToHashSet(),
            separation: SeparationOfDuty.OwnerRoutesToHr,
            subjectId: c => ((MatrixCommand)c).SubjectId);
        var handler = new CountingHandler();
        var dispatcher = CreateDispatcher(AccessContext.For(Guid.NewGuid(), RoleCode.Hr), policy, handler, new FailClosedSubjectRoleLookup());

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            dispatcher.DispatchAsync<MatrixCommand, int>(new MatrixCommand(ownerId), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Owner_route_refusal_names_hr_for_a_different_actor()
    {
        var policy = new CommandPolicy("shifts.approve_reject", new[] { RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner }.ToHashSet(),
            separation: SeparationOfDuty.OwnerRoutesToHr, subjectId: c => ((MatrixCommand)c).SubjectId);
        var dispatcher = CreateDispatcher(AccessContext.For(Guid.NewGuid(), RoleCode.Dispatcher), policy, new CountingHandler(),
            new OwnerLookup(Guid.NewGuid()));
        var refusal = await Assert.ThrowsAsync<AuthorizationException>(() =>
            dispatcher.DispatchAsync<MatrixCommand, int>(new MatrixCommand(Guid.NewGuid()), TestContext.Current.CancellationToken));
        Assert.Equal(RefusalCodes.OwnerRoutesToHr, refusal.Code);
        Assert.Equal([RoleCode.Hr], refusal.AllowedRoles);
    }

    [Fact]
    public async Task Owner_route_refusal_reaches_the_owner_clause()
    {
        var subjectId = Guid.NewGuid();
        var handler = new CountingHandler();
        var policy = new CommandPolicy("shifts.approve_reject", new[] { RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner }.ToHashSet(),
            separation: SeparationOfDuty.OwnerRoutesToHr, subjectId: c => ((MatrixCommand)c).SubjectId);
        var dispatcher = CreateDispatcher(AccessContext.For(Guid.NewGuid(), RoleCode.Dispatcher), policy, handler,
            new OwnerLookup(subjectId));

        var refusal = await Assert.ThrowsAsync<AuthorizationException>(() =>
            dispatcher.DispatchAsync<MatrixCommand, int>(new MatrixCommand(subjectId), TestContext.Current.CancellationToken));
        Assert.Equal(RefusalCodes.OwnerRoutesToHr, refusal.Code);
        Assert.Equal([RoleCode.Hr], refusal.AllowedRoles);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task Query_dispatcher_fails_closed_for_missing_policy_and_missing_visible_to()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new QueryDispatcher(new TestAccessor(AccessContext.For(Guid.NewGuid(), RoleCode.Hr)), services);
        var noPolicy = await Assert.ThrowsAsync<AuthorizationException>(() => dispatcher.DispatchAsync<NoPolicyQuery, VisibleReadModel>(new()));
        Assert.Equal(RefusalCodes.PermissionDenied, noPolicy.Code);
        var noVisibleTo = await Assert.ThrowsAsync<AuthorizationException>(() => dispatcher.DispatchAsync<PolicyQuery, UnmarkedReadModel>(new()));
        Assert.Equal(RefusalCodes.PermissionDenied, noVisibleTo.Code);
    }

    [Fact]
    public async Task Query_without_a_session_returns_not_signed_in_with_no_roles()
    {
        var services = new ServiceCollection()
            .AddSingleton<IQueryHandler<OwnerQuery, OwnerReadModel>, OwnerQueryHandler>()
            .BuildServiceProvider();
        var dispatcher = new QueryDispatcher(new TestAccessor(null), services);

        var refusal = await Assert.ThrowsAsync<AuthorizationException>(() =>
            dispatcher.DispatchAsync<OwnerQuery, OwnerReadModel>(new(), TestContext.Current.CancellationToken));
        Assert.Equal(RefusalCodes.NotSignedIn, refusal.Code);
        Assert.Empty(refusal.AllowedRoles);
    }

    [Fact]
    public async Task Not_own_subject_with_a_null_subject_is_refused()
    {
        var handler = new CountingHandler();
        var policy = new CommandPolicy("shifts.approve_reject", new[] { RoleCode.Dispatcher }.ToHashSet(),
            separation: SeparationOfDuty.NotOwnSubject, subjectId: _ => null);
        var dispatcher = CreateDispatcher(AccessContext.For(Guid.NewGuid(), RoleCode.Dispatcher), policy, handler);

        var refusal = await Assert.ThrowsAsync<AuthorizationException>(() =>
            dispatcher.DispatchAsync<MatrixCommand, int>(new MatrixCommand(null), TestContext.Current.CancellationToken));
        Assert.Equal(RefusalCodes.SelfApproval, refusal.Code);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public void Command_policy_constructor_rejects_every_invalid_rule()
    {
        Assert.Throws<ArgumentException>(() => new CommandPolicy(" ", new[] { RoleCode.Driver }.ToHashSet()));
        Assert.Throws<ArgumentException>(() => new CommandPolicy("x", Array.Empty<RoleCode>().ToHashSet()));
        Assert.Throws<ArgumentException>(() => new CommandPolicy("x", new[] { RoleCode.Driver }.ToHashSet(), SubjectRelation.Own, ownRoles: new[] { RoleCode.Driver }.ToHashSet()));
        Assert.Throws<ArgumentException>(() => new CommandPolicy("x", new[] { RoleCode.Driver }.ToHashSet(), SubjectRelation.Own,
            subjectId: _ => null));
        Assert.Throws<ArgumentException>(() => new CommandPolicy("x", new[] { RoleCode.Driver }.ToHashSet(), ownRoles: new[] { RoleCode.Driver }.ToHashSet()));
        Assert.Throws<ArgumentException>(() => new CommandPolicy("x", new[] { RoleCode.Driver }.ToHashSet(), ownRoles: new[] { RoleCode.Dispatcher }.ToHashSet()));
    }

    [Fact]
    public void Own_roles_outside_allowed_roles_are_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(() => new CommandPolicy("x", new[] { RoleCode.Driver }.ToHashSet(),
            SubjectRelation.Own, subjectId: _ => Guid.NewGuid(), ownRoles: new[] { RoleCode.Dispatcher }.ToHashSet()));
        Assert.Contains("ownRoles", exception.Message);
    }

    [Fact]
    public void Subject_dependent_policies_cannot_be_built_without_a_subject_delegate()
    {
        Assert.Throws<ArgumentException>(() => new CommandPolicy("x", new[] { RoleCode.Driver }.ToHashSet(), SubjectRelation.Own, ownRoles: new[] { RoleCode.Driver }.ToHashSet()));
        Assert.Throws<ArgumentException>(() => new CommandPolicy("x", new[] { RoleCode.Driver }.ToHashSet(), separation: SeparationOfDuty.NotOwnSubject));
    }

    private sealed record MatrixCommand(Guid? SubjectId) : ICommand<int>;
    [CommandPolicy("notifications.mine")]
    private sealed record AttributedCommand : ICommand<int>;
    [CommandPolicy("employees.view")]
    private sealed record OwnAttributedCommand(Guid? SubjectId) : ICommand<int>, IHasSubject;
    [CommandPolicy("shifts.approve_reject", Separation = SeparationOfDuty.OwnerRoutesToHr)]
    private sealed record SeparatedAttributedCommand(Guid? SubjectId) : ICommand<int>, IHasSubject;
    private sealed class CountingHandler : ICommandHandler<MatrixCommand, int>
    {
        public int Count { get; private set; }
        public Task<int> HandleAsync(MatrixCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken) { Count++; return Task.FromResult(1); }
    }
    private sealed class TestAccessor(AccessContext? current) : IAccessContextAccessor { public AccessContext? Current => current; }
    private sealed class TestPolicies(CommandPolicy policy) : ICommandPolicyProvider { public CommandPolicy? GetPolicy(Type commandType) => policy; }
    private sealed class NullPolicyProvider : ICommandPolicyProvider { public CommandPolicy? GetPolicy(Type commandType) => null; }
    private sealed class TestTransactions(ICommandTransaction transaction) : ICommandTransactionFactory
    {
        public Task<ICommandTransaction> BeginAsync(CancellationToken cancellationToken) => Task.FromResult(transaction);
    }
    private sealed class TestTransaction : ICommandTransaction
    {
        public CommandContext Context { get; } = new(null!, null!);
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingTransaction : ICommandTransaction
    {
        public int Commits; public int Rollbacks; public int Disposals; public bool ThrowOnRollback;
        public CommandContext Context { get; } = new(null!, null!);
        public Task CommitAsync(CancellationToken cancellationToken) { Commits++; return Task.CompletedTask; }
        public Task RollbackAsync(CancellationToken cancellationToken) { Rollbacks++; if (ThrowOnRollback) throw new InvalidOperationException("rollback"); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
    private sealed class ThrowingHandler : ICommandHandler<MatrixCommand, int>
    { public Task<int> HandleAsync(MatrixCommand c, AccessContext a, CommandContext x, CancellationToken t) => throw new InvalidOperationException("handler"); }
    private sealed class ContextRecordingHandler : ICommandHandler<MatrixCommand, int>
    {
        public CommandContext? Context { get; private set; }
        public Task<int> HandleAsync(MatrixCommand c, AccessContext a, CommandContext context, CancellationToken t)
        { Context = context; return Task.FromResult(1); }
    }

    private static CommandDispatcher CreateDispatcher(AccessContext actor, CommandPolicy policy, ICommandHandler<MatrixCommand, int> handler, ISubjectRoleLookup? subjectRoles = null, ICommandTransaction? transaction = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAccessContextAccessor>(new TestAccessor(actor));
        services.AddSingleton<ICommandPolicyProvider>(new TestPolicies(policy));
        services.AddSingleton<ICommandTransactionFactory>(new TestTransactions(transaction ?? new TestTransaction()));
        services.AddSingleton<ISubjectRoleLookup>(subjectRoles ?? new FailClosedSubjectRoleLookup());
        services.AddSingleton<ICommandHandler<MatrixCommand, int>>(handler);
        services.AddLogging();
        services.AddScoped<CommandDispatcher>();
        return services.BuildServiceProvider().GetRequiredService<CommandDispatcher>();
    }
    [QueryPolicy("documents.medical.view")]
    private sealed record MedicalQuery(Guid? SubjectId) : IQuery<MedicalReadModel>, IHasSubject;
    [VisibleTo(RoleCode.Dispatcher, RoleCode.Hr)]
    private sealed record MedicalReadModel(Guid EmployeeId) : IReadModel;
    private sealed class MedicalQueryHandler : IQueryHandler<MedicalQuery, MedicalReadModel>
    { public Task<MedicalReadModel> HandleAsync(MedicalQuery q, AccessContext a, CancellationToken t) => Task.FromResult(new MedicalReadModel(q.SubjectId ?? a.EmployeeId)); }

    private sealed class OwnerLookup(Guid ownerId) : ISubjectRoleLookup
    {
        public Task<IReadOnlySet<RoleCode>?> GetRolesAsync(Guid subjectId, CommandContext context, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<RoleCode>?>(subjectId == ownerId ? new[] { RoleCode.Owner }.ToHashSet() : null);
    }

    private sealed record NoPolicyQuery : IQuery<VisibleReadModel>;
    [QueryPolicy("employees.view")]
    private sealed record PolicyQuery : IQuery<UnmarkedReadModel>;
    [VisibleTo(RoleCode.Hr)] private sealed record VisibleReadModel : IReadModel;
    private sealed record UnmarkedReadModel : IReadModel;
    [QueryPolicy("employees.view")]
    private sealed record OwnerQuery : IQuery<OwnerReadModel>;
    [VisibleTo(RoleCode.Owner)] private sealed record OwnerReadModel : IReadModel;
    private sealed class OwnerQueryHandler : IQueryHandler<OwnerQuery, OwnerReadModel>
    { public Task<OwnerReadModel> HandleAsync(OwnerQuery query, AccessContext actor, CancellationToken cancellationToken) => Task.FromResult(new OwnerReadModel()); }
}
