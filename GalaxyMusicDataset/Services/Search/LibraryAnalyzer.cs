using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;

namespace GalaxyMusicDataset.Services.Search;

/// <summary>
/// Lowercases and ASCII-folds tokens without English stop words, so short words like
/// "to" in titles remain searchable.
/// </summary>
internal sealed class LibraryAnalyzer(LuceneVersion version) : Analyzer
{
    protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
    {
        var tokenizer = new StandardTokenizer(version, reader);
        TokenStream stream = new LowerCaseFilter(version, tokenizer);
        stream = new ASCIIFoldingFilter(stream);
        return new TokenStreamComponents(tokenizer, stream);
    }
}
