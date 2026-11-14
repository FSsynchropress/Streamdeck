' Sound-Galerie: mitgelieferte Sounds als Kacheln mit Symbol, Kategorie-Chips und Suche.
' Klick = vorhören (erneuter Klick stoppt), Doppelklick/Enter = übernehmen.
Imports System.Drawing.Drawing2D
Imports NAudio.Wave
Imports StreamDeck.Core

Public Class SoundPickerForm
    Inherits ThemedForm

    Private ReadOnly _catalog As SoundCatalog
    Private ReadOnly _search As New TextBox With {.PlaceholderText = "Suchen … z. B. airhorn, fail, tür", .Width = 300}
    Private ReadOnly _count As New Label With {.AutoSize = True, .ForeColor = Theme.Muted, .Font = Theme.Ui(9.0F)}
    Private ReadOnly _chips As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .WrapContents = True, .Padding = New Padding(16, 0, 16, 10)}
    Private ReadOnly _grid As New SoundGrid With {.Dock = DockStyle.Fill}
    Private ReadOnly _ok As New ThemedButton("Übernehmen", ButtonKind.Primary)
    Private _category As String
    Private _player As WaveOutEvent
    Private _reader As AudioFileReader

    ''' <summary>Gewählter Pfad (relativ, z. B. "Sounds\airhorn.mp3").</summary>
    Public Property Result As String

    Public Sub New(catalog As SoundCatalog, current As String)
        _catalog = catalog
        Text = "Sound-Galerie"
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False
        MinimizeBox = False
        ClientSize = New Size(1020, 640)
        MinimumSize = New Size(560, 420)

        ' Kopfzeile: Suche links, Anzahl rechts
        Dim head As New Panel With {.Dock = DockStyle.Top, .Height = 58, .Padding = New Padding(20, 16, 20, 6)}
        _search.Font = Theme.Ui(10.5F)
        _search.Dock = DockStyle.Left
        _count.Dock = DockStyle.Right
        _count.Padding = New Padding(0, 7, 0, 0)
        head.Controls.Add(_search)
        head.Controls.Add(_count)
        AddHandler _search.TextChanged, Sub() Refill()

        ' Kategorie-Chips
        AddChip("Alle", Nothing, Theme.Accent)
        For Each c In catalog.Categories
            AddChip(c, c, CategoryColor(c))
        Next

        Dim gridHost As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(12, 0, 4, 0), .BackColor = Theme.Surface}
        gridHost.Controls.Add(_grid)
        AddHandler _grid.TileClicked, Sub(s, e) TogglePreview(e)
        AddHandler _grid.TileActivated, Sub(s, e) Accept(e)
        AddHandler _grid.SelectionChanged, Sub() _ok.Enabled = _grid.Selected IsNot Nothing

        Dim hint As New Label With {.Text = "Klick = anhören · Doppelklick = übernehmen · Leertaste = abspielen/stoppen", .AutoSize = True,
                                    .ForeColor = Theme.Faint, .Font = Theme.Ui(8.5F), .Padding = New Padding(0, 9, 0, 0)}
        Dim cancel As New ThemedButton("Abbrechen", ButtonKind.Ghost) With {.DialogResult = DialogResult.Cancel}
        AddHandler _ok.Click, Sub() Accept(_grid.Selected)

        Controls.Add(gridHost)
        Controls.Add(_chips)
        Controls.Add(head)
        Controls.Add(MakeFooter(hint, cancel, _ok))
        CancelButton = cancel
        Theme.Apply(Me)

        Refill()
        Dim cur = catalog.Sounds.FirstOrDefault(Function(s) String.Equals(s.File, current, StringComparison.OrdinalIgnoreCase))
        _grid.Select(cur)
        _ok.Enabled = _grid.Selected IsNot Nothing
        AddHandler Shown, Sub()
                              _grid.Select(_grid.Selected, scrollIntoView:=True)   ' erst jetzt steht die Rastergröße fest
                              _search.Focus()
                          End Sub
        LoadDurationsAsync()
    End Sub

    ' ------------------------------------------------------------------ Kategorien

    Private Shared ReadOnly Palette As New Dictionary(Of String, Color)(StringComparer.OrdinalIgnoreCase) From {
        {"Hype", Color.FromArgb(242, 169, 59)},
        {"Fail", Color.FromArgb(255, 107, 94)},
        {"Comedy", Color.FromArgb(244, 114, 182)},
        {"Spannung", Color.FromArgb(167, 139, 250)},
        {"Game & Sci-Fi", Color.FromArgb(34, 211, 238)},
        {"Menschen", Color.FromArgb(251, 146, 60)},
        {"Tiere", Color.FromArgb(74, 222, 128)},
        {"Alltag", Color.FromArgb(96, 165, 250)}}

    Private Shared ReadOnly Fallback As Color() = {Color.FromArgb(45, 212, 191), Color.FromArgb(232, 121, 249), Color.FromArgb(163, 230, 53)}

    Public Shared Function CategoryColor(category As String) As Color
        If category Is Nothing Then Return Theme.Accent
        Dim c As Color
        If Palette.TryGetValue(category, c) Then Return c
        If category = SoundCatalog.OtherCategory Then Return Theme.Muted
        Return Fallback(Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(category) Mod Fallback.Length))
    End Function

    Private Sub AddChip(text As String, category As String, color As Color)
        Dim n = If(category Is Nothing, _catalog.Sounds.Count, _catalog.Sounds.Where(Function(s) s.Category = category).Count())
        Dim chip As New CategoryChip(text, n, color) With {.Selected = category Is Nothing}
        AddHandler chip.Click, Sub()
                                   _category = category
                                   For Each other As CategoryChip In _chips.Controls
                                       other.Selected = other Is chip
                                   Next
                                   Refill()
                               End Sub
        _chips.Controls.Add(chip)
    End Sub

    Private Sub Refill()
        Dim items = _catalog.Filter(_category, _search.Text)
        _grid.SetItems(items)
        _count.Text = If(items.Count = 1, "1 Sound", $"{items.Count} Sounds")
        _ok.Enabled = _grid.Selected IsNot Nothing
    End Sub

    ' ------------------------------------------------------------------ Vorhören

    Private Sub TogglePreview(entry As SoundEntry)
        If entry Is Nothing Then Return
        Dim same = _grid.Playing Is entry
        StopPreview()
        If same Then Return
        Try
            Dim path = ProcessActions.ResolveAppRelativePath(entry.File, AppContext.BaseDirectory)
            _reader = New AudioFileReader(path) With {.Volume = 0.8F}
            _player = New WaveOutEvent()
            _player.Init(_reader)
            Dim player = _player
            AddHandler _player.PlaybackStopped, Sub() BeginInvokeSafe(Sub()
                                                                         If _player Is player Then StopPreview()
                                                                     End Sub)
            _player.Play()
            _grid.Playing = entry
        Catch ex As Exception
            StopPreview()
            MessageBox.Show(Me, $"Sound kann nicht abgespielt werden:{vbCrLf}{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    Private Sub StopPreview()
        Dim p = _player, r = _reader
        _player = Nothing
        _reader = Nothing
        _grid.Playing = Nothing
        Try
            p?.Stop()
        Catch
        End Try
        p?.Dispose()
        r?.Dispose()
    End Sub

    Private Sub BeginInvokeSafe(a As Action)
        If IsDisposed OrElse Not IsHandleCreated Then Return
        Try
            BeginInvoke(a)
        Catch ex As InvalidOperationException
        End Try
    End Sub

    ''' <summary>Dauer jeder Datei im Hintergrund ermitteln (einmal pro Programmlauf zwischengespeichert).</summary>
    Private Sub LoadDurationsAsync()
        Dim todo = _catalog.Sounds.Where(Function(s) Not SoundGrid.Durations.ContainsKey(s.File)).ToList()
        If todo.Count = 0 Then Return
        Task.Run(Sub()
                     For Each s In todo
                         If IsDisposed Then Return
                         Try
                             Using r As New AudioFileReader(ProcessActions.ResolveAppRelativePath(s.File, AppContext.BaseDirectory))
                                 SoundGrid.Durations(s.File) = r.TotalTime
                             End Using
                         Catch
                             SoundGrid.Durations(s.File) = TimeSpan.Zero
                         End Try
                     Next
                     BeginInvokeSafe(Sub() _grid.Invalidate())
                 End Sub)
    End Sub

    ' ------------------------------------------------------------------ Bedienung

    Private Sub Accept(entry As SoundEntry)
        If entry Is Nothing Then Return
        Result = entry.File
        DialogResult = DialogResult.OK
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        Select Case keyData
            Case Keys.Enter
                If _grid.Selected IsNot Nothing Then
                    Accept(_grid.Selected)
                    Return True
                End If
            Case Keys.Down
                If _search.Focused Then
                    _grid.Focus()
                    If _grid.Selected Is Nothing Then _grid.MoveSelection(0)
                    Return True
                End If
            Case Keys.Space
                If _grid.Focused Then
                    TogglePreview(_grid.Selected)
                    Return True
                End If
        End Select
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        StopPreview()
        MyBase.OnFormClosed(e)
    End Sub

    ''' <summary>Öffnet die Galerie. Nothing = abgebrochen.</summary>
    Public Shared Function Pick(owner As IWin32Window, current As String) As String
        Dim catalog = SoundActionHandler.LoadBuiltInCatalog()
        If catalog.Sounds.Count = 0 Then
            MessageBox.Show(owner, "Im Programmordner wurden keine mitgelieferten Sounds gefunden.", "Sound-Galerie", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return Nothing
        End If
        Using d As New SoundPickerForm(catalog, current)
            Return If(d.ShowDialog(owner) = DialogResult.OK, d.Result, Nothing)
        End Using
    End Function
End Class

' ====================================================================== Kategorie-Chip

''' <summary>Abgerundeter Filter-Knopf mit Farbpunkt und Anzahl.</summary>
Public Class CategoryChip
    Inherits Control

    Private ReadOnly _count As Integer
    Private ReadOnly _color As Color
    Private _selected As Boolean
    Private _hover As Boolean

    Public Sub New(text As String, count As Integer, color As Color)
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        Me.Text = text
        _count = count
        _color = color
        Cursor = Cursors.Hand
        Margin = New Padding(4, 3, 4, 3)
        AccessibleRole = AccessibleRole.RadioButton
        AccessibleName = $"{text} ({count})"
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

    Private Const Measure As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine

    Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
        Dim textW = TextRenderer.MeasureText(Text, Theme.UiSemi(9.0F), Size.Empty, Measure).Width +
                    TextRenderer.MeasureText(_count.ToString(), Theme.Ui(8.0F), Size.Empty, Measure).Width
        Return New Size(textW + Theme.PxI(Me, 36), Theme.PxI(Me, 30))
    End Function

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Size = GetPreferredSize(Size.Empty)
    End Sub

    Protected Overrides Sub OnDpiChangedAfterParent(e As EventArgs)
        MyBase.OnDpiChangedAfterParent(e)
        Size = GetPreferredSize(Size.Empty)
    End Sub

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

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(If(Parent?.BackColor, Theme.Surface))
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim r As New RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F)
        Dim rad = r.Height / 2
        If _selected Then
            Theme.FillRound(g, Color.FromArgb(48, _color), r, rad)
            Theme.DrawRound(g, Color.FromArgb(170, _color), r, rad)
        Else
            Theme.FillRound(g, If(_hover, Theme.SelectedBg, Theme.Raised), r, rad)
            Theme.DrawRound(g, Theme.BorderColor, r, rad)
        End If
        Dim dot = Theme.Px(Me, 8)
        Dim x = Theme.Px(Me, 12)
        Theme.Dot(g, _color, x + dot / 2, Height / 2.0F, dot)
        Dim flags = TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine Or TextFormatFlags.Left
        Dim tx = CInt(x + dot + Theme.Px(Me, 6))
        Dim f = Theme.UiSemi(9.0F)
        Dim tw = TextRenderer.MeasureText(Text, f, Size.Empty, Measure).Width
        TextRenderer.DrawText(g, Text, f, New Rectangle(tx, 0, tw + 2, Height), If(_selected, Theme.Fg, Theme.FgSoft), flags)
        TextRenderer.DrawText(g, _count.ToString(), Theme.Ui(8.0F), New Rectangle(tx + tw, 0, Math.Max(0, Width - tx - tw), Height), Theme.Faint, flags)
    End Sub
End Class

' ====================================================================== Kachelraster

''' <summary>Scrollbares Raster aus Sound-Kacheln (selbst gezeichnet).</summary>
Public Class SoundGrid
    Inherits Panel

    ''' <summary>Dauer je Datei (gemeinsam für alle Galerien, einmal ermittelt).</summary>
    Public Shared ReadOnly Durations As New Concurrent.ConcurrentDictionary(Of String, TimeSpan)(StringComparer.OrdinalIgnoreCase)

    Private Const TileW As Integer = 158
    Private Const TileH As Integer = 104
    Private Const Gap As Integer = 10

    Private _items As New List(Of SoundEntry)
    Private _selected As SoundEntry
    Private _playing As SoundEntry
    Private _hover As Integer = -1
    Private ReadOnly _emoji As New Font(PickEmojiFamily(), 19.0F)

    Public Event TileClicked As EventHandler(Of SoundEntry)
    Public Event TileActivated As EventHandler(Of SoundEntry)
    Public Event SelectionChanged As EventHandler

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
        AutoScroll = True
        TabStop = True
        BackColor = Theme.Surface
        AccessibleRole = AccessibleRole.List
        AccessibleName = "Sounds"
    End Sub

    <Runtime.InteropServices.DllImport("uxtheme.dll", CharSet:=Runtime.InteropServices.CharSet.Unicode)>
    Private Shared Function SetWindowTheme(hwnd As IntPtr, appName As String, idList As String) As Integer
    End Function

    ''' <summary>Dunkle Bildlaufleiste (Windows 10 1809+); ältere Systeme behalten die helle.</summary>
    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Try
            SetWindowTheme(Handle, "DarkMode_Explorer", Nothing)
        Catch ex As Exception When TypeOf ex Is DllNotFoundException OrElse TypeOf ex Is EntryPointNotFoundException
        End Try
    End Sub

    Private Shared Function PickEmojiFamily() As String
        Dim installed = FontFamily.Families.Select(Function(f) f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
        Return If(installed.Contains("Segoe UI Emoji"), "Segoe UI Emoji", If(installed.Contains("Segoe UI Symbol"), "Segoe UI Symbol", Theme.UiFamily))
    End Function

    Public ReadOnly Property Selected As SoundEntry
        Get
            Return _selected
        End Get
    End Property

    Public Property Playing As SoundEntry
        Get
            Return _playing
        End Get
        Set(value As SoundEntry)
            _playing = value
            Invalidate()
        End Set
    End Property

    Public Sub SetItems(items As List(Of SoundEntry))
        _items = items
        _hover = -1
        If _selected IsNot Nothing AndAlso Not _items.Contains(_selected) Then
            _selected = Nothing
            RaiseEvent SelectionChanged(Me, EventArgs.Empty)
        End If
        AutoScrollPosition = Point.Empty
        UpdateScrollSize()
        Invalidate()
    End Sub

    Public Overloads Sub [Select](entry As SoundEntry, Optional scrollIntoView As Boolean = False)
        If entry IsNot Nothing AndAlso Not _items.Contains(entry) Then entry = Nothing
        _selected = entry
        If scrollIntoView AndAlso entry IsNot Nothing Then EnsureVisible(_items.IndexOf(entry))
        Invalidate()
        RaiseEvent SelectionChanged(Me, EventArgs.Empty)
    End Sub

    ' --- Geometrie

    Private ReadOnly Property Tw As Integer
        Get
            Return Theme.PxI(Me, TileW)
        End Get
    End Property

    Private ReadOnly Property Th As Integer
        Get
            Return Theme.PxI(Me, TileH)
        End Get
    End Property

    Private ReadOnly Property Gp As Integer
        Get
            Return Theme.PxI(Me, Gap)
        End Get
    End Property

    Private ReadOnly Property Columns As Integer
        Get
            Return Math.Max(1, (ClientSize.Width - Gp) \ (Tw + Gp))
        End Get
    End Property

    ''' <summary>Kachelbreite so gestreckt, dass die Zeile die Breite füllt.</summary>
    Private ReadOnly Property CellW As Integer
        Get
            Return Math.Max(Tw, (ClientSize.Width - Gp) \ Columns - Gp)
        End Get
    End Property

    Private Function TileRect(i As Integer) As Rectangle
        Dim col = i Mod Columns, row = i \ Columns
        Return New Rectangle(Gp + col * (CellW + Gp), Gp + row * (Th + Gp) + AutoScrollPosition.Y, CellW, Th)
    End Function

    Private Sub UpdateScrollSize()
        Dim rows = CInt(Math.Ceiling(_items.Count / CDbl(Columns)))
        AutoScrollMinSize = New Size(0, Gp + rows * (Th + Gp))
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        UpdateScrollSize()
        Invalidate()
    End Sub

    Private Function HitTest(p As Point) As Integer
        For i = 0 To _items.Count - 1
            If TileRect(i).Contains(p) Then Return i
        Next
        Return -1
    End Function

    Private Sub EnsureVisible(i As Integer)
        If i < 0 Then Return
        Dim r = TileRect(i)
        Dim y = -AutoScrollPosition.Y
        If r.Top < 0 Then
            AutoScrollPosition = New Point(0, y + r.Top - Gp)
        ElseIf r.Bottom > ClientSize.Height Then
            AutoScrollPosition = New Point(0, y + r.Bottom - ClientSize.Height + Gp)
        End If
        Invalidate()
    End Sub

    ' --- Maus & Tastatur

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        Dim h = HitTest(e.Location)
        If h <> _hover Then
            _hover = h
            Cursor = If(h >= 0, Cursors.Hand, Cursors.Default)
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
        If h >= 0 AndAlso e.Button = MouseButtons.Left Then
            [Select](_items(h))
            If e.Clicks = 1 Then RaiseEvent TileClicked(Me, _items(h))
        End If
        MyBase.OnMouseDown(e)
    End Sub

    Protected Overrides Sub OnMouseDoubleClick(e As MouseEventArgs)
        Dim h = HitTest(e.Location)
        If h >= 0 Then RaiseEvent TileActivated(Me, _items(h))
        MyBase.OnMouseDoubleClick(e)
    End Sub

    Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
        Select Case keyData
            Case Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown
                Return True
        End Select
        Return MyBase.IsInputKey(keyData)
    End Function

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        Dim pageRows = Math.Max(1, ClientSize.Height \ (Th + Gp))
        Select Case e.KeyCode
            Case Keys.Left : MoveSelection(-1)
            Case Keys.Right : MoveSelection(1)
            Case Keys.Up : MoveSelection(-Columns)
            Case Keys.Down : MoveSelection(Columns)
            Case Keys.PageUp : MoveSelection(-Columns * pageRows)
            Case Keys.PageDown : MoveSelection(Columns * pageRows)
            Case Keys.Home : MoveTo(0)
            Case Keys.End : MoveTo(_items.Count - 1)
        End Select
        MyBase.OnKeyDown(e)
    End Sub

    Public Sub MoveSelection(delta As Integer)
        Dim cur = If(_selected Is Nothing, -1, _items.IndexOf(_selected))
        MoveTo(If(cur < 0, 0, cur + delta))
    End Sub

    Private Sub MoveTo(i As Integer)
        If _items.Count = 0 Then Return
        i = Math.Max(0, Math.Min(_items.Count - 1, i))
        [Select](_items(i))
        EnsureVisible(i)
    End Sub

    Protected Overrides Sub OnGotFocus(e As EventArgs)
        Invalidate()
        MyBase.OnGotFocus(e)
    End Sub

    Protected Overrides Sub OnLostFocus(e As EventArgs)
        Invalidate()
        MyBase.OnLostFocus(e)
    End Sub

    Protected Overrides Sub OnScroll(se As ScrollEventArgs)
        MyBase.OnScroll(se)
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        MyBase.OnMouseWheel(e)
        Invalidate()
    End Sub

    ' --- Zeichnen

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(BackColor)
        g.SmoothingMode = SmoothingMode.AntiAlias
        If _items.Count = 0 Then
            TextRenderer.DrawText(g, "Keine Sounds gefunden.", Theme.Ui(10.0F), ClientRectangle, Theme.Muted,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            Return
        End If
        For i = 0 To _items.Count - 1
            Dim r = TileRect(i)
            If r.Bottom < e.ClipRectangle.Top OrElse r.Top > e.ClipRectangle.Bottom Then Continue For
            DrawTile(g, _items(i), r, i = _hover)
        Next
    End Sub

    Private Sub DrawTile(g As Graphics, s As SoundEntry, r As Rectangle, hover As Boolean)
        Dim accent = SoundPickerForm.CategoryColor(s.Category)
        Dim isSel = s Is _selected
        Dim isPlaying = s Is _playing
        Dim rf As New RectangleF(r.X + 0.5F, r.Y + 0.5F, r.Width - 1, r.Height - 1)
        Dim rad = Theme.Px(Me, 10)

        Theme.FillRound(g, If(isSel, Theme.SelectedBg, If(hover, Theme.KeyCapHover, Theme.Raised)), rf, rad)
        ' farbiger Schimmer von oben links in Kategoriefarbe
        Using path = Theme.Round(rf, rad), br As New LinearGradientBrush(rf, Color.FromArgb(If(isPlaying, 80, 38), accent), Color.FromArgb(0, accent), LinearGradientMode.ForwardDiagonal)
            g.FillPath(br, path)
        End Using
        Dim edge = If(isPlaying, accent, If(isSel, Theme.Accent, If(hover, Theme.KeyEdge, Theme.BorderColor)))
        Theme.DrawRound(g, edge, rf, rad, If(isSel OrElse isPlaying, Theme.Px(Me, 2), 1.0F))
        If isSel AndAlso Focused AndAlso ShowFocusCues Then Theme.DrawRound(g, Theme.Fg, RectangleF.Inflate(rf, -3, -3), rad - 2, 1)

        Dim pad = Theme.PxI(Me, 12)
        ' Symbol in Kategoriefarbe
        Dim iconBox As New Rectangle(r.X + pad - 2, r.Y + pad - 4, Theme.PxI(Me, 48), Theme.PxI(Me, 40))
        Dim icon = If(String.IsNullOrEmpty(s.Icon), "♪", s.Icon)
        TextRenderer.DrawText(g, icon, _emoji, iconBox, accent, TextFormatFlags.Left Or TextFormatFlags.Top Or TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding)

        ' Play-/Stopp-Knopf oben rechts
        If hover OrElse isPlaying OrElse isSel Then
            Dim d = Theme.Px(Me, 28)
            Dim cx = r.Right - pad - d / 2 + 2, cy = r.Y + pad + d / 2 - 2
            Theme.Dot(g, If(isPlaying, accent, Color.FromArgb(If(hover, 70, 40), Theme.Fg)), cx, cy, d)
            Dim glyph = If(isPlaying, ChrW(&HE71A), ChrW(&HE768))    ' Stopp / Play
            TextRenderer.DrawText(g, glyph, Theme.Icons(9.0F), New Rectangle(CInt(cx - d / 2), CInt(cy - d / 2), CInt(d), CInt(d)),
                                  If(isPlaying, Theme.AccentText, Theme.Fg), TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
        End If

        ' Name und Unterzeile
        Dim flags = TextFormatFlags.Left Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis
        Dim nameRect As New Rectangle(r.X + pad, r.Bottom - pad - Theme.PxI(Me, 38), r.Width - 2 * pad, Theme.PxI(Me, 20))
        TextRenderer.DrawText(g, s.Name, Theme.UiSemi(9.5F), nameRect, Theme.Fg, flags)
        Dim dur As TimeSpan
        Dim subline = s.Category
        If Durations.TryGetValue(s.File, dur) AndAlso dur > TimeSpan.Zero Then subline &= " · " & FormatDuration(dur)
        Dim subRect As New Rectangle(r.X + pad, r.Bottom - pad - Theme.PxI(Me, 17), r.Width - 2 * pad, Theme.PxI(Me, 17))
        TextRenderer.DrawText(g, subline, Theme.Ui(8.0F), subRect, Theme.Muted, flags)
    End Sub

    Private Shared Function FormatDuration(t As TimeSpan) As String
        If t.TotalSeconds < 60 Then Return t.TotalSeconds.ToString("0.0") & " s"
        Return $"{CInt(Math.Floor(t.TotalMinutes))}:{t.Seconds:00} min"
    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then _emoji.Dispose()
        MyBase.Dispose(disposing)
    End Sub
End Class
