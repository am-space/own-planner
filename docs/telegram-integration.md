# Telegram private chat integration

OwnPlanner can expose the existing planning assistant through one private Telegram bot. The feature
is disabled by default and supports private text messages only.

## Configuration and deployment

1. Create a bot with BotFather and note its token and username.
2. Generate a high-entropy webhook secret.
3. Configure `Telegram__Enabled=true`, `Telegram__BotToken`, `Telegram__BotUsername`,
   `Telegram__WebhookSecret`, and optionally `Telegram__LinkTokenLifetimeMinutes` (default 15) and
   `Telegram__ProcessedUpdateRetentionDays` (default 7, constrained to 1–30 days).
4. Register `https://<public-host>/api/telegram/webhook` with Telegram's `setWebhook`, passing the
   configured secret as `secret_token`. Webhook registration is deployment-managed; OwnPlanner
   exposes no endpoint that returns the bot token or registers a webhook.

When enabled, startup fails with only the names of missing settings. Tokens, secrets, message text,
and complete updates are not logged. Disable the channel by setting `Telegram__Enabled=false` and
removing the webhook at Telegram.

## Linking and identity

An authenticated user creates a short-lived deep link from Settings. The server persists only the
SHA-256 hash of its random, single-use token. `/start <token>` in a private bot chat binds the
verified Telegram numeric user and chat IDs to that OwnPlanner account. Both sides are unique;
relinking requires disconnecting first. Replaced, expired, consumed, disconnected, and
account-deleted tokens cannot be reused.

Normal updates resolve the OwnPlanner user only through this persisted mapping. Telegram usernames,
display names, and client-provided OwnPlanner identifiers never select a tenant database.

## Delivery and commands

The webhook requires an exact `X-Telegram-Bot-Api-Secret-Token`. It ignores non-private, bot,
non-text, edited, callback, media, group, channel, and other unsupported update shapes. Every
accepted `update_id` is reserved under a unique key before side effects, and each chat is serialized
in process. A persisted per-link high-water mark prevents an older concurrent update from executing
after a newer one. Deduplication rows are retained for a bounded seven-day retry window by default
and pruned during reservation. A failed reserved update is marked failed and acknowledged; a
Telegram retry is not replayed because planner mutations may already have occurred.

Ordinary text uses the same quota reservation, token accounting, context limits, planning modes,
Gemini flow, and user-bound tools as web chat. Responses are plain text and split at 4,096 UTF-16
code units without dividing Unicode surrogate pairs. Supported commands are `/start`, `/help`,
`/mode <day|week|global|reflection|analysis>`, `/new`, `/status`, and `/unlink`.

Telegram and web histories remain separate. `/new` removes only the Telegram session; `/unlink`
removes the mapping and Telegram session without deleting planner data.

## Stored data, export, and cleanup

The central auth database stores numeric Telegram user/chat IDs, connection timestamps, selected
mode, hashed temporary tokens, and processed update metadata. It stores no Telegram message bodies
or complete payloads. Disconnect deletes the mapping and tokens. Account deletion cascade-deletes
all user-owned Telegram rows; processed update IDs are channel-delivery metadata without a user or
message payload.

The existing account export is intentionally a planner-data export of the isolated per-user SQLite
database. It does not include central authentication/security metadata, including credentials,
personal access tokens, usage counters, Telegram identifiers, token hashes, or processed update IDs.
