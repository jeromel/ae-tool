namespace AeTool.Connectors;

/// <summary>
/// Plafond de décompression d'un fichier reçu (draw.io compressé, classeur xlsx). Un fichier piégé de quelques Mo peut se
/// décompresser en plusieurs Go et saturer la mémoire : tout flux décompressé est lu à travers ce budget, partagé par
/// toutes les parties d'un même fichier (pages d'un dessin, fichiers d'un classeur). Au-delà, la lecture est refusée.
/// Voir docs/securite.md, risque « Décompression sans plafond ».
/// </summary>
public sealed class DecompressionBudget
{
    /// <summary>50 Mo décompressés par fichier, valeur fixée par la revue de maintenabilité 0.15 (Colbert).</summary>
    public const long DefaultLimit = 50L * 1024 * 1024;

    public long Limit { get; }
    private long _used;

    public DecompressionBudget(long limit = DefaultLimit) => Limit = limit;

    /// <summary>Enveloppe un flux décompressé : chaque octet lu est décompté du budget.</summary>
    public Stream Wrap(Stream decompressed) => new BoundedStream(decompressed, this);

    private void Consume(int count)
    {
        _used += count;
        if (_used > Limit)
        {
            var e = new InvalidDataException($"Fichier refusé : son contenu décompressé dépasse {Limit / (1024 * 1024)} Mo.");
            e.Data[LimitMarker] = true;
            throw e;
        }
    }

    private const string LimitMarker = "ae.plafond-decompression";

    /// <summary>Vrai pour un refus de plafond : distinct d'un fichier simplement corrompu, son message doit parvenir tel quel.</summary>
    public static bool IsLimit(Exception e) => e.Data.Contains(LimitMarker);

    private sealed class BoundedStream(Stream inner, DecompressionBudget budget) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            budget.Consume(n);
            return n;
        }

        public override int Read(Span<byte> buffer)
        {
            var n = inner.Read(buffer);
            budget.Consume(n);
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}

