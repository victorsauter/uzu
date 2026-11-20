using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Dogo.Api.Common;
using Dogo.Api.Features.Notifications;
using Dogo.Api.Features.Organizations;
using Dogo.Api.Features.Reports;
using Dogo.Api.Features.Users;
using Dogo.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Dogo.Api.Features.Admin;

/// <summary>
/// Administration (spec §4.7, ADR 0020): approving organisations, handling reports, suspending
/// accounts. No screen in V1; every endpoint answers 403 <c>admin.forbidden</c> to non-administrators.
/// </summary>
public static class AdminEndpoints
{
    public const int DefaultListLimit = 50;
    public const int MaxListLimit = 100;

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/admin")
            .WithTags("Admin")
            .RequireAdmin()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/organizations", ListOrganizations)
            .WithName("adminListOrganizations")
            .WithSummary("List organisations in one status (Pending by default), oldest first.")
            .ProducesValidationProblem();

        group.MapPost("/organizations/{id:guid}/approve", ApproveOrganization)
            .WithName("adminApproveOrganization")
            .WithSummary("Approve an organisation; its owner is notified. Idempotent.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/organizations/{id:guid}/reject", RejectOrganization)
            .WithName("adminRejectOrganization")
            .WithSummary("Reject an organisation (pending, or approved to withdraw it). Idempotent.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/reports", ListReports)
            .WithName("adminListReports")
            .WithSummary("List reports in one status (Open by default), oldest first, with the reporter and what they are about.")
            .ProducesValidationProblem();

        group.MapPatch("/reports/{id:guid}", UpdateReport)
            .WithName("adminUpdateReport")
            .WithSummary("Classify a report and write an administration note.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/users/{id:guid}/suspend", SuspendUser)
            .WithName("adminSuspendUser")
            .WithSummary("Suspend an account: writes refused (except deleting the account), sessions revoked, weekly series stopped, upcoming walks cancelled with their registrants notified, push devices removed, upcoming registrations to other walks cancelled. Idempotent.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/users/{id:guid}/unsuspend", UnsuspendUser)
            .WithName("adminUnsuspendUser")
            .WithSummary("Lift an account's suspension. Restores nothing: series stay stopped, walks and registrations cancelled, devices and sessions gone. Idempotent.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<Ok<List<AdminOrganizationResponse>>> ListOrganizations(
        DogoDbContext db,
        CancellationToken cancellationToken,
        OrganizationStatus status = OrganizationStatus.Pending,
        [Range(1, MaxListLimit)] int limit = DefaultListLimit)
    {
        var organizations = await db.Organizations.AsNoTracking()
            .Where(o => o.Status == status)
            .OrderBy(o => o.CreatedAt).ThenBy(o => o.Id)
            .Take(limit)
            .Select(o => new { Organization = o, o.CreatedBy, MemberCount = o.Memberships.Count(m => m.Status == MembershipStatus.Accepted) })
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(organizations.Select(o => ToResponse(o.Organization, o.CreatedBy, o.MemberCount)).ToList());
    }

    private static async Task<Results<Ok<AdminOrganizationResponse>, ProblemHttpResult>> ApproveOrganization(
        Guid id, ClaimsPrincipal principal, DogoDbContext db, NotificationQueue notifications, CancellationToken cancellationToken)
    {
        var (organization, problem) = await FindOpenOrganizationAsync(db, id, cancellationToken);
        if (organization is null)
        {
            return problem!;
        }

        if (organization.Status != OrganizationStatus.Approved)
        {
            organization.Status = OrganizationStatus.Approved;
            await db.SaveChangesAsync(cancellationToken);

            // Spec §4.4: the owner is told.
            notifications.Enqueue(new NotificationRequest(
                NotificationType.OrganizationApproved,
                NotificationSubject.ForOrganization(organization),
                principal.GetUserId(),
                await db.OwnerIdsAsync(organization.Id, cancellationToken)));
        }

        return TypedResults.Ok(await LoadResponseAsync(db, organization, cancellationToken));
    }

    private static async Task<Results<Ok<AdminOrganizationResponse>, ProblemHttpResult>> RejectOrganization(
        Guid id, DogoDbContext db, CancellationToken cancellationToken)
    {
        var (organization, problem) = await FindOpenOrganizationAsync(db, id, cancellationToken);
        if (organization is null)
        {
            return problem!;
        }

        // The organisation disappears from other users' view; its walks and members are kept.
        organization.Status = OrganizationStatus.Rejected;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await LoadResponseAsync(db, organization, cancellationToken));
    }

    private static async Task<Ok<List<AdminReportResponse>>> ListReports(
        DogoDbContext db,
        CancellationToken cancellationToken,
        ReportStatus status = ReportStatus.Open,
        [Range(1, MaxListLimit)] int limit = DefaultListLimit)
    {
        var reports = await WithReferences(db.Reports.AsNoTracking())
            .Where(r => r.Status == status)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(reports.Select(AdminReportResponse.From).ToList());
    }

    private static async Task<Results<Ok<AdminReportResponse>, ProblemHttpResult>> UpdateReport(
        Guid id, UpdateReportRequest request, DogoDbContext db, CancellationToken cancellationToken)
    {
        var report = await WithReferences(db.Reports).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (report is null)
        {
            return Problems.NotFound(ErrorCodes.ReportNotFound, "The report does not exist.");
        }

        if (request.Status is { } status)
        {
            report.Status = status;
        }

        if (request.AdminNote is not null)
        {
            report.AdminNote = string.IsNullOrWhiteSpace(request.AdminNote) ? null : request.AdminNote.Trim();
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(AdminReportResponse.From(report));
    }

    private static async Task<Results<Ok<AdminUserResponse>, ProblemHttpResult>> SuspendUser(
        Guid id, ClaimsPrincipal principal, AccountSuspension suspension, CancellationToken cancellationToken)
    {
        if (id == principal.GetUserId())
        {
            return Problems.Conflict(ErrorCodes.AdminSelfSuspension, "An administrator cannot suspend their own account.");
        }

        var user = await suspension.SuspendAsync(id, cancellationToken);
        return user is null ? UserNotFound() : TypedResults.Ok(new AdminUserResponse(user.Id, user.DisplayName, user.Status));
    }

    private static async Task<Results<Ok<AdminUserResponse>, ProblemHttpResult>> UnsuspendUser(
        Guid id, AccountSuspension suspension, CancellationToken cancellationToken)
    {
        var user = await suspension.UnsuspendAsync(id, cancellationToken);
        return user is null ? UserNotFound() : TypedResults.Ok(new AdminUserResponse(user.Id, user.DisplayName, user.Status));
    }

    /// <summary>An organisation an administrator can still decide on: 404, or 409 <c>organization.closed</c>.</summary>
    private static async Task<(Organization? Organization, ProblemHttpResult? Problem)> FindOpenOrganizationAsync(
        DogoDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (organization is null)
        {
            return (null, OrganizationQueries.NotFound());
        }

        return organization.Status == OrganizationStatus.Closed
            ? (null, OrganizationQueries.Closed())
            : (organization, null);
    }

    private static async Task<AdminOrganizationResponse> LoadResponseAsync(DogoDbContext db, Organization organization, CancellationToken cancellationToken)
    {
        var createdBy = organization.CreatedById is { } createdById
            ? await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == createdById, cancellationToken)
            : null;
        var memberCount = await db.Memberships.CountAsync(
            m => m.OrganizationId == organization.Id && m.Status == MembershipStatus.Accepted, cancellationToken);
        return ToResponse(organization, createdBy, memberCount);
    }

    private static AdminOrganizationResponse ToResponse(Organization organization, User? createdBy, int memberCount) => new(
        organization.Id,
        organization.Name,
        organization.Description,
        organization.City,
        organization.Status,
        createdBy is null ? null : UserSummaryResponse.From(createdBy),
        memberCount,
        organization.CreatedAt);

    private static IQueryable<Report> WithReferences(IQueryable<Report> reports) =>
        reports.Include(r => r.Reporter).Include(r => r.TargetUser).Include(r => r.TargetDog).ThenInclude(d => d!.Members).ThenInclude(m => m.User).Include(r => r.Walk);

    private static ProblemHttpResult UserNotFound() => Problems.NotFound(ErrorCodes.UserNotFound, "The user does not exist.");
}
