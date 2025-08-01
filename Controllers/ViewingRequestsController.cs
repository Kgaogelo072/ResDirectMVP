using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using AutoMapper;
using PropertyListingAPI.Data;
using PropertyListingAPI.DTOs;
using PropertyListingAPI.Interfaces;
using PropertyListingAPI.Models;
using PropertyListingAPI.Enums;

[ApiController]
[Route("api/[controller]")]
public class ViewingRequestsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IMapper _mapper;
    private readonly IEmailService _emailService;
    private readonly IWhatsAppService _whatsAppService;

    public ViewingRequestsController(ApplicationDbContext context, IMapper mapper, IEmailService emailService, IWhatsAppService whatsAppService)
    {
        _context = context;
        _mapper = mapper;
        _emailService = emailService;
        _whatsAppService = whatsAppService;
    }

    [HttpPost]
    [Authorize(Roles = "Tenant")]
    public async Task<IActionResult> Create(ViewingRequestCreateDto dto)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
        var tenant = await _context.Users.FindAsync(userId);
        var property = await _context.Properties.Include(p => p.Agent).FirstOrDefaultAsync(p => p.Id == dto.PropertyId);

        if (property == null)
            return NotFound("Property not found.");

        var request = new ViewingRequest
        {
            PropertyId = dto.PropertyId,
            TenantId = userId,
            ViewingDate = dto.ViewingDate,
            Status = PropertyListingAPI.Enums.ViewingStatus.Pending
        };

        _context.ViewingRequests.Add(request);
        await _context.SaveChangesAsync();

        var emailBody = $@"
            Hi {property.Agent.FullName},<br/>
            {tenant.FullName} has requested to view your property <b>{property.Title}</b> on <b>{dto.ViewingDate:dd MMM yyyy HH:mm}</b>.
        ";
        await _emailService.SendEmailAsync(property.Agent.Email, "New Viewing Request", emailBody);

        await _whatsAppService.SendMessageAsync(property.Agent.PhoneNumber,
            $"New viewing request from {tenant.FullName} for '{property.Title}' on {dto.ViewingDate:dd MMM yyyy HH:mm}");

        return Ok("Viewing request submitted.");
    }

    [HttpGet("by-agent")]
    [Authorize(Roles = "Agent")]
    public async Task<IActionResult> GetForAgent()
    {
        var agentId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
        var requests = await _context.ViewingRequests
            .Include(r => r.Property)
            .Include(r => r.Tenant)
            .Where(r => r.Property.AgentId == agentId)
            .ToListAsync();

        return Ok(_mapper.Map<IEnumerable<ViewingRequestReadDto>>(requests));
    }

    [HttpPut("{id}/status")]
    [Authorize(Roles = "Agent")]
    public async Task<IActionResult> UpdateStatus(int id, [FromQuery] string status)
    {
        var agentId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
        var request = await _context.ViewingRequests
            .Include(r => r.Property)
            .FirstOrDefaultAsync(r => r.Id == id && r.Property.AgentId == agentId);

        if (request == null) return NotFound();

        if (!Enum.TryParse(status, true, out ViewingStatus parsedStatus))
            return BadRequest("Invalid status.");

        request.Status = parsedStatus;
        await _context.SaveChangesAsync();

        return Ok("Viewing status updated.");
    }
}
