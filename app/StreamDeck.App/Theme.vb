' Dunkles „Werkbank“-Design: Farben, Schriften, Symbole und selbst gezeichnete Bausteine
' (Knöpfe, Navigationseinträge, Segmentschalter, Menü-Renderer). Grundlage ist das Design-Canvas
' „Stream Deck App“ (Hauptfenster, Aktionseditor, Einstellungen, Infobereich).
Imports System.Drawing.Drawing2D
Imports System.Runtime.InteropServices
Imports StreamDeck.Core

Public Module Theme
    ' --- Farben
    Public ReadOnly Bg As Color = Color.FromArgb(15, 16, 18)            ' Grundfläche
    Public ReadOnly Chrome As Color = Color.FromArgb(11, 12, 14)        ' Titel-/Statusleiste
    Public ReadOnly SidebarBg As Color = Color.FromArgb(18, 20, 23)
    Public ReadOnly Surface As Color = Color.FromArgb(21, 23, 26)        ' Inspektor, Dialoge
    Public ReadOnly Raised As Color = Color.FromArgb(26, 29, 33)         ' Knöpfe, Karten
    Public ReadOnly SelectedBg As Color = Color.FromArgb(34, 38, 43)
    Public ReadOnly HoverBg As Color = Color.FromArgb(28, 31, 35)
    Public ReadOnly LineColor As Color = Color.FromArgb(35, 38, 43)
    Public ReadOnly BorderColor As Color = Color.FromArgb(44, 48, 54)
    Public ReadOnly DashColor As Color = Color.FromArgb(58, 63, 70)
    Public ReadOnly PlateColor As Color = Color.FromArgb(10, 11, 13)
    Public ReadOnly InputBg As Color = Color.FromArgb(15, 16, 18)
    Public ReadOnly KeyCap As Color = Color.FromArgb(35, 39, 44)
    Public ReadOnly KeyCapHover As Color = Color.FromArgb(42, 47, 53)
    Public ReadOnly KeyEdge As Color = Color.FromArgb(52, 57, 64)
    Public ReadOnly KeySkirt As Color = Color.FromArgb(5, 6, 7)
    Public ReadOnly Fg As Color = Color.FromArgb(236, 237, 239)
    Public ReadOnly FgSoft As Color = Color.FromArgb(201, 205, 210)
    Public ReadOnly Muted As Color = Color.FromArgb(160, 166, 174)
    Public ReadOnly Faint As Color = Color.FromArgb(133, 139, 147)
    Public ReadOnly Accent As Color = Color.FromArgb(242, 169, 59)
    Public ReadOnly AccentHover As Color = Color.FromArgb(255, 189, 92)
    Public ReadOnly AccentText As Color = Color.FromArgb(26, 18, 6)
    Public ReadOnly AccentDim As Color = Color.FromArgb(58, 45, 22)
    Public ReadOnly AccentSoftText As Color = Color.FromArgb(255, 226, 176)
    Public ReadOnly Good As Color = Color.FromArgb(67, 209, 127)
    Public ReadOnly GoodText As Color = Color.FromArgb(123, 227, 166)
    Public ReadOnly GoodBg As Color = Color.FromArgb(20, 38, 28)
    Public ReadOnly Bad As Color = Color.FromArgb(255, 107, 94)
    Public ReadOnly BadText As Color = Color.FromArgb(255, 138, 126)
    Public ReadOnly BadBg As Color = Color.FromArgb(46, 24, 22)

    ' --- Schriften (Inter/JetBrains Mono aus dem Entwurf → Windows-Gegenstücke)
    Public ReadOnly UiFamily As String = PickFamily("Segoe UI")
    Public ReadOnly UiSemiFamily As String = PickFamily("Segoe UI Semibold", "Segoe UI")
    Public ReadOnly MonoFamily As String = PickFamily("Cascadia Mono", "Consolas")
    Public ReadOnly IconFamily As String = PickFamily("Segoe Fluent Icons", "Segoe MDL2 Assets")

    Private ReadOnly FontCache As New Dictionary(Of String, Font)

    Private Function PickFamily(ParamArray names As String()) As String
        Dim installed = FontFamily.Families.Select(Function(f) f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
        For Each n In names
            If installed.Contains(n) Then Return n
        Next
        Return SystemFonts.MessageBoxFont.FontFamily.Name
    End Function

    Private Function Cached(family As String, size As Single, style As FontStyle) As Font
        Dim key = $"{family}|{size}|{CInt(style)}"
        Dim f As Font = Nothing
        If Not FontCache.TryGetValue(key, f) Then
            f = New Font(family, size, style)
            FontCache(key) = f
        End If
        Return f
    End Function

    ''' <summary>Normale UI-Schrift (Punkt).</summary>
    Public Function Ui(size As Single) As Font
        Return Cached(UiFamily, size, FontStyle.Regular)
    End Function

    ''' <summary>Halbfett.</summary>
    Public Function UiSemi(size As Single) As Font
        Return Cached(UiSemiFamily, size, FontStyle.Regular)
    End Function

    Public Function UiBold(size As Single) As Font
        Return Cached(UiFamily, size, FontStyle.Bold)
    End Function

    Public Function Mono(size As Single) As Font
        Return Cached(MonoFamily, size, FontStyle.Regular)
    End Function

    Public Function Icons(size As Single) As Font
        Return Cached(IconFamily, size, FontStyle.Regular)
    End Function

    ' --- Symbole (Segoe Fluent Icons / MDL2 Assets)
    Public ReadOnly GlyphAdd As String = ChrW(&HE710)
    Public ReadOnly GlyphMore As String = ChrW(&HE712)
    Public ReadOnly GlyphSettings As String = ChrW(&HE713)
    Public ReadOnly GlyphPlay As String = ChrW(&HE768)
    Public ReadOnly GlyphEdit As String = ChrW(&HE70F)
    Public ReadOnly GlyphDelete As String = ChrW(&HE74D)
    Public ReadOnly GlyphUpload As String = ChrW(&HE898)
    Public ReadOnly GlyphDownload As String = ChrW(&HE896)
    Public ReadOnly GlyphFolder As String = ChrW(&HE838)
    Public ReadOnly GlyphUp As String = ChrW(&HE70E)
    Public ReadOnly GlyphDown As String = ChrW(&HE70D)
    Public ReadOnly GlyphClose As String = ChrW(&HE711)

    ''' <summary>Symbol für eine Aktion.</summary>
    Public Function GlyphFor(a As ActionConfig) As String
        If a Is Nothing Then Return GlyphAdd
        Select Case If(a.ActionType, "").ToLowerInvariant()
            Case ActionTypes.Media
                Select Case If(a.MediaKey, "").ToLowerInvariant()
                    Case "mute" : Return ChrW(&HE74F)
                    Case "playpause" : Return ChrW(&HE768)
                    Case "next" : Return ChrW(&HE893)
                    Case "prev" : Return ChrW(&HE892)
                    Case "stop" : Return ChrW(&HE71A)
                    Case "volup" : Return ChrW(&HE995)
                    Case "voldown" : Return ChrW(&HE993)
                    Case Else : Return ChrW(&HE767)
                End Select
            Case ActionTypes.Sound : Return ChrW(&HE8D6)
            Case ActionTypes.Launch : Return ChrW(&HE8A7)
            Case ActionTypes.Hotkey : Return ChrW(&HE765)
            Case ActionTypes.Text : Return ChrW(&HE8D2)
            Case ActionTypes.Url : Return ChrW(&HE774)
            Case ActionTypes.Command : Return ChrW(&HE756)
            Case ActionTypes.Obs : Return ChrW(&HE714)
            Case ActionTypes.Page
                If a.Target = ActionTypes.PageBack Then Return ChrW(&HE72B)
                If a.Target = ActionTypes.PageFirst Then Return ChrW(&HE80F)
                Return ChrW(&HE72A)
            Case ActionTypes.Profile : Return ChrW(&HE77B)
            Case ActionTypes.Multi : Return ChrW(&HE81E)
            Case ActionTypes.Wait : Return ChrW(&HE916)
            Case ActionTypes.Close : Return ChrW(&HE711)
            Case ActionTypes.Http : Return ChrW(&HE909)
            Case ActionTypes.Mic : Return If(String.Equals(a.Op, "unmute", StringComparison.OrdinalIgnoreCase), ChrW(&HE720), ChrW(&HEC54))
            Case ActionTypes.Timer : Return ChrW(&HE823)
            Case ActionTypes.Toggle : Return ChrW(&HE8AB)
            Case Else : Return ChrW(&HE946)
        End Select
    End Function

    ''' <summary>Kurzer Typname für die Unterzeile einer Taste.</summary>
    Public Function CaptionFor(a As ActionConfig) As String
        If a Is Nothing Then Return ""
        Select Case If(a.ActionType, "").ToLowerInvariant()
            Case ActionTypes.Media : Return "Medien"
            Case ActionTypes.Sound : Return "Sound"
            Case ActionTypes.Launch : Return "Programm"
            Case ActionTypes.Hotkey : Return "Hotkey"
            Case ActionTypes.Text : Return "Text"
            Case ActionTypes.Url : Return "Webseite"
            Case ActionTypes.Command : Return "Befehl"
            Case ActionTypes.Obs : Return "OBS"
            Case ActionTypes.Page : Return "Seite"
            Case ActionTypes.Profile : Return "Profil"
            Case ActionTypes.Multi : Return "Mehrfach"
            Case ActionTypes.Wait : Return "Warten"
            Case ActionTypes.Close : Return "Schließen"
            Case ActionTypes.Http : Return "HTTP"
            Case ActionTypes.Mic : Return "Mikro"
            Case ActionTypes.Timer : Return "Timer"
            Case ActionTypes.Toggle : Return "Umschalter"
            Case Else : Return a.ActionType
        End Select
    End Function

    ' --- Zeichen-Hilfen

    Public Function Px(c As Control, value As Single) As Single
        Return value * c.DeviceDpi / 96.0F
    End Function

    Public Function PxI(c As Control, value As Integer) As Integer
        Return CInt(Math.Round(value * c.DeviceDpi / 96.0))
    End Function

    Public Function Round(r As RectangleF, radius As Single) As GraphicsPath
        Return TrayContext.RoundedRect(r, Math.Max(0.5F, Math.Min(radius, Math.Min(r.Width, r.Height) / 2)))
    End Function

    Public Sub FillRound(g As Graphics, color As Color, r As RectangleF, radius As Single)
        Using p = Round(r, radius), b As New SolidBrush(color)
            g.FillPath(b, p)
        End Using
    End Sub

    Public Sub DrawRound(g As Graphics, color As Color, r As RectangleF, radius As Single, Optional width As Single = 1.0F, Optional dashed As Boolean = False)
        Using p = Round(r, radius), pen As New Pen(color, width)
            If dashed Then pen.DashPattern = {4.0F, 3.0F}
            g.DrawPath(pen, p)
        End Using
    End Sub

    ''' <summary>Zeichnet einen kleinen gefüllten Statuspunkt.</summary>
    Public Sub Dot(g As Graphics, color As Color, cx As Single, cy As Single, d As Single)
        Using b As New SolidBrush(color)
            g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d)
        End Using
    End Sub

    ' --- Fenster

    <DllImport("dwmapi.dll")>
    Private Function DwmSetWindowAttribute(hwnd As IntPtr, attr As Integer, ByRef value As Integer, size As Integer) As Integer
    End Function

    Private Function ColorRef(c As Color) As Integer
        Return c.R Or (CInt(c.G) << 8) Or (CInt(c.B) << 16)
    End Function

    ''' <summary>Dunkle Titelleiste (Windows 10 20H1+) und unter Windows 11 die Chrome-Farbe.</summary>
    Public Sub DarkTitleBar(f As Form)
        Try
            Dim darkOn = 1
            DwmSetWindowAttribute(f.Handle, 20, darkOn, 4)        ' DWMWA_USE_IMMERSIVE_DARK_MODE
            Dim caption = ColorRef(Chrome)
            DwmSetWindowAttribute(f.Handle, 35, caption, 4)       ' DWMWA_CAPTION_COLOR (Windows 11)
            Dim border = ColorRef(LineColor)
            DwmSetWindowAttribute(f.Handle, 34, border, 4)        ' DWMWA_BORDER_COLOR (Windows 11)
        Catch ex As Exception When TypeOf ex Is DllNotFoundException OrElse TypeOf ex Is EntryPointNotFoundException
        End Try
    End Sub

    ''' <summary>Färbt Standard-Steuerelemente (rekursiv) im dunklen Design.</summary>
    Public Sub Apply(root As Control)
        For Each c As Control In root.Controls
            StyleOne(c)
            If c.HasChildren AndAlso Not TypeOf c Is NumericUpDown Then Apply(c)
        Next
    End Sub

    Public Sub StyleOne(c As Control)
        Select Case True
            Case TypeOf c Is ThemedButton, TypeOf c Is NavItem, TypeOf c Is Segmented
                ' zeichnen sich selbst
            Case TypeOf c Is TextBox
                Dim tb = DirectCast(c, TextBox)
                tb.BorderStyle = BorderStyle.FixedSingle
                If tb.BackColor <> AccentDim Then tb.BackColor = InputBg
                tb.ForeColor = Fg
                If Not tb.Multiline AndAlso tb.Font.Size < 10.0F AndAlso tb.Font.FontFamily.Name = UiFamily Then tb.Font = Ui(10.0F)
                FrameHook.Attach(tb)
            Case TypeOf c Is ComboBox
                Dim cb = DirectCast(c, ComboBox)
                cb.FlatStyle = FlatStyle.Flat
                cb.BackColor = Raised
                cb.ForeColor = Fg
                cb.Font = Ui(10.0F)
                FrameHook.Attach(cb)
            Case TypeOf c Is NumericUpDown
                Dim n = DirectCast(c, NumericUpDown)
                n.BorderStyle = BorderStyle.FixedSingle
                n.BackColor = InputBg
                n.ForeColor = Fg
            Case TypeOf c Is ListBox
                Dim lb = DirectCast(c, ListBox)
                lb.BorderStyle = BorderStyle.None
                lb.BackColor = InputBg
                lb.ForeColor = Fg
            Case TypeOf c Is CheckBox
                Dim cb = DirectCast(c, CheckBox)
                cb.FlatStyle = FlatStyle.Flat
                cb.FlatAppearance.BorderColor = BorderColor
                cb.FlatAppearance.CheckedBackColor = If(cb.Appearance = Appearance.Button, AccentDim, Raised)
                cb.FlatAppearance.MouseOverBackColor = HoverBg
                cb.ForeColor = Fg
                If cb.Appearance = Appearance.Button Then cb.BackColor = Raised
            Case TypeOf c Is Button
                Dim b = DirectCast(c, Button)
                b.FlatStyle = FlatStyle.Flat
                b.FlatAppearance.BorderColor = BorderColor
                b.FlatAppearance.MouseOverBackColor = SelectedBg
                b.FlatAppearance.MouseDownBackColor = HoverBg
                b.BackColor = Raised
                b.ForeColor = Fg
                b.Cursor = Cursors.Hand
            Case TypeOf c Is LinkLabel
                Dim l = DirectCast(c, LinkLabel)
                l.LinkColor = Accent
                l.ActiveLinkColor = AccentHover
                l.VisitedLinkColor = Accent
            Case TypeOf c Is Label
                If c.ForeColor = SystemColors.GrayText Then c.ForeColor = Muted
            Case TypeOf c Is GroupBox
                c.ForeColor = Muted
        End Select
    End Sub

    ''' <summary>Abschnittsüberschrift in Versalien („PROFILE“, „SEITEN“ …).</summary>
    Public Function SectionLabel(text As String) As Label
        Return New Label With {.Text = text.ToUpperInvariant(), .AutoSize = True, .ForeColor = Muted, .Font = UiSemi(7.5F), .Margin = New Padding(2, 4, 2, 4)}
    End Function

    ''' <summary>Feldbeschriftung über einem Eingabefeld.</summary>
    Public Function FieldLabel(text As String) As Label
        Return New Label With {.Text = text, .AutoSize = True, .ForeColor = Muted, .Font = UiSemi(8.5F), .Padding = New Padding(0, 10, 0, 4)}
    End Function

    ''' <summary>Fügt Steuerelemente mit Dock=Top in der angegebenen Reihenfolge (oben → unten) ein.</summary>
    Public Sub StackTop(container As Control, ParamArray items As Control())
        For i = items.Length - 1 To 0 Step -1
            items(i).Dock = DockStyle.Top
            container.Controls.Add(items(i))
        Next
    End Sub
End Module

' ====================================================================== Rahmen für Eingabefelder

''' <summary>
''' Übermalt den hellen System-Rahmen von TextBox/ComboBox mit einem dunklen (bei Fokus Bernstein)
''' und färbt den Aufklapp-Knopf der ComboBox. Hängt sich als NativeWindow an das Fenster-Handle.
''' </summary>
Public Class FrameHook
    Inherits NativeWindow

    Private Const WM_PAINT As Integer = &HF
    Private Const WM_NCPAINT As Integer = &H85
    Private Const WM_SETFOCUS As Integer = &H7
    Private Const WM_KILLFOCUS As Integer = &H8
    Private Const WM_MOUSEMOVE As Integer = &H200
    Private Const WM_ENABLE As Integer = &HA

    Private ReadOnly _control As Control

    <DllImport("user32.dll")>
    Private Shared Function GetWindowDC(hWnd As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ReleaseDC(hWnd As IntPtr, hDC As IntPtr) As Integer
    End Function

    Private Sub New(c As Control)
        _control = c
    End Sub

    Public Shared Sub Attach(c As Control)
        If c.Tag Is GetType(FrameHook) Then Return
        c.Tag = GetType(FrameHook)
        Dim hook As New FrameHook(c)
        If c.IsHandleCreated Then hook.AssignHandle(c.Handle)
        AddHandler c.HandleCreated, Sub() hook.AssignHandle(c.Handle)
        AddHandler c.HandleDestroyed, Sub() hook.ReleaseHandle()
        AddHandler c.GotFocus, Sub() hook.Draw()
        AddHandler c.LostFocus, Sub() hook.Draw()
    End Sub

    Protected Overrides Sub WndProc(ByRef m As Message)
        MyBase.WndProc(m)
        Select Case m.Msg
            Case WM_PAINT, WM_NCPAINT, WM_SETFOCUS, WM_KILLFOCUS, WM_ENABLE
                Draw()
            Case WM_MOUSEMOVE
                If TypeOf _control Is ComboBox Then Draw()
        End Select
    End Sub

    Private Sub Draw()
        If Handle = IntPtr.Zero Then Return
        Dim hdc = GetWindowDC(Handle)
        If hdc = IntPtr.Zero Then Return
        Try
            Using g = Graphics.FromHdc(hdc)
                Dim w = _control.Width, h = _control.Height
                Dim focused = _control.ContainsFocus
                Dim edge = If(focused, Theme.Accent, Theme.BorderColor)
                If TypeOf _control Is ComboBox Then
                    Dim cb = DirectCast(_control, ComboBox)
                    Dim bw = SystemInformation.VerticalScrollBarWidth + 2
                    Dim btn As New Rectangle(w - bw - 1, 1, bw, h - 2)
                    Using b As New SolidBrush(cb.BackColor)
                        g.FillRectangle(b, btn)
                    End Using
                    TextRenderer.DrawText(g, ChrW(&HE70D), Theme.Icons(7.5F), btn, If(cb.Enabled, Theme.Muted, Theme.Faint),
                                          TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                    ' innerer heller Rand des Flat-Stils
                    Using inner As New Pen(cb.BackColor)
                        g.DrawRectangle(inner, 1, 1, w - 3, h - 3)
                    End Using
                ElseIf TypeOf _control Is TextBox Then
                    Using inner As New Pen(_control.BackColor)
                        g.DrawRectangle(inner, 1, 1, w - 3, h - 3)
                    End Using
                End If
                Using p As New Pen(edge)
                    g.DrawRectangle(p, 0, 0, w - 1, h - 1)
                End Using
            End Using
        Finally
            ReleaseDC(Handle, hdc)
        End Try
    End Sub
End Class

' ====================================================================== Fenster-Basis

''' <summary>Formular mit dunklem Hintergrund, dunkler Titelleiste und Theme-Schrift.</summary>
Public Class ThemedForm
    Inherits Form

    Public Sub New()
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScaleMode = AutoScaleMode.Dpi
        BackColor = Theme.Surface
        ForeColor = Theme.Fg
        Font = Theme.Ui(9.5F)
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

    ''' <summary>Fußleiste mit Trennlinie oben; Knöpfe rechtsbündig in der angegebenen Reihenfolge.</summary>
    Protected Function MakeFooter(leftControl As Control, ParamArray right As Control()) As Panel
        Dim footer As New BorderPanel With {.Dock = DockStyle.Bottom, .BackColor = Theme.SidebarBg, .Padding = New Padding(20, 14, 20, 14), .Height = 66, .Borders = AnchorStyles.Top}
        Dim flow As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.RightToLeft, .WrapContents = False}
        For i = right.Length - 1 To 0 Step -1
            flow.Controls.Add(right(i))
        Next
        footer.Controls.Add(flow)
        If leftControl IsNot Nothing Then
            leftControl.Dock = DockStyle.Left
            footer.Controls.Add(leftControl)
        End If
        Return footer
    End Function
End Class

' ====================================================================== Panels

''' <summary>Panel mit einzelnen Rahmenlinien.</summary>
Public Class BorderPanel
    Inherits Panel

    Public Property Borders As AnchorStyles = AnchorStyles.None
    Public Property LineColor As Color = Theme.LineColor

    Public Sub New()
        SetStyle(ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Using p As New Pen(LineColor)
            If Borders.HasFlag(AnchorStyles.Top) Then e.Graphics.DrawLine(p, 0, 0, Width, 0)
            If Borders.HasFlag(AnchorStyles.Bottom) Then e.Graphics.DrawLine(p, 0, Height - 1, Width, Height - 1)
            If Borders.HasFlag(AnchorStyles.Left) Then e.Graphics.DrawLine(p, 0, 0, 0, Height)
            If Borders.HasFlag(AnchorStyles.Right) Then e.Graphics.DrawLine(p, Width - 1, 0, Width - 1, Height)
        End Using
    End Sub
End Class

''' <summary>Abgerundete Karte (z. B. Protokoll, Gerätestatus, Geräteplatte).</summary>
Public Class CardPanel
    Inherits Panel

    Public Property Radius As Single = 12
    Public Property Fill As Color = Theme.SidebarBg
    Public Property Edge As Color = Theme.LineColor

    Public Sub New()
        SetStyle(ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint, True)
    End Sub

    Protected Overrides Sub OnPaintBackground(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(If(Parent?.BackColor, Theme.Bg))
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim r As New RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F)
        Dim rad = Theme.Px(Me, Radius)
        Theme.FillRound(g, Fill, r, rad)
        Theme.DrawRound(g, Edge, r, rad)
    End Sub

    ''' <summary>Kinder sehen die Füllfarbe als Hintergrund.</summary>
    Public Overrides Property BackColor As Color
        Get
            Return Fill
        End Get
        Set(value As Color)
            Fill = value
        End Set
    End Property
End Class

' ====================================================================== Knopf

Public Enum ButtonKind
    Secondary
    Primary
    Ghost
    Danger
    IconOnly
End Enum

''' <summary>Selbst gezeichneter Knopf mit abgerundeten Ecken (Primär = Bernstein).</summary>
Public Class ThemedButton
    Inherits Button

    Private _hover As Boolean
    Private _down As Boolean
    Private _kind As ButtonKind
    Private _glyph As String

    Public Property BaseHeight As Integer = 36
    ''' <summary>Text linksbündig (z. B. Einträge unten in der Seitenleiste).</summary>
    Public Property AlignLeft As Boolean

    Public Sub New(text As String, Optional kind As ButtonKind = ButtonKind.Secondary, Optional glyph As String = Nothing)
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        FlatStyle = FlatStyle.Flat
        FlatAppearance.BorderSize = 0
        Me.Text = text
        _kind = kind
        _glyph = glyph
        Cursor = Cursors.Hand
        Font = If(kind = ButtonKind.Primary, Theme.UiSemi(9.5F), Theme.Ui(9.5F))
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink
        Margin = New Padding(4, 0, 4, 0)
        If kind = ButtonKind.IconOnly Then AccessibleName = text
    End Sub

    Public Property Kind As ButtonKind
        Get
            Return _kind
        End Get
        Set(value As ButtonKind)
            _kind = value
            Invalidate()
        End Set
    End Property

    Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
        Dim h = Theme.PxI(Me, BaseHeight)
        If _kind = ButtonKind.IconOnly Then Return New Size(h, h)
        Dim w = TextRenderer.MeasureText(Text, Font).Width + Theme.PxI(Me, 28)
        If _glyph IsNot Nothing Then w += Theme.PxI(Me, 22)
        Return New Size(w, h)
    End Function

    Protected Overrides Sub OnMouseEnter(e As EventArgs)
        _hover = True
        Invalidate()
        MyBase.OnMouseEnter(e)
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        _hover = False
        _down = False
        Invalidate()
        MyBase.OnMouseLeave(e)
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        If e.Button = MouseButtons.Left Then
            _down = True
            Invalidate()
        End If
        MyBase.OnMouseDown(e)
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        _down = False
        Invalidate()
        MyBase.OnMouseUp(e)
    End Sub

    Protected Overrides Sub OnEnabledChanged(e As EventArgs)
        Invalidate()
        MyBase.OnEnabledChanged(e)
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(If(Parent?.BackColor, Theme.Bg))
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim r As New RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F)
        Dim rad = Theme.Px(Me, 8)

        Dim fill As Color = Color.Empty, edge As Color = Color.Empty, fore As Color
        Select Case _kind
            Case ButtonKind.Primary
                fill = If(_hover AndAlso Not _down, Theme.AccentHover, Theme.Accent)
                fore = Theme.AccentText
            Case ButtonKind.Secondary
                fill = If(_hover, Theme.SelectedBg, Theme.Raised)
                edge = Theme.BorderColor
                fore = Theme.Fg
            Case ButtonKind.Danger
                If _hover Then fill = Theme.BadBg
                fore = Theme.BadText
            Case Else
                If _hover Then fill = Theme.SelectedBg
                fore = If(_hover, Theme.Fg, If(_kind = ButtonKind.IconOnly, Theme.Muted, Theme.FgSoft))
        End Select
        If Not Enabled Then
            fore = Theme.Faint
            If _kind = ButtonKind.Primary Then fill = Theme.AccentDim
        End If
        If Not fill.IsEmpty Then Theme.FillRound(g, fill, r, rad)
        If Not edge.IsEmpty Then Theme.DrawRound(g, edge, r, rad)
        If Focused AndAlso ShowFocusCues Then
            Theme.DrawRound(g, If(_kind = ButtonKind.Primary, Theme.Fg, Theme.Accent), RectangleF.Inflate(r, -1, -1), rad, Theme.Px(Me, 1.5F))
        End If

        Dim flags = TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine
        If _kind = ButtonKind.IconOnly Then
            TextRenderer.DrawText(g, If(_glyph, Text), Theme.Icons(10.0F), ClientRectangle, fore, flags Or TextFormatFlags.HorizontalCenter)
            Return
        End If
        Dim textSize = TextRenderer.MeasureText(Text, Font)
        Dim glyphW = If(_glyph Is Nothing, 0, Theme.PxI(Me, 22))
        Dim x = If(AlignLeft, Theme.PxI(Me, 10), (Width - textSize.Width - glyphW) \ 2)
        If _glyph IsNot Nothing Then
            TextRenderer.DrawText(g, _glyph, Theme.Icons(9.0F), New Rectangle(x, 0, glyphW, Height), fore, flags Or TextFormatFlags.Left)
        End If
        TextRenderer.DrawText(g, Text, Font, New Rectangle(x + glyphW, 0, textSize.Width + 2, Height), fore, flags Or TextFormatFlags.Left)
    End Sub
End Class

' ====================================================================== Navigationseintrag

''' <summary>Eintrag in der Seitenleiste: Punkt, Titel, Unterzeile, Zähler rechts.</summary>
Public Class NavItem
    Inherits Control

    Private _hover As Boolean
    Private _selected As Boolean
    Private _subtitle As String
    Private _badge As String

    Public Property ShowDot As Boolean

    Public Sub New(title As String)
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
        Text = title
        TabStop = True
        Cursor = Cursors.Hand
        AccessibleRole = AccessibleRole.ListItem
        AccessibleName = title
        Height = 40
    End Sub

    Public Property Selected As Boolean
        Get
            Return _selected
        End Get
        Set(value As Boolean)
            _selected = value
            Invalidate()
        End Set
    End Property

    Public Property Subtitle As String
        Get
            Return _subtitle
        End Get
        Set(value As String)
            _subtitle = value
            Height = Theme.PxI(Me, If(String.IsNullOrEmpty(value), 40, 50))
            Invalidate()
        End Set
    End Property

    Public Property Badge As String
        Get
            Return _badge
        End Get
        Set(value As String)
            _badge = value
            Invalidate()
        End Set
    End Property

    Protected Overrides Sub OnMouseEnter(e As EventArgs)
        _hover = True
        Invalidate()
        MyBase.OnMouseEnter(e)
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        _hover = False
        Invalidate()
        MyBase.OnMouseLeave(e)
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        If e.KeyCode = Keys.Enter OrElse e.KeyCode = Keys.Space Then
            OnClick(EventArgs.Empty)
            e.Handled = True
        End If
        MyBase.OnKeyDown(e)
    End Sub

    Protected Overrides Sub OnGotFocus(e As EventArgs)
        Invalidate()
        MyBase.OnGotFocus(e)
    End Sub

    Protected Overrides Sub OnLostFocus(e As EventArgs)
        Invalidate()
        MyBase.OnLostFocus(e)
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(If(Parent?.BackColor, Theme.SidebarBg))
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim r As New RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F)
        Dim rad = Theme.Px(Me, 9)
        If _selected Then
            Theme.FillRound(g, Theme.SelectedBg, r, rad)
        ElseIf _hover Then
            Theme.FillRound(g, Theme.HoverBg, r, rad)
        End If
        If Focused AndAlso ShowFocusCues Then Theme.DrawRound(g, Theme.Accent, RectangleF.Inflate(r, -1, -1), rad, Theme.Px(Me, 1.5F))

        Dim pad = Theme.PxI(Me, 12)
        Dim x = pad
        If ShowDot Then
            Dim d = Theme.Px(Me, 8)
            Theme.Dot(g, If(_selected, Theme.Accent, Theme.DashColor), x + d / 2, Height / 2.0F, d)
            x += Theme.PxI(Me, 18)
        End If
        Dim badgeW = 0
        If Not String.IsNullOrEmpty(_badge) Then
            Dim bf = Theme.Mono(8.0F)
            badgeW = TextRenderer.MeasureText(_badge, bf).Width
            TextRenderer.DrawText(g, _badge, bf, New Rectangle(Width - pad - badgeW, 0, badgeW, Height), Theme.Muted, TextFormatFlags.VerticalCenter Or TextFormatFlags.Right)
        End If
        Dim textW = Width - x - pad - badgeW - Theme.PxI(Me, 6)
        Dim titleFont = If(_selected, Theme.UiSemi(9.5F), Theme.Ui(9.5F))
        Dim flags = TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine
        If String.IsNullOrEmpty(_subtitle) Then
            TextRenderer.DrawText(g, Text, titleFont, New Rectangle(x, 0, textW, Height), Theme.Fg, flags Or TextFormatFlags.VerticalCenter)
        Else
            Dim half = Height \ 2
            TextRenderer.DrawText(g, Text, titleFont, New Rectangle(x, 0, textW, half + Theme.PxI(Me, 2)), Theme.Fg, flags Or TextFormatFlags.Bottom)
            TextRenderer.DrawText(g, _subtitle, Theme.Ui(8.0F), New Rectangle(x, half + Theme.PxI(Me, 2), textW, half - 2), Theme.Muted, flags Or TextFormatFlags.Top)
        End If
    End Sub
End Class

' ====================================================================== Segmentschalter

''' <summary>Reiter in einer Kapsel (z. B. „Kurzer Druck | Langer Druck“).</summary>
Public Class Segmented
    Inherits Control

    Private ReadOnly _items As String()
    Private _selected As Integer
    Private _hover As Integer = -1

    Public Event SelectedIndexChanged As EventHandler

    Public Sub New(ParamArray items As String())
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
        _items = items
        TabStop = True
        Cursor = Cursors.Hand
        AccessibleRole = AccessibleRole.PageTabList
        Height = 44
    End Sub

    Public Property SelectedIndex As Integer
        Get
            Return _selected
        End Get
        Set(value As Integer)
            If value = _selected OrElse value < 0 OrElse value >= _items.Length Then Return
            _selected = value
            Invalidate()
            RaiseEvent SelectedIndexChanged(Me, EventArgs.Empty)
        End Set
    End Property

    Private Function Cell(i As Integer) As RectangleF
        Dim pad = Theme.Px(Me, 4)
        Dim w = (Width - 2 * pad - (_items.Length - 1) * pad) / _items.Length
        Return New RectangleF(pad + i * (w + pad), pad, w, Height - 2 * pad)
    End Function

    Private Function HitTest(p As Point) As Integer
        For i = 0 To _items.Length - 1
            If Cell(i).Contains(p) Then Return i
        Next
        Return -1
    End Function

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        Dim h = HitTest(e.Location)
        If h <> _hover Then
            _hover = h
            Invalidate()
        End If
        MyBase.OnMouseMove(e)
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        _hover = -1
        Invalidate()
        MyBase.OnMouseLeave(e)
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        Focus()
        Dim h = HitTest(e.Location)
        If h >= 0 Then SelectedIndex = h
        MyBase.OnMouseDown(e)
    End Sub

    Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
        If keyData = Keys.Left OrElse keyData = Keys.Right Then Return True
        Return MyBase.IsInputKey(keyData)
    End Function

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        If e.KeyCode = Keys.Left Then SelectedIndex = Math.Max(0, _selected - 1)
        If e.KeyCode = Keys.Right Then SelectedIndex = Math.Min(_items.Length - 1, _selected + 1)
        MyBase.OnKeyDown(e)
    End Sub

    Protected Overrides Sub OnGotFocus(e As EventArgs)
        Invalidate()
        MyBase.OnGotFocus(e)
    End Sub

    Protected Overrides Sub OnLostFocus(e As EventArgs)
        Invalidate()
        MyBase.OnLostFocus(e)
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(If(Parent?.BackColor, Theme.Surface))
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim outer As New RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F)
        Theme.FillRound(g, Theme.InputBg, outer, Theme.Px(Me, 10))
        Theme.DrawRound(g, If(Focused AndAlso ShowFocusCues, Theme.Accent, Theme.LineColor), outer, Theme.Px(Me, 10))
        For i = 0 To _items.Length - 1
            Dim c = Cell(i)
            If i = _selected Then
                Theme.FillRound(g, Theme.KeyCapHover, c, Theme.Px(Me, 7))
            ElseIf i = _hover Then
                Theme.FillRound(g, Theme.HoverBg, c, Theme.Px(Me, 7))
            End If
            TextRenderer.DrawText(g, _items(i), If(i = _selected, Theme.UiSemi(9.0F), Theme.Ui(9.0F)), Rectangle.Round(c),
                                  If(i = _selected, Theme.Fg, Theme.Muted), TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix)
        Next
    End Sub
End Class

' ====================================================================== Menüs

''' <summary>Dunkle Kontextmenüs (global über ToolStripManager.Renderer).</summary>
Public Class DarkMenuRenderer
    Inherits ToolStripProfessionalRenderer

    Public Sub New()
        MyBase.New(New DarkColors())
        RoundedEdges = False
    End Sub

    Protected Overrides Sub OnRenderItemText(e As ToolStripItemTextRenderEventArgs)
        e.TextColor = If(e.Item.Enabled, Theme.Fg, Theme.Faint)
        MyBase.OnRenderItemText(e)
    End Sub

    Protected Overrides Sub OnRenderArrow(e As ToolStripArrowRenderEventArgs)
        e.ArrowColor = Theme.Muted
        MyBase.OnRenderArrow(e)
    End Sub

    Protected Overrides Sub OnRenderItemCheck(e As ToolStripItemImageRenderEventArgs)
        TextRenderer.DrawText(e.Graphics, ChrW(&HE73E), Theme.Icons(9.0F), e.ImageRectangle, Theme.Accent, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
    End Sub

    Private Class DarkColors
        Inherits ProfessionalColorTable

        Public Overrides ReadOnly Property ToolStripDropDownBackground As Color
            Get
                Return Theme.Surface
            End Get
        End Property
        Public Overrides ReadOnly Property ImageMarginGradientBegin As Color
            Get
                Return Theme.Surface
            End Get
        End Property
        Public Overrides ReadOnly Property ImageMarginGradientMiddle As Color
            Get
                Return Theme.Surface
            End Get
        End Property
        Public Overrides ReadOnly Property ImageMarginGradientEnd As Color
            Get
                Return Theme.Surface
            End Get
        End Property
        Public Overrides ReadOnly Property MenuBorder As Color
            Get
                Return Theme.BorderColor
            End Get
        End Property
        Public Overrides ReadOnly Property MenuItemBorder As Color
            Get
                Return Theme.SelectedBg
            End Get
        End Property
        Public Overrides ReadOnly Property MenuItemSelected As Color
            Get
                Return Theme.SelectedBg
            End Get
        End Property
        Public Overrides ReadOnly Property MenuItemSelectedGradientBegin As Color
            Get
                Return Theme.SelectedBg
            End Get
        End Property
        Public Overrides ReadOnly Property MenuItemSelectedGradientEnd As Color
            Get
                Return Theme.SelectedBg
            End Get
        End Property
        Public Overrides ReadOnly Property MenuItemPressedGradientBegin As Color
            Get
                Return Theme.SelectedBg
            End Get
        End Property
        Public Overrides ReadOnly Property MenuItemPressedGradientEnd As Color
            Get
                Return Theme.SelectedBg
            End Get
        End Property
        Public Overrides ReadOnly Property SeparatorDark As Color
            Get
                Return Theme.LineColor
            End Get
        End Property
        Public Overrides ReadOnly Property SeparatorLight As Color
            Get
                Return Theme.Surface
            End Get
        End Property
        Public Overrides ReadOnly Property CheckBackground As Color
            Get
                Return Theme.Surface
            End Get
        End Property
        Public Overrides ReadOnly Property CheckSelectedBackground As Color
            Get
                Return Theme.SelectedBg
            End Get
        End Property
        Public Overrides ReadOnly Property CheckPressedBackground As Color
            Get
                Return Theme.SelectedBg
            End Get
        End Property
    End Class
End Class
