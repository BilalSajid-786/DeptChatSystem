using System.Security.Claims;
using DeptChat.Domain.Entities;
using DeptChat.Infrastructure.Persistence;
using DeptChat.Shared.DTOs;
using DeptChat.Shared.Enums;
using DeptChat.WebApi.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DeptChat.WebApi.Controllers;

[Authorize]
[ApiController]
[Route("api/departments")]
public class DepartmentsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IHubContext<ChatHub> _hubContext;

    public DepartmentsController(ApplicationDbContext dbContext, IHubContext<ChatHub> hubContext)
    {
        _dbContext = dbContext;
        _hubContext = hubContext;
    }

    /// <summary>
    /// Gets all relevant departments with real-time unread badge counts.
    /// - Normal Users: sees all active departments and unread replies in their private room.
    /// - Managers: by default sees all departments (or assigned departments if restricted) and unread counts.
    /// - Admins: sees all departments and global unread totals.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetDepartments()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var roleClaim = User.FindFirstValue(ClaimTypes.Role);
        Enum.TryParse<UserRole>(roleClaim, out var role);

        var query = _dbContext.Departments.Where(d => d.IsActive);

        // By default, departments are visible to every user and manager.
        // If a department has explicit managers assigned, only assigned managers see it.
        // Unrestricted departments remain visible to all managers by default.
        if (role == UserRole.Manager)
        {
            var assignedDeptIds = await _dbContext.DepartmentManagers
                .Where(dm => dm.UserId == userId)
                .Select(dm => dm.DepartmentId)
                .ToListAsync();

            var restrictedDeptIds = await _dbContext.DepartmentManagers
                .Select(dm => dm.DepartmentId)
                .Distinct()
                .ToListAsync();

            query = query.Where(d => assignedDeptIds.Contains(d.Id) || !restrictedDeptIds.Contains(d.Id));
        }

        var departments = await query.ToListAsync();
        var deptIds = departments.Select(d => d.Id).ToList();

        Dictionary<Guid, int> unreadCounts;
        if (role == UserRole.User)
        {
            unreadCounts = await _dbContext.Messages
                .Where(m => deptIds.Contains(m.Conversation.DepartmentId) 
                            && m.Conversation.CreatedByUserId == userId 
                            && m.SenderId != userId 
                            && m.Status < MessageDeliveryStatus.Read)
                .GroupBy(m => m.Conversation.DepartmentId)
                .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.DepartmentId, x => x.Count);
        }
        else
        {
            unreadCounts = await _dbContext.Messages
                .Where(m => deptIds.Contains(m.Conversation.DepartmentId) 
                            && m.SenderRole == UserRole.User 
                            && m.Status < MessageDeliveryStatus.Read)
                .GroupBy(m => m.Conversation.DepartmentId)
                .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.DepartmentId, x => x.Count);
        }

        var result = departments.Select(dept => new DepartmentDto(
            dept.Id,
            dept.Name,
            dept.Description,
            dept.IsActive,
            unreadCounts.GetValueOrDefault(dept.Id, 0)
        )).ToList();

        return Ok(result);
    }

    /// <summary>
    /// Admin only: Add a new department.
    /// By default, newly added departments are visible to every user and manager.
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> CreateDepartment([FromBody] CreateDepartmentDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequest(new { message = "Department name is required." });
        }

        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim() ?? string.Empty,
            IsActive = true
        };

        _dbContext.Departments.Add(dept);
        await _dbContext.SaveChangesAsync();

        var responseDto = new DepartmentDto(dept.Id, dept.Name, dept.Description, dept.IsActive, 0);

        // Real-time broadcast to all connected clients (users, managers, admins)
        await _hubContext.Clients.All.SendAsync("DepartmentAdded", responseDto);

        return StatusCode(StatusCodes.Status201Created, responseDto);
    }

    /// <summary>
    /// Admin only: Assign a manager to a department.
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("{id}/managers")]
    public async Task<IActionResult> AssignManager(Guid id, [FromBody] AssignManagerDto dto)
    {
        var deptExists = await _dbContext.Departments.AnyAsync(d => d.Id == id);
        if (!deptExists) return NotFound(new { message = "Department not found." });

        var manager = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == dto.UserId && u.Role == UserRole.Manager);
        if (manager == null) return BadRequest(new { message = "User is not a valid Manager." });

        var alreadyAssigned = await _dbContext.DepartmentManagers.AnyAsync(dm => dm.DepartmentId == id && dm.UserId == dto.UserId);
        if (alreadyAssigned) return Ok(new { message = "Manager already assigned to this department." });

        _dbContext.DepartmentManagers.Add(new DepartmentManager
        {
            DepartmentId = id,
            UserId = dto.UserId
        });
        await _dbContext.SaveChangesAsync();
        return Ok(new { message = "Manager assigned successfully." });
    }

    /// <summary>
    /// Admin only: Soft-deletes a department.
    /// Sets IsActive to false and notifies all connected clients via SignalR.
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteDepartment(Guid id)
    {
        var dept = await _dbContext.Departments.FirstOrDefaultAsync(d => d.Id == id);
        if (dept == null)
        {
            return NotFound(new { message = "Department not found." });
        }

        dept.IsActive = false;
        await _dbContext.SaveChangesAsync();

        // Broadcast real-time deletion to all connected clients
        await _hubContext.Clients.All.SendAsync("DepartmentDeleted", id);

        return Ok(new { message = "Department deleted successfully.", departmentId = id });
    }
}
