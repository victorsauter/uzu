using System.Net;
using System.Net.Http.Json;
using Dogo.Api.Features.Dogs;
using Dogo.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dogo.Api.Tests.Dogs;

/// <summary>An account has at most <see cref="Dog.MaxPerOwner"/> dogs (409 <c>dog.limit_reached</c>).</summary>
public sealed class DogLimitTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private async Task AddDogsAsync(Guid ownerId, int count) =>
        await WithDbAsync(async db =>
        {
            for (var i = 0; i < count; i++)
            {
                var dog = new Dog
                {
                    Name = $"Dog {i}",
                    Size = DogSize.Medium,
                    Sociability = Sociability.Easygoing,
                    Reactivity = Reactivity.None,
                    LeashHabit = LeashHabit.OnLeash,
                };
                db.Dogs.Add(dog);
                db.DogMembers.Add(new DogMember { DogId = dog.Id, UserId = ownerId, Role = DogMemberRole.Owner });
            }

            await db.SaveChangesAsync(Ct);
        });

    [Fact]
    public async Task The_twenty_first_dog_is_refused()
    {
        var (client, session) = await SignedInAsync();
        await AddDogsAsync(session.User.Id, Dog.MaxPerOwner - 1);

        await CreateDogAsync(client);
        var refused = await client.PostAsJsonAsync("/api/v1/dogs", ValidDog("One too many"), Ct);

        await AssertProblemAsync(refused, HttpStatusCode.Conflict, "dog.limit_reached");
        Assert.Equal(Dog.MaxPerOwner, await WithDbAsync(db => db.Dogs.CountAsync(Ct)));
    }

    [Fact]
    public async Task Deleting_a_dog_makes_room_again_and_others_are_not_concerned()
    {
        var (client, session) = await SignedInAsync();
        await AddDogsAsync(session.User.Id, Dog.MaxPerOwner);
        var (other, _) = await SignedInAsync("bob", "Bob");

        await CreateDogAsync(other);
        var someDog = await WithDbAsync(db => db.DogMembers.Where(m => m.UserId == session.User.Id).Select(m => m.Dog).Select(d => d.Id).FirstAsync(Ct));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/dogs/{someDog}", Ct)).StatusCode);
        await CreateDogAsync(client);
    }

    [Fact]
    public async Task Concurrent_creations_cannot_pass_the_limit_together()
    {
        var (client, session) = await SignedInAsync();
        await AddDogsAsync(session.User.Id, Dog.MaxPerOwner - 2);
        await client.GetAsync("/api/v1/me/dogs", Ct);

        using var start = new SemaphoreSlim(0);
        var requests = Enumerable.Range(0, 10).Select(async i =>
        {
            await start.WaitAsync(Ct);
            return await client.PostAsJsonAsync("/api/v1/dogs", ValidDog($"Racer {i}"), Ct);
        }).ToList();
        start.Release(10);
        var responses = await Task.WhenAll(requests);

        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        foreach (var refused in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            await AssertProblemAsync(refused, HttpStatusCode.Conflict, "dog.limit_reached");
        }

        Assert.Equal(Dog.MaxPerOwner, await WithDbAsync(db => db.Dogs.CountAsync(Ct)));
    }
}
