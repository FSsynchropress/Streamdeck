' Berechnung für die Aktion window (ohne Windows-Aufrufe, damit testbar): neues Fensterrechteck
' beim Wechsel auf einen anderen Monitor oder beim Platzieren in Prozent des Arbeitsbereichs.

Public Structure RectI
    Public X As Integer
    Public Y As Integer
    Public Width As Integer
    Public Height As Integer

    Public Sub New(x As Integer, y As Integer, width As Integer, height As Integer)
        Me.X = x
        Me.Y = y
        Me.Width = width
        Me.Height = height
    End Sub

    Public Overrides Function ToString() As String
        Return $"{X},{Y} {Width}x{Height}"
    End Function
End Structure

Public Module WindowLayout
    ''' <summary>
    ''' Fenster von einem Monitor auf einen anderen bringen: relative Lage und Größe bleiben erhalten
    ''' (skaliert auf den Arbeitsbereich des Zielmonitors), das Fenster bleibt vollständig sichtbar.
    ''' </summary>
    Public Function MoveToArea(window As RectI, fromArea As RectI, toArea As RectI) As RectI
        Dim sx = toArea.Width / Math.Max(1.0, fromArea.Width)
        Dim sy = toArea.Height / Math.Max(1.0, fromArea.Height)
        Dim w = Math.Min(toArea.Width, CInt(Math.Round(window.Width * sx)))
        Dim h = Math.Min(toArea.Height, CInt(Math.Round(window.Height * sy)))
        Dim x = toArea.X + CInt(Math.Round((window.X - fromArea.X) * sx))
        Dim y = toArea.Y + CInt(Math.Round((window.Y - fromArea.Y) * sy))
        x = Math.Max(toArea.X, Math.Min(x, toArea.X + toArea.Width - w))
        y = Math.Max(toArea.Y, Math.Min(y, toArea.Y + toArea.Height - h))
        Return New RectI(x, y, w, h)
    End Function

    ''' <summary>Rechteck in Prozent des Arbeitsbereichs (0–100), z. B. linke Hälfte = 0, 0, 50, 100.</summary>
    Public Function PercentOf(area As RectI, xPct As Integer, yPct As Integer, wPct As Integer, hPct As Integer) As RectI
        Dim cx = Math.Max(0, Math.Min(100, xPct))
        Dim cy = Math.Max(0, Math.Min(100, yPct))
        Dim cw = Math.Max(1, Math.Min(100 - cx, wPct))
        Dim ch = Math.Max(1, Math.Min(100 - cy, hPct))
        Return New RectI(area.X + area.Width * cx \ 100, area.Y + area.Height * cy \ 100,
                         area.Width * cw \ 100, area.Height * ch \ 100)
    End Function

    ''' <summary>Monitor-Nummer (1-basiert) -> Index; ungültig wirft eine verständliche Meldung.</summary>
    Public Function MonitorIndex(monitor As Integer, count As Integer) As Integer
        If monitor < 1 OrElse monitor > count Then Throw New ArgumentException($"Monitor {monitor} gibt es nicht (vorhanden: 1–{count}).")
        Return monitor - 1
    End Function
End Module
