using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Quotes;

/// <summary>
/// Chat quote database (streamer.bot staple): stores quotes per profile and
/// answers the "!quote" command family on every platform. Mirrors the
/// CurrencyCommandService / WatchtimeCommandService chat-pipeline contract —
/// <see cref="ProcessCommandAsync"/> returns the ready-to-send reply, or null
/// when the message is not a quote command (ChatViewModel falls through).
/// </summary>
public interface IQuoteService
{
    /// <summary>
    /// Processes a quote command (!quote, !quotecount) and returns the chat
    /// reply, or null when the message is not a quote command.
    /// </summary>
    /// <param name="command">Raw chat message text.</param>
    /// <param name="username">Sender's username (stored as AddedBy on !quote add).</param>
    /// <param name="platform">Originating platform (stored as the quote's Platform source).</param>
    /// <param name="isModerator">Sender's moderator status — gates add/del.</param>
    Task<string?> ProcessCommandAsync(string command, string username, string platform, bool isModerator);

    // ===== Management surface (Commands page QUOTES tab) =====
    Task<Quote> AddQuoteAsync(Quote quote);
    Task<Quote?> GetQuoteByNumberAsync(int number);
    Task<Quote?> GetRandomQuoteAsync();
    Task<IEnumerable<Quote>> SearchQuotesAsync(string searchText, int limit = 20);
    Task<IEnumerable<Quote>> GetQuotesAsync();
    Task<bool> DeleteQuoteAsync(int number);
    Task<int> GetQuoteCountAsync();
}
