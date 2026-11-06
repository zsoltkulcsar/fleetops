using Npgsql;
using FleetOps.Migrations;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;
using FleetOps.Api.Pipeline;
using FleetOps.Kernel.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace FleetOps.Tests;

public sealed class PostgresTests
{
    [Fact]
    public async Task Real_postgres_transaction_context_commits_isolates_and_rolls_back()
    {
        await using var postgres = await TestPostgres.StartAsync(nameof(PostgresTests), TestContext.Current.CancellationToken);
        await using (var setup = new NpgsqlConnection(postgres.AdminConnectionString))
        {
            await setup.OpenAsync(TestContext.Current.CancellationToken);
            await using var create = new NpgsqlCommand("create table context_rows (id integer not null)", setup);
            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            await using var grant = new NpgsqlCommand("grant select, insert on table context_rows to fleetops_runtime", setup);
            await grant.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var precondition = new ContextPrecondition();
        var provider = new ServiceCollection()
            .AddSingleton<IAccessContextAccessor>(new FixedAccessor(AccessContext.For(Guid.NewGuid(), RoleCode.Dispatcher)))
            .AddSingleton<ICommandPolicyProvider>(new FixedPolicyProvider(CommandPolicy.For("shifts.approve_reject", RoleCode.Dispatcher)))
            .AddSingleton<ICommandTransactionFactory>(new NpgsqlCommandTransactionFactory(Configuration(postgres.ConnectionString)))
            .AddSingleton<ISubjectRoleLookup, FailClosedSubjectRoleLookup>()
            .AddSingleton<ICommandStatePrecondition<InsertCommand>>(precondition)
            .AddSingleton<ICommandHandler<InsertCommand, int>, InsertContextHandler>()
            .AddScoped<CommandDispatcher>().BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<CommandDispatcher>();
        Assert.Equal(1, await dispatcher.DispatchAsync<InsertCommand, int>(new InsertCommand(), TestContext.Current.CancellationToken));
        Assert.True(precondition.ContextSeen);
        await using (var verify = new NpgsqlConnection(postgres.ConnectionString))
        {
            await verify.OpenAsync(TestContext.Current.CancellationToken);
            await using var count = new NpgsqlCommand("select count(*) from context_rows", verify);
            Assert.Equal(1L, await count.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }
        var failing = new ServiceCollection()
            .AddSingleton<IAccessContextAccessor>(new FixedAccessor(AccessContext.For(Guid.NewGuid(), RoleCode.Dispatcher)))
            .AddSingleton<ICommandPolicyProvider>(new FixedPolicyProvider(CommandPolicy.For("shifts.approve_reject", RoleCode.Dispatcher)))
            .AddSingleton<ICommandTransactionFactory>(new NpgsqlCommandTransactionFactory(Configuration(postgres.ConnectionString)))
            .AddSingleton<ISubjectRoleLookup, FailClosedSubjectRoleLookup>()
            .AddSingleton<ICommandStatePrecondition<InsertCommand>, RefusingPrecondition>()
            .AddSingleton<ICommandHandler<InsertCommand, int>, InsertAndFailHandler>()
            .AddScoped<CommandDispatcher>().BuildServiceProvider();
        await Assert.ThrowsAsync<AuthorizationException>(() => failing.GetRequiredService<CommandDispatcher>().DispatchAsync<InsertCommand, int>(new InsertCommand(), TestContext.Current.CancellationToken));
        await using var final = new NpgsqlConnection(postgres.ConnectionString);
        await final.OpenAsync(TestContext.Current.CancellationToken);
        await using var finalCount = new NpgsqlCommand("select count(*) from context_rows", final);
        Assert.Equal(1L, await finalCount.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task Postgres_17_accepts_a_tls_capable_database_connection()
    {
        await using var postgres = await TestPostgres.StartAsync(nameof(PostgresTests), TestContext.Current.CancellationToken);
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("select 1", connection);
        Assert.Equal(1, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Runtime_role_can_read_the_marker_but_cannot_execute_schema_ddl()
    {
        await using var postgres = await TestPostgres.StartAsync(nameof(PostgresTests), TestContext.Current.CancellationToken);
        var admin = new NpgsqlConnectionStringBuilder(postgres.AdminConnectionString);
        var migrator = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
        { Username = "fleetops_migrator", Password = "migrator-password" };
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.Configure(options, migrator.ConnectionString);
        await using (var db = new FleetOpsDbContext(options.Options))
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var runtime = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
        { Username = "fleetops_runtime", Password = "runtime-password" };
        await using var runtimeConnection = new NpgsqlConnection(runtime.ConnectionString);
        await runtimeConnection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var ddl = new NpgsqlCommand(
            "CREATE TABLE fleetops.runtime_role_must_not_ddl (id integer);", runtimeConnection))
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                ddl.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            Assert.Equal("42501", exception.SqlState);
        }
        await using var select = new NpgsqlCommand(
            "SELECT name FROM fleetops.migration_marker WHERE id = 1;", runtimeConnection);
        Assert.Equal("solution-skeleton", await select.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Real_dispatcher_rolls_back_handler_failure_on_postgres()
    {
        await using var postgres = await TestPostgres.StartAsync(nameof(PostgresTests), TestContext.Current.CancellationToken);
        await using (var setup = new NpgsqlConnection(postgres.AdminConnectionString))
        {
            await setup.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand("create table dispatch_rows (id integer not null)", setup);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            await using var grant = new NpgsqlCommand("grant insert, select on table dispatch_rows to fleetops_runtime", setup);
            await grant.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var policy = new CommandPolicy("shifts.approve_reject", new[] { RoleCode.Dispatcher }.ToHashSet());
        using var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddSingleton<IAccessContextAccessor>(new FixedAccessor(AccessContext.For(Guid.NewGuid(), RoleCode.Dispatcher)))
            .AddSingleton<ICommandPolicyProvider>(new FixedPolicyProvider(policy))
            .AddSingleton<ICommandTransactionFactory>(new NpgsqlCommandTransactionFactory(Configuration(postgres.ConnectionString)))
            .AddSingleton<ISubjectRoleLookup, FailClosedSubjectRoleLookup>()
            .AddSingleton<ICommandHandler<InsertCommand, int>, InsertAndFailHandler>()
            .AddScoped<CommandDispatcher>()
            .BuildServiceProvider();
        var dispatcher = services.GetRequiredService<CommandDispatcher>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync<InsertCommand, int>(new InsertCommand(), TestContext.Current.CancellationToken));

        await using var verify = new NpgsqlConnection(postgres.ConnectionString);
        await verify.OpenAsync(TestContext.Current.CancellationToken);
        await using var count = new NpgsqlCommand("select count(*) from dispatch_rows", verify);
        Assert.Equal(0L, await count.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private sealed record InsertCommand : ICommand<int>;
    private sealed class InsertAndFailHandler : ICommandHandler<InsertCommand, int>
    {
        public async Task<int> HandleAsync(InsertCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
        {
            await using var insert = new NpgsqlCommand("insert into dispatch_rows values (1)", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            throw new InvalidOperationException("expected handler failure");
        }
    }
    private sealed class InsertContextHandler : ICommandHandler<InsertCommand, int>
    {
        public async Task<int> HandleAsync(InsertCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
        {
            await using var insert = new NpgsqlCommand("insert into context_rows values (1)", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
            await insert.ExecuteNonQueryAsync(cancellationToken); return 1;
        }
    }
    private sealed class ContextPrecondition : ICommandStatePrecondition<InsertCommand>
    {
        public bool ContextSeen { get; private set; }
        public Task<bool> CheckAsync(InsertCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
        { ContextSeen = context.DbConnection is NpgsqlConnection && context.DbTransaction is NpgsqlTransaction; return Task.FromResult(ContextSeen); }
    }
    private sealed class RefusingPrecondition : ICommandStatePrecondition<InsertCommand>
    { public Task<bool> CheckAsync(InsertCommand c, AccessContext a, CommandContext x, CancellationToken t) => Task.FromResult(false); }
    private sealed class FixedAccessor(AccessContext context) : IAccessContextAccessor { public AccessContext? Current => context; }
    private sealed class FixedPolicyProvider(CommandPolicy policy) : ICommandPolicyProvider { public CommandPolicy? GetPolicy(Type commandType) => policy; }
    private static IConfiguration Configuration(string connectionString) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:FleetOps"] = connectionString })
        .Build();
}
