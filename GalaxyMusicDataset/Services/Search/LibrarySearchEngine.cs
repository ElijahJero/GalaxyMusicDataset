using GalaxyMusicDataset.Services.Normalization;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace GalaxyMusicDataset.Services.Search;

public sealed class LibrarySearchEngine : IDisposable
{
    public const LuceneVersion Version = LuceneVersion.LUCENE_48;

    private readonly LibraryAnalyzer _analyzer = new(Version);
    private readonly object _gate = new();
    private RAMDirectory _directory = new();
    private DirectoryReader? _reader;
    private IndexSearcher? _searcher;

    public void Rebuild(IEnumerable<LibrarySearchDocument> documents)
    {
        var next = new RAMDirectory();
        var config = new IndexWriterConfig(Version, _analyzer)
        {
            OpenMode = OpenMode.CREATE
        };
        using (var writer = new IndexWriter(next, config))
        {
            foreach (var document in documents)
            {
                writer.AddDocument(ToLuceneDocument(document));
            }

            writer.Commit();
        }

        var reader = DirectoryReader.Open(next);
        lock (_gate)
        {
            _searcher = new IndexSearcher(reader);
            _reader?.Dispose();
            _directory.Dispose();
            _reader = reader;
            _directory = next;
        }
    }

    public IReadOnlyList<long> Search(string? q, string? artist, string? title, string? album)
    {
        var query = BuildQuery(q, artist, title, album);
        if (query is null)
        {
            return [];
        }

        lock (_gate)
        {
            if (_searcher is null)
            {
                return [];
            }

            var searcher = _searcher;
            var limit = Math.Max(1, searcher.IndexReader.NumDocs);
            var hits = searcher.Search(query, limit).ScoreDocs;
            var ids = new long[hits.Length];
            for (var i = 0; i < hits.Length; i++)
            {
                var doc = searcher.Doc(hits[i].Doc);
                ids[i] = long.Parse(doc.Get("id"));
            }

            return ids;
        }
    }

    private Query? BuildQuery(string? q, string? artist, string? title, string? album)
    {
        var root = new BooleanQuery();
        if (!AddClause(root, "text", q)
            || !AddClause(root, "artist", artist)
            || !AddClause(root, "title", title)
            || !AddClause(root, "album", album))
        {
            return null;
        }

        return root.Clauses.Count == 0 ? null : root;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _reader?.Dispose();
            _directory.Dispose();
            _analyzer.Dispose();
            _searcher = null;
            _reader = null;
        }
    }

    private bool AddClause(BooleanQuery root, string field, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var clause = BuildFieldQuery(field, raw);
        if (clause is null)
        {
            return false;
        }

        root.Add(clause, Occur.MUST);
        return true;
    }

    private Query? BuildFieldQuery(string field, string raw)
    {
        var tokens = Analyze(raw);
        if (tokens.Count == 0)
        {
            return null;
        }

        var query = new BooleanQuery();
        foreach (var token in tokens)
        {
            query.Add(BuildTokenQuery(field, token), Occur.MUST);
        }

        return query;
    }

    private static Query BuildTokenQuery(string field, string token)
    {
        var term = new Term(field, token);
        var query = new BooleanQuery { MinimumNumberShouldMatch = 1 };
        query.Add(new TermQuery(term) { Boost = 3f }, Occur.SHOULD);
        if (token.Length >= 2)
        {
            query.Add(new PrefixQuery(term) { Boost = 2f }, Occur.SHOULD);
        }

        if (token.Length >= 6)
        {
            query.Add(new FuzzyQuery(term, 2) { Boost = 0.6f }, Occur.SHOULD);
        }
        else if (token.Length >= 4)
        {
            query.Add(new FuzzyQuery(term, 1) { Boost = 0.6f }, Occur.SHOULD);
        }

        return query;
    }

    private IReadOnlyList<string> Analyze(string text)
    {
        using var stream = _analyzer.GetTokenStream("text", text);
        stream.Reset();
        var term = stream.GetAttribute<Lucene.Net.Analysis.TokenAttributes.ICharTermAttribute>();
        var tokens = new List<string>();
        while (stream.IncrementToken())
        {
            var value = term.ToString();
            if (value.Length > 0)
            {
                tokens.Add(value);
            }
        }

        stream.End();
        return tokens;
    }

    private static Document ToLuceneDocument(LibrarySearchDocument document)
    {
        var artistParts = new List<string?> { document.ArtistName };
        artistParts.AddRange(document.Aliases.Select(static a => (string?)a));

        var lucene = new Document
        {
            new StringField("id", document.TrackId.ToString(), Field.Store.YES),
            new TextField("title", Searchable(document.Title), Field.Store.NO),
            new TextField("artist", Searchable(artistParts), Field.Store.NO),
            new TextField("album", Searchable(document.AlbumTitle), Field.Store.NO),
            new TextField("text", Searchable([document.Title, document.ArtistName, document.AlbumTitle, ..document.Aliases]), Field.Store.NO)
        };
        return lucene;
    }

    private static string Searchable(params string?[] parts) => Searchable((IEnumerable<string?>)parts);

    private static string Searchable(IEnumerable<string?> parts)
    {
        var values = new List<string>();
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            values.Add(part);
            var romanized = TextNormalizer.RomanizeIfKana(part);
            if (!string.IsNullOrEmpty(romanized))
            {
                values.Add(romanized);
            }
        }

        return string.Join(" ", values);
    }
}
