using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Quotes;

/// <summary>
/// Handles the !quote command family (streamer.bot staple) on top of the
/// per-profile Quotes table. Invoked from the ChatViewModel chat pipeline
/// exactly like CurrencyCommandService/WatchtimeCommandService: the returned
/// string is sent on the ORIGINATING platform (Twitch/Kick/YouTube/Discord)
/// by the caller; returning null lets other handlers process the message.
///
/// Commands:
///   !quote                     — random quote (everyone)
///   !quote &lt;n&gt;                — quote by number (everyone)
///   !quote search &lt;text&gt;       — substring search (everyone)
///   !quote add &lt;text&gt;          — add a quote (moderator+)
///   !quote del|remove|delete &lt;n&gt; — delete a quote (moderator+)
///   !quotecount                — total count (everyone)
/// </summary>
public class QuoteService : IQuoteService
{
    /// <summary>Quotes shown per !quote search reply, to keep chat replies short.</summary>
    private const int SearchReplyLimit = 3;

    /// <summary>Character cap per quote excerpt in search replies.</summary>
    private const int ExcerptLength = 60;

    private readonly ILogger<QuoteService> _logger;
    private readonly IDataService _dataService;

    public QuoteService(ILogger<QuoteService> logger, IDataService dataService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
    }

    /// <inheritdoc />
    public async Task<string?> ProcessCommandAsync(string command, string username, string platform, bool isModerator)
    {
        if (string.IsNullOrWhiteSpace(command) || !command.StartsWith("!"))
            return null;

        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();

        if (cmd != "!quote" && cmd != "!quotecount")
            return null; // Not a quote command — let the pipeline continue

        try
        {
            if (cmd == "!quotecount")
            {
                return await HandleQuoteCountAsync();
            }

            // !quote <sub> ...
            if (parts.Length > 1)
            {
                var sub = parts[1].ToLowerInvariant();

                if (sub is "add")
                    return isModerator
                        ? await HandleAddAsync(parts, username, platform)
                        : "You must be a moderator to add quotes";

                if (sub is "del" or "remove" or "delete")
                    return isModerator
                        ? await HandleDeleteAsync(parts)
                        : "You must be a moderator to delete quotes";

                if (sub is "search" or "find")
                    return await HandleSearchAsync(parts);

                if (sub is "help")
                    return "Usage: !quote (random) • !quote <number> • !quote search <text> • mods: !quote add <text>, !quote del <number> • !quotecount";

                // !quote <n> — number lookup
                if (int.TryParse(parts[1], out var number))
                    return await HandleByNumberAsync(number);
            }

            // !quote — random
            return await HandleRandomAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing quote command: {Command}", command);
            return "An error occurred while processing the quote command.";
        }
    }

    private async Task<string> HandleRandomAsync()
    {
        var count = await _dataService.GetQuoteCountAsync();
        if (count == 0)
        {
            return "No quotes yet — a moderator can add one with !quote add <text>";
        }

        var quote = await _dataService.GetRandomQuoteAsync();
        return quote == null
            ? "No quotes yet — a moderator can add one with !quote add <text>"
            : FormatQuote(quote);
    }

    private async Task<string> HandleByNumberAsync(int number)
    {
        if (number <= 0)
        {
            return "Quote numbers start at 1.";
        }

        var quote = await _dataService.GetQuoteByNumberAsync(number);
        return quote == null
            ? $"Quote #{number} not found ({await _dataService.GetQuoteCountAsync()} quote(s) in the database)"
            : FormatQuote(quote);
    }

    private async Task<string> HandleAddAsync(string[] parts, string username, string platform)
    {
        var text = string.Join(' ', parts.Skip(2)).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Usage: !quote add <text>";
        }

        var quote = await _dataService.AddQuoteAsync(new Quote
        {
            Text = text,
            AddedBy = username,
            Platform = platform
        });

        _logger.LogInformation("Added quote #{Number} on {Platform} by {Username}", quote.Number, platform, username);
        return $"Quote #{quote.Number} added by {username}.";
    }

    private async Task<string> HandleDeleteAsync(string[] parts)
    {
        if (parts.Length < 3 || !int.TryParse(parts[2], out var number) || number <= 0)
        {
            return "Usage: !quote del <number>";
        }

        var deleted = await _dataService.DeleteQuoteAsync(number);
        if (!deleted)
        {
            return $"Quote #{number} not found";
        }

        _logger.LogInformation("Deleted quote #{Number}", number);
        return $"Quote #{number} deleted.";
    }

    private async Task<string> HandleSearchAsync(string[] parts)
    {
        var query = string.Join(' ', parts.Skip(2)).Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return "Usage: !quote search <text>";
        }

        var matches = (await _dataService.SearchQuotesAsync(query)).ToList();
        if (matches.Count == 0)
        {
            return $"No quotes matching '{query}'";
        }

        var total = await _dataService.GetQuoteCountAsync();
        var excerpts = matches.Take(SearchReplyLimit)
            .Select(q => $"#{q.Number} \"{Excerpt(q.Text)}\"");
        return $"Found {matches.Count}/{total}: {string.Join(" | ", excerpts)}";
    }

    private async Task<string> HandleQuoteCountAsync()
    {
        var count = await _dataService.GetQuoteCountAsync();
        return count == 1
            ? "There is 1 quote in the database."
            : $"There are {count} quotes in the database.";
    }

    /// <summary>Chat-ready rendering of one quote (streamer.bot format).</summary>
    private static string FormatQuote(Quote quote)
    {
        var source = string.IsNullOrWhiteSpace(quote.AddedBy)
            ? ""
            : $" — added by {quote.AddedBy}";
        return $"Quote #{quote.Number}: \"{quote.Text}\"{source}";
    }

    private static string Excerpt(string text)
    {
        return text.Length <= ExcerptLength ? text : text[..(ExcerptLength - 1)] + "…";
    }

    // ===== Management surface (Commands page QUOTES tab) =====

    /// <inheritdoc />
    public Task<Quote> AddQuoteAsync(Quote quote) => _dataService.AddQuoteAsync(quote);

    /// <inheritdoc />
    public Task<Quote?> GetQuoteByNumberAsync(int number) => _dataService.GetQuoteByNumberAsync(number);

    /// <inheritdoc />
    public Task<Quote?> GetRandomQuoteAsync() => _dataService.GetRandomQuoteAsync();

    /// <inheritdoc />
    public Task<IEnumerable<Quote>> SearchQuotesAsync(string searchText, int limit = 20) =>
        _dataService.SearchQuotesAsync(searchText, limit);

    /// <inheritdoc />
    public Task<IEnumerable<Quote>> GetQuotesAsync() => _dataService.GetQuotesAsync();

    /// <inheritdoc />
    public Task<bool> DeleteQuoteAsync(int number) => _dataService.DeleteQuoteAsync(number);

    /// <inheritdoc />
    public Task<int> GetQuoteCountAsync() => _dataService.GetQuoteCountAsync();
}
