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

Two mechanical footnotes if you go the subscription route:
`claude setup-token` mints a one-year token, but it is **not read in `--bare` mode** (the mode you
would want for reproducible runs), and it grants model requests only.

## Configuring a provider

Admin → Inference Providers → New, or `POST /api/v1/inference-providers`:

| Field | Value |
|---|---|
| Kind | `Anthropic` |
| Capability | `Chat` or `Vision` (**not** `Transcription` — see below) |
| Base URL | `https://api.anthropic.com` (prefilled; override only for a gateway) |
| Model | e.g. `claude-opus-5`, `claude-sonnet-5`, `claude-haiku-4-5` |
| API key | See the three modes below |
| Timeout | Defaults to 300s |

Set the row `IsDefault` for its capability to route every agent through it, or point a single agent
at it with `PUT /api/v1/agents/{id}/inference-provider` — that override works for built-in agents,
which makes it the cheapest way to try Claude on one step without moving the whole pipeline.

### The three credential modes

`ChatClientFactory.BuildAnthropic` branches on the stored key:

1. **Blank** — neither `ApiKey` nor `AuthToken` is set, and the Anthropic SDK falls back to its own
   resolution: `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`, or an `ant auth login` profile. This is
   the local-development path: export the variable, leave the field empty, and **no secret is ever
   persisted in the database**.
2. **Starts with `sk-ant-oat`** — sent as `Authorization: Bearer`. This is the OAuth/subscription
   shape (`oat` = OAuth token, as minted by `claude setup-token`). Read the permitted-use table
   above before using it.
3. **Anything else** — sent as `x-api-key`. The normal Console API key path, and the only one
   suitable for a deployed ReelForge.

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

## Reasoning effort is not forwarded

`ReelForgeAgentBase.BuildChatOptions` carries a per-agent `ReasoningEffort` (`none`/`low`/`medium`/
`xhigh` — a vLLM/Qwen chat-template vocabulary) onto the wire through
`ChatOptions.RawRepresentationFactory`, which produces an **OpenAI-SDK-typed**
`ChatCompletionOptions`. Those options are built once in the agent's constructor, long before the
per-run provider is resolved, so the agent cannot know what it is about to talk to.

`OpenAIRawOptionsStrippingChatClient` wraps the Anthropic client and drops that factory before it
reaches the SDK, leaving the Azure and OpenAI-compatible paths byte-identical. The consequence is
that a per-agent reasoning effort is **not** applied on the Anthropic path today. That is the
correct default: the SDK's `AnthropicThinkingMode.Adaptive` sends no thinking configuration at all,
which is how Anthropic documents current models should be called, and it avoids the HTTP 400 that
`thinking.type=disabled` returns on models that always think.

Forwarding effort properly would mean mapping ReelForge's vocabulary onto
`ChatOptions.Reasoning` / `ReasoningOptions.Effort` — a worthwhile follow-up, deliberately out of
scope here because it changes shared `ChatOptions` construction and so touches the OpenAI paths too.

## Max output tokens

Anthropic's Messages API requires `max_tokens` on every request, unlike Chat Completions where it is
optional. Nothing in this codebase sets `ChatOptions.MaxOutputTokens`, so the factory supplies a
default of **16,384** — sized for the largest structured outputs the engine produces (video edit
decisions, motion-graphics plans) while staying clear of the range where a *non-streaming* request
risks an HTTP timeout. Agents run through `AIAgent.RunAsync`, which does not stream. A per-request
`ChatOptions.MaxOutputTokens` still overrides it.

## Dependency note

The `Anthropic` package pulls `Microsoft.Extensions.AI.Abstractions` 10.5.1, a minor bump over the
10.3.0 that `Microsoft.Agents.AI.OpenAI` already brings in. Same major version, so NuGet unifies
both on 10.5.1.

## No schema migration

`InferenceProviderKind` is persisted as a string (`.HasConversion<string>()` in both DbContexts), so
adding `Anthropic` needed no EF migration — the same reason the `Vision` capability needed none.
