' Tests für die Einzelaktionen aus Abschnitt 2: Umschalter, Timer, HTTP, Programm schließen,
' Browserwahl, Modifier einzeln, Datei-Protokoll.
Imports System.Collections.Concurrent
Imports System.Net
Imports StreamDeck.Core
Imports Xunit

Public Class ToggleTests
    <Fact>
    Public Async Function ToggleAlternatesPerKey() As Task
        Dim r As New ActionRunner()
        Dim sent As New ConcurrentQueue(Of String)
        r.Register("hotkey", DelegateActionHandler.FromSync(Sub(a) sent.Enqueue(a.Keys)))
        Dim toggle As New ActionConfig With {.ActionType = "toggle", .Actions = New List(Of ActionConfig) From {
            New ActionConfig With {.ActionType = "hotkey", .Keys = "A"},
            New ActionConfig With {.ActionType = "hotkey", .Keys = "B"}}}
        Await r.Fire(toggle, 1)
        Await r.Fire(toggle, 1)
        Await r.Fire(toggle, 2)      ' andere Taste hat eigenen Zustand
        Await r.Fire(toggle, 1)
        AssertSeq({"A", "B", "A", "A"}, sent.ToArray())
        Assert.Equal(1, r.NextToggleIndex(toggle, 1))
    End Function

    <Fact>
    Public Async Function ToggleStateSurvivesConfigClone() As Task
        Dim r As New ActionRunner()
        Dim sent As New ConcurrentQueue(Of String)
        r.Register("hotkey", DelegateActionHandler.FromSync(Sub(a) sent.Enqueue(a.Keys)))
        Dim toggle As New ActionConfig With {.ActionType = "toggle", .Actions = New List(Of ActionConfig) From {
            New ActionConfig With {.ActionType = "hotkey", .Keys = "A"},
            New ActionConfig With {.ActionType = "hotkey", .Keys = "B"},
            New ActionConfig With {.ActionType = "hotkey", .Keys = "C"}}}
        Await r.Fire(toggle, 0)
        Await r.Fire(ConfigStore.CloneAction(toggle), 0)
        Await r.Fire(ConfigStore.CloneAction(toggle), 0)
        Await r.Fire(toggle, 0)
        AssertSeq({"A", "B", "C", "A"}, sent.ToArray())
    End Function
End Class

Public Class TimerTests
    <Fact>
    Public Async Function TimerPlaysSoundWhenDone() As Task
        Dim r As New ActionRunner()
        Dim played As New ConcurrentQueue(Of String)
        Dim notes As New ConcurrentQueue(Of String)
        r.Register("sound", DelegateActionHandler.FromSync(Sub(a) played.Enqueue(a.File)))
        AddHandler r.Notify, Sub(t, m) notes.Enqueue(t)
        Await r.Fire(New ActionConfig With {.ActionType = "timer", .Seconds = 0.05}, 4)
        AssertSeq({ActionRunner.DefaultTimerSound}, played.ToArray())
        Assert.Contains("Timer abgelaufen", notes)
        Assert.Equal(0, r.RunningTimerCount)
    End Function

    <Fact>
    Public Async Function SecondPressCancelsTimer() As Task
        Dim r As New ActionRunner()
        Dim played As New ConcurrentQueue(Of String)
        r.Register("sound", DelegateActionHandler.FromSync(Sub(a) played.Enqueue(a.File)))
        Dim timer As New ActionConfig With {.ActionType = "timer", .Seconds = 5, .File = "x.mp3"}
        Dim first = r.Fire(timer, 4)
        Await WaitUntil(Function() r.RunningTimerCount = 1)
        Await r.Fire(ConfigStore.CloneAction(timer), 4)
        Assert.True(first.Wait(2000), "Timer wurde nicht abgebrochen")
        Assert.Empty(played)
        Assert.Equal(0, r.RunningTimerCount)
    End Function

    <Fact>
    Public Async Function TimerWithoutDurationIsLogged() As Task
        Dim r As New ActionRunner()
        Dim logs As New ConcurrentQueue(Of String)
        AddHandler r.Log, Sub(m) logs.Enqueue(m)
        Await r.Fire(New ActionConfig With {.ActionType = "timer"})
        Assert.Contains(logs, Function(l) l.Contains("Dauer"))
    End Function

    Private Shared Async Function WaitUntil(condition As Func(Of Boolean)) As Task
        Dim sw = Diagnostics.Stopwatch.StartNew()
        While Not condition() AndAlso sw.ElapsedMilliseconds < 2000
            Await Task.Delay(5)
        End While
        Assert.True(condition())
    End Function

    <Fact>
    Public Sub FormatSeconds()
        Assert.Equal("20 s", ActionTypes.FormatSeconds(20))
        Assert.Equal("1:30", ActionTypes.FormatSeconds(90))
        Assert.Equal("1:02:05", ActionTypes.FormatSeconds(3725))
    End Sub
End Class

Public Class HttpActionTests
    <Fact>
    Public Async Function PostRequestHasBodyContentTypeAndHeaders() As Task
        Dim a As New ActionConfig With {.ActionType = "http", .Method = "post", .Url = "example.org/hook", .Body = "{""x"":""ä""}",
                                        .Headers = New Dictionary(Of String, String) From {{"X-Token", "geheim"}}}
        Using req = HttpActions.BuildRequest(a)
            Assert.Equal("POST", req.Method.Method)
            Assert.Equal("https://example.org/hook", req.RequestUri.AbsoluteUri)
            Assert.Equal("application/json", req.Content.Headers.ContentType.MediaType)
            Assert.Equal("utf-8", req.Content.Headers.ContentType.CharSet)
            Assert.Equal("{""x"":""ä""}", Await req.Content.ReadAsStringAsync())
            Assert.Equal("geheim", req.Headers.GetValues("X-Token").Single())
        End Using
    End Function

    <Fact>
    Public Sub GetRequestHasNoBody()
        Using req = HttpActions.BuildRequest(New ActionConfig With {.Url = "http://localhost/x", .Body = "ignoriert"})
            Assert.Equal("GET", req.Method.Method)
            Assert.Null(req.Content)
        End Using
    End Sub

    <Fact>
    Public Sub InvalidInputIsRejected()
        Assert.Throws(Of ArgumentException)(Function() HttpActions.BuildRequest(New ActionConfig With {.Url = "http://x", .Method = "FOO"}))
        Assert.Throws(Of ArgumentException)(Function() HttpActions.BuildRequest(New ActionConfig With {.Url = "ftp://x"}))
    End Sub

    <Fact>
    Public Sub HeadersParseAndFormat()
        Dim h = HttpActions.ParseHeaders("Authorization: Bearer abc" & vbCrLf & vbCrLf & "  X-A :  1:2 ")
        Assert.Equal("Bearer abc", h("authorization"))
        Assert.Equal("1:2", h("X-A"))
        Assert.Throws(Of FormatException)(Function() HttpActions.ParseHeaders("ohne Doppelpunkt"))
        Assert.Equal("", HttpActions.FormatHeaders(Nothing))
    End Sub

    ''' <summary>Echte Anfrage an einen lokalen HttpListener: Body kommt an, 500 wird protokolliert.</summary>
    <Fact>
    Public Async Function SendsRequestAndLogsServerErrors() As Task
        Dim port = FreePort()
        Using listener As New HttpListener()
            listener.Prefixes.Add($"http://localhost:{port}/")
            listener.Start()
            Dim received As New ConcurrentQueue(Of String)
            Dim serve = Task.Run(Sub()
                                     For i = 1 To 2
                                         Dim ctx = listener.GetContext()
                                         Using sr As New IO.StreamReader(ctx.Request.InputStream)
                                             received.Enqueue($"{ctx.Request.HttpMethod} {ctx.Request.Url.AbsolutePath} {sr.ReadToEnd()}")
                                         End Using
                                         ctx.Response.StatusCode = If(ctx.Request.Url.AbsolutePath = "/fehler", 500, 200)
                                         ctx.Response.Close()
                                     Next
                                 End Sub)
            Dim r As New ActionRunner()
            HttpActions.Register(r)
            Dim logs As New ConcurrentQueue(Of String)
            AddHandler r.Log, Sub(m) logs.Enqueue(m)
            Await r.Fire(New ActionConfig With {.ActionType = "http", .Method = "POST", .Url = $"http://localhost:{port}/ok", .Body = "hallo"}, 5)
            Await r.Fire(New ActionConfig With {.ActionType = "http", .Url = $"http://localhost:{port}/fehler"}, 6)
            Assert.True(serve.Wait(5000))
            AssertSeq({"POST /ok hallo", "GET /fehler "}, received.ToArray())
            Assert.DoesNotContain(logs, Function(l) l.Contains("Taste 5"))
            Assert.Contains(logs, Function(l) l.Contains("Taste 6") AndAlso l.Contains("500"))
        End Using
    End Function

    Private Shared Function FreePort() As Integer
        Dim l As New Sockets.TcpListener(IPAddress.Loopback, 0)
        l.Start()
        Dim p = CType(l.LocalEndpoint, IPEndPoint).Port
        l.Stop()
        Return p
    End Function
End Class

Public Class ProcessActionTests
    <Theory>
    <InlineData("C:\Programme\OBS\obs64.exe", "obs64")>
    <InlineData("notepad.exe", "notepad")>
    <InlineData("""C:\a b\Tool.EXE""", "Tool")>
    <InlineData("chrome", "chrome")>
    Public Sub ProcessNameFromPath(path As String, expected As String)
        Assert.Equal(expected, ProcessActions.ProcessNameFromPath(path))
    End Sub

    <Fact>
    Public Sub CloseNotRunningProgramIsLogged()
        Dim logs As New List(Of String)
        Dim n = ProcessActions.CloseProcesses(New ActionConfig With {.Path = "gibt-es-sicher-nicht-123.exe"}, AddressOf logs.Add)
        Assert.Equal(0, n)
        Assert.Contains(logs, Function(l) l.Contains("läuft nicht"))
    End Sub

    <Fact>
    Public Sub UrlWithBrowser()
        Dim def = ProcessActions.BuildUrl(New ActionConfig With {.Url = "example.org"})
        Assert.Equal("https://example.org/", def.FileName)
        Dim ff = ProcessActions.BuildUrl(New ActionConfig With {.Url = "example.org", .Browser = "firefox"})
        Assert.Equal("firefox.exe", ff.FileName)
        Assert.Equal("""https://example.org/""", ff.Arguments)
        Dim full = ProcessActions.BuildUrl(New ActionConfig With {.Url = "https://x.de", .Browser = "C:\Browser\b.exe"})
        Assert.Equal("C:\Browser\b.exe", full.FileName)
    End Sub
End Class

Public Class ModifierTests
    <Fact>
    Public Sub SingleAndSidedModifiers()
        AssertSeq({CUShort(&HA3)}, Hotkeys.ParseHotkey("RCtrl"))
        AssertSeq({CUShort(&H11)}, Hotkeys.ParseHotkey("Ctrl"))
        AssertSeq({CUShort(&HA0), CUShort(&HA5), CUShort(&H41)}, Hotkeys.ParseHotkey("A+LShift+RAlt"))
        Assert.True(Hotkeys.IsExtendedKey(&HA3))
        Assert.False(Hotkeys.IsExtendedKey(&HA2))
    End Sub
End Class

Public Class NewConfigFieldTests
    <Fact>
    Public Sub NewFieldsRoundTrip()
        Dim a As New ActionConfig With {.ActionType = "text", .Text = "Hallo 😀", .Enter = True}
        Dim json = Text.Json.JsonSerializer.Serialize(a, ConfigStore.JsonOptions)
        Assert.Contains("""enter"": true", json)
        Dim back = ConfigStore.CloneAction(New ActionConfig With {.ActionType = "http", .Method = "POST",
                                           .Headers = New Dictionary(Of String, String) From {{"A", "1"}}, .Seconds = 3, .Force = True, .Restart = True, .Browser = "x"})
        Assert.Equal("1", back.Headers("A"))
        Assert.Equal(3.0, back.Seconds)
        Assert.True(back.Force)
        Assert.True(back.Restart)
        Assert.Equal("x", back.Browser)
    End Sub

    <Fact>
    Public Sub DescribeNewTypes()
        Assert.Contains("POST", ActionTypes.Describe(New ActionConfig With {.ActionType = "http", .Method = "post", .Url = "u"}))
        Assert.Contains("erzwungen", ActionTypes.Describe(New ActionConfig With {.ActionType = "close", .Path = "x", .Force = True}))
        Assert.Equal("Mikrofon stumm/an", ActionTypes.Describe(New ActionConfig With {.ActionType = "mic"}))
        Assert.Equal("Timer 5:00", ActionTypes.Describe(New ActionConfig With {.ActionType = "timer", .Seconds = 300}))
        Assert.Contains("firefox", ActionTypes.Describe(New ActionConfig With {.ActionType = "url", .Url = "u", .Browser = "firefox"}))
    End Sub
End Class

Public Class FileLogTests
    <Fact>
    Public Sub WritesDailyFileAndDeletesOldOnes()
        Dim dir = IO.Path.Combine(IO.Path.GetTempPath(), "sd-log-" & Guid.NewGuid().ToString("N"))
        Try
            Dim today As New Date(2026, 3, 20, 10, 0, 0)
            IO.Directory.CreateDirectory(dir)
            Dim old = IO.Path.Combine(dir, "streamdeck-2026-03-01.log")
            Dim recent = IO.Path.Combine(dir, "streamdeck-2026-03-15.log")
            Dim other = IO.Path.Combine(dir, "anderes.log")
            For Each f In {old, recent, other}
                IO.File.WriteAllText(f, "x")
            Next
            Dim log As New FileLog(dir, Function() today)
            log.Write("Hallo")
            log.Write("Welt")
            Assert.False(IO.File.Exists(old))
            Assert.True(IO.File.Exists(recent))
            Assert.True(IO.File.Exists(other))
            Dim lines = IO.File.ReadAllLines(log.FileFor(today))
            Assert.Equal(2, lines.Length)
            Assert.Equal("2026-03-20 10:00:00.000  Hallo", lines(0))
        Finally
            Try
                IO.Directory.Delete(dir, True)
            Catch
            End Try
        End Try
    End Sub
End Class
