# Tau.Client

A typed .NET client for any server that implements the `/v1/systemone` contract: the Tau
Runtime, TypeSafe's hosted Jev, or Kev. Point it at a base URL and get typed answers back —
an enum value, a score, or a probability — instead of hand-rolling the request and response
JSON yourself.

## Install

Packed locally only (not published to any feed in R1):

```bash
dotnet pack src/Tau.Client -c Release -o artifacts/packages
dotnet add <YourProject> package Tau.Client --source artifacts/packages
```

## Example

```csharp
using Tau.Client;

public enum Urgency { Low, Medium, High }

using var client = new SystemOneClient(new Uri("http://localhost:5000/"));

var decision = await client.DecideAsync<Urgency>(
    state: "The customer says their payment failed three times and they need this resolved today.",
    instructions: "How urgent is this support ticket?",
    descriptions: new Dictionary<Urgency, string>
    {
        [Urgency.Low] = "can wait a few days",
        [Urgency.Medium] = "should be handled this week",
        [Urgency.High] = "needs same-day attention",
    });

Console.WriteLine(decision.Value);                        // e.g. Urgency.High
Console.WriteLine(decision.Probabilities[Urgency.High]);   // e.g. 0.82
Console.WriteLine(decision.Confidence);
```

`ScoreAsync` and `NoulAsync` work the same way for an ordered scale and a yes/no question
respectively. `SystemOneAsync` sends a raw `Tau.Contract.DecisionRequest` for anything the
typed helpers don't cover.

`SystemOneAsync` also takes per-request headers. Send `x-tau-raw: true` to get the model's
uncalibrated reference probabilities from a Tau Runtime (other servers ignore the header):

```csharp
var raw = await client.SystemOneAsync(request, new Dictionary<string, string> { ["x-tau-raw"] = "true" });
```

## Enum wire names

An enum member's wire name (the criteria key sent to the server, and the key the response's
`choice` is matched against) is resolved in this order: a `JsonStringEnumMemberNameAttribute`,
then an `EnumMemberAttribute`, then the plain C# member name. Use either attribute when the
wire name needs to differ from the member name.

## Errors

- `SystemOneValidationException` — the server rejected the request (422). `Problems` lists
  each offending field; `RawBody` is kept even when the body isn't Tau's shape (for example a
  Jev or Kev error format).
- `SystemOneHttpException` — any other non-success status (401, 429/529 after retries are
  exhausted, or a 5xx). `StatusCode` and `Body` are included.
- `SystemOneProtocolException` — a success status, but the body didn't parse, or didn't
  contain the answer that was asked for.

`SystemOneClient` retries 429 and 529 responses with exponential backoff (3 attempts, 0.5
second base delay, by default); every other status is reported immediately. Pass a
`RetryPolicy` to the constructor to change this.

## Pointing at a local Tau Runtime

Start the Runtime, then use its base URL — no API key is needed (Tau accepts and ignores an
`Authorization` header, so contract clients that always send one still work):

```csharp
using var client = new SystemOneClient(new Uri("http://localhost:5000/"));
```

Point the same code at the hosted Jev endpoint by supplying its base URL and API key instead:

```csharp
using var client = new SystemOneClient(new Uri("https://api.typesafe.ai/"), apiKey: "sk-...");
```
