using System.Collections.ObjectModel;

namespace RepoPulse.Core.Repositories;

// Pure, MAUI-independent minimal-diff synchronizer for keeping an
// ObservableCollection<RepositoryListItem> in step with a freshly computed
// desired list (RP-011's live search/sort). Only Remove/Insert/Move/
// indexer-replace are ever used — never Clear() — so no call here can raise
// a Reset. That matters beyond tidiness: MAUI's Android CollectionView
// hosts CollectionView.Header (where the search box lives) inside the same
// RecyclerView as the items, so a Reset forces a full adapter invalidation
// that tears down the header and drops the SearchBar's IME focus mid-
// keystroke — the exact bug this type exists to keep from coming back.
//
// Identity is GitHubRepository.FullName, compared case-insensitively. A
// repository's FullName is stable in the vast majority of loads, but an
// owner or repository rename between two loads can change its casing while
// it is still the same repository (RP-009's own list-fetch already treats
// FullName as case-insensitive-unique for dedupe) — a casing-only change
// must update the existing row in place, never remove+reinsert it or leave
// a duplicate behind.
public static class RepositoryListItemSynchronizer
{
    public static void Sync(ObservableCollection<RepositoryListItem> current, IReadOnlyList<RepositoryListItem> desired)
    {
        for (var i = current.Count - 1; i >= 0; i--)
        {
            if (!desired.Any(item => AreSameIdentity(item.FullName, current[i].FullName)))
            {
                current.RemoveAt(i);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var item = desired[i];
            var currentIndex = IndexOf(current, item.FullName);

            if (currentIndex == -1)
            {
                current.Insert(i, item);
            }
            else if (currentIndex != i)
            {
                current.Move(currentIndex, i);
                current[i] = item;
            }
            else
            {
                current[i] = item;
            }
        }
    }

    private static int IndexOf(ObservableCollection<RepositoryListItem> current, string fullName)
    {
        for (var i = 0; i < current.Count; i++)
        {
            if (AreSameIdentity(current[i].FullName, fullName))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool AreSameIdentity(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
