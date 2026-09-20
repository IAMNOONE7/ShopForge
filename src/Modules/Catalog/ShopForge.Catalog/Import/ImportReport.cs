namespace ShopForge.Catalog.Import;

internal sealed record ImportReport(
    int Created,
    int Updated,
    int Skipped,
    int Invalid,
    int Failed,
    List<string> IgnoredColumns,
    List<ImportIssue> Issues);

internal sealed record ImportIssue(int Row, string? Column, string Message);
