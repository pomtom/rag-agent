// STEP 2 of ingestion: long text -> small pieces ("chunks").
//
// WHY CHUNK AT ALL
//   - An embedding describes the meaning of ONE piece of text. A whole 50-page manual squeezed
//     into one vector means "a bit of everything" and matches nothing well.
//   - The chat model only needs (and should only pay for) the few relevant pieces.
//
// HOW
//   1. Split the text into paragraphs (blank lines). A paragraph longer than ChunkSize is cut at a space.
//   2. Add paragraphs to the current chunk until the next one would not fit.
//   3. Start the next chunk with the last ChunkOverlap characters of the previous one.
//
// WHY OVERLAP
//   An answer can sit right on a chunk border ("...returns are accepted" | "within 30 days").
//   Repeating a little text at the start of the next chunk keeps that sentence whole somewhere.
//
// Sizes are in characters, not tokens, to keep it easy to follow. Roughly 4 characters = 1 token.

using System.Text;

namespace AgentRag.Api.Ingestion;

public sealed class TextChunker
{
    public const int ChunkSize = 2000;
    public const int ChunkOverlap = 200;

    private const string ParagraphSeparator = "\n\n";

    /// <summary>The chunks in document order. The position in the list is the chunk index (0, 1, 2...).</summary>
    public List<string> Split(string text)
    {
        List<string> pieces = SplitIntoPieces(text);
        List<string> chunks = new List<string>();

        StringBuilder current = new StringBuilder();
        bool currentHasNewText = false;   // false while "current" only holds the overlap from the previous chunk

        foreach (string piece in pieces)
        {
            // The next piece does not fit: close the current chunk and start a new one with the overlap.
            if (currentHasNewText && current.Length + ParagraphSeparator.Length + piece.Length > ChunkSize)
            {
                string finished = current.ToString();
                chunks.Add(finished);

                current.Clear();
                current.Append(GetTail(finished, ChunkOverlap));
                currentHasNewText = false;
            }

            if (current.Length > 0)
            {
                current.Append(ParagraphSeparator);
            }

            current.Append(piece);
            currentHasNewText = true;
        }

        if (currentHasNewText)
        {
            chunks.Add(current.ToString());
        }

        return chunks;
    }

    // Paragraphs, with any paragraph longer than the chunk size cut into chunk-size pieces.
    private static List<string> SplitIntoPieces(string text)
    {
        List<string> pieces = new List<string>();

        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] paragraphs = normalized.Split(ParagraphSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string paragraph in paragraphs)
        {
            string remaining = paragraph;
            while (remaining.Length > ChunkSize)
            {
                int cut = remaining.LastIndexOf(' ', ChunkSize);
                if (cut <= 0)
                {
                    cut = ChunkSize;   // one giant "word" (a URL, a long number): cut it anyway
                }

                pieces.Add(remaining.Substring(0, cut).Trim());
                remaining = remaining.Substring(cut).Trim();
            }

            if (remaining.Length > 0)
            {
                pieces.Add(remaining);
            }
        }

        return pieces;
    }

    // The last `length` characters, moved forward to the next space so no word is cut in half.
    private static string GetTail(string text, int length)
    {
        if (text.Length <= length)
        {
            return text;
        }

        int start = text.Length - length;
        int space = text.IndexOf(' ', start);
        if (space >= 0 && space < text.Length - 1)
        {
            start = space + 1;
        }

        return text.Substring(start);
    }
}
