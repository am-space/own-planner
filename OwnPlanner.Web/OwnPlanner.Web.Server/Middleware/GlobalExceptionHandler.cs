using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace OwnPlanner.Web.Server.Middleware
{
	/// <summary>
	/// Global exception handler that catches unhandled exceptions and returns a consistent error response.
	/// Logs all exceptions and provides different responses for development vs production environments.
	/// </summary>
	public class GlobalExceptionHandler : IExceptionHandler
	{
		private readonly ILogger<GlobalExceptionHandler> _logger;
		private readonly IHostEnvironment _environment;

		public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment environment)
		{
			_logger = logger;
			_environment = environment;
		}

		public async ValueTask<bool> TryHandleAsync(
			HttpContext httpContext,
			Exception exception,
			CancellationToken cancellationToken)
		{
			_logger.LogError(
				exception,
				"Exception occurred: {Message} | Path: {Path} | Method: {Method}",
				exception.Message,
				httpContext.Request.Path,
				httpContext.Request.Method);

			var problemDetails = CreateProblemDetails(httpContext, exception);

			httpContext.Response.StatusCode = problemDetails.Status ?? (int)HttpStatusCode.InternalServerError;
			httpContext.Response.ContentType = "application/problem+json";

			await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

			return true; // Exception handled
		}

		private ProblemDetails CreateProblemDetails(HttpContext context, Exception exception)
		{
			var statusCode = GetStatusCode(exception);

			var problemDetails = new ProblemDetails
			{
				Status = statusCode,
				Title = GetTitle(exception),
				Type = $"https://httpstatuses.io/{statusCode}",
				Instance = context.Request.Path
			};

			// In development, include the full exception details
			if (_environment.IsDevelopment())
			{
				problemDetails.Detail = exception.ToString();
				problemDetails.Extensions["exceptionType"] = exception.GetType().FullName;
				
				if (exception.InnerException != null)
				{
					problemDetails.Extensions["innerException"] = exception.InnerException.Message;
				}
			}
			else
			{
				// In production, use a generic message for security
				problemDetails.Detail = exception is KeyNotFoundException
					? exception.Message
					: "An error occurred while processing your request.";
			}

			return problemDetails;
		}

		private static int GetStatusCode(Exception exception) => exception switch
		{
			KeyNotFoundException => (int)HttpStatusCode.NotFound,
			ArgumentNullException => (int)HttpStatusCode.BadRequest,
			ArgumentException => (int)HttpStatusCode.BadRequest,
			UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
			InvalidOperationException => (int)HttpStatusCode.BadRequest,
			_ => (int)HttpStatusCode.InternalServerError
		};

		private static string GetTitle(Exception exception) => exception switch
		{
			KeyNotFoundException => "Resource Not Found",
			ArgumentNullException => "Invalid Request",
			ArgumentException => "Invalid Request",
			UnauthorizedAccessException => "Unauthorized",
			InvalidOperationException => "Invalid Operation",
			_ => "Internal Server Error"
		};
	}
}
