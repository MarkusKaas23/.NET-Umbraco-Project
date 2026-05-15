# Anonymous cookie-only chat identity

This is a portfolio/learning project, not a production system. Chat **Visitors** are identified solely by a server-issued GUID in the `chatUserId` cookie (2-year lifetime, `HttpOnly`) — there is no email, no login, and no integration with Umbraco Members or ASP.NET Identity. This keeps the demo friction-free for short visits, avoids storing any PII, and keeps scope tight around the AI chat experience itself.

## Consequences

- A Visitor who clears cookies, switches browsers, or switches devices is treated as a brand new Visitor; their prior **Sessions** and **Exchanges** still exist in SQL but become unreachable.
- `ChatMessages.UserId` is a random GUID, not a foreign key to any user table. Do not "fix" this by adding a relation without revisiting this ADR.
- Adding real authentication later requires deciding what to do with cookie-orphaned rows (migrate, abandon, or purge) and rewriting `GetAllSessions(userId)` to read from the new identity store.
