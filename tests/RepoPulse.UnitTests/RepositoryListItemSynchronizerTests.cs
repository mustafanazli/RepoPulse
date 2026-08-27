using System.Collections.ObjectModel;
using System.Collections.Specialized;
using RepoPulse.Core.Repositories;

namespace RepoPulse.UnitTests;

// Covers RP-011's minimal-diff CollectionView synchronization: every
// transition RepositoryListPage relies on (insert/remove/move/filter-to-
// one/filter-to-zero/back-to-full-list) plus the case-sensitivity bug found
// in PR #13 review (identity must be FullName compared case-insensitively,
// never remove+reinsert or duplicate a repository that only changed
// casing). No MAUI host needed — ObservableCollection/RepositoryListItem
// are plain BCL/Core types.
public class RepositoryListItemSynchronizerTests
{
    private static GitHubRepository MakeRepository(string fullName, int stars = 0, string? description = null)
    {
        var parts = fullName.Split('/');
        return new GitHubRepository(
            parts[0],
            parts[1],
            fullName,
            description,
            $"https://github.com/{fullName}",
            stars,
            0,
            0,
            null,
            "main",
            false,
            false,
            null,
            null);
    }

    private static RepositoryListItem MakeItem(string fullName, int stars = 0, string? description = null) =>
        RepositoryListItem.FromRepository(MakeRepository(fullName, stars, description));

    [Fact]
    public void Sync_EmptyToPopulated_InsertsAllInOrder()
    {
        var current = new ObservableCollection<RepositoryListItem>();
        var desired = new List<RepositoryListItem> { MakeItem("owner/A"), MakeItem("owner/B") };

        RepositoryListItemSynchronizer.Sync(current, desired);

        Assert.Equal(new[] { "owner/A", "owner/B" }, current.Select(i => i.FullName));
    }

    [Fact]
    public void Sync_PopulatedToEmpty_RemovesAll()
    {
        var current = new ObservableCollection<RepositoryListItem> { MakeItem("owner/A"), MakeItem("owner/B") };

        RepositoryListItemSynchronizer.Sync(current, Array.Empty<RepositoryListItem>());

        Assert.Empty(current);
    }

    [Fact]
    public void Sync_FilterNarrowsToSingleItem_RemovesOthersKeepsMatch()
    {
        var current = new ObservableCollection<RepositoryListItem> { MakeItem("owner/A"), MakeItem("owner/B"), MakeItem("owner/C") };

        RepositoryListItemSynchronizer.Sync(current, new List<RepositoryListItem> { MakeItem("owner/B") });

        var remaining = Assert.Single(current);
        Assert.Equal("owner/B", remaining.FullName);
    }

    [Fact]
    public void Sync_ClearedFilterRestoresFullListInOriginalOrder()
    {
        var current = new ObservableCollection<RepositoryListItem> { MakeItem("owner/A"), MakeItem("owner/B"), MakeItem("owner/C") };
        RepositoryListItemSynchronizer.Sync(current, new List<RepositoryListItem> { MakeItem("owner/B") });

        RepositoryListItemSynchronizer.Sync(current, new List<RepositoryListItem> { MakeItem("owner/A"), MakeItem("owner/B"), MakeItem("owner/C") });

        Assert.Equal(new[] { "owner/A", "owner/B", "owner/C" }, current.Select(i => i.FullName));
    }

    [Fact]
    public void Sync_ReordersExistingItemsToMatchDesiredOrder()
    {
        var current = new ObservableCollection<RepositoryListItem> { MakeItem("owner/A"), MakeItem("owner/B"), MakeItem("owner/C") };

        RepositoryListItemSynchronizer.Sync(current, new List<RepositoryListItem> { MakeItem("owner/C"), MakeItem("owner/A"), MakeItem("owner/B") });

        Assert.Equal(new[] { "owner/C", "owner/A", "owner/B" }, current.Select(i => i.FullName));
    }

    [Fact]
    public void Sync_SameCasingNewInstance_UpdatesDisplayedDataInPlace()
    {
        var current = new ObservableCollection<RepositoryListItem> { MakeItem("owner/A", stars: 1) };

        RepositoryListItemSynchronizer.Sync(current, new List<RepositoryListItem> { MakeItem("owner/A", stars: 42) });

        var item = Assert.Single(current);
        Assert.Contains("42", item.StatsText);
    }

    [Fact]
    public void Sync_CasingOnlyChange_TreatedAsSameIdentity_NoDuplicateAndDataUpdated()
    {
        var current = new ObservableCollection<RepositoryListItem> { MakeItem("mustafanazli/RepoPulse", stars: 1) };

        RepositoryListItemSynchronizer.Sync(current, new List<RepositoryListItem> { MakeItem("MustafaNazli/repopulse", stars: 7) });

        var item = Assert.Single(current);
        Assert.Equal("MustafaNazli/repopulse", item.FullName);
        Assert.Contains("7", item.StatsText);
    }

    [Fact]
    public void Sync_DesiredContainsCasingVariantOfExistingItem_DoesNotCreateSecondEntry()
    {
        var current = new ObservableCollection<RepositoryListItem>
        {
            MakeItem("owner/A"),
            MakeItem("mustafanazli/RepoPulse")
        };

        RepositoryListItemSynchronizer.Sync(current, new List<RepositoryListItem>
        {
            MakeItem("owner/A"),
            MakeItem("MUSTAFANAZLI/REPOPULSE")
        });

        Assert.Equal(2, current.Count);
        Assert.Single(current, i => string.Equals(i.FullName, "MUSTAFANAZLI/REPOPULSE", StringComparison.Ordinal));
    }

    // RP-012: toggling a favorite is just another data change on an
    // already-present row (favorite state lives on RepositoryListItem
    // itself) — it must go through the same indexer-replace path as any
    // other in-place update, never a remove+reinsert and never a Reset.
    [Fact]
    public void Sync_FavoriteStateChangeOnExistingItem_ReplacesInPlaceWithoutMoveOrReset()
    {
        var current = new ObservableCollection<RepositoryListItem> { MakeItem("owner/A"), MakeItem("owner/B") };
        var actions = new List<NotifyCollectionChangedAction>();
        current.CollectionChanged += (_, e) => actions.Add(e.Action);

        var repository = MakeRepository("owner/A");
        var favoritedA = RepositoryListItem.FromRepository(repository, isFavorite: true);
        RepositoryListItemSynchronizer.Sync(current, new List<RepositoryListItem> { favoritedA, MakeItem("owner/B") });

        Assert.Equal(new[] { "owner/A", "owner/B" }, current.Select(i => i.FullName));
        Assert.True(current[0].IsFavorite);
        Assert.False(current[1].IsFavorite);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Move, actions);
    }

    // The generic Sync<TItem> overload (introduced for RP-012's mixed
    // RepositoryListItem/FavoriteIdentityRow "Favoriler" rows) must behave
    // identically to the RepositoryListItem-specific overload above for the
    // same scenario — same insert/remove/reorder/no-Reset guarantees, just
    // with an explicit key selector instead of the implicit FullName one.
    [Fact]
    public void GenericSync_MixedObjectRows_SyncsByExplicitKeySelectorWithoutReset()
    {
        var current = new ObservableCollection<object>();
        var resetRaised = false;
        current.CollectionChanged += (_, e) => resetRaised |= e.Action == NotifyCollectionChangedAction.Reset;

        static string KeyOf(object row) => row switch
        {
            RepositoryListItem item => item.FullName,
            string identity => identity,
            _ => throw new InvalidOperationException()
        };

        RepositoryListItemSynchronizer.Sync(current, new List<object> { MakeItem("owner/A"), "owner/Offline" }, KeyOf);
        Assert.Equal(2, current.Count);

        RepositoryListItemSynchronizer.Sync(current, new List<object> { "owner/Offline" }, KeyOf);
        var remaining = Assert.Single(current);
        Assert.Equal("owner/Offline", remaining);

        Assert.False(resetRaised);
    }

    [Fact]
    public void Sync_NeverRaisesResetAction()
    {
        var current = new ObservableCollection<RepositoryListItem>();
        var resetRaised = false;
        current.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                resetRaised = true;
            }
        };

        // Exercises insert, reorder, filter-to-one, filter-to-zero, and
        // back-to-full-list — the exact sequence a live search+sort session
        // produces — checking that none of it ever raises Reset, which is
        // what dropped the SearchBar's IME focus in the bug this fixes.
        RepositoryListItemSynchronizer.Sync(current, new List<RepositoryListItem> { MakeItem("owner/A"), MakeItem("owner/B"), MakeItem("owner/C") });
        RepositoryListItemSynchronizer.Sync(current, new List<RepositoryListItem> { MakeItem("owner/C"), MakeItem("owner/A"), MakeItem("owner/B") });
        RepositoryListItemSynchronizer.Sync(current, new List<RepositoryListItem> { MakeItem("owner/A") });
        RepositoryListItemSynchronizer.Sync(current, Array.Empty<RepositoryListItem>());
        RepositoryListItemSynchronizer.Sync(current, new List<RepositoryListItem> { MakeItem("owner/A"), MakeItem("owner/B"), MakeItem("owner/C") });

        Assert.False(resetRaised);
    }
}
