using System.Linq;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ReelForge.WorkflowEngine.Agents.Tools;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

/// <summary>
/// Coverage for <see cref="SandboxFileOutline"/> — a pure, no-I/O locator aid, so every case here
/// runs against plain in-memory strings. Deliberately does not assert exact regex internals; only
/// the documented JSON contract (imports/symbols/truncated/totalLines/totalChars) that
/// <c>GetSandboxFileOutline</c> actually returns to a model.
/// </summary>
public class SandboxFileOutlineTests
{
    private const string RemotionSample =
        """
        import { AbsoluteFill, useCurrentFrame, interpolate } from 'remotion';
        import React from 'react';
        import { Logo } from './Logo.tsx';

        export interface HeroSceneProps {
          title: string;
        }

        export type Direction = 'left' | 'right';

        const INTERNAL_HELPER = 42;

        export const MAX_DURATION = 300;

        function clampFrame(frame: number): number {
          return Math.max(0, frame);
        }

        export const HeroScene: React.FC<HeroSceneProps> = ({ title }) => {
          const frame = useCurrentFrame();
          return <AbsoluteFill><Logo /></AbsoluteFill>;
        };

        export function formatTitle(title: string) {
          return title.toUpperCase();
        }

        export default HeroScene;

        <Composition id="HeroSceneComposition" width={1920} height={1080} fps={30} durationInFrames={150} />
        """;

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Realistic_tsx_file_yields_imports_and_expected_symbol_kinds()
    {
        JsonElement outline = Parse(SandboxFileOutline.BuildOutline(RemotionSample));

        string[] imports = outline.GetProperty("imports").EnumerateArray().Select(e => e.GetString()!).ToArray();
        imports.Should().Contain("remotion");
        imports.Should().Contain("react");
        imports.Should().Contain("./Logo.tsx");

        var symbols = outline.GetProperty("symbols").EnumerateArray().ToList();

        symbols.Should().Contain(s =>
            s.GetProperty("kind").GetString() == "interface" && s.GetProperty("name").GetString() == "HeroSceneProps");

        symbols.Should().Contain(s =>
            s.GetProperty("kind").GetString() == "type" && s.GetProperty("name").GetString() == "Direction");

        symbols.Should().Contain(s =>
            s.GetProperty("kind").GetString() == "const" && s.GetProperty("name").GetString() == "MAX_DURATION");

        symbols.Should().Contain(s =>
            s.GetProperty("kind").GetString() == "function" && s.GetProperty("name").GetString() == "formatTitle");

        symbols.Should().Contain(s =>
            s.GetProperty("kind").GetString() == "component" && s.GetProperty("name").GetString() == "HeroScene");

        symbols.Should().Contain(s => s.GetProperty("kind").GetString() == "default");

        symbols.Should().Contain(s =>
            s.GetProperty("kind").GetString() == "composition" && s.GetProperty("name").GetString() == "HeroSceneComposition");

        // A purely internal (non-exported) const must not be picked up as a top-level symbol —
        // this outline only surfaces exported/composition entries, per its documented contract.
        symbols.Should().NotContain(s => s.GetProperty("name").GetString() == "INTERNAL_HELPER");
    }

    [Fact]
    public void Symbol_entries_record_their_source_line_and_a_trimmed_signature()
    {
        JsonElement outline = Parse(SandboxFileOutline.BuildOutline(RemotionSample));
        var heroScene = outline.GetProperty("symbols").EnumerateArray()
            .Single(s => s.GetProperty("name").GetString() == "HeroScene");

        heroScene.GetProperty("line").GetInt32().Should().BeGreaterThan(0);
        heroScene.GetProperty("signature").GetString().Should().Contain("HeroScene");
    }

    [Fact]
    public void TotalLines_and_totalChars_reflect_the_input()
    {
        string content = "line one\nline two\nline three";
        JsonElement outline = Parse(SandboxFileOutline.BuildOutline(content));

        outline.GetProperty("totalLines").GetInt32().Should().Be(3);
        outline.GetProperty("totalChars").GetInt32().Should().Be(content.Length);
    }

    [Fact]
    public void Empty_file_produces_an_empty_but_valid_outline()
    {
        JsonElement outline = Parse(SandboxFileOutline.BuildOutline(string.Empty));

        outline.GetProperty("totalLines").GetInt32().Should().Be(0);
        outline.GetProperty("totalChars").GetInt32().Should().Be(0);
        outline.GetProperty("imports").GetArrayLength().Should().Be(0);
        outline.GetProperty("symbols").GetArrayLength().Should().Be(0);
        outline.GetProperty("truncated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void MaxEntries_caps_the_symbol_list_and_sets_truncated()
    {
        StringBuilder builder = new();
        for (int i = 0; i < 10; i++)
            builder.AppendLine($"export const value{i} = {i};");

        string content = builder.ToString();

        JsonElement uncapped = Parse(SandboxFileOutline.BuildOutline(content, maxEntries: 200));
        uncapped.GetProperty("symbols").GetArrayLength().Should().Be(10);
        uncapped.GetProperty("truncated").GetBoolean().Should().BeFalse();

        JsonElement capped = Parse(SandboxFileOutline.BuildOutline(content, maxEntries: 3));
        capped.GetProperty("symbols").GetArrayLength().Should().Be(3);
        capped.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Garbage_input_never_throws()
    {
        string[] garbageInputs =
        [
            "\0\0\0binary garbage￿",
            new string('x', 500_000),
            "export const \n\n\n === !!! ???",
            "<Composition id= this is not valid jsx at all >>>",
            "import import import from from from",
        ];

        foreach (string garbage in garbageInputs)
        {
            System.Action act = () => SandboxFileOutline.BuildOutline(garbage);
            act.Should().NotThrow();
        }
    }

    [Fact]
    public void Null_content_never_throws_and_is_treated_as_empty()
    {
        JsonElement outline = Parse(SandboxFileOutline.BuildOutline(null!));
        outline.GetProperty("totalLines").GetInt32().Should().Be(0);
    }
}
