namespace OwnPlanner.Application.Chat;

/// <summary>The author of a <see cref="ChatMessage"/>.</summary>
public enum ChatRole
{
	User,
	Model
}

/// <summary>
/// A single completed conversation turn entry (plain text, no tool-call structure). Used to keep a
/// transport-neutral transcript that history compaction can summarize/trim and replay into a rebuilt
/// chat session, without depending on the chat SDK's internal history representation.
/// </summary>
public sealed record ChatMessage(ChatRole Role, string Text);
