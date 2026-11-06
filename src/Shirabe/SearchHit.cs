namespace Shirabe
{
    /// <summary>検索結果の 1 行。</summary>
    public sealed class SearchHit
    {
        public SearchHit(string title, string snippet, string url)
        {
            Title = title ?? string.Empty;
            Snippet = snippet ?? string.Empty;
            Url = url ?? string.Empty;
        }

        public string Title { get; }
        public string Snippet { get; }
        public string Url { get; }
    }
}
