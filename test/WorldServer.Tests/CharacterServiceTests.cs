using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shouldly;
using WorldServerV2.Data;
using WorldServerV2.Data.Entities;
using WorldServerV2.Services;

namespace WorldServer.Tests;

/// <summary>
/// Unit tests for <see cref="CharacterService"/> (session-scoped loading + directory)
/// and <see cref="GameSession"/> character helpers.
/// Uses EF Core in-memory database to exercise real LINQ queries without a DB server.
/// </summary>
public class CharacterServiceTests
{
    // ── GetCharactersForAccountAsync ───────────────────────────────────

    [Fact]
    public async Task GetCharactersForAccountAsync_returns_characters_from_database()
    {
        using var fixture = new Fixture();
        fixture.Seed(
            new Character { CharacterId = 1, AccountId = 42, SlotId = 0, Name = "Gandalf", Realm = 1 },
            new Character { CharacterId = 2, AccountId = 42, SlotId = 1, Name = "Frodo", Realm = 1 });

        var sut = fixture.CreateService();
        var characters = await sut.GetCharactersForAccountAsync(42);

        characters.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetCharactersForAccountAsync_ignores_other_accounts()
    {
        using var fixture = new Fixture();
        fixture.Seed(
            new Character { CharacterId = 1, AccountId = 42, SlotId = 0, Name = "Mine", Realm = 1 },
            new Character { CharacterId = 2, AccountId = 99, SlotId = 0, Name = "Theirs", Realm = 2 });

        var sut = fixture.CreateService();
        var characters = await sut.GetCharactersForAccountAsync(42);

        characters.Count.ShouldBe(1);
        characters[0].Name.ShouldBe("Mine");
    }

    [Fact]
    public async Task GetCharactersForAccountAsync_returns_empty_for_unknown_account()
    {
        using var fixture = new Fixture();
        var sut = fixture.CreateService();

        var characters = await sut.GetCharactersForAccountAsync(999);

        characters.ShouldBeEmpty();
    }

    // ── Directory: LoadDirectoryAsync + FindByName/FindById ────────────

    [Fact]
    public async Task LoadDirectoryAsync_populates_name_and_id_indexes()
    {
        using var fixture = new Fixture();
        fixture.Seed(
            new Character { CharacterId = 1, AccountId = 42, SlotId = 0, Name = "Gandalf", Realm = 1, Career = 5 },
            new Character { CharacterId = 2, AccountId = 42, SlotId = 1, Name = "Frodo", Realm = 1, Career = 3 });

        var sut = fixture.CreateService();
        await sut.LoadDirectoryAsync();

        var byName = sut.FindByName("Gandalf");
        byName.ShouldNotBeNull();
        byName.Value.CharacterId.ShouldBe(1u);
        byName.Value.Career.ShouldBe((byte)5);

        var byId = sut.FindById(2);
        byId.ShouldNotBeNull();
        byId.Value.Name.ShouldBe("Frodo");
    }

    [Fact]
    public async Task FindByName_is_case_insensitive()
    {
        using var fixture = new Fixture();
        fixture.Seed(new Character { CharacterId = 1, AccountId = 42, SlotId = 0, Name = "Gandalf", Realm = 1 });

        var sut = fixture.CreateService();
        await sut.LoadDirectoryAsync();

        sut.FindByName("gandalf").ShouldNotBeNull();
        sut.FindByName("GANDALF").ShouldNotBeNull();
    }

    [Fact]
    public void FindByName_returns_null_before_directory_loaded()
    {
        using var fixture = new Fixture();
        fixture.Seed(new Character { CharacterId = 1, AccountId = 42, SlotId = 0, Name = "Gandalf", Realm = 1 });

        var sut = fixture.CreateService();

        sut.FindByName("Gandalf").ShouldBeNull();
    }

    [Fact]
    public void FindById_returns_null_for_unknown_id()
    {
        using var fixture = new Fixture();
        var sut = fixture.CreateService();

        sut.FindById(999).ShouldBeNull();
    }

    [Fact]
    public async Task LoadDirectoryAsync_replaces_stale_entries()
    {
        using var fixture = new Fixture();
        fixture.Seed(new Character { CharacterId = 1, AccountId = 42, SlotId = 0, Name = "Original", Realm = 1 });

        var sut = fixture.CreateService();
        await sut.LoadDirectoryAsync();
        sut.FindByName("Original").ShouldNotBeNull();

        // Add another character to the DB
        fixture.Mutate(db =>
        {
            db.Characters.Add(new Character { CharacterId = 2, AccountId = 42, SlotId = 1, Name = "NewChar", Realm = 1 });
            db.SaveChanges();
        });

        await sut.LoadDirectoryAsync();

        sut.FindByName("Original").ShouldNotBeNull();
        sut.FindByName("NewChar").ShouldNotBeNull();
        sut.FindById(2).ShouldNotBeNull();
    }

    [Fact]
    public async Task LoadDirectoryAsync_removes_deleted_characters()
    {
        using var fixture = new Fixture();
        fixture.Seed(
            new Character { CharacterId = 1, AccountId = 42, SlotId = 0, Name = "Kept", Realm = 1 },
            new Character { CharacterId = 2, AccountId = 42, SlotId = 1, Name = "Deleted", Realm = 1 });

        var sut = fixture.CreateService();
        await sut.LoadDirectoryAsync();
        sut.FindByName("Deleted").ShouldNotBeNull();

        fixture.Mutate(db =>
        {
            var toDelete = db.Characters.Find(2u);
            if (toDelete != null) db.Characters.Remove(toDelete);
            db.SaveChanges();
        });

        await sut.LoadDirectoryAsync();

        sut.FindByName("Kept").ShouldNotBeNull();
        sut.FindByName("Deleted").ShouldBeNull();
        sut.FindById(2).ShouldBeNull();
    }

    [Fact]
    public async Task CharacterSummary_contains_expected_fields()
    {
        using var fixture = new Fixture();
        fixture.Seed(new Character
        {
            CharacterId = 7, AccountId = 42, SlotId = 0, Name = "TestChar",
            Realm = 2, Career = 4
        });

        var sut = fixture.CreateService();
        await sut.LoadDirectoryAsync();

        var summary = sut.FindById(7);
        summary.ShouldNotBeNull();
        summary.Value.CharacterId.ShouldBe(7u);
        summary.Value.Name.ShouldBe("TestChar");
        summary.Value.Realm.ShouldBe((byte)2);
        summary.Value.Career.ShouldBe((byte)4);
        summary.Value.AccountId.ShouldBe(42);
    }

    // ── Test infrastructure ───────────────────────────────────────────

    /// <summary>
    /// Creates an in-memory <see cref="CharacterDbContext"/> and provides
    /// an <see cref="IDbContextFactory{TContext}"/> wrapper for <see cref="CharacterService"/>.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        private readonly DbContextOptions<CharacterDbContext> _options;

        public Fixture()
        {
            _options = new DbContextOptionsBuilder<CharacterDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
        }

        /// <summary>Seeds characters into the in-memory database.</summary>
        public void Seed(params Character[] characters)
        {
            using var db = new CharacterDbContext(_options);
            db.Characters.AddRange(characters);
            db.SaveChanges();
        }

        /// <summary>Runs an arbitrary mutation against the database.</summary>
        public void Mutate(Action<CharacterDbContext> action)
        {
            using var db = new CharacterDbContext(_options);
            action(db);
        }

        /// <summary>Creates a <see cref="CharacterService"/> backed by the in-memory db.</summary>
        public CharacterService CreateService()
            => new(new InMemoryDbContextFactory(_options), new GameDataStore(), NullLogger.Instance);

        public void Dispose() { /* InMemory cleans up on GC */ }

        /// <summary>
        /// Minimal <see cref="IDbContextFactory{TContext}"/> that creates contexts
        /// using the shared in-memory options.
        /// </summary>
        private sealed class InMemoryDbContextFactory(DbContextOptions<CharacterDbContext> options)
            : IDbContextFactory<CharacterDbContext>
        {
            public CharacterDbContext CreateDbContext() => new(options);
        }
    }

    /// <summary>Null-pattern logger that discards all messages.</summary>
    private sealed class NullLogger : ILogger<CharacterService>
    {
        public static readonly NullLogger Instance = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
