using System.Security.Claims;
using System.Threading.Tasks;
using AIStudyHub.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIStudyHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TrashController : ControllerBase
{
    private readonly ITrashService _trashService;

    public TrashController(ITrashService trashService)
    {
        _trashService = trashService;
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null ? int.Parse(claim.Value) : 0;
    }

    [HttpGet]
    public async Task<IActionResult> GetTrashItems([FromQuery] int page = 1, [FromQuery] int pageSize = 12)
    {
        var trash = await _trashService.GetTrashItemsAsync(GetCurrentUserId(), page, pageSize);
        return Ok(trash);
    }

    [HttpPost("document/{id}")]
    public async Task<IActionResult> MoveDocumentToTrash(int id)
    {
        await _trashService.MoveDocumentToTrashAsync(id, GetCurrentUserId());
        return Ok(new { message = "Document moved to trash" });
    }

    [HttpPost("folder/{id}")]
    public async Task<IActionResult> MoveFolderToTrash(int id)
    {
        await _trashService.MoveFolderToTrashAsync(id, GetCurrentUserId());
        return Ok(new { message = "Folder moved to trash" });
    }

    [HttpPost("restore/document/{id}")]
    public async Task<IActionResult> RestoreDocument(int id)
    {
        await _trashService.RestoreDocumentAsync(id, GetCurrentUserId());
        return Ok(new { message = "Document restored successfully" });
    }

    [HttpPost("restore/folder/{id}")]
    public async Task<IActionResult> RestoreFolder(int id)
    {
        await _trashService.RestoreFolderAsync(id, GetCurrentUserId());
        return Ok(new { message = "Folder restored successfully" });
    }

    [HttpDelete("permanent/document/{id}")]
    public async Task<IActionResult> PermanentlyDeleteDocument(int id)
    {
        await _trashService.PermanentlyDeleteDocumentAsync(id, GetCurrentUserId());
        return Ok(new { message = "Document permanently deleted" });
    }

    [HttpDelete("permanent/folder/{id}")]
    public async Task<IActionResult> PermanentlyDeleteFolder(int id)
    {
        await _trashService.PermanentlyDeleteFolderAsync(id, GetCurrentUserId());
        return Ok(new { message = "Folder permanently deleted" });
    }

    [HttpPost("empty")]
    public async Task<IActionResult> EmptyTrash()
    {
        await _trashService.EmptyTrashAsync(GetCurrentUserId());
        return Ok(new { message = "Trash emptied successfully" });
    }
}
