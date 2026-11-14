' Tests für Abschnitt 7: Fensterberechnung, Plugins (DLL-Schnittstelle und Skripte), Profile teilen.
Imports System.Collections.Concurrent
Imports StreamDeck.Core
Imports Xunit

Public Class WindowLayoutTests
    <Fact>
    Public Sub MoveToAreaKeepsRelativePositionAndScales()
        Dim fromArea As New RectI(0, 0, 1920, 1040)
        Dim toArea As New RectI(1920, 0, 2560, 1400)
        Dim r = WindowLayout.MoveToArea(New RectI(960, 520, 480, 260), fromArea, toArea)
        Assert.Equal(1920 + 1280, r.X)
        Assert.Equal(700, r.Y)
        Assert.Equal(640, r.Width)
        Assert.Equal(350, r.Height)
    End Sub

    <Fact>
    Public Sub MoveToAreaKeepsWindowInside()
        Dim r = WindowLayout.MoveToArea(New RectI(1800, 900, 600, 400), New RectI(0, 0, 1920, 1040), New RectI(-1280, 0, 1280, 1000))
        Assert.True(r.X >= -1280 AndAlso r.X + r.Width <= 0)
        Assert.True(r.Y >= 0 AndAlso r.Y + r.Height <= 1000)
    End Sub

    <Fact>
    Public Sub PercentOfHalves()
        Dim area As New RectI(100, 0, 2000, 1000)
        Dim left = WindowLayout.PercentOf(area, 0, 0, 50, 100)
        Dim right = WindowLayout.PercentOf(area, 50, 0, 50, 100)
        Assert.Equal(New RectI(100, 0, 1000, 1000), left)
        Assert.Equal(New RectI(1100, 0, 1000, 1000), right)
        Dim clamped = WindowLayout.PercentOf(area, 80, 0, 50, 100)   ' Breite wird auf den Rest begrenzt
        Assert.Equal(400, clamped.Width)
    End Sub

    <Fact>
    Public Sub MonitorIndexValidates()
        Assert.Equal(1, WindowLayout.MonitorIndex(2, 3))
        Assert.Throws(Of ArgumentException)(Function() WindowLayout.MonitorIndex(4, 3))
        Assert.Throws(Of ArgumentException)(Function() WindowLayout.MonitorIndex(0, 3))
    End Sub

    <Fact>
    Public Sub DescribeWindowAndPlugin()
        Assert.Equal("obs64 → Monitor 2", ActionTypes.Describe(New ActionConfig With {.ActionType = "window", .Op = "monitor", .Monitor = 2, .Path = "obs64"}))
        Assert.Equal("aktives Fenster maximieren", ActionTypes.Describe(New ActionConfig With {.ActionType = "window", .Op = "maximize"}))
        Assert.Equal("Plugin Hue an", ActionTypes.Describe(New ActionConfig With {.ActionType = "plugin", .Plugin = "Hue", .Args = "an"}))
    End Sub
End Class

Public Class PluginTests
    Private Class EchoPlugin
        Implements IDeckPlugin
        Public ReadOnly Calls As New ConcurrentQueue(Of String)
        Public ReadOnly Property Name As String = "Echo" Implements IDeckPlugin.Name
        Public ReadOnly Property Description As String = "gibt Argumente zurück" Implements IDeckPlugin.Description
        Public Function ExecuteAsync(args As String, context As ActionContext) As Task Implements IDeckPlugin.ExecuteAsync
            Calls.Enqueue($"{context.KeyIndex}:{args}")
            Return Task.CompletedTask
        End Function
    End Class

    Private Shared Function TempDir() As String
        Dim d = IO.Path.Combine(IO.Path.GetTempPath(), "sd-plugins-" & Guid.NewGuid().ToString("N"))
        IO.Directory.CreateDirectory(d)
        Return d
    End Function

    <Fact>
    Public Async Function DllStylePluginIsCalledByName() As Task
        Dim dir = TempDir()
        Try
            Dim host As New PluginHost(dir, Sub(m) Return)
            Dim echo As New EchoPlugin()
            host.Add(echo)
            Dim r As New ActionRunner()
            r.Register("plugin", host)
            Await r.Fire(New ActionConfig With {.ActionType = "plugin", .Plugin = "echo", .Args = "hallo"}, 7)
            AssertSeq({"7:hallo"}, echo.Calls.ToArray())
            Assert.Contains(host.Available(), Function(p) p.Name = "Echo" AndAlso p.Description = "gibt Argumente zurück")
        Finally
            IO.Directory.Delete(dir, True)
        End Try
    End Function

    <Fact>
    Public Async Function ScriptPluginRunsAndReportsErrors() As Task
        Dim dir = TempDir()
        Try
            Dim outFile = IO.Path.Combine(dir, "out.txt")
            IO.File.WriteAllText(IO.Path.Combine(dir, "schreiben.cmd"), $"@>""{outFile}"" echo %1 %STREAMDECK_KEY%")   ' Umleitung vorn, sonst liest cmd "4>" als Kanal 4
            IO.File.WriteAllText(IO.Path.Combine(dir, "kaputt.cmd"), "@echo Fehlertext 1>&2" & vbCrLf & "@exit /b 3")
            Dim host As New PluginHost(dir, Sub(m) Return)
            Dim r As New ActionRunner()
            r.Register("plugin", host)
            Dim logs As New ConcurrentQueue(Of String)
            AddHandler r.Log, Sub(m) logs.Enqueue(m)
            Await r.Fire(New ActionConfig With {.ActionType = "plugin", .Plugin = "schreiben", .Args = "test"}, 4)
            Assert.Equal("test 4", IO.File.ReadAllText(outFile).Trim())
            Await r.Fire(New ActionConfig With {.ActionType = "plugin", .Plugin = "kaputt"}, 5)
            Assert.Contains(logs, Function(l) l.Contains("Code 3") AndAlso l.Contains("Fehlertext"))
            Await r.Fire(New ActionConfig With {.ActionType = "plugin", .Plugin = "gibtsnicht"})
            Assert.Contains(logs, Function(l) l.Contains("nicht gefunden"))
            Assert.Contains(host.Available(), Function(p) p.Name = "schreiben")
        Finally
            IO.Directory.Delete(dir, True)
        End Try
    End Function

    <Fact>
    Public Sub ScriptLookupRejectsPathTricks()
        Dim host As New PluginHost(IO.Path.GetTempPath(), Sub(m) Return)
        Assert.Null(host.FindScript("..\..\Windows\notepad"))
        Assert.Null(host.FindScript(""))
    End Sub

    <Fact>
    Public Sub BuildScriptStartPerType()
        Dim ps = PluginHost.BuildScriptStart("C:\p\a b.ps1", "x y", 2)
        Assert.Equal("powershell.exe", ps.FileName)
        Assert.Equal("-NoProfile -ExecutionPolicy Bypass -File ""C:\p\a b.ps1"" x y", ps.Arguments)
        Assert.Equal("2", ps.Environment("STREAMDECK_KEY"))
        Dim cmd = PluginHost.BuildScriptStart("C:\p\a b.cmd", "", 0)
        Assert.Equal("/c """"C:\p\a b.cmd""""", cmd.Arguments)
        Assert.True(cmd.CreateNoWindow)
    End Sub
End Class

Public Class ProfileShareTests
    <Fact>
    Public Sub ExportImportRoundTripWithUniqueName()
        Dim src = ConfigStore.CreateDefault()
        Dim json = ConfigEdit.ExportProfile(src.Profiles(0))
        Assert.Contains("""streamDeckProfile"": 1", json)

        Dim target = ConfigStore.CreateDefault()
        Dim p = ConfigEdit.ImportProfile(target, json)
        Assert.Equal("Standard 2", p.Name)
        Assert.Equal(2, target.Profiles.Count)
        Assert.Equal(src.Profiles(0).Pages.Count, p.Pages.Count)
        Assert.Equal("Mute", p.Pages(0).GetKey(0).Label)
    End Sub

    <Fact>
    Public Sub ImportAcceptsBareProfileAndRejectsWholeConfig()
        Dim target = ConfigStore.CreateDefault()
        Dim bare = Text.Json.JsonSerializer.Serialize(New ProfileConfig With {.Name = "Neu", .Pages = New List(Of PageConfig) From {New PageConfig With {.Name = "A"}}}, ConfigStore.JsonOptions)
        Assert.Equal("Neu", ConfigEdit.ImportProfile(target, bare).Name)
        Dim whole = ConfigStore.Serialize(ConfigStore.CreateDefault())
        Assert.Throws(Of FormatException)(Function() ConfigEdit.ImportProfile(target, whole))
    End Sub
End Class
