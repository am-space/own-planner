using Microsoft.AspNetCore.Mvc;

namespace OwnPlanner.Web.Server.Controllers
{
	/// <summary>
	/// Test controller to verify exception handling behavior.
	/// Remove this in production.
	/// </summary>
	[ApiController]
	[Route("api/[controller]")]
	public class TestExceptionController : ControllerBase
	{
		private readonly ILogger<TestExceptionController> _logger;

		public TestExceptionController(ILogger<TestExceptionController> logger)
		{
			_logger = logger;
		}

		/// <summary>
		/// Throws an unhandled exception to test the global exception handler.
		/// </summary>
		[HttpGet("throw-exception")]
		public IActionResult ThrowException()
		{
			_logger.LogInformation("About to throw test exception");
			throw new InvalidOperationException("This is a test exception");
		}

		/// <summary>
		/// Throws a KeyNotFoundException to test 404 handling.
		/// </summary>
		[HttpGet("not-found")]
		public IActionResult ThrowNotFound()
		{
			_logger.LogInformation("About to throw KeyNotFoundException");
			throw new KeyNotFoundException("Resource with ID '12345' was not found");
		}

		/// <summary>
		/// Throws an ArgumentException to test 400 handling.
		/// </summary>
		[HttpGet("bad-request")]
		public IActionResult ThrowBadRequest()
		{
			_logger.LogInformation("About to throw ArgumentException");
			throw new ArgumentException("Invalid parameter value provided");
		}

		/// <summary>
		/// Throws an UnauthorizedAccessException to test 401 handling.
		/// </summary>
		[HttpGet("unauthorized")]
		public IActionResult ThrowUnauthorized()
		{
			_logger.LogInformation("About to throw UnauthorizedAccessException");
			throw new UnauthorizedAccessException("You do not have permission to access this resource");
		}

		/// <summary>
		/// Returns a successful response.
		/// </summary>
		[HttpGet("success")]
		public IActionResult Success()
		{
			_logger.LogInformation("Returning successful response");
			return Ok(new { message = "Success!", timestamp = DateTime.UtcNow });
		}
	}
}
