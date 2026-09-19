# Anthropic (Claude) inference provider

ReelForge can run any agent on Claude by registering an `inference_providers` row of kind
`Anthropic`. This document covers how that provider is wired, and — importantly — **which
credential you are allowed to put in it**, which is not the same answer for local development and
for a deployed ReelForge.

## What this is, and what it deliberately is not

This is **Claude as a model backend**, not the [Claude Agent SDK](https://code.claude.com/docs/en/agent-sdk/overview).

ReelForge already has an agent harness: `ReelForgeAgentBase` + `AgentStepExecutor` +
`ToolGroupCatalog` + the room infrastructure supply the agent loop, structured output,
retry-with-feedback, tool scoping, step caching, and a container sandbox. The Agent SDK is a
*competing* harness — Claude Code packaged as a library, with its own built-in Read/Write/Edit/Bash
tools that duplicate the sandbox tools under different security properties. Adopting it would mean
running two harnesses side by side and taking a hard dependency on one vendor's agent loop.

There is also no official .NET Agent SDK (it ships for TypeScript and Python only), so using it at
all would require a Node sidecar container alongside `sandbox-executor`.

The decision was to keep ReelForge's own harness and treat Claude purely as another `IChatClient`
behind the existing provider abstraction. The entire integration is therefore one arm of
`ChatClientFactory.Build` — every one of the agents is already provider-agnostic and needed no
change.

## Credentials: what is permitted

Anthropic draws a hard line between subscription credentials and API credentials. From
[Claude Code's legal & compliance page](https://code.claude.com/docs/en/legal-and-compliance):

> **OAuth authentication** is intended exclusively for purchasers of Claude Free, Pro, Max, Team,
> and Enterprise subscription plans and is designed to support ordinary use of Claude Code and
> other native Anthropic applications.
>
> **Developers** building products or services that interact with Claude's capabilities, including
> those using the Agent SDK, should use API key authentication through Claude Console or a
> supported cloud provider. Anthropic does not permit third-party developers to offer Claude.ai
> login into their own applications, or to route requests through Free, Pro, or Max plan
> credentials on behalf of their users.

And, on limits:

> Advertised usage limits for Pro and Max plans assume **ordinary, individual usage** of Claude Code
> and the Agent SDK.

Applied to ReelForge:

| Scenario | Credential | Permitted |
|---|---|---|
| A developer running ReelForge locally against their own account | Personal subscription (`sk-ant-oat…`) or a personal API key | **Yes** — this is individual use by the credential's owner |
| A developer's own unattended CI hammering a personal subscription | Subscription OAuth token | **Mechanically works, but strains "ordinary, individual usage"** — prefer an API key |
| A deployed ReelForge serving other people's workflow executions | **API key** (Claude Console) or Bedrock / Vertex / Foundry | Subscription credentials are **not** permitted here — that is intermediating them on users' behalf |

The practical rule: a subscription credential is fine while you are the only person whose work it
runs. The moment ReelForge executes workflows for anyone else, that row needs a Console API key.

If you go the subscription route, `claude setup-token` mints a one-year token that grants model
requests only.

## Configuring a provider

Admin → Inference Providers → New, or `POST /api/v1/inference-providers`:

| Field | Value |
|---|---|
| Kind | `Anthropic` |
| Capability | `Chat` or `Vision` (**not** `Transcription` — see below) |
| Base URL | `https://api.anthropic.com` (prefilled, and required — override only for a gateway, which must be a public address: `IsDisallowedEndpoint` rejects loopback and RFC1918, so an in-cluster gateway will not pass) |
| Model | e.g. `claude-opus-5`, `claude-sonnet-5`, `claude-haiku-4-5` |
| API key | See the three modes below |
| Timeout | Defaults to 300s |

Set the row `IsDefault` for its capability to route every agent through it, or point a single agent
at it with `PUT /api/v1/agents/{id}/inference-provider` — that override works for built-in agents,
which makes it the cheapest way to try Claude on one step without moving the whole pipeline.

### The three credential modes

`ChatClientFactory.BuildAnthropic` branches on the stored key:

1. **The literal `env:`** — neither `ApiKey` nor `AuthToken` is set on the client, so the Anthropic
   SDK falls back to its own resolution: `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`, or an
   `ant auth login` profile. This is the local-development path: export the variable, store `env:`
   as the key, and **no secret is ever persisted in the database**.
2. **Starts with `sk-ant-oat`** — sent as `Authorization: Bearer`. This is the OAuth/subscription
   shape (`oat` = OAuth token, as minted by `claude setup-token`). Read the permitted-use table
   above before using it.
3. **Anything else** — sent as `x-api-key`. The normal Console API key path, and the only one
   suitable for a deployed ReelForge.

**An empty key is an error, not mode 1.** `AnthropicClient.ShouldAutoResolveCredentials` is
get-only and defaults to `true`, so leaving both properties null makes the SDK quietly use whatever
credential the container's environment carries. Three different faults produce an empty key — a
blank field, a whitespace-only paste, and a Data Protection key-ring mismatch (the resolver
deliberately degrades a failed decrypt to an empty string rather than throwing) — and under an
"empty means ambient" rule all three would silently reroute billing to the host's account. Since
*executing* a workflow needs no admin rights, any authenticated user could then spend it. So the
factory throws unless the `env:` sentinel says ambient use was actually intended.

To move a row back to mode 1 from the UI you must set the key field to `env:` explicitly; leaving
the field blank on edit means "leave the stored key unchanged", not "clear it".

Stored keys are encrypted at rest with ASP.NET Core Data Protection (`ISecretProtector`) on the
shared `dpkeys` volume, exactly like every other provider kind, and are never returned in
plaintext.

## Capabilities

`Chat` and `Vision` both resolve through `IChatClientFactory`, and Claude is natively multimodal, so
a single `Anthropic` row can back agent completions or `VideoAnalyze`'s shot captioning.

`Transcription` is **rejected with a 400** at the API boundary, on create, update, and the ad-hoc
test endpoint: Anthropic exposes no speech-to-text API. `TranscriptionClientFactory` throws an
explanatory `NotSupportedException` as a second line of defence for rows written directly to the
database. Keep using the bundled `whisper` service (or any OpenAI-compatible ASR endpoint) for
transcription.

## Sampling parameters and reasoning effort are not forwarded

`ReelForgeAgentBase.BuildChatOptions` builds one `ChatOptions` per agent, in the constructor of a
singleton, and reuses it for every run — while the provider behind it is resolved per run. So the
agent cannot know what it is about to talk to, and the options it produces are shaped for an
OpenAI-style backend in two ways that Claude cannot accept.

**Sampling parameters would hard-fail.** Every agent sets a `Temperature` (0.2–0.8) and some set
`TopP`/`TopK`. The Anthropic SDK's own `[Obsolete]` text is unambiguous that this is not merely
advisory:

> Models released after Claude Opus 4.6 do not support setting temperature. A value of 1.0 will be
> accepted for backwards compatibility, **all other values will be rejected with a 400 error**.

…and likewise any `top_k`, and any `top_p` below 0.99. Forwarding them would make **every** agent
run against a current Claude model fail. `AnthropicChatOptionsAdapter` therefore nulls all three, so
a per-agent temperature has no effect on the Anthropic path.

**The raw representation is the wrong SDK's type.** A per-agent `ReasoningEffort` (`none`/`low`/
`medium`/`xhigh` — a vLLM/Qwen chat-template vocabulary) reaches the wire through
`ChatOptions.RawRepresentationFactory`, which produces an **OpenAI-SDK-typed**
`ChatCompletionOptions`. The Anthropic adapter does read that property — its own docs offer it as
the escape hatch for full control over thinking configuration — so a foreign type there is at best
silently ignored.

`AnthropicChatOptionsAdapter` wraps the Anthropic client and drops that factory before it reaches
the SDK, leaving the Azure and OpenAI-compatible paths byte-identical. The consequence is that a
per-agent reasoning effort is **not** applied on the Anthropic path today.

What that means in practice: ReelForge never sets `ChatOptions.Reasoning`, so no
`output_config.effort` is sent. Under the SDK's default `AnthropicThinkingMode.Adaptive` the model
still thinks, at its own default effort — thinking is **on**, not off, and thinking tokens count
against `max_tokens` (see below). This is also why nothing here ever sets
`ReasoningEffort.None`: that would send `thinking.type=disabled`, which models that always think
reject with an HTTP 400.

Forwarding effort properly would mean mapping ReelForge's vocabulary onto
`ChatOptions.Reasoning` / `ReasoningOptions.Effort` — a worthwhile follow-up, deliberately out of
scope here because it changes shared `ChatOptions` construction and so touches the OpenAI paths too.

## Max output tokens

Anthropic's Messages API requires `max_tokens` on every request, unlike Chat Completions where it is
optional. No *agent* run sets `ChatOptions.MaxOutputTokens`, so the factory supplies a default of
**16,384** — sized for the largest structured outputs the engine produces (video edit
decisions, motion-graphics plans) while staying clear of the range where a *non-streaming* request
risks an HTTP timeout. Agents run through `AIAgent.RunAsync`, which does not stream. A per-request
`ChatOptions.MaxOutputTokens` still overrides it.

## Dependency note

The `Anthropic` package declares three dependencies in its `net9.0` group:

| Package | Version | Note |
|---|---|---|
| `Microsoft.Extensions.AI.Abstractions` | 10.5.1 | Minor bump over the 10.3.0 `Microsoft.Agents.AI.OpenAI` already brings in; NuGet unifies both on 10.5.1 |
| `System.Net.ServerSentEvents` | 10.0.1 | Already present transitively at 10.0.3 |
| `System.Text.Json` | 10.0.6 | **Widest blast radius** — a 10.x assembly in a `net9.0` app, affecting every `JsonSerializer` call in the engine, not just the Anthropic path |

Run `dotnet list package --include-transitive` and exercise the structured-output paths (video
analysis artifacts, `ExtractStepConfig`, the word-enum plan outputs) before merging.

## No schema migration

`InferenceProviderKind` is persisted as a string (`.HasConversion<string>()` in both DbContexts), so
adding `Anthropic` needed no EF migration — the same reason the `Vision` capability needed none.
