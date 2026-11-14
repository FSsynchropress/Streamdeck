' Serielles Protokoll Version 1 (siehe CLAUDE.md) – Auswertung der Zeilen vom Gerät.

Public Enum MessageKind
    Unknown
    Hello
    KeyDown
    KeyUp
    Pong
End Enum

Public Structure DeviceMessage
    Public Kind As MessageKind
    Public KeyIndex As Integer
    Public ProtocolVersion As Integer
    Public KeyCount As Integer
End Structure

Public Module DeviceProtocol
    Public Const HelloRequest As String = "HELLO?"
    Public Const PingRequest As String = "PING"
    Public Const SupportedVersion As Integer = 1

    ''' <summary>Wertet eine Zeile vom Gerät aus. Unbekanntes (z. B. Diagnoseausgaben) liefert Kind = Unknown.</summary>
    Public Function Parse(line As String) As DeviceMessage
        Dim msg As New DeviceMessage With {.Kind = MessageKind.Unknown, .KeyIndex = -1}
        If line Is Nothing Then Return msg
        Dim s = line.Trim()
        If s.Length = 0 Then Return msg

        If s = "PONG" Then
            msg.Kind = MessageKind.Pong
            Return msg
        End If

        Dim parts = s.Split(" "c, StringSplitOptions.RemoveEmptyEntries)

        If parts.Length = 2 AndAlso (parts(0) = "D" OrElse parts(0) = "U") Then
            Dim n As Integer
            If Integer.TryParse(parts(1), Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture, n) AndAlso n <= 255 Then
                msg.Kind = If(parts(0) = "D", MessageKind.KeyDown, MessageKind.KeyUp)
                msg.KeyIndex = n
            End If
            Return msg
        End If

        If parts.Length = 4 AndAlso parts(0) = "HELLO" AndAlso parts(1) = "STREAMDECK" Then
            Dim v, k As Integer
            If Integer.TryParse(parts(2), v) AndAlso Integer.TryParse(parts(3), k) AndAlso v > 0 AndAlso k > 0 Then
                msg.Kind = MessageKind.Hello
                msg.ProtocolVersion = v
                msg.KeyCount = k
            End If
        End If
        Return msg
    End Function

    Public Function LedCommand(r As Integer, g As Integer, b As Integer) As String
        Return $"LED {Clamp(r)} {Clamp(g)} {Clamp(b)}"
    End Function

    Private Function Clamp(v As Integer) As Integer
        Return Math.Max(0, Math.Min(255, v))
    End Function
End Module
