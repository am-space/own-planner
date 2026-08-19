using FluentAssertions;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OwnPlanner.Application.Telegram;
using OwnPlanner.Application.Usage;
using OwnPlanner.Web.Server.Controllers;
using OwnPlanner.Web.Server.Models;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Tests.Controllers;

public sealed class TelegramControllerTests
{
	[Fact]
	public void ConnectionStatus_JsonContract_UsesPlanningModeName()
	{
		var json = JsonSerializer.Serialize(
			new TelegramConnectionStatus(true, true, false, 100, DateTimeOffset.UtcNow, "DayWork"),
			new JsonSerializerOptions(JsonSerializerDefaults.Web));

		json.Should().Contain("\"mode\":\"DayWork\"").And.NotContain("\"mode\":2");
	}

	[Fact]
	public async Task Webhook_DuplicateIsAcknowledgedWithoutSendingOrProcessing()
	{
		var fixture = new Fixture();
		fixture.Integration.ReserveUpdateAsync(42, Arg.Any<CancellationToken>()).Returns(TelegramUpdateReservation.Duplicate);
		var result = await fixture.Controller.Webhook(Fixture.PrivateTextUpdate(), TestContext.Current.CancellationToken);

		result.Should().BeOfType<OkResult>();
		await fixture.Bot.DidNotReceiveWithAnyArgs().SendTextAsync(default, default!, TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task Webhook_IgnoresNonPrivateUpdatesAfterDeduplication()
	{
		var fixture = new Fixture();
		var update = Fixture.PrivateTextUpdate();
		update.Message!.Chat!.Type = "group";
		var result = await fixture.Controller.Webhook(update, TestContext.Current.CancellationToken);

		result.Should().BeOfType<OkResult>();
		await fixture.Integration.Received(1).CompleteUpdateAsync(42, true, Arg.Any<CancellationToken>());
		await fixture.Bot.DidNotReceiveWithAnyArgs().SendTextAsync(default, default!, TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task Webhook_UnlinkedPrivateUserGetsGenericConnectionDirection()
	{
		var fixture = new Fixture();
		var result = await fixture.Controller.Webhook(Fixture.PrivateTextUpdate(), TestContext.Current.CancellationToken);

		result.Should().BeOfType<OkResult>();
		await fixture.Bot.Received(1).SendTextAsync(200, "Connect Telegram from OwnPlanner Settings before using this bot.", Arg.Any<CancellationToken>());
		await fixture.Integration.Received(1).CompleteUpdateAsync(42, true, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Webhook_StaleLinkedUpdateIsAcknowledgedWithoutExecutingChatTurn()
	{
		var fixture = new Fixture();
		var userId = Guid.NewGuid();
		fixture.Integration.FindLinkedAccountAsync(100, 200, Arg.Any<CancellationToken>())
			.Returns(new TelegramLinkedAccount(userId, 100, 200, OwnPlanner.Application.Chat.PlanningMode.DayWork));
		fixture.Integration.TryAdvanceChatUpdateAsync(userId, 42, Arg.Any<CancellationToken>()).Returns(false);

		var result = await fixture.Controller.Webhook(Fixture.PrivateTextUpdate(), TestContext.Current.CancellationToken);

		result.Should().BeOfType<OkResult>();
		await fixture.Sessions.DidNotReceiveWithAnyArgs().GetOrCreateSessionAsync(default!, default!, TestContext.Current.CancellationToken);
		await fixture.Integration.Received(1).CompleteUpdateAsync(42, true, Arg.Any<CancellationToken>());
	}

	private sealed class Fixture
	{
		public ITelegramIntegrationService Integration { get; } = Substitute.For<ITelegramIntegrationService>();
		public ITelegramBotClient Bot { get; } = Substitute.For<ITelegramBotClient>();
		public IChatSessionManager Sessions { get; } = Substitute.For<IChatSessionManager>();
		public TelegramController Controller { get; }

		public Fixture()
		{
			Integration.ReserveUpdateAsync(42, Arg.Any<CancellationToken>()).Returns(TelegramUpdateReservation.Reserved);
			Controller = new TelegramController(
				Integration,
				Bot,
				Sessions,
				Substitute.For<IUsageQuotaService>(),
				new TelegramChatLock(),
				Options.Create(new TelegramOptions { Enabled = true, WebhookSecret = "webhook-secret" }),
				Substitute.For<ILogger<TelegramController>>());
		}

		public static TelegramUpdate PrivateTextUpdate() => new()
		{
			UpdateId = 42,
			Message = new TelegramMessage
			{
				Text = "hello", From = new TelegramUser { Id = 100 }, Chat = new TelegramChat { Id = 200, Type = "private" },
			},
		};
	}
}
