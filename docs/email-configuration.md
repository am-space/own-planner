# Email configuration

OwnPlanner sends transactional email (currently password-reset links) through the
`IEmailSender` port. Two adapters are available, selected by configuration:

| Provider  | Adapter              | Use                                                           |
| --------- | -------------------- | ------------------------------------------------------------ |
| `Logging` | `LoggingEmailSender` | Local dev — logs the message (incl. the reset link), no SMTP |
| `Smtp`    | `SmtpEmailSender`    | Real delivery over SMTP via MailKit                          |

Selection (`Program.cs`): the `Email:Provider` value is used if set; otherwise it
defaults to `Logging` in the Development environment and `Smtp` elsewhere.

## Configuration keys (`Email` section)

| Key                         | Notes                                                   |
| --------------------------- | ------------------------------------------------------- |
| `Email:Provider`            | `Smtp` or `Logging`                                     |
| `Email:Host`                | SMTP host (secret)                                      |
| `Email:Port`                | SMTP port — `587` for STARTTLS submission               |
| `Email:User`                | SMTP user (secret)                                      |
| `Email:Password`            | SMTP password / API key (secret)                        |
| `Email:FromAddress`         | Verified sender address (secret)                        |
| `Email:FromName`            | Sender display name                                     |
| `Email:ResetUrlBase`        | Base URL for reset links, e.g. `https://controlcode.space` — the email links to `{base}/reset-password?token=...` |
| `Email:ResetTokenLifetimeMinutes` | Token validity window (default 30)                |

**Never commit secrets.** Provide `Host`/`User`/`Password`/`FromAddress` via
user-secrets or environment variables.

```sh
# user-secrets (run in OwnPlanner.Web/OwnPlanner.Web.Server)
dotnet user-secrets set "Email:Provider" "Smtp"
dotnet user-secrets set "Email:Host" "smtp-relay.brevo.com"
dotnet user-secrets set "Email:Port" "587"
dotnet user-secrets set "Email:User" "<brevo-smtp-login>"
dotnet user-secrets set "Email:Password" "<brevo-smtp-key>"
dotnet user-secrets set "Email:FromAddress" "no-reply@controlcode.space"
dotnet user-secrets set "Email:ResetUrlBase" "https://controlcode.space"
```

Environment variables use the `Email__Key` form (double underscore), e.g.
`Email__Password`.

## Provider: Brevo (default)

Use Brevo's transactional **SMTP** relay (free tier ~300/day, EU region for GDPR fit).
Because the transport is plain SMTP, switching providers later is a configuration
change only — no code change.

## Deliverability (required regardless of provider)

On the sending domain (`controlcode.space`) configure:

- **SPF** — authorize the provider's sending hosts.
- **DKIM** — add the provider's DKIM CNAME/TXT records and enable signing.
- **DMARC** — publish a policy (start with `p=none` for monitoring, then tighten).

Verify with a tool such as [mail-tester.com](https://www.mail-tester.com) before
going live.
