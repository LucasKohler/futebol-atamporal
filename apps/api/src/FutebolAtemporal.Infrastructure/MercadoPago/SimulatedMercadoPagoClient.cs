using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FutebolAtemporal.Application.Payments;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FutebolAtemporal.Infrastructure.MercadoPago;

public sealed class SimulatedMercadoPagoStore
{
    internal sealed class Record
    {
        public required string MpPaymentId { get; init; }
        public string? ExternalReference { get; init; }
        public decimal? Amount { get; set; }
        public string Status { get; set; } = "pending";
        public DateTimeOffset? DateApproved { get; set; }
    }

    private readonly ConcurrentDictionary<string, Record> _items = new(StringComparer.Ordinal);

    internal Record Create(MpCreatePixPaymentRequest request)
    {
        // A chave da tentativa ? ?nica no banco e continua a mesma entre processos.
        var id = $"sim-{Guid.Parse(request.IdempotencyKey):N}";
        return _items.GetOrAdd(id, _ => new Record
        {
            MpPaymentId = id,
            ExternalReference = request.ExternalReference,
            Amount = request.Amount
        });
    }

    internal Record? Get(string mpPaymentId) => _items.GetValueOrDefault(mpPaymentId);

    public void SetAmountForTest(string mpPaymentId, decimal? amount)
    {
        if (_items.TryGetValue(mpPaymentId, out var rec))
            rec.Amount = amount;
    }

    internal void SetStatus(string mpPaymentId, string status, DateTimeOffset? approvedAt)
    {
        _items.AddOrUpdate(
            mpPaymentId,
            _ => new Record
            {
                MpPaymentId = mpPaymentId,
                Status = status,
                DateApproved = approvedAt ?? (status == "approved" ? DateTimeOffset.UtcNow : null)
            },
            (_, existing) =>
            {
                existing.Status = status;
                existing.DateApproved = approvedAt ?? (status == "approved" ? DateTimeOffset.UtcNow : existing.DateApproved);
                return existing;
            });
    }
}

public sealed class SimulatedMercadoPagoClient : IMercadoPagoClient, ISimulatedMercadoPagoControl
{
    private readonly SimulatedMercadoPagoStore _store;
    private readonly AppDbContext _db;

    public SimulatedMercadoPagoClient(SimulatedMercadoPagoStore store, AppDbContext db)
    {
        _store = store;
        _db = db;
    }

    public string Mode => "Simulator";
    public bool IsConfigured => true;
    public bool IsSimulated => true;

    public Task<MpPixPaymentResult> CreatePixPaymentAsync(MpCreatePixPaymentRequest request, CancellationToken ct = default)
    {
        var rec = _store.Create(request);
        var amountStr = request.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        var qrCode = $"00020126580014br.gov.bcb.pix0136{rec.MpPaymentId}52040000530398654{amountStr.Length:00}{amountStr}5802BR5917FUTEBOL ATEMPORAL6009SAO PAULO62070503***6304SIMU";
        var qrBase64 = BuildDeterministicQrPngBase64(qrCode);

        return Task.FromResult(new MpPixPaymentResult(
            rec.MpPaymentId,
            "pending",
            qrCode,
            qrBase64,
            request.ExpiresAt.ToUniversalTime(),
            IsSimulated: true));
    }

    public async Task<MpPixPaymentStatus> GetPaymentAsync(string mpPaymentId, CancellationToken ct = default)
    {
        var record = _store.Get(mpPaymentId);
        if (record?.ExternalReference is not null)
            return ToPaymentStatus(mpPaymentId, record.Status, record.ExternalReference, record.Amount, record.DateApproved);

        var charge = await _db.PixCharges.AsNoTracking()
            .FirstOrDefaultAsync(c => c.IsSimulated && c.MpPaymentId == mpPaymentId, ct);
        var reference = charge?.MembershipPaymentId is Guid paymentId
            ? $"membership:{paymentId:D}"
            : charge?.MatchFeePaymentId is Guid feeId ? $"matchday:{feeId:D}" : null;
        var persistedStatus = charge?.ApprovedAt is not null ? "approved" : "pending";
        return ToPaymentStatus(mpPaymentId, record?.Status ?? persistedStatus,
            reference, charge?.Amount, record?.DateApproved ?? charge?.ApprovedAt);
    }

    private static MpPixPaymentStatus ToPaymentStatus(
        string id, string status, string? reference, decimal? amount, DateTimeOffset? approvedAt)
        => new(id, status, status == "approved" ? "accredited" : "pending_waiting_transfer", reference, amount, approvedAt);

    public void SetStatus(string mpPaymentId, string status, DateTimeOffset? approvedAt = null)
        => _store.SetStatus(mpPaymentId, status, approvedAt);

    /// <summary>Gera um PNG válido 126x126 em C# puro (padrão visual QR determinístico) para sandbox/dev.</summary>
    private static string BuildDeterministicQrPngBase64(string seed)
    {
        const int modules = 21;
        const int scale = 6;
        const int size = modules * scale;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var grid = new bool[modules, modules];

        void Finder(int ox, int oy)
        {
            for (var y = 0; y < 7; y++)
            for (var x = 0; x < 7; x++)
            {
                var border = x == 0 || x == 6 || y == 0 || y == 6;
                var center = x >= 2 && x <= 4 && y >= 2 && y <= 4;
                grid[oy + y, ox + x] = border || center;
            }
        }

        Finder(0, 0);
        Finder(modules - 7, 0);
        Finder(0, modules - 7);

        for (var y = 0; y < modules; y++)
        for (var x = 0; x < modules; x++)
        {
            var inFinder = (x < 8 && y < 8) || (x >= modules - 8 && y < 8) || (x < 8 && y >= modules - 8);
            if (inFinder) continue;
            var bitIdx = (y * modules + x) % (hash.Length * 8);
            grid[y, x] = ((hash[bitIdx / 8] >> (bitIdx % 8)) & 1) == 1;
        }

        var raw = new byte[size * (size + 1)];
        for (var y = 0; y < size; y++)
        {
            var rowStart = y * (size + 1);
            raw[rowStart] = 0; // filter None
            var my = y / scale;
            for (var x = 0; x < size; x++)
            {
                raw[rowStart + 1 + x] = grid[my, x / scale] ? (byte)0x11 : (byte)0xFF;
            }
        }

        using var ms = new MemoryStream();
        ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), size);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), size);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 0; // grayscale
        WriteChunk(ms, "IHDR", ihdr);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(raw, 0, raw.Length);
        WriteChunk(ms, "IDAT", compressed.ToArray());
        WriteChunk(ms, "IEND", Array.Empty<byte>());

        return Convert.ToBase64String(ms.ToArray());
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        stream.Write(len);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in type) crc = Step(crc, b);
        foreach (var b in data) crc = Step(crc, b);
        return ~crc;

        static uint Step(uint c, byte b)
        {
            c ^= b;
            for (var i = 0; i < 8; i++)
                c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320u : (c >> 1);
            return c;
        }
    }
}