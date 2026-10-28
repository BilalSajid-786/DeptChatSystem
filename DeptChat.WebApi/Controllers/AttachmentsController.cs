using DeptChat.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeptChat.WebApi.Controllers;

[Authorize]
[ApiController]
[Route("api/attachments")]
public class AttachmentsController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AttachmentsController> _logger;

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp"
    };

    private static readonly HashSet<string> AllowedDocExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".txt", ".xlsx", ".xls", ".csv", ".zip", ".rar", ".7z", ".mp3", ".m4a", ".wav", ".aac"
    };

    public AttachmentsController(IWebHostEnvironment env, ILogger<AttachmentsController> logger)
    {
        _env = env;
        _logger = logger;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(30 * 1024 * 1024)] // 30 MB
    public async Task<IActionResult> Upload(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "No file was uploaded." });
        }

        if (file.Length > 25 * 1024 * 1024)
        {
            return BadRequest(new { message = "File size exceeds the 25 MB limit." });
        }

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext))
        {
            ext = ".bin";
        }

        var isImage = AllowedImageExtensions.Contains(ext);
        var isDoc = AllowedDocExtensions.Contains(ext);

        if (!isImage && !isDoc)
        {
            return BadRequest(new { message = $"File type '{ext}' is not supported." });
        }

        try
        {
            var webRoot = _env.WebRootPath;
            if (string.IsNullOrEmpty(webRoot))
            {
                webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            }

            var monthFolder = DateTime.UtcNow.ToString("yyyy-MM");
            var uploadsDir = Path.Combine(webRoot, "uploads", monthFolder);
            if (!Directory.Exists(uploadsDir))
            {
                Directory.CreateDirectory(uploadsDir);
            }

            var safeOriginalName = Path.GetFileNameWithoutExtension(file.FileName);
            // Remove invalid characters
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                safeOriginalName = safeOriginalName.Replace(c, '_');
            }
            if (safeOriginalName.Length > 30)
            {
                safeOriginalName = safeOriginalName.Substring(0, 30);
            }

            var uniqueFileName = $"{Guid.NewGuid():N}_{safeOriginalName}{ext.ToLowerInvariant()}";
            var filePath = Path.Combine(uploadsDir, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var relativeUrl = $"/uploads/{monthFolder}/{uniqueFileName}";

            var result = new AttachmentUploadResultDto(
                Url: relativeUrl,
                FileName: file.FileName,
                FileSizeBytes: file.Length,
                ContentType: file.ContentType ?? (isImage ? "image/jpeg" : "application/octet-stream"),
                IsImage: isImage
            );

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload attachment {FileName}", file.FileName);
            return StatusCode(500, new { message = "An error occurred while uploading the file." });
        }
    }
}
