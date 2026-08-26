namespace RepoPulse.Infrastructure.Favorites;

// Deliberately just an absolute file path — RepoPulse.Infrastructure never
// references FileSystem.AppDataDirectory or any other MAUI API. The MAUI
// app (MauiProgram) is the only place that resolves the real on-device path
// and passes it in; RepoPulse.UnitTests passes a temp-file path instead,
// with no MAUI host involved at all.
public sealed record SqliteFavoriteRepositoryStoreOptions(string DatabasePath);
