' Mini-Ansicht: kleines, randloses Fenster mit dem Tastenraster der aktuellen Seite – immer im Vordergrund,
' teiltransparent und nur zur Anzeige (Klicks lösen keine Aktionen aus). Am Gerät gedrückte Tasten leuchten auf.
' Bedienung direkt am Fenster: ziehen = verschieben (rastet an den Bildschirmrändern ein), Rand ziehen = Größe,
' Mausrad = Deckkraft, Rechtsklick = Menü. Die Werte stehen in DeckConfig.MiniView.
Imports System.Drawing.Drawing2D
Imports System.Runtime.InteropServices
Imports StreamDeck.Core

Public Class MiniDeckForm
    Inherits Form

    Private Const Cols As Integer = 5
    Private Const Rows As Integer = 3
    Private Const PadRatio As Single = 0.16F   ' Rand der Platte, relativ zur Tastengröße
    Private Const GapRatio As Single = 0.1F    ' Abstand zwischen den Tasten
    Private Const SnapPx As Integer = 16       ' Einrasten an Bildschirmrändern (bei 100 %)
    Private Const EdgePx As Integer = 6        ' Greifbereich am Rand für die Größe (bei 100 %)

    Public Shared ReadOnly OpacitySteps As Integer() = {100, 85, 70, 55, 40, 25}
    Public Shared ReadOnly SizeSteps As (Name As String, Size As Integer)() = {("Klein", 32), ("Mittel", 48), ("Groß", 72), ("Sehr groß", 100)}

    Private ReadOnly _tray As TrayContext
    Private ReadOnly _engine As DeckEngine
    Private ReadOnly _pressed(ConfigStore.KeyCount - 1) As Boolean
    Private ReadOnly _topmostTimer As New System.Windows.Forms.Timer With {.Interval = 2000}
    Private ReadOnly _saveTimer As New System.Windows.Forms.Timer With {.Interval = 600}
    Private _menu As ContextMenuStrip
    Private _sizeAtStart As Size

    ''' <summary>Position/Größe/Deckkraft geändert – der Tray speichert die Konfiguration.</summary>
    Public Event SettingsChanged()
    ''' <summary>Über das eigene Menü geschlossen.</summary>
    Public Event CloseRequested()

    Public Sub New(tray As TrayContext)
        _tray = tray
        _engine = tray.Engine
        Text = "DIY Stream Deck – Mini-Ansicht"
        FormBorderStyle = FormBorderStyle.None
        ShowInTaskbar = False
        StartPosition = FormStartPosition.Manual
        MaximizeBox = False
        MinimizeBox = False
        TopMost = True
        AllowTransparency = True   ' immer „layered“, damit Durchklicken auch bei 100 % Deckkraft geht
        BackColor = Theme.PlateColor
        DoubleBuffered = True
        ApplyOpacity()
        Bounds = InitialBounds()

        AddHandler _topmostTimer.Tick, Sub() KeepOnTop()
        AddHandler _saveTimer.Tick, Sub()
                                        _saveTimer.Stop()
                                        RaiseEvent SettingsChanged()
                                    End Sub
        AddHandler _engine.StateChanged, AddressOf Engine_Changed
        AddHandler _engine.ConfigChanged, AddressOf Engine_Changed
        AddHandler _engine.KeyStateChanged, AddressOf Engine_KeyStateChanged
        AddHandler _tray.ConnectionChanged, AddressOf Tray_ConnectionChanged
        _topmostTimer.Start()
    End Sub

    ''' <summary>
    ''' Immer frisch aus der Konfiguration lesen: DeckEngine.UpdateConfig ersetzt sie durch eine Kopie.
    ''' Die Werte ändert nur dieses Fenster (UI-Thread); gespeichert wird über SettingsChanged.
    ''' </summary>
    Private ReadOnly Property Settings As MiniViewSettings
        Get
            Return _engine.Config.MiniView
        End Get
    End Property

    ' ------------------------------------------------------------------ Fensterstil

    Protected Overrides ReadOnly Property ShowWithoutActivation As Boolean
        Get
            Return True
        End Get
    End Property

    Private Const WS_EX_TOPMOST As Integer = &H8
    Private Const WS_EX_TRANSPARENT As Integer = &H20
    Private Const WS_EX_TOOLWINDOW As Integer = &H80
    Private Const WS_EX_LAYERED As Integer = &H80000
    Private Const WS_EX_NOACTIVATE As Integer = &H8000000

    Protected Overrides ReadOnly Property CreateParams As CreateParams
        Get
            Dim cp = MyBase.CreateParams
            cp.ExStyle = cp.ExStyle Or WS_EX_TOPMOST Or WS_EX_TOOLWINDOW Or WS_EX_NOACTIVATE Or WS_EX_LAYERED
            ' Wird schon im Basiskonstruktor abgefragt – da ist _engine noch leer.
            If _engine IsNot Nothing AndAlso Settings.ClickThrough Then cp.ExStyle = cp.ExStyle Or WS_EX_TRANSPARENT
            Return cp
        End Get
    End Property

    <DllImport("user32.dll")>
    Private Shared Function SetWindowPos(hWnd As IntPtr, after As IntPtr, x As Integer, y As Integer, cx As Integer, cy As Integer, flags As UInteger) As Boolean
    End Function

    ''' <summary>Über allen Fenstern halten, ohne den Fokus zu nehmen (sonst drängen sich andere Topmost-Fenster davor).</summary>
    Private Sub KeepOnTop()
        If IsHandleCreated AndAlso Visible Then SetWindowPos(Handle, New IntPtr(-1), 0, 0, 0, 0, &H1UI Or &H2UI Or &H10UI)
    End Sub

    ' ------------------------------------------------------------------ Einstellungen

    Public ReadOnly Property ClickThrough As Boolean
        Get
            Return Settings.ClickThrough
        End Get
    End Property

    ''' <summary>Durchklickbar: Mausklicks gehen durch das Fenster hindurch (lösen über das Tray-Menü).</summary>
    Public Sub SetClickThrough(enabled As Boolean)
        If Settings.ClickThrough = enabled Then Return
        Settings.ClickThrough = enabled
        If IsHandleCreated Then UpdateStyles()
        Invalidate()
        SaveSoon()
    End Sub

    Public Sub SetOpacityPercent(percent As Integer)
        Settings.Opacity = Math.Max(MiniViewSettings.MinOpacity, Math.Min(100, percent))
        ApplyOpacity()
        SaveSoon()
    End Sub

    ''' <summary>
    ''' Bei genau 1.0 setzt WinForms keine Layered-Attribute mehr – das immer „layered“ Fenster wäre dann unsichtbar.
    ''' 99,6 % sieht genauso aus.
    ''' </summary>
    Private Sub ApplyOpacity()
        Opacity = Math.Min(0.996, Settings.Opacity / 100.0)
    End Sub

    Public Sub SetKeySize(logical As Integer)
        Settings.KeySize = Math.Max(MiniViewSettings.MinKeySize, Math.Min(MiniViewSettings.MaxKeySize, logical))
        Bounds = KeepOnScreen(New Rectangle(Location, PixelSize(Dpx(Settings.KeySize))))
        RememberLocation()
    End Sub

    ''' <summary>In eine Ecke des Bildschirms legen, auf dem das Fenster gerade ist.</summary>
    Public Sub MoveToCorner(right As Boolean, bottom As Boolean)
        Dim area = Screen.FromRectangle(Bounds).WorkingArea
        Dim m = Dpx(8)
        Location = New Point(If(right, area.Right - Width - m, area.Left + m), If(bottom, area.Bottom - Height - m, area.Top + m))
        RememberLocation()
    End Sub

    Private Sub RememberLocation()
        Settings.X = Left
        Settings.Y = Top
        SaveSoon()
    End Sub

    Private Sub SaveSoon()
        _saveTimer.Stop()
        _saveTimer.Start()
    End Sub

    ' ------------------------------------------------------------------ Maße

    Private Function Dpx(value As Integer) As Integer
        Return CInt(Math.Round(value * DeviceDpi / 96.0))
    End Function

    Private Shared Function PadOf(k As Integer) As Integer
        Return CInt(Math.Round(k * PadRatio))
    End Function

    Private Shared Function GapOf(k As Integer) As Integer
        Return Math.Max(2, CInt(Math.Round(k * GapRatio)))
    End Function

    ''' <summary>Fenstergröße für eine Tastengröße in Gerätepixeln.</summary>
    Public Shared Function PixelSize(k As Integer) As Size
        Dim pad = PadOf(k)
        Dim gap = GapOf(k)
        Return New Size(2 * pad + Cols * k + (Cols - 1) * gap, 2 * pad + Rows * k + (Rows - 1) * gap)
    End Function

    ''' <summary>Größte Tastengröße (Gerätepixel), deren Fenster in die Breite bzw. Höhe passt.</summary>
    Public Shared Function KeyFor(extent As Integer, byHeight As Boolean) As Integer
        Dim fit = Function(k As Integer) If(byHeight, PixelSize(k).Height, PixelSize(k).Width) <= extent
        Dim n = If(byHeight, Rows, Cols)
        Dim k0 = Math.Max(1, CInt(Math.Floor(extent / (n + 2 * PadRatio + (n - 1) * GapRatio))))
        While fit(k0 + 1)
            k0 += 1
        End While
        While k0 > 1 AndAlso Not fit(k0)
            k0 -= 1
        End While
        Return k0
    End Function

    Private Function InitialBounds() As Rectangle
        Dim s = PixelSize(Dpx(Settings.KeySize))
        Dim area = Screen.PrimaryScreen.WorkingArea
        Dim loc = If(Settings.X.HasValue AndAlso Settings.Y.HasValue,
                     New Point(Settings.X.Value, Settings.Y.Value),
                     New Point(area.Left + Dpx(8), area.Top + Dpx(8)))
        Return KeepOnScreen(New Rectangle(loc, s))
    End Function

    ''' <summary>Liegt das Fenster (z. B. nach Abstecken eines Monitors) nicht mehr sichtbar, kommt es nach oben links.</summary>
    Private Function KeepOnScreen(r As Rectangle) As Rectangle
        For Each sc In Screen.AllScreens
            Dim i = Rectangle.Intersect(sc.WorkingArea, r)
            If i.Width >= Math.Min(r.Width, 40) AndAlso i.Height >= Math.Min(r.Height, 30) Then Return r
        Next
        Dim area = Screen.PrimaryScreen.WorkingArea
        Return New Rectangle(area.Left + Dpx(8), area.Top + Dpx(8), r.Width, r.Height)
    End Function

    Protected Overrides Sub OnDpiChanged(e As DpiChangedEventArgs)
        MyBase.OnDpiChanged(e)
        Size = PixelSize(Dpx(Settings.KeySize))
    End Sub

    Protected Overrides Sub OnSizeChanged(e As EventArgs)
        MyBase.OnSizeChanged(e)
        If Width > 0 AndAlso Height > 0 Then
            Dim radius = Math.Max(4.0F, KeyFor(Width, False) * 0.22F)
            Using p = TrayContext.RoundedRect(New RectangleF(0, 0, Width, Height), radius)
                Region = New Region(p)
            End Using
        End If
        Invalidate()
    End Sub

    ' ------------------------------------------------------------------ Maus: verschieben, Größe, Menü

    Private Const WM_MOUSEACTIVATE As Integer = &H21
    Private Const WM_NCHITTEST As Integer = &H84
    Private Const WM_NCLBUTTONDBLCLK As Integer = &HA3
    Private Const WM_NCRBUTTONUP As Integer = &HA5
    Private Const WM_MOUSEWHEEL As Integer = &H20A
    Private Const WM_SIZING As Integer = &H214
    Private Const WM_MOVING As Integer = &H216
    Private Const WM_ENTERSIZEMOVE As Integer = &H231
    Private Const WM_EXITSIZEMOVE As Integer = &H232

    <StructLayout(LayoutKind.Sequential)>
    Private Structure RECT
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    Protected Overrides Sub WndProc(ByRef m As Message)
        Select Case m.Msg
            Case WM_MOUSEACTIVATE
                m.Result = New IntPtr(3)   ' MA_NOACTIVATE: ein Klick nimmt dem aktiven Programm nicht den Fokus
                Return
            Case WM_NCHITTEST
                m.Result = New IntPtr(HitTest(PointToClient(PointFromLParam(m.LParam))))
                Return
            Case WM_NCLBUTTONDBLCLK
                Return   ' kein Maximieren per Doppelklick
            Case WM_NCRBUTTONUP
                ShowMenu(Cursor.Position)
                Return
            Case WM_MOUSEWHEEL
                Dim delta = CShort((m.WParam.ToInt64() >> 16) And &HFFFF)
                SetOpacityPercent(Settings.Opacity + If(delta > 0, 5, -5))
                Return
            Case WM_SIZING
                Dim r = Marshal.PtrToStructure(Of RECT)(m.LParam)
                KeepAspect(m.WParam.ToInt32(), r)
                Marshal.StructureToPtr(r, m.LParam, False)
                m.Result = New IntPtr(1)
                Return
            Case WM_MOVING
                Dim r = Marshal.PtrToStructure(Of RECT)(m.LParam)
                Snap(r)
                Marshal.StructureToPtr(r, m.LParam, False)
                m.Result = New IntPtr(1)
                Return
            Case WM_ENTERSIZEMOVE
                _sizeAtStart = Size
            Case WM_EXITSIZEMOVE
                If Size <> _sizeAtStart Then
                    Settings.KeySize = Math.Max(MiniViewSettings.MinKeySize, Math.Min(MiniViewSettings.MaxKeySize,
                                                CInt(Math.Round(KeyFor(Width, False) * 96.0 / DeviceDpi))))
                End If
                RememberLocation()
        End Select
        MyBase.WndProc(m)
    End Sub

    Private Shared Function PointFromLParam(lParam As IntPtr) As Point
        Dim v = lParam.ToInt64()
        Return New Point(CShort(v And &HFFFF), CShort((v >> 16) And &HFFFF))
    End Function

    ''' <summary>Rand = Größe ändern, sonst überall „Titelleiste“ = verschieben.</summary>
    Private Function HitTest(p As Point) As Integer
        Dim e = Dpx(EdgePx)
        Dim l = p.X < e
        Dim r = p.X >= Width - e
        Dim t = p.Y < e
        Dim b = p.Y >= Height - e
        If t AndAlso l Then Return 13   ' HTTOPLEFT
        If t AndAlso r Then Return 14   ' HTTOPRIGHT
        If b AndAlso l Then Return 16   ' HTBOTTOMLEFT
        If b AndAlso r Then Return 17   ' HTBOTTOMRIGHT
        If l Then Return 10             ' HTLEFT
        If r Then Return 11             ' HTRIGHT
        If t Then Return 12             ' HTTOP
        If b Then Return 15             ' HTBOTTOM
        Return 2                        ' HTCAPTION
    End Function

    ''' <summary>Beim Ziehen am Rand das Seitenverhältnis halten; die gegenüberliegende Seite bleibt stehen.</summary>
    Private Sub KeepAspect(edge As Integer, ByRef r As RECT)
        Dim byHeight = edge = 3 OrElse edge = 6   ' WMSZ_TOP / WMSZ_BOTTOM
        Dim k = KeyFor(If(byHeight, r.Bottom - r.Top, r.Right - r.Left), byHeight)
        k = Math.Max(Dpx(MiniViewSettings.MinKeySize), Math.Min(Dpx(MiniViewSettings.MaxKeySize), k))
        Dim s = PixelSize(k)
        Dim fromLeft = edge = 1 OrElse edge = 4 OrElse edge = 7
        Dim fromTop = edge = 3 OrElse edge = 4 OrElse edge = 5
        If fromLeft Then r.Left = r.Right - s.Width Else r.Right = r.Left + s.Width
        If fromTop Then r.Top = r.Bottom - s.Height Else r.Bottom = r.Top + s.Height
    End Sub

    ''' <summary>Beim Verschieben an den Rändern des Arbeitsbereichs einrasten.</summary>
    Private Sub Snap(ByRef r As RECT)
        Dim w = r.Right - r.Left
        Dim h = r.Bottom - r.Top
        Dim area = Screen.FromRectangle(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom)).WorkingArea
        Dim d = Dpx(SnapPx)
        If Math.Abs(r.Left - area.Left) < d Then r.Left = area.Left
        If Math.Abs(area.Right - r.Right) < d Then r.Left = area.Right - w
        If Math.Abs(r.Top - area.Top) < d Then r.Top = area.Top
        If Math.Abs(area.Bottom - r.Bottom) < d Then r.Top = area.Bottom - h
        r.Right = r.Left + w
        r.Bottom = r.Top + h
    End Sub

    <DllImport("user32.dll")>
    Private Shared Function SetForegroundWindow(hWnd As IntPtr) As Boolean
    End Function

    Private Sub ShowMenu(at As Point)
        _menu?.Dispose()
        _menu = BuildMenu()
        ' Ohne Vordergrund schließt das Menü weder mit Esc noch beim Klick daneben (das Fenster wird sonst nie aktiv).
        SetForegroundWindow(Handle)
        _menu.Show(at)
    End Sub

    Private Function BuildMenu() As ContextMenuStrip
        Dim s = Settings
        Dim m As New ContextMenuStrip With {.ShowItemToolTips = True}
        m.Items.Add(New ToolStripMenuItem(_engine.Summary()) With {.Enabled = False})
        m.Items.Add(New ToolStripSeparator())

        Dim opacityMenu As New ToolStripMenuItem("Deckkraft")
        For Each p In OpacitySteps
            Dim v = p
            opacityMenu.DropDownItems.Add(New ToolStripMenuItem($"{v} %", Nothing, Sub() SetOpacityPercent(v)) With {.Checked = s.Opacity = v})
        Next
        opacityMenu.DropDownItems.Add(New ToolStripSeparator())
        opacityMenu.DropDownItems.Add(New ToolStripMenuItem("Feiner: Mausrad über dem Fenster") With {.Enabled = False})
        m.Items.Add(opacityMenu)

        Dim sizeMenu As New ToolStripMenuItem("Größe")
        For Each st In SizeSteps
            Dim v = st.Size
            sizeMenu.DropDownItems.Add(New ToolStripMenuItem(st.Name, Nothing, Sub() SetKeySize(v)) With {.Checked = s.KeySize = v})
        Next
        sizeMenu.DropDownItems.Add(New ToolStripSeparator())
        sizeMenu.DropDownItems.Add(New ToolStripMenuItem("Stufenlos: am Rand ziehen") With {.Enabled = False})
        m.Items.Add(sizeMenu)

        Dim corner As New ToolStripMenuItem("In die Ecke legen")
        corner.DropDownItems.Add("Oben links", Nothing, Sub() MoveToCorner(False, False))
        corner.DropDownItems.Add("Oben rechts", Nothing, Sub() MoveToCorner(True, False))
        corner.DropDownItems.Add("Unten links", Nothing, Sub() MoveToCorner(False, True))
        corner.DropDownItems.Add("Unten rechts", Nothing, Sub() MoveToCorner(True, True))
        m.Items.Add(corner)

        m.Items.Add(New ToolStripMenuItem("Durchklickbar machen", Nothing, Sub() SetClickThrough(True)) With {
            .ToolTipText = "Klicks gehen durch das Fenster hindurch. Wieder lösen über das Tray-Menü."})
        m.Items.Add(New ToolStripSeparator())
        m.Items.Add("Editor öffnen", Nothing, Sub() _tray.ShowMainForm())
        m.Items.Add("Mini-Ansicht schließen", Nothing, Sub() RaiseEvent CloseRequested())
        Return m
    End Function

    ' ------------------------------------------------------------------ Ereignisse

    Private Sub OnUi(action As Action)
        If IsDisposed OrElse Not IsHandleCreated Then Return
        Try
            BeginInvoke(action)
        Catch ex As InvalidOperationException
        End Try
    End Sub

    Private Sub Engine_Changed()
        OnUi(Sub() Invalidate())
    End Sub

    Private Sub Tray_ConnectionChanged()
        Invalidate()
    End Sub

    Private Sub Engine_KeyStateChanged(keyIndex As Integer, pressed As Boolean)
        OnUi(Sub()
                 If keyIndex < 0 OrElse keyIndex >= _pressed.Length Then Return
                 _pressed(keyIndex) = pressed
                 Invalidate()
             End Sub)
    End Sub

    ' ------------------------------------------------------------------ Zeichnen

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.Clear(Theme.PlateColor)
        Dim k = KeyFor(Width, False)
        Dim pad = PadOf(k)
        Dim gap = GapOf(k)
        Dim radius = Math.Max(3.0F, k * 0.18F)
        Dim page = _engine.CurrentPage

        For i = 0 To ConfigStore.KeyCount - 1
            Dim rect As New RectangleF(pad + (i Mod Cols) * (k + gap), pad + (i \ Cols) * (k + gap), k, k)
            DrawKey(g, rect, radius, page.GetKey(i), _pressed(i))
        Next

        ' Verbindungsstatus: Punkt im oberen Rand, rechts – aber außerhalb der abgerundeten Ecke
        Dim corner = Math.Max(4.0F, k * 0.22F)
        Dim connected = _tray.Connection.PortName IsNot Nothing
        Dim d = Math.Max(Dpx(5), pad * 0.5F)
        Theme.Dot(g, If(connected, Theme.Good, Theme.Bad), Width - corner - d, pad / 2.0F, d)

        Theme.DrawRound(g, Theme.KeyEdge, New RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F), corner)
        If Settings.ClickThrough Then
            ' kleines Schloss unten rechts: fixiert, lösen über das Tray-Menü
            Dim pt = CSng(Math.Max(6.0, Math.Round(pad * 0.6 * 72.0 / DeviceDpi * 2) / 2))
            TextRenderer.DrawText(g, ChrW(&HE72E), Theme.Icons(pt), New Rectangle(Width - pad, Height - pad, pad, pad), Theme.Faint,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
        End If
    End Sub

    Private Sub DrawKey(g As Graphics, r As RectangleF, radius As Single, key As KeyConfig, pressed As Boolean)
        If key Is Nothing Then
            If pressed Then Theme.FillRound(g, Theme.AccentDim, r, radius)
            Theme.DrawRound(g, If(pressed, Theme.Accent, Theme.DashColor), r, radius, Math.Max(1.0F, r.Width / 40), dashed:=Not pressed)
            Return
        End If
        Theme.FillRound(g, If(pressed, Theme.AccentDim, Theme.KeyCap), r, radius)
        Theme.DrawRound(g, If(pressed, Theme.Accent, Theme.KeyEdge), r, radius, If(pressed, Math.Max(1.5F, r.Width / 30), 1.0F))

        Dim fore = If(pressed, Theme.AccentSoftText, Theme.Fg)
        Dim label = If(key.Label, "").Trim()
        Dim inner = RectangleF.Inflate(r, -r.Width * 0.08F, -r.Height * 0.08F)
        Dim labelPt = CSng(Math.Max(5.5, Math.Round(r.Height * 0.16 * 72.0 / DeviceDpi * 2) / 2))
        Dim labelFont = Theme.UiSemi(labelPt)
        Dim labelH = If(label.Length > 0, TextRenderer.MeasureText("Ag", labelFont).Height, 0)
        Dim iconSize = Math.Min(inner.Width, inner.Height - labelH) * If(label.Length > 0, 0.8F, 0.7F)
        Dim showIcon = iconSize >= Dpx(10)
        Dim blockH = If(showIcon, iconSize, 0) + labelH
        Dim y = inner.Y + (inner.Height - blockH) / 2

        If showIcon Then
            Dim iconRect As New RectangleF(inner.X + (inner.Width - iconSize) / 2, y, iconSize, iconSize)
            Dim img = KeyIcons.GetImage(key)
            If img IsNot Nothing Then
                g.InterpolationMode = InterpolationMode.HighQualityBicubic
                g.DrawImage(img, iconRect)
            Else
                Dim pt = CSng(Math.Max(6.0, Math.Round(iconSize * 0.62 * 72.0 / DeviceDpi * 2) / 2))
                TextRenderer.DrawText(g, Theme.GlyphFor(key.Press), Theme.Icons(pt), Rectangle.Round(RectangleF.Inflate(iconRect, 4, 4)), fore,
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
            End If
            y += iconSize
        End If
        If label.Length > 0 Then
            Dim top = If(showIcon, y, r.Y + (r.Height - labelH) / 2)
            Dim tr As New Rectangle(CInt(r.X + 1), CInt(top), CInt(r.Width - 2), labelH)
            TextRenderer.DrawText(g, label, labelFont, tr, fore,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPadding)
        End If
    End Sub

    ' ------------------------------------------------------------------ Aufräumen

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            RemoveHandler _engine.StateChanged, AddressOf Engine_Changed
            RemoveHandler _engine.ConfigChanged, AddressOf Engine_Changed
            RemoveHandler _engine.KeyStateChanged, AddressOf Engine_KeyStateChanged
            RemoveHandler _tray.ConnectionChanged, AddressOf Tray_ConnectionChanged
            If _saveTimer.Enabled Then   ' noch nicht gespeicherte Änderung nicht verlieren
                _saveTimer.Stop()
                RaiseEvent SettingsChanged()
            End If
            _topmostTimer.Dispose()
            _saveTimer.Dispose()
            _menu?.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
