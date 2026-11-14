' Führt Aktionen asynchron aus. Die Windows-spezifischen Aktionstypen (hotkey, sound, obs …)
' registriert die App als Handler; multi, toggle, timer und wait sind hier eingebaut.
Imports System.Threading
Imports System.Collections.Concurrent

Public Interface IActionHandler
    Function ExecuteAsync(action As ActionConfig, context As ActionContext) As Task
End Interface

Public Class ActionContext
    ''' <summary>Taste, die die Aktion ausgelöst hat (-1 = z. B. Test-Knopf im Editor).</summary>
    Public Property KeyIndex As Integer = -1
    Public Property Cancellation As CancellationToken
    Friend Property Depth As Integer
End Class

''' <summary>Handler aus einer Funktion (praktisch für einfache Typen und Tests).</summary>
Public Class DelegateActionHandler
    Implements IActionHandler

    Private ReadOnly _fn As Func(Of ActionConfig, ActionContext, Task)

    Public Sub New(fn As Func(Of ActionConfig, ActionContext, Task))
        _fn = fn
    End Sub

    Public Shared Function FromSync(action As Action(Of ActionConfig)) As DelegateActionHandler
        Return New DelegateActionHandler(Function(a, c)
                                             action(a)
                                             Return Task.CompletedTask
                                         End Function)
    End Function

    Public Function ExecuteAsync(action As ActionConfig, context As ActionContext) As Task Implements IActionHandler.ExecuteAsync
        Return _fn(action, context)
    End Function
End Class

Public Class ActionRunner
    Public Const MaxNesting As Integer = 8
    Public Const MaxWaitMs As Integer = 60000
    Public Const MaxTimerSeconds As Double = 24 * 3600
    ''' <summary>Ton am Ende eines Timers, wenn keine Datei angegeben ist (mitgeliefert).</summary>
    Public Const DefaultTimerSound As String = "Sounds\alarm.mp3"

    Private ReadOnly _handlers As New Dictionary(Of String, IActionHandler)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _toggleState As New ConcurrentDictionary(Of String, Integer)
    Private ReadOnly _timers As New ConcurrentDictionary(Of String, CancellationTokenSource)

    Public Event Log(message As String)
    ''' <summary>Kurze Meldung für den Benutzer (Tray-Hinweis/Overlay), z. B. Timer abgelaufen.</summary>
    Public Event Notify(title As String, message As String)

    Public Sub New()
        _handlers(ActionTypes.Multi) = New DelegateActionHandler(AddressOf RunMultiAsync)
        _handlers(ActionTypes.Toggle) = New DelegateActionHandler(AddressOf RunToggleAsync)
        _handlers(ActionTypes.Timer) = New DelegateActionHandler(AddressOf RunTimerAsync)
        _handlers(ActionTypes.Random) = New DelegateActionHandler(AddressOf RunRandomAsync)
        _handlers(ActionTypes.Wait) = New DelegateActionHandler(
            Function(a, c) Task.Delay(Math.Max(0, Math.Min(MaxWaitMs, If(a.Ms, 0))), c.Cancellation))
    End Sub

    ''' <summary>Meldung an den Benutzer weitergeben (für Handler der App, z. B. Mikrofon).</summary>
    Public Sub RaiseNotify(title As String, message As String)
        RaiseEvent Notify(title, message)
    End Sub

    ''' <summary>
    ''' Schlüssel für Zustand pro Taste (Umschalter, laufender Timer). Enthält den Inhalt der Aktion,
    ''' damit der Zustand ein Neuladen der Konfiguration übersteht, solange die Aktion gleich bleibt.
    ''' </summary>
    Private Shared Function StateKey(action As ActionConfig, context As ActionContext) As String
        Return $"{context.KeyIndex}|{Text.Json.JsonSerializer.Serialize(action, ConfigStore.JsonOptions)}"
    End Function

    Public Sub Register(actionType As String, handler As IActionHandler)
        _handlers(actionType) = handler
    End Sub

    Public Function CanHandle(actionType As String) As Boolean
        Return actionType IsNot Nothing AndAlso _handlers.ContainsKey(actionType)
    End Function

    ''' <summary>Startet die Aktion im Hintergrund (blockiert nie). Fehler landen im Log.</summary>
    Public Function Fire(action As ActionConfig, Optional keyIndex As Integer = -1) As Task
        If action Is Nothing Then Return Task.CompletedTask
        Dim ctx As New ActionContext With {.KeyIndex = keyIndex, .Cancellation = CancellationToken.None}
        Return Task.Run(Function() RunAsync(action, ctx))
    End Function

    ''' <summary>Führt die Aktion aus und wartet darauf. Wirft nie – Fehler werden gemeldet.</summary>
    Public Async Function RunAsync(action As ActionConfig, context As ActionContext) As Task
        If action Is Nothing OrElse String.IsNullOrWhiteSpace(action.ActionType) Then Return
        Dim handler As IActionHandler = Nothing
        If Not _handlers.TryGetValue(action.ActionType.Trim(), handler) Then
            RaiseEvent Log($"Unbekannter Aktionstyp '{action.ActionType}'.")
            Return
        End If
        Try
            Await handler.ExecuteAsync(action, context).ConfigureAwait(False)
        Catch ex As OperationCanceledException
            ' abgebrochen – kein Fehler
        Catch ex As Exception
            Dim who = If(context.KeyIndex >= 0, $"Taste {context.KeyIndex}", "Test")
            RaiseEvent Log($"{who}: {ActionTypes.Describe(action)} fehlgeschlagen – {ex.Message}")
        End Try
    End Function

    Private Async Function RunMultiAsync(action As ActionConfig, context As ActionContext) As Task
        If action.Actions Is Nothing Then Return
        If context.Depth >= MaxNesting Then Throw New InvalidOperationException("zu tief verschachtelte Mehrfach-Aktion")
        Dim inner As New ActionContext With {.KeyIndex = context.KeyIndex, .Cancellation = context.Cancellation, .Depth = context.Depth + 1}
        Dim list = action.Actions.ToList()
        If action.Parallel.GetValueOrDefault() Then
            ' Gleichzeitig: alle starten, auf alle warten (RunAsync wirft nie)
            Await Task.WhenAll(list.Select(Function(a) Task.Run(Function() RunAsync(a, inner)))).ConfigureAwait(False)
            Return
        End If
        For Each a In list
            context.Cancellation.ThrowIfCancellationRequested()
            Await RunAsync(a, inner).ConfigureAwait(False)
        Next
    End Function

    ''' <summary>Zufallsaktion: eine Aktion der Liste zufällig ausführen (nie zweimal dieselbe hintereinander, wenn es mehrere gibt).</summary>
    Private Async Function RunRandomAsync(action As ActionConfig, context As ActionContext) As Task
        If action.Actions Is Nothing OrElse action.Actions.Count = 0 Then Return
        If context.Depth >= MaxNesting Then Throw New InvalidOperationException("zu tief verschachtelte Zufallsaktion")
        Dim list = action.Actions.ToList()
        Dim key = StateKey(action, context)
        Dim last As Integer
        If Not _toggleState.TryGetValue("rnd|" & key, last) Then last = -1
        Dim index = System.Random.Shared.Next(list.Count)
        If list.Count > 1 AndAlso index = last Then index = (index + 1 + System.Random.Shared.Next(list.Count - 1)) Mod list.Count
        _toggleState("rnd|" & key) = index
        Dim inner As New ActionContext With {.KeyIndex = context.KeyIndex, .Cancellation = context.Cancellation, .Depth = context.Depth + 1}
        Await RunAsync(list(index), inner).ConfigureAwait(False)
    End Function

    ''' <summary>
    ''' Umschalter: jeder Druck führt die nächste Aktion der Liste aus (A, B, A, B …).
    ''' Mit zwei Tastenkombinationen = Hotkey-Switch, mit zwei Mehrfach-Aktionen = Multi-Action-Switch.
    ''' </summary>
    Private Async Function RunToggleAsync(action As ActionConfig, context As ActionContext) As Task
        If action.Actions Is Nothing OrElse action.Actions.Count = 0 Then Return
        If context.Depth >= MaxNesting Then Throw New InvalidOperationException("zu tief verschachtelte Umschalt-Aktion")
        Dim list = action.Actions.ToList()
        Dim key = StateKey(action, context)
        Dim index = 0
        _toggleState.AddOrUpdate(key, 0, Function(k, old)
                                             index = (old + 1) Mod list.Count
                                             Return index
                                         End Function)
        Dim inner As New ActionContext With {.KeyIndex = context.KeyIndex, .Cancellation = context.Cancellation, .Depth = context.Depth + 1}
        Await RunAsync(list(index), inner).ConfigureAwait(False)
    End Function

    ''' <summary>Welche Aktion des Umschalters beim nächsten Druck dran ist (0-basiert; für Anzeige und Tests).</summary>
    Public Function NextToggleIndex(action As ActionConfig, keyIndex As Integer) As Integer
        Dim current As Integer
        If action?.Actions Is Nothing OrElse action.Actions.Count = 0 Then Return 0
        If Not _toggleState.TryGetValue(StateKey(action, New ActionContext With {.KeyIndex = keyIndex}), current) Then Return 0
        Return (current + 1) Mod action.Actions.Count
    End Function

    ''' <summary>
    ''' Countdown: nach "seconds" Sekunden Ton abspielen und Meldung zeigen.
    ''' Erneuter Druck während der Timer läuft bricht ihn ab.
    ''' </summary>
    Private Async Function RunTimerAsync(action As ActionConfig, context As ActionContext) As Task
        Dim seconds = action.Seconds.GetValueOrDefault()
        If seconds <= 0 Then Throw New ArgumentException("Keine Dauer angegeben.")
        seconds = Math.Min(seconds, MaxTimerSeconds)
        Dim key = StateKey(action, context)
        Dim running As CancellationTokenSource = Nothing
        If _timers.TryRemove(key, running) Then
            running.Cancel()
            RaiseEvent Log($"Timer {ActionTypes.FormatSeconds(seconds)} abgebrochen")
            RaiseEvent Notify("Timer abgebrochen", ActionTypes.FormatSeconds(seconds))
            Return
        End If

        Using cts = CancellationTokenSource.CreateLinkedTokenSource(context.Cancellation)
            If Not _timers.TryAdd(key, cts) Then Return
            RaiseEvent Log($"Timer {ActionTypes.FormatSeconds(seconds)} gestartet")
            RaiseEvent Notify("Timer gestartet", $"{ActionTypes.FormatSeconds(seconds)} – erneuter Druck bricht ab")
            Try
                Await Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                Return
            Finally
                Dim mine As CancellationTokenSource = Nothing
                If _timers.TryGetValue(key, mine) AndAlso mine Is cts Then _timers.TryRemove(key, mine)
            End Try
        End Using

        RaiseEvent Log($"Timer {ActionTypes.FormatSeconds(seconds)} abgelaufen")
        RaiseEvent Notify("Timer abgelaufen", ActionTypes.FormatSeconds(seconds))
        Dim sound As New ActionConfig With {
            .ActionType = ActionTypes.Sound,
            .File = If(String.IsNullOrWhiteSpace(action.File), DefaultTimerSound, action.File),
            .Volume = action.Volume,
            .Device = action.Device,
            .Restart = True}
        Await RunAsync(sound, New ActionContext With {.KeyIndex = context.KeyIndex, .Cancellation = context.Cancellation, .Depth = context.Depth + 1}).ConfigureAwait(False)
    End Function

    ''' <summary>Anzahl laufender Timer (für Tray-Anzeige und Tests).</summary>
    Public ReadOnly Property RunningTimerCount As Integer
        Get
            Return _timers.Count
        End Get
    End Property
End Class
