using System.Security.Claims;
using DeptChat.Domain.Entities;
using DeptChat.Infrastructure.Persistence;
using DeptChat.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DeptChat.WebApi.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/clients")]
public class ClientsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public ClientsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Admin-only: Retrieves all client organizations.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetClients()
    {
        var clients = await _dbContext.Clients
            .OrderBy(c => c.Name)
            .Select(c => new ClientSummaryDto(
                c.Id,
                c.Name,
                c.Code,
                c.Description,
                c.IsActive,
                c.IsBlocked,
                c.CreatedAt
            ))
            .ToListAsync();

        return Ok(clients);
    }

    /// <summary>
    /// Admin-only: Blocks a client organization. All users and managers under this client will be suspended.
    /// </summary>
    [HttpPost("{id}/block")]
    public async Task<IActionResult> BlockClient(Guid id)
    {
        var client = await _dbContext.Clients.FindAsync(id);
        if (client == null)
        {
            return NotFound(new { message = "Client organization not found." });
        }

        client.IsBlocked = true;
        client.IsActive = false;
        client.BlockedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return Ok(new { message = $"Client organization '{client.Name}' has been blocked." });
    }

    /// <summary>
    /// Admin-only: Unblocks a client organization.
    /// </summary>
    [HttpPost("{id}/unblock")]
    public async Task<IActionResult> UnblockClient(Guid id)
    {
        var client = await _dbContext.Clients.FindAsync(id);
        if (client == null)
        {
            return NotFound(new { message = "Client organization not found." });
        }

        client.IsBlocked = false;
        client.IsActive = true;
        client.BlockedAt = null;
        await _dbContext.SaveChangesAsync();

        return Ok(new { message = $"Client organization '{client.Name}' has been unblocked." });
    }
}
