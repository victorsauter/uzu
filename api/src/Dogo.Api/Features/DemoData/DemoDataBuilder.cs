using Dogo.Api.Features.Blocks;
using Dogo.Api.Features.Dogs;
using Dogo.Api.Features.Messages;
using Dogo.Api.Features.Organizations;
using Dogo.Api.Features.Reports;
using Dogo.Api.Features.Users;
using Dogo.Api.Features.Walks;
using Dogo.Api.Infrastructure.Persistence;

namespace Dogo.Api.Features.DemoData;

/// <summary>A photo to fetch for a demo person (<see cref="AvatarSeed"/>) or dog (<see cref="WikipediaTitle"/> of its breed).</summary>
internal sealed record DemoPhoto(object Target, string? AvatarSeed, string? WikipediaTitle)
{
    public void Assign(string fileName)
    {
        switch (Target)
        {
            case User user:
                user.PhotoFileName = fileName;
                break;
            case Dog dog:
                dog.PhotoFileName = fileName;
                break;
        }
    }
}

/// <summary>Entities of the demo data set, not saved yet, and the creation date each one should carry.</summary>
internal sealed class DemoDataSet
{
    public List<User> Users { get; } = [];
    public List<Dog> Dogs { get; } = [];
    public List<DogMember> DogMembers { get; } = [];
    public List<Organization> Organizations { get; } = [];
    public List<Membership> Memberships { get; } = [];
    public List<WalkSeries> Series { get; } = [];
    public List<Walk> Walks { get; } = [];
    public List<Registration> Registrations { get; } = [];
    public List<Message> Messages { get; } = [];
    public List<Block> Blocks { get; } = [];
    public List<Report> Reports { get; } = [];
    public List<DemoPhoto> Photos { get; } = [];

    /// <summary>
    /// Back-dated creation dates. <see cref="DogoDbContext"/> stamps <c>CreatedAt</c> with the current
    /// time on insert, so these are applied by a second save.
    /// </summary>
    public Dictionary<IHasCreatedAt, DateTimeOffset> CreatedAt { get; } = new(ReferenceEqualityComparer.Instance);
}

/// <summary>
/// Turns the demo tables (<see cref="DemoPeople"/>, <see cref="DemoCommunity"/>, <see cref="DemoWalks"/>,
/// <see cref="DemoConversations"/>) into entities. Deterministic: the same names and structure on every
/// run (fixed random seeds), dates relative to <c>now</c>. Every business rule of the specification
/// holds on the result: allowed sizes, capacity, one active registration per dog, professional walks
/// for members only, blocked people kept out of the blocker's walks, threads written by the organizer
/// and accepted participants only.
/// </summary>
internal sealed class DemoDataBuilder
{
    /// <summary>Dev sign-in subjects of demo accounts start with this prefix; removal relies on it.</summary>
    public const string SubjectPrefix = "demo-";

    private const int Seed = 68306; // INSEE code of Seppois-le-Haut.
    private static readonly TimeSpan MessageGap = TimeSpan.FromMinutes(1);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(WalkSeries.DefaultTimeZone);

    private readonly DateTimeOffset _now;
    private readonly DateOnly _today;
    private readonly DemoDataSet _set = new();
    private readonly Dictionary<string, User> _users = [];
    private readonly Dictionary<Guid, DemoPerson> _people = [];
    private readonly Dictionary<Guid, List<Dog>> _dogsByOwner = [];
    private readonly Dictionary<string, Organization> _organizations = [];
    private readonly Dictionary<Guid, HashSet<Guid>> _acceptedMembers = [];
    private readonly Dictionary<Guid, HashSet<Guid>> _blockedBy = [];
    private readonly Dictionary<Guid, List<(DateTimeOffset Start, DateTimeOffset End)>> _busy = [];
    private readonly Dictionary<Guid, List<(DateTimeOffset Start, DateTimeOffset End)>> _busyDogs = [];
    private readonly Dictionary<string, Walk> _walksByKey = [];

    private DemoDataBuilder(DateTimeOffset now)
    {
        // Whole seconds: readable dates, and nothing finer than PostgreSQL's microseconds.
        _now = new DateTimeOffset(now.UtcTicks - (now.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
        _today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_now, Zone).DateTime);
    }

    public static DemoDataSet Build(DateTimeOffset now) => new DemoDataBuilder(now).Build();

    public static bool IsDemoSubject(string subject) => subject.StartsWith(SubjectPrefix, StringComparison.Ordinal);

    private DemoDataSet Build()
    {
        AddPeople();
        AddOrganizations();

        // Blocks first: the registrations keep blocked people out of the blocker's walks.
        AddBlocks();

        // Walks with imposed participants first, so that nothing else keeps them busy at that time.
        var index = 0;
        foreach (var template in DemoWalks.All.OrderBy(w => w.With is null))
        {
            AddWalk(template, index++);
        }

        AddReports();
        ChoosePhotos();
        return _set;
    }

    private void AddPeople()
    {
        var people = DemoPeople.Owners.Concat(DemoPeople.Educators).ToList();
        for (var i = 0; i < people.Count; i++)
        {
            var person = people[i];
            var user = new User
            {
                Provider = IdentityProvider.Dev,
                ProviderSubject = SubjectPrefix + person.Key,
                DisplayName = person.DisplayName,
                Bio = person.Bio,
            };
            var joined = _now.AddDays(-(420 - (i * 5)));
            Created(user, joined);
            _set.Users.Add(user);
            _users.Add(person.Key, user);
            _people.Add(user.Id, person);

            var dogs = new List<Dog>();
            for (var d = 0; d < person.Dogs.Count; d++)
            {
                var spec = person.Dogs[d];
                var breed = DemoBreeds.ByName.TryGetValue(spec.Breed, out var known)
                    ? known
                    : throw new InvalidOperationException($"Unknown demo breed '{spec.Breed}'.");
                var dog = new Dog
                {
                    Name = spec.Name,
                    Size = breed.Size,
                    Breed = spec.Breed,
                    BirthDate = _today.AddMonths(-spec.AgeMonths).AddDays(-(((i * 7) + (d * 11)) % 28)),
                    Sociability = spec.Sociability,
                    Reactivity = spec.Reactivity,
                    LeashHabit = spec.LeashHabit,
                    Description = spec.Description,
                };
                Created(dog, joined.AddDays(1 + d).AddMinutes(17));
                dogs.Add(dog);
                _set.Dogs.Add(dog);
                AddDogMember(dog, user, _set.CreatedAt[dog]);
            }

            _dogsByOwner.Add(user.Id, dogs);
        }

        // Dogs shared by two people: the co-owner can register them too (after their own dogs).
        foreach (var shared in DemoCommunity.SharedDogs)
        {
            var dog = _dogsByOwner[_users[shared.Owner].Id].Single(d => d.Name == shared.Name);
            var coOwner = _users[shared.CoOwner];
            var linkedAt = _now.AddDays(-shared.DaysAgo);
            if (linkedAt <= _set.CreatedAt[dog] || linkedAt <= _set.CreatedAt[coOwner])
            {
                throw new InvalidOperationException($"Shared demo dog '{shared.Name}' is linked before it or its co-owner exists.");
            }

            AddDogMember(dog, coOwner, linkedAt);
            _dogsByOwner[coOwner.Id].Add(dog);
        }
    }

    private void AddDogMember(Dog dog, User user, DateTimeOffset createdAt)
    {
        var member = new DogMember { DogId = dog.Id, UserId = user.Id, Role = DogMemberRole.Owner };
        Created(member, createdAt);
        _set.DogMembers.Add(member);
    }

    private void AddOrganizations()
    {
        var owners = DemoPeople.Owners.Select(p => _users[p.Key]).ToList();
        for (var i = 0; i < DemoCommunity.Organizations.Count; i++)
        {
            var spec = DemoCommunity.Organizations[i];
            var owner = _users[spec.Owner];
            var organization = new Organization
            {
                Name = spec.Name,
                Description = spec.Description,
                City = spec.City,
                Status = spec.Status,
                CreatedById = owner.Id,
            };
            var createdAt = _set.CreatedAt[owner].AddDays(3);
            Created(organization, createdAt);
            _set.Organizations.Add(organization);
            _organizations.Add(spec.Key, organization);
            _acceptedMembers.Add(organization.Id, []);

            var day = 0;
            AddMembership(organization, owner, MembershipRole.Owner, MembershipStatus.Accepted, createdAt);
            foreach (var educator in spec.Educators)
            {
                AddMembership(organization, _users[educator], MembershipRole.Educator, MembershipStatus.Accepted, createdAt.AddDays(++day));
            }

            foreach (var member in spec.ExtraMembers ?? [])
            {
                AddMembership(organization, _users[member], MembershipRole.Member, MembershipStatus.Accepted, createdAt.AddDays(++day));
            }

            // Members and requests: a fixed draw among the owners.
            var candidates = Shuffle(owners, new Random(Seed + i)).Where(u => !_acceptedMembers[organization.Id].Contains(u.Id)).ToList();
            foreach (var member in candidates.Take(spec.MemberCount))
            {
                AddMembership(organization, member, MembershipRole.Member, MembershipStatus.Accepted, createdAt.AddDays(++day + 2));
            }

            var requests = candidates.Skip(spec.MemberCount).Take(spec.PendingRequestCount).ToList();
            for (var r = 0; r < requests.Count; r++)
            {
                AddMembership(organization, requests[r], MembershipRole.Member, MembershipStatus.Pending, _now.AddDays(-(r + 1)).AddHours(-3 * r));
            }

            if (i == 0)
            {
                // A declined request, kept as history (spec §2).
                var declined = candidates.Skip(spec.MemberCount + spec.PendingRequestCount).First();
                AddMembership(organization, declined, MembershipRole.Member, MembershipStatus.Declined, _now.AddDays(-20));
            }
        }
    }

    private void AddMembership(Organization organization, User user, MembershipRole role, MembershipStatus status, DateTimeOffset createdAt)
    {
        var membership = new Membership { OrganizationId = organization.Id, UserId = user.Id, Role = role, Status = status };
        Created(membership, Min(createdAt, _now.AddDays(-1)));
        _set.Memberships.Add(membership);
        if (status == MembershipStatus.Accepted)
        {
            _acceptedMembers[organization.Id].Add(user.Id);
        }
    }

    private void AddBlocks()
    {
        foreach (var spec in DemoCommunity.Blocks)
        {
            var block = new Block { BlockerId = _users[spec.Blocker].Id, BlockedId = _users[spec.Blocked].Id };
            Created(block, _now.AddDays(-spec.DaysAgo).AddHours(-5));
            _set.Blocks.Add(block);
            if (!_blockedBy.TryGetValue(block.BlockerId, out var blocked))
            {
                _blockedBy[block.BlockerId] = blocked = [];
            }

            blocked.Add(block.BlockedId);
        }
    }

    private void AddWalk(DemoWalk template, int index)
    {
        WalkSeries? series = null;
        if (template.PastWeeks > 0)
        {
            series = new WalkSeries { OrganizerId = _users[template.Organizer].Id };
            _set.Series.Add(series);
        }

        // A series: its past occurrences one week apart, each created when the previous one started
        // (WalkSeriesJob), then the upcoming one.
        DateTimeOffset? previousStart = null;
        for (var week = template.PastWeeks; week >= 0; week--)
        {
            var start = StartOf(template.Day - (7 * week), template.Time);
            var createdAt = previousStart ?? DefaultCreatedAt(start, index);
            var walk = CreateWalk(template, start, createdAt, series, cancelled: week == 0 && template.Cancelled);
            if (series is not null && previousStart is null)
            {
                Created(series, createdAt);
            }

            var conversation = week == 0 ? template.Conversation : week == 1 ? template.PastConversation : null;
            Populate(template, walk, conversation, new Random(Seed + (index * 10) + week));
            previousStart = start;
            if (week == 0)
            {
                _walksByKey.Add(template.Key, walk);
            }
        }
    }

    private Walk CreateWalk(DemoWalk template, DateTimeOffset start, DateTimeOffset createdAt, WalkSeries? series, bool cancelled)
    {
        var walk = new Walk
        {
            OrganizerId = _users[template.Organizer].Id,
            Title = template.Title,
            Description = template.Description,
            StartsAt = start,
            DurationMinutes = template.DurationMinutes,
            Location = Walk.CreatePoint(template.Place.Latitude, template.Place.Longitude),
            LocationLabel = template.Place.Label,
            MaxDogs = template.MaxDogs,
            AllowedSizes = [.. template.Sizes.Distinct().Order()],
            LeashRule = template.LeashRule,
            Rules = template.Rules,
            Access = template.Access,
            Status = cancelled ? WalkStatus.Cancelled : WalkStatus.Scheduled,
            SeriesId = series?.Id,
            OrganizationId = template.Organization is { } key ? _organizations[key].Id : null,
        };

        // Past walks had their reminder, as WalkReminderJob would have done.
        if (start <= _now && !cancelled)
        {
            walk.ReminderSentAt = start - WalkReminderJob.Lead;
        }

        Created(walk, createdAt);
        _set.Walks.Add(walk);
        MarkBusy(walk.OrganizerId!.Value, walk);
        return walk;
    }

    /// <summary>Registrations (accepted, pending, a little history) and the thread of one walk.</summary>
    private void Populate(DemoWalk template, Walk walk, string? conversationKey, Random random)
    {
        var organizerId = walk.OrganizerId!.Value;
        var upcoming = walk.StartsAt > _now;
        var accepted = new List<(Guid UserId, Dog Dog)>();
        var registered = new HashSet<Guid>();

        if (template.OrganizerDogs)
        {
            foreach (var dog in _dogsByOwner[organizerId].Where(d => walk.AllowedSizes.Contains(d.Size) && !IsDogBusy(d.Id, walk)).Take(2))
            {
                if (accepted.Count < template.Accepted)
                {
                    accepted.Add((organizerId, dog));
                    registered.Add(dog.Id);
                }
            }
        }

        var candidates = Candidates(template, walk, random).ToList();
        var next = 0;
        while (accepted.Count < Math.Min(template.Accepted, walk.MaxDogs) && next < candidates.Count)
        {
            var userId = candidates[next++];
            var dogs = EligibleDogs(userId, walk, registered);
            var room = Math.Min(template.Accepted, walk.MaxDogs) - accepted.Count;
            var take = dogs.Count > 1 && room > 1 && random.Next(4) == 0 ? 2 : 1;
            foreach (var dog in dogs.Take(take))
            {
                accepted.Add((userId, dog));
                registered.Add(dog.Id);
            }

            if (dogs.Count > 0)
            {
                MarkBusy(userId, walk);
            }
        }

        // Requests awaiting the organizer: only on upcoming walks with approval.
        var pending = new List<(Guid UserId, Dog Dog)>();
        if (upcoming && walk.Access == WalkAccess.Approval && walk.Status == WalkStatus.Scheduled)
        {
            while (pending.Count < template.Pending && next < candidates.Count)
            {
                var userId = candidates[next++];
                if (EligibleDogs(userId, walk, registered).FirstOrDefault() is { } dog)
                {
                    pending.Add((userId, dog));
                    registered.Add(dog.Id);
                }
            }
        }

        // A little history: a cancelled registration here and there, a declined request on approval walks.
        var history = new List<(Guid UserId, Dog Dog, RegistrationStatus Status)>();
        if (random.Next(3) == 0 && next < candidates.Count && EligibleDogs(candidates[next], walk, registered).FirstOrDefault() is { } cancelledDog)
        {
            history.Add((candidates[next++], cancelledDog, RegistrationStatus.Cancelled));
        }

        if (walk.Access == WalkAccess.Approval && next < candidates.Count && EligibleDogs(candidates[next], walk, registered).FirstOrDefault() is { } declinedDog)
        {
            history.Add((candidates[next], declinedDog, RegistrationStatus.Declined));
        }

        var messages = conversationKey is null ? [] : BuildThread(DemoConversations.ByKey[conversationKey], walk, accepted);

        // Registrations happen between the walk creation and its start (or now), before the first message.
        var registrations = accepted.Select(a => (a.UserId, a.Dog, Status: RegistrationStatus.Accepted))
            .Concat(pending.Select(p => (p.UserId, p.Dog, Status: RegistrationStatus.Pending)))
            .Concat(history)
            .ToList();
        var end = Min(Min(walk.StartsAt, _now), messages.Count > 0 ? messages[0].At : DateTimeOffset.MaxValue).AddMinutes(-10);
        var createdAt = _set.CreatedAt[walk];
        if (end - createdAt < TimeSpan.FromMinutes(registrations.Count + 2))
        {
            createdAt = end.AddHours(-(registrations.Count + 2));
            _set.CreatedAt[walk] = createdAt;
        }

        var step = (end - createdAt) / (registrations.Count + 1);
        for (var r = 0; r < registrations.Count; r++)
        {
            var (userId, dog, status) = registrations[r];
            var registration = new Registration { WalkId = walk.Id, DogId = dog.Id, UserId = userId, Status = status };
            Created(registration, userId == organizerId ? createdAt.AddMinutes(1) : createdAt + (step * (r + 1)));
            _set.Registrations.Add(registration);
            if (registration.IsActive)
            {
                MarkBusy(_busyDogs, dog.Id, walk);
            }
        }

        foreach (var (message, at) in messages)
        {
            Created(message, at);
            _set.Messages.Add(message);
        }
    }

    /// <summary>
    /// People who may register to the walk, imposed participants first: members of the
    /// organisation for a professional walk (spec §4.6), never someone the organizer blocked
    /// (spec §4.7), never someone busy with another walk at that time.
    /// </summary>
    private IEnumerable<Guid> Candidates(DemoWalk template, Walk walk, Random random)
    {
        var organizerId = walk.OrganizerId!.Value;
        var pool = walk.OrganizationId is { } organizationId
            ? _set.Users.Where(u => _acceptedMembers[organizationId].Contains(u.Id))
            : _set.Users;
        var forced = (template.With ?? []).Select(key => _users[key]).ToList();
        var blocked = _blockedBy.GetValueOrDefault(organizerId) ?? [];
        return forced.Concat(Shuffle(pool.Except(forced).ToList(), random))
            .Select(u => u.Id)
            .Where(id => id != organizerId && !blocked.Contains(id) && !IsBusy(id, walk))
            .Where(id => walk.OrganizationId is not { } org || _acceptedMembers[org].Contains(id));
    }

    /// <summary>
    /// The user's dogs (shared ones included) that the walk allows, not registered to it yet, and not
    /// brought by another of their owners to an overlapping walk.
    /// </summary>
    private List<Dog> EligibleDogs(Guid userId, Walk walk, HashSet<Guid> registered) =>
        _dogsByOwner[userId].Where(d => walk.AllowedSizes.Contains(d.Size) && !registered.Contains(d.Id) && !IsDogBusy(d.Id, walk)).ToList();

    /// <summary>
    /// Messages of a thread, written by the organizer and accepted participants, strictly before
    /// now and one minute apart at least.
    /// </summary>
    private List<(Message Message, DateTimeOffset At)> BuildThread(
        DemoConversation conversation, Walk walk, List<(Guid UserId, Dog Dog)> accepted)
    {
        var organizerId = walk.OrganizerId!.Value;
        var participants = accepted.Select(a => a.UserId).Where(id => id != organizerId).Distinct().ToList();
        var lines = new List<(Guid AuthorId, DateTimeOffset At, string Text)>();
        foreach (var line in conversation.Lines)
        {
            if (line.Speaker > 0 && participants.Count == 0)
            {
                continue;
            }

            var authorId = line.Speaker == 0 ? organizerId : participants[(line.Speaker - 1) % participants.Count];
            var dog = accepted.Where(a => a.UserId == authorId).Select(a => a.Dog).FirstOrDefault() ?? _dogsByOwner[authorId].FirstOrDefault();
            var text = line.Text
                .Replace("{dog}", dog?.Name ?? "mon chien", StringComparison.Ordinal)
                .Replace("{organizer}", _people[organizerId].FirstName, StringComparison.Ordinal);
            var at = (conversation.RelativeToNow ? _now : walk.StartsAt).AddMinutes(line.Minutes);
            lines.Add((authorId, at, text));
        }

        // Nothing in the future, in order: clamp from the last message backwards.
        var times = lines.Select(l => l.At).ToArray();
        for (var i = times.Length - 1; i >= 0; i--)
        {
            var latest = i == times.Length - 1 ? _now - MessageGap : times[i + 1] - MessageGap;
            times[i] = Min(times[i], latest);
        }

        return lines.Select((l, i) => (new Message { WalkId = walk.Id, AuthorId = l.AuthorId, Text = l.Text }, times[i])).ToList();
    }

    private void AddReports()
    {
        foreach (var spec in DemoCommunity.Reports)
        {
            var report = new Report
            {
                ReporterId = _users[spec.Reporter].Id,
                TargetUserId = spec.TargetUser is { } user ? _users[user].Id : null,
                TargetDogId = spec.TargetDog is { } dog ? _dogsByOwner[_users[dog.Owner].Id].Single(d => d.Name == dog.Name).Id : null,
                WalkId = spec.Walk is { } walk ? _walksByKey[walk].Id : null,
                Type = spec.Type,
                Facts = spec.Facts,
                Status = spec.Status,
                AdminNote = spec.AdminNote,
            };
            Created(report, _now.AddDays(-spec.DaysAgo).AddHours(-2));
            _set.Reports.Add(report);
        }
    }

    /// <summary>
    /// Half of the people get an illustrated avatar, half of the dogs a photo of their breed: the
    /// first dog of each breed that has one, so that no two dogs share the same picture.
    /// </summary>
    private void ChoosePhotos()
    {
        for (var i = 0; i < _set.Users.Count; i += 2)
        {
            _set.Photos.Add(new DemoPhoto(_set.Users[i], _set.Users[i].ProviderSubject, null));
        }

        var breeds = new HashSet<string>();
        foreach (var dog in _set.Dogs)
        {
            if (breeds.Count == _set.Dogs.Count / 2)
            {
                break;
            }

            var title = DemoBreeds.ByName[dog.Breed!].WikipediaTitle;
            if (title is not null && breeds.Add(dog.Breed!))
            {
                _set.Photos.Add(new DemoPhoto(dog, null, title));
            }
        }
    }

    /// <summary>
    /// The instant of a local time <paramref name="day"/> days from today. An upcoming walk always
    /// starts at least an hour from now: one planned earlier today is moved to the next quarter hour.
    /// </summary>
    private DateTimeOffset StartOf(int day, TimeOnly time)
    {
        var start = WeeklyRecurrence.ToInstant(_today.AddDays(day).ToDateTime(time), Zone);
        if (day >= 0 && start < _now.AddHours(1))
        {
            var soon = _now.AddHours(1);
            var quarter = TimeSpan.FromMinutes(15).Ticks;
            start = new DateTimeOffset(((soon.UtcTicks + quarter - 1) / quarter) * quarter, TimeSpan.Zero);
        }

        return start;
    }

    /// <summary>A few days before the start, and never after now (upcoming walks were created at least five days ago).</summary>
    private DateTimeOffset DefaultCreatedAt(DateTimeOffset start, int index) =>
        Min(start.AddDays(-(6 + (index % 5))), _now.AddDays(-(5 + (index % 6))).AddHours(-index));

    private void MarkBusy(Guid userId, Walk walk) => MarkBusy(_busy, userId, walk);

    private static void MarkBusy(Dictionary<Guid, List<(DateTimeOffset Start, DateTimeOffset End)>> busy, Guid id, Walk walk)
    {
        if (!busy.TryGetValue(id, out var slots))
        {
            busy[id] = slots = [];
        }

        slots.Add((walk.StartsAt, walk.StartsAt.AddMinutes(walk.DurationMinutes)));
    }

    /// <summary>Someone already at another walk (organizing or registered) overlapping this one, with half an hour to get there.</summary>
    private bool IsBusy(Guid userId, Walk walk) => IsBusy(_busy, userId, walk);

    /// <summary>A dog already registered to an overlapping walk, possibly by another of its owners.</summary>
    private bool IsDogBusy(Guid dogId, Walk walk) => IsBusy(_busyDogs, dogId, walk);

    private static bool IsBusy(Dictionary<Guid, List<(DateTimeOffset Start, DateTimeOffset End)>> busy, Guid id, Walk walk)
    {
        var start = walk.StartsAt.AddMinutes(-30);
        var end = walk.StartsAt.AddMinutes(walk.DurationMinutes + 30);
        return busy.TryGetValue(id, out var slots) && slots.Any(s => s.Start < end && start < s.End);
    }

    private void Created(IHasCreatedAt entity, DateTimeOffset createdAt) => _set.CreatedAt[entity] = createdAt;

    private static List<T> Shuffle<T>(IReadOnlyList<T> items, Random random)
    {
        var shuffled = items.ToList();
        random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(shuffled));
        return shuffled;
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a <= b ? a : b;
}
