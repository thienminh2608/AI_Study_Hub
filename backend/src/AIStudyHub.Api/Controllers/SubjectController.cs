using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIStudyHub.Api.Controllers;

[ApiController]
public class SubjectController : ControllerBase
{
    private readonly ISubjectService _subjectService;

    public SubjectController(ISubjectService subjectService)
    {
        _subjectService = subjectService;
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null && int.TryParse(claim.Value, out int id) ? id : 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUBLIC / USER ENDPOINTS
    // ─────────────────────────────────────────────────────────────────────────

    [AllowAnonymous]
    [HttpGet("api/subjects")]
    public async Task<IActionResult> GetApprovedSubjects()
    {
        var subjects = await _subjectService.GetApprovedSubjectsAsync();
        return Ok(subjects);
    }

    [AllowAnonymous]
    [HttpGet("api/subjects/tree")]
    public async Task<IActionResult> GetSubjectTree([FromQuery] string? status = "APPROVED")
    {
        var tree = await _subjectService.GetSubjectTreeAsync(status);
        return Ok(tree);
    }

    [Authorize]
    [HttpPost("api/subjects/resolve")]
    public async Task<IActionResult> ResolveSubject([FromBody] CreateSubjectDto dto)
    {
        int userId = GetCurrentUserId();
        string resolved = await _subjectService.CreateOrResolveSubjectAsync(dto.Name, userId, dto.ParentSubjectId);
        return Ok(new { subject = resolved });
    }

    [Authorize]
    [HttpPost("api/subjects/resolve-path")]
    public async Task<IActionResult> ResolveSubjectPath([FromBody] ResolveSubjectPathDto dto)
    {
        int userId = GetCurrentUserId();
        string resolved = await _subjectService.CreateOrResolveSubjectPathAsync(dto.SubjectName, dto.ChildSubjectName, userId);
        return Ok(new { subject = resolved });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MODERATOR / ADMIN ENDPOINTS
    // ─────────────────────────────────────────────────────────────────────────

    [Authorize(Roles = "MODERATOR,ADMIN")]
    [HttpGet("api/moderator/subjects")]
    public async Task<IActionResult> GetModeratorSubjects([FromQuery] string? status, [FromQuery] string? search)
    {
        var subjects = await _subjectService.GetSubjectsForModeratorAsync(status, search);
        return Ok(subjects);
    }

    [Authorize(Roles = "MODERATOR,ADMIN")]
    [HttpPost("api/moderator/subjects")]
    public async Task<IActionResult> CreateModeratorSubject([FromBody] CreateSubjectDto dto)
    {
        int userId = GetCurrentUserId();
        try
        {
            var created = await _subjectService.CreateSubjectAsync(dto.Name, userId, dto.ParentSubjectId, dto.SortOrder, autoApprove: true);
            return Ok(created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [Authorize(Roles = "MODERATOR,ADMIN")]
    [HttpPut("api/moderator/subjects/{id}/move")]
    public async Task<IActionResult> MoveSubject(int id, [FromBody] MoveSubjectDto dto)
    {
        try
        {
            bool moved = await _subjectService.MoveSubjectSubtreeAsync(id, dto.NewParentSubjectId, dto.NewSortOrder);
            return Ok(new { success = moved, message = "Di chuyển danh mục môn học thành công." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [Authorize(Roles = "MODERATOR,ADMIN")]
    [HttpPost("api/moderator/subjects/{id}/approve")]
    public async Task<IActionResult> ApproveSubject(int id)
    {
        int userId = GetCurrentUserId();
        try
        {
            var approved = await _subjectService.ApproveSubjectAsync(id, userId);
            return Ok(approved);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [Authorize(Roles = "MODERATOR,ADMIN")]
    [HttpPost("api/moderator/subjects/{id}/reject")]
    public async Task<IActionResult> RejectSubject(int id, [FromBody] RejectSubjectDto dto)
    {
        int userId = GetCurrentUserId();
        try
        {
            var rejected = await _subjectService.RejectSubjectAsync(id, dto.Reason, userId);
            return Ok(rejected);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [Authorize(Roles = "MODERATOR,ADMIN")]
    [HttpDelete("api/moderator/subjects/{id}")]
    public async Task<IActionResult> DeleteSubject(int id)
    {
        int userId = GetCurrentUserId();
        try
        {
            await _subjectService.DeleteSubjectAsync(id, userId);
            return Ok(new { message = "Xóa môn học thành công." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
