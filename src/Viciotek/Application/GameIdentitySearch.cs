using Viciotek.Domain;

namespace Viciotek.Application;

public sealed record SteamGridGame(int Id, string Name);

public interface ISteamGridDbGames
{
    Task<SteamGridGame?> GetSteamGameAsync(int appId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SteamGridGame>> SearchGamesAsync(string term, CancellationToken cancellationToken);
}

public sealed record GameIdentityMatch(SteamGridGame? Confirmed, string? SearchName, IReadOnlyList<SteamGridGame> Choices);

public sealed class GameIdentitySearch(ISteamGridDbGames provider)
{
    public async Task<GameIdentityMatch> FindAsync(Game game, CancellationToken cancellationToken)
    {
        if (game.SteamGridDbGameId is > 0)
            return new(new(game.SteamGridDbGameId.Value, game.SteamGridDbName ?? game.Name), game.CoverSearchName ?? game.Name, []);

        if (game.SteamAppId is > 0 && !game.IsNameCustomized)
        {
            var steamGame = await provider.GetSteamGameAsync(game.SteamAppId.Value, cancellationToken).ConfigureAwait(false);
            if (steamGame is not null) return new(steamGame, steamGame.Name, []);
        }

        var candidates = game.IsNameCustomized ? new[] { game.Name } :
            new[] { game.CoverSearchName }.Concat(game.IdentityCandidates).Concat(
                new[] { game.Name, game.ExecutableName is null ? null : Path.GetFileNameWithoutExtension(game.ExecutableName) });
        var queries = candidates.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => GameTitle.Clean(value!)).Where(GameTitle.IsDescriptive)
            .DistinctBy(GameTitle.Normalize).Take(3);
        var choices = new Dictionary<int, SteamGridGame>();
        string? lastQuery = null;
        foreach (var query in queries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = await provider.SearchGamesAsync(query, cancellationToken).ConfigureAwait(false);
            var exact = ExactMatches(query, results);
            if (exact.Count == 1 && !GameTitle.IsAbbreviation(query)) return new(exact[0], query, []);
            // Do not downgrade an ambiguous title to a less reliable executable name.
            if (exact.Count > 1) return new(null, query, results);
            var normalized = GameTitle.Normalize(query);
            if (results.Any(result => GameTitle.Normalize(result.Name).StartsWith(normalized + " ", StringComparison.Ordinal) ||
                normalized.StartsWith(GameTitle.Normalize(result.Name) + " ", StringComparison.Ordinal)))
                return new(null, query, results);
            foreach (var result in results) choices.TryAdd(result.Id, result);
            lastQuery = query;
        }
        return new(null, lastQuery, choices.Values.ToList());
    }

    public static IReadOnlyList<SteamGridGame> ExactMatches(string query, IReadOnlyList<SteamGridGame> results)
    {
        var normalized = GameTitle.Normalize(query);
        return results.Where(result => GameTitle.Normalize(result.Name) == normalized).DistinctBy(result => result.Id).ToList();
    }
}
