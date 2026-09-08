namespace GalaxyMusicDataset.Services.Search;

public sealed record LibrarySearchDocument(
    long TrackId,
    string Title,
    string ArtistName,
    string? AlbumTitle,
    IReadOnlyList<string> Aliases);
