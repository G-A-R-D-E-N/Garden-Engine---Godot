using System.IO;
using System.Linq;
using Xunit;

namespace OpenCw.Ui.Tests;

public class FirstPlayableRouteTests
{
    static string Source(params string[] path)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(FirstPlayableRouteTests).Assembly.Location)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "opencw", "scripts"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "opencw" }.Concat(path).ToArray()));
    }

    static string NodeSection(string scene, string header)
    {
        int start = scene.IndexOf(header, StringComparison.Ordinal);
        Assert.True(start >= 0, "section missing from the scene: " + header);
        int next = scene.IndexOf("\n[", start + header.Length, StringComparison.Ordinal);
        return scene[start..(next < 0 ? scene.Length : next)];
    }

    static void AssertNodeRuns(string scene, string header, string scriptPath)
    {
        string section = NodeSection(scene, header);
        const string marker = "script = ExtResource(\"";
        int at = section.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, "section has no script attached: " + header);
        int idStart = at + marker.Length;
        string id = section[idStart..section.IndexOf('"', idStart)];
        Assert.Contains("[ext_resource type=\"Script\" path=\"" + scriptPath + "\" id=\"" + id + "\"]", scene);
    }

    static string Body(string text, string header)
    {
        int start = text.IndexOf(header, StringComparison.Ordinal);
        Assert.True(start >= 0, "declaration missing: " + header);
        int open = text.IndexOf('{', start);
        Assert.True(open >= 0, "declaration has no body: " + header);
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return text[(open + 1)..i];
        }
        Assert.True(false, "declaration body is not closed: " + header);
        return "";
    }

    static void Ordered(string body, params string[] calls)
    {
        int previous = -1;
        foreach (var call in calls)
        {
            int at = body.IndexOf(call, StringComparison.Ordinal);
            Assert.True(at >= 0, "the body never calls " + call);
            Assert.True(at > previous, "the body calls " + call + " out of order");
            previous = at;
        }
    }

    [Fact]
    public void TheGameStartsOnTheMainMenu()
    {
        Assert.Contains("run/main_scene=\"res://scenes/main_menu.tscn\"", Source("project.godot"));
        Assert.Contains("path=\"res://scripts/UI/MainMenu.cs\"", Source("scenes", "main_menu.tscn"));
    }

    [Fact]
    public void NewGameResetsStateAndOpensThePrewarSanctuary()
    {
        var menu = Source("scripts", "UI", "MainMenu.cs");
        Assert.Contains("NewGameScene { get; set; } = \"res://scenes/sanctuary_prewar.tscn\"", menu);
        var press = Body(menu, "private void OnMainPressed(FoEntry entry)");
        Assert.Contains("case \"new\":", press);
        Ordered(press,
            "ResourceLoader.Load<PackedScene>(NewGameScene)?.Instantiate()",
            "scene.Connect(Node.SignalName.TreeEntered, Callable.From(OnPendingNewGameDestinationEnteredTree)",
            "GetTree().ChangeSceneToNode(scene)");
        Assert.DoesNotContain("GameSaveService.ResetForNewGame", press);
        var entered = Body(menu, "private static void OnPendingNewGameDestinationEnteredTree()");
        Ordered(entered, "s_pendingNewGameDestination.Adopted()", "GameSaveService.ResetForNewGame");
    }

    [Fact]
    public void TheSanctuarySceneHostsThePlayerUnderThePrewarSanctuaryScript()
    {
        var scene = Source("scenes", "sanctuary_prewar.tscn");
        Assert.Contains("path=\"res://scripts/PrewarSanctuary.cs\"", scene);
        Assert.Contains("path=\"res://scenes/Player.tscn\"", scene);
        Assert.Contains("[node name=\"Player\" parent=\".\" instance=", scene);
    }

    [Fact]
    public void TheSanctuaryStreamsItsWorldspaceAroundThePlayerAndBindsDoors()
    {
        var host = Source("scripts", "PrewarSanctuary.cs");
        Assert.Contains("GetNode<PlayerController>(\"Player\")", host);
        Assert.Contains("new WorldspaceStreamer", host);
        Assert.Contains("ManifestPath = ManifestPath", host);
        Assert.Contains("TargetPath = new NodePath(\"../Player\")", host);
        Assert.Contains("AddChild(_streamer);", host);
        Assert.Contains("AddChild(doors);", host);
        Assert.Contains("doors.Bind(_player.GetNode<PlayerInteraction>(\"Interaction\"))", host);
        Assert.Contains("interaction.Activated += Activate", Source("scripts", "DoorToggle.cs"));
    }

    [Fact]
    public void ThePlayerSceneCarriesInputCameraCollisionAndInteraction()
    {
        var player = Source("scenes", "Player.tscn");
        Assert.Contains("[node name=\"Player\" type=\"CharacterBody3D\"]", player);
        AssertNodeRuns(player, "[node name=\"Player\" type=\"CharacterBody3D\"]", "res://scripts/Player/PlayerController.cs");
        Assert.Contains("[node name=\"Collision\" type=\"CollisionShape3D\" parent=\".\"]", player);
        Assert.Contains("[node name=\"Input\" type=\"Node\" parent=\".\"]", player);
        AssertNodeRuns(player, "[node name=\"Input\" type=\"Node\" parent=\".\"]", "res://scripts/Player/PlayerInput.cs");
        Assert.Contains("[node name=\"Interaction\" type=\"Node3D\" parent=\".\"]", player);
        AssertNodeRuns(player, "[node name=\"Interaction\" type=\"Node3D\" parent=\".\"]", "res://scripts/Player/Interaction/PlayerInteraction.cs");
        Assert.Contains("[node name=\"CameraRig\" type=\"Node3D\" parent=\".\"]", player);
        Assert.Contains("[node name=\"Camera3D\" type=\"Camera3D\" parent=\"CameraRig\"]", player);
        AssertNodeRuns(player, "[node name=\"CameraRig\" type=\"Node3D\" parent=\".\"]", "res://scripts/Player/Camera/CameraRig.cs");
    }
}
