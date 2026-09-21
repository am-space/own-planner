using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OwnPlanner.Application.WeeklyReviews;
using OwnPlanner.Mcp.Tools;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Controllers;

[ApiController, Authorize, Route("api/weekly-review")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WeeklyReviewController(IWeeklyReviewService service, IPerUserAppInitializationService initialization) : ControllerBase
{
	public sealed record SettingsRequest(bool Enabled, string? TimeZoneId, int WeekStart = 1, string ReminderTime = "18:00", string Channel = "telegram");
	public sealed record TransitionRequest(string Action, string? LocalTime);

	[HttpGet("settings")]
	public Task<IActionResult> Settings(CancellationToken ct) => Execute(async () => await service.GetPreferencesAsync(ct), ct);

	[HttpPut("settings")]
	public Task<IActionResult> Configure(SettingsRequest request, CancellationToken ct) => Execute(async () =>
		await service.ConfigureAsync(request.Enabled, request.TimeZoneId, request.WeekStart, request.ReminderTime, request.Channel, ct), ct);

	[HttpPost("open")]
	public Task<IActionResult> Open(Guid? reviewId = null, int offset = 0, int limit = 20, CancellationToken ct = default, DateOnly? targetWeek = null) =>
		Execute(async () => await service.OpenAsync(reviewId, offset, limit, ct, targetWeek), ct);

	[HttpPost("{id:guid}/transition")]
	public Task<IActionResult> Transition(Guid id, TransitionRequest request, CancellationToken ct) =>
		Execute(async () => await service.TransitionAsync(id, request.Action, request.LocalTime, ct), ct);

	private async Task<IActionResult> Execute(Func<Task<object>> action, CancellationToken ct)
	{
		var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
		await initialization.EnsureInitializedAsync(new SessionContext { UserId = userId, SessionId = "weekly-review-http" }, ct);
		try { return Ok(await action()); }
		catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
		catch (KeyNotFoundException) { return NotFound(new { message = "Weekly review not found." }); }
	}
}
