# AI Chat Assistant

The domain-specific subsystem of this Umbraco project: an anonymous, cookie-tracked chat interface that proxies the Mistral AI API and persists conversation history in SQL.

## Language

**Visitor**:
An anonymous person browsing the public site, tracked across visits by a 2-year cookie (`chatUserId`).
_Avoid_: User (collides with Umbraco backoffice users), Guest, Anon

**Session**:
One conversation thread belonging to a Visitor, identified by a 30-day cookie (`chatSessionId`) that is reset when the Visitor starts a new chat.
_Avoid_: Conversation, Thread, Chat

**Exchange**:
One prompt-response pair within a Session — a Visitor's question and the AI's reply, stored together as a single row.
_Avoid_: Message (Message is reserved for one side of an Exchange when speaking to the Mistral API), Turn

## Relationships

- A **Visitor** has zero or more **Sessions**
- A **Session** belongs to exactly one **Visitor**
- A **Session** is an ordered sequence of **Exchanges**
- One **Exchange** splits into two API **Messages** (role: `user`, role: `assistant`) when sent to Mistral

## Flagged ambiguities

- "User" was previously used for the anonymous chat visitor — resolved: this is a **Visitor**. "User" is reserved for Umbraco backoffice editors.
- "Message" is ambiguous in the codebase — the `ChatMessage` class actually represents an **Exchange** (a pair). When talking to the Mistral API, a Message is one side of an Exchange. The class name lies about its shape; the glossary corrects it.
