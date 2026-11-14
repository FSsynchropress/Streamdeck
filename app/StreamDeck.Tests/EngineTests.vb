Imports System.Collections.Concurrent
Imports System.Threading
Imports StreamDeck.Core
Imports Xunit

' ====================================================================== Simuliertes Gerät

''' <summary>Ein simulierter COM-Port. Mit IsDeck=True antwortet er wie die Firmware.</summary>
Friend Class FakePort
    Public Property Name As String
    Public Property IsDeck As Boolean = True
    Public Property HelloLine As String = "HELLO STREAMDECK 1 15"
    Public Property AnswerPing As Boolean = True
    Public Property OpenFails As Boolean
    Public Property Unplugged As Boolean
    ''' <summary>Simuliert langsame Ports (z. B. Bluetooth), deren Öffnen dauert.</summary>
    Public Property OpenDelayMs As Integer
    Public ReadOnly Received As New ConcurrentQueue(Of String)
    Public ReadOnly ToHost As New BlockingCollection(Of String)

    Public Sub Send(line As String)
        ToHost.Add(line)
    End Sub

    Public Function PingCount() As Integer
        Return Received.Where(Function(l) l = "PING").Count()
    End Function
End Class

Friend Class FakeLink
    Implements ISerialLink

    Private ReadOnly _port As FakePort
    Private _open As Boolean

    Public Sub New(port As FakePort)
        _port = port
    End Sub

    Public ReadOnly Property PortName As String Implements ISerialLink.PortName
        Get
            Return _port.Name
        End Get
    End Property

    Public Sub Open() Implements ISerialLink.Open
        If _port.OpenDelayMs > 0 Then Thread.Sleep(_port.OpenDelayMs)
        If _port.OpenFails OrElse _port.Unplugged Then Throw New UnauthorizedAccessException("belegt")
        _open = True
    End Sub

    Public Sub WriteLine(line As String) Implements ISerialLink.WriteLine
        If _port.Unplugged OrElse Not _open Then Throw New IO.IOException("weg")
        _port.Received.Enqueue(line)
        If Not _port.IsDeck Then Return
        If line = "HELLO?" Then _port.ToHost.Add(_port.HelloLine)
        If line = "PING" AndAlso _port.AnswerPing Then _port.ToHost.Add("PONG")
    End Sub

    Public Function ReadLine(timeoutMs As Integer) As String Implements ISerialLink.ReadLine
        If _port.Unplugged Then Throw New IO.IOException("weg")
        Dim line As String = Nothing
        If _port.ToHost.TryTake(line, timeoutMs) Then Return line
        Return Nothing
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        _open = False
    End Sub
End Class

Friend Class FakeFactory
    Implements ISerialLinkFactory

    Public ReadOnly Ports As New List(Of FakePort)

    Public Function GetPortNames() As String() Implements ISerialLinkFactory.GetPortNames
        SyncLock Ports
            Return Ports.Where(Function(p) Not p.Unplugged).Select(Function(p) p.Name).ToArray()
        End SyncLock
    End Function

    Public Function Create(portName As String) As ISerialLink Implements ISerialLinkFactory.Create
        SyncLock Ports
            Return New FakeLink(Ports.First(Function(p) p.Name = portName))
        End SyncLock
    End Function
End Class

Friend Module Wait
    Public Function Until(condition As Func(Of Boolean), Optional timeoutMs As Integer = 5000) As Boolean
        Return SpinWait.SpinUntil(condition, timeoutMs)
    End Function
End Module

Public Class DeckConnectionTests
    Private Shared Function FastConnection(factory As FakeFactory) As DeckConnection
        Return New DeckConnection(factory) With {
            .HelloTimeoutMs = 300, .PingIntervalMs = 50, .DeadTimeoutMs = 400, .RescanDelayMs = 50, .ReadSliceMs = 10}
    End Function

    <Fact>
    Public Sub FindsDeckAmongOtherPortsAndReportsKeys()
        Dim f As New FakeFactory()
        f.Ports.Add(New FakePort With {.Name = "COM1", .IsDeck = False})        ' stummes Gerät
        f.Ports.Add(New FakePort With {.Name = "COM2", .OpenFails = True})      ' belegt
        Dim deck As New FakePort With {.Name = "COM7"}
        f.Ports.Add(deck)

        Using c = FastConnection(f)
            Dim downs As New ConcurrentQueue(Of Integer)
            Dim ups As New ConcurrentQueue(Of Integer)
            AddHandler c.KeyDown, Sub(k) downs.Enqueue(k)
            AddHandler c.KeyUp, Sub(k) ups.Enqueue(k)
            c.Start()

            Assert.True(Wait.Until(Function() c.IsConnected), "nicht verbunden")
            Assert.Equal("COM7", c.PortName)
            Assert.Equal(15, c.KeyCount)
            Assert.Contains("HELLO?", f.Ports(0).Received)                     ' auch das fremde Gerät wurde gefragt

            deck.Send("D 4")
            deck.Send("INFO irgendwas")                                          ' Diagnosezeilen stören nicht
            deck.Send("U 4")
            Assert.True(Wait.Until(Function() ups.Count = 1))
            AssertSeq({4}, downs.ToArray())
            AssertSeq({4}, ups.ToArray())
            Assert.True(Wait.Until(Function() deck.PingCount() >= 3), "kein regelmäßiges PING")
        End Using
    End Sub

    <Fact>
    Public Sub ReconnectsAfterUnplug()
        Dim f As New FakeFactory()
        Dim deck As New FakePort With {.Name = "COM3"}
        f.Ports.Add(deck)
        Using c = FastConnection(f)
            Dim connects = 0, disconnects = 0
            AddHandler c.Connected, Sub(p) Interlocked.Increment(connects)
            AddHandler c.Disconnected, Sub() Interlocked.Increment(disconnects)
            c.Start()
            Assert.True(Wait.Until(Function() connects = 1))

            deck.Unplugged = True
            Assert.True(Wait.Until(Function() disconnects = 1), "Abziehen nicht erkannt")
            Assert.False(c.IsConnected)
            Assert.False(c.Send("LED 1 2 3"))

            deck.Unplugged = False
            Assert.True(Wait.Until(Function() connects = 2), "nicht neu verbunden")
            Assert.True(c.Send("LED 1 2 3"))
            Assert.Contains("LED 1 2 3", deck.Received)
        End Using
    End Sub

    <Fact>
    Public Sub SilentDeviceIsDroppedAfterTimeout()
        Dim f As New FakeFactory()
        Dim deck As New FakePort With {.Name = "COM3", .AnswerPing = False}
        f.Ports.Add(deck)
        Using c = FastConnection(f)
            Dim disconnects = 0
            AddHandler c.Disconnected, Sub() Interlocked.Increment(disconnects)
            c.Start()
            Assert.True(Wait.Until(Function() c.IsConnected))
            Assert.True(Wait.Until(Function() disconnects >= 1, 3000), "Zeitüberschreitung nicht erkannt")
        End Using
    End Sub

    <Fact>
    Public Sub WrongProtocolVersionIsNotAccepted()
        Dim f As New FakeFactory()
        f.Ports.Add(New FakePort With {.Name = "COM3", .HelloLine = "HELLO STREAMDECK 2 15"})
        Using c = FastConnection(f)
            Dim logs As New ConcurrentQueue(Of String)
            AddHandler c.Log, Sub(m) logs.Enqueue(m)
            c.Start()
            Assert.True(Wait.Until(Function() logs.Any(Function(l) l.Contains("Protokollversion 2"))))
            Assert.False(c.IsConnected)
        End Using
    End Sub

    <Fact>
    Public Sub SlowPortDoesNotDelayDiscovery()
        Dim f As New FakeFactory()
        f.Ports.Add(New FakePort With {.Name = "COM15", .IsDeck = False, .OpenDelayMs = 4000})   ' wie ein Bluetooth-Port
        f.Ports.Add(New FakePort With {.Name = "COM19"})
        Using c = FastConnection(f)
            Dim sw = Diagnostics.Stopwatch.StartNew()
            c.Start()
            Assert.True(Wait.Until(Function() c.IsConnected, 3000))
            Assert.True(sw.ElapsedMilliseconds < 2000, $"Verbindung erst nach {sw.ElapsedMilliseconds} ms")
        End Using
    End Sub

    <Fact>
    Public Sub PortWithoutDeckIsNotProbedOverAndOver()
        Dim f As New FakeFactory()
        Dim other As New FakePort With {.Name = "COM1", .IsDeck = False}
        f.Ports.Add(other)
        Using c = FastConnection(f)
            c.FailedPortRetryMs = 60000
            c.Start()
            Thread.Sleep(1200)          ' viele Suchläufe (RescanDelay 50 ms)
            Assert.Equal(1, other.Received.Where(Function(l) l = "HELLO?").Count())

            ' Deck wird neu eingesteckt -> sofort gefunden, obwohl COM1 noch gesperrt ist
            Dim deck As New FakePort With {.Name = "COM9"}
            SyncLock f.Ports
                f.Ports.Add(deck)
            End SyncLock
            Assert.True(Wait.Until(Function() c.IsConnected, 2000))
            Assert.Equal("COM9", c.PortName)
        End Using
    End Sub

    <Fact>
    Public Sub StopEndsQuicklyWithoutPorts()
        Dim c = FastConnection(New FakeFactory())
        c.Start()
        Thread.Sleep(100)
        Dim sw = Diagnostics.Stopwatch.StartNew()
        c.Stop()
        Assert.True(sw.ElapsedMilliseconds < 2000)
    End Sub
End Class

' ====================================================================== Runner

Public Class ActionRunnerTests
    Private Shared Sub SyncLockAdd(list As List(Of String), s As String)
        SyncLock list
            list.Add(s)
        End SyncLock
    End Sub

    <Fact>
    Public Async Function MultiRunsStepsInOrderIncludingWait() As Task
        Dim r As New ActionRunner()
        Dim order As New List(Of String)
        r.Register("hotkey", DelegateActionHandler.FromSync(Sub(a) SyncLockAdd(order, a.Keys)))
        Dim multi As New ActionConfig With {.ActionType = "multi", .Actions = New List(Of ActionConfig) From {
            New ActionConfig With {.ActionType = "hotkey", .Keys = "A"},
            New ActionConfig With {.ActionType = "wait", .Ms = 30},
            New ActionConfig With {.ActionType = "hotkey", .Keys = "B"},
            New ActionConfig With {.ActionType = "multi", .Actions = New List(Of ActionConfig) From {New ActionConfig With {.ActionType = "hotkey", .Keys = "C"}}}}}
        Dim sw = Diagnostics.Stopwatch.StartNew()
        Await r.Fire(multi, 2)
        AssertSeq({"A", "B", "C"}, order.ToArray())
        Assert.True(sw.ElapsedMilliseconds >= 25)
    End Function

    <Fact>
    Public Async Function ErrorsAreLoggedNotThrown() As Task
        Dim r As New ActionRunner()
        Dim logs As New ConcurrentQueue(Of String)
        AddHandler r.Log, Sub(m) logs.Enqueue(m)
        r.Register("hotkey", DelegateActionHandler.FromSync(Sub(a) Throw New InvalidOperationException("kaputt")))
        Await r.Fire(New ActionConfig With {.ActionType = "hotkey", .Keys = "X"}, 3)
        Await r.Fire(New ActionConfig With {.ActionType = "gibtsnicht"})
        Await r.Fire(Nothing)
        Assert.Contains(logs, Function(l) l.Contains("Taste 3") AndAlso l.Contains("kaputt"))
        Assert.Contains(logs, Function(l) l.Contains("gibtsnicht"))
    End Function

    <Fact>
    Public Async Function FailingStepDoesNotStopMulti() As Task
        Dim r As New ActionRunner()
        Dim ran As New List(Of String)
        r.Register("hotkey", DelegateActionHandler.FromSync(Sub(a) SyncLockAdd(ran, a.Keys)))
        r.Register("text", DelegateActionHandler.FromSync(Sub(a) Throw New Exception("x")))
        Await r.Fire(New ActionConfig With {.ActionType = "multi", .Actions = New List(Of ActionConfig) From {
            New ActionConfig With {.ActionType = "text"}, New ActionConfig With {.ActionType = "hotkey", .Keys = "Z"}}})
        AssertSeq({"Z"}, ran.ToArray())
    End Function

    <Fact>
    Public Async Function DeepNestingIsStopped() As Task
        Dim r As New ActionRunner()
        Dim logs As New ConcurrentQueue(Of String)
        AddHandler r.Log, Sub(m) logs.Enqueue(m)
        Dim a As New ActionConfig With {.ActionType = "wait", .Ms = 0}
        For i = 1 To ActionRunner.MaxNesting + 3
            a = New ActionConfig With {.ActionType = "multi", .Actions = New List(Of ActionConfig) From {a}}
        Next
        Await r.Fire(a)
        Assert.Contains(logs, Function(l) l.Contains("verschachtelt"))
    End Function
End Class

' ====================================================================== Engine

Public Class DeckEngineTests
    Private _now As Long
    Private ReadOnly _fired As New ConcurrentQueue(Of String)

    Private Function MakeEngine(cfg As DeckConfig) As DeckEngine
        Dim r As New ActionRunner()
        r.Register("hotkey", DelegateActionHandler.FromSync(Sub(a) _fired.Enqueue(a.Keys)))
        Return New DeckEngine(cfg, r, Function() Interlocked.Read(_now), tickIntervalMs:=0)
    End Function

    Private Shared Function Hk(keys As String) As ActionConfig
        Return New ActionConfig With {.ActionType = "hotkey", .Keys = keys}
    End Function

    Private Shared Function MakeConfig() As DeckConfig
        Dim start As New PageConfig With {.Name = "Start"}
        start.SetKey(0, New KeyConfig With {.Label = "kurz", .Press = Hk("A")})
        start.SetKey(1, New KeyConfig With {.Label = "kurz+lang", .Press = Hk("B"), .LongPress = Hk("LONG")})
        start.SetKey(2, New KeyConfig With {.Press = New ActionConfig With {.ActionType = "profile", .Target = "Anderes"}})
        start.SetKey(14, New KeyConfig With {.Press = New ActionConfig With {.ActionType = "page", .Target = "Zwei"}})
        Dim two As New PageConfig With {.Name = "Zwei"}
        two.SetKey(0, New KeyConfig With {.Press = Hk("Z")})
        two.SetKey(14, New KeyConfig With {.Press = New ActionConfig With {.ActionType = "page", .Target = "@back"}})
        Dim cfg As New DeckConfig With {.LongPressMs = 500}
        cfg.Profiles.Add(New ProfileConfig With {.Name = "Standard", .Pages = New List(Of PageConfig) From {start, two}})
        cfg.Profiles.Add(New ProfileConfig With {.Name = "Anderes", .Pages = New List(Of PageConfig) From {New PageConfig With {.Name = "X"}}})
        Return cfg
    End Function

    <Fact>
    Public Sub ShortPressFiresOnDown()
        Using e = MakeEngine(MakeConfig())
            Dim states As New ConcurrentQueue(Of String)
            AddHandler e.KeyStateChanged, Sub(k, p) states.Enqueue($"{k}{If(p, "D", "U")}")
            e.OnKeyDown(0)
            Assert.True(Wait.Until(Function() _fired.Count = 1))
            Assert.True(e.IsHeld(0))
            e.OnKeyUp(0)
            Thread.Sleep(50)
            AssertSeq({"A"}, _fired.ToArray())
            AssertSeq({"0D", "0U"}, states.ToArray())
        End Using
    End Sub

    <Fact>
    Public Sub LongPressKeyShortWhenReleasedEarly()
        Using e = MakeEngine(MakeConfig())
            e.OnKeyDown(1)
            _now = 300
            e.Tick()
            Thread.Sleep(50)
            Assert.Empty(_fired)          ' noch nichts – könnte lang werden
            e.OnKeyUp(1)
            Assert.True(Wait.Until(Function() _fired.Count = 1))
            AssertSeq({"B"}, _fired.ToArray())
        End Using
    End Sub

    <Fact>
    Public Sub LongPressFiresWhileHeldAndNoShortAfterwards()
        Using e = MakeEngine(MakeConfig())
            e.OnKeyDown(1)
            _now = 600
            e.Tick()
            Assert.True(Wait.Until(Function() _fired.Count = 1))
            e.OnKeyUp(1)
            Thread.Sleep(50)
            AssertSeq({"LONG"}, _fired.ToArray())
        End Using
    End Sub

    <Fact>
    Public Sub PageKeysNavigateAndKeyKeepsItsActionUntilRelease()
        Using e = MakeEngine(MakeConfig())
            e.OnKeyDown(14)
            Assert.True(Wait.Until(Function() e.CurrentPage.Name = "Zwei"))
            e.OnKeyUp(14)                  ' Loslassen nach Seitenwechsel löst nichts auf der neuen Seite aus
            e.OnKeyDown(0)
            e.OnKeyUp(0)
            Assert.True(Wait.Until(Function() _fired.Count = 1))
            AssertSeq({"Z"}, _fired.ToArray())
            e.OnKeyDown(14)
            Assert.True(Wait.Until(Function() e.CurrentPage.Name = "Start"))
        End Using
    End Sub

    <Fact>
    Public Sub ProfileActionSwitchesAndRequestsSave()
        Using e = MakeEngine(MakeConfig())
            Dim saves = 0
            AddHandler e.ConfigChanged, Sub() Interlocked.Increment(saves)
            e.OnKeyDown(2)
            Assert.True(Wait.Until(Function() e.CurrentProfile.Name = "Anderes"))
            Assert.Equal("Anderes", e.Config.ActiveProfile)
            Assert.True(Wait.Until(Function() saves = 1))
        End Using
    End Sub

    <Fact>
    Public Sub UnassignedKeyAndDoubleDownAreHarmless()
        Using e = MakeEngine(MakeConfig())
            e.OnKeyDown(9)
            e.OnKeyDown(9)
            e.OnKeyUp(9)
            e.OnKeyUp(9)
            e.OnKeyUp(99)
            Thread.Sleep(50)
            Assert.Empty(_fired)
        End Using
    End Sub

    <Fact>
    Public Sub ReleaseAllOnDisconnectFiresNothing()
        Using e = MakeEngine(MakeConfig())
            Dim ups As New ConcurrentQueue(Of Integer)
            AddHandler e.KeyStateChanged, Sub(k, p)
                                              If Not p Then ups.Enqueue(k)
                                          End Sub
            e.OnKeyDown(1)
            e.ReleaseAll()
            _now = 2000
            e.Tick()
            e.OnKeyUp(1)
            Thread.Sleep(50)
            Assert.Empty(_fired)
            AssertSeq({1}, ups.ToArray())
        End Using
    End Sub

    <Fact>
    Public Sub UpdateConfigIsCopyOnWriteAndKeepsPage()
        Using e = MakeEngine(MakeConfig())
            e.GoToPage("Zwei")
            Dim before = e.Config
            e.UpdateConfig(Sub(c) c.FindProfile("Standard").FindPage("Zwei").SetKey(5, New KeyConfig With {.Label = "neu"}))
            Assert.NotSame(before, e.Config)
            Assert.Null(before.Profiles(0).Pages(1).GetKey(5))
            Assert.Equal("neu", e.CurrentPage.GetKey(5).Label)
            Assert.Equal("Zwei", e.CurrentPage.Name)
        End Using
    End Sub

    <Fact>
    Public Sub UpdateConfigCanShowRenamedPage()
        Using e = MakeEngine(MakeConfig())
            e.GoToPage("Zwei")
            e.UpdateConfig(Sub(c) ConfigEdit.RenamePage(c.FindProfile("Standard"), "Zwei", "Seite B"), "Standard", "Seite B")
            Assert.Equal("Seite B", e.CurrentPage.Name)
            Assert.Equal("Seite B", e.Config.Profiles(0).Pages(0).GetKey(14).Press.Target)
        End Using
    End Sub

    <Fact>
    Public Sub LongPressThresholdFollowsConfig()
        Using e = MakeEngine(MakeConfig())
            e.UpdateConfig(Sub(c) c.LongPressMs = 1000)
            e.OnKeyDown(1)
            _now = 700
            e.Tick()
            e.OnKeyUp(1)
            Assert.True(Wait.Until(Function() _fired.Count = 1))
            AssertSeq({"B"}, _fired.ToArray())
        End Using
    End Sub
End Class

' ====================================================================== Bearbeiten

Public Class ConfigEditTests
    <Fact>
    Public Sub RenamePageUpdatesAllReferencesIncludingNested()
        Dim cfg = ConfigStore.CreateDefault()
        Dim p = cfg.Profiles(0)
        p.Pages(0).SetKey(3, New KeyConfig With {.LongPress = New ActionConfig With {.ActionType = "multi", .Actions = New List(Of ActionConfig) From {
            New ActionConfig With {.ActionType = "page", .Target = "seite 2"}}}})
        ConfigEdit.RenamePage(p, "Seite 2", "Werkzeuge")
        Assert.Equal("Werkzeuge", p.Pages(1).Name)
        Assert.Equal("Werkzeuge", p.Pages(0).GetKey(14).Press.Target)
        Assert.Equal("Werkzeuge", p.Pages(0).GetKey(3).LongPress.Actions(0).Target)
        Assert.Equal("@back", p.Pages(1).GetKey(14).Press.Target)   ' Sonderziele bleiben
    End Sub

    <Fact>
    Public Sub RenameRejectsDuplicatesEmptyAndReserved()
        Dim p = ConfigStore.CreateDefault().Profiles(0)
        Assert.Throws(Of ArgumentException)(Sub() ConfigEdit.RenamePage(p, "Start", "seite 2"))
        Assert.Throws(Of ArgumentException)(Sub() ConfigEdit.RenamePage(p, "Start", "  "))
        Assert.Throws(Of ArgumentException)(Sub() ConfigEdit.RenamePage(p, "Start", "@back"))
        ConfigEdit.RenamePage(p, "Start", "START")   ' nur Schreibweise ändern ist erlaubt
        Assert.Equal("START", p.Pages(0).Name)
    End Sub

    <Fact>
    Public Sub RenameProfileUpdatesActiveAndProfileActions()
        Dim cfg = ConfigStore.CreateDefault()
        ConfigEdit.AddProfile(cfg, "Spiele")
        cfg.Profiles(1).Pages(0).SetKey(0, New KeyConfig With {.Press = New ActionConfig With {.ActionType = "profile", .Target = "Standard"}})
        ConfigEdit.RenameProfile(cfg, "Standard", "Büro")
        Assert.Equal("Büro", cfg.ActiveProfile)
        Assert.Equal("Büro", cfg.Profiles(1).Pages(0).GetKey(0).Press.Target)
    End Sub

    <Fact>
    Public Sub AddAndDeleteKeepAtLeastOne()
        Dim cfg = ConfigStore.CreateDefault()
        Dim prof = ConfigEdit.AddProfile(cfg)
        Assert.Equal("Profil", prof.Name)
        Assert.Equal("Profil 2", ConfigEdit.AddProfile(cfg).Name)
        Assert.Single(prof.Pages)
        Assert.False(ConfigEdit.DeletePage(prof, "Start"))           ' letzte Seite bleibt
        Assert.Equal("Seite", ConfigEdit.AddPage(prof).Name)
        Assert.True(ConfigEdit.DeletePage(prof, "start"))
        Assert.True(ConfigEdit.DeleteProfile(cfg, "Standard"))
        Assert.Equal("Profil", cfg.ActiveProfile)
        Assert.True(ConfigEdit.DeleteProfile(cfg, "Profil"))
        Assert.False(ConfigEdit.DeleteProfile(cfg, "Profil 2"))      ' letztes Profil bleibt
    End Sub

    <Fact>
    Public Sub UniqueNames()
        Assert.Equal("Seite", ConfigEdit.UniqueName({"Start"}, "Seite"))
        Assert.Equal("Seite 3", ConfigEdit.UniqueName({"seite", "Seite 2"}, "Seite"))
    End Sub
End Class

' ====================================================================== Prozesse

Public Class ProcessActionsTests
    <Fact>
    Public Sub LaunchExpandsVariablesAndSetsWorkingDirectory()
        Dim psi = ProcessActions.BuildLaunch(New ActionConfig With {.Path = """%WINDIR%\notepad.exe""", .Args = "%TEMP%\x.txt"})
        Dim expected = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe")
        Assert.Equal(expected, psi.FileName, ignoreCase:=True)
        Assert.Equal(IO.Path.GetDirectoryName(expected), psi.WorkingDirectory, ignoreCase:=True)
        Assert.DoesNotContain("%", psi.Arguments)
        Assert.True(psi.UseShellExecute)
        Assert.Equal("calc.exe", ProcessActions.BuildLaunch(New ActionConfig With {.Path = "calc.exe"}).FileName)
        Assert.Throws(Of ArgumentException)(Function() ProcessActions.BuildLaunch(New ActionConfig With {.Path = " "}))
    End Sub

    <Theory>
    <InlineData("example.org", "https://example.org/")>
    <InlineData("http://example.org/a?b=1", "http://example.org/a?b=1")>
    <InlineData(" https://example.org/x ", "https://example.org/x")>
    Public Sub NormalizesUrls(input As String, expected As String)
        Assert.Equal(expected, ProcessActions.NormalizeUrl(input))
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("file:///C:/Windows/system32/cmd.exe")>
    <InlineData("javascript:alert(1)")>
    <InlineData("ftp://example.org")>
    Public Sub RejectsNonWebUrls(input As String)
        Assert.Throws(Of ArgumentException)(Function() ProcessActions.NormalizeUrl(input))
    End Sub

    <Fact>
    Public Sub CmdHiddenAndVisible()
        Dim hidden = ProcessActions.BuildCommand(New ActionConfig With {.Command = "echo hi", .Hidden = True})
        Assert.Equal("cmd.exe", hidden.FileName)
        Assert.Equal("/c echo hi", hidden.Arguments)
        Assert.True(hidden.CreateNoWindow)
        Assert.False(hidden.UseShellExecute)
        Assert.True(hidden.RedirectStandardError)

        Dim visible = ProcessActions.BuildCommand(New ActionConfig With {.Command = "dir", .Shell = "cmd"})
        Assert.Equal("/k dir", visible.Arguments)
        Assert.True(visible.UseShellExecute)
    End Sub

    <Fact>
    Public Sub PowerShellCommandIsEncodedExactly()
        Dim cmd = "Write-Host ""Grüße"" 'x' $env:USERNAME; exit 3"
        Dim psi = ProcessActions.BuildCommand(New ActionConfig With {.Command = cmd, .Shell = "powershell", .Hidden = True})
        Assert.Equal("powershell.exe", psi.FileName)
        Dim args = psi.ArgumentList.ToList()
        Assert.DoesNotContain("-NoExit", args)
        Dim encoded = args(args.IndexOf("-EncodedCommand") + 1)
        Assert.Equal(cmd, Text.Encoding.Unicode.GetString(Convert.FromBase64String(encoded)))
    End Sub

    <Fact>
    Public Sub PowerShellScriptFileUsesFileParameter()
        Dim psi = ProcessActions.BuildCommand(New ActionConfig With {.Command = """C:\Skripte\mein skript.ps1""", .Shell = "pwsh"})
        Assert.Equal("pwsh.exe", psi.FileName)
        Dim args = psi.ArgumentList.ToList()
        Assert.Contains("-NoExit", args)
        Assert.Equal("C:\Skripte\mein skript.ps1", args(args.IndexOf("-File") + 1))
    End Sub

    <Fact>
    Public Sub AppRelativePathsResolveAgainstProgramFolder()
        Assert.Equal("C:\Programme\Deck\Sounds\tusch.mp3", ProcessActions.ResolveAppRelativePath("Sounds\tusch.mp3", "C:\Programme\Deck\"))
        Assert.Equal("D:\eigene\a.wav", ProcessActions.ResolveAppRelativePath("""D:\eigene\a.wav""", "C:\Programme\Deck"))
        Dim windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        Assert.Equal(IO.Path.Combine(windir, "Media\x.wav"), ProcessActions.ResolveAppRelativePath("%WINDIR%\Media\x.wav", "C:\egal"), ignoreCase:=True)
    End Sub

    <Fact>
    Public Sub HiddenCommandReportsNonZeroExitCode()
        Dim r As New ActionRunner()
        Dim logs As New ConcurrentQueue(Of String)
        ProcessActions.Register(r, Sub(m) logs.Enqueue(m))
        Assert.True(r.Fire(New ActionConfig With {.ActionType = "command", .Command = "echo Fehlertext 1>&2 & exit 7", .Hidden = True}).Wait(10000))
        Assert.Contains(logs, Function(l) l.Contains("Code 7") AndAlso l.Contains("Fehlertext"))
    End Sub
End Class

Public Class SoundDurationConfigTests
    <Fact>
    Public Sub DurationIsReadWrittenAndDescribed()
        Dim cfg = ConfigStore.Deserialize("{ ""profiles"": [ { ""name"": ""A"", ""pages"": [ { ""name"": ""P"", ""keys"": {" &
            " ""0"": { ""press"": { ""type"": ""sound"", ""file"": ""Sounds\\tusch.mp3"", ""duration"": 2.5 } }," &
            " ""1"": { ""press"": { ""type"": ""sound"", ""file"": ""Sounds\\gong.mp3"" } } } } ] } ] }")
        Dim page = cfg.Profiles(0).Pages(0)
        Assert.Equal(2.5, page.GetKey(0).Press.Duration)
        Assert.Null(page.GetKey(1).Press.Duration)
        Dim json = ConfigStore.Serialize(cfg)
        Assert.Contains("""duration"": 2.5", json)
        Assert.Equal(1, json.Split("duration").Length - 1)          ' ohne Dauer wird nichts geschrieben
        Assert.StartsWith("Sound tusch.mp3 (2", ActionTypes.Describe(page.GetKey(0).Press))
        Assert.Equal("Sound gong.mp3", ActionTypes.Describe(page.GetKey(1).Press))
    End Sub
End Class

Friend Module SeqAssert
    ''' <summary>Vergleicht Folgen (Assert.Equal mit Array-Literalen wählt in VB eine Span-Überladung, die VB nicht kann).</summary>
    Public Sub AssertSeq(Of T)(expected As IEnumerable(Of T), actual As IEnumerable(Of T))
        Assert.True(expected.SequenceEqual(actual), $"Erwartet [{String.Join(", ", expected)}], erhalten [{String.Join(", ", actual)}]")
    End Sub
End Module
