using System.Security.Claims;
using Dogo.Api.Common;
using Dogo.Api.Features.Dogs;
using Dogo.Api.Infrastructure.Auth;
using Dogo.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dogo.Api.Features.Users;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("").WithTags("Users");

        group.MapGet("/me", GetMe)
            .WithName("getMe")
            .WithSummary("Get the signed-in user's profile.");

        group.MapPatch("/me", UpdateMe)
            .WithName("updateMe")
            .WithSummary("Update the signed-in user's profile.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/me", DeleteMe)
            .WithName("deleteMe")
            .WithSummary("Delete the signed-in user's account: upcoming walks cancelled, dogs, registrations, devices, memberships, blocks and sessions deleted; messages and reports kept without author. Allowed to a suspended account (the only write it may do).")
            .AllowWhileSuspended();

        group.MapGet("/users/{id:guid}", GetUser)
            .WithName("getUser")
            .WithSummary("Get a user's public profile and dogs.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<Results<Ok<MeResponse>, ProblemHttpResult>> GetMe(
        ClaimsPrincipal principal, DogoDbContext db, IOptions<AdminOptions> admin, CancellationToken cancellationToken)
    {
        var user = await db.Users.FindAsync([principal.GetUserId()], cancellationToken);
        return user is null
            ? Problems.Unauthorized(ErrorCodes.Unauthenticated, "The account no longer exists.")
            : TypedResults.Ok(MeResponse.From(user, admin.Value));
    }

    private static async Task<Results<Ok<MeResponse>, ProblemHttpResult>> UpdateMe(
        UpdateMeRequest request, ClaimsPrincipal principal, DogoDbContext db, IOptions<AdminOptions> admin, CancellationToken cancellationToken)
    {
        var user = await db.Users.FindAsync([principal.GetUserId()], cancellationToken);
        if (user is null)
        {
            return Problems.Unauthorized(ErrorCodes.Unauthenticated, "The account no longer exists.");
        }

        if (request.DisplayName is not null)
        {
            user.DisplayName = request.DisplayName.Trim();
        }

        if (request.Bio is not null)
        {
            user.Bio = request.Bio.Trim();
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(MeResponse.From(user, admin.Value));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteMe(
        ClaimsPrincipal principal, AccountDeletion deletion, CancellationToken cancellationToken) =>
        await deletion.DeleteAsync(principal.GetUserId(), cancellationToken)
            ? TypedResults.NoContent()
            : Problems.Unauthorized(ErrorCodes.InvalidToken, "The account no longer exists.");

    private static async Task<Results<Ok<PublicUserResponse>, ProblemHttpResult>> GetUser(
        Guid id, DogoDbContext db, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return Problems.NotFound(ErrorCodes.UserNotFound, "The user does not exist.");
        }

        var dogs = await db.Dogs.AsNoTracking().WithMembers().OwnedBy(id).OrderForDisplay().ToListAsync(cancellationToken);
        return TypedResults.Ok(PublicUserResponse.From(user, dogs));
    }
}
