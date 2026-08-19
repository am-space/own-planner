using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using OwnPlanner.Application.Telegram;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed class TelegramWebhookAndLockTests
{
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("wrong")]
	public async Task SecretFilter_RejectsBeforeInvokingModelBindingPipeline(string? suppliedSecret)
	{
		var filter = new TelegramWebhookSecretFilter(Options.Create(new TelegramOptions
		{
			Enabled = true, WebhookSecret = "expected",
		}));
		var context = CreateResourceContext(suppliedSecret);
		var nextCalled = false;

		await filter.OnResourceExecutionAsync(context, () =>
		{
			nextCalled = true;
			return Task.FromResult(new ResourceExecutedContext(ToActionContext(context), []));
		});

		context.Result.Should().BeOfType<UnauthorizedResult>();
		nextCalled.Should().BeFalse("resource filters run before model binding and must stop invalid webhook bodies");
	}

	[Fact]
	public async Task SecretFilter_ValidSecretContinuesPipeline()
	{
		var filter = new TelegramWebhookSecretFilter(Options.Create(new TelegramOptions
		{
			Enabled = true, WebhookSecret = "expected",
		}));
		var context = CreateResourceContext("expected");
		var nextCalled = false;

		await filter.OnResourceExecutionAsync(context, () =>
		{
			nextCalled = true;
			return Task.FromResult(new ResourceExecutedContext(ToActionContext(context), []));
		});

		nextCalled.Should().BeTrue();
		context.Result.Should().BeNull();
	}

	[Fact]
	public async Task ChatLock_SerializesWaiters_ThenRemovesIdleEntry()
	{
		var chatLock = new TelegramChatLock();
		using var first = await chatLock.AcquireAsync(123, TestContext.Current.CancellationToken);
		var secondTask = chatLock.AcquireAsync(123, TestContext.Current.CancellationToken).AsTask();
		await Task.Yield();

		secondTask.IsCompleted.Should().BeFalse();
		chatLock.EntryCount.Should().Be(1);
		first.Dispose();
		using var second = await secondTask;
		second.Dispose();

		chatLock.EntryCount.Should().Be(0);
	}

	private static ResourceExecutingContext CreateResourceContext(string? secret)
	{
		var httpContext = new DefaultHttpContext();
		if (secret is not null) httpContext.Request.Headers["X-Telegram-Bot-Api-Secret-Token"] = secret;
		var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
		return new ResourceExecutingContext(actionContext, [], new List<IValueProviderFactory>());
	}

	private static ActionContext ToActionContext(ResourceExecutingContext context)
		=> new(context.HttpContext, context.RouteData, context.ActionDescriptor, context.ModelState);
}
