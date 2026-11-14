' Kurze Einblendung unten in der Bildschirmmitte (Profil/Seite/Ordner gewechselt, Timer, Mikrofon).
' Das Fenster nimmt nie den Fokus, ist für Mausklicks durchlässig und blendet sich selbst aus.
Imports System.Drawing.Drawing2D
Imports System.Runtime.InteropServices

Public Class OverlayForm
    Inherits Form

    Public Const VisibleMs As Integer = 1400
    Private Const FadeSteps As Integer = 8
    Private Const MaxOpacity As Double = 0.96

    Private ReadOnly _hideTimer As New System.Windows.Forms.Timer With {.Interval = VisibleMs}
    Private ReadOnly _fadeTimer As New System.Windows.Forms.Timer With {.Interval = 25}
    Private _title As String = ""
    Private _subtitle As String = ""
    Private _glyph As String = ""

    Public Sub New()
        FormBorderStyle = FormBorderStyle.None
        ShowInTaskbar = False
        StartPosition = FormStartPosition.Manual
        TopMost = True
        BackColor = Theme.Raised
        DoubleBuffered = True
        Opacity = 0
        AddHandler _hideTimer.Tick, Sub()
                                        _hideTimer.Stop()
                                        _fadeTimer.Start()
                                    End Sub
        AddHandler _fadeTimer.Tick, Sub()
                                        Opacity = Math.Max(0, Opacity - MaxOpacity / FadeSteps)
                                        If Opacity <= 0 Then
                                            _fadeTimer.Stop()
                                            Hide()
                                        End If
                                    End Sub
    End Sub

    Protected Overrides ReadOnly Property ShowWithoutActivation As Boolean
        Get
            Return True
        End Get
    End Property

    Protected Overrides ReadOnly Property CreateParams As CreateParams
        Get
            Const WS_EX_TOPMOST = &H8, WS_EX_TRANSPARENT = &H20, WS_EX_TOOLWINDOW = &H80, WS_EX_NOACTIVATE = &H8000000
            Dim cp = MyBase.CreateParams
            cp.ExStyle = cp.ExStyle Or WS_EX_TOPMOST Or WS_EX_TRANSPARENT Or WS_EX_TOOLWINDOW Or WS_EX_NOACTIVATE
            Return cp
        End Get
    End Property

    <DllImport("user32.dll")>
    Private Shared Function SetWindowPos(hWnd As IntPtr, after As IntPtr, x As Integer, y As Integer, cx As Integer, cy As Integer, flags As UInteger) As Boolean
    End Function

    ''' <summary>Zeigt die Meldung (ersetzt eine noch sichtbare) auf dem Bildschirm, auf dem die Maus ist.</summary>
    Public Sub ShowMessage(title As String, subtitle As String, Optional glyph As String = Nothing)
        _title = If(title, "")
        _subtitle = If(subtitle, "")
        _glyph = If(glyph, "")
        Dim dpi = DeviceDpi / 96.0F
        Dim pad = CInt(18 * dpi)
        Dim glyphW = If(_glyph.Length > 0, CInt(34 * dpi), 0)
        Dim tSize = TextRenderer.MeasureText(_title, Theme.UiSemi(13.0F))
        Dim sSize = TextRenderer.MeasureText(_subtitle, Theme.Ui(9.5F))
        Dim w = Math.Max(CInt(220 * dpi), Math.Max(tSize.Width, sSize.Width) + glyphW + pad * 2 + CInt(6 * dpi))
        Dim h = pad * 2 + tSize.Height + If(_subtitle.Length > 0, sSize.Height + CInt(2 * dpi), 0)
        Dim area = Screen.FromPoint(Cursor.Position).WorkingArea
        Bounds = New Rectangle(area.Left + (area.Width - w) \ 2, area.Bottom - h - CInt(80 * dpi), w, h)
        Using path = Rounded(New Rectangle(0, 0, w, h), CInt(12 * dpi))
            Region = New Region(path)
        End Using
        _fadeTimer.Stop()
        _hideTimer.Stop()
        Opacity = MaxOpacity
        If Not Visible Then Show()
        ' über allen Fenstern halten, ohne den Fokus zu nehmen (SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE)
        SetWindowPos(Handle, New IntPtr(-1), 0, 0, 0, 0, &H1UI Or &H2UI Or &H10UI)
        Invalidate()
        _hideTimer.Start()
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim dpi = DeviceDpi / 96.0F
        Dim pad = CInt(18 * dpi)
        Using accent As New SolidBrush(Theme.Accent)
            g.FillRectangle(accent, 0, 0, CInt(5 * dpi), Height)
        End Using
        Dim x = pad
        If _glyph.Length > 0 Then
            TextRenderer.DrawText(g, _glyph, Theme.Icons(18.0F), New Rectangle(x, 0, CInt(28 * dpi), Height), Theme.Accent,
                                  TextFormatFlags.VerticalCenter Or TextFormatFlags.HorizontalCenter Or TextFormatFlags.NoPrefix)
            x += CInt(34 * dpi)
        End If
        Dim tf = Theme.UiSemi(13.0F)
        Dim th = TextRenderer.MeasureText(_title, tf).Height
        TextRenderer.DrawText(g, _title, tf, New Point(x, pad), Theme.Fg, TextFormatFlags.NoPrefix)
        If _subtitle.Length > 0 Then
            TextRenderer.DrawText(g, _subtitle, Theme.Ui(9.5F), New Point(x, pad + th + CInt(2 * dpi)), Theme.Muted, TextFormatFlags.NoPrefix)
        End If
    End Sub

    Private Shared Function Rounded(r As Rectangle, radius As Integer) As GraphicsPath
        Dim d = radius * 2
        Dim p As New GraphicsPath()
        p.AddArc(r.X, r.Y, d, d, 180, 90)
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90)
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
        p.CloseFigure()
        Return p
    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _hideTimer.Dispose()
            _fadeTimer.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
