using Dogo.Api.Features.Photos;
using Dogo.Api.Features.Users;
using Dogo.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dogo.Api.Features.Dogs;

/// <summary>A human linked to a dog, with their role (ADR 0042).</summary>
public sealed record DogMemberResponse(UserSummaryResponse User, DogMemberRole Role);

public sealed record DogResponse(
    Guid Id,
    IReadOnlyList<DogMemberResponse> Members,
    string Name,
    string? PhotoUrl,
    DogSize Size,
    DateOnly? BirthDate,
    string? Breed,
    Sociability Sociability,
    Reactivity Reactivity,
    LeashHabit LeashHabit,
    string Description,
    DateTimeOffset CreatedAt)
{
    /// <summary>Requires <see cref="Dog.Members"/> and their users to be loaded (<see cref="DogQueries.WithMembers"/>).</summary>
    public static DogResponse From(Dog dog) => new(
        dog.Id,
        MembersOf(dog),
        dog.Name,
        PhotoUrls.For(dog.PhotoFileName),
        dog.Size,
        dog.BirthDate,
        dog.Breed,
        dog.Sociability,
        dog.Reactivity,
        dog.LeashHabit,
        dog.Description,
        dog.CreatedAt);

    /// <summary>The humans linked to the dog, oldest link first. Requires <see cref="Dog.Members"/> and their users to be loaded.</summary>
    internal static IReadOnlyList<DogMemberResponse> MembersOf(Dog dog) =>
        dog.Members
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.UserId)
            .Select(m => new DogMemberResponse(UserSummaryResponse.From(m.User), m.Role))
            .ToList();
}

public static class DogQueries
{
    /// <summary>Display order of a user's dogs: oldest first.</summary>
    public static IQueryable<Dog> OrderForDisplay(this IQueryable<Dog> dogs) =>
        dogs.OrderBy(d => d.CreatedAt).ThenBy(d => d.Id);

    /// <summary>Loads the humans that <see cref="DogResponse.From"/> needs, in the same query (no N+1).</summary>
    public static IQueryable<Dog> WithMembers(this IQueryable<Dog> dogs) => dogs.Include(d => d.Members).ThenInclude(m => m.User);

    /// <summary>Dogs of which <paramref name="userId"/> is an owner (ADR 0042).</summary>
    public static IQueryable<Dog> OwnedBy(this IQueryable<Dog> dogs, Guid userId) =>
        dogs.Where(d => d.Members.Any(m => m.UserId == userId && m.Role == DogMemberRole.Owner));

    /// <summary>Whether <paramref name="userId"/> is an owner of the dog.</summary>
    public static Task<bool> IsOwnerAsync(this DogoDbContext db, Guid dogId, Guid userId, CancellationToken cancellationToken) =>
        db.DogMembers.AnyAsync(m => m.DogId == dogId && m.UserId == userId && m.Role == DogMemberRole.Owner, cancellationToken);
}
