using System.Security.Claims;
using GradrTab.Configuration;
using GradrTab.DTOs;
using GradrTab.Models;
using GradrTab.Repositories;
using GradrTab.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GradrTab.Controllers;

// Accepts documents as multipart/form-data, reads everything that can be read
// out of them and stores the result in the database.
[ApiController]
[Authorize]
[Route("api/v1/documents")]
public class DocumentsController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDocumentProcessingService _processingService;
    private readonly IFileStorageService _storage;
    private readonly DocumentProcessingOptions _options;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IUnitOfWork unitOfWork,
        IDocumentProcessingService processingService,
        IFileStorageService storage,
        IOptions<DocumentProcessingOptions> options,
        ILogger<DocumentsController> logger)
    {
        _unitOfWork = unitOfWork;
        _processingService = processingService;
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    // POST /api/v1/documents
    // Uploads one or more documents. Send them as multipart/form-data, the
    // recommended field name is "files", a browser file input sends the same name.
    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<DocumentUploadResponseDto>> Upload(
        [FromForm] List<IFormFile>? files,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        // Fall back to every file part in the request so any field name works
        var uploads = files is { Count: > 0 }
            ? files
            : Request.HasFormContentType
                ? Request.Form.Files.ToList()
                : [];

        if (uploads.Count == 0)
        {
            return BadRequest(new { message = "No files were uploaded. Send one or more files as multipart/form-data." });
        }

        if (uploads.Count > _options.MaxFilesPerRequest)
        {
            return BadRequest(new
            {
                message = $"Too many files in one request, the maximum is {_options.MaxFilesPerRequest}."
            });
        }

        var results = new List<DocumentUploadResultDto>(uploads.Count);

        // Each file is processed on its own so one bad file cannot fail the batch
        foreach (var upload in uploads)
        {
            results.Add(await _processingService.IngestAsync(upload, userId, cancellationToken));
        }

        var response = new DocumentUploadResponseDto(
            results.Count(result => result.Id is not null),
            results.Count(result => result.Id is null),
            results);

        if (response.AcceptedCount == 0)
        {
            return BadRequest(response);
        }

        return StatusCode(StatusCodes.Status201Created, response);
    }

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }

    // GET /api/v1/documents
    // Lists the caller's documents newest first, with optional filters.
    [HttpGet]
    public async Task<ActionResult<PagedResultDto<DocumentResponseDto>>> List(
        [FromQuery] DocumentQueryDto query,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var documents = OwnedDocuments(userId.Value);

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            if (!Enum.TryParse<DocumentType>(query.Type, true, out var documentType))
            {
                return BadRequest(new
                {
                    message = $"Unknown document type '{query.Type}'. Valid values: {string.Join(", ", Enum.GetNames<DocumentType>())}."
                });
            }

            documents = documents.Where(document => document.DocumentType == documentType);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<DocumentStatus>(query.Status, true, out var documentStatus))
            {
                return BadRequest(new
                {
                    message = $"Unknown status '{query.Status}'. Valid values: {string.Join(", ", Enum.GetNames<DocumentStatus>())}."
                });
            }

            documents = documents.Where(document => document.Status == documentStatus);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();

            documents = documents.Where(document =>
                document.OriginalFileName.Contains(term) ||
                (document.ExtractedText != null && document.ExtractedText.Contains(term)));
        }

        if (query.UploadedFrom is not null)
        {
            documents = documents.Where(document => document.CreatedAt >= query.UploadedFrom.Value);
        }

        if (query.UploadedTo is not null)
        {
            documents = documents.Where(document => document.CreatedAt <= query.UploadedTo.Value);
        }

        var totalCount = await documents.CountAsyncCompat(cancellationToken);
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 200 ? 20 : query.PageSize;

        var items = await documents
            .OrderByDescending(document => document.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsyncCompat(cancellationToken);

        return Ok(new PagedResultDto<DocumentResponseDto>(
            items.Select(document => document.ToResponse()).ToList(),
            page,
            pageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    // GET /api/v1/documents/{id}
    // Returns the stored information about one document, without its entries.
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentResponseDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var document = await _unitOfWork.Documents.GetOwnedAsync(id, userId.Value, cancellationToken: cancellationToken);

        if (document is null)
        {
            return NotFound(new { message = $"No document with id {id} was found." });
        }

        return Ok(document.ToResponse());
    }

    // GET /api/v1/documents/{id}/detail
    // Returns the document together with the first entries read out of it.
    [HttpGet("{id:guid}/detail")]
    public async Task<ActionResult<DocumentDetailResponseDto>> GetDetail(
        Guid id,
        [FromQuery] int entryLimit = 200,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var document = await _unitOfWork.Documents.GetOwnedAsync(id, userId.Value, cancellationToken: cancellationToken);

        if (document is null)
        {
            return NotFound(new { message = $"No document with id {id} was found." });
        }

        var limit = entryLimit is < 1 or > 2000 ? 200 : entryLimit;

        var entries = await _unitOfWork.DocumentEntries.ForDocument(id)
            .OrderBy(entry => entry.EntryIndex)
            .Take(limit)
            .ToListAsyncCompat(cancellationToken);

        return Ok(new DocumentDetailResponseDto(
            document.ToResponse(),
            entries.Select(entry => entry.ToResponse()).ToList()));
    }

    // GET /api/v1/documents/{id}/entries?page=1&pageSize=50
    // Pages through everything that was read out of one document.
    [HttpGet("{id:guid}/entries")]
    public async Task<ActionResult<PagedResultDto<DocumentEntryResponseDto>>> GetEntries(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var exists = await _unitOfWork.Documents.ExistsOwnedAsync(id, userId.Value, cancellationToken);

        if (!exists)
        {
            return NotFound(new { message = $"No document with id {id} was found." });
        }

        var entries = _unitOfWork.DocumentEntries.ForDocument(id);

        var totalCount = await entries.CountAsyncCompat(cancellationToken);

        var currentPage = page < 1 ? 1 : page;
        var currentPageSize = pageSize is < 1 or > 500 ? 50 : pageSize;

        var items = await entries
            .OrderBy(entry => entry.EntryIndex)
            .Skip((currentPage - 1) * currentPageSize)
            .Take(currentPageSize)
            .ToListAsyncCompat(cancellationToken);

        return Ok(new PagedResultDto<DocumentEntryResponseDto>(
            items.Select(entry => entry.ToResponse()).ToList(),
            currentPage,
            currentPageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)currentPageSize)));
    }

    // GET /api/v1/documents/{id}/content
    // Streams back the original uploaded file.
    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var document = await _unitOfWork.Documents.GetOwnedAsync(id, userId.Value, cancellationToken: cancellationToken);

        if (document is null)
        {
            return NotFound(new { message = $"No document with id {id} was found." });
        }

        var fullPath = _storage.GetFullPath(document.StoragePath);

        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound(new { message = "The stored copy of this document is no longer on disk." });
        }

        var stream = await _storage.OpenReadAsync(document.StoragePath, cancellationToken);

        var contentType = string.IsNullOrWhiteSpace(document.ContentType)
            ? "application/octet-stream"
            : document.ContentType;

        return File(stream, contentType, document.OriginalFileName);
    }

    // POST /api/v1/documents/{id}/reprocess
    // Reads the stored copy again, useful after an extractor has been improved.
    [HttpPost("{id:guid}/reprocess")]
    public async Task<ActionResult<DocumentUploadResultDto>> Reprocess(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var exists = await _unitOfWork.Documents.ExistsOwnedAsync(id, userId.Value, cancellationToken);

        if (!exists)
        {
            return NotFound(new { message = $"No document with id {id} was found." });
        }

        var result = await _processingService.ReprocessAsync(id, cancellationToken);

        if (result is null)
        {
            return NotFound(new { message = $"No document with id {id} was found." });
        }

        return Ok(result);
    }

    // DELETE /api/v1/documents/{id}
    // Removes the document, everything read out of it and the stored file.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        // The delete needs a tracked entity, so it is queried directly
        var document = await _unitOfWork.Documents.GetOwnedTrackedAsync(id, userId.Value, cancellationToken);

        if (document is null)
        {
            return NotFound(new { message = $"No document with id {id} was found." });
        }

        var storagePath = document.StoragePath;

        // The entries are removed by the configured cascade delete
        _unitOfWork.Documents.Remove(document);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _storage.DeleteAsync(storagePath, cancellationToken);

        _logger.LogInformation("Deleted document {DocumentId} ({FileName})", id, document.OriginalFileName);

        return NoContent();
    }

    // Documents are only ever visible to the user that uploaded them
    private IQueryable<Document> OwnedDocuments(Guid userId) =>
        _unitOfWork.Documents.OwnedBy(userId);
}