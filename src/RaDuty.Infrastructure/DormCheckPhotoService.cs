using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Infrastructure;

public sealed class DormCheckPhotoService : IDormCheckPhotoService
{
    private const int MaximumPhotosPerCheck = 4;
    private const long MaximumPhotoBytes = 5 * 1024 * 1024;
    private readonly RaDutyDbContext db;
    private readonly ICurrentUserService currentUserService;
    private readonly string storageRoot;

    public DormCheckPhotoService(RaDutyDbContext db, ICurrentUserService currentUserService, IConfiguration configuration)
    {
        this.db = db;
        this.currentUserService = currentUserService;
        var configured = configuration["DormCheckPhotos:StoragePath"] ?? "App_Data/DormCheckPhotos";
        storageRoot = Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(Directory.GetCurrentDirectory(), configured));
    }

    public async Task<IReadOnlyList<DormCheckPhotoDto>> AddAsync(Guid checkId, IReadOnlyList<DormCheckPhotoUpload> photos, CancellationToken cancellationToken)
    {
        if (photos.Count == 0) throw new AppException(400, "PHOTO_REQUIRED", "Choose at least one picture.");
        var current = await currentUserService.GetAsync(cancellationToken);
        var check = await db.DormRoomChecks.Include(x => x.Photos).Include(x => x.DormRoom)
            .SingleOrDefaultAsync(x => x.Id == checkId && x.DormRoom.ResidenceHallId == current.ResidenceHallId, cancellationToken)
            ?? throw new AppException(404, "DORM_CHECK_NOT_FOUND", "Dorm check not found in your hall.");
        if (current.Role is not HallRole.HallDirector and not HallRole.Admin && check.CheckedByUserId != current.Id)
            throw new AppException(403, "PHOTO_UPLOAD_NOT_ALLOWED", "Only the RA who completed this check can add pictures.");
        if (check.Photos.Count + photos.Count > MaximumPhotosPerCheck)
            throw new AppException(400, "TOO_MANY_PHOTOS", $"A room check can have up to {MaximumPhotosPerCheck} pictures.");

        var prepared = new List<PreparedPhoto>(photos.Count);
        foreach (var photo in photos)
        {
            if (photo.Length <= 0) throw new AppException(400, "EMPTY_PHOTO", "One of the selected pictures is empty.");
            if (photo.Length > MaximumPhotoBytes) throw new AppException(400, "PHOTO_TOO_LARGE", "Each picture must be 5 MB or smaller.");
            var contentType = photo.ContentType.Split(';', 2)[0].Trim().ToLowerInvariant();
            var extension = Extension(contentType) ?? throw new AppException(400, "UNSUPPORTED_PHOTO_TYPE", "Use a JPEG, PNG, WebP, HEIC, or HEIF picture.");
            await using var memory = new MemoryStream((int)photo.Length);
            await photo.Content.CopyToAsync(memory, cancellationToken);
            var bytes = memory.ToArray();
            if (bytes.LongLength > MaximumPhotoBytes || !HasValidSignature(bytes, contentType))
                throw new AppException(400, "INVALID_PHOTO", "One of the selected files is not a valid supported picture.");
            var storedFileName = $"{Guid.NewGuid():N}{extension}";
            var originalFileName = Path.GetFileName(photo.FileName.Trim());
            if (string.IsNullOrWhiteSpace(originalFileName)) originalFileName = $"room-photo{extension}";
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
                db.DormCheckPhotos.Add(new DormCheckPhoto
                {
                    DormRoomCheck = check,
                    OriginalFileName = photo.OriginalFileName,
                    StoredFileName = photo.StoredFileName,
                    ContentType = photo.ContentType,
                    SizeBytes = photo.Content.LongLength
                });
            }
            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = current.Id,
                Action = "DORM_CHECK_PHOTOS_ADDED",
                EntityType = "DormRoomCheck",
                EntityId = check.Id.ToString(),
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

        return check.Photos.OrderBy(x => x.UploadedAt)
            .Select(x => new DormCheckPhotoDto(x.Id, x.OriginalFileName, x.ContentType, x.SizeBytes, x.UploadedAt)).ToList();
    }

    public async Task<DormCheckPhotoContentDto> GetAsync(Guid photoId, CancellationToken cancellationToken)
    {
        var current = await currentUserService.GetAsync(cancellationToken);
        var photo = await db.DormCheckPhotos.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == photoId && x.DormRoomCheck.DormRoom.ResidenceHallId == current.ResidenceHallId, cancellationToken)
            ?? throw new AppException(404, "DORM_CHECK_PHOTO_NOT_FOUND", "Dorm check picture not found in your hall.");
        var path = SafePath(photo.StoredFileName);
        if (!File.Exists(path)) throw new AppException(404, "DORM_CHECK_PHOTO_MISSING", "The stored dorm check picture is unavailable.");
        return new DormCheckPhotoContentDto(await File.ReadAllBytesAsync(path, cancellationToken), photo.ContentType, photo.OriginalFileName);
    }

    private string SafePath(string storedFileName)
    {
        var path = Path.GetFullPath(Path.Combine(storageRoot, storedFileName));
        if (!path.StartsWith(storageRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The dorm check photo path is invalid.");
        return path;
    }

    private static string? Extension(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/heic" => ".heic",
        "image/heif" => ".heif",
        _ => null
    };

    private static bool HasValidSignature(byte[] bytes, string contentType) => contentType switch
    {
        "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        "image/png" => bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/webp" => bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP",
        "image/heic" or "image/heif" => bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 4, 4) == "ftyp" && new[] { "heic", "heix", "hevc", "hevx", "mif1", "msf1" }.Contains(Encoding.ASCII.GetString(bytes, 8, 4)),
        _ => false
    };

    private sealed record PreparedPhoto(byte[] Content, string OriginalFileName, string StoredFileName, string ContentType);
}
