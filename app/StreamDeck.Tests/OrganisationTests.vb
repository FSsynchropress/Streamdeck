' Tests für Abschnitt 3 (parallel, Zufall) und 4 (Ordner, Blättern, Profile reihum, Navigations-Meldung).
Imports System.Collections.Concurrent
Imports StreamDeck.Core
Imports Xunit

Public Class CombineTests
    <Fact>
    Public Async Function ParallelMultiRunsAtTheSameTime() As Task
        ' Jede Aktion wartet, bis alle drei gestartet sind. Liefen sie nacheinander,
        ' käme die erste nie über das Tor hinaus (Timeout statt Hängen). Keine Messung
        ' von Laufzeiten, damit der Test auf langsamen CI-Rechnern stabil bleibt.
        Dim r As New ActionRunner()
        Dim started = 0
        Dim timedOut = 0
        Dim allStarted As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        r.Register("probe", New DelegateActionHandler(
            Async Function(a, ctx)
                If Threading.Interlocked.Increment(started) = 3 Then allStarted.TrySetResult(True)
                Dim first = Await Task.WhenAny(allStarted.Task, Task.Delay(5000))
                If first IsNot allStarted.Task Then Threading.Interlocked.Increment(timedOut)
            End Function))
        Dim multi As New ActionConfig With {.ActionType = "multi", .Parallel = True, .Actions = New List(Of ActionConfig) From {
            New ActionConfig With {.ActionType = "probe"},
            New ActionConfig With {.ActionType = "probe"},
            New ActionConfig With {.ActionType = "probe"}}}
        Await r.Fire(multi)
        Assert.Equal(3, started)
        Assert.Equal(0, timedOut)   ' nacheinander würden die ersten beiden ins Timeout laufen
    End Function

    <Fact>
    Public Async Function RandomPicksFromListWithoutDirectRepeat() As Task
        Dim r As New ActionRunner()
        Dim sent As New ConcurrentQueue(Of String)
        r.Register("hotkey", DelegateActionHandler.FromSync(Sub(a) sent.Enqueue(a.Keys)))
        Dim rnd As New ActionConfig With {.ActionType = "random", .Actions = New List(Of ActionConfig) From {
            New ActionConfig With {.ActionType = "hotkey", .Keys = "A"},
            New ActionConfig With {.ActionType = "hotkey", .Keys = "B"},
            New ActionConfig With {.ActionType = "hotkey", .Keys = "C"}}}
        For i = 1 To 60
            Await r.Fire(rnd, 3)
        Next
        Dim list = sent.ToList()
        Assert.Equal(60, list.Count)
        Assert.All(list, Sub(k) Assert.Contains(k, {"A", "B", "C"}))
        For i = 1 To list.Count - 1
            Assert.NotEqual(list(i - 1), list(i))
        Next
        Assert.Equal(3, list.Distinct().Count())
    End Function
End Class

Public Class FolderAndPagingTests
    Private Shared Function MakeConfig() As DeckConfig
        Dim cfg As New DeckConfig With {.ActiveProfile = "A"}
        Dim a As New ProfileConfig With {.Name = "A"}
        a.Pages.Add(New PageConfig With {.Name = "Start"})
        a.Pages.Add(New PageConfig With {.Name = "Zwei"})
        a.Pages.Add(New PageConfig With {.Name = "Ordner1", .Folder = True})
        a.Pages.Add(New PageConfig With {.Name = "Drei"})
        a.Pages.Add(New PageConfig With {.Name = "Unterordner", .Folder = True})
        cfg.Profiles.Add(a)
        cfg.Profiles.Add(New ProfileConfig With {.Name = "B"})
        cfg.Profiles.Add(New ProfileConfig With {.Name = "C"})
        Return cfg
    End Function

    <Fact>
    Public Sub FoldersGetFixedBackKeyAndStartPageIsNoFolder()
        Dim cfg = MakeConfig()
        cfg.Profiles(0).Pages(0).Folder = True
        cfg.Profiles(0).FindPage("Ordner1").SetKey(0, New KeyConfig With {.Label = "überschrieben"})
        ConfigStore.Normalize(cfg)
        Assert.False(cfg.Profiles(0).Pages(0).IsFolder)
        Dim back = cfg.Profiles(0).FindPage("Ordner1").GetKey(0)
        Assert.Equal(ActionTypes.PageBack, back.Press.Target)
        Assert.Null(cfg.Profiles(0).FindPage("Zwei").GetKey(0))
    End Sub

    <Fact>
    Public Sub NestedFoldersAndBack()
        Dim s As New DeckState(MakeConfig())
        Assert.True(s.GoToPage("Zwei"))
        Assert.True(s.GoToPage("Ordner1"))
        Assert.True(s.GoToPage("Unterordner"))
        ' feste Zurück-Taste führt Ebene für Ebene zurück
        Assert.True(s.GoToPage(s.GetKey(0).Press.Target))
        Assert.Equal("Ordner1", s.CurrentPage.Name)
        Assert.True(s.GoToPage(ActionTypes.PageBack))
        Assert.Equal("Zwei", s.CurrentPage.Name)
    End Sub

    <Fact>
    Public Sub PagingSkipsFoldersAndWraps()
        Dim s As New DeckState(MakeConfig())
        Assert.True(s.GoToPage(ActionTypes.TargetNext))
        Assert.Equal("Zwei", s.CurrentPage.Name)
        Assert.True(s.GoToPage(ActionTypes.TargetNext))
        Assert.Equal("Drei", s.CurrentPage.Name)     ' Ordner1 übersprungen
        Assert.True(s.GoToPage(ActionTypes.TargetNext))
        Assert.Equal("Start", s.CurrentPage.Name)    ' ringsum
        Assert.True(s.GoToPage(ActionTypes.TargetPrev))
        Assert.Equal("Drei", s.CurrentPage.Name)
    End Sub

    <Fact>
    Public Sub PagingFromFolderUsesPageItWasOpenedFrom()
        Dim s As New DeckState(MakeConfig())
        s.GoToPage("Zwei")
        s.GoToPage("Ordner1")
        Assert.True(s.GoToPage(ActionTypes.TargetNext))
        Assert.Equal("Drei", s.CurrentPage.Name)
    End Sub

    <Fact>
    Public Sub ProfilesCycle()
        Dim s As New DeckState(MakeConfig())
        Assert.True(s.SelectProfile(ActionTypes.TargetNext))
        Assert.Equal("B", s.CurrentProfile.Name)
        Assert.True(s.SelectProfile(ActionTypes.TargetPrev))
        Assert.True(s.SelectProfile(ActionTypes.TargetPrev))
        Assert.Equal("C", s.CurrentProfile.Name)
        Assert.Equal("C", s.Config.ActiveProfile)
    End Sub

    <Fact>
    Public Sub SetFolderMovesOccupyingKey()
        Dim cfg = MakeConfig()
        ConfigStore.Normalize(cfg)
        Dim p = cfg.Profiles(0)
        p.FindPage("Drei").SetKey(0, New KeyConfig With {.Label = "Alt"})
        Assert.True(ConfigEdit.SetFolder(cfg, p, "Drei", True))
        Assert.Equal("Alt", p.FindPage("Drei").GetKey(1).Label)
        Assert.Equal("Zurück", p.FindPage("Drei").GetKey(0).Label)
        Assert.False(ConfigEdit.SetFolder(cfg, p, "Start", True))   ' Startseite nicht
    End Sub

    <Fact>
    Public Sub MovingFolderBackKeyMovesBackKeys()
        Dim cfg = MakeConfig()
        ConfigStore.Normalize(cfg)
        Dim folder = cfg.Profiles(0).FindPage("Ordner1")
        folder.SetKey(14, New KeyConfig With {.Label = "X"})
        ConfigEdit.SetFolderBackKey(cfg, 14)
        ConfigStore.Normalize(cfg)
        Assert.Equal(14, cfg.FolderBackKey)
        Assert.Equal("Zurück", folder.GetKey(14).Label)
        Assert.Equal("X", folder.GetKey(0).Label)    ' alte Zurück-Taste entfernt, X auf die frei gewordene Taste
        Assert.Equal(1, Enumerable.Range(0, 15).Count(Function(i) folder.GetKey(i)?.Label = "Zurück"))
    End Sub
End Class

Public Class NavigatedEventTests
    <Fact>
    Public Async Function DeviceNavigationRaisesNavigatedButUiDoesNot() As Task
        Dim cfg As New DeckConfig With {.ActiveProfile = "A"}
        Dim a As New ProfileConfig With {.Name = "A"}
        Dim start As New PageConfig With {.Name = "Start"}
        start.SetKey(0, New KeyConfig With {.Press = New ActionConfig With {.ActionType = "page", .Target = "Box"}})
        a.Pages.Add(start)
        a.Pages.Add(New PageConfig With {.Name = "Box", .Folder = True})
        cfg.Profiles.Add(a)
        cfg.Profiles.Add(New ProfileConfig With {.Name = "B"})
        Dim events As New ConcurrentQueue(Of String)
        Using e As New DeckEngine(cfg, New ActionRunner(), Function() 0, tickIntervalMs:=0)
            AddHandler e.Navigated, Sub(t, s) events.Enqueue(t)
            e.GoToPage("Box")                    ' Oberfläche: keine Meldung
            e.GoToPage(ActionTypes.PageBack)
            Assert.Empty(events)
            e.OnKeyDown(0)
            e.OnKeyUp(0)
            Await WaitFor(Function() events.Count >= 1)
            Assert.Equal("Ordner: Box", events.First())
            Assert.Contains("Ordner Box", e.Summary())
            Assert.False(e.ApplyForegroundProcess("irgendwas"))
            Assert.True(e.SelectProfile("B", notify:=True))
            Assert.Contains("Profil: B", events)
        End Using
    End Function

    Private Shared Async Function WaitFor(cond As Func(Of Boolean)) As Task
        Dim sw = Diagnostics.Stopwatch.StartNew()
        While Not cond() AndAlso sw.ElapsedMilliseconds < 2000
            Await Task.Delay(5)
        End While
        Assert.True(cond())
    End Function
End Class
