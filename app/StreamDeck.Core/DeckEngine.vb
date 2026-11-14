' Herzstück der App ohne Oberfläche: verbindet Tastenereignisse (D/U), Erkennung kurz/lang,
' aktuelle Seite/Profil und die Ausführung der Aktionen.
'
' Threading: Tastenereignisse kommen vom Verbindungs-Thread, Bedienung von der Oberfläche.
' Alle Zustandsänderungen laufen unter _sync. Die Konfiguration wird nie an Ort und Stelle
' verändert, sondern kopiert, geändert und ersetzt (UpdateConfig) – Lesen ist daher immer sicher.
' Ereignisse kommen auf beliebigen Threads; die Oberfläche muss selbst auf ihren Thread wechseln.

Public Class DeckEngine
    Implements IDisposable

    Private ReadOnly _sync As New Object
    Private ReadOnly _state As DeckState
    Private ReadOnly _tracker As KeyPressTracker
    Private ReadOnly _held As New Dictionary(Of Integer, KeyConfig)
    Private ReadOnly _clock As Func(Of Long)
    Private _timer As Threading.Timer

    Public ReadOnly Property Runner As ActionRunner

    ''' <summary>Taste am Gerät gedrückt/losgelassen (für die Live-Hervorhebung).</summary>
    Public Event KeyStateChanged(keyIndex As Integer, pressed As Boolean)
    ''' <summary>Profil, Seite oder Konfiguration haben sich geändert (neu zeichnen).</summary>
    Public Event StateChanged()
    ''' <summary>Konfiguration wurde geändert und sollte gespeichert werden.</summary>
    Public Event ConfigChanged()
    Public Event Log(message As String)
    ''' <summary>
    ''' Profil/Seite/Ordner wurde am Gerät oder automatisch gewechselt (für das On-Screen-Overlay).
    ''' Nicht bei Klicks in der Oberfläche.
    ''' </summary>
    Public Event Navigated(title As String, subtitle As String)

    ''' <param name="clock">Millisekunden-Uhr (für Tests); Standard Environment.TickCount64.</param>
    ''' <param name="tickIntervalMs">Prüfintervall für langen Druck; 0 = kein Timer (Tests rufen Tick selbst).</param>
    Public Sub New(config As DeckConfig, runner As ActionRunner, Optional clock As Func(Of Long) = Nothing, Optional tickIntervalMs As Integer = 20)
        _clock = If(clock, Function() Environment.TickCount64)
        Me.Runner = runner
        _state = New DeckState(config)
        _tracker = New KeyPressTracker(AddressOf HasLongAction) With {.LongPressMs = _state.Config.LongPressMs}

        AddHandler _state.Changed, Sub() RaiseEvent StateChanged()
        AddHandler runner.Log, Sub(m) RaiseEvent Log(m)
        runner.Register(ActionTypes.Page, DelegateActionHandler.FromSync(Sub(a) GoToPage(a.Target, notify:=True)))
        runner.Register(ActionTypes.Profile, DelegateActionHandler.FromSync(
            Sub(a)
                Dim relative = a.Target IsNot Nothing AndAlso a.Target.StartsWith("@", StringComparison.Ordinal)
                If Not SelectProfile(a.Target, notify:=True) AndAlso Not relative Then RaiseEvent Log($"Profil '{a.Target}' gibt es nicht.")
            End Sub))

        If tickIntervalMs > 0 Then _timer = New Threading.Timer(Sub(s) Tick(), Nothing, tickIntervalMs, tickIntervalMs)
    End Sub

    ' ------------------------------------------------------------------ Zustand (lesen)

    Public ReadOnly Property Config As DeckConfig
        Get
            Return _state.Config
        End Get
    End Property

    Public ReadOnly Property CurrentProfile As ProfileConfig
        Get
            Return _state.CurrentProfile
        End Get
    End Property

    Public ReadOnly Property CurrentPage As PageConfig
        Get
            Return _state.CurrentPage
        End Get
    End Property

    Public ReadOnly Property IsAutoProfile As Boolean
        Get
            SyncLock _sync
                Return _state.IsAutoProfile
            End SyncLock
        End Get
    End Property

    Public Function IsHeld(keyIndex As Integer) As Boolean
        SyncLock _sync
            Return _held.ContainsKey(keyIndex)
        End SyncLock
    End Function

    ' ------------------------------------------------------------------ Gerät

    ''' <summary>Verbindet die Ereignisse einer DeckConnection mit dieser Engine.</summary>
    Public Sub Attach(connection As DeckConnection)
        AddHandler connection.KeyDown, AddressOf OnKeyDown
        AddHandler connection.KeyUp, AddressOf OnKeyUp
        AddHandler connection.Disconnected, AddressOf ReleaseAll
        AddHandler connection.Log, Sub(m) RaiseEvent Log(m)
    End Sub

    Private Function HasLongAction(keyIndex As Integer) As Boolean
        Dim k As KeyConfig = Nothing
        Return _held.TryGetValue(keyIndex, k) AndAlso k IsNot Nothing AndAlso k.LongPress IsNot Nothing AndAlso Not String.IsNullOrEmpty(k.LongPress.ActionType)
    End Function

    Public Sub OnKeyDown(keyIndex As Integer)
        Dim toFire As ActionConfig = Nothing
        SyncLock _sync
            If _held.ContainsKey(keyIndex) Then Return   ' doppeltes D ignorieren
            Dim key = _state.GetKey(keyIndex)            ' Belegung beim Drücken merken (Seite kann wechseln)
            _held(keyIndex) = key
            If _tracker.OnDown(keyIndex, _clock()) = PressKind.ShortPress Then toFire = key?.Press
        End SyncLock
        RaiseEvent KeyStateChanged(keyIndex, True)
        If toFire IsNot Nothing Then Runner.Fire(toFire, keyIndex)
    End Sub

    Public Sub OnKeyUp(keyIndex As Integer)
        Dim toFire As ActionConfig = Nothing
        SyncLock _sync
            Dim key As KeyConfig = Nothing
            If Not _held.TryGetValue(keyIndex, key) Then Return
            _held.Remove(keyIndex)
            Dim kind = _tracker.OnUp(keyIndex, _clock())
            If key IsNot Nothing AndAlso kind.HasValue Then toFire = If(kind.Value = PressKind.LongPress, key.LongPress, key.Press)
        End SyncLock
        RaiseEvent KeyStateChanged(keyIndex, False)
        If toFire IsNot Nothing Then Runner.Fire(toFire, keyIndex)
    End Sub

    ''' <summary>Löst fällige lange Tastendrücke aus. Läuft per Timer, Tests rufen es direkt auf.</summary>
    Public Sub Tick()
        Dim due As New List(Of (Integer, ActionConfig))
        SyncLock _sync
            If _tracker.PendingCount = 0 Then Return
            For Each k In _tracker.Tick(_clock())
                Dim key As KeyConfig = Nothing
                If _held.TryGetValue(k, key) AndAlso key?.LongPress IsNot Nothing Then due.Add((k, key.LongPress))
            Next
        End SyncLock
        For Each d In due
            Runner.Fire(d.Item2, d.Item1)
        Next
    End Sub

    ''' <summary>Verbindung weg: alle gehaltenen Tasten gelten als losgelassen (ohne Aktion).</summary>
    Public Sub ReleaseAll()
        Dim released As List(Of Integer)
        SyncLock _sync
            released = _held.Keys.ToList()
            _held.Clear()
            _tracker.Reset()
        End SyncLock
        For Each k In released
            RaiseEvent KeyStateChanged(k, False)
        Next
    End Sub

    ' ------------------------------------------------------------------ Bedienung

    ''' <param name="notify">True = Navigated auslösen (Wechsel am Gerät).</param>
    Public Function GoToPage(target As String, Optional notify As Boolean = False) As Boolean
        Dim ok As Boolean
        Dim title As String = Nothing, subtitle As String = Nothing
        SyncLock _sync
            ok = _state.GoToPage(target)
            If ok Then DescribePage(title, subtitle)
        End SyncLock
        If ok AndAlso notify Then RaiseEvent Navigated(title, subtitle)
        Return ok
    End Function

    ''' <summary>Profil manuell wählen ("@next"/"@prev" = reihum); wird als aktives Profil gespeichert.</summary>
    Public Function SelectProfile(name As String, Optional notify As Boolean = False) As Boolean
        Dim ok As Boolean
        Dim before As ProfileConfig
        Dim title As String = Nothing, subtitle As String = Nothing
        SyncLock _sync
            before = _state.CurrentProfile
            ok = _state.SelectProfile(name)
            If ok Then DescribeProfile(title, subtitle)
        End SyncLock
        If ok Then RaiseEvent ConfigChanged()
        If ok AndAlso notify AndAlso before IsNot _state.CurrentProfile Then RaiseEvent Navigated(title, subtitle)
        Return ok
    End Function

    ''' <summary>Smart Profiles: Profil nach Vordergrundprogramm wählen. Ein Wechsel wird gemeldet (Navigated).</summary>
    Public Function ApplyForegroundProcess(processName As String) As Boolean
        Dim ok As Boolean
        Dim title As String = Nothing, subtitle As String = Nothing
        SyncLock _sync
            ok = _state.ApplyForegroundProcess(processName)
            If ok Then
                DescribeProfile(title, subtitle)
                If _state.IsAutoProfile Then subtitle = $"automatisch für {processName}"
            End If
        End SyncLock
        If ok Then RaiseEvent Navigated(title, subtitle)
        Return ok
    End Function

    Private Sub DescribeProfile(ByRef title As String, ByRef subtitle As String)
        title = $"Profil: {_state.CurrentProfile.Name}"
        subtitle = $"Seite {_state.CurrentPage.Name}"
    End Sub

    Private Sub DescribePage(ByRef title As String, ByRef subtitle As String)
        Dim page = _state.CurrentPage
        Dim profile = _state.CurrentProfile
        If page.IsFolder Then
            title = $"Ordner: {page.Name}"
            subtitle = $"Profil {profile.Name} · Zurück auf Taste {_state.Config.FolderBackKey}"
        Else
            Dim pages = profile.Pages.Where(Function(p) Not p.IsFolder).ToList()
            title = $"Seite: {page.Name}"
            subtitle = $"Profil {profile.Name} · {pages.IndexOf(page) + 1}/{pages.Count}"
        End If
    End Sub

    ''' <summary>Kurzer Zustandstext, z. B. "Profil Standard · Seite Start" (für Tray-Tooltip und Menü).</summary>
    Public Function Summary() As String
        SyncLock _sync
            Dim page = _state.CurrentPage
            Return $"Profil {_state.CurrentProfile.Name}{If(_state.IsAutoProfile, " (auto)", "")} · {If(page.IsFolder, "Ordner", "Seite")} {page.Name}"
        End SyncLock
    End Function

    ''' <summary>
    ''' Ändert die Konfiguration: Kopie anlegen, mutate darauf anwenden, Kopie übernehmen.
    ''' Optional danach gezielt Profil/Seite anzeigen (z. B. nach Umbenennen oder Anlegen).
    ''' </summary>
    Public Sub UpdateConfig(mutate As Action(Of DeckConfig), Optional showProfile As String = Nothing, Optional showPage As String = Nothing)
        SyncLock _sync
            Dim copy = ConfigStore.CloneConfig(_state.Config)
            mutate(copy)
            _state.ReplaceConfig(copy, showProfile, showPage)
            _tracker.LongPressMs = _state.Config.LongPressMs
        End SyncLock
        RaiseEvent ConfigChanged()
    End Sub

    ''' <summary>Komplett neue Konfiguration (Import).</summary>
    Public Sub ReplaceConfig(config As DeckConfig)
        SyncLock _sync
            _state.ReplaceConfig(config)
            _tracker.LongPressMs = _state.Config.LongPressMs
        End SyncLock
        RaiseEvent ConfigChanged()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        _timer?.Dispose()
        _timer = Nothing
    End Sub
End Class
