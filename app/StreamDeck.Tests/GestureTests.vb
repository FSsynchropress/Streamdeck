' Tests für Abschnitt 5 über die Engine: Doppeldruck, Aktion beim Loslassen, Wiederholung, Taste lernen.
Imports System.Collections.Concurrent
Imports System.Threading
Imports StreamDeck.Core
Imports Xunit

Public Class GestureEngineTests
    Private _now As Long
    Private ReadOnly _fired As New ConcurrentQueue(Of String)

    Private Function MakeEngine(key As KeyConfig) As DeckEngine
        Dim cfg As New DeckConfig With {.LongPressMs = 500, .DoublePressMs = 300}
        Dim page As New PageConfig With {.Name = "Start"}
        page.SetKey(0, key)
        page.SetKey(1, New KeyConfig With {.Press = Hk("Z")})
        cfg.Profiles.Add(New ProfileConfig With {.Name = "Standard", .Pages = New List(Of PageConfig) From {page}})
        Dim r As New ActionRunner()
        r.Register("hotkey", DelegateActionHandler.FromSync(Sub(a) _fired.Enqueue(a.Keys)))
        Return New DeckEngine(cfg, r, Function() Interlocked.Read(_now), tickIntervalMs:=0)
    End Function

    Private Shared Function Hk(keys As String) As ActionConfig
        Return New ActionConfig With {.ActionType = "hotkey", .Keys = keys}
    End Function

    Private Sub At(ms As Long)
        Interlocked.Exchange(_now, ms)
    End Sub

    Private Function Fired(Optional expected As Integer = -1) As String()
        Dim sw = Diagnostics.Stopwatch.StartNew()
        While expected >= 0 AndAlso _fired.Count < expected AndAlso sw.ElapsedMilliseconds < 2000
            Thread.Sleep(5)
        End While
        Thread.Sleep(30)   ' ggf. überzählige Aktionen abwarten
        Return _fired.ToArray()
    End Function

    <Fact>
    Public Sub DoublePressFiresDoubleNotShort()
        Using e = MakeEngine(New KeyConfig With {.Press = Hk("S"), .DoublePress = Hk("D")})
            At(0) : e.OnKeyDown(0)
            At(80) : e.OnKeyUp(0)
            At(200) : e.Tick()
            At(250) : e.OnKeyDown(0)
            At(300) : e.OnKeyUp(0)
            At(2000) : e.Tick()
            AssertSeq({"D"}, Fired(1))
        End Using
    End Sub

    <Fact>
    Public Sub SinglePressWithDoubleConfiguredFiresShortAfterWindow()
        Using e = MakeEngine(New KeyConfig With {.Press = Hk("S"), .DoublePress = Hk("D")})
            At(0) : e.OnKeyDown(0)
            At(80) : e.OnKeyUp(0)
            At(300) : e.Tick()
            Assert.Empty(Fired())
            At(400) : e.Tick()
            AssertSeq({"S"}, Fired(1))
        End Using
    End Sub

    <Fact>
    Public Sub PushToTalkPressAndRelease()
        Using e = MakeEngine(New KeyConfig With {.Press = Hk("AN"), .Release = Hk("AUS")})
            At(0) : e.OnKeyDown(0)
            AssertSeq({"AN"}, Fired(1))
            At(3000) : e.OnKeyUp(0)
            AssertSeq({"AN", "AUS"}, Fired(2))
        End Using
    End Sub

    <Fact>
    Public Sub DisconnectWhileHeldRunsReleaseOnly()
        Using e = MakeEngine(New KeyConfig With {.Press = Hk("AN"), .Release = Hk("AUS"), .LongPress = Hk("LANG")})
            At(0) : e.OnKeyDown(0)
            e.ReleaseAll()
            AssertSeq({"AUS"}, Fired(1))
            Assert.False(e.IsHeld(0))
        End Using
    End Sub

    <Fact>
    Public Sub RepeatWhileHeld()
        Using e = MakeEngine(New KeyConfig With {.Press = Hk("V+"), .Repeat = True})
            At(0) : e.OnKeyDown(0)
            At(399) : e.Tick()
            At(400) : e.Tick()
            At(520) : e.Tick()
            At(600) : e.OnKeyUp(0)
            At(2000) : e.Tick()
            AssertSeq({"V+", "V+", "V+"}, Fired(3))
        End Using
    End Sub

    <Fact>
    Public Sub LearnModeReportsKeyWithoutAction()
        Using e = MakeEngine(New KeyConfig With {.Press = Hk("S")})
            Dim learned As New ConcurrentQueue(Of Integer)
            AddHandler e.KeyLearned, Sub(k) learned.Enqueue(k)
            e.LearnMode = True
            e.OnKeyDown(1)
            e.OnKeyUp(1)
            AssertSeq({1}, learned.ToArray())
            Assert.Empty(Fired())
            e.LearnMode = False
            e.OnKeyDown(1)
            AssertSeq({"Z"}, Fired(1))
        End Using
    End Sub

    <Fact>
    Public Sub RenamingPageUpdatesDoubleAndReleaseTargets()
        Dim cfg As New DeckConfig
        Dim p As New ProfileConfig With {.Name = "P"}
        Dim start As New PageConfig With {.Name = "Start"}
        start.SetKey(0, New KeyConfig With {
            .DoublePress = New ActionConfig With {.ActionType = "page", .Target = "Alt"},
            .Release = New ActionConfig With {.ActionType = "page", .Target = "Alt"}})
        p.Pages.Add(start)
        p.Pages.Add(New PageConfig With {.Name = "Alt"})
        cfg.Profiles.Add(p)
        ConfigEdit.RenamePage(p, "Alt", "Neu")
        Assert.Equal("Neu", start.GetKey(0).DoublePress.Target)
        Assert.Equal("Neu", start.GetKey(0).Release.Target)
    End Sub
End Class
