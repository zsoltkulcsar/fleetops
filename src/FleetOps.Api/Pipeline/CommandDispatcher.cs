using FleetOps.Kernel.Authorization;
using FleetOps.Kernel;
using FleetOps.Modules.Notifications;

namespace FleetOps.Api.Pipeline;

public sealed class CommandDispatcher(IAccessContextAccessor accessor, ICommandPolicyProvider policies, ICommandTransactionFactory transactions, IServiceProvider services, ISubjectRoleLookup subjectRoles, ILogger<CommandDispatcher> logger)
{
    public async Task<TResult> DispatchAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand<TResult>
    {
        var policy = policies.GetPolicy(command.GetType()) ?? throw new InvalidOperationException($"Command {command.GetType().FullName} has no policy.");
        var actor = accessor.Current;
        if (actor is null) Refuse(RefusalCodes.NotSignedIn, policy, []);
        if (policy.RequiresStepUp && accessor is not IStepUpContextAccessor { HasFreshStepUp: true }) Refuse(RefusalCodes.StepUpRequired, policy, policy.AllowedRoles);
        var subject = policy.SubjectId?.Invoke(command);
        var permittedRoles = actor!.Roles.Where(role =>
        {
            var cell = PermissionMatrix.Resolve(policy.ActionKey, [role])?.Cell ?? PermissionMatrix.Cell.None;
            return cell == PermissionMatrix.Cell.Full || (cell == PermissionMatrix.Cell.Own && subject is Guid own && own == actor.EmployeeId);
        }).ToHashSet();
        if (permittedRoles.Count == 0) Refuse(RefusalCodes.PermissionDenied, policy, policy.AllowedRoles);
        if (policy.Separation == SeparationOfDuty.NotOwnSubject && subject is not Guid) Refuse(RefusalCodes.SelfApproval, policy, policy.AllowedRoles);
        if (policy.Separation != SeparationOfDuty.None && subject == actor.EmployeeId) Refuse(RefusalCodes.SelfApproval, policy, policy.AllowedRoles);

        await using var transaction = await transactions.BeginAsync(cancellationToken);
        if (policy.Separation == SeparationOfDuty.OwnerRoutesToHr)
        {
            var routedSubject = subject ?? throw new AuthorizationException(RefusalCodes.OwnerRoutesToHr, policy.ActionKey, [RoleCode.Hr]);
            var roles = await subjectRoles.GetRolesAsync(routedSubject, transaction.Context, cancellationToken);
            if (roles is null || roles.Count == 0 || (roles.Contains(RoleCode.Owner) && !actor.Roles.Contains(RoleCode.Hr)))
                Refuse(RefusalCodes.OwnerRoutesToHr, policy, [RoleCode.Hr]);
        }
        foreach (var precondition in services.GetServices<ICommandStatePrecondition<TCommand>>())
            if (!await precondition.CheckAsync(command, actor, transaction.Context, cancellationToken)) Refuse(RefusalCodes.StateConflict, policy, policy.AllowedRoles);
        if (policy.StatePrecondition is not null && !policy.StatePrecondition(command, actor)) Refuse(RefusalCodes.StateConflict, policy, policy.AllowedRoles);

        var handler = services.GetService<ICommandHandler<TCommand, TResult>>() ?? throw new InvalidOperationException($"No handler registered for {typeof(TCommand).FullName}.");
        try
        {
            var result = await handler.HandleAsync(command, actor, transaction.Context, cancellationToken);
            if (result is not null && result.GetType() != typeof(TResult)) throw new InvalidOperationException($"Handler returned {result.GetType()} instead of {typeof(TResult)}.");
            var visible = typeof(TResult).GetCustomAttributes(typeof(VisibleToAttribute), true).SingleOrDefault() as VisibleToAttribute;
            if (visible is not null && !permittedRoles.Any(visible.Roles.Contains)) Refuse(RefusalCodes.PermissionDenied, policy, visible.Roles);
            var changed = services.GetService<IChangedSubjects>();
            var evaluation = services.GetService<IPostCommandEvaluation>();
            if (changed is not null && evaluation is not null && changed.Items.Count > 0)
                await evaluation.EvaluateAsync(changed.Items, transaction.Context, cancellationToken);
            await transaction.CommitAsync(CancellationToken.None);
            var postCommit = services.GetService<IPostCommitNotifications>();
            var notificationIds = services.GetService<INotificationIds>()?.Items ?? [];
            if (postCommit is not null && notificationIds.Count > 0)
            {
                try { await postCommit.TrySendAsync(notificationIds, CancellationToken.None); }
                catch (Exception exception) { logger.LogWarning("Post-commit notification send failed. ExceptionType={ExceptionType}", exception.GetType().Name); }
            }
            return result;
        }
        catch { try { await transaction.RollbackAsync(CancellationToken.None); } catch { } throw; }
    }

    // Restricted entry point for registered notification jobs, including authenticated cron runs.
    internal async Task<NotificationJobResult> DispatchNotificationJobAsync(RunNotificationJobCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        var handler = services.GetRequiredService<ICommandHandler<RunNotificationJobCommand, NotificationJobResult>>();
        var result = await handler.HandleAsync(command, AccessContext.For(Guid.Empty, RoleCode.Owner), transaction.Context, cancellationToken);
        await transaction.CommitAsync(CancellationToken.None);
        var ids = services.GetRequiredService<INotificationIds>().Items;
        if (ids.Count > 0)
            await services.GetRequiredService<IPostCommitNotifications>().TrySendAsync(ids, CancellationToken.None);
        return result;
    }

    internal async Task ApplyScheduledLeaveAsync(Guid employeeId, DateOnly today, CommandContext context, CancellationToken cancellationToken)
    {
        var handler = services.GetRequiredService<ICommandHandler<FleetOps.People.ApplyScheduledLeaveCommand, FleetOps.People.PersonSaveResult>>();
        await handler.HandleAsync(new(employeeId, today), AccessContext.For(Guid.Empty, RoleCode.Hr), context, cancellationToken);
    }

    // Only the driver authentication command can enter without an existing session.
    public async Task<FleetOps.Identity.EnrolDriverResult?> DispatchAsync(FleetOps.Identity.EnrolDriverCommand command, CancellationToken cancellationToken = default)
    {
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        try
        {
            var handler = services.GetRequiredService<FleetOps.Identity.EnrolDriverHandler>();
            var result = await handler.ExecuteAsync(command, transaction.Context, cancellationToken);
            // A refusal commits its failed-attempt increments.
            await transaction.CommitAsync(CancellationToken.None);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void Refuse(string code, CommandPolicy policy, IReadOnlyCollection<RoleCode> allowed) => throw new AuthorizationException(code, policy.ActionKey, allowed);

}
