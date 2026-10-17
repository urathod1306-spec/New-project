using System.Security.Cryptography;

namespace NabconsIntranet.Infrastructure;

public sealed record StoredFile(string StoredPath, string OriginalFileName, string ContentType, long SizeBytes, string Sha256);

public sealed class UploadRejectedException(string message) : Exception(message);

/// <summary>
/// Stores uploaded files in the protected documents folder (outside the web root).
/// Files are renamed to generated IDs, checked against an extension allow-list, a size limit
/// and their real file signature. They can only be read back through authorised download pages.
/// </summary>
public sealed class FileStore
{
    private readonly IntranetOptions _o;

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".doc"] = "application/msword",
        [".xls"] = "application/vnd.ms-excel",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".zip"] = "application/zip",
    };

    /// <summary>Business-lead attachments (fixed list, independent of the circular upload settings).</summary>
    public static readonly string[] LeadExtensions = { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".jpg", ".jpeg", ".png", ".zip" };

    public FileStore(IntranetOptions o) => _o = o;

    public string AllowedList => string.Join(", ", _o.AllowedUploadExtensions);
    public string AcceptAttribute => string.Join(",", _o.AllowedUploadExtensions);

    public Task<StoredFile> SaveAsync(IFormFile file, string area) => SaveAsync(file, area, _o.AllowedUploadExtensions, _o.MaxUploadBytes);

    public async Task<StoredFile> SaveAsync(IFormFile file, string area, IReadOnlyCollection<string> allowed, long maxBytes)
    {
        var original = Path.GetFileName(file.FileName ?? "").Trim();
        if (string.IsNullOrEmpty(original)) throw new UploadRejectedException("No file name supplied.");
        if (original.Length > 200) original = original[^200..];
        var ext = Path.GetExtension(original).ToLowerInvariant();
        if (!allowed.Contains(ext) || !ContentTypes.ContainsKey(ext))
            throw new UploadRejectedException($"File type '{ext}' is not allowed. Allowed: {string.Join(", ", allowed)}.");
        if (file.Length == 0) throw new UploadRejectedException("The file is empty.");
        if (file.Length > maxBytes)
            throw new UploadRejectedException($"The file is larger than the {maxBytes / 1024 / 1024} MB limit.");

        var header = new byte[8192];
        int read;
        await using (var s = file.OpenReadStream())
            read = await s.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        if (!SignatureMatches(ext, header.AsSpan(0, read)))
            throw new UploadRejectedException($"The file content does not match a genuine {ext} file.");

        var folder = Path.Combine(_o.DocumentsPath, area, DateTime.UtcNow.ToString("yyyy"));
        Directory.CreateDirectory(folder);
        var storedName = Guid.NewGuid().ToString("N") + ".dat";
        var full = Path.Combine(folder, storedName);

        string sha;
        await using (var input = file.OpenReadStream())
        await using (var output = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            int n;
            while ((n = await input.ReadAsync(buffer)) > 0)
            {
                hasher.AppendData(buffer, 0, n);
                await output.WriteAsync(buffer.AsMemory(0, n));
            }
            sha = Convert.ToHexString(hasher.GetHashAndReset());
        }
        var relative = Path.GetRelativePath(_o.DocumentsPath, full).Replace('\\', '/');
        return new StoredFile(relative, original, ContentTypes[ext], file.Length, sha);
    }

    /// <summary>Saves server-generated content (used for demo data).</summary>
    public StoredFile SaveBytes(byte[] content, string originalName, string area)
    {
        var ext = Path.GetExtension(originalName).ToLowerInvariant();
        var folder = Path.Combine(_o.DocumentsPath, area, DateTime.UtcNow.ToString("yyyy"));
        Directory.CreateDirectory(folder);
        var full = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".dat");
        File.WriteAllBytes(full, content);
        var relative = Path.GetRelativePath(_o.DocumentsPath, full).Replace('\\', '/');
        return new StoredFile(relative, originalName, ContentTypes.GetValueOrDefault(ext, "application/octet-stream"),
            content.Length, Convert.ToHexString(SHA256.HashData(content)));
    }

    public Stream? OpenRead(string storedPath)
    {
        var root = Path.GetFullPath(_o.DocumentsPath) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(_o.DocumentsPath, storedPath));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;
        return new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static bool SignatureMatches(string ext, ReadOnlySpan<byte> h)
    {
        static bool Starts(ReadOnlySpan<byte> data, params byte[] sig) => data.Length >= sig.Length && data[..sig.Length].SequenceEqual(sig);
        return ext switch
        {
            ".pdf" => Starts(h, 0x25, 0x50, 0x44, 0x46, 0x2D),
            ".docx" or ".xlsx" or ".pptx" or ".zip" => Starts(h, 0x50, 0x4B, 0x03, 0x04),
            ".doc" or ".xls" or ".ppt" => Starts(h, 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1),
            ".png" => Starts(h, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
            ".jpg" or ".jpeg" => Starts(h, 0xFF, 0xD8, 0xFF),
            ".txt" or ".csv" => !h.Contains((byte)0),
            _ => false,
        };
    }
}
