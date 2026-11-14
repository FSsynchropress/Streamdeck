' Hauptfenster: Seitenleiste (Profile, Seiten), Geräteplatte 3 × 5 mit Live-Hervorhebung,
' Protokoll, Inspektor für die ausgewählte Taste, Statusleiste.
Imports System.Drawing.Drawing2D
Imports System.Text.Json
Imports StreamDeck.Core

Public Class MainForm
    Inherits ThemedForm

    Private Const Cols As Integer = 5

    Private ReadOnly _tray As TrayContext
    Private ReadOnly _engine As DeckEngine
    Private ReadOnly _buttons(ConfigStore.KeyCount - 1) As KeyButton
    Private ReadOnly _brand As New Panel With {.Height = 64}
    Private ReadOnly _profileList As New Panel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink}
    Private ReadOnly _pageList As New Panel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink}
    Private ReadOnly _crumb As New Label With {.AutoSize = True, .ForeColor = Theme.Muted, .Font = Theme.Ui(8.5F), .Dock = DockStyle.Top}
    Private ReadOnly _pageTitle As New Label With {.AutoSize = True, .ForeColor = Theme.Fg, .Font = Theme.UiBold(19.0F), .Dock = DockStyle.Top}
    Private ReadOnly _deckHost As New Panel With {.Dock = DockStyle.Fill}
    Private ReadOnly _plate As New CardPanel With {.Radius = 24, .Fill = Theme.PlateColor, .Edge = Color.FromArgb(30, 33, 37)}
    Private ReadOnly _log As New ListBox With {.Dock = DockStyle.Fill, .IntegralHeight = False, .DrawMode = DrawMode.OwnerDrawFixed, .BorderStyle = BorderStyle.None}
    Private ReadOnly _status As New BorderPanel With {.Dock = DockStyle.Bottom, .Height = 30, .Borders = AnchorStyles.Top}
    Private ReadOnly _inspector As KeyInspector
    Private ReadOnly _tip As New ToolTip()
    Private _copied As KeyConfig
    Private ReadOnly _learnButton As New ThemedButton("Taste lernen")
    Private _selected As Integer = 0

    Public Sub New(tray As TrayContext)
        _tray = tray
        _engine = tray.Engine
        _inspector = New KeyInspector(_engine.Runner) With {.Dock = DockStyle.Right, .Width = 420}

        SuspendLayout()
        Text = "DIY Stream Deck"
        Icon = tray.IconConnected
        ClientSize = New Size(1360, 860)
        MinimumSize = New Size(1040, 660)
        StartPosition = FormStartPosition.CenterScreen
        BackColor = Theme.Bg

        Controls.Add(BuildCenter())
        Controls.Add(BuildSidebar())
        Controls.Add(_inspector)
        _status.BackColor = Theme.Chrome
        AddHandler _status.Paint, AddressOf PaintStatus
        Controls.Add(_status)
        ResumeLayout(True)

        AddHandler _inspector.SaveRequested, Sub(i, k, p, pg) SetKey(i, k, p, pg)

        ' Ereignisse von Engine/Tray (kommen auf beliebigen Threads)
        AddHandler _engine.StateChanged, AddressOf Engine_StateChanged
        AddHandler _engine.KeyStateChanged, AddressOf Engine_KeyStateChanged
        AddHandler _engine.KeyLearned, AddressOf Engine_KeyLearned
        AddHandler _tray.LogAdded, AddressOf Tray_LogAdded
        AddHandler _tray.ConnectionChanged, AddressOf UpdateConnectionStatus

        For Each line In _tray.LogLines
            _log.Items.Add(line)
        Next
        ScrollLogToEnd()
        RefreshView()
        UpdateConnectionStatus()
    End Sub

    ' ------------------------------------------------------------------ Aufbau

    Private Function BuildSidebar() As Control
        Dim sidebar As New BorderPanel With {.Dock = DockStyle.Left, .Width = 256, .BackColor = Theme.SidebarBg, .Borders = AnchorStyles.Right, .Padding = New Padding(12, 10, 12, 12)}
        AddHandler _brand.Paint, AddressOf PaintBrand

        Dim profileHead = SectionHead("Profile", "Neues Profil", AddressOf NewProfile)
        Dim pageHead = SectionHead("Seiten", "Neue Seite", AddressOf NewPage)
        Dim content As New Panel With {.Dock = DockStyle.Fill, .AutoScroll = True}
        Theme.StackTop(content, _brand, Spacer(10), profileHead, _profileList, Spacer(18), pageHead, _pageList)

        Dim foot As New BorderPanel With {.Dock = DockStyle.Bottom, .Borders = AnchorStyles.Top, .Height = 142, .Padding = New Padding(0, 12, 0, 0)}
        Dim exportB = SideButton("Konfiguration exportieren", Theme.GlyphUpload, AddressOf ExportConfig)
        Dim importB = SideButton("Konfiguration importieren", Theme.GlyphDownload, AddressOf ImportConfig)
        Dim settingsB = SideButton("Einstellungen", Theme.GlyphSettings, AddressOf ShowSettings)
        Theme.StackTop(foot, exportB, importB, settingsB)

        sidebar.Controls.Add(content)
        sidebar.Controls.Add(foot)
        Return sidebar
    End Function

    Private Function BuildCenter() As Control
        Dim center As New Panel With {.Dock = DockStyle.Fill, .BackColor = Theme.Bg, .Padding = New Padding(32, 22, 32, 18)}

        ' --- Kopf: Brotkrumen, Seitenname, Aktionen
        Dim head As New Panel With {.Dock = DockStyle.Top, .Height = 74}
        Dim titles As New Panel With {.Dock = DockStyle.Fill}
        titles.Controls.Add(_pageTitle)
        titles.Controls.Add(_crumb)
        Dim actions As New FlowLayoutPanel With {.Dock = DockStyle.Right, .AutoSize = True, .WrapContents = False, .Padding = New Padding(0, 22, 0, 0)}
        Dim rename As New ThemedButton("Seite umbenennen")
        AddHandler rename.Click, Sub() RenamePage(_engine.CurrentPage.Name)
        Dim more As New ThemedButton("Weitere Seitenaktionen", ButtonKind.IconOnly, Theme.GlyphMore)
        AddHandler more.Click, Sub() ShowPageMenu(more)
        _tip.SetToolTip(more, "Weitere Seitenaktionen")
        AddHandler _learnButton.Click, Sub() SetLearnMode(Not _engine.LearnMode)
        _tip.SetToolTip(_learnButton, "Danach eine Taste am Gerät drücken – sie wird hier ausgewählt (ohne die Aktion auszuführen)")
        Dim print As New ThemedButton("Druckvorlage")
        AddHandler print.Click, Sub()
                                    Using d As New KeycapSheetDialog(_engine.CurrentProfile)
                                        d.ShowDialog(Me)
                                    End Using
                                End Sub
        _tip.SetToolTip(print, "Belegung als Vorlage für die Keycap-Einleger drucken oder als Bild speichern")
        Dim mini As New ThemedButton("Mini-Ansicht")
        AddHandler mini.Click, Sub() _tray.ToggleMiniView()
        _tip.SetToolTip(mini, "Kleines Tastenraster der aktuellen Seite, immer im Vordergrund – zum Beispiel oben links an den Bildschirmrand legen")
        actions.Controls.Add(mini)
        actions.Controls.Add(_learnButton)
        actions.Controls.Add(print)
        actions.Controls.Add(rename)
        actions.Controls.Add(more)
        head.Controls.Add(titles)
        head.Controls.Add(actions)

        ' --- Protokoll
        Dim logCard As New CardPanel With {.Dock = DockStyle.Bottom, .Height = 200, .Padding = New Padding(16, 6, 8, 10)}
        Dim logHead As New BorderPanel With {.Dock = DockStyle.Top, .Height = 40, .Borders = AnchorStyles.Bottom, .Padding = New Padding(0, 0, 0, 6)}
        logHead.Controls.Add(New Label With {.Text = "Protokoll", .Font = Theme.UiSemi(9.5F), .ForeColor = Theme.Fg, .AutoSize = False, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft})
        Dim logButtons As New FlowLayoutPanel With {.Dock = DockStyle.Right, .AutoSize = True, .WrapContents = False}
        Dim copyB As New ThemedButton("Kopieren", ButtonKind.Ghost) With {.BaseHeight = 30}
        Dim clearB As New ThemedButton("Leeren", ButtonKind.Ghost) With {.BaseHeight = 30}
        AddHandler copyB.Click, Sub()
                                    If _log.Items.Count > 0 Then Clipboard.SetText(String.Join(vbCrLf, _log.Items.Cast(Of Object)))
                                End Sub
        AddHandler clearB.Click, Sub() _log.Items.Clear()
        logButtons.Controls.Add(copyB)
        logButtons.Controls.Add(clearB)
        logHead.Controls.Add(logButtons)
        _log.BackColor = Theme.SidebarBg
        _log.ForeColor = Theme.FgSoft
        _log.Font = Theme.Mono(9.0F)
        _log.ItemHeight = 21
        AddHandler _log.DrawItem, AddressOf DrawLogItem
        Dim logBody As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(0, 6, 0, 0)}
        logBody.Controls.Add(_log)
        logCard.Controls.Add(logBody)
        logCard.Controls.Add(logHead)

        ' --- Geräteplatte
        Dim hint As New Label With {.Text = "Klick = bearbeiten · Rechtsklick = Menü · Ziehen = tauschen · Aktion von oben auf eine Taste ziehen = zuweisen · Am Gerät gedrückte Tasten leuchten live auf",
                                    .Dock = DockStyle.Bottom, .Height = 34, .TextAlign = ContentAlignment.MiddleCenter, .ForeColor = Theme.Muted, .Font = Theme.Ui(8.5F)}
        For i = 0 To ConfigStore.KeyCount - 1
            Dim b As New KeyButton(i)
            AddHandler b.Click, AddressOf KeyButton_Click
            AddHandler b.MouseUp, AddressOf KeyButton_MouseUp
            AddHandler b.SwapRequested, AddressOf SwapKeys
            AddHandler b.ActionDropped, AddressOf DropAction
            _buttons(i) = b
            _plate.Controls.Add(b)
        Next
        _deckHost.Controls.Add(_plate)
        AddHandler _deckHost.Resize, Sub() LayoutDeck()

        center.Controls.Add(_deckHost)
        center.Controls.Add(hint)
        center.Controls.Add(Spacer(14, DockStyle.Bottom))
        center.Controls.Add(logCard)
        center.Controls.Add(BuildPalette())
        center.Controls.Add(head)
        Return center
    End Function

    ''' <summary>Aktionsleiste: jeden Aktionstyp per Drag-and-drop auf eine Taste ziehen.</summary>
    Private Function BuildPalette() As Control
        Dim strip As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .WrapContents = True, .Padding = New Padding(0, 0, 0, 10)}
        For Each t In ActionTypes.All.Where(Function(x) x <> ActionTypes.Wait)
            strip.Controls.Add(New ActionChip(t))
        Next
        Return strip
    End Function

    Private Sub DropAction(index As Integer, actionType As String)
        If ConfigStore.IsFixedBackKey(_engine.Config, _engine.CurrentPage, index) Then Return
        SelectKey(index)
        If _inspector.KeyIndex = index Then _inspector.ApplyActionType(actionType)
    End Sub

    ' ------------------------------------------------------------------ Taste lernen

    Private Sub SetLearnMode(active As Boolean)
        _engine.LearnMode = active
        _learnButton.Text = If(active, "Taste am Gerät drücken …", "Taste lernen")
        _learnButton.Invalidate()
    End Sub

    Private Sub Engine_KeyLearned(keyIndex As Integer)
        OnUi(Sub()
                 SetLearnMode(False)
                 If keyIndex >= 0 AndAlso keyIndex < _buttons.Length Then
                     SelectKey(keyIndex)
                     _buttons(keyIndex).Focus()
                 End If
             End Sub)
    End Sub

    Private Shared Function Spacer(height As Integer, Optional dock As DockStyle = DockStyle.Top) As Panel
        Return New Panel With {.Height = height, .Dock = dock}
    End Function

    Private Function SectionHead(title As String, addTip As String, handler As Action) As Panel
        Dim p As New Panel With {.Height = 34, .Padding = New Padding(8, 0, 0, 0)}
        Dim l = Theme.SectionLabel(title)
        l.AutoSize = False
        l.Dock = DockStyle.Fill
        l.TextAlign = ContentAlignment.MiddleLeft
        Dim add As New ThemedButton(addTip, ButtonKind.IconOnly, Theme.GlyphAdd) With {.BaseHeight = 30, .Dock = DockStyle.Right}
        AddHandler add.Click, Sub() handler()
        _tip.SetToolTip(add, addTip)
        p.Controls.Add(l)
        p.Controls.Add(add)
        Return p
    End Function

    Private Shared Function SideButton(text As String, glyph As String, handler As Action) As ThemedButton
        Dim b As New ThemedButton(text, ButtonKind.Ghost, glyph) With {.AlignLeft = True, .AutoSize = False, .BaseHeight = 40, .Height = 40}
        AddHandler b.Click, Sub() handler()
        Return b
    End Function

    ''' <summary>Öffnet ein neues GitHub-Issue mit vorausgefüllter Vorlage (Bug Report oder Funktionswunsch).</summary>
    Private Sub OpenIssue(bug As Boolean)
        Dim version = _tray.Updates.CurrentVersion
        Dim title As String, label As String, body As String
        If bug Then
            title = "[Bug] "
            label = "bug"
            body = "**Was ist passiert?**" & vbLf & vbLf & vbLf &
                   "**Was hätte passieren sollen?**" & vbLf & vbLf & vbLf &
                   "**Schritte zum Nachstellen**" & vbLf & "1. " & vbLf & vbLf &
                   "**Protokoll** (Tray-Menü „Protokollordner öffnen“, relevante Zeilen einfügen)" & vbLf & vbLf & vbLf &
                   "---" & vbLf & $"App-Version: {version}" & vbLf & $"Windows: {Environment.OSVersion.VersionString}"
        Else
            title = "[Idee] "
            label = "enhancement"
            body = "**Welche Funktion wünschst du dir?**" & vbLf & vbLf & vbLf &
                   "**Wofür brauchst du sie / welches Problem löst sie?**" & vbLf & vbLf & vbLf &
                   "---" & vbLf & $"App-Version: {version}"
        End If
        Dim url = $"{Updater.RepoUrl}/issues/new?labels={label}&title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}"
        Try
            Process.Start(New ProcessStartInfo(url) With {.UseShellExecute = True})
        Catch ex As Exception
            MessageBox.Show(Me, $"Der Browser konnte nicht geöffnet werden:{vbCrLf}{ex.Message}", If(bug, "Fehler melden", "Funktion vorschlagen"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    Private Sub LayoutDeck()
        If _deckHost.ClientSize.Width <= 0 OrElse _deckHost.ClientSize.Height <= 0 Then Return
        Dim pad = Theme.PxI(Me, 22)
        Dim gap = Theme.PxI(Me, 14)
        Dim availW = _deckHost.ClientSize.Width
        Dim availH = _deckHost.ClientSize.Height
        Dim keySize = Math.Min((availW - 2 * pad - 4 * gap) \ 5, (availH - 2 * pad - 2 * gap) \ 3)
        keySize = Math.Max(Theme.PxI(Me, 52), Math.Min(keySize, Theme.PxI(Me, 150)))
        Dim plateW = 5 * keySize + 4 * gap + 2 * pad
        Dim plateH = 3 * keySize + 2 * gap + 2 * pad
        _plate.Bounds = New Rectangle(Math.Max(0, (availW - plateW) \ 2), Math.Max(0, (availH - plateH) \ 2), plateW, plateH)
        For i = 0 To _buttons.Length - 1
            _buttons(i).Bounds = New Rectangle(pad + (i Mod Cols) * (keySize + gap), pad + (i \ Cols) * (keySize + gap), keySize, keySize)
        Next
    End Sub

    ' ------------------------------------------------------------------ Zeichnen: Marke, Status, Protokoll

    Private Sub PaintBrand(sender As Object, e As PaintEventArgs)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim s = Theme.Px(_brand, 28)
        Dim x0 = Theme.Px(_brand, 8)
        Dim y0 = Theme.Px(_brand, 10)
        Theme.FillRound(g, Theme.Accent, New RectangleF(x0, y0, s, s), Theme.Px(_brand, 7))
        Using b As New SolidBrush(Theme.AccentText)
            Dim gp = Theme.Px(_brand, 2)
            Dim cell = (s - Theme.Px(_brand, 10) - 2 * gp) / 3
            For r = 0 To 1
                For c = 0 To 2
                    g.FillRectangle(b, x0 + Theme.Px(_brand, 5) + c * (cell + gp), y0 + Theme.Px(_brand, 8) + r * (cell + gp), cell, cell)
                Next
            Next
        End Using
        Dim tx = CInt(x0 + s + Theme.Px(_brand, 10))
        TextRenderer.DrawText(g, "DIY Stream Deck", Theme.UiSemi(10.0F), New Point(tx, CInt(y0 - Theme.Px(_brand, 3))), Theme.Fg)

        Dim port = _tray.Connection.PortName
        Dim pillText = If(port IsNot Nothing, $"Verbunden · {port}", "Kein Gerät – suche …")
        Dim pf = Theme.UiSemi(8.0F)
        Dim tw = TextRenderer.MeasureText(pillText, pf).Width
        Dim pill As New RectangleF(tx, y0 + Theme.Px(_brand, 18), tw + Theme.Px(_brand, 26), Theme.Px(_brand, 20))
        Theme.FillRound(g, If(port IsNot Nothing, Theme.GoodBg, Theme.BadBg), pill, pill.Height / 2)
        Theme.Dot(g, If(port IsNot Nothing, Theme.Good, Theme.Bad), pill.X + Theme.Px(_brand, 10), pill.Y + pill.Height / 2, Theme.Px(_brand, 7))
        TextRenderer.DrawText(g, pillText, pf, New Rectangle(CInt(pill.X + Theme.Px(_brand, 18)), CInt(pill.Y), tw + 4, CInt(pill.Height)),
                              If(port IsNot Nothing, Theme.GoodText, Theme.BadText), TextFormatFlags.VerticalCenter Or TextFormatFlags.Left)
    End Sub

    Private Sub PaintStatus(sender As Object, e As PaintEventArgs)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim port = _tray.Connection.PortName
        Dim h = _status.Height
        Dim x = Theme.Px(_status, 16)
        Theme.Dot(g, If(port IsNot Nothing, Theme.Good, Theme.Bad), x + 3, h / 2.0F, Theme.Px(_status, 7))
        Dim f = Theme.Ui(8.5F)
        Dim leftText = If(port IsNot Nothing,
                          $"Gerät verbunden · {port} · Protokoll v{DeviceProtocol.SupportedVersion} · {_tray.Connection.KeyCount} Tasten",
                          "Kein Gerät verbunden – suche auf allen COM-Ports …")
        TextRenderer.DrawText(g, leftText, f, New Rectangle(CInt(x + 14), 0, _status.Width \ 2, h), Theme.Muted, TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)

        Dim pathText = _tray.ConfigPath
        Dim mf = Theme.Mono(8.0F)
        Dim pw = TextRenderer.MeasureText(pathText, mf).Width
        Dim rx = _status.Width - Theme.PxI(_status, 16) - pw
        TextRenderer.DrawText(g, pathText, mf, New Rectangle(rx, 0, pw, h), Theme.Faint, TextFormatFlags.VerticalCenter)
        Dim profileText = If(_engine.IsAutoProfile, $"Profil automatisch gewählt – manuell: {_engine.Config.ActiveProfile}", "Profil manuell gewählt")
        Dim ptw = TextRenderer.MeasureText(profileText, f).Width
        TextRenderer.DrawText(g, profileText, f, New Rectangle(rx - ptw - Theme.PxI(_status, 20), 0, ptw, h), Theme.Muted, TextFormatFlags.VerticalCenter)
    End Sub

    Private Sub DrawLogItem(sender As Object, e As DrawItemEventArgs)
        If e.Index < 0 OrElse e.Index >= _log.Items.Count Then Return
        Dim g = e.Graphics
        Dim selected = (e.State And DrawItemState.Selected) = DrawItemState.Selected
        Using b As New SolidBrush(If(selected, Theme.SelectedBg, Theme.SidebarBg))
            g.FillRectangle(b, e.Bounds)
        End Using
        Dim line = CStr(_log.Items(e.Index))
        Dim time = ""
        Dim msg = line
        If line.Length > 10 AndAlso line(2) = ":"c AndAlso line(5) = ":"c Then
            time = line.Substring(0, 8)
            msg = line.Substring(8).TrimStart()
        End If
        Dim f = _log.Font
        Dim flags = TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis
        Dim x = e.Bounds.X + 2
        If time.Length > 0 Then
            Dim tw = TextRenderer.MeasureText(time, f).Width
            TextRenderer.DrawText(g, time, f, New Rectangle(x, e.Bounds.Y, tw, e.Bounds.Height), Theme.Faint, flags)
            x += tw + Theme.PxI(_log, 14)
        End If
        Dim isError = msg.Contains("Fehler", StringComparison.OrdinalIgnoreCase) OrElse msg.Contains("fehlgeschlagen", StringComparison.OrdinalIgnoreCase)
        TextRenderer.DrawText(g, msg, f, New Rectangle(x, e.Bounds.Y, e.Bounds.Right - x, e.Bounds.Height), If(isError, Theme.BadText, Theme.FgSoft), flags)
    End Sub

    ' ------------------------------------------------------------------ Ereignisse Engine/Tray

    Private Sub OnUi(action As Action)
        If IsDisposed OrElse Not IsHandleCreated Then Return
        Try
            BeginInvoke(action)
        Catch ex As InvalidOperationException
        End Try
    End Sub

    Private Sub Engine_StateChanged()
        OnUi(AddressOf RefreshView)
    End Sub

    Private Sub Engine_KeyStateChanged(keyIndex As Integer, pressed As Boolean)
        OnUi(Sub()
                 If keyIndex >= 0 AndAlso keyIndex < _buttons.Length Then _buttons(keyIndex).Pressed = pressed
             End Sub)
    End Sub

    Private Sub Tray_LogAdded(line As String)
        _log.Items.Add(line)
        While _log.Items.Count > TrayContext.MaxLogLines
            _log.Items.RemoveAt(0)
        End While
        ScrollLogToEnd()
    End Sub

    Private Sub ScrollLogToEnd()
        If _log.Items.Count > 0 Then _log.TopIndex = _log.Items.Count - 1
    End Sub

    Private Sub UpdateConnectionStatus()
        Icon = If(_tray.Connection.PortName IsNot Nothing, _tray.IconConnected, _tray.IconDisconnected)
        _brand.Invalidate()
        _status.Invalidate()
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If e.CloseReason = CloseReason.UserClosing AndAlso Not _tray.IsExiting Then
            e.Cancel = True   ' nur verstecken, die App läuft im Infobereich weiter
            Hide()
            _tray.OnMainFormHidden()
            Return
        End If
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        RemoveHandler _engine.StateChanged, AddressOf Engine_StateChanged
        RemoveHandler _engine.KeyStateChanged, AddressOf Engine_KeyStateChanged
        RemoveHandler _engine.KeyLearned, AddressOf Engine_KeyLearned
        _engine.LearnMode = False
        RemoveHandler _tray.LogAdded, AddressOf Tray_LogAdded
        RemoveHandler _tray.ConnectionChanged, AddressOf UpdateConnectionStatus
        MyBase.OnFormClosed(e)
    End Sub

    ' ------------------------------------------------------------------ Anzeige

    Private Sub RefreshView()
        Dim cfg = _engine.Config
        Dim profile = _engine.CurrentProfile
        Dim page = _engine.CurrentPage

        ' Profile
        Dim profileItems As New List(Of Control)
        For Each p In cfg.Profiles
            Dim name = p.Name
            Dim isCurrent = name = profile.Name
            Dim subtitle As String
            If isCurrent AndAlso _engine.IsAutoProfile Then
                subtitle = "automatisch gewählt"
            ElseIf Not String.IsNullOrWhiteSpace(p.AutoSwitchProcess) Then
                subtitle = "automatisch bei " & p.AutoSwitchProcess
            Else
                subtitle = If(isCurrent, "manuell gewählt", Nothing)
            End If
            Dim item As New NavItem(name) With {.ShowDot = True, .Selected = isCurrent, .Subtitle = subtitle}
            AddHandler item.Click, Sub() _engine.SelectProfile(name)
            AddHandler item.DoubleClick, Sub() RenameProfile(name)
            AddHandler item.MouseUp, Sub(s, e)
                                         If e.Button = MouseButtons.Right Then ShowProfileMenu(name, item, e.Location)
                                     End Sub
            _tip.SetToolTip(item, "Klick = wählen · Doppelklick = umbenennen · Rechtsklick = Menü")
            profileItems.Add(item)
        Next
        ReplaceItems(_profileList, profileItems)

        ' Seiten
        Dim pageItems As New List(Of Control)
        For Each pg In profile.Pages
            Dim name = pg.Name
            Dim used = Enumerable.Range(0, ConfigStore.KeyCount).Count(Function(i) pg.GetKey(i) IsNot Nothing)
            Dim item As New NavItem(If(pg.IsFolder, "▸ " & name, name)) With {.Selected = name = page.Name, .Badge = $"{used}/{ConfigStore.KeyCount}",
                                                                           .Subtitle = If(pg.IsFolder, "Ordner", Nothing)}
            AddHandler item.Click, Sub() _engine.GoToPage(name)
            AddHandler item.DoubleClick, Sub() RenamePage(name)
            AddHandler item.MouseUp, Sub(s, e)
                                         If e.Button = MouseButtons.Right Then ShowPageItemMenu(name, item, e.Location)
                                     End Sub
            pageItems.Add(item)
        Next
        ReplaceItems(_pageList, pageItems)

        _crumb.Text = $"Profil {profile.Name}  /  {If(page.IsFolder, "Ordner", "Seite")}"
        _pageTitle.Text = page.Name

        SyncInspector(profile, page)
        Dim inspectorHere = _inspector.ProfileName = profile.Name AndAlso _inspector.PageName = page.Name
        For i = 0 To _buttons.Length - 1
            Dim k = page.GetKey(i)
            _buttons(i).Key = k
            _buttons(i).Pressed = _engine.IsHeld(i)
            _buttons(i).Selected = inspectorHere AndAlso i = _inspector.KeyIndex
            _tip.SetToolTip(_buttons(i), Describe(i, k))
        Next
        _status.Invalidate()
    End Sub

    Private Shared Sub ReplaceItems(list As Panel, items As List(Of Control))
        list.SuspendLayout()
        For Each c In list.Controls.Cast(Of Control).ToList()
            c.Dispose()
        Next
        list.Controls.Clear()
        Theme.StackTop(list, items.ToArray())
        list.ResumeLayout(True)
    End Sub

    ''' <summary>Hält den Inspektor auf der gewählten Taste der aktuellen Seite, ohne ungespeicherte Eingaben zu verwerfen.</summary>
    Private Sub SyncInspector(profile As ProfileConfig, page As PageConfig)
        If _inspector.IsDirty Then Return   ' er speichert in seine eigene Seite – Eingaben behalten
        Dim samePlace = _inspector.ProfileName = profile.Name AndAlso _inspector.PageName = page.Name AndAlso _inspector.KeyIndex = _selected
        If Not samePlace OrElse KeyInspector.Serialize(page.GetKey(_selected)) <> _inspector.StoredJson Then LoadInspector()
    End Sub

    Private Sub LoadInspector()
        Dim profile = _engine.CurrentProfile
        Dim page = _engine.CurrentPage
        _inspector.LoadKey(_selected, CloneKey(page.GetKey(_selected)), profile.Name, page.Name,
                           profile.Pages.Select(Function(p) p.Name).ToList(), _engine.Config.Profiles.Select(Function(p) p.Name).ToList())
    End Sub

    Private Shared Function Describe(index As Integer, k As KeyConfig) As String
        Dim s = $"Taste {index} (Reihe {index \ Cols + 1}, Spalte {index Mod Cols + 1})"
        If k Is Nothing Then Return s & vbCrLf & "nicht belegt – klicken zum Bearbeiten"
        s &= vbCrLf & "Kurz: " & ActionTypes.Describe(k.Press)
        If k.Repeat.GetValueOrDefault() Then s &= " (wiederholt beim Halten)"
        If k.LongPress IsNot Nothing Then s &= vbCrLf & "Lang: " & ActionTypes.Describe(k.LongPress)
        If k.DoublePress IsNot Nothing Then s &= vbCrLf & "Doppelt: " & ActionTypes.Describe(k.DoublePress)
        If k.Release IsNot Nothing Then s &= vbCrLf & "Loslassen: " & ActionTypes.Describe(k.Release)
        If Not String.IsNullOrWhiteSpace(k.Description) Then s &= vbCrLf & vbCrLf & k.Description
        Return s
    End Function

    ' ------------------------------------------------------------------ Tasten

    Private Sub KeyButton_Click(sender As Object, e As EventArgs)
        If TypeOf e Is MouseEventArgs AndAlso DirectCast(e, MouseEventArgs).Button <> MouseButtons.Left Then Return
        SelectKey(DirectCast(sender, KeyButton).KeyIndex)
    End Sub

    ''' <summary>Wählt eine Taste für den Inspektor; fragt bei ungespeicherten Änderungen nach.</summary>
    Private Sub SelectKey(index As Integer)
        If ConfigStore.IsFixedBackKey(_engine.Config, _engine.CurrentPage, index) Then
            MessageBox.Show(Me, $"In Ordnern ist Taste {index} fest die Zurück-Taste. Welche Taste das ist, lässt sich in den Einstellungen ändern.",
                            "Ordner", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim here = _inspector.ProfileName = _engine.CurrentProfile.Name AndAlso _inspector.PageName = _engine.CurrentPage.Name
        If here AndAlso index = _inspector.KeyIndex Then Return
        If _inspector.IsDirty Then
            Dim answer = MessageBox.Show(Me, $"Änderungen an Taste {_inspector.KeyIndex} speichern?", "Taste bearbeiten", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question)
            If answer = DialogResult.Cancel Then Return
            If answer = DialogResult.Yes AndAlso Not _inspector.TrySave() Then Return
        End If
        _selected = index
        LoadInspector()
        For i = 0 To _buttons.Length - 1
            _buttons(i).Selected = i = index
        Next
    End Sub

    Private Sub KeyButton_MouseUp(sender As Object, e As MouseEventArgs)
        If e.Button <> MouseButtons.Right Then Return
        Dim b = DirectCast(sender, KeyButton)
        Dim i = b.KeyIndex
        Dim has = b.Key IsNot Nothing
        Dim m As New ContextMenuStrip()
        m.Items.Add("Bearbeiten", Nothing, Sub() SelectKey(i))
        m.Items.Add("Kurzen Druck testen", Nothing, Sub() _engine.Runner.Fire(b.Key?.Press, i)).Enabled = has AndAlso b.Key.Press IsNot Nothing
        m.Items.Add(New ToolStripSeparator())
        m.Items.Add("Kopieren", Nothing, Sub() _copied = CloneKey(b.Key)).Enabled = has
        m.Items.Add("Einfügen", Nothing, Sub() SetKey(i, CloneKey(_copied))).Enabled = _copied IsNot Nothing
        m.Items.Add("Leeren", Nothing, Sub() SetKey(i, Nothing)).Enabled = has
        AddHandler m.Closed, Sub() BeginInvoke(Sub() m.Dispose())
        m.Show(b, e.Location)
    End Sub

    Private Shared Function CloneKey(k As KeyConfig) As KeyConfig
        If k Is Nothing Then Return Nothing
        Return JsonSerializer.Deserialize(Of KeyConfig)(JsonSerializer.Serialize(k, ConfigStore.JsonOptions), ConfigStore.JsonOptions)
    End Function

    Private Sub SetKey(index As Integer, key As KeyConfig, Optional profileName As String = Nothing, Optional pageName As String = Nothing)
        Dim p = If(profileName, _engine.CurrentProfile.Name)
        Dim pg = If(pageName, _engine.CurrentPage.Name)
        _engine.UpdateConfig(Sub(c) c.FindProfile(p)?.FindPage(pg)?.SetKey(index, key), p, pg)
    End Sub

    Private Sub SwapKeys(fromIndex As Integer, toIndex As Integer)
        If fromIndex = toIndex Then Return
        If ConfigStore.IsFixedBackKey(_engine.Config, _engine.CurrentPage, fromIndex) OrElse
           ConfigStore.IsFixedBackKey(_engine.Config, _engine.CurrentPage, toIndex) Then Return
        Dim p = _engine.CurrentProfile.Name
        Dim pg = _engine.CurrentPage.Name
        _engine.UpdateConfig(Sub(c)
                                 Dim page = c.FindProfile(p)?.FindPage(pg)
                                 If page Is Nothing Then Return
                                 Dim a = page.GetKey(fromIndex)
                                 Dim b = page.GetKey(toIndex)
                                 page.SetKey(fromIndex, b)
                                 page.SetKey(toIndex, a)
                             End Sub, p, pg)
    End Sub

    ' ------------------------------------------------------------------ Profile

    Private Function AskName(prompt As String, title As String, defaultValue As String, existing As IEnumerable(Of String), Optional currentName As String = Nothing) As String
        Dim value = defaultValue
        Do
            value = TextPrompt.Ask(Me, title, prompt, value)
            If value Is Nothing Then Return Nothing   ' Abbrechen
            Dim err = ConfigEdit.ValidateName(value, existing, currentName)
            If err Is Nothing Then Return value.Trim()
            MessageBox.Show(Me, err, title, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Loop
    End Function

    ''' <summary>Ein Profil als Datei speichern, um es auf einem anderen Rechner zu importieren.</summary>
    Private Sub ExportProfile(name As String)
        Dim p = _engine.Config.FindProfile(name)
        If p Is Nothing Then Return
        Using d As New SaveFileDialog With {.Filter = "Stream-Deck-Profil (*.sdprofile.json)|*.sdprofile.json|JSON (*.json)|*.json", .FileName = $"{name}.sdprofile.json"}
            If d.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                IO.File.WriteAllText(d.FileName, ConfigEdit.ExportProfile(p), New Text.UTF8Encoding(False))
                _tray.Log($"Profil '{name}' exportiert: {d.FileName}")
            Catch ex As Exception
                MessageBox.Show(Me, ex.Message, "Profil exportieren", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

    Private Sub ImportProfile()
        Using d As New OpenFileDialog With {.Filter = "Stream-Deck-Profil (*.sdprofile.json;*.json)|*.sdprofile.json;*.json|Alle Dateien|*.*"}
            If d.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim json As String
            Try
                json = IO.File.ReadAllText(d.FileName)
                ConfigEdit.ImportProfile(ConfigStore.CloneConfig(_engine.Config), json)   ' Probelauf: Fehler vor dem Übernehmen melden
            Catch ex As Exception
                MessageBox.Show(Me, $"Die Datei ist kein gültiges Profil:{vbCrLf}{ex.Message}", "Profil importieren", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End Try
            Dim imported As String = Nothing
            _engine.UpdateConfig(Sub(c) imported = ConfigEdit.ImportProfile(c, json).Name)
            _tray.Log($"Profil '{imported}' importiert")
            MessageBox.Show(Me, $"Profil '{imported}' wurde hinzugefügt. Pfade zu Programmen, Sounds und Symbolen müssen auf diesem Rechner ggf. angepasst werden.",
                            "Profil importieren", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Using
    End Sub

    Private Sub ShowProfileMenu(name As String, anchor As Control, location As Point)
        Dim m As New ContextMenuStrip()
        m.Items.Add("Umbenennen …", Nothing, Sub() RenameProfile(name))
        m.Items.Add("Automatisch umschalten bei …", Nothing, Sub() EditAutoSwitch(name))
        Dim running As New ToolStripMenuItem("Laufendes Programm hinzufügen")
        For Each procName In ForegroundProcess.GetWindowedProcessNames()
            Dim n = procName
            running.DropDownItems.Add(n, Nothing, Sub() AddAutoSwitch(name, n))
        Next
        If running.DropDownItems.Count = 0 Then running.DropDownItems.Add("(keine Programme mit Fenster gefunden)").Enabled = False
        m.Items.Add(running)
        m.Items.Add(New ToolStripSeparator())
        m.Items.Add("Profil exportieren (für anderen Rechner) …", Nothing, Sub() ExportProfile(name))
        m.Items.Add("Profil importieren …", Nothing, Sub() ImportProfile())
        m.Items.Add(New ToolStripSeparator())
        m.Items.Add("Löschen", Nothing, Sub() DeleteProfile(name)).Enabled = _engine.Config.Profiles.Count > 1
        AddHandler m.Closed, Sub() BeginInvoke(Sub() m.Dispose())
        m.Show(anchor, location)
    End Sub

    Private Sub NewProfile()
        Dim names = _engine.Config.Profiles.Select(Function(p) p.Name).ToList()
        Dim name = AskName("Name des neuen Profils:", "Neues Profil", ConfigEdit.UniqueName(names, "Profil"), names)
        If name Is Nothing Then Return
        _engine.UpdateConfig(Sub(c) ConfigEdit.AddProfile(c, name))
        _engine.SelectProfile(name)
    End Sub

    Private Sub RenameProfile(old As String)
        Dim name = AskName("Neuer Name des Profils:", "Profil umbenennen", old, _engine.Config.Profiles.Select(Function(p) p.Name), old)
        If name Is Nothing OrElse name = old Then Return
        Dim show = If(_engine.CurrentProfile.Name = old, name, Nothing)
        _engine.UpdateConfig(Sub(c) ConfigEdit.RenameProfile(c, old, name), show)
    End Sub

    Private Sub DeleteProfile(name As String)
        If _engine.Config.Profiles.Count <= 1 Then
            MessageBox.Show(Me, "Das letzte Profil kann nicht gelöscht werden.", "Profil löschen", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        If MessageBox.Show(Me, $"Profil '{name}' mit allen Seiten löschen?", "Profil löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        _engine.UpdateConfig(Sub(c) ConfigEdit.DeleteProfile(c, name))
    End Sub

    Private Sub EditAutoSwitch(profileName As String)
        Dim current = _engine.Config.FindProfile(profileName)?.AutoSwitchProcess
        Dim value = TextPrompt.Ask(Me, "Automatisch umschalten",
                                   $"Prozessname(n) ohne .exe, mehrere mit ; trennen (z. B. obs64; chrome). Ist eines dieser Programme im Vordergrund, wird '{profileName}' gewählt. Leer = aus.",
                                   If(current, ""), allowEmpty:=True)
        If value Is Nothing Then Return
        SetAutoSwitch(profileName, If(String.IsNullOrWhiteSpace(value), Nothing, value.Trim()))
    End Sub

    Private Sub AddAutoSwitch(profileName As String, processName As String)
        Dim current = _engine.Config.FindProfile(profileName)?.AutoSwitchProcess
        Dim parts = If(current, "").Split({";"c, ","c}, StringSplitOptions.RemoveEmptyEntries).Select(Function(x) x.Trim()).ToList()
        If Not parts.Contains(processName, StringComparer.OrdinalIgnoreCase) Then parts.Add(processName)
        SetAutoSwitch(profileName, String.Join("; ", parts))
    End Sub

    Private Sub SetAutoSwitch(profileName As String, value As String)
        _engine.UpdateConfig(Sub(c)
                                 Dim p = c.FindProfile(profileName)
                                 If p IsNot Nothing Then p.AutoSwitchProcess = value
                             End Sub)
    End Sub

    ' ------------------------------------------------------------------ Seiten

    Private Sub ShowPageMenu(anchor As Control)
        Dim m As New ContextMenuStrip()
        m.Items.Add("Neue Seite …", Nothing, Sub() NewPage())
        m.Items.Add("Neuer Ordner …", Nothing, Sub() NewPage(asFolder:=True))
        m.Items.Add("Seite umbenennen …", Nothing, Sub() RenamePage(_engine.CurrentPage.Name))
        m.Items.Add("Seite löschen", Nothing, Sub() DeletePage(_engine.CurrentPage.Name)).Enabled = _engine.CurrentProfile.Pages.Count > 1
        m.Items.Add(New ToolStripSeparator())
        m.Items.Add("Konfigurationsordner öffnen", Nothing, Sub() OpenConfigFolder())
        m.Items.Add("Beenden", Nothing, Sub() _tray.ExitApp())
        AddHandler m.Closed, Sub() BeginInvoke(Sub() m.Dispose())
        m.Show(anchor, New Point(anchor.Width - m.PreferredSize.Width, anchor.Height + 4))
    End Sub

    Private Sub ShowPageItemMenu(name As String, anchor As Control, location As Point)
        Dim m As New ContextMenuStrip()
        m.Items.Add("Umbenennen …", Nothing, Sub() RenamePage(name))
        Dim pg = _engine.CurrentProfile.FindPage(name)
        Dim isFolder = pg IsNot Nothing AndAlso pg.IsFolder
        Dim folderItem = m.Items.Add(If(isFolder, "In normale Seite umwandeln", "In Ordner umwandeln"), Nothing, Sub() SetFolder(name, Not isFolder))
        folderItem.Enabled = pg IsNot Nothing AndAlso pg IsNot _engine.CurrentProfile.Pages(0)
        m.Items.Add("Löschen", Nothing, Sub() DeletePage(name)).Enabled = _engine.CurrentProfile.Pages.Count > 1
        AddHandler m.Closed, Sub() BeginInvoke(Sub() m.Dispose())
        m.Show(anchor, location)
    End Sub

    ''' <summary>Ordner: per Taste öffnen (Aktion 'Seite wechseln'), feste Zurück-Taste, beim Blättern übersprungen.</summary>
    Private Sub SetFolder(name As String, folder As Boolean)
        Dim profileName = _engine.CurrentProfile.Name
        Dim lost = False
        _engine.UpdateConfig(Sub(c) lost = Not ConfigEdit.SetFolder(c, c.FindProfile(profileName), name, folder))
        If lost AndAlso folder Then
            MessageBox.Show(Me, $"Auf Taste {_engine.Config.FolderBackKey} lag eine Belegung, und es war keine Taste mehr frei – sie wurde durch 'Zurück' ersetzt.",
                            "Ordner", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
    End Sub

    Private Sub NewPage(Optional asFolder As Boolean = False)
        Dim profileName = _engine.CurrentProfile.Name
        Dim names = _engine.CurrentProfile.Pages.Select(Function(p) p.Name).ToList()
        If asFolder Then
            Dim folderName = AskName("Name des neuen Ordners:", "Neuer Ordner", ConfigEdit.UniqueName(names, "Ordner"), names)
            If folderName Is Nothing Then Return
            Dim fromPage = _engine.CurrentPage.Name
            Dim placed = -1
            _engine.UpdateConfig(Sub(c)
                                     Dim p = c.FindProfile(profileName)
                                     ConfigEdit.AddPage(p, folderName)
                                     ConfigEdit.SetFolder(c, p, folderName, True)
                                     ' Taste zum Öffnen auf die erste freie Taste der aktuellen Seite legen (verschachtelbar)
                                     Dim here = p.FindPage(fromPage)
                                     placed = Enumerable.Range(0, ConfigStore.KeyCount).Where(
                                         Function(i) here.GetKey(i) Is Nothing AndAlso Not ConfigStore.IsFixedBackKey(c, here, i)).DefaultIfEmpty(-1).First()
                                     If placed >= 0 Then here.SetKey(placed, New KeyConfig With {
                                         .Label = folderName, .Press = New ActionConfig With {.ActionType = ActionTypes.Page, .Target = folderName}})
                                 End Sub, profileName, fromPage)
            If placed >= 0 Then
                SelectKey(placed)
            Else
                MessageBox.Show(Me, $"Ordner '{folderName}' angelegt. Auf dieser Seite ist keine Taste frei – zum Öffnen einer Taste die Aktion 'Seite wechseln' mit Ziel '{folderName}' geben.",
                                "Neuer Ordner", MessageBoxButtons.OK, MessageBoxIcon.Information)
            End If
            Return
        End If
        Dim name = AskName("Name der neuen Seite:", "Neue Seite", ConfigEdit.UniqueName(names, "Seite"), names)
        If name Is Nothing Then Return
        _engine.UpdateConfig(Sub(c)
                                 Dim page = ConfigEdit.AddPage(c.FindProfile(profileName), name)
                                 ' Ohne Zurück-Taste käme man am Gerät nicht mehr von der Seite weg.
                                 page.SetKey(ConfigStore.KeyCount - 1, New KeyConfig With {
                                     .Label = "Zurück", .Press = New ActionConfig With {.ActionType = ActionTypes.Page, .Target = ActionTypes.PageBack}})
                             End Sub, profileName, name)
    End Sub

    Private Sub RenamePage(old As String)
        Dim profileName = _engine.CurrentProfile.Name
        Dim name = AskName("Neuer Name der Seite:", "Seite umbenennen", old, _engine.CurrentProfile.Pages.Select(Function(p) p.Name), old)
        If name Is Nothing OrElse name = old Then Return
        If _engine.CurrentPage.Name = old Then
            _engine.UpdateConfig(Sub(c) ConfigEdit.RenamePage(c.FindProfile(profileName), old, name), profileName, name)
        Else
            _engine.UpdateConfig(Sub(c) ConfigEdit.RenamePage(c.FindProfile(profileName), old, name))
        End If
    End Sub

    Private Sub DeletePage(name As String)
        Dim profileName = _engine.CurrentProfile.Name
        If _engine.CurrentProfile.Pages.Count <= 1 Then
            MessageBox.Show(Me, "Die letzte Seite eines Profils kann nicht gelöscht werden.", "Seite löschen", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        If MessageBox.Show(Me, $"Seite '{name}' löschen?", "Seite löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        _engine.UpdateConfig(Sub(c) ConfigEdit.DeletePage(c.FindProfile(profileName), name))
    End Sub

    ' ------------------------------------------------------------------ Datei / Einstellungen

    Private Sub OpenConfigFolder()
        Process.Start(New ProcessStartInfo(IO.Path.GetDirectoryName(_tray.ConfigPath)) With {.UseShellExecute = True})
    End Sub

    Private Sub ImportConfig()
        Using dlg As New OpenFileDialog With {.Filter = "Stream-Deck-Konfiguration (*.json)|*.json|Alle Dateien|*.*", .Title = "Konfiguration importieren"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim cfg As DeckConfig
            Try
                cfg = ConfigStore.Deserialize(IO.File.ReadAllText(dlg.FileName))
            Catch ex As Exception
                MessageBox.Show(Me, $"Die Datei ist keine gültige Konfiguration:{vbCrLf}{ex.Message}", "Importieren", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End Try
            Dim pages = cfg.Profiles.Sum(Function(p) p.Pages.Count)
            If MessageBox.Show(Me, $"{cfg.Profiles.Count} Profil(e) mit {pages} Seite(n) gefunden.{vbCrLf}Die aktuelle Konfiguration wird ersetzt (die bisherige bleibt als config.json.bak erhalten). Fortfahren?",
                               "Importieren", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
            _engine.ReplaceConfig(cfg)
            _tray.Log($"Konfiguration importiert: {dlg.FileName}")
        End Using
    End Sub

    Private Sub ExportConfig()
        Using dlg As New SaveFileDialog With {.Filter = "Stream-Deck-Konfiguration (*.json)|*.json", .FileName = "streamdeck-config.json", .Title = "Konfiguration exportieren"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                IO.File.WriteAllText(dlg.FileName, ConfigStore.Serialize(_engine.Config), New Text.UTF8Encoding(False))
                _tray.Log($"Konfiguration exportiert: {dlg.FileName}")
            Catch ex As Exception
                MessageBox.Show(Me, ex.Message, "Exportieren", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

    Private Sub ShowSettings()
        Dim autostartOn As Boolean
        Try
            autostartOn = Autostart.IsEnabled()
        Catch
        End Try
        Using dlg As New SettingsForm(_engine.Config, autostartOn, _tray.Connection.PortName, _tray.ConfigPath)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                _tray.ApplySettings(dlg.LongPressMs, dlg.ObsUrl, dlg.ObsPassword, dlg.AutostartEnabled, dlg.FolderBackKey, dlg.ShowOverlay, dlg.ShowNavigation, dlg.DoublePressMs, dlg.RunAsAdmin, dlg.VoiceMix)
            End If
        End Using
    End Sub
End Class

' ====================================================================== Taste im Raster

''' <summary>Tastenkappe: Symbol, Beschriftung, Typ; „LIVE“ beim Drücken am Gerät, Bernstein-Ring bei Auswahl.</summary>
Public Class KeyButton
    Inherits Control

    Private _key As KeyConfig
    Private _pressed As Boolean
    Private _selected As Boolean
    Private _hover As Boolean
    Private _mouseDownAt As Point?
    Private _suppressClick As Boolean

    Public ReadOnly Property KeyIndex As Integer
    Public Event SwapRequested(fromIndex As Integer, toIndex As Integer)
    ''' <summary>Aus der Aktionsleiste wurde ein Aktionstyp auf diese Taste gezogen.</summary>
    Public Event ActionDropped(keyIndex As Integer, actionType As String)

    Public Sub New(index As Integer)
        KeyIndex = index
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.UserPaint Or ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
        Cursor = Cursors.Hand
        AllowDrop = True
        TabStop = True
        AccessibleRole = AccessibleRole.PushButton
        AccessibleName = $"Taste {index}"
    End Sub

    Public Property Key As KeyConfig
        Get
            Return _key
        End Get
        Set(value As KeyConfig)
            _key = value
            AccessibleName = $"Taste {KeyIndex}: " & If(value Is Nothing, "nicht belegt", If(value.Label, ActionTypes.Describe(value.Press)))
            Invalidate()
        End Set
    End Property

    Public Property Pressed As Boolean
        Get
            Return _pressed
        End Get
        Set(value As Boolean)
            If _pressed = value Then Return
            _pressed = value
            Invalidate()
        End Set
    End Property

    Public Property Selected As Boolean
        Get
            Return _selected
        End Get
        Set(value As Boolean)
            If _selected = value Then Return
            _selected = value
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

    Protected Overrides Sub OnGotFocus(e As EventArgs)
        Invalidate()
        MyBase.OnGotFocus(e)
    End Sub

    Protected Overrides Sub OnLostFocus(e As EventArgs)
        Invalidate()
        MyBase.OnLostFocus(e)
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        If e.KeyCode = Keys.Enter OrElse e.KeyCode = Keys.Space Then
            OnClick(EventArgs.Empty)
            e.Handled = True
        End If
        MyBase.OnKeyDown(e)
    End Sub

    ' --- Ziehen zum Tauschen
    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        _suppressClick = False
        If e.Button = MouseButtons.Left Then _mouseDownAt = e.Location
        MyBase.OnMouseDown(e)
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        If _mouseDownAt.HasValue AndAlso e.Button = MouseButtons.Left Then
            Dim d = SystemInformation.DragSize
            If Math.Abs(e.X - _mouseDownAt.Value.X) > d.Width OrElse Math.Abs(e.Y - _mouseDownAt.Value.Y) > d.Height Then
                _mouseDownAt = Nothing
                _suppressClick = True
                DoDragDrop(New DataObject("StreamDeckKey", KeyIndex), DragDropEffects.Move)
            End If
        End If
        MyBase.OnMouseMove(e)
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        _mouseDownAt = Nothing
        MyBase.OnMouseUp(e)
    End Sub

    Protected Overrides Sub OnClick(e As EventArgs)
        If _suppressClick Then
            _suppressClick = False
            Return
        End If
        Focus()
        MyBase.OnClick(e)
    End Sub

    Protected Overrides Sub OnDragOver(e As DragEventArgs)
        e.Effect = If(e.Data.GetDataPresent("StreamDeckKey"), DragDropEffects.Move,
                      If(e.Data.GetDataPresent(ActionChip.DataFormat), DragDropEffects.Copy, DragDropEffects.None))
        MyBase.OnDragOver(e)
    End Sub

    Protected Overrides Sub OnDragDrop(e As DragEventArgs)
        If e.Data.GetDataPresent("StreamDeckKey") Then RaiseEvent SwapRequested(CInt(e.Data.GetData("StreamDeckKey")), KeyIndex)
        If e.Data.GetDataPresent(ActionChip.DataFormat) Then RaiseEvent ActionDropped(KeyIndex, CStr(e.Data.GetData(ActionChip.DataFormat)))
        MyBase.OnDragDrop(e)
    End Sub

    ' --- Zeichnen
    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.Clear(If(Parent?.BackColor, Theme.PlateColor))

        Dim inset = Theme.Px(Me, 5)          ' Platz für den Auswahlring
        Dim rad = Theme.Px(Me, 13)
        Dim outer As New RectangleF(inset, inset, Width - 2 * inset - 1, Height - 2 * inset - 1)
        Dim empty = _key Is Nothing

        ' Auswahl-/Fokusring
        If _selected Then
            Theme.DrawRound(g, Theme.Accent, New RectangleF(1, 1, Width - 3, Height - 3), rad + inset - 1, Theme.Px(Me, 2))
        ElseIf Focused AndAlso ShowFocusCues Then
            Theme.DrawRound(g, Theme.Muted, New RectangleF(1, 1, Width - 3, Height - 3), rad + inset - 1, Theme.Px(Me, 1.5F))
        End If

        Dim cap As RectangleF
        If empty Then
            cap = outer
            If _hover Then Theme.FillRound(g, Theme.HoverBg, cap, rad)
            Theme.DrawRound(g, Theme.DashColor, cap, rad, Theme.Px(Me, 1.5F), dashed:=True)
        ElseIf _pressed Then
            Dim shift = Theme.Px(Me, 3)
            Dim body As New RectangleF(outer.X, outer.Y + shift, outer.Width, outer.Height - shift)
            Theme.FillRound(g, Theme.KeySkirt, body, rad)
            cap = New RectangleF(body.X, body.Y, body.Width, body.Height - Theme.Px(Me, 2))
            Theme.FillRound(g, Theme.AccentDim, cap, rad)
            Theme.DrawRound(g, Theme.Accent, cap, rad)
        Else
            Theme.FillRound(g, Theme.KeySkirt, outer, rad)
            cap = New RectangleF(outer.X, outer.Y, outer.Width, outer.Height - Theme.Px(Me, 5))
            Theme.FillRound(g, If(_hover, Theme.KeyCapHover, Theme.KeyCap), cap, rad)
            Theme.DrawRound(g, Theme.KeyEdge, cap, rad)
        End If

        ' Inhalt
        Dim fore = If(empty, Theme.Muted, If(_pressed, Theme.AccentSoftText, Theme.Fg))
        Dim captionColor = If(_pressed, Color.FromArgb(232, 201, 143), Theme.Muted)
        Dim label = If(empty, "Frei", If(_key.Label, ""))
        Dim caption = If(empty, $"Taste {KeyIndex}", Theme.CaptionFor(_key.Press))
        Dim big = cap.Height > Theme.Px(Me, 110)
        Dim iconPx = cap.Height * 0.27F
        Dim labelFont = Theme.UiSemi(If(big, 10.0F, 9.0F))
        Dim capFont = Theme.Ui(If(big, 8.0F, 7.5F))
        Dim labelH = TextRenderer.MeasureText("Ag", labelFont).Height
        Dim capH = TextRenderer.MeasureText("Ag", capFont).Height
        Dim blockH = iconPx + Theme.Px(Me, 6) + labelH + If(caption.Length > 0, capH, 0)
        Dim y = cap.Y + (cap.Height - blockH) / 2

        Dim iconRect As New RectangleF(cap.X + (cap.Width - iconPx) / 2, y, iconPx, iconPx)
        Dim img = If(empty, Nothing, KeyIcons.GetImage(_key))
        If img IsNot Nothing Then
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
            g.DrawImage(img, iconRect)
        Else
            Dim pt = CSng(Math.Round(iconPx * 72.0F / DeviceDpi * 2) / 2)
            Dim glyph = If(empty, Theme.GlyphAdd, Theme.GlyphFor(_key.Press))
            TextRenderer.DrawText(g, glyph, Theme.Icons(Math.Max(8.0F, pt)), Rectangle.Round(RectangleF.Inflate(iconRect, 6, 6)), fore,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
        End If
        y += iconPx + Theme.Px(Me, 6)
        Dim textRect As New Rectangle(CInt(cap.X + 4), CInt(y), CInt(cap.Width - 8), labelH)
        TextRenderer.DrawText(g, label, labelFont, textRect, fore, TextFormatFlags.HorizontalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine)
        If caption.Length > 0 Then
            TextRenderer.DrawText(g, caption, capFont, New Rectangle(textRect.X, textRect.Bottom, textRect.Width, capH), captionColor,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine)
        End If

        ' Markierung „langer Druck belegt“
        If Not empty AndAlso _key.LongPress IsNot Nothing Then
            Dim d = Theme.Px(Me, 14)
            Dim mr As New RectangleF(cap.X + Theme.Px(Me, 8), cap.Y + Theme.Px(Me, 8), d, d)
            Using p As New Pen(Theme.Faint, Theme.Px(Me, 1.5F))
                g.DrawEllipse(p, mr)
                g.DrawLine(p, mr.X + d * 0.3F, mr.Y + d / 2, mr.Right - d * 0.3F, mr.Y + d / 2)
            End Using
        End If

        ' „LIVE“ beim Drücken am Gerät
        If _pressed Then
            Dim bf = Theme.UiBold(6.5F)
            Dim bs = TextRenderer.MeasureText("LIVE", bf)
            Dim br As New RectangleF(cap.Right - bs.Width - Theme.Px(Me, 14), cap.Y + Theme.Px(Me, 7), bs.Width + Theme.Px(Me, 6), bs.Height)
            Theme.FillRound(g, Theme.Accent, br, Theme.Px(Me, 4))
            TextRenderer.DrawText(g, "LIVE", bf, Rectangle.Round(br), Theme.AccentText, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
        End If
    End Sub
End Class

' ====================================================================== Symbole

Public Module KeyIcons
    Private ReadOnly Cache As New Dictionary(Of String, Image)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Symbol der Taste; ohne eigenes Symbol das Programmsymbol einer launch-Aktion.</summary>
    Public Function GetImage(key As KeyConfig) As Image
        If key Is Nothing Then Return Nothing
        If Not String.IsNullOrWhiteSpace(key.Icon) Then Return Load(key.Icon)
        Dim a = key.Press
        If a IsNot Nothing AndAlso String.Equals(a.ActionType, ActionTypes.Launch, StringComparison.OrdinalIgnoreCase) AndAlso Not String.IsNullOrWhiteSpace(a.Path) Then
            Return Load(a.Path)
        End If
        Return Nothing
    End Function

    ''' <summary>Lädt ein Bild (PNG/JPG/ICO/…) oder das Symbol einer EXE/Datei. Ergebnis wird zwischengespeichert.</summary>
    Public Function Load(path As String) As Image
        Dim full = Environment.ExpandEnvironmentVariables(path.Trim().Trim(""""c))
        If Not IO.Path.IsPathRooted(full) Then full = FindInPath(full)
        If full Is Nothing OrElse Not (IO.File.Exists(full) OrElse IO.Directory.Exists(full)) Then Return Nothing
        Dim stamp = If(IO.File.Exists(full), IO.File.GetLastWriteTimeUtc(full).Ticks, 0L)
        Dim cacheKey = full & "|" & stamp
        Dim img As Image = Nothing
        If Cache.TryGetValue(cacheKey, img) Then Return img
        Try
            Dim ext = IO.Path.GetExtension(full).ToLowerInvariant()
            If {".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tif", ".tiff"}.Contains(ext) Then
                Using fs = IO.File.OpenRead(full), src = Image.FromStream(fs)
                    img = New Bitmap(src)
                End Using
            ElseIf IO.File.Exists(full) Then
                Using ico = Icon.ExtractAssociatedIcon(full)
                    img = ico?.ToBitmap()
                End Using
            End If
        Catch
            img = Nothing
        End Try
        Cache(cacheKey) = img
        Return img
    End Function

    Private Function FindInPath(name As String) As String
        Dim candidates As New List(Of String) From {Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows)}
        candidates.AddRange(If(Environment.GetEnvironmentVariable("PATH"), "").Split(";"c, StringSplitOptions.RemoveEmptyEntries))
        For Each folder In candidates
            Try
                Dim p = IO.Path.Combine(folder.Trim(), name)
                If IO.File.Exists(p) Then Return p
            Catch
            End Try
        Next
        Return Nothing
    End Function
End Module
