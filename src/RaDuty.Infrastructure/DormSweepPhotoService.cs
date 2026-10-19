using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Infrastructure;

/// <summary>Stores suite-sweep media independently from room-check media.</summary>
public sealed class DormSweepPhotoService : IDormSweepPhotoService
{
    private const int MaximumPhotosPerSweep = 4;
    private const long MaximumPhotoBytes = 5 * 1024 * 1024;
    private readonly RaDutyDbContext db;
    private readonly ICurrentUserService currentUserService;
    private readonly string storageRoot;

    public DormSweepPhotoService(RaDutyDbContext db, ICurrentUserService currentUserService, IConfiguration configuration)
    {
        this.db = db;
        this.currentUserService = currentUserService;
        storageRoot = DormSweepPhotoStorage.ResolveRoot(configuration);
    }

    public async Task<IReadOnlyList<DormSweepPhotoDto>> AddAsync(Guid sweepId, IReadOnlyList<DormSweepPhotoUpload> photos, CancellationToken cancellationToken)
    {
        if (photos.Count == 0) throw new AppException(400, "PHOTO_REQUIRED", "Choose at least one picture.");
        var current = await currentUserService.GetAsync(cancellationToken);
        var sweep = await db.DormSuiteSweeps.Include(x => x.Photos)
            .SingleOrDefaultAsync(x => x.Id == sweepId && x.ResidenceHallId == current.ResidenceHallId, cancellationToken)
            ?? throw new AppException(404, "DORM_SWEEP_NOT_FOUND", "Dorm sweep not found in your hall.");
        if (current.Role is not HallRole.HallDirector and not HallRole.Admin && sweep.CheckedByUserId != current.Id)
            throw new AppException(403, "PHOTO_UPLOAD_NOT_ALLOWED", "Only the RA who completed this sweep can add pictures.");
        if (sweep.Photos.Count + photos.Count > MaximumPhotosPerSweep)
            throw new AppException(400, "TOO_MANY_PHOTOS", $"A dorm sweep can have up to {MaximumPhotosPerSweep} pictures.");

        var prepared = new List<PreparedPhoto>(photos.Count);
        foreach (var photo in photos)
        {
            if (photo.Length <= 0) throw new AppException(400, "EMPTY_PHOTO", "One of the selected pictures is empty.");
            if (photo.Length > MaximumPhotoBytes) throw new AppException(400, "PHOTO_TOO_LARGE", "Each picture must be 5 MB or smaller.");
            var contentType = photo.ContentType.Split(';', 2)[0].Trim().ToLowerInvariant();
            var extension = Extension(contentType) ?? throw new AppException(400, "UNSUPPORTED_PHOTO_TYPE", "Use a JPEG, PNG, or WebP picture so it can be included in the PDF.");
            await using var memory = new MemoryStream((int)photo.Length);
            await photo.Content.CopyToAsync(memory, cancellationToken);
            var bytes = memory.ToArray();
            if (bytes.LongLength > MaximumPhotoBytes || !HasValidSignature(bytes, contentType))
                throw new AppException(400, "INVALID_PHOTO", "One of the selected files is not a valid supported picture.");
            try { using var image = QuestPDF.Infrastructure.Image.FromBinaryData(bytes); }
            catch (Exception) { throw new AppException(400, "INVALID_PHOTO", "The picture could not be read. Use a valid JPEG, PNG, or WebP picture."); }
            var storedFileName = $"{Guid.NewGuid():N}{extension}";
            var originalFileName = Path.GetFileName(photo.FileName.Trim());
            if (string.IsNullOrWhiteSpace(originalFileName)) originalFileName = $"suite-photo{extension}";
            if (originalFileName.Length > 180) originalFileName = originalFileName[..180];
            prepared.Add(new PreparedPhoto(bytes, originalFileName, storedFileName, contentType));
        }

        Directory.CreateDirectory(storageRoot);
        var writtenPaths = new List<string>(prepared.Count);
        try
        {
            foreach (var photo in prepared)
            {
                var path = SafePath(photo.StoredFileName);
                await File.WriteAllBytesAsync(path, photo.Content, cancellationToken);
                writtenPaths.Add(path);
                db.DormSweepPhotos.Add(new DormSweepPhoto
                {
                    DormSuiteSweep = sweep,
                    OriginalFileName = photo.OriginalFileName,
                    StoredFileName = photo.StoredFileName,
                    ContentType = photo.ContentType,
                    SizeBytes = photo.Content.LongLength
                });
            }
            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = current.Id,
                Action = "DORM_SWEEP_PHOTOS_ADDED",
                EntityType = "DormSuiteSweep",
                EntityId = sweep.Id.ToString(),
                NewValuesJson = JsonSerializer.Serialize(new { PhotoCount = prepared.Count })
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            foreach (var path in writtenPaths)
                if (File.Exists(path)) File.Delete(path);
            throw;
        }

        return sweep.Photos.OrderBy(x => x.UploadedAt)
            .Select(x => new DormSweepPhotoDto(x.Id, x.OriginalFileName, x.ContentType, x.SizeBytes, x.UploadedAt)).ToList();
    }

    public async Task<DormSweepPhotoContentDto> GetAsync(Guid photoId, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var photo = await db.DormSweepPhotos.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == photoId && x.DormSuiteSweep.ResidenceHallId == current.ResidenceHallId, cancellationToken)
            ?? throw new AppException(404, "DORM_SWEEP_PHOTO_NOT_FOUND", "Dorm sweep picture not found in your hall.");
        var path = SafePath(photo.StoredFileName);
        if (!File.Exists(path)) throw new AppException(404, "DORM_SWEEP_PHOTO_MISSING", "The stored dorm sweep picture is unavailable.");
        return new DormSweepPhotoContentDto(await File.ReadAllBytesAsync(path, cancellationToken), photo.ContentType, photo.OriginalFileName);
    }

    private string SafePath(string storedFileName)
    {
        var path = Path.GetFullPath(Path.Combine(storageRoot, storedFileName));
        if (!path.StartsWith(storageRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The dorm sweep photo path is invalid.");
        return path;
    }

    private static string? Extension(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg", "image/png" => ".png", "image/webp" => ".webp", _ => null
    };

    private static bool HasValidSignature(byte[] bytes, string contentType) => contentType switch
    {
        "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        "image/png" => bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/webp" => bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP",
        _ => false
    };

    private sealed record PreparedPhoto(byte[] Content, string OriginalFileName, string StoredFileName, string ContentType);
}

internal static class DormSweepPhotoStorage
{
    public static string ResolveRoot(IConfiguration? configuration)
    {
        var configured = configuration?["DormSweepPhotos:StoragePath"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            var roomPath = configuration?["DormCheckPhotos:StoragePath"] ?? "App_Data/DormCheckPhotos";
            var roomRoot = Path.GetFullPath(roomPath);
            configured = Path.Combine(Path.GetDirectoryName(roomRoot)!, "DormSweepPhotos");
        }
        return Path.GetFullPath(configured);
    }
}
