using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using ReelForge.WorkflowEngine.Execution.Context;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Covers <see cref="JsonOutputDigest"/> directly — pure, deterministic string-in/string-out
/// logic with no dependency on a live model or on <see cref="AgentInputBudget"/>. Budgets in
/// these tests are chosen generously (see each test's comment) so the primary structural digest
/// alone gets under <c>MaxOutputChars</c>, without also tripping the secondary middle-out
/// fallback — that fallback is covered on its own by the non-JSON/malformed-JSON tests below.
/// </summary>
public class JsonOutputDigestTests
{
    private static readonly JsonDigestSettings DefaultSettings = new(
        MaxOutputChars: 200,
        MaxStringChars: 30,
        MaxArrayItems: 3,
        MaxDepth: 4);

    [Fact]
    public void Digest_returns_input_byte_identical_when_already_under_budget()
    {
        string output = "{\"a\":1,\"b\":\"short\"}";

        JsonOutputDigest.Digest(output, DefaultSettings).Should().BeSameAs(output);
    }

    [Fact]
    public void Digest_truncates_a_long_string_with_suffix_and_still_parses()
    {
        string longValue = new string('x', 200);
        string output = JsonSerializer.Serialize(new { note = longValue });
        // Original (~210 chars) exceeds the cap, but the structurally-digested form (30 kept
        // chars + a short suffix) comfortably fits under 100, so only the string-truncation path
        // is exercised here.
        JsonDigestSettings settings = DefaultSettings with { MaxOutputChars = 100 };

        string digested = JsonOutputDigest.Digest(output, settings);

        JsonDocument doc = JsonDocument.Parse(digested);
        string note = doc.RootElement.GetProperty("note").GetString()!;
        note.Should().StartWith(new string('x', settings.MaxStringChars));
        note.Should().Contain("…(+170 chars)");
    }

    [Fact]
    public void Digest_caps_an_over_long_array_with_a_marker_element_and_stays_valid_json()
    {
        object[] items = Enumerable.Range(0, 200).Select(i => (object)i).ToArray();
        string output = JsonSerializer.Serialize(new { items });
        // Original (~600+ chars for 200 numbers) exceeds the cap; the digested form (3 kept
        // items + one marker element) is under 60 chars, well under 150.
        JsonDigestSettings settings = DefaultSettings with { MaxOutputChars = 150 };

        string digested = JsonOutputDigest.Digest(output, settings);

        JsonDocument doc = JsonDocument.Parse(digested);
        JsonElement array = doc.RootElement.GetProperty("items");
        array.GetArrayLength().Should().Be(settings.MaxArrayItems + 1);
        for (int i = 0; i < settings.MaxArrayItems; i++)
            array[i].GetInt32().Should().Be(i);
        array[settings.MaxArrayItems].GetString().Should().Contain("+197 more items omitted by context budget");
    }

    [Fact]
    public void Digest_replaces_content_beyond_max_depth_with_a_marker()
    {
        // Nest an object 20 levels deep so it exceeds a MaxDepth of 2; the writer stops
        // recursing the moment it crosses the depth cap, so the REWRITTEN size is independent of
        // how many levels exist beyond that — only the ORIGINAL (undigested) size grows with
        // depth, which is exactly what pushes it over MaxOutputChars here.
        string nested = "{\"v\":1}";
        for (int i = 0; i < 20; i++)
            nested = $"{{\"n\":{nested}}}";
        JsonDigestSettings settings = DefaultSettings with { MaxOutputChars = 90, MaxDepth = 2 };

        string digested = JsonOutputDigest.Digest(nested, settings);

        JsonDocument doc = JsonDocument.Parse(digested);
        // depth 0/1/2 stay real nested objects; the depth-3 "n" is where the cap bites.
        JsonElement depth3 = doc.RootElement
            .GetProperty("n").GetProperty("n").GetProperty("n");
        depth3.ValueKind.Should().Be(JsonValueKind.String);
        depth3.GetString().Should().Be("…(nested content omitted by context budget)");
    }

    [Fact]
    public void Digest_falls_back_to_middle_out_for_non_json_input()
    {
        string text = new string('a', 60) + new string('b', 60) + new string('c', 60);
        JsonDigestSettings settings = DefaultSettings with { MaxOutputChars = 100 };

        string digested = JsonOutputDigest.Digest(text, settings);

        digested.Should().Contain("characters omitted by context budget");
        digested.Should().StartWith("a");
        digested.Should().EndWith("c");
        digested.Length.Should().BeLessThanOrEqualTo(settings.MaxOutputChars);
    }

    [Fact]
    public void Digest_middle_out_keeps_roughly_60_percent_head_and_40_percent_tail()
    {
        string text = new string('a', 1000);
        JsonDigestSettings settings = DefaultSettings with { MaxOutputChars = 200 };

        string digested = JsonOutputDigest.Digest(text, settings);
        int markerIndex = digested.IndexOf('…');

        markerIndex.Should().BeGreaterThan(0);
        // Head should be noticeably larger than tail, consistent with a 60/40 split.
        int tailLength = digested.Length - digested.LastIndexOf('…') - 1;
        markerIndex.Should().BeGreaterThan(tailLength);
    }

    [Fact]
    public void Digest_never_throws_for_malformed_json_and_falls_back_to_middle_out()
    {
        string malformed = "{\"a\": [1, 2, " + new string('x', 200);
        JsonDigestSettings settings = DefaultSettings with { MaxOutputChars = 100 };

        Action act = () => JsonOutputDigest.Digest(malformed, settings);

        act.Should().NotThrow();
        string digested = JsonOutputDigest.Digest(malformed, settings);
        digested.Should().Contain("characters omitted by context budget");
    }

    [Fact]
    public void Digest_preserves_property_order()
    {
        string longValue = new string('z', 200);
        string output = $"{{\"z\":1,\"a\":\"{longValue}\",\"m\":true}}";
        JsonDigestSettings settings = DefaultSettings with { MaxOutputChars = 150 };

        string digested = JsonOutputDigest.Digest(output, settings);

        JsonDocument doc = JsonDocument.Parse(digested);
        List<string> names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        names.Should().Equal("z", "a", "m");
    }

    [Fact]
    public void Digest_preserves_numbers_bools_and_nulls_exactly()
    {
        string longValue = new string('q', 200);
        string output = $"{{\"pad\":\"{longValue}\",\"n\":1.50,\"i\":-42,\"t\":true,\"f\":false,\"nil\":null}}";
        JsonDigestSettings settings = DefaultSettings with { MaxOutputChars = 150 };

        string digested = JsonOutputDigest.Digest(output, settings);

        JsonDocument doc = JsonDocument.Parse(digested);
        JsonElement root = doc.RootElement;
        root.GetProperty("n").GetRawText().Should().Be("1.50");
        root.GetProperty("i").GetInt32().Should().Be(-42);
        root.GetProperty("t").GetBoolean().Should().BeTrue();
        root.GetProperty("f").GetBoolean().Should().BeFalse();
        root.GetProperty("nil").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
