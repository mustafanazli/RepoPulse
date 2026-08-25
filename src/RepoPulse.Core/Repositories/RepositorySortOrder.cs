namespace RepoPulse.Core.Repositories;

// Client-side-only ordering applied to an already-loaded repository list
// (RP-011) — never sent to GitHub. RP-009's list endpoint always requests
// sort=updated&direction=desc server-side; this enum only reorders what is
// already in memory, so UpdatedDescending is deliberately the default (it
// matches what the page already shows before the user touches anything).
public enum RepositorySortOrder
{
    UpdatedDescending,
    NameAscending
}
