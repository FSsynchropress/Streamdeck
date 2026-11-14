' Erkennt kurzen und langen Tastendruck anhand von D/U und Zeitstempeln.
'
' - Taste OHNE Aktion für langen Druck: der kurze Druck wird sofort beim Drücken ausgelöst
'   (keine Verzögerung).
' - Taste MIT Aktion für langen Druck: beim Loslassen vor Ablauf von LongPressMs -> kurz,
'   sonst wird beim Erreichen von LongPressMs (noch gedrückt) lang ausgelöst.

Public Enum PressKind
    ShortPress
    LongPress
End Enum

Public Class KeyPressTracker
    Private ReadOnly _hasLongAction As Func(Of Integer, Boolean)
    Private ReadOnly _pending As New Dictionary(Of Integer, Long)  ' Taste -> Zeitpunkt des Drückens

    Public Property LongPressMs As Integer = 500

    Public Sub New(hasLongAction As Func(Of Integer, Boolean))
        _hasLongAction = hasLongAction
    End Sub

    ''' <summary>Taste gedrückt. Liefert ShortPress, wenn sofort ausgelöst werden soll.</summary>
    Public Function OnDown(key As Integer, nowMs As Long) As PressKind?
        If _hasLongAction(key) Then
            _pending(key) = nowMs
            Return Nothing
        End If
        _pending.Remove(key)
        Return PressKind.ShortPress
    End Function

    ''' <summary>Taste losgelassen. Liefert ShortPress, wenn noch kein langer Druck ausgelöst wurde.</summary>
    Public Function OnUp(key As Integer, nowMs As Long) As PressKind?
        Dim downAt As Long
        If Not _pending.TryGetValue(key, downAt) Then Return Nothing
        _pending.Remove(key)
        If nowMs - downAt >= LongPressMs Then Return PressKind.LongPress  ' Tick kam zu spät
        Return PressKind.ShortPress
    End Function

    ''' <summary>Regelmäßig aufrufen. Liefert die Tasten, deren langer Druck jetzt fällig ist.</summary>
    Public Function Tick(nowMs As Long) As List(Of Integer)
        Dim due As New List(Of Integer)
        For Each kv In _pending
            If nowMs - kv.Value >= LongPressMs Then due.Add(kv.Key)
        Next
        For Each k In due
            _pending.Remove(k)
        Next
        due.Sort()
        Return due
    End Function

    Public ReadOnly Property PendingCount As Integer
        Get
            Return _pending.Count
        End Get
    End Property

    Public Sub Reset()
        _pending.Clear()
    End Sub
End Class
