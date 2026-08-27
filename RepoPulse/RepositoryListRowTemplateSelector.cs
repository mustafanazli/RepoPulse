using RepoPulse.Core.Repositories;

namespace RepoPulse;

// RP-012: RepositoryCollectionView's "Favoriler" view mixes two row shapes
// in one ObservableCollection<object> — a RepositoryListItem when the
// favorite is present in the currently loaded live list, or a
// FavoriteIdentityRow when it isn't (offline, or simply not on this
// session's list) — while "Tümü" only ever contains RepositoryListItem.
// Both cases share the same single CollectionView (never a nested/second
// one), so virtualization is unaffected either way.
public sealed class RepositoryListRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? RepositoryTemplate { get; set; }

    public DataTemplate? FavoriteIdentityTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) => item switch
    {
        RepositoryListItem => RepositoryTemplate ?? throw new InvalidOperationException($"{nameof(RepositoryTemplate)} was not set."),
        FavoriteIdentityRow => FavoriteIdentityTemplate ?? throw new InvalidOperationException($"{nameof(FavoriteIdentityTemplate)} was not set."),
        _ => throw new InvalidOperationException($"Unknown repository list row type: {item.GetType()}.")
    };
}
