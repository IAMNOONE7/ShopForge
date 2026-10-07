namespace ShopForge.Shared.Stores;

// What shoppers looked for and whether the shop had it. Stage 33 turns this into "the hundred searches that
// found nothing", which is the most actionable report a catalogue has; until then it is only collected.
//
// The term and the number of answers, and nothing about who typed it: no customer, no session, no address. A
// search is a question about the catalogue rather than a fact about a person (D-178).
public interface ISearchLog
{
    Task RecordAsync(string terms, int found, CancellationToken cancellationToken);
}
