using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.UI.Services;
using VyralBot.UI.ViewModels;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>Sharecode, offline-code, path, rating and share-document helpers.</summary>
public class MarketplaceServiceTests
{
    [Fact]
    public void GenerateShareCode_matches_prefix_and_body_format()
    {
        for (int i = 0; i < 50; i++)
        {
            string code = MarketplaceService.GenerateShareCode();
            Assert.StartsWith(MarketplaceService.SharecodePrefix, code);
            string body = code[MarketplaceService.SharecodePrefix.Length..];
            Assert.Equal(MarketplaceService.CodeBodyLength, body.Length);
            Assert.All(body, c => Assert.Contains(c, MarketplaceService.CodeAlphabet));
        }
    }

    [Fact]
    public void Generated_codes_do_not_collide()
    {
        var seen = new HashSet<string>();
        for (int i = 0; i < 500; i++)
        {
            Assert.True(seen.Add(MarketplaceService.GenerateShareCode()));
        }
    }

    [Fact]
    public void TryNormalizeShareCode_accepts_full_code()
    {
        Assert.True(MarketplaceService.TryNormalizeShareCode("VYRAL-7K3F9M2Q4D", out string code));
        Assert.Equal("VYRAL-7K3F9M2Q4D", code);
    }

    [Fact]
    public void TryNormalizeShareCode_accepts_lowercase_and_bare_body()
    {
        Assert.True(MarketplaceService.TryNormalizeShareCode("  7k3f9m2q4d ", out string code));
        Assert.Equal("VYRAL-7K3F9M2Q4D", code);
    }

    [Fact]
    public void TryNormalizeShareCode_fixes_lookalike_characters()
    {
        Assert.True(MarketplaceService.TryNormalizeShareCode("vyral-oilu1abcde", out string code));
        Assert.Equal("VYRAL-011V1ABCDE", code);
    }

    [Fact]
    public void TryNormalizeShareCode_accepts_a_url_containing_a_code()
    {
        Assert.True(MarketplaceService.TryNormalizeShareCode(
            "https://raw.githubusercontent.com/ryanthabot/vyral/main/marketplace/commands/VYRAL-7K3F9M2Q4D.json",
            out string code));
        Assert.Equal("VYRAL-7K3F9M2Q4D", code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("VYRAL-TOOSHORT")]
    [InlineData("hello world")]
    public void TryNormalizeShareCode_rejects_garbage(string? input)
        => Assert.False(MarketplaceService.TryNormalizeShareCode(input, out _));

    [Fact]
    public void TryNormalizeShareCode_rejects_offline_codes()
        => Assert.False(MarketplaceService.TryNormalizeShareCode(MarketplaceService.OfflinePrefix + "abc", out _));

    [Fact]
    public void Offline_code_round_trips_unicode_and_large_payloads()
    {
        string flowJson = JsonSerializer.Serialize(new
        {
            name = "Giveaway 🎉 ünïcode",
            nodes = Enumerable.Range(0, 400).Select(n => new
            {
                id = $"node{n}",
                blockType = "send_message",
                config = new Dictionary<string, string> { ["text"] = new string('é', 40) + n }
            })
        });

        string code = MarketplaceService.EncodeOfflineCode(flowJson);
        Assert.StartsWith(MarketplaceService.OfflinePrefix, code);
        Assert.True(MarketplaceService.LooksLikeOfflineCode(code));
        Assert.True(MarketplaceService.TryDecodeOfflineCode(code, out string decoded));
        Assert.Equal(flowJson, decoded);
    }

    [Fact]
    public void TryDecodeOfflineCode_rejects_junk()
    {
        Assert.False(MarketplaceService.TryDecodeOfflineCode(null, out _));
        Assert.False(MarketplaceService.TryDecodeOfflineCode("not a code", out _));
        Assert.False(MarketplaceService.TryDecodeOfflineCode(MarketplaceService.OfflinePrefix + "!!!" , out _));
    }

    [Fact]
    public void ApplyVote_counts_up_and_down()
    {
        Assert.Equal((6, 1), MarketplaceService.ApplyVote(thumbsUp: true, currentUp: 5, currentDown: 1));
        Assert.Equal((5, 2), MarketplaceService.ApplyVote(thumbsUp: false, currentUp: 5, currentDown: 1));
    }

    [Fact]
    public void File_paths_follow_the_marketplace_layout()
    {
        Assert.Equal("marketplace/commands/VYRAL-7K3F9M2Q4D.json", MarketplaceService.CommandFilePath("VYRAL-7K3F9M2Q4D"));
        Assert.Equal("marketplace/ratings/VYRAL-7K3F9M2Q4D.json", MarketplaceService.RatingFilePath("VYRAL-7K3F9M2Q4D"));
    }

    [Fact]
    public void Raw_and_listing_urls_point_at_the_configured_repo()
    {
        var settings = new MarketplaceSettings
        {
            GitHubOwner = "ryanthabot",
            GitHubRepo = "vyral",
            Branch = "main"
        };

        Assert.Equal(
            "https://raw.githubusercontent.com/ryanthabot/vyral/main/marketplace/commands/VYRAL-7K3F9M2Q4D.json",
            MarketplaceService.RawFileUrl(settings, MarketplaceService.CommandFilePath("VYRAL-7K3F9M2Q4D")));
        Assert.Equal(
            "https://api.github.com/repos/ryanthabot/vyral/contents/marketplace/commands?ref=main",
            MarketplaceService.ListApiUrl(settings, "marketplace/commands"));
    }

    [Fact]
    public void Share_document_round_trips_with_category_normalization()
    {
        var snapshot = new FlowEditorSnapshotDto
        {
            Id = "abc123",
            Name = "Giveaway Roll",
            Trigger = new FlowTriggerDto { Type = "command", CommandName = "roll" },
            Nodes = new List<FlowNodeDto>
            {
                new()
                {
                    Id = "n1",
                    BlockType = "send_message",
                    Label = "Send",
                    X = 120,
                    Y = 240,
                    Config = new Dictionary<string, string> { ["text"] = "{user} won!" }
                }
            }
        };

        string json = MarketplaceService.BuildShareDocumentJson(
            snapshot, "VYRAL-7K3F9M2Q4D", "  Giveaway Roll  ", "  Rolls a winner  ",
            "Games", "  PixelQueen ");

        var document = MarketplaceService.ParseShareDocument(json);
        Assert.NotNull(document);
        Assert.Equal("VYRAL-7K3F9M2Q4D", document!.Sharecode);
        Assert.Equal("Giveaway Roll", document.Name);
        Assert.Equal("Rolls a winner", document.Description);
        Assert.Equal(MarketplaceCategories.Games, document.Category);
        Assert.Equal("PixelQueen", document.Author);
        Assert.Contains("Giveaway Roll", document.Flow);

        // Unmapped categories normalize to "other"; junk documents are rejected.
        string otherJson = MarketplaceService.BuildShareDocumentJson(snapshot, "VYRAL-7K3F9M2Q4D", "x", null, "nonsense", null);
        Assert.Equal(MarketplaceCategories.Other, MarketplaceService.ParseShareDocument(otherJson)!.Category);
        Assert.Null(MarketplaceService.ParseShareDocument("{\"nope\":1}"));
    }

    [Fact]
    public void MarketplaceSettings_defaults_and_configured_flags()
    {
        var unset = new MarketplaceSettings { ShareWebhookUrl = null, ApiKey = null };
        Assert.False(unset.IsShareConfigured);
        Assert.False(unset.IsRatingConfigured);

        var set = new MarketplaceSettings
        {
            ShareWebhookUrl = "https://api.botghost.com/webhook/1/abc",
            RatingWebhookUrl = "https://api.botghost.com/webhook/1/def",
            ApiKey = "key"
        };
        Assert.True(set.IsShareConfigured);
        Assert.True(set.IsRatingConfigured);
        Assert.Equal("ryanthabot", set.GitHubOwner);
        Assert.Equal("vyral", set.GitHubRepo);
        Assert.Equal("main", set.Branch);
    }
}

/// <summary>
/// Offline-path tests for the shared import resolver (no network, no DB —
/// the IDataService dependency is only touched by the online fetch branch).
/// </summary>
public class ImportResolutionTests
{
    private static MarketplaceService CreateService() =>
        new(null!, NullLogger<MarketplaceService>.Instance);

    private static string SampleOfflineCode()
    {
        var snapshot = new FlowEditorSnapshotDto
        {
            Id = "abc123",
            Name = "Ticket Flow",
            Nodes = new List<FlowNodeDto>
            {
                new()
                {
                    Id = "n1",
                    BlockType = "send_message",
                    X = 12,
                    Y = 34,
                    Config = new Dictionary<string, string> { ["text"] = "hi" }
                }
            }
        };
        return MarketplaceService.EncodeOfflineCode(FlowSnapshotService.BuildDocument(snapshot));
    }

    [Fact]
    public async Task ResolveImportInputAsync_decodes_offline_codes()
    {
        var result = await CreateService().ResolveImportInputAsync(SampleOfflineCode());
        Assert.True(result.Success);
        Assert.Equal("offline share code", result.Label);
        Assert.Equal("Ticket Flow", result.Snapshot!.Name);
    }

    [Fact]
    public async Task ResolveImportInputAsync_rejects_corrupt_offline_codes()
    {
        var result = await CreateService().ResolveImportInputAsync(MarketplaceService.OfflinePrefix + "!!!");
        Assert.False(result.Success);
        Assert.Contains("offline code", result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveImportInputAsync_asks_for_a_code_on_empty_input(string? input)
    {
        var result = await CreateService().ResolveImportInputAsync(input!);
        Assert.False(result.Success);
        Assert.Contains("Paste a share code", result.Error);
    }

    [Fact]
    public async Task ResolveImportInputAsync_rejects_non_code_text()
    {
        var result = await CreateService().ResolveImportInputAsync("hello world");
        Assert.False(result.Success);
        Assert.Contains("does not look like a share code", result.Error);
    }
}

/// <summary>
/// Round-trips between the editor snapshot DTO and the portable .flow.json
/// document — including the file shape (position{x,y} / parameters{...}) that
/// must survive import intact.
/// </summary>
public class FlowSnapshotServiceTests
{
    private static FlowEditorSnapshotDto SampleSnapshot() => new()
    {
        Id = "abc123",
        Name = "Ticket Flow",
        Description = "Opens a ticket",
        Enabled = true,
        Trigger = new FlowTriggerDto { Type = "command", CommandName = "ticket", Platform = "Discord" },
        Options = new List<FlowOptionDto>
        {
            new() { Type = "cooldown", Value = "10" },
            new() { Type = "dm_only", Value = "" } // flag row must normalize to "true"
        },
        Parameters = new List<FlowParameterDto>
        {
            new() { Name = "subject", Type = "text", Description = "Ticket subject", Required = true }
        },
        EntryNodeId = "n1",
        Nodes = new List<FlowNodeDto>
        {
            new()
            {
                Id = "n1",
                BlockType = "send_message",
                Label = "Send",
                X = 120.5,
                Y = 240.25,
                Config = new Dictionary<string, string> { ["text"] = "hello {user}", ["Embed"] = "{\"title\":\"Hi\"}" },
                Next = "n2"
            },
            new()
            {
                Id = "n2",
                BlockType = "switch",
                Label = "Switch",
                X = 300,
                Y = 200,
                Config = new Dictionary<string, string> { ["expression"] = "{choice}" },
                Branches = new Dictionary<string, string> { ["a"] = "n1", ["b"] = "n1" },
                BranchError = "n1"
            }
        }
    };

    [Fact]
    public void BuildDocument_then_ParseDocument_preserves_nodes_config_positions_and_routing()
    {
        var snapshot = SampleSnapshot();
        string document = FlowSnapshotService.BuildDocument(snapshot);

        // The document carries the file shape.
        using (var check = JsonDocument.Parse(document))
        {
            var n1 = check.RootElement.GetProperty("nodes")[0];
            Assert.Equal(120.5, n1.GetProperty("position").GetProperty("x").GetDouble());
            Assert.Equal("hello {user}", n1.GetProperty("parameters").GetProperty("text").GetString());

            var connections = check.RootElement.GetProperty("connections");
            Assert.Contains(connections.EnumerateArray(), c =>
                c.GetProperty("source").GetString() == "n1:next" && c.GetProperty("target").GetString() == "n2:in");
            Assert.Contains(connections.EnumerateArray(), c =>
                c.GetProperty("source").GetString() == "n2:case:a" && c.GetProperty("target").GetString() == "n1:in");
        }

        var parsed = FlowSnapshotService.ParseDocument(document);
        Assert.NotNull(parsed);
        Assert.Equal("Ticket Flow", parsed!.Name);
        Assert.Equal("command", parsed.Trigger!.Type);
        Assert.Equal("ticket", parsed.Trigger.CommandName);
        Assert.Equal("n1", parsed.EntryNodeId);

        var roundTrippedN1 = parsed.Nodes!.Single(n => n.Id == "n1");
        Assert.Equal(120.5, roundTrippedN1.X);
        Assert.Equal(240.25, roundTrippedN1.Y);
        Assert.Equal("hello {user}", roundTrippedN1.Config["text"]);
        Assert.Equal("{\"title\":\"Hi\"}", roundTrippedN1.Config["Embed"]);
        Assert.Equal("n2", roundTrippedN1.Next);

        var roundTrippedN2 = parsed.Nodes!.Single(n => n.Id == "n2");
        Assert.Equal("n1", roundTrippedN2.Branches!["a"]);
        Assert.Equal("n1", roundTrippedN2.Branches!["b"]);
        Assert.Equal("n1", roundTrippedN2.BranchError);
    }

    [Fact]
    public void ParseDocument_reads_legacy_file_shape_with_connections()
    {
        // A document that only carries positions/parameters/connections in the
        // raw file shape (like older exports) must fully restore the canvas —
        // this is the regression case for the dropped position/parameter bug.
        const string legacyDocument = """
            {
              "id": "legacy1",
              "name": "Legacy Flow",
              "trigger": { "type": "manual" },
              "entryNodeId": "a1",
              "nodes": [
                { "id": "a1", "blockType": "send_message", "label": "A", "position": { "x": 42, "y": 99 }, "parameters": { "text": "hi" } },
                { "id": "b1", "blockType": "wait", "label": "B", "position": { "x": 7, "y": 8 }, "parameters": {} }
              ],
              "connections": [ { "id": "c1", "source": "a1:next", "target": "b1:in" } ]
            }
            """;

        var parsed = FlowSnapshotService.ParseDocument(legacyDocument);
        Assert.NotNull(parsed);
        Assert.Equal("Legacy Flow", parsed!.Name);

        var a1 = parsed.Nodes!.Single(n => n.Id == "a1");
        Assert.Equal(42, a1.X);
        Assert.Equal(99, a1.Y);
        Assert.Equal("hi", a1.Config["text"]);
        Assert.Equal("b1", a1.Next);
    }

    [Fact]
    public void ParseDocument_rejects_non_flow_json()
    {
        Assert.Null(FlowSnapshotService.ParseDocument("not json at all"));
        Assert.Null(FlowSnapshotService.ParseDocument("[1, 2, 3]"));
    }
}
