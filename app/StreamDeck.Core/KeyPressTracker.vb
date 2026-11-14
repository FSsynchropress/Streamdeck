' Erkennt Tastengesten anhand von D/U und Zeitstempeln: kurz, lang, doppelt, Wiederholung, Loslassen.
'
' - Taste ohne lang/doppelt: kurzer Druck sofort beim Drücken (keine Verzögerung).
'   Mit "repeat": nach RepeatDelayMs wird der kurze Druck alle RepeatIntervalMs wiederholt, bis losgelassen.
' - Taste mit langem Druck: Loslassen vor LongPressMs -> kurz, sonst bei Erreichen von LongPressMs -> lang.
' - Taste mit Doppeldruck: nach dem (kurzen) Loslassen wird DoublePressMs auf einen zweiten Druck gewartet;
'   kommt er, gilt sofort "doppelt", sonst danach "kurz".
' - Loslassen meldet immer Release (für Aktionen beim Loslassen, z. B. Push-to-Talk).

Public Enum PressKind
    ShortPress
    LongPress
    DoublePress
    Repeat
    Release
End Enum

''' <summary>Was eine Taste kann (bestimmt, ob und wie lange auf die Entscheidung gewartet wird).</summary>
Public Structure KeyCaps
    Public HasLong As Boolean
    Public HasDouble As Boolean
    Public Repeat As Boolean

    Public Shared Function [For](key As KeyConfig) As KeyCaps
        If key Is Nothing Then Return New KeyCaps
        Return New KeyCaps With {
            .HasLong = HasAction(key.LongPress),
            .HasDouble = HasAction(key.DoublePress),
            .Repeat = key.Repeat.GetValueOrDefault() AndAlso HasAction(key.Press)}
    End Function

    Private Shared Function HasAction(a As ActionConfig) As Boolean
        Return a IsNot Nothing AndAlso Not String.IsNullOrEmpty(a.ActionType)
    End Function
End Structure

Public Class KeyPressTracker
    Private Enum Phase
        Holding         ' gedrückt, Entscheidung kurz/lang offen
        LongFired       ' gedrückt, lang schon ausgelöst
        WaitingSecond   ' losgelassen, wartet auf zweiten Druck
        SecondHeld      ' zweiter Druck (doppelt ausgelöst), noch gedrückt
        Repeating       ' gedrückt, kurz ausgelöst, wiederholt
    End Enum

    Private Class State
        Public Caps As KeyCaps
        Public Phase As Phase
        Public DownAt As Long
        Public UpAt As Long
        Public NextRepeat As Long
    End Class

    Private ReadOnly _caps As Func(Of Integer, KeyCaps)
    Private ReadOnly _states As New Dictionary(Of Integer, State)

    Public Property LongPressMs As Integer = 500
    Public Property DoublePressMs As Integer = 300
    Public Property RepeatDelayMs As Integer = 400
    Public Property RepeatIntervalMs As Integer = 120

    Public Sub New(caps As Func(Of Integer, KeyCaps))
        _caps = caps
    End Sub

    ''' <summary>Taste gedrückt. Liefert sofort fällige Ereignisse (kurz oder doppelt).</summary>
    Public Function OnDown(key As Integer, nowMs As Long) As List(Of PressKind)
        Dim result As New List(Of PressKind)
        Dim st As State = Nothing
        If _states.TryGetValue(key, st) AndAlso st.Phase = Phase.WaitingSecond Then
            If nowMs - st.UpAt <= DoublePressMs Then
                st.Phase = Phase.SecondHeld
                result.Add(PressKind.DoublePress)
                Return result
            End If
            ' Fenster verpasst (Tick kam zu spät): erster Druck war kurz
            result.Add(PressKind.ShortPress)
            _states.Remove(key)
        End If

        Dim caps = _caps(key)
        If Not caps.HasLong AndAlso Not caps.HasDouble Then
            result.Add(PressKind.ShortPress)
            If caps.Repeat Then _states(key) = New State With {.Caps = caps, .Phase = Phase.Repeating, .DownAt = nowMs, .NextRepeat = nowMs + RepeatDelayMs}
            Return result
        End If
        _states(key) = New State With {.Caps = caps, .Phase = Phase.Holding, .DownAt = nowMs}
        Return result
    End Function

    ''' <summary>Taste losgelassen. Liefert ggf. kurz/lang und immer zuletzt Release.</summary>
    Public Function OnUp(key As Integer, nowMs As Long) As List(Of PressKind)
        Dim result As New List(Of PressKind)
        Dim st As State = Nothing
        If _states.TryGetValue(key, st) Then
            Select Case st.Phase
                Case Phase.Holding
                    If st.Caps.HasLong AndAlso nowMs - st.DownAt >= LongPressMs Then
                        result.Add(PressKind.LongPress)        ' Tick kam zu spät
                        _states.Remove(key)
                    ElseIf st.Caps.HasDouble Then
                        st.Phase = Phase.WaitingSecond
                        st.UpAt = nowMs
                    Else
                        result.Add(PressKind.ShortPress)
                        _states.Remove(key)
                    End If
                Case Phase.WaitingSecond
                    ' Up ohne Down – ignorieren
                Case Else
                    _states.Remove(key)
            End Select
        End If
        result.Add(PressKind.Release)
        Return result
    End Function

    ''' <summary>Regelmäßig aufrufen. Liefert fällige Ereignisse (lang, kurz nach verpasstem Doppeldruck, Wiederholung).</summary>
    Public Function Tick(nowMs As Long) As List(Of (Key As Integer, Kind As PressKind))
        Dim due As New List(Of (Key As Integer, Kind As PressKind))
        For Each kv In _states.ToList()
            Dim st = kv.Value
            Select Case st.Phase
                Case Phase.Holding
                    If st.Caps.HasLong AndAlso nowMs - st.DownAt >= LongPressMs Then
                        due.Add((kv.Key, PressKind.LongPress))
                        st.Phase = Phase.LongFired
                    End If
                Case Phase.WaitingSecond
                    If nowMs - st.UpAt > DoublePressMs Then
                        due.Add((kv.Key, PressKind.ShortPress))
                        _states.Remove(kv.Key)
                    End If
                Case Phase.Repeating
                    If nowMs >= st.NextRepeat Then
                        due.Add((kv.Key, PressKind.Repeat))
                        st.NextRepeat = Math.Max(st.NextRepeat + RepeatIntervalMs, nowMs + RepeatIntervalMs \ 2)
                    End If
            End Select
        Next
        due.Sort(Function(a, b) a.Key.CompareTo(b.Key))
        Return due
    End Function

    ''' <summary>Wartet für diese Taste noch etwas (auch nach dem Loslassen, z. B. auf den zweiten Druck)?</summary>
    Public Function IsPending(key As Integer) As Boolean
        Return _states.ContainsKey(key)
    End Function

    Public ReadOnly Property PendingCount As Integer
        Get
            Return _states.Count
        End Get
    End Property

    Public Sub Reset()
        _states.Clear()
    End Sub
End Class
